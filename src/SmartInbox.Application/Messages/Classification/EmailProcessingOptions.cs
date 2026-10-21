namespace SmartInbox.Application.Messages.Classification;

public sealed class EmailProcessingOptions
{
    public const string SectionName = "EmailProcessing";
    public bool Enabled { get; set; }
    public int MaxConcurrency { get; set; } = 1;
    public int MaxAttempts { get; set; } = 3;
    public int PollIntervalSeconds { get; set; } = 2;
    public int RetryDelaySeconds { get; set; } = 30;
    public int LeaseDurationSeconds { get; set; } = 120;
    public int RequestTimeoutSeconds { get; set; } = 90;
    public int MaxInputCharacters { get; set; } = 40_000;
}
