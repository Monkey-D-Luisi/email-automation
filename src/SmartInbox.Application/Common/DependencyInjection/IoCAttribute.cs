namespace SmartInbox.Application.Common.DependencyInjection;

public enum IoCLifetime
{
    Transient,
    Scoped,
    Singleton,
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class IoCAttribute(
    Type serviceType,
    IoCLifetime lifetime = IoCLifetime.Scoped,
    bool allowMultiple = false) : Attribute
{
    public Type ServiceType { get; } = serviceType;
    public IoCLifetime Lifetime { get; } = lifetime;
    public bool AllowMultiple { get; } = allowMultiple;
}
