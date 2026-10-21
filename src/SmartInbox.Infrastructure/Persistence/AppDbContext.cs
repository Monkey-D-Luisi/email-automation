using Microsoft.EntityFrameworkCore;
using SmartInbox.Infrastructure.Messages.Persistence;
using SmartInbox.Infrastructure.WorkItems.Persistence;

namespace SmartInbox.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    internal DbSet<InboundMessageRecord> InboundMessages => Set<InboundMessageRecord>();
    internal DbSet<EmailClassificationRecord> EmailClassifications => Set<EmailClassificationRecord>();
    internal DbSet<WorkItemRecord> WorkItems => Set<WorkItemRecord>();
    internal DbSet<WorkItemFactRecord> WorkItemFacts => Set<WorkItemFactRecord>();
    internal DbSet<ResponseDraftRecord> ResponseDrafts => Set<ResponseDraftRecord>();
    internal DbSet<ResponseDraftRevisionRecord> ResponseDraftRevisions => Set<ResponseDraftRevisionRecord>();
    internal DbSet<WorkItemAuditRecord> WorkItemAuditEntries => Set<WorkItemAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
