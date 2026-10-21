namespace SmartInbox.Application.Messages.Classification.Models;

public enum EmailAnalysisProviderType
{
    OpenAi = 1,
    Fake = 2,
}

public static class EmailAnalysisProviderTypeCodec
{
    public static string ToId(EmailAnalysisProviderType providerType) => providerType switch
    {
        EmailAnalysisProviderType.OpenAi => "openai",
        EmailAnalysisProviderType.Fake => "fake",
        _ => throw new ArgumentOutOfRangeException(nameof(providerType)),
    };

    public static bool TryParse(string? providerId, out EmailAnalysisProviderType providerType)
    {
        providerType = default;
        providerType = providerId?.Trim().ToLowerInvariant() switch
        {
            "openai" => EmailAnalysisProviderType.OpenAi,
            "fake" => EmailAnalysisProviderType.Fake,
            _ => default,
        };
        return providerType != default;
    }

    public static EmailAnalysisProviderType Parse(string? providerId) =>
        TryParse(providerId, out var providerType)
            ? providerType
            : throw new ArgumentException("The configured email analysis provider is not supported.", nameof(providerId));
}
