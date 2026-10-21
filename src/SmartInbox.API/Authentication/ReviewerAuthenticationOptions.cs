namespace SmartInbox.API.Authentication;

public sealed class ReviewerAuthenticationOptions
{
    public const string SectionName = "ReviewAuthentication";
    public const string SchemeName = "ReviewerCookie";
    public const string PolicyName = "HumanReviewer";
    public string ReviewerId { get; set; } = "local-reviewer";
    public string DisplayName { get; set; } = "Local reviewer";
    public string Password { get; set; } = string.Empty;
}
