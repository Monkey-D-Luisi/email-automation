using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartInbox.Infrastructure;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.Infrastructure.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL integration";
}

public sealed class PostgresFixture : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("SMARTINBOX_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("Set SMARTINBOX_TEST_CONNECTION_STRING to a dedicated PostgreSQL test database.");

        var connection = new NpgsqlConnectionStringBuilder(configured);
        if (connection.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new InvalidOperationException("PostgreSQL integration tests only connect to localhost.");
        if (string.IsNullOrEmpty(connection.Database) ||
            !connection.Database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PostgreSQL integration tests require a dedicated database name ending in '_test'.");

        ConnectionString = connection.ConnectionString;
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(ConnectionString, options => options.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
        .Options);

    public ServiceProvider CreateServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = ConnectionString,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
