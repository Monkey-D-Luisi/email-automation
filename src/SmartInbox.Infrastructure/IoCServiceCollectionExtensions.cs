using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SmartInbox.Application.Common.DependencyInjection;

namespace SmartInbox.Infrastructure;

public static class IoCServiceCollectionExtensions
{
    public static IServiceCollection AddAttributedServices(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);
        if (assemblies.Length == 0 || assemblies.Any(assembly => assembly is null))
            throw new ArgumentException("At least one non-null assembly is required.", nameof(assemblies));

        var implementations = assemblies.Distinct()
            .OrderBy(assembly => assembly.FullName, StringComparer.Ordinal)
            .SelectMany(assembly => assembly.DefinedTypes
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .Select(type => type.AsType()));
        return AddAttributedTypes(services, implementations);
    }

    internal static IServiceCollection AddAttributedTypes(IServiceCollection services, IEnumerable<Type> implementations)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(implementations);

        var declarations = implementations.Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .SelectMany(type => type.GetCustomAttributes<IoCAttribute>(inherit: false)
                .OrderBy(attribute => attribute.ServiceType.FullName, StringComparer.Ordinal)
                .Select(attribute => new Declaration(type, attribute)))
            .ToArray();

        Validate(services, declarations);

        foreach (var group in declarations.GroupBy(declaration => declaration.Implementation))
        {
            var implementation = group.Key;
            var lifetime = ConvertLifetime(group.First().Attribute.Lifetime);
            if (!services.Any(descriptor => descriptor.ServiceType == implementation))
                services.Add(ServiceDescriptor.Describe(implementation, implementation, lifetime));
        }

        foreach (var declaration in declarations)
        {
            if (declaration.Attribute.ServiceType == declaration.Implementation)
                continue;
            services.Add(ServiceDescriptor.Describe(
                declaration.Attribute.ServiceType,
                provider => provider.GetRequiredService(declaration.Implementation),
                ConvertLifetime(declaration.Attribute.Lifetime)));
        }

        return services;
    }

    private static void Validate(IServiceCollection services, IReadOnlyCollection<Declaration> declarations)
    {
        foreach (var declaration in declarations)
        {
            var implementation = declaration.Implementation;
            var service = declaration.Attribute.ServiceType;
            if (!implementation.IsClass || implementation.IsAbstract || implementation.ContainsGenericParameters ||
                service.ContainsGenericParameters || (!service.IsInterface && !service.IsClass) ||
                !service.IsAssignableFrom(implementation) || !Enum.IsDefined(declaration.Attribute.Lifetime))
                throw new InvalidOperationException($"Invalid [IoC] declaration: {implementation.FullName} -> {service.FullName}.");
        }

        var duplicate = declarations.GroupBy(item => (item.Implementation, item.Attribute.ServiceType))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Duplicate [IoC] declaration: {duplicate.Key.Implementation.FullName} -> {duplicate.Key.ServiceType.FullName}.");

        foreach (var group in declarations.GroupBy(item => item.Attribute.ServiceType))
        {
            if (group.Count() > 1 && group.Any(item => !item.Attribute.AllowMultiple))
                throw new InvalidOperationException($"Duplicate single [IoC] service {group.Key.FullName}.");
            if (group.Select(item => item.Attribute.Lifetime).Distinct().Count() > 1)
                throw new InvalidOperationException($"Conflicting [IoC] lifetimes for service {group.Key.FullName}.");
            if (services.Any(descriptor => descriptor.ServiceType == group.Key))
                throw new InvalidOperationException($"The [IoC] service {group.Key.FullName} is already registered explicitly.");
        }

        foreach (var group in declarations.GroupBy(item => item.Implementation))
        {
            if (group.Select(item => item.Attribute.Lifetime).Distinct().Count() > 1)
                throw new InvalidOperationException($"Conflicting [IoC] lifetimes for implementation {group.Key.FullName}.");
            var registrations = services.Where(descriptor => descriptor.ServiceType == group.Key).ToArray();
            var lifetime = ConvertLifetime(group.First().Attribute.Lifetime);
            if (registrations.Length > 1 || registrations.Any(descriptor => descriptor.Lifetime != lifetime))
                throw new InvalidOperationException($"Conflicting explicit construction for [IoC] implementation {group.Key.FullName}.");
        }
    }

    private static ServiceLifetime ConvertLifetime(IoCLifetime lifetime) => lifetime switch
    {
        IoCLifetime.Transient => ServiceLifetime.Transient,
        IoCLifetime.Scoped => ServiceLifetime.Scoped,
        IoCLifetime.Singleton => ServiceLifetime.Singleton,
        _ => throw new InvalidOperationException("Invalid [IoC] lifetime."),
    };

    private sealed record Declaration(Type Implementation, IoCAttribute Attribute);
}
