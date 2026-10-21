using SmartInbox.Application.Common.DependencyInjection;
using SmartInbox.Application.Messages.Classification.Ports;
using SmartInbox.Application.Messages.Classification.Models;

namespace SmartInbox.Application.Messages.Classification;

[IoC(typeof(IEmailAnalysisProviderFactory), IoCLifetime.Scoped)]
public sealed class EmailAnalysisProviderFactory(
    IEnumerable<IEmailAnalysisProvider> implementations,
    EmailAnalysisProviderType selectedProvider) : IEmailAnalysisProviderFactory
{
    private readonly IReadOnlyDictionary<EmailAnalysisProviderType, IEmailAnalysisProvider> providers = CreateProviderMap(implementations);

    public IEmailAnalysisProvider Get(EmailAnalysisProviderType providerType)
    {
        if (providerType != selectedProvider)
            throw new InvalidOperationException("The requested email analysis provider is not configured.");
        return providers.TryGetValue(providerType, out var provider)
            ? provider
            : throw new InvalidOperationException("The configured email analysis provider is not registered.");
    }

    private static IReadOnlyDictionary<EmailAnalysisProviderType, IEmailAnalysisProvider> CreateProviderMap(
        IEnumerable<IEmailAnalysisProvider> implementations)
    {
        var providers = implementations.ToArray();
        var duplicate = providers.GroupBy(provider => provider.ProviderType).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Duplicate email analysis providers are registered for {duplicate.Key}.");
        return providers.ToDictionary(provider => provider.ProviderType);
    }
}
