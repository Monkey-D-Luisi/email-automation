using Microsoft.EntityFrameworkCore;

namespace SmartInbox.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    internal DbSet<InboundMessageRecord> InboundMessages => Set<InboundMessageRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
