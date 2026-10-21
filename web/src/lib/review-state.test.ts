import assert from "node:assert/strict";
import test from "node:test";
import { ApiError } from "./api.ts";
import { canApproveDraft, canVerifyFact, loaded, loadFailed, loading, mutationFailure } from "./review-state.ts";

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
