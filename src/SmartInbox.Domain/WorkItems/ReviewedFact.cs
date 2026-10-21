using SmartInbox.Domain.Messages;

namespace SmartInbox.Domain.WorkItems;

public sealed class ReviewedFact
{
    public Guid Id { get; private set; }
    public EmailFactType Type { get; private set; }
    public string OriginalValue { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public ReviewedFactStatus Status { get; private set; }
    public DateTimeOffset? ModifiedAt { get; private set; }
    public string? ModifiedBy { get; private set; }

    private ReviewedFact() { }

    internal static ReviewedFact Create(EmailFact fact) => new()
    {
        Id = Guid.NewGuid(),
        Type = fact.Type,
        OriginalValue = fact.Value,
        Value = fact.Value,
        Status = ReviewedFactStatus.Unverified,
    };

    public static ReviewedFact Restore(
        Guid id,
        EmailFactType type,
        string originalValue,
        string value,
        ReviewedFactStatus status,
        DateTimeOffset? modifiedAt,
        string? modifiedBy)
    {
        if (id == Guid.Empty || !Enum.IsDefined(type) || !Enum.IsDefined(status) ||
            string.IsNullOrWhiteSpace(originalValue) || originalValue.Length > 300 ||
            string.IsNullOrWhiteSpace(value) || value.Length > 300 ||
            (modifiedAt is null) != (modifiedBy is null))
            throw new ArgumentException("The persisted reviewed fact is invalid.");
        return new ReviewedFact
        {
            Id = id,
            Type = type,
            OriginalValue = originalValue,
            Value = value,
            Status = status,
            ModifiedAt = modifiedAt?.ToUniversalTime(),
            ModifiedBy = modifiedBy,
        };
    }

    internal bool Verify(string reviewerId, DateTimeOffset now)
    {
        if (Status != ReviewedFactStatus.Unverified)
            return false;
        Status = ReviewedFactStatus.Verified;
        ModifiedAt = now.ToUniversalTime();
        ModifiedBy = reviewerId;
        return true;
    }

    internal bool Correct(string value, string reviewerId, DateTimeOffset now)
    {
        ValidateValue(value);
        if (Value == value && Status == ReviewedFactStatus.Unverified)
            return false;
        Value = value;
        Status = ReviewedFactStatus.Unverified;
        ModifiedAt = now.ToUniversalTime();
        ModifiedBy = reviewerId;
        return true;
    }

    internal bool Reject(string reviewerId, DateTimeOffset now)
    {
        if (Status != ReviewedFactStatus.Unverified)
            return false;
        Status = ReviewedFactStatus.Rejected;
        ModifiedAt = now.ToUniversalTime();
        ModifiedBy = reviewerId;
        return true;
    }

    private static void ValidateValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 300)
            throw new ArgumentException("The reviewed fact value is invalid.", nameof(value));
    }
}
