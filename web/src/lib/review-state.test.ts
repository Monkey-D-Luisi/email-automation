import assert from "node:assert/strict";
import test from "node:test";
import { ApiError } from "./api.ts";
import { loaded, loadFailed, loading, mutationFailure } from "./review-state.ts";

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
