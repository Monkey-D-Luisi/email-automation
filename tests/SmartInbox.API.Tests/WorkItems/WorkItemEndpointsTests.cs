using System.Net;
using System.Net.Http.Json;
using SmartInbox.Application.Messages.Classification.Models;
using SmartInbox.Application.Messages.Classification.Ports;
using SmartInbox.Domain.Messages;

namespace SmartInbox.API.Tests.WorkItems;

[Collection(PostgresApiCollection.Name)]
public sealed class WorkItemEndpointsTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Reviewer_can_correct_verify_draft_approve_resolve_and_inspect_audit()
    {
        await fixture.ResetAsync();
        var (host, client) = fixture.CreateProcessingHost(new FakeEmailAnalysisProvider());
        using var hostLifetime = host;
        using var clientLifetime = client;
        var message = new SyntheticMessage(
            "demo",
            "synthetic-mailbox",
            $"phase3-{Guid.NewGuid():N}",
            "sender@example.test",
            "Synthetic Sender",
            "Invoice question",
            "Please check invoice INV-2030.",
            DateTimeOffset.UtcNow);
        using var ingestion = await client.PostAsJsonAsync("/api/v1/messages", message);
        Assert.Equal(HttpStatusCode.Accepted, ingestion.StatusCode);
        var ingestionBody = await ingestion.Content.ReadFromJsonAsync<ReceiveResponse>();
        Assert.NotNull(ingestionBody);

        MessageResponse? processed = null;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            processed = await client.GetFromJsonAsync<MessageResponse>($"/api/v1/messages/{ingestionBody.Id:D}");
            if (processed?.Status == "Processed")
                break;
            await Task.Delay(50);
        }
        Assert.Equal("Processed", processed?.Status);
        Assert.True(processed!.Classification!.RequiresReview);

        client.DefaultRequestHeaders.Authorization = null;
        await LoginAsync(client);
        var page = await client.GetFromJsonAsync<WorkItemListResponse>("/api/v1/work-items?requiresReview=true&pageSize=10");
        var listed = Assert.Single(page!.Items, item => item.SourceMessageId == ingestionBody.Id);
        Assert.Equal("PendingReview", listed.Status);

        using var detailBefore = await client.GetAsync($"/api/v1/work-items/{listed.Id:D}");
        detailBefore.EnsureSuccessStatusCode();
        using var beforeJson = System.Text.Json.JsonDocument.Parse(await detailBefore.Content.ReadAsStringAsync());
        var factId = beforeJson.RootElement.GetProperty("facts")[0].GetProperty("id").GetGuid();
        Assert.Equal("SalesInquiry", beforeJson.RootElement.GetProperty("originalClassification").GetProperty("category").GetString());

        Assert.Equal(1, await PostMutationAsync(client,
            $"/api/v1/work-items/{listed.Id:D}/start-review", new { expectedVersion = 0 }));
        using var staleReview = await PatchMutationResponseAsync(client, $"/api/v1/work-items/{listed.Id:D}/review",
            new { expectedVersion = 0, category = "SupportRequest", priority = "Normal", notes = (string?)null, missingInformation = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Conflict, staleReview.StatusCode);
        Assert.Equal(2, await PatchMutationAsync(client,
            $"/api/v1/work-items/{listed.Id:D}/review",
            new { expectedVersion = 1, category = "SupportRequest", priority = "High", notes = "Synthetic correction", missingInformation = Array.Empty<string>() }));
        Assert.Equal(3, await PatchMutationAsync(client,
            $"/api/v1/work-items/{listed.Id:D}/facts/{factId:D}", new { expectedVersion = 2, value = "INV-2031" }));
        Assert.Equal(4, await PostMutationAsync(client,
            $"/api/v1/work-items/{listed.Id:D}/facts/{factId:D}/verify", new { expectedVersion = 3 }));
        Assert.Equal(5, await PostMutationAsync(client,
            $"/api/v1/work-items/{listed.Id:D}/draft", new { expectedVersion = 4, subject = "Invoice question", body = "We received your message." }));
        Assert.Equal(6, await PostMutationAsync(client,
            $"/api/v1/work-items/{listed.Id:D}/draft", new { expectedVersion = 5, subject = "Invoice question", body = "Could you confirm the invoice number?" }));
        Assert.Equal(7, await PostMutationAsync(client,
            $"/api/v1/work-items/{listed.Id:D}/draft/approve", new { expectedVersion = 6, revision = 2 }));
        using var repeatedApproval = await client.PostAsJsonAsync(
            $"/api/v1/work-items/{listed.Id:D}/draft/approve", new { expectedVersion = 6, revision = 2 });
        Assert.Equal(HttpStatusCode.Conflict, repeatedApproval.StatusCode);

        using var detailAfter = await client.GetAsync($"/api/v1/work-items/{listed.Id:D}");
        using var afterJson = System.Text.Json.JsonDocument.Parse(await detailAfter.Content.ReadAsStringAsync());
        var root = afterJson.RootElement;
        Assert.Equal("SalesInquiry", root.GetProperty("originalClassification").GetProperty("category").GetString());
        Assert.True(root.GetProperty("originalClassification").GetProperty("requiresReview").GetBoolean());
        Assert.Equal("SupportRequest", root.GetProperty("effectiveCategory").GetString());
        Assert.Equal("INV-2031", root.GetProperty("facts")[0].GetProperty("value").GetString());
        Assert.Equal("Verified", root.GetProperty("facts")[0].GetProperty("status").GetString());
        Assert.Equal("Approved", root.GetProperty("draft").GetProperty("revisions")[1].GetProperty("status").GetString());

        Assert.Equal(8, await PostMutationAsync(client,
            $"/api/v1/work-items/{listed.Id:D}/resolve", new { expectedVersion = 7 }));
        var history = await client.GetFromJsonAsync<HistoryResponse>($"/api/v1/work-items/{listed.Id:D}/history");
        Assert.Contains(history!.Entries, entry => entry.Action == "FactVerified");
        Assert.Contains(history.Entries, entry => entry.Action == "DraftApproved");
        Assert.Single(history.Entries, entry => entry.Action == "DraftApproved");
        Assert.Contains(history.Entries, entry => entry.Action == "Resolved");
    }

    private static async Task LoginAsync(HttpClient client)
    {
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await GetAntiforgeryTokenAsync(client));
        using var login = await client.PostAsJsonAsync(
            "/api/v1/reviewer/login", new { password = "synthetic-reviewer-password-for-tests-0123456789" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await GetAntiforgeryTokenAsync(client));
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/reviewer/antiforgery");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AntiforgeryToken>())!.RequestToken;
    }

    private static async Task<int> PostMutationAsync(HttpClient client, string path, object payload)
    {
        using var response = await client.PostAsJsonAsync(path, payload);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ExpectedVersionResponse>())!.Version;
    }

    private static async Task<int> PatchMutationAsync(HttpClient client, string path, object payload)
    {
        using var response = await PatchMutationResponseAsync(client, path, payload);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ExpectedVersionResponse>();
        return result!.Version;
    }

    private static Task<HttpResponseMessage> PatchMutationResponseAsync(HttpClient client, string path, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, path) { Content = JsonContent.Create(payload) };
        return client.SendAsync(request);
    }

    private sealed record SyntheticMessage(
        string Source,
        string MailboxId,
        string ExternalMessageId,
        string SenderEmail,
        string SenderName,
        string Subject,
        string BodyText,
        DateTimeOffset ReceivedAt);

    private sealed record ReceiveResponse(Guid Id, string Status);

    private sealed record MessageResponse(string Status, ClassificationResponse? Classification);

    private sealed record ClassificationResponse(bool RequiresReview);

    private sealed record WorkItemListResponse(IReadOnlyList<WorkItemListEntry> Items);

    private sealed record WorkItemListEntry(Guid Id, Guid SourceMessageId, string Status);

    private sealed record ExpectedVersionResponse(int Version);

    private sealed record AntiforgeryToken(string RequestToken);

    private sealed record HistoryResponse(IReadOnlyList<HistoryEntry> Entries);

    private sealed record HistoryEntry(string Action);

    private sealed class FakeEmailAnalysisProvider : IEmailAnalysisProvider
    {
        public EmailAnalysisProviderType ProviderType => EmailAnalysisProviderType.OpenAi;

        public Task<EmailAnalysisProviderResult> AnalyzeAsync(InboundMessage message, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailAnalysisProviderResult(
                new EmailAnalysisCandidate(
                    "SalesInquiry", "Normal", "The sender asks about invoice INV-2030.", "en",
                    [new EmailFactCandidate("InvoiceNumber", "INV-2030")], [], [], false, [], null),
                "synthetic-fake-model", 1, 10, null, 5, null));
    }
}
