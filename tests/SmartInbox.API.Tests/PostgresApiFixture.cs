using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SmartInbox.Application.Messages.Classification.Ports;
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
    public const string GmailIngestionTestToken = "test-token-for-gmail-ingestion-0123456789abcdef";
    public const string GmailDraftExportTestToken = "test-token-for-gmail-draft-export-0123456789abcdef";
    public const string GmailTestMailboxId = "integration-test-mailbox";
    private const long MigrationLockId = 824_111_177;
    private WebApplicationFactory<Program>? factory;
    private string adminConnectionString = string.Empty;
    private string schemaName = string.Empty;

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

        adminConnectionString = connection.ConnectionString;
        schemaName = $"smartinbox_api_it_{Guid.NewGuid():N}";
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
            await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(ConnectionString, options => options
                    .MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)
                    .MigrationsHistoryTable("__EFMigrationsHistory", schemaName))
                .Options);
            await context.Database.MigrateAsync();
            await using var verifyConnection = new NpgsqlConnection(ConnectionString);
            await verifyConnection.OpenAsync();
            await using var verifyCommand = new NpgsqlCommand("SELECT count(*) FROM information_schema.columns WHERE table_schema = @schema AND table_name = 'inbound_messages' AND column_name = 'FailureKind'", verifyConnection);
            verifyCommand.Parameters.AddWithValue("schema", schemaName);
            if (Convert.ToInt32(await verifyCommand.ExecuteScalarAsync()) != 1)
                throw new InvalidOperationException("The API test schema did not receive the Phase 2 migration.");
        }
        finally
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@lockId)", migrationLock);
            command.Parameters.AddWithValue("lockId", MigrationLockId);
            await command.ExecuteNonQueryAsync();
        }

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Database"] = ConnectionString,
                    ["ServiceAuthentication:Token"] = TestToken,
                    ["ReviewAuthentication:Password"] = "synthetic-reviewer-password-for-tests-0123456789",
                    ["ReviewAuthentication:ReviewerId"] = "reviewer-1",
                    ["ReviewAuthentication:DisplayName"] = "Synthetic reviewer",
                }));
            builder.ConfigureTestServices(ConfigureTestDatabase);
        });
        Client = CreateConfiguredClient(factory);
        var configuredConnection = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Database");
        if (new NpgsqlConnectionStringBuilder(configuredConnection).SearchPath != schemaName)
            throw new InvalidOperationException("The API test host did not use its isolated PostgreSQL schema.");
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.Database.OpenConnectionAsync();
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT current_setting('search_path') || '|' || coalesce(current_schema(), '') || '|' || coalesce(to_regclass('inbound_messages')::text, '') || '|' || EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'inbound_messages' AND column_name = 'FailureKind')::text";
            var databaseContext = (string?)await command.ExecuteScalarAsync();
            var databaseContextParts = databaseContext?.Split('|');
            if (databaseContextParts is not { Length: 4 } || databaseContextParts[1] != schemaName ||
                databaseContextParts[2] is not ("inbound_messages") && !databaseContextParts[2].EndsWith(".inbound_messages", StringComparison.Ordinal) ||
                databaseContextParts[3] != "true")
                throw new InvalidOperationException($"The API test host connected to an unexpected PostgreSQL schema: {databaseContext}");
        }
        Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestToken);
    }

    public HttpClient CreateClient() => factory?.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true })
        ?? throw new InvalidOperationException("The API test host has not been initialized.");

    public (WebApplicationFactory<Program> Host, HttpClient Client) CreateProcessingHost(IEmailAnalysisProvider provider)
    {
        var processingFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Database"] = ConnectionString,
                    ["ServiceAuthentication:Token"] = TestToken,
                    ["ReviewAuthentication:Password"] = "synthetic-reviewer-password-for-tests-0123456789",
                    ["ReviewAuthentication:ReviewerId"] = "reviewer-1",
                    ["ReviewAuthentication:DisplayName"] = "Synthetic reviewer",
                    ["EmailProcessing:Enabled"] = "true",
                    ["EmailProcessing:PollIntervalSeconds"] = "1",
                    ["OpenAI:ApiKey"] = "synthetic-test-credential",
                    ["GmailIntegration:Enabled"] = "true",
                    ["GmailIntegration:SourceId"] = "gmail-test",
                    ["GmailIntegration:MailboxId"] = GmailTestMailboxId,
                    ["GmailIntegration:IngestionToken"] = GmailIngestionTestToken,
                    ["GmailIntegration:DraftExportToken"] = GmailDraftExportTestToken,
                }));
            builder.ConfigureTestServices(services =>
            {
                ConfigureTestDatabase(services);
                services.RemoveAll<IEmailAnalysisProvider>();
                services.AddSingleton(provider);
            });
        });
        var client = CreateConfiguredClient(processingFactory);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestToken);
        return (processingFactory, client);
    }

    private HttpClient CreateConfiguredClient(WebApplicationFactory<Program> webApplicationFactory)
    {
        var previousConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Database");
        Environment.SetEnvironmentVariable("ConnectionStrings__Database", ConnectionString);
        try
        {
            return webApplicationFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__Database", previousConnectionString);
        }
    }

    public async Task ResetAsync()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString, options => options
                .MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__EFMigrationsHistory", schemaName))
            .Options);
            await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE email_classifications, inbound_messages CASCADE");
    }

    private void ConfigureTestDatabase(IServiceCollection services)
    {
        services.RemoveAll<AppDbContext>();
        services.RemoveAll<DbContextOptions<AppDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(
            ConnectionString,
            npgsql => npgsql
                .MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__EFMigrationsHistory", schemaName)));
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        if (factory is not null)
            await factory.DisposeAsync();

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
