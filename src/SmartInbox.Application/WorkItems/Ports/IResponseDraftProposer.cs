using SmartInbox.Application.WorkItems.Models;

namespace SmartInbox.Application.WorkItems.Ports;

public interface IResponseDraftProposer
{
    Task<DraftProposalResult> ProposeAsync(DraftProposalContext context, CancellationToken cancellationToken = default);
}
