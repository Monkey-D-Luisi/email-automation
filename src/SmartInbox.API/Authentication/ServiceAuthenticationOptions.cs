namespace SmartInbox.API.Authentication;

public sealed class ServiceAuthenticationOptions
{
    public const string SectionName = "ServiceAuthentication";
    public string Token { get; set; } = string.Empty;
}
