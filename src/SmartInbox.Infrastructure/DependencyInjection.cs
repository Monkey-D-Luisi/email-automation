#pragma warning disable OPENAI001

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenAI.Responses;
using OpenAI;
using System.ClientModel;
using System.ClientModel.Primitives;
using SmartInbox.Application.Common.Models;
using SmartInbox.Application.EmailAnalysis;
using SmartInbox.Infrastructure.AI;
using SmartInbox.Infrastructure.Processing;
using SmartInbox.Application;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddAttributedServices(
            typeof(Application.DependencyInjection).Assembly,
            typeof(DependencyInjection).Assembly);
        ConfigureEmailProcessing(services, configuration);
        return services;
    }

    private static void ConfigureEmailProcessing(IServiceCollection services, IConfiguration configuration)
    {
        var processing = configuration.GetSection(EmailProcessingOptions.SectionName).Get<EmailProcessingOptions>() ?? new();
        services.AddSingleton(processing);
        services.AddOptions<EmailProcessingOptions>()
            .Bind(configuration.GetSection(EmailProcessingOptions.SectionName))
            .Validate(options => options.MaxConcurrency is >= 1 and <= 4, "EmailProcessing:MaxConcurrency must be between 1 and 4.")
            .Validate(options => options.MaxAttempts is >= 1 and <= 10, "EmailProcessing:MaxAttempts must be between 1 and 10.")
            .Validate(options => options.PollIntervalSeconds is >= 1 and <= 60, "EmailProcessing:PollIntervalSeconds must be between 1 and 60.")
            .Validate(options => options.RetryDelaySeconds is >= 1 and <= 3_600, "EmailProcessing:RetryDelaySeconds must be between 1 and 3600.")
            .Validate(options => options.RequestTimeoutSeconds is >= 1 and <= 300, "EmailProcessing:RequestTimeoutSeconds must be between 1 and 300.")
            .Validate(options => options.LeaseDurationSeconds > options.RequestTimeoutSeconds + 5 && options.LeaseDurationSeconds <= 3_600,
                "EmailProcessing:LeaseDurationSeconds must exceed the request timeout by at least five seconds and be at most 3600.")
            .Validate(options => options.MaxInputCharacters is >= 1 and <= 40_000,
                "EmailProcessing:MaxInputCharacters must be between 1 and 40000.")
            .ValidateOnStart();

        services.AddOptions<EmailAnalysisProviderOptions>()
            .Bind(configuration.GetSection(EmailAnalysisProviderOptions.SectionName))
            .Validate(options => !processing.Enabled || EmailAnalysisProviderTypeCodec.TryParse(options.Provider, out _),
                "EmailAnalysis:Provider must be a supported provider ID when processing is enabled.")
            .ValidateOnStart();

        var providerType = EmailAnalysisProviderType.OpenAi;
        if (processing.Enabled)
        {
            if (!EmailAnalysisProviderTypeCodec.TryParse(configuration[$"{EmailAnalysisProviderOptions.SectionName}:Provider"], out providerType))
                throw new InvalidOperationException("EmailAnalysis:Provider must be a supported provider ID when processing is enabled.");
        }

        services.AddSingleton(typeof(EmailAnalysisProviderType), providerType);
        var openAiSelectedAndEnabled = processing.Enabled && providerType == EmailAnalysisProviderType.OpenAi;
        var openAiOptions = services.AddOptions<OpenAiEmailAnalysisOptions>()
            .Bind(configuration.GetSection(OpenAiEmailAnalysisOptions.SectionName));
        if (openAiSelectedAndEnabled)
        {
            openAiOptions
                .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), "OpenAI:ApiKey is required when email processing is enabled.")
                .Validate(options => !string.IsNullOrWhiteSpace(options.Model) &&
                                     options.Model.Length <= 128 && options.Model.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'),
                    "OpenAI:Model must be a valid model ID.")
                .Validate(options => options.MaxOutputTokenCount is >= 1 and <= 128_000,
                    "OpenAI:MaxOutputTokenCount must be between 1 and 128000.")
                .Validate(options => options.ModelPricing.Values.All(pricing =>
                        (pricing.InputCostPerMillionTokensUsd is null or >= 0) &&
                        (pricing.CachedInputCostPerMillionTokensUsd is null or >= 0) &&
                        (pricing.OutputCostPerMillionTokensUsd is null or >= 0) &&
                        (pricing.InputCostPerMillionTokensUsd is null) == (pricing.OutputCostPerMillionTokensUsd is null)),
                    "OpenAI:ModelPricing values must be non-negative and include both input and output rates.")
                .ValidateOnStart();
        }

        services.AddSingleton(provider =>
        {
            var openAi = provider.GetRequiredService<IOptions<OpenAiEmailAnalysisOptions>>().Value;
            var processingOptions = provider.GetRequiredService<IOptions<EmailProcessingOptions>>().Value;
            return new ResponsesClient(
                new ApiKeyCredential(openAiSelectedAndEnabled ? openAi.ApiKey : "disabled"),
                new ResponsesClientOptions
                {
                    RetryPolicy = new ClientRetryPolicy(0),
                    NetworkTimeout = TimeSpan.FromSeconds(processingOptions.RequestTimeoutSeconds),
                });
        });

        services.AddHostedService<InboundMessageBackgroundService>();
    }
}
