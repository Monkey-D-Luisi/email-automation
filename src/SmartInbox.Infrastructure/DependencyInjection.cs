using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        return services;
    }
}
