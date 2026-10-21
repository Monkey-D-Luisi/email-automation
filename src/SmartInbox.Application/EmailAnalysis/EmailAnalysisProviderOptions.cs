namespace SmartInbox.Application.EmailAnalysis;

public sealed class EmailAnalysisProviderOptions
{
    public const string SectionName = "EmailAnalysis";
    public string Provider { get; set; } = "openai";
}
