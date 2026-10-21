using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using SmartInbox.Application.Common.Interfaces;
using SmartInbox.Application.Common.Models;
using SmartInbox.Domain.Messages;

namespace SmartInbox.API.Tests.Messages;

[Collection(PostgresApiCollection.Name)]
public sealed class MessageEndpointsTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Receive_duplicate_get_and_list_use_stable_v1_contracts()
    {
        await fixture.ResetAsync();
        var suffix = $"api-flow-{Guid.NewGuid():N}";
        var request = NewRequest(suffix);
        var first = await fixture.Client.PostAsJsonAsync("/api/v1/messages", request);
        Assert.True(first.StatusCode == HttpStatusCode.Accepted, await first.Content.ReadAsStringAsync());
        var firstBody = await first.Content.ReadFromJsonAsync<ReceiveResponse>();
        var duplicate = await fixture.Client.PostAsJsonAsync("/api/v1/messages", request);
        Assert.True(duplicate.StatusCode == HttpStatusCode.OK, await duplicate.Content.ReadAsStringAsync());
        var duplicateBody = await duplicate.Content.ReadFromJsonAsync<ReceiveResponse>();

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.NotNull(firstBody);
        Assert.NotNull(duplicateBody);
        Assert.Equal(firstBody.Id, duplicateBody.Id);

        var get = await fixture.Client.GetFromJsonAsync<MessageResponse>($"/api/v1/messages/{firstBody.Id:D}");
        Assert.NotNull(get);
        Assert.Equal($"Case-Sensitive-{suffix}", get.ExternalMessageId);
        Assert.Equal("Received", get.Status);
        Assert.Equal("application/pdf", Assert.Single(get.Attachments).ContentType);
        Assert.Null(get.Classification);
        Assert.Equal(0, get.Processing.AttemptCount);

        var list = await fixture.Client.GetFromJsonAsync<MessageListResponse>("/api/v1/messages?pageNumber=1&pageSize=1");
        Assert.NotNull(list);
        Assert.Equal(1, list.PageNumber);
        Assert.Equal(1, list.PageSize);
        Assert.InRange(list.Items.Count, 0, 1);
        Assert.True(list.TotalCount >= 1);
        Assert.Equal(HttpStatusCode.NotFound,
            (await fixture.Client.GetAsync($"/api/v1/messages/{Guid.NewGuid():D}")).StatusCode);
    }

    [Fact]
    public async Task Reused_external_id_with_changed_content_returns_conflict_without_overwriting()
    {
        await fixture.ResetAsync();
        var request = NewRequest($"api-conflict-{Guid.NewGuid():N}");
        var created = await fixture.Client.PostAsJsonAsync("/api/v1/messages", request);
        Assert.True(created.StatusCode == HttpStatusCode.Accepted, await created.Content.ReadAsStringAsync());
        var createdBody = await created.Content.ReadFromJsonAsync<ReceiveResponse>();
        var changed = request with { BodyText = "Different synthetic body" };

        var conflict = await fixture.Client.PostAsJsonAsync("/api/v1/messages", changed);
        var stored = await fixture.Client.GetFromJsonAsync<MessageResponse>(
            $"/api/v1/messages/{createdBody!.Id:D}");

        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(request.BodyText, stored!.BodyText);
    }

    [Fact]
    public async Task Simultaneous_posts_persist_one_message_and_return_one_internal_id()
    {
        await fixture.ResetAsync();
        var request = NewRequest($"api-concurrent-{Guid.NewGuid():N}") with
        {
            ExternalMessageId = $"external-{Guid.NewGuid():N}",
        };
        var responses = await Task.WhenAll(
            fixture.Client.PostAsJsonAsync("/api/v1/messages", request),
            fixture.Client.PostAsJsonAsync("/api/v1/messages", request));
        var bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<ReceiveResponse>()));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Accepted));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.NotNull(bodies[0]);
        Assert.NotNull(bodies[1]);
        Assert.Equal(bodies[0]!.Id, bodies[1]!.Id);
    }

    [Fact]
    public async Task Message_routes_require_valid_service_token_and_health_is_public()
    {
        await fixture.ResetAsync();
        using var anonymousClient = fixture.CreateClient();
        using var missing = await anonymousClient.GetAsync("/api/v1/messages");
        using var invalidClient = fixture.CreateClient();
        invalidClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "wrong-token");
        using var invalid = await invalidClient.GetAsync("/api/v1/messages");
        using var health = await anonymousClient.GetAsync("/health");

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Request_validation_and_total_body_limit_return_problem_details()
    {
        await fixture.ResetAsync();
        var tooLongField = NewRequest("api-validation") with
        {
            BodyText = new string('x', 40_001),
        };
        using var validation = await fixture.Client.PostAsJsonAsync("/api/v1/messages", tooLongField);
        using var validationJson = System.Text.Json.JsonDocument.Parse(await validation.Content.ReadAsStringAsync());

        var oversizedJson = "{\"source\":\"demo\",\"bodyText\":\"" + new string('x', 65_536) + "\"}";
        using var oversized = await fixture.Client.PostAsync(
            "/api/v1/messages",
            new StringContent(oversizedJson, System.Text.Encoding.UTF8, "application/json"));
        var oversizedProblem = await oversized.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, validation.StatusCode);
        Assert.True(validationJson.RootElement.GetProperty("errors").TryGetProperty("BodyText", out _));
        Assert.Equal((HttpStatusCode)413, oversized.StatusCode);
        Assert.Equal(413, oversizedProblem!.Status);
    }

    [Fact]
    public async Task Request_rejects_unrecognized_attachment_bytes_field()
    {
        await fixture.ResetAsync();
        var json = """
            {"source":"demo","mailboxId":"box","externalMessageId":"id","subject":"x","bodyText":"y","receivedAt":"2026-10-21T10:00:00Z","attachments":[{"externalAttachmentId":"a","name":"x.pdf","contentType":"application/pdf","sizeBytes":12,"bytes":"c2VjcmV0"}]}
            """;
        using var response = await fixture.Client.PostAsync(
            "/api/v1/messages",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Background_worker_classifies_persisted_message_and_duplicate_does_not_repeat_analysis()
    {
        await fixture.ResetAsync();
        var provider = new FakeEmailAnalysisProvider();
        var (host, client) = fixture.CreateProcessingHost(provider);
        using var hostLifetime = host;
        using var clientLifetime = client;
        var request = NewRequest($"api-analysis-{Guid.NewGuid():N}") with
        {
            Subject = "Quote request",
            BodyText = "Please quote 20 annual licenses.",
        };
        using var created = await client.PostAsJsonAsync("/api/v1/messages", request);
        Assert.True(created.StatusCode == HttpStatusCode.Accepted, await created.Content.ReadAsStringAsync());
        var received = await created.Content.ReadFromJsonAsync<ReceiveResponse>();
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        Assert.NotNull(received);

        MessageResponse? processed = null;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            processed = await client.GetFromJsonAsync<MessageResponse>($"/api/v1/messages/{received.Id:D}");
            if (processed?.Status == "Processed")
                break;
            await Task.Delay(50);
        }

        Assert.NotNull(processed);
        Assert.Equal("Processed", processed.Status);
        Assert.Equal("SalesInquiry", processed.Classification?.Category);
        Assert.Equal("Normal", processed.Classification?.Priority);
        Assert.False(processed.Classification?.RequiresReview);
        Assert.Equal(1, processed.Processing.AttemptCount);
        Assert.Equal("openai", processed.Processing.Metrics?.Provider);

        using var duplicate = await client.PostAsJsonAsync("/api/v1/messages", request);

        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(1, provider.CallCount);
    }

    private static ReceiveRequest NewRequest(string suffix) => new(
        "Demo.Provider",
        "synthetic-mailbox",
        $"Case-Sensitive-{suffix}",
        $"thread-{suffix}",
        "sender@example.test",
        "Synthetic Sender",
        "Synthetic message",
        "Synthetic body",
        new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero),
        [new AttachmentRequest("attachment-1", "synthetic.pdf", "Application/PDF", 123)]);

    private sealed record ReceiveRequest(
        string Source,
        string MailboxId,
        string ExternalMessageId,
        string ExternalThreadId,
        string SenderEmail,
        string SenderName,
        string Subject,
        string BodyText,
        DateTimeOffset ReceivedAt,
        IReadOnlyList<AttachmentRequest> Attachments);

    private sealed record AttachmentRequest(
        string ExternalAttachmentId,
        string Name,
        string ContentType,
        long SizeBytes);

    private sealed record ReceiveResponse(Guid Id, string Status);

    private sealed record MessageResponse(
        Guid Id,
        string ExternalMessageId,
        string BodyText,
        string Status,
        IReadOnlyList<AttachmentResponse> Attachments,
        ClassificationResponse? Classification,
        ProcessingResponse Processing);

    private sealed record AttachmentResponse(string ExternalAttachmentId, string Name, string ContentType, long SizeBytes);

    private sealed record ClassificationResponse(string Category, string Priority, bool RequiresReview);

    private sealed record ProcessingResponse(int AttemptCount, MetricsResponse? Metrics);

    private sealed record MetricsResponse(string Provider, string Model);

    private sealed record MessageListResponse(
        IReadOnlyList<MessageResponse> Items,
        int PageNumber,
        int PageSize,
        long TotalCount);

    private sealed class FakeEmailAnalysisProvider : IEmailAnalysisProvider
    {
        private int callCount;

        public EmailAnalysisProviderType ProviderType => EmailAnalysisProviderType.OpenAi;
        public int CallCount => Volatile.Read(ref callCount);

        public Task<EmailAnalysisProviderResult> AnalyzeAsync(
            InboundMessage message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref callCount);
            var result = new EmailAnalysisProviderResult(
                new EmailAnalysisCandidate(
                    "SalesInquiry", "Normal", "The sender requests a license quote.", "en",
                    [new EmailFactCandidate("ProductOrService", "20 annual licenses")], [], [], false, [], null),
                "synthetic-fake-model", 1, 20, null, 10, null);
            return Task.FromResult(result);
        }
    }
}
