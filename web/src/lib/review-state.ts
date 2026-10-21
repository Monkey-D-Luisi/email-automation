import { ApiError, type GmailDraftExport } from "./api.ts";

export type LoadState<T> =
  | { status: "loading" }
  | { status: "ready"; value: T }
  | { status: "error"; message: string };

export const loading = <T>(): LoadState<T> => ({ status: "loading" });
export const loaded = <T>(value: T): LoadState<T> => ({ status: "ready", value });

export function loadFailed<T>(error: unknown): LoadState<T> {
  return { status: "error", message: error instanceof Error ? error.message : "The request failed." };
}

export function canVerifyFact(status: string, persistedValue: string, visibleValue: string): boolean {
  return status === "Unverified" && persistedValue === visibleValue;
}

export function canApproveDraft(
  status: string,
  persistedSubject: string,
  persistedBody: string,
  visibleSubject: string,
  visibleBody: string,
): boolean {
  return status === "Draft" &&
    persistedSubject.trim().length > 0 &&
    persistedBody.trim().length > 0 &&
    persistedSubject === visibleSubject &&
    persistedBody === visibleBody;
}

export function canRequestGmailDraft(input: {
  enabled: boolean;
  workItemStatus: string;
  reviewerOwnsItem: boolean;
  revision: number | null;
  revisionStatus: string | null;
  persistedSubject: string | null;
  persistedBody: string | null;
  visibleSubject: string;
  visibleBody: string;
  savedRecipient: string | null;
  confirmedRecipient: string;
  recipientConfirmed: boolean;
  exports: GmailDraftExport[];
  mailboxId: string | null;
}): boolean {
  const eligible = input.enabled &&
    input.workItemStatus === "InReview" &&
    input.reviewerOwnsItem &&
    input.revision !== null &&
    input.revisionStatus === "Approved" &&
    input.persistedSubject === input.visibleSubject &&
    input.persistedBody === input.visibleBody &&
    input.savedRecipient !== null &&
    input.confirmedRecipient === input.savedRecipient &&
    input.recipientConfirmed &&
    input.mailboxId !== null;
  if (!eligible) return false;
  return !input.exports.some(exported =>
    exported.approvedRevision === input.revision &&
    exported.destinationMailboxId === input.mailboxId &&
    exported.recipientEmail === input.savedRecipient);
}

export function canManuallyResolveGmailDraftExport(status: string): boolean {
  return status === "Failed" || status === "NeedsReconciliation";
}

export function mutationFailure(error: unknown): { stale: boolean; message: string } {
  if (error instanceof ApiError && error.status === 409) {
    return { stale: true, message: "This item changed after you opened it. Reload the latest version before editing." };
  }
  return { stale: false, message: error instanceof Error ? error.message : "The update failed." };
}
