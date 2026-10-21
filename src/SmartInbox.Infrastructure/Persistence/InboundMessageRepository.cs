using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartInbox.Application.Common.DependencyInjection;
using SmartInbox.Application.Common.Interfaces;
using SmartInbox.Domain.Messages;

namespace SmartInbox.Infrastructure.Persistence;

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
            .SingleOrDefaultAsync(message => message.Id == id, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    public async Task<InboundMessagePage> ListAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.InboundMessages.AsNoTracking();
        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(message => message.ImportedAt)
            .ThenBy(message => message.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new InboundMessagePage(items.Select(ToDomain).ToArray(), pageNumber, pageSize, totalCount);
    }

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
    };

    private static InboundMessage ToDomain(InboundMessageRecord record)
    {
        if (!Enum.TryParse<InboundMessageStatus>(record.Status, ignoreCase: false, out var status) || !Enum.IsDefined(status))
            throw new InvalidOperationException("The persisted inbound message has an unknown status.");

        var attachments = JsonSerializer.Deserialize<AttachmentMetadata[]>(record.AttachmentsJson, JsonOptions)
            ?? throw new InvalidOperationException("The persisted attachment metadata is invalid.");

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
            attachments);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
