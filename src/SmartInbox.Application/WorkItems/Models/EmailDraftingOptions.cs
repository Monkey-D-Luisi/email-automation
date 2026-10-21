namespace SmartInbox.Application.WorkItems.Models;

public sealed class EmailDraftingOptions
{
    public const string SectionName = "EmailDrafting";
    public bool Enabled { get; set; }
    public int MaxConcurrency { get; set; } = 1;
    public int RequestTimeoutSeconds { get; set; } = 60;
    public int MaxInputCharacters { get; set; } = 12_000;
    public int MaxOutputTokens { get; set; } = 800;
}
