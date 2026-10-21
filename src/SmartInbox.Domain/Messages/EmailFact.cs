namespace SmartInbox.Domain.Messages;

public sealed record EmailFact(EmailFactType Type, string Value);
