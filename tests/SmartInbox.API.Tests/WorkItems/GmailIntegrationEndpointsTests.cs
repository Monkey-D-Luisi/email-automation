using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartInbox.Application.Messages.Classification.Models;
using SmartInbox.Application.Messages.Classification.Ports;
using SmartInbox.Domain.Messages;

namespace SmartInbox.API.Tests.WorkItems;

[Collection(PostgresApiCollection.Name)]
public sealed class GmailIntegrationEndpointsTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Gmail_ingestion_and_approved_draft_export_use_separate_credentials_and_persist_the_snapshot()
    {
        await fixture.ResetAsync();
        var (host, client) = fixture.CreateProcessingHost(new FakeEmailAnalysisProvider());
        using var hostLifetime = host;
        using var clientLifetime = client;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PostgresApiFixture.GmailIngestionTestToken);
        var message = new GmailMessageRequest(
            "gmail-message-001", "gmail-thread-001", "sender@example.test", "Synthetic sender",
            "Synthetic request", "Please check invoice INV-2030.", DateTimeOffset.UtcNow);
        using var received = await client.PostAsJsonAsync("/api/v1/integrations/gmail/messages", message);
        Assert.Equal(HttpStatusCode.Accepted, received.StatusCode);
        var messageId = (await received.Content.ReadFromJsonAsync<ReceivedMessage>())!.Id;
        using var replay = await client.PostAsJsonAsync("/api/v1/integrations/gmail/messages", message);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(messageId, (await replay.Content.ReadFromJsonAsync<ReceivedMessage>())!.Id);

        using var conflict = await client.PostAsJsonAsync(
            "/api/v1/integrations/gmail/messages", message with { BodyText = "Different message content." });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var sameThread = await client.PostAsJsonAsync(
            "/api/v1/integrations/gmail/messages", message with { GmailMessageId = "gmail-message-002" });
        Assert.Equal(HttpStatusCode.Accepted, sameThread.StatusCode);

        using var unsupportedMailboxImpersonation = await client.PostAsJsonAsync(
            "/api/v1/integrations/gmail/messages", new
            {
                gmailMessageId = "gmail-message-spoofed",
                gmailThreadId = "gmail-thread-001",
                senderEmail = "sender@example.test",
                subject = "Synthetic request",
                bodyText = "Synthetic body.",
                receivedAt = DateTimeOffset.UtcNow,
                source = "attacker-controlled",
                mailboxId = "some-other-mailbox",
            });
        Assert.Equal(HttpStatusCode.BadRequest, unsupportedMailboxImpersonation.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token-for-service-authentication-0123456789abcdef");
        JsonElement processed = default;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var response = await client.GetAsync($"/api/v1/messages/{messageId:D}");
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            processed = json.RootElement.Clone();
            if (processed.GetProperty("status").GetString() == "Processed")
                break;
            await Task.Delay(50);
        }
        Assert.Equal("Processed", processed.GetProperty("status").GetString());
        Assert.Equal("gmail-test", processed.GetProperty("source").GetString());
        Assert.Equal(PostgresApiFixture.GmailTestMailboxId, processed.GetProperty("mailboxId").GetString());

        client.DefaultRequestHeaders.Authorization = null;
        await LoginAsync(client);
        using var sessionResponse = await client.GetAsync("/api/v1/reviewer/session");
        using var session = JsonDocument.Parse(await sessionResponse.Content.ReadAsStringAsync());
        Assert.True(session.RootElement.GetProperty("gmailDraftExportEnabled").GetBoolean());
        Assert.Equal(PostgresApiFixture.GmailTestMailboxId, session.RootElement.GetProperty("gmailDraftExportMailboxId").GetString());

        using var queueResponse = await client.GetAsync("/api/v1/work-items?requiresReview=true&pageSize=10");
        using var queue = JsonDocument.Parse(await queueResponse.Content.ReadAsStringAsync());
        var workItem = queue.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("sourceMessageId").GetGuid() == messageId);
        var workItemId = workItem.GetProperty("id").GetGuid();

        Assert.Equal(1, await PostMutationAsync(client, $"/api/v1/work-items/{workItemId:D}/start-review", new { expectedVersion = 0 }));
        Assert.Equal(2, await PostMutationAsync(client, $"/api/v1/work-items/{workItemId:D}/draft", new
        {
            expectedVersion = 1,
            subject = "Approved response",
            body = "We received your invoice question.",
        }));
        Assert.Equal(3, await PostMutationAsync(client, $"/api/v1/work-items/{workItemId:D}/draft/approve", new
        {
            expectedVersion = 2,
            revision = 1,
        }));

        var exportsPath = $"/api/v1/work-items/{workItemId:D}/draft-exports";
        using var wrongMailbox = await client.PostAsJsonAsync(exportsPath, new
        {
            expectedVersion = 3,
            revision = 1,
            destinationMailboxId = "attacker-selected-mailbox",
            recipientEmail = "sender@example.test",
        });
        Assert.Equal(HttpStatusCode.Conflict, wrongMailbox.StatusCode);

        var request = new
        {
            expectedVersion = 3,
            revision = 1,
            destinationMailboxId = PostgresApiFixture.GmailTestMailboxId,
            recipientEmail = "sender@example.test",
        };
        using var created = await client.PostAsJsonAsync(exportsPath, request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var exportId = createdJson.RootElement.GetProperty("id").GetGuid();
        using var duplicate = await client.PostAsJsonAsync(exportsPath, request);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(exportId, (await duplicate.Content.ReadFromJsonAsync<ExportResponse>())!.Id);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PostgresApiFixture.GmailIngestionTestToken);
        using var wrongService = await client.PostAsync("/api/v1/integrations/gmail/draft-exports/claim", null);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongService.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PostgresApiFixture.GmailDraftExportTestToken);
        using var claimResponse = await client.PostAsync("/api/v1/integrations/gmail/draft-exports/claim", null);
        Assert.Equal(HttpStatusCode.OK, claimResponse.StatusCode);
        using var claimJson = JsonDocument.Parse(await claimResponse.Content.ReadAsStringAsync());
        var claim = claimJson.RootElement.GetProperty("claim");
        Assert.Equal(exportId, claim.GetProperty("exportId").GetGuid());
        Assert.Equal("Approved response", claim.GetProperty("subject").GetString());
        Assert.Equal("We received your invoice question.", claim.GetProperty("body").GetString());
        Assert.Equal("sender@example.test", claim.GetProperty("recipientEmail").GetString());
        Assert.Equal(PostgresApiFixture.GmailTestMailboxId, claim.GetProperty("destinationMailboxId").GetString());
        var attemptId = claim.GetProperty("attemptId").GetGuid();

        var callbackPath = $"/api/v1/integrations/gmail/draft-exports/{exportId:D}/complete";
        var callback = new { attemptId, gmailDraftId = "synthetic-gmail-draft-001" };
        using var completed = await client.PostAsJsonAsync(callbackPath, callback);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        using var duplicateCallback = await client.PostAsJsonAsync(callbackPath, callback);
        Assert.Equal(HttpStatusCode.OK, duplicateCallback.StatusCode);
        using var callbackJson = JsonDocument.Parse(await duplicateCallback.Content.ReadAsStringAsync());
        Assert.Equal("Duplicate", callbackJson.RootElement.GetProperty("status").GetString());

        client.DefaultRequestHeaders.Authorization = null;
        using var listResponse = await client.GetAsync(exportsPath);
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var persisted = Assert.Single(list.RootElement.GetProperty("exports").EnumerateArray());
        Assert.Equal("Succeeded", persisted.GetProperty("status").GetString());
        Assert.Equal("synthetic-gmail-draft-001", persisted.GetProperty("gmailDraftId").GetString());

        using var historyResponse = await client.GetAsync($"/api/v1/work-items/{workItemId:D}/history");
        using var history = JsonDocument.Parse(await historyResponse.Content.ReadAsStringAsync());
        Assert.Contains(history.RootElement.GetProperty("entries").EnumerateArray(),
            entry => entry.GetProperty("action").GetString() == "GmailDraftExportSucceeded");
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
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("requestToken").GetString()!;
    }

    private static async Task<int> PostMutationAsync(HttpClient client, string path, object payload)
    {
        using var response = await client.PostAsJsonAsync(path, payload);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ExpectedVersionResponse>())!.Version;
    }

    private sealed record GmailMessageRequest(
        string GmailMessageId,
        string GmailThreadId,
        string SenderEmail,
        string SenderName,
        string Subject,
        string BodyText,
        DateTimeOffset ReceivedAt);

    private sealed record ReceivedMessage(Guid Id, string Status);
    private sealed record ExportResponse(Guid Id);
    private sealed record ExpectedVersionResponse(int Version);

    private sealed class FakeEmailAnalysisProvider : IEmailAnalysisProvider
    {
        public EmailAnalysisProviderType ProviderType => EmailAnalysisProviderType.OpenAi;

        public Task<EmailAnalysisProviderResult> AnalyzeAsync(InboundMessage message, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailAnalysisProviderResult(
                new EmailAnalysisCandidate(
                    "SalesInquiry", "Normal", "The sender asks about an invoice.", "en",
                    [new EmailFactCandidate("InvoiceNumber", "INV-2030")], [], [], false, [], null),
                "synthetic-fake-model", 1, 10, null, 5, null));
    }
}
