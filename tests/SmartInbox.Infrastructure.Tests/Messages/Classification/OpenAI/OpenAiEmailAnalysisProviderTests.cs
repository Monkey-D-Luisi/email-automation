#pragma warning disable OPENAI001

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenAI.Responses;
using SmartInbox.Application.Messages.Classification;
using SmartInbox.Application.Messages.Classification.Models;
using SmartInbox.Application.Messages.Classification.Ports;
using SmartInbox.Domain.Messages;
using SmartInbox.Infrastructure.Messages.Classification.OpenAI;

namespace SmartInbox.Infrastructure.Tests.Messages.Classification.OpenAI;

public sealed class OpenAiEmailAnalysisProviderTests
{
    private const string ValidResponse = """
        {"category":"SalesInquiry","suggestedPriority":"Normal","summary":"A team requests a quote.","language":"en","facts":[{"type":"ProductOrService","value":"20 synthetic units"}],"missingInformation":[],"warnings":[],"isAmbiguous":false,"uncertaintyReasons":[],"priorityEvidence":null}
        """;

    [Fact]
    public async Task Analyze_maps_model_usage_and_configured_pricing_without_calling_live_openai()
    {
        var client = new FakeResponsesClient((_, _) => Task.FromResult(new OpenAiResponsesResult(
            ValidResponse, "gpt-6-luna", "Completed", 1_000, 200, 100)));
        const string privateEmailText = "synthetic-private-email-body";
        var provider = CreateProvider(client, new OpenAiModelPricing
        {
            InputCostPerMillionTokensUsd = 0.10m,
            CachedInputCostPerMillionTokensUsd = 0.01m,
            OutputCostPerMillionTokensUsd = 0.50m,
        });

        var result = await provider.AnalyzeAsync(Message(privateEmailText));

        Assert.Equal("gpt-6-luna", result.Model);
        Assert.Equal(1_000, result.InputTokens);
        Assert.Equal(200, result.CachedInputTokens);
        Assert.Equal(100, result.OutputTokens);
        Assert.Equal(0.000132m, result.EstimatedCostUsd);
        Assert.Contains("OpenAI email analysis completed", string.Join('\n', provider.Logger.Messages));
        Assert.DoesNotContain(privateEmailText, string.Join('\n', provider.Logger.Messages));
    }

    [Fact]
    public async Task Analyze_leaves_cost_unknown_when_pricing_is_not_configured()
    {
        var client = new FakeResponsesClient((_, _) => Task.FromResult(new OpenAiResponsesResult(
            ValidResponse, "gpt-6-luna", "Completed", 100, 0, 20)));

        var result = await CreateProvider(client).AnalyzeAsync(Message());

        Assert.Null(result.EstimatedCostUsd);
    }

    [Fact]
    public async Task Analyze_classifies_transient_connectivity_and_permanent_authentication_errors_without_logging_provider_text()
    {
        const string sensitiveProviderText = "synthetic-secret-response-body";
        var connectionProvider = CreateProvider(new FakeResponsesClient((_, _) =>
            Task.FromException<OpenAiResponsesResult>(new HttpRequestException(sensitiveProviderText))));
        var connectionError = await Assert.ThrowsAsync<EmailAnalysisException>(() => connectionProvider.AnalyzeAsync(Message()));

        var response = Substitute.For<PipelineResponse>();
        response.Status.Returns(401);
        var authenticationProvider = CreateProvider(new FakeResponsesClient((_, _) =>
            Task.FromException<OpenAiResponsesResult>(new ClientResultException(sensitiveProviderText, response, null))));
        var authenticationError = await Assert.ThrowsAsync<EmailAnalysisException>(() => authenticationProvider.AnalyzeAsync(Message()));

        Assert.Equal(ProcessingFailureKind.TransientConnectivity, connectionError.FailureKind);
        Assert.True(connectionError.Retryable);
        Assert.Equal("openai", connectionError.Metrics?.ProviderId);
        Assert.Equal("gpt-6-luna", connectionError.Metrics?.Model);
        Assert.Equal(ProcessingFailureKind.Authentication, authenticationError.FailureKind);
        Assert.False(authenticationError.Retryable);
        Assert.DoesNotContain(sensitiveProviderText, string.Join('\n', connectionProvider.Logger.Messages));
        Assert.DoesNotContain(sensitiveProviderText, string.Join('\n', authenticationProvider.Logger.Messages));
    }

    [Fact]
    public async Task Analyze_maps_rate_limit_as_retryable_and_invalid_output_as_permanent()
    {
        var response = Substitute.For<PipelineResponse>();
        response.Status.Returns(429);
        var rateLimitProvider = CreateProvider(new FakeResponsesClient((_, _) =>
            Task.FromException<OpenAiResponsesResult>(new ClientResultException("rate limited", response, null))));
        var rateLimit = await Assert.ThrowsAsync<EmailAnalysisException>(() => rateLimitProvider.AnalyzeAsync(Message()));
        var invalidProvider = CreateProvider(new FakeResponsesClient((_, _) => Task.FromResult(
            new OpenAiResponsesResult("{invalid", "gpt-6-luna", "Completed", 30, 0, 10))));
        var invalid = await Assert.ThrowsAsync<EmailAnalysisException>(() => invalidProvider.AnalyzeAsync(Message()));

        Assert.Equal(ProcessingFailureKind.RateLimited, rateLimit.FailureKind);
        Assert.True(rateLimit.Retryable);
        Assert.Equal(ProcessingFailureKind.InvalidStructuredOutput, invalid.FailureKind);
        Assert.False(invalid.Retryable);
        Assert.NotNull(invalid.Metrics);
    }

    [Fact]
    public async Task Analyze_maps_provider_timeout_to_retryable_timeout_without_logging_email_content()
    {
        const string privateEmailText = "synthetic-private-email-body";
        var client = new FakeResponsesClient(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new OpenAiResponsesResult("unreachable", null, null, null, null, null);
        });
        var fixture = CreateProvider(client, timeoutSeconds: 1);

        var exception = await Assert.ThrowsAsync<EmailAnalysisException>(() => fixture.AnalyzeAsync(Message(privateEmailText)));

        Assert.Equal(ProcessingFailureKind.Timeout, exception.FailureKind);
        Assert.True(exception.Retryable);
        Assert.NotNull(exception.Metrics);
        Assert.DoesNotContain(privateEmailText, string.Join('\n', fixture.Logger.Messages));
    }

    [Fact]
    public async Task Analyze_maps_unsupported_model_response_to_a_permanent_failure()
    {
        var response = Substitute.For<PipelineResponse>();
        response.Status.Returns(404);
        var provider = CreateProvider(new FakeResponsesClient((_, _) =>
            Task.FromException<OpenAiResponsesResult>(new ClientResultException("synthetic model not found", response, null))));

        var exception = await Assert.ThrowsAsync<EmailAnalysisException>(() => provider.AnalyzeAsync(Message()));

        Assert.Equal(ProcessingFailureKind.UnsupportedProviderOrModel, exception.FailureKind);
        Assert.False(exception.Retryable);
        Assert.Equal("gpt-6-luna", exception.Metrics?.Model);
    }

    [Fact]
    public async Task Analyze_propagates_caller_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new FakeResponsesClient(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Unreachable");
        });
        var task = CreateProvider(client).AnalyzeAsync(Message(), cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    private static ProviderFixture CreateProvider(
        IOpenAiResponsesClient client,
        OpenAiModelPricing? pricing = null,
        int timeoutSeconds = 5)
    {
        var logger = new RecordingLogger<OpenAiEmailAnalysisProvider>();
        var options = new OpenAiEmailAnalysisOptions();
        if (pricing is not null)
            options.ModelPricing[options.Model] = pricing;
        var provider = new OpenAiEmailAnalysisProvider(
            client,
            Options.Create(options),
            Options.Create(new EmailProcessingOptions { RequestTimeoutSeconds = timeoutSeconds }),
            logger);
        return new ProviderFixture(provider, logger);
    }

    private static InboundMessage Message(string? body = null) => InboundMessage.Receive(
        "test", "mailbox", Guid.NewGuid().ToString("N"), null, null, null,
        "Synthetic sales request", body ?? "Please quote 20 synthetic units.",
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class FakeResponsesClient(
        Func<CreateResponseOptions, CancellationToken, Task<OpenAiResponsesResult>> create) : IOpenAiResponsesClient
    {
        public Task<OpenAiResponsesResult> CreateResponseAsync(CreateResponseOptions options, CancellationToken cancellationToken) =>
            create(options, cancellationToken);
    }

    private sealed record ProviderFixture(
        OpenAiEmailAnalysisProvider Provider,
        RecordingLogger<OpenAiEmailAnalysisProvider> Logger)
    {
        public Task<SmartInbox.Application.Messages.Classification.Models.EmailAnalysisProviderResult> AnalyzeAsync(
            InboundMessage message,
            CancellationToken cancellationToken = default) => Provider.AnalyzeAsync(message, cancellationToken);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
