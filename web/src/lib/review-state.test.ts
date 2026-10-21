import assert from "node:assert/strict";
import test from "node:test";
import { ApiError } from "./api.ts";
import { canApproveDraft, canManuallyResolveGmailDraftExport, canRequestGmailDraft, canVerifyFact, loaded, loadFailed, loading, mutationFailure } from "./review-state.ts";

test("inbox state exposes loading, loaded, and request errors", () => {
  assert.deepEqual(loading(), { status: "loading" });
  assert.deepEqual(loaded(["synthetic"]), { status: "ready", value: ["synthetic"] });
  assert.deepEqual(loadFailed(new Error("offline")), { status: "error", message: "offline" });
});

test("stale edit conflicts tell the reviewer to reload before saving again", () => {
  assert.deepEqual(mutationFailure(new ApiError(409, "conflict")), {
    stale: true,
    message: "This item changed after you opened it. Reload the latest version before editing.",
  });
  assert.deepEqual(mutationFailure(new ApiError(400, "invalid")), { stale: false, message: "invalid" });
});

test("a fact cannot be verified while its visible value differs from the persisted value", () => {
  assert.equal(canVerifyFact("Unverified", "INV-2030", "INV-2031"), false);
  assert.equal(canVerifyFact("Unverified", "INV-2031", "INV-2031"), true);
  assert.equal(canVerifyFact("Verified", "INV-2031", "INV-2031"), false);
});

test("a draft can only be approved when the exact persisted revision is visible", () => {
  assert.equal(canApproveDraft("Draft", "Response", "Please confirm.", "Response", "Please confirm."), true);
  assert.equal(canApproveDraft("Draft", "Response", "Please confirm.", "Edited response", "Please confirm."), false);
  assert.equal(canApproveDraft("Draft", "Response", "Please confirm.", "Response", "Edited body"), false);
  assert.equal(canApproveDraft("Draft", "Edited response", "Please confirm.", "Edited response", "Please confirm."), true);
  assert.equal(canApproveDraft("Approved", "Response", "Please confirm.", "Response", "Please confirm."), false);
});

test("a Gmail draft request requires the current approved snapshot and explicit mailbox and recipient confirmation", () => {
  const input = {
    enabled: true,
    workItemStatus: "InReview",
    reviewerOwnsItem: true,
    revision: 2,
    revisionStatus: "Approved",
    persistedSubject: "Response",
    persistedBody: "Please confirm.",
    visibleSubject: "Response",
    visibleBody: "Please confirm.",
    savedRecipient: "sender@example.test",
    confirmedRecipient: "sender@example.test",
    recipientConfirmed: true,
    exports: [],
    mailboxId: "test-mailbox",
  };
  assert.equal(canRequestGmailDraft(input), true);
  assert.equal(canRequestGmailDraft({ ...input, enabled: false }), false);
  assert.equal(canRequestGmailDraft({ ...input, workItemStatus: "Resolved" }), false);
  assert.equal(canRequestGmailDraft({ ...input, revisionStatus: "Draft" }), false);
  assert.equal(canRequestGmailDraft({ ...input, visibleBody: "Edited unsaved response" }), false);
  assert.equal(canRequestGmailDraft({ ...input, recipientConfirmed: false }), false);
  assert.equal(canRequestGmailDraft({ ...input, confirmedRecipient: "other@example.test" }), false);
  assert.equal(canRequestGmailDraft({ ...input, reviewerOwnsItem: false }), false);
  assert.equal(canRequestGmailDraft({
    ...input,
    exports: [{
      id: "export-1", approvedRevision: 2, destinationMailboxId: "test-mailbox", recipientEmail: "sender@example.test",
      contentFingerprint: "", requestedBy: "reviewer", createdAt: "", updatedAt: "", completedAt: null,
      status: "Pending", attemptCount: 0, gmailDraftId: null, failureKind: null,
    }],
  }), false);
});

test("manual export resolution is available only after a failed or ambiguous attempt", () => {
  assert.equal(canManuallyResolveGmailDraftExport("Failed"), true);
  assert.equal(canManuallyResolveGmailDraftExport("NeedsReconciliation"), true);
  assert.equal(canManuallyResolveGmailDraftExport("Pending"), false);
  assert.equal(canManuallyResolveGmailDraftExport("Succeeded"), false);
});
