using System.Net;
using System.Net.Http.Json;

namespace SmartInbox.API.Tests.Authentication;

[Collection(PostgresApiCollection.Name)]
public sealed class ReviewerAuthenticationTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Service_token_cannot_authenticate_as_a_reviewer_and_cookie_mutations_require_csrf()
    {
        using var serviceOnly = await fixture.Client.GetAsync("/api/v1/work-items");
        Assert.Equal(HttpStatusCode.Unauthorized, serviceOnly.StatusCode);

        using var client = fixture.CreateClient();
        var anonymousToken = await GetAntiforgeryTokenAsync(client);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", anonymousToken);
        using var login = await client.PostAsJsonAsync(
            "/api/v1/reviewer/login",
            new { password = "synthetic-reviewer-password-for-tests-0123456789" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var session = await client.GetFromJsonAsync<ReviewerSession>("/api/v1/reviewer/session");
        Assert.Equal("reviewer-1", session!.ReviewerId);
        Assert.Equal("Synthetic reviewer", session.DisplayName);
        Assert.False(session.AiDraftingEnabled);
        Assert.False(session.GmailDraftExportEnabled);
        Assert.False(session.OutlookDraftExportEnabled);

        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await GetAntiforgeryTokenAsync(client));
        using var logout = await client.PostAsync("/api/v1/reviewer/logout", null);
        Assert.True(logout.StatusCode == HttpStatusCode.NoContent, await logout.Content.ReadAsStringAsync());
        using var expired = await client.GetAsync("/api/v1/reviewer/session");
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    public async Task Invalid_login_password_is_rejected()
    {
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await GetAntiforgeryTokenAsync(client));
        using var response = await client.PostAsJsonAsync("/api/v1/reviewer/login", new { password = "wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cookie_mutations_without_an_antiforgery_token_are_rejected()
    {
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await GetAntiforgeryTokenAsync(client));
        using var login = await client.PostAsJsonAsync(
            "/api/v1/reviewer/login", new { password = "synthetic-reviewer-password-for-tests-0123456789" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");

        using var logout = await client.PostAsync("/api/v1/reviewer/logout", null);
        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/reviewer/antiforgery");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AntiforgeryToken>();
        return result!.RequestToken;
    }

    private sealed record AntiforgeryToken(string RequestToken);

    private sealed record ReviewerSession(
        string ReviewerId,
        string DisplayName,
        bool AiDraftingEnabled,
        bool GmailDraftExportEnabled,
        bool OutlookDraftExportEnabled);
}
