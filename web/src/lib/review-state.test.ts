import assert from "node:assert/strict";
import test from "node:test";
import { ApiError } from "./api.ts";
import { canApproveDraft, canManuallyResolveGmailDraftExport, canRequestDraftExport, canVerifyFact, loaded, loadFailed, loading, mutationFailure } from "./review-state.ts";

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

test("an external draft request requires the current approved snapshot and explicit mailbox and recipient confirmation", () => {
  const input = {
    provider: "Gmail" as const,
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
  assert.equal(canRequestDraftExport(input), true);
  assert.equal(canRequestDraftExport({ ...input, enabled: false }), false);
  assert.equal(canRequestDraftExport({ ...input, workItemStatus: "Resolved" }), false);
  assert.equal(canRequestDraftExport({ ...input, revisionStatus: "Draft" }), false);
  assert.equal(canRequestDraftExport({ ...input, visibleBody: "Edited unsaved response" }), false);
  assert.equal(canRequestDraftExport({ ...input, recipientConfirmed: false }), false);
  assert.equal(canRequestDraftExport({ ...input, confirmedRecipient: "other@example.test" }), false);
  assert.equal(canRequestDraftExport({ ...input, reviewerOwnsItem: false }), false);
  assert.equal(canRequestDraftExport({
    ...input,
    exports: [{
      id: "export-1", approvedRevision: 2, destinationMailboxId: "test-mailbox", recipientEmail: "sender@example.test",
      contentFingerprint: "", requestedBy: "reviewer", createdAt: "", updatedAt: "", completedAt: null,
      status: "Pending", attemptCount: 0, provider: "Gmail", externalDraftId: null, gmailDraftId: null, failureKind: null,
    }],
  }), false);
  assert.equal(canRequestDraftExport({
    ...input,
    provider: "Outlook",
    exports: [{
      id: "export-outlook", approvedRevision: 2, destinationMailboxId: "test-mailbox", recipientEmail: "sender@example.test",
      contentFingerprint: "", requestedBy: "reviewer", createdAt: "", updatedAt: "", completedAt: null,
      status: "Pending", attemptCount: 0, provider: "Outlook", externalDraftId: null, gmailDraftId: null, failureKind: null,
    }],
  }), false);
  assert.equal(canRequestDraftExport({ ...input, provider: "Outlook" }), true);
});

test("manual export resolution is available only after a failed or ambiguous attempt", () => {
  assert.equal(canManuallyResolveGmailDraftExport("Failed"), true);
  assert.equal(canManuallyResolveGmailDraftExport("NeedsReconciliation"), true);
  assert.equal(canManuallyResolveGmailDraftExport("Pending"), false);
  assert.equal(canManuallyResolveGmailDraftExport("Succeeded"), false);
});
