using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartInbox.Application.Common.DependencyInjection;
using SmartInbox.Application.Messages.Ports;
using SmartInbox.Application.Messages.Models;
using SmartInbox.Domain.Messages;
using SmartInbox.Domain.WorkItems;
using SmartInbox.Infrastructure.Persistence;
using SmartInbox.Infrastructure.WorkItems.Persistence;

namespace SmartInbox.Infrastructure.Messages.Persistence;

[IoC(typeof(IInboundMessageRepository), IoCLifetime.Scoped)]
internal sealed class InboundMessageRepository(AppDbContext dbContext) : IInboundMessageRepository
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task<AddInboundMessageResult> AddIfAbsentAsync(
        InboundMessage message,
        CancellationToken cancellationToken = default)
    {
        var record = ToRecord(message);
        dbContext.InboundMessages.Add(record);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new AddInboundMessageResult(message, Created: true);
        }
        catch (DbUpdateException exception) when (IsIdempotencyViolation(exception))
        {
            dbContext.Entry(record).State = EntityState.Detached;
            var existing = await dbContext.InboundMessages
                .AsNoTracking()
                .Include(candidate => candidate.Classification)
                .SingleOrDefaultAsync(candidate =>
                    candidate.Source == message.Source &&
                    candidate.MailboxId == message.MailboxId &&
                    candidate.ExternalMessageId == message.ExternalMessageId,
                    cancellationToken);

            if (existing is null)
                throw;

            return new AddInboundMessageResult(ToDomain(existing), Created: false);
        }
    }

    public async Task<InboundMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.InboundMessages
            .AsNoTracking()
            .Include(message => message.Classification)
            .SingleOrDefaultAsync(message => message.Id == id, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    public async Task<InboundMessagePage> ListAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.InboundMessages.AsNoTracking().Include(message => message.Classification);
        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(message => message.ImportedAt)
            .ThenBy(message => message.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new InboundMessagePage(items.Select(ToDomain).ToArray(), pageNumber, pageSize, totalCount);
    }

    public async Task<InboundMessageProcessingClaim?> ClaimNextForProcessingAsync(
        Guid leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        if (leaseOwner == Guid.Empty)
            throw new ArgumentException("A processing lease owner is required.", nameof(leaseOwner));
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE inbound_messages
            SET "Status" = 'Failed',
                "FailureKind" = 'RetryLimitReached',
                "FailureMessage" = 'Processing lease expired after the retry limit.',
                "ProcessingRetryable" = FALSE,
                "NextAttemptAt" = NULL,
                "ProcessingLeaseOwner" = NULL,
                "ProcessingLeaseExpiresAt" = NULL
            WHERE "Status" = 'Processing'
              AND "ProcessingLeaseExpiresAt" <= {now}
              AND "ProcessingAttemptCount" >= {maxAttempts}
            """, cancellationToken);

        var record = await dbContext.InboundMessages
            .FromSqlInterpolated($"""
                SELECT * FROM inbound_messages
                WHERE "Status" = 'Received'
                   OR ("Status" = 'Failed' AND "ProcessingRetryable" = TRUE AND "NextAttemptAt" <= {now})
                   OR ("Status" = 'Processing' AND "ProcessingLeaseExpiresAt" <= {now}
                       AND "ProcessingAttemptCount" < {maxAttempts})
                ORDER BY "ImportedAt", "Id"
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (record is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        dbContext.InboundMessages.Attach(record);
        record.Status = InboundMessageStatus.Processing.ToString();
        record.ProcessingAttemptCount++;
        record.FailureKind = null;
        record.FailureMessage = null;
        record.ProcessingRetryable = false;
        record.NextAttemptAt = null;
        record.ProcessingLeaseOwner = leaseOwner;
        record.ProcessingLeaseExpiresAt = now.Add(leaseDuration);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new InboundMessageProcessingClaim(ToDomain(record), leaseOwner);
    }

    public async Task<bool> CompleteProcessingAsync(
        Guid id,
        Guid leaseOwner,
        EmailClassification classification,
        EmailAnalysisMetrics metrics,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(metrics);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var record = await FindOwnedProcessingRecordAsync(id, leaseOwner, cancellationToken);
        if (record is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        dbContext.InboundMessages.Attach(record);
        ToDomain(record).CompleteProcessing(classification, metrics);
        record.Status = InboundMessageStatus.Processed.ToString();
        record.ProcessingLeaseOwner = null;
        record.ProcessingLeaseExpiresAt = null;
        record.FailureKind = null;
        record.FailureMessage = null;
        record.ProcessingRetryable = false;
        record.NextAttemptAt = null;
        WriteMetrics(record, metrics);
        record.Classification = ToRecord(id, classification);
        dbContext.EmailClassifications.Add(record.Classification);
        dbContext.WorkItems.Add(WorkItemPersistenceMapper.ToRecord(
            WorkItem.Create(id, classification, classification.ClassifiedAt)));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> FailProcessingAsync(
        Guid id,
        Guid leaseOwner,
        ProcessingFailureKind failureKind,
        string safeMessage,
        bool retryable,
        DateTimeOffset? nextAttemptAt,
        EmailAnalysisMetrics? metrics,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var record = await FindOwnedProcessingRecordAsync(id, leaseOwner, cancellationToken);
        if (record is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        dbContext.InboundMessages.Attach(record);
        ToDomain(record).FailProcessing(failureKind, safeMessage, retryable, nextAttemptAt, metrics);
        record.Status = InboundMessageStatus.Failed.ToString();
        record.ProcessingLeaseOwner = null;
        record.ProcessingLeaseExpiresAt = null;
        record.FailureKind = failureKind.ToString();
        record.FailureMessage = safeMessage.Trim();
        record.ProcessingRetryable = retryable;
        record.NextAttemptAt = nextAttemptAt?.ToUniversalTime();
        if (metrics is not null)
            WriteMetrics(record, metrics);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<InboundMessageRecord?> FindOwnedProcessingRecordAsync(
        Guid id,
        Guid leaseOwner,
        CancellationToken cancellationToken) =>
        await dbContext.InboundMessages
            .FromSqlInterpolated($"""
                SELECT * FROM inbound_messages
                WHERE "Id" = {id} AND "Status" = 'Processing' AND "ProcessingLeaseOwner" = {leaseOwner}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);

    private static bool IsIdempotencyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: InboundMessageRecordConfiguration.IdempotencyIndexName,
        };

    private static InboundMessageRecord ToRecord(InboundMessage message) => new()
    {
        Id = message.Id,
        Source = message.Source,
        MailboxId = message.MailboxId,
        ExternalMessageId = message.ExternalMessageId,
        ExternalThreadId = message.ExternalThreadId,
        SenderEmail = message.SenderEmail,
        SenderName = message.SenderName,
        Subject = message.Subject,
        BodyText = message.BodyText,
        ReceivedAt = message.ReceivedAt,
        ImportedAt = message.ImportedAt,
        Status = message.Status.ToString(),
        ContentFingerprint = message.ContentFingerprint,
        AttachmentsJson = JsonSerializer.Serialize(message.Attachments, JsonOptions),
        ProcessingAttemptCount = message.ProcessingAttemptCount,
        FailureKind = message.FailureKind?.ToString(),
        FailureMessage = message.FailureMessage,
        ProcessingRetryable = message.ProcessingRetryable,
        NextAttemptAt = message.NextAttemptAt,
        LastProviderId = message.AnalysisMetrics?.ProviderId,
        LastModel = message.AnalysisMetrics?.Model,
        LastDurationMilliseconds = message.AnalysisMetrics?.DurationMilliseconds,
        LastInputTokens = message.AnalysisMetrics?.InputTokens,
        LastCachedInputTokens = message.AnalysisMetrics?.CachedInputTokens,
        LastOutputTokens = message.AnalysisMetrics?.OutputTokens,
        LastEstimatedCostUsd = message.AnalysisMetrics?.EstimatedCostUsd,
        Classification = message.Classification is null ? null : ToRecord(message.Id, message.Classification),
    };

    private static EmailClassificationRecord ToRecord(Guid messageId, EmailClassification classification) => new()
    {
        MessageId = messageId,
        Category = classification.Category.ToString(),
        Priority = classification.Priority.ToString(),
        Summary = classification.Summary,
        Language = classification.Language,
        FactsJson = JsonSerializer.Serialize(classification.Facts, JsonOptions),
        MissingInformationJson = JsonSerializer.Serialize(classification.MissingInformation, JsonOptions),
        WarningsJson = JsonSerializer.Serialize(classification.Warnings, JsonOptions),
        UncertaintyReasonsJson = JsonSerializer.Serialize(classification.UncertaintyReasons, JsonOptions),
        ReviewReasonsJson = JsonSerializer.Serialize(classification.ReviewReasons, JsonOptions),
        RequiresReview = classification.RequiresReview,
        ClassifiedAt = classification.ClassifiedAt,
    };

    private static InboundMessage ToDomain(InboundMessageRecord record)
    {
        if (!Enum.TryParse<InboundMessageStatus>(record.Status, false, out var status) || !Enum.IsDefined(status))
            throw new InvalidOperationException("The persisted inbound message has an unknown status.");
        var attachments = JsonSerializer.Deserialize<AttachmentMetadata[]>(record.AttachmentsJson, JsonOptions)
            ?? throw new InvalidOperationException("The persisted attachment metadata is invalid.");
        var failureKind = ParseOptionalEnum<ProcessingFailureKind>(record.FailureKind, "failure kind");
        var metrics = ReadMetrics(record);
        var classification = record.Classification is null ? null : ToDomain(record.Classification);

        return InboundMessage.Restore(
            record.Id,
            record.Source,
            record.MailboxId,
            record.ExternalMessageId,
            record.ExternalThreadId,
            record.SenderEmail,
            record.SenderName,
            record.Subject,
            record.BodyText,
            record.ReceivedAt,
            record.ImportedAt,
            status,
            record.ContentFingerprint,
            attachments,
            classification,
            metrics,
            record.ProcessingAttemptCount,
            failureKind,
            record.FailureMessage,
            record.ProcessingRetryable,
            record.NextAttemptAt);
    }

    private static EmailClassification ToDomain(EmailClassificationRecord record)
    {
        var category = ParseEnum<EmailCategory>(record.Category, "classification category");
        var priority = ParseEnum<EmailPriority>(record.Priority, "classification priority");
        var facts = JsonSerializer.Deserialize<EmailFact[]>(record.FactsJson, JsonOptions)
            ?? throw new InvalidOperationException("The persisted classification facts are invalid.");
        var missingInformation = DeserializeStrings(record.MissingInformationJson);
        var warnings = DeserializeStrings(record.WarningsJson);
        var uncertaintyReasons = DeserializeStrings(record.UncertaintyReasonsJson);
        var reviewReasons = JsonSerializer.Deserialize<EmailReviewReason[]>(record.ReviewReasonsJson, JsonOptions)
            ?? throw new InvalidOperationException("The persisted classification review reasons are invalid.");
        var classification = EmailClassification.Create(
            category, priority, record.Summary, record.Language, facts, missingInformation, warnings,
            uncertaintyReasons, reviewReasons, record.ClassifiedAt);
        if (classification.RequiresReview != record.RequiresReview)
            throw new InvalidOperationException("The persisted classification review state is inconsistent.");
        return classification;
    }

    private static EmailAnalysisMetrics? ReadMetrics(InboundMessageRecord record)
    {
        if (record.LastProviderId is null && record.LastModel is null && record.LastDurationMilliseconds is null &&
            record.LastInputTokens is null && record.LastCachedInputTokens is null && record.LastOutputTokens is null &&
            record.LastEstimatedCostUsd is null)
            return null;
        if (record.LastProviderId is null || record.LastModel is null || record.LastDurationMilliseconds is null)
            throw new InvalidOperationException("The persisted processing metrics are incomplete.");
        return new EmailAnalysisMetrics(
            record.LastProviderId,
            record.LastModel,
            record.LastDurationMilliseconds.Value,
            record.LastInputTokens,
            record.LastCachedInputTokens,
            record.LastOutputTokens,
            record.LastEstimatedCostUsd);
    }

    private static void WriteMetrics(InboundMessageRecord record, EmailAnalysisMetrics? metrics)
    {
        record.LastProviderId = metrics?.ProviderId;
        record.LastModel = metrics?.Model;
        record.LastDurationMilliseconds = metrics?.DurationMilliseconds;
        record.LastInputTokens = metrics?.InputTokens;
        record.LastCachedInputTokens = metrics?.CachedInputTokens;
        record.LastOutputTokens = metrics?.OutputTokens;
        record.LastEstimatedCostUsd = metrics?.EstimatedCostUsd;
    }

    private static string[] DeserializeStrings(string json) =>
        JsonSerializer.Deserialize<string[]>(json, JsonOptions)
        ?? throw new InvalidOperationException("The persisted classification text values are invalid.");

    private static T ParseEnum<T>(string value, string description) where T : struct, Enum =>
        Enum.TryParse<T>(value, false, out var result) && Enum.IsDefined(result)
            ? result
            : throw new InvalidOperationException($"The persisted {description} is invalid.");

    private static T? ParseOptionalEnum<T>(string? value, string description) where T : struct, Enum =>
        value is null ? null : ParseEnum<T>(value, description);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
