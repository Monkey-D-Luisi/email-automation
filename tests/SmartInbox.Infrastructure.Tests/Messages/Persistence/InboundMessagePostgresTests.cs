using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartInbox.Application.Messages.Ports;
using SmartInbox.Domain.Messages;
using SmartInbox.Infrastructure.Messages.Persistence;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.Infrastructure.Tests.Messages.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class InboundMessagePostgresTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Database_enforces_the_unique_external_message_index()
    {
        var message = NewMessage();
        await using (var firstContext = fixture.CreateContext())
        {
            firstContext.InboundMessages.Add(ToRecord(message));
            await firstContext.SaveChangesAsync();
        }

        var duplicate = InboundMessage.Receive(
            message.Source, message.MailboxId, message.ExternalMessageId, message.ExternalThreadId,
            message.SenderEmail, message.SenderName, message.Subject, message.BodyText,
            message.ReceivedAt, message.ImportedAt);
        await using var secondContext = fixture.CreateContext();
        secondContext.InboundMessages.Add(ToRecord(duplicate));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => secondContext.SaveChangesAsync());

        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal(InboundMessageRecordConfiguration.IdempotencyIndexName, postgresException.ConstraintName);
    }

    [Fact]
    public async Task Independent_contexts_concurrently_persist_only_one_message()
    {
        var first = NewMessage();
        var second = InboundMessage.Receive(
            first.Source, first.MailboxId, first.ExternalMessageId, first.ExternalThreadId,
            first.SenderEmail, first.SenderName, first.Subject, first.BodyText,
            first.ReceivedAt, first.ImportedAt);
        using var provider = fixture.CreateServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var firstRepository = firstScope.ServiceProvider.GetRequiredService<IInboundMessageRepository>();
        var secondRepository = secondScope.ServiceProvider.GetRequiredService<IInboundMessageRepository>();

        var results = await Task.WhenAll(
            firstRepository.AddIfAbsentAsync(first),
            secondRepository.AddIfAbsentAsync(second));

        Assert.Single(results, result => result.Created);
        Assert.Equal(results[0].Message.Id, results[1].Message.Id);
        await using var verifyContext = fixture.CreateContext();
        var count = await verifyContext.InboundMessages.CountAsync(message =>
            message.Source == first.Source &&
            message.MailboxId == first.MailboxId &&
            message.ExternalMessageId == first.ExternalMessageId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task External_message_id_case_is_preserved_by_the_database_unique_key()
    {
        var message = NewMessage();
        var caseVariant = InboundMessage.Receive(
            message.Source,
            message.MailboxId,
            message.ExternalMessageId.ToUpperInvariant(),
            message.ExternalThreadId,
            message.SenderEmail,
            message.SenderName,
            message.Subject,
            message.BodyText,
            message.ReceivedAt,
            message.ImportedAt);
        using var provider = fixture.CreateServiceProvider();
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IInboundMessageRepository>();

        var first = await repository.AddIfAbsentAsync(message);
        var second = await repository.AddIfAbsentAsync(caseVariant);

        Assert.True(first.Created);
        Assert.True(second.Created);
        Assert.NotEqual(first.Message.Id, second.Message.Id);
    }

    private static InboundMessage NewMessage()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        return InboundMessage.Receive(
            "integration-test", $"mailbox-{suffix}", $"external-{suffix}", $"thread-{suffix}",
            "synthetic@example.test", "Synthetic Sender", "Synthetic integration message", "Synthetic body",
            now, now);
    }

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
        AttachmentsJson = "[]",
    };
}
