using NSubstitute;
using SmartInbox.Application.Messages.Ports;
using SmartInbox.Application.Messages.Models;
using SmartInbox.Application.Messages.ListInboundMessages;
using SmartInbox.Application.Messages.ReceiveInboundMessage;
using SmartInbox.Domain.Messages;

namespace SmartInbox.Application.Tests.Messages;

public sealed class ReceiveInboundMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handler_uses_TimeProvider_and_returns_database_insert_result()
    {
        var repository = Substitute.For<IInboundMessageRepository>();
        InboundMessage? savedMessage = null;
        repository.AddIfAbsentAsync(Arg.Any<InboundMessage>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                savedMessage = call.Arg<InboundMessage>();
                return Task.FromResult(new AddInboundMessageResult(savedMessage, Created: true));
            });
        var handler = new ReceiveInboundMessageCommandHandler(repository, new TestTimeProvider(Now));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.Created);
        await repository.Received(1).AddIfAbsentAsync(Arg.Any<InboundMessage>(), Arg.Any<CancellationToken>());
        Assert.NotNull(savedMessage);
        Assert.Equal(Now, savedMessage.ImportedAt);
        Assert.Equal(savedMessage.Id, result.Id);
    }

    [Fact]
    public async Task Handler_returns_existing_id_for_identical_external_message()
    {
        var existing = CreateMessage("Body");
        var repository = Substitute.For<IInboundMessageRepository>();
        repository.AddIfAbsentAsync(Arg.Any<InboundMessage>(), Arg.Any<CancellationToken>())
            .Returns(new AddInboundMessageResult(existing, Created: false));
        var handler = new ReceiveInboundMessageCommandHandler(repository, new TestTimeProvider(Now));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(existing.Id, result.Id);
    }

    [Fact]
    public async Task Handler_rejects_external_id_reuse_with_different_content()
    {
        var existing = CreateMessage("Original body");
        var repository = Substitute.For<IInboundMessageRepository>();
        repository.AddIfAbsentAsync(Arg.Any<InboundMessage>(), Arg.Any<CancellationToken>())
            .Returns(new AddInboundMessageResult(existing, Created: false));
        var handler = new ReceiveInboundMessageCommandHandler(repository, new TestTimeProvider(Now));

        await Assert.ThrowsAsync<ExternalMessageConflictException>(() =>
            handler.Handle(ValidCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task Validator_rejects_invalid_ids_address_and_length_limits()
    {
        var command = ValidCommand() with
        {
            Source = "provider name",
            ExternalMessageId = new string('x', InboundMessageLimits.ExternalId + 1),
            SenderEmail = "not-an-email",
            BodyText = new string('x', InboundMessageLimits.BodyText + 1),
        };
        var validator = new ReceiveInboundMessageCommandValidator();

        var result = await validator.ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.Source));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.ExternalMessageId));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.SenderEmail));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.BodyText));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 101)]
    [InlineData(10001, 1)]
    public async Task List_validator_limits_page_number_and_size(int pageNumber, int pageSize)
    {
        var result = await new ListInboundMessagesQueryValidator()
            .ValidateAsync(new ListInboundMessagesQuery(pageNumber, pageSize));

        Assert.False(result.IsValid);
    }

    private static ReceiveInboundMessageCommand ValidCommand() => new(
        "provider", "mailbox", "external-id", "thread", "sender@example.test", "Sender", "Subject", "Body",
        Now.AddMinutes(-1), null);

    private static InboundMessage CreateMessage(string body) => InboundMessage.Receive(
        "provider", "mailbox", "external-id", "thread", "sender@example.test", "Sender", "Subject", body,
        Now.AddMinutes(-1), Now);

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
