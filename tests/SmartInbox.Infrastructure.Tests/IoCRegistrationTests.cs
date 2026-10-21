using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartInbox.Application.Common.DependencyInjection;
using SmartInbox.Application.Messages.Ports;
using SmartInbox.Infrastructure;

namespace SmartInbox.Infrastructure.Tests;

public sealed class IoCRegistrationTests
{
    [Fact]
    public void Infrastructure_registers_and_resolves_repository_with_scoped_lifetime()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = "Host=localhost;Database=smartinbox_test;Username=test;Password=test",
            })
            .Build());

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IInboundMessageRepository) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IInboundMessageRepository>());
    }

    [Fact]
    public async Task Enabled_openai_processing_requires_credentials_at_host_startup()
    {
        using var host = CreateHost(new Dictionary<string, string?>
        {
            ["EmailProcessing:Enabled"] = "true",
            ["EmailAnalysis:Provider"] = "openai",
            ["OpenAI:Model"] = "gpt-6-luna",
        });

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("OpenAI:ApiKey is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Enabled_processing_rejects_unknown_provider_identity()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = "Host=localhost;Database=smartinbox_test;Username=test;Password=test",
                ["EmailProcessing:Enabled"] = "true",
                ["EmailAnalysis:Provider"] = "unsupported-provider",
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInfrastructure(configuration));

        Assert.Contains("EmailAnalysis:Provider", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Attributed_registration_rejects_duplicate_single_services_before_mutating_collection()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            IoCServiceCollectionExtensions.AddAttributedTypes(
                services,
                [typeof(ProbeOne), typeof(ProbeTwo)]));

        Assert.Contains("Duplicate single [IoC] service", exception.Message, StringComparison.Ordinal);
        Assert.Empty(services);
    }

    private interface IProbe { }

    private static IHost CreateHost(Dictionary<string, string?> settings)
    {
        settings["ConnectionStrings:Database"] = "Host=localhost;Database=smartinbox_test;Username=test;Password=test";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddInfrastructure(configuration);
            })
            .Build();
    }

    [IoC(typeof(IProbe))]
    private sealed class ProbeOne : IProbe { }

    [IoC(typeof(IProbe))]
    private sealed class ProbeTwo : IProbe { }
}
