namespace SmartInbox.Infrastructure.Messages.Persistence;

internal sealed class EmailClassificationRecord
{
    public Guid MessageId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string FactsJson { get; set; } = "[]";
    public string MissingInformationJson { get; set; } = "[]";
    public string WarningsJson { get; set; } = "[]";
    public string UncertaintyReasonsJson { get; set; } = "[]";
    public string ReviewReasonsJson { get; set; } = "[]";
    public bool RequiresReview { get; set; }
    public DateTimeOffset ClassifiedAt { get; set; }
    public InboundMessageRecord Message { get; set; } = null!;
}
