namespace SmartInbox.Infrastructure.WorkItems.Persistence;

internal sealed class ResponseDraftRecord
{
    public Guid Id { get; set; }
    public Guid WorkItemId { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public List<ResponseDraftRevisionRecord> Revisions { get; set; } = [];
}
