namespace SmartInbox.Application.WorkItems.Models;

public sealed class WorkItemNotFoundException() : Exception("The work item was not found.");

public sealed class WorkItemFactNotFoundException() : Exception("The reviewed fact was not found.");

public sealed class WorkItemConcurrencyException(string message = "The work item was changed by another request. Reload it and try again.") : Exception(message);

public sealed class WorkItemStateConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class WorkItemValidationException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class DraftingDisabledException() : Exception("AI-assisted response drafting is disabled.");

public sealed class DraftProposalException(string message, Exception? innerException = null)
    : Exception(message, innerException);
