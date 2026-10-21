using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Npgsql;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.API.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresApiCollection : ICollectionFixture<PostgresApiFixture>
{
    public const string Name = "PostgreSQL API integration";
}

public sealed class PostgresApiFixture : IAsyncLifetime
{
    private const string TestToken = "test-token-for-service-authentication-0123456789abcdef";
    private WebApplicationFactory<Program>? factory;

    public HttpClient Client { get; private set; } = null!;
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
        await using (var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                         .UseNpgsql(ConnectionString, options => options.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                         .Options))
            await context.Database.MigrateAsync();

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Database"] = ConnectionString,
                    ["ServiceAuthentication:Token"] = TestToken,
                })));
        Client = factory.CreateClient();
        Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestToken);
    }

    public HttpClient CreateClient() => factory?.CreateClient()
        ?? throw new InvalidOperationException("The API test host has not been initialized.");

    public async Task DisposeAsync()
    {
        Client.Dispose();
        if (factory is not null)
            await factory.DisposeAsync();
    }
}
