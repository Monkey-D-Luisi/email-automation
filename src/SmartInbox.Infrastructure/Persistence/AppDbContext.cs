using Microsoft.EntityFrameworkCore;

namespace SmartInbox.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    internal DbSet<InboundMessageRecord> InboundMessages => Set<InboundMessageRecord>();
    internal DbSet<EmailClassificationRecord> EmailClassifications => Set<EmailClassificationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
