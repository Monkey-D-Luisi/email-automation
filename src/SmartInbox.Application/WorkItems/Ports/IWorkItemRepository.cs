using SmartInbox.Application.WorkItems.Models;
using SmartInbox.Domain.WorkItems;

namespace SmartInbox.Application.WorkItems.Ports;

public interface IWorkItemRepository
{
    Task<WorkItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkItemPage> ListAsync(WorkItemListFilter filter, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkItemAuditEntry>?> GetHistoryAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveAsync(WorkItem workItem, CancellationToken cancellationToken = default);
}
