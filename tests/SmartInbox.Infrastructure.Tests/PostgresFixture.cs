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
    private const long MigrationLockId = 824_111_177;
    private string adminConnectionString = string.Empty;
    private string schemaName = string.Empty;

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

        adminConnectionString = connection.ConnectionString;
        schemaName = $"smartinbox_it_{Guid.NewGuid():N}";
        await using var migrationLock = new NpgsqlConnection(adminConnectionString);
        await migrationLock.OpenAsync();
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_lock(@lockId)", migrationLock))
        {
            command.Parameters.AddWithValue("lockId", MigrationLockId);
            await command.ExecuteNonQueryAsync();
        }
        try
        {
            await using (var command = new NpgsqlCommand($"CREATE SCHEMA \"{schemaName}\"", migrationLock))
                await command.ExecuteNonQueryAsync();

            connection.SearchPath = schemaName;
            ConnectionString = connection.ConnectionString;
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
        }
        finally
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@lockId)", migrationLock);
            command.Parameters.AddWithValue("lockId", MigrationLockId);
            await command.ExecuteNonQueryAsync();
        }
    }

    public AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(ConnectionString, options => options
            .MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)
            .MigrationsHistoryTable("__EFMigrationsHistory", schemaName))
        .Options);

    public async Task ResetAsync()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE email_classifications, inbound_messages CASCADE");
    }

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

    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(schemaName))
            return;

        await using var migrationLock = new NpgsqlConnection(adminConnectionString);
        await migrationLock.OpenAsync();
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_lock(@lockId)", migrationLock))
        {
            command.Parameters.AddWithValue("lockId", MigrationLockId);
            await command.ExecuteNonQueryAsync();
        }
        try
        {
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE", migrationLock);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@lockId)", migrationLock);
            command.Parameters.AddWithValue("lockId", MigrationLockId);
            await command.ExecuteNonQueryAsync();
        }
    }
}
