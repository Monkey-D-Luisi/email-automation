namespace SmartInbox.Infrastructure.Messages.Classification.OpenAI;

internal sealed class OpenAiEmailAnalysisOptions
{
    public const string SectionName = "OpenAI";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-6-luna";
    public int MaxOutputTokenCount { get; set; } = 1_200;
    public Dictionary<string, OpenAiModelPricing> ModelPricing { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class OpenAiModelPricing
{
    public decimal? InputCostPerMillionTokensUsd { get; set; }
    public decimal? CachedInputCostPerMillionTokensUsd { get; set; }
    public decimal? OutputCostPerMillionTokensUsd { get; set; }
}
