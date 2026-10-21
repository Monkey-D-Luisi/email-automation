import { ApiError } from "./api.ts";

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

export function mutationFailure(error: unknown): { stale: boolean; message: string } {
  if (error instanceof ApiError && error.status === 409) {
    return { stale: true, message: "This item changed after you opened it. Reload the latest version before editing." };
  }
  return { stale: false, message: error instanceof Error ? error.message : "The update failed." };
}
