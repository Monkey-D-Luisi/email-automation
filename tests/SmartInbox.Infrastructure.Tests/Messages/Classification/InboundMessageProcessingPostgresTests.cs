using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SmartInbox.Application.Messages.Classification;
using SmartInbox.Application.Messages.Ports;
using SmartInbox.Application.Messages.Classification.Models;
using SmartInbox.Application.Messages.Classification.Ports;
using SmartInbox.Application.Messages.Models;
using SmartInbox.Domain.Messages;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.Infrastructure.Tests.Messages.Classification;

[Collection(PostgresCollection.Name)]
public sealed class InboundMessageProcessingPostgresTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Completed_classification_is_persisted_once_and_duplicate_ingestion_is_not_claimable()
    {
        await fixture.ResetAsync();
        var message = await AddMessageAsync();
        using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IInboundMessageRepository>();
        var owner = Guid.NewGuid();
        var claim = await repository.ClaimNextForProcessingAsync(owner, Now, TimeSpan.FromMinutes(1), 3);
        Assert.NotNull(claim);

        var classification = Classification("first result");
        Assert.True(await repository.CompleteProcessingAsync(message.Id, owner, classification, Metrics()));

        using var duplicateServices = fixture.CreateServiceProvider();
        using var duplicateScope = duplicateServices.CreateScope();
        var duplicateInput = InboundMessage.Receive(
            message.Source, message.MailboxId, message.ExternalMessageId, message.ExternalThreadId,
            message.SenderEmail, message.SenderName, message.Subject, message.BodyText,
            message.ReceivedAt, message.ImportedAt, message.Attachments);
        var duplicate = await duplicateScope.ServiceProvider.GetRequiredService<IInboundMessageRepository>().AddIfAbsentAsync(duplicateInput);
        Assert.False(duplicate.Created);
        Assert.Equal(InboundMessageStatus.Processed, duplicate.Message.Status);
        Assert.Equal("first result", duplicate.Message.Classification?.Summary);
        Assert.Null(await repository.ClaimNextForProcessingAsync(Guid.NewGuid(), Now.AddMinutes(1), TimeSpan.FromMinutes(1), 3));
        Assert.False(await repository.CompleteProcessingAsync(message.Id, owner, Classification("stale result"), Metrics()));

        await using var context = fixture.CreateContext();
        Assert.Equal(1, await context.EmailClassifications.CountAsync(row => row.MessageId == message.Id));
    }

    [Fact]
    public async Task Two_independent_workers_cannot_claim_the_same_message()
    {
        await fixture.ResetAsync();
        await AddMessageAsync();
        using var firstServices = fixture.CreateServiceProvider();
        using var secondServices = fixture.CreateServiceProvider();

        var claims = await Task.WhenAll(
            ClaimAsync(firstServices, Guid.NewGuid(), Now),
            ClaimAsync(secondServices, Guid.NewGuid(), Now));

        Assert.Single(claims, claim => claim is not null);
    }

    [Fact]
    public async Task Expired_lease_is_reclaimed_and_stale_worker_cannot_overwrite_new_result()
    {
        await fixture.ResetAsync();
        var message = await AddMessageAsync();
        using var firstServices = fixture.CreateServiceProvider();
        using var secondServices = fixture.CreateServiceProvider();
        var oldOwner = Guid.NewGuid();
        var oldClaim = await ClaimAsync(firstServices, oldOwner, Now);
        Assert.NotNull(oldClaim);

        var newOwner = Guid.NewGuid();
        var newClaim = await ClaimAsync(secondServices, newOwner, Now.AddSeconds(31));
        Assert.NotNull(newClaim);
        Assert.Equal(2, newClaim.Message.ProcessingAttemptCount);

        using var oldScope = firstServices.CreateScope();
        using var newScope = secondServices.CreateScope();
        var oldRepository = oldScope.ServiceProvider.GetRequiredService<IInboundMessageRepository>();
        var newRepository = newScope.ServiceProvider.GetRequiredService<IInboundMessageRepository>();
        Assert.False(await oldRepository.CompleteProcessingAsync(message.Id, oldOwner, Classification("stale result"), Metrics()));
        Assert.True(await newRepository.CompleteProcessingAsync(message.Id, newOwner, Classification("current result"), Metrics()));

        using var readServices = fixture.CreateServiceProvider();
        using var readScope = readServices.CreateScope();
        var persisted = await readScope.ServiceProvider.GetRequiredService<IInboundMessageRepository>().GetByIdAsync(message.Id);
        Assert.Equal("current result", persisted?.Classification?.Summary);
    }

    [Fact]
    public async Task Transient_failure_is_retried_then_classification_is_persisted()
    {
        await fixture.ResetAsync();
        var message = await AddMessageAsync();
        var time = new MutableTimeProvider(Now);
        var analyzer = new RecordingAnalyzer((_, attempt, _) => attempt == 1
            ? throw new EmailAnalysisException(
                "synthetic provider detail",
                ProcessingFailureKind.TransientConnectivity,
                retryable: true,
                new EmailAnalysisMetrics("fake", "synthetic-test-model", 7, 10, null, 2, null))
            : Task.FromResult(AnalysisResult("retry succeeded")));
        var options = new EmailProcessingOptions { MaxAttempts = 3, RetryDelaySeconds = 10, LeaseDurationSeconds = 30 };

        Assert.True(await ProcessOneAsync(analyzer, options, time));
        var failed = await GetMessageAsync(message.Id);
        Assert.Equal(InboundMessageStatus.Failed, failed.Status);
        Assert.True(failed.ProcessingRetryable);
        Assert.DoesNotContain("synthetic provider detail", failed.FailureMessage);
        Assert.Equal(7, failed.AnalysisMetrics?.DurationMilliseconds);

        time.Advance(TimeSpan.FromSeconds(10));
        Assert.True(await ProcessOneAsync(analyzer, options, time));
        var processed = await GetMessageAsync(message.Id);
        Assert.Equal(InboundMessageStatus.Processed, processed.Status);
        Assert.Equal(2, processed.ProcessingAttemptCount);
        Assert.Equal("retry succeeded", processed.Classification?.Summary);
    }

    [Fact]
    public async Task Failed_completion_after_provider_success_can_repeat_the_provider_call_after_lease_expiry()
    {
        await fixture.ResetAsync();
        var message = await AddMessageAsync();
        var time = new MutableTimeProvider(Now);
        var analyzer = new RecordingAnalyzer((_, _, _) => Task.FromResult(AnalysisResult("recovered result")));
        var options = new EmailProcessingOptions { MaxAttempts = 3, LeaseDurationSeconds = 30 };

        using (var services = fixture.CreateServiceProvider())
        using (var scope = services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IInboundMessageRepository>();
            var failingRepository = Substitute.For<IInboundMessageRepository>();
            failingRepository.ClaimNextForProcessingAsync(
                    Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call => repository.ClaimNextForProcessingAsync(
                    call.Arg<Guid>(), call.Arg<DateTimeOffset>(), call.Arg<TimeSpan>(), call.Arg<int>(), call.Arg<CancellationToken>()));
            failingRepository.CompleteProcessingAsync(
                    Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<EmailClassification>(), Arg.Any<EmailAnalysisMetrics>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromException<bool>(new DbUpdateException("Injected completion persistence failure.")));

            var processor = new InboundMessageProcessor(failingRepository, analyzer, options, time);
            await Assert.ThrowsAsync<DbUpdateException>(() => processor.ProcessNextAsync(CancellationToken.None));
        }

        Assert.Equal(1, analyzer.CallCount);
        var leased = await GetMessageAsync(message.Id);
        Assert.Equal(InboundMessageStatus.Processing, leased.Status);
        Assert.Equal(1, leased.ProcessingAttemptCount);

        time.Advance(TimeSpan.FromSeconds(31));
        Assert.True(await ProcessOneAsync(analyzer, options, time));

        var processed = await GetMessageAsync(message.Id);
        Assert.Equal(InboundMessageStatus.Processed, processed.Status);
        Assert.Equal(2, processed.ProcessingAttemptCount);
        Assert.Equal(2, analyzer.CallCount);
        Assert.Equal("recovered result", processed.Classification?.Summary);
    }

    [Fact]
    public async Task Transient_failures_stop_at_the_configured_attempt_limit()
    {
        await fixture.ResetAsync();
        var message = await AddMessageAsync();
        var time = new MutableTimeProvider(Now);
        var analyzer = new RecordingAnalyzer((_, _, _) =>
            throw new EmailAnalysisException("synthetic provider detail", ProcessingFailureKind.RateLimited, retryable: true));
        var options = new EmailProcessingOptions { MaxAttempts = 2, RetryDelaySeconds = 1, LeaseDurationSeconds = 30 };

        Assert.True(await ProcessOneAsync(analyzer, options, time));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await ProcessOneAsync(analyzer, options, time));

        var failed = await GetMessageAsync(message.Id);
        Assert.Equal(InboundMessageStatus.Failed, failed.Status);
        Assert.Equal(2, failed.ProcessingAttemptCount);
        Assert.Equal(ProcessingFailureKind.RateLimited, failed.FailureKind);
        Assert.False(failed.ProcessingRetryable);
        Assert.Null(failed.NextAttemptAt);
        Assert.Equal(2, analyzer.CallCount);
        Assert.False(await ProcessOneAsync(analyzer, options, time));
    }

    private async Task<InboundMessage> AddMessageAsync()
    {
        var message = InboundMessage.Receive(
            "synthetic", "test-mailbox", Guid.NewGuid().ToString("N"), null, null, null,
            "Synthetic email", "Please classify this synthetic email.", Now, Now);
        using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IInboundMessageRepository>().AddIfAbsentAsync(message);
        Assert.True(result.Created);
        return message;
    }

    private async Task<InboundMessage> GetMessageAsync(Guid id)
    {
        using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IInboundMessageRepository>().GetByIdAsync(id)
            ?? throw new InvalidOperationException("The test message was not persisted.");
    }

    private async Task<bool> ProcessOneAsync(IEmailAnalyzer analyzer, EmailProcessingOptions options, MutableTimeProvider time)
    {
        using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var processor = new InboundMessageProcessor(
            scope.ServiceProvider.GetRequiredService<IInboundMessageRepository>(), analyzer, options, time);
        return await processor.ProcessNextAsync(CancellationToken.None);
    }

    private static async Task<InboundMessageProcessingClaim?> ClaimAsync(
        ServiceProvider services,
        Guid owner,
        DateTimeOffset now)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IInboundMessageRepository>()
            .ClaimNextForProcessingAsync(owner, now, TimeSpan.FromSeconds(30), 3);
    }

    private static EmailClassification Classification(string summary) => EmailClassification.Create(
        EmailCategory.GeneralInquiry,
        EmailPriority.Normal,
        summary,
        "en",
        [], [], [], [], [], Now);

    private static EmailAnalysisMetrics Metrics() => new("fake", "synthetic-test-model", 1, 10, null, 5, null);

    private static EmailAnalyzerResult AnalysisResult(string summary) => new(Classification(summary), Metrics());

    private sealed class RecordingAnalyzer(
        Func<InboundMessage, int, CancellationToken, Task<EmailAnalyzerResult>> analyze) : IEmailAnalyzer
    {
        private int callCount;
        public int CallCount => Volatile.Read(ref callCount);

        public Task<EmailAnalyzerResult> AnalyzeAsync(InboundMessage message, CancellationToken cancellationToken = default) =>
            analyze(message, Interlocked.Increment(ref callCount), cancellationToken);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan amount) => current = current.Add(amount);
    }
}
