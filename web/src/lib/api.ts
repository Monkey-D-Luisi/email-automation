export type ApiProblem = {
  title?: string;
  detail?: string;
  status?: number;
};

export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

export type ReviewerSession = {
  reviewerId: string;
  displayName: string;
  aiDraftingEnabled: boolean;
  gmailDraftExportEnabled: boolean;
  gmailDraftExportMailboxId: string | null;
};

export type GmailDraftExport = {
  id: string;
  approvedRevision: number;
  destinationMailboxId: string;
  recipientEmail: string;
  contentFingerprint: string;
  requestedBy: string;
  createdAt: string;
  updatedAt: string;
  completedAt: string | null;
  status: "Pending" | "InProgress" | "Succeeded" | "Failed" | "NeedsReconciliation" | "Cancelled";
  attemptCount: number;
  gmailDraftId: string | null;
  failureKind: "ProviderNotStarted" | "ProviderOutcomeUnknown" | null;
};

export type GmailDraftExportList = {
  enabled: boolean;
  destinationMailboxId: string | null;
  exports: GmailDraftExport[];
};

export type WorkItemListItem = {
  id: string;
  sourceMessageId: string;
  subject: string;
  senderEmail: string | null;
  senderName: string | null;
  receivedAt: string;
  status: string;
  category: string;
  priority: string;
  requiresReview: boolean;
  assignedReviewerId: string | null;
  version: number;
};

export type WorkItemPage = {
  items: WorkItemListItem[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
};

export type WorkItem = {
  id: string;
  sourceMessageId: string;
  status: string;
  assignedReviewerId: string | null;
  effectiveCategory: string;
  effectivePriority: string;
  reviewerNotes: string | null;
  missingInformation: string[];
  createdAt: string;
  updatedAt: string;
  resolvedAt: string | null;
  resolvedBy: string | null;
  version: number;
  subject: string;
  bodyText: string;
  senderEmail: string | null;
  senderName: string | null;
  receivedAt: string;
  importedAt: string;
  processingStatus: string;
  processingAttemptCount: number;
  processingFailureKind: string | null;
  processingFailureMessage: string | null;
  originalClassification: {
    category: string;
    priority: string;
    summary: string;
    language: string;
    facts: { type: string; value: string }[];
    missingInformation: string[];
    warnings: string[];
    uncertaintyReasons: string[];
    reviewReasons: string[];
    requiresReview: boolean;
    classifiedAt: string;
  } | null;
  facts: ReviewedFact[];
  draft: ResponseDraft | null;
};

export type ReviewedFact = {
  id: string;
  type: string;
  originalValue: string;
  value: string;
  status: "Unverified" | "Verified" | "Rejected";
  modifiedAt: string | null;
  modifiedBy: string | null;
};

export type ResponseDraft = {
  id: string;
  recipientEmail: string;
  currentRevision: number;
  revisions: DraftRevision[];
};

export type DraftRevision = {
  id: string;
  revision: number;
  subject: string;
  body: string;
  status: "Draft" | "Approved" | "Rejected";
  createdAt: string;
  createdBy: string;
  decidedAt: string | null;
  decidedBy: string | null;
  feedback: string | null;
  generationMetrics: {
    providerId: string;
    model: string;
    durationMilliseconds: number;
    inputTokens: number | null;
    cachedInputTokens: number | null;
    outputTokens: number | null;
    estimatedCostUsd: number | null;
  } | null;
};

export type AuditEntry = {
  id: string;
  workItemId: string;
  actorId: string;
  action: string;
  occurredAt: string;
  entityId: string | null;
  revision: number | null;
  summary: string;
};

export type WorkItemFilters = {
  pageNumber: number;
  pageSize: number;
  status?: string;
  priority?: string;
  category?: string;
};

async function readProblem(response: Response): Promise<ApiProblem> {
  try {
    return (await response.json()) as ApiProblem;
  } catch {
    return {};
  }
}

async function csrfToken(): Promise<string> {
  const response = await fetch("/api/v1/reviewer/antiforgery", {
    cache: "no-store",
    credentials: "same-origin",
  });
  if (!response.ok) throw new ApiError(response.status, "Could not verify this request.");
  const result = (await response.json()) as { requestToken: string };
  return result.requestToken;
}

export async function apiRequest<T>(path: string, init: RequestInit = {}): Promise<T> {
  const method = (init.method ?? "GET").toUpperCase();
  const headers = new Headers(init.headers);
  headers.set("Accept", "application/json");
  if (method !== "GET" && method !== "HEAD" && method !== "OPTIONS") {
    headers.set("X-CSRF-TOKEN", await csrfToken());
  }

  const response = await fetch(path, {
    ...init,
    method,
    headers,
    credentials: "same-origin",
    cache: "no-store",
  });
  if (!response.ok) {
    const problem = await readProblem(response);
    throw new ApiError(response.status, problem.detail ?? problem.title ?? "The request failed.");
  }
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export const getSession = () => apiRequest<ReviewerSession>("/api/v1/reviewer/session");

export const signIn = (password: string) =>
  apiRequest<{ reviewerId: string; displayName: string }>("/api/v1/reviewer/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ password }),
  });

export const signOut = () => apiRequest<void>("/api/v1/reviewer/logout", { method: "POST" });

export function listWorkItems(filters: WorkItemFilters): Promise<WorkItemPage> {
  const query = new URLSearchParams({ pageNumber: String(filters.pageNumber), pageSize: String(filters.pageSize) });
  if (filters.status) query.set("status", filters.status);
  if (filters.priority) query.set("priority", filters.priority);
  if (filters.category) query.set("category", filters.category);
  return apiRequest<WorkItemPage>(`/api/v1/work-items?${query.toString()}`);
}

export const getWorkItem = (id: string) => apiRequest<WorkItem>(`/api/v1/work-items/${id}`);

export const getHistory = (id: string) =>
  apiRequest<{ entries: AuditEntry[] }>(`/api/v1/work-items/${id}/history`);

export const getGmailDraftExports = (id: string) =>
  apiRequest<GmailDraftExportList>(`/api/v1/work-items/${id}/draft-exports`);

export const createGmailDraftExport = (id: string, request: {
  expectedVersion: number;
  revision: number;
  destinationMailboxId: string;
  recipientEmail: string;
}) => apiRequest<GmailDraftExport>(`/api/v1/work-items/${id}/draft-exports`, {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify(request),
});

export const retryGmailDraftExport = (workItemId: string, exportId: string, request: {
  expectedVersion: number;
  confirmedNoDraftExists: boolean;
}) => apiRequest<GmailDraftExport>(`/api/v1/work-items/${workItemId}/draft-exports/${exportId}/retry`, {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify(request),
});

export const reconcileGmailDraftExport = (workItemId: string, exportId: string, request: {
  expectedVersion: number;
  gmailDraftId: string;
}) => apiRequest<GmailDraftExport>(`/api/v1/work-items/${workItemId}/draft-exports/${exportId}/reconcile`, {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify(request),
});

export const mutateWorkItem = (path: string, payload: unknown, method = "POST") =>
  apiRequest<{ version: number }>(path, {
    method,
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
