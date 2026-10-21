using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartInbox.Application.Common.DependencyInjection;
using SmartInbox.Application.Common.Interfaces;
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

    [IoC(typeof(IProbe))]
    private sealed class ProbeOne : IProbe { }

    [IoC(typeof(IProbe))]
    private sealed class ProbeTwo : IProbe { }
}
