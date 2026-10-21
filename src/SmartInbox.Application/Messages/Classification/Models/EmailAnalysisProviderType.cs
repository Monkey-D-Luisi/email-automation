namespace SmartInbox.Application.Messages.Classification.Models;

public enum EmailAnalysisProviderType
{
    OpenAi = 1,
}

public static class EmailAnalysisProviderTypeCodec
{
    public static string ToId(EmailAnalysisProviderType providerType) => providerType switch
    {
        EmailAnalysisProviderType.OpenAi => "openai",
        _ => throw new ArgumentOutOfRangeException(nameof(providerType)),
    };

    public static bool TryParse(string? providerId, out EmailAnalysisProviderType providerType)
    {
        providerType = default;
        if (!string.Equals(providerId?.Trim(), "openai", StringComparison.OrdinalIgnoreCase))
            return false;

        providerType = EmailAnalysisProviderType.OpenAi;
        return true;
    }

    public static EmailAnalysisProviderType Parse(string? providerId) =>
        TryParse(providerId, out var providerType)
            ? providerType
            : throw new ArgumentException("The configured email analysis provider is not supported.", nameof(providerId));
}
