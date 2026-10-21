"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import {
  getHistory, getSession, getWorkItem, listWorkItems, mutateWorkItem, signOut,
  type AuditEntry, type ReviewerSession, type WorkItem, type WorkItemFilters, type WorkItemPage,
} from "../lib/api";
import { canApproveDraft, canVerifyFact, loadFailed, loading, mutationFailure, type LoadState } from "../lib/review-state";

const categories = ["SalesInquiry", "SupportRequest", "BillingDocument", "GeneralInquiry", "Irrelevant", "Unknown"];
const priorities = ["Low", "Normal", "High", "Urgent"];
const statuses = ["PendingReview", "InReview", "Resolved"];
const emptyFilters: WorkItemFilters = { pageNumber: 1, pageSize: 20 };

function formatDate(value: string) {
  return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(new Date(value));
}

export default function ReviewWorkspace() {
  const router = useRouter();
  const [session, setSession] = useState<ReviewerSession | null | undefined>(undefined);
  const [inbox, setInbox] = useState<LoadState<WorkItemPage>>(loading());
  const [filters, setFilters] = useState<WorkItemFilters>(emptyFilters);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [detail, setDetail] = useState<LoadState<WorkItem>>(loading());
  const [history, setHistory] = useState<LoadState<AuditEntry[]>>(loading());
  const [refreshNumber, setRefreshNumber] = useState(0);
  const [stale, setStale] = useState(false);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState("");
  const [failure, setFailure] = useState("");
  const [category, setCategory] = useState(categories[0]);
  const [priority, setPriority] = useState(priorities[1]);
  const [notes, setNotes] = useState("");
  const [missingInformation, setMissingInformation] = useState("");
  const [factValues, setFactValues] = useState<Record<string, string>>({});
  const [draftSubject, setDraftSubject] = useState("");
  const [draftBody, setDraftBody] = useState("");
  const [draftFeedback, setDraftFeedback] = useState("");

  useEffect(() => {
    let current = true;
    getSession().then(value => { if (current) setSession(value); }).catch(() => { if (current) setSession(null); });
    return () => { current = false; };
  }, []);

  useEffect(() => {
    if (session === null) router.replace("/login");
  }, [router, session]);

  useEffect(() => {
    if (!session) return;
    let current = true;
    setInbox(loading());
    listWorkItems(filters).then(value => { if (current) setInbox({ status: "ready", value }); }).catch(error => { if (current) setInbox(loadFailed(error)); });
    return () => { current = false; };
  }, [filters, session]);

  useEffect(() => {
    if (!session || !selectedId) {
      setDetail(loading());
      setHistory(loading());
      return;
    }
    let current = true;
    setDetail(loading());
    setHistory(loading());
    Promise.all([getWorkItem(selectedId), getHistory(selectedId)]).then(([item, audit]) => {
      if (!current) return;
      setDetail({ status: "ready", value: item });
      setHistory({ status: "ready", value: audit.entries });
      setCategory(item.effectiveCategory);
      setPriority(item.effectivePriority);
      setNotes(item.reviewerNotes ?? "");
      setMissingInformation(item.missingInformation.join("\n"));
      setFactValues(Object.fromEntries(item.facts.map(fact => [fact.id, fact.value])));
      const revision = item.draft?.revisions.at(-1);
      setDraftSubject(revision?.subject ?? "");
      setDraftBody(revision?.body ?? "");
      setDraftFeedback(revision?.feedback ?? "");
      setStale(false);
      setFailure("");
    }).catch(error => { if (current) setDetail(loadFailed(error)); });
    return () => { current = false; };
  }, [selectedId, refreshNumber, session]);

  const reload = useCallback(() => setRefreshNumber(value => value + 1), []);

  async function act(path: string, payload: unknown, method = "POST", success = "Changes saved.") {
    setBusy(true);
    setFailure("");
    setNotice("");
    try {
      await mutateWorkItem(path, payload, method);
      setNotice(success);
      reload();
      setFilters(value => ({ ...value }));
    } catch (error) {
      const outcome = mutationFailure(error);
      setFailure(outcome.message);
      setStale(outcome.stale);
    } finally {
      setBusy(false);
    }
  }

  const current = detail.status === "ready" ? detail.value : null;
  const revision = current?.draft?.revisions.at(-1) ?? null;
  const canEdit = Boolean(current && current.status === "InReview" && !stale && !busy);
  const unverifiedCount = current?.facts.filter(fact => fact.status === "Unverified").length ?? 0;
  const itemBase = current ? `/api/v1/work-items/${current.id}` : "";
  const pages = inbox.status === "ready" ? Math.max(1, Math.ceil(inbox.value.totalCount / inbox.value.pageSize)) : 1;

  async function submitReview(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!current) return;
    await act(`${itemBase}/review`, {
      expectedVersion: current.version,
      category,
      priority,
      notes,
      missingInformation: missingInformation.split(/\r?\n/).map(value => value.trim()).filter(Boolean),
    }, "PATCH", "Review details saved.");
  }

  async function saveDraft(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!current) return;
    await act(`${itemBase}/draft`, { expectedVersion: current.version, subject: draftSubject, body: draftBody }, "POST", "A new unapproved draft revision was saved.");
  }

  async function logout() {
    try { await signOut(); } finally { setSession(null); router.replace("/login"); }
  }

  if (session === undefined) return <main className="center-state">Loading reviewer session…</main>;
  if (session === null) {
    return <main className="center-state">Opening sign in…</main>;
  }

  return (
    <main className="app-shell">
      <header className="topbar">
        <a className="brand" href="/" aria-label="Smart Inbox home"><span className="brand-mark">SI</span><span><strong>Smart Inbox</strong><small>Human review</small></span></a>
        <div className="account"><span className="reviewer-avatar" aria-hidden="true">{session.displayName.slice(0, 1).toUpperCase()}</span><span className="reviewer-name">{session.displayName}</span><button className="quiet-button" onClick={logout}>Sign out</button></div>
      </header>

      <section className="page-heading">
        <div><p className="eyebrow">REVIEW WORKSPACE</p><h1>Inbox</h1><p className="muted">Review classified messages, verify facts, and prepare internal response drafts.</p></div>
        <div className="privacy-pill"><span className="status-dot" />Local reviewer session</div>
      </section>

      <div className="workspace-grid">
        <aside className="inbox-panel panel">
          <div className="panel-heading"><div><h2>Work queue</h2><p className="muted">{inbox.status === "ready" ? `${inbox.value.totalCount} items` : "Classified messages"}</p></div><button className="icon-button" aria-label="Refresh inbox" onClick={reload}>↻</button></div>
          <div className="filter-grid">
            <label>Status<select value={filters.status ?? ""} onChange={event => setFilters({ ...filters, status: event.target.value || undefined, pageNumber: 1 })}><option value="">All statuses</option>{statuses.map(value => <option key={value}>{value}</option>)}</select></label>
            <label>Priority<select value={filters.priority ?? ""} onChange={event => setFilters({ ...filters, priority: event.target.value || undefined, pageNumber: 1 })}><option value="">All priorities</option>{priorities.map(value => <option key={value}>{value}</option>)}</select></label>
            <label>Category<select value={filters.category ?? ""} onChange={event => setFilters({ ...filters, category: event.target.value || undefined, pageNumber: 1 })}><option value="">All categories</option>{categories.map(value => <option key={value}>{value}</option>)}</select></label>
          </div>
          {inbox.status === "loading" && <div className="list-state">Loading work items…</div>}
          {inbox.status === "error" && <div className="list-state error-box" role="alert">{inbox.message}<button className="text-button" onClick={reload}>Try again</button></div>}
          {inbox.status === "ready" && inbox.value.items.length === 0 && <div className="list-state"><span className="empty-icon">✓</span><strong>Nothing in this view</strong><span className="muted">Try a different filter or check back later.</span></div>}
          {inbox.status === "ready" && inbox.value.items.length > 0 && <div className="work-list">{inbox.value.items.map(item => <button key={item.id} className={`work-list-item ${selectedId === item.id ? "selected" : ""}`} onClick={() => setSelectedId(item.id)}>
            <span className="work-item-top"><strong>{item.subject || "(No subject)"}</strong><span className={`priority priority-${item.priority.toLowerCase()}`}>{item.priority}</span></span>
            <span className="work-item-sender">{item.senderName || item.senderEmail || "Unknown sender"}</span>
            <span className="work-item-bottom"><span>{item.category}</span><span>{formatDate(item.receivedAt)}</span></span>
            {item.requiresReview && <span className="review-flag">Needs attention</span>}
          </button>)}</div>}
          {inbox.status === "ready" && <div className="pagination"><button className="quiet-button" disabled={filters.pageNumber <= 1} onClick={() => setFilters({ ...filters, pageNumber: filters.pageNumber - 1 })}>← Previous</button><span>Page {filters.pageNumber} of {pages}</span><button className="quiet-button" disabled={filters.pageNumber >= pages} onClick={() => setFilters({ ...filters, pageNumber: filters.pageNumber + 1 })}>Next →</button></div>}
        </aside>

        <section className="detail-panel">
          {!selectedId && <div className="panel empty-detail"><span className="empty-icon">✉</span><h2>Select a message</h2><p className="muted">Choose an item from the work queue to review its classification and response draft.</p></div>}
          {selectedId && detail.status === "loading" && <div className="panel list-state">Loading message details…</div>}
          {selectedId && detail.status === "error" && <div className="panel error-box" role="alert">{detail.message}<button className="text-button" onClick={reload}>Try again</button></div>}
          {current && <>
            <div className="detail-header panel"><div><div className="detail-title-row"><span className={`status-badge status-${current.status.toLowerCase()}`}>{current.status}</span>{current.originalClassification?.requiresReview && <span className="review-flag">Needs attention</span>}</div><h2>{current.subject || "(No subject)"}</h2><p className="muted">{current.senderName ? `${current.senderName} · ` : ""}{current.senderEmail ?? "Unknown sender"} · {formatDate(current.receivedAt)}</p></div><div className="detail-header-actions">{current.status === "PendingReview" && <button className="primary" disabled={busy || stale} onClick={() => void act(`${itemBase}/start-review`, { expectedVersion: current.version }, "POST", "Review assigned to you.")}>Start review</button>}{stale && <button className="secondary" onClick={reload}>Reload latest version</button>}</div></div>
            {(failure || notice) && <div className={failure ? "error-box inline-message" : "success-box inline-message"} role={failure ? "alert" : "status"}>{failure || notice}</div>}
            <div className="detail-columns">
              <div className="detail-main">
                <section className="panel section-card"><div className="section-heading"><div><p className="eyebrow">SOURCE MESSAGE</p><h3>Original email</h3></div><span className="small-meta">Received {formatDate(current.receivedAt)}</span></div><div className="email-body">{current.bodyText || "(No plain-text body)"}</div></section>

                <section className="panel section-card"><div className="section-heading"><div><p className="eyebrow">AI ANALYSIS</p><h3>Original classification</h3></div><span className={`status-badge ${current.originalClassification?.requiresReview ? "status-attention" : "status-done"}`}>{current.originalClassification?.requiresReview ? "Review required" : "No warning"}</span></div>
                  {current.originalClassification ? <><p className="classification-summary">{current.originalClassification.summary}</p><div className="classification-chips"><span>{current.originalClassification.category}</span><span>{current.originalClassification.priority}</span><span>{current.originalClassification.language}</span></div>{current.originalClassification.reviewReasons.length > 0 && <div className="reason-list"><strong>Review reasons</strong><ul>{current.originalClassification.reviewReasons.map(reason => <li key={reason}>{reason}</li>)}</ul></div>}{current.originalClassification.warnings.length > 0 && <div className="reason-list"><strong>Warnings</strong><ul>{current.originalClassification.warnings.map(reason => <li key={reason}>{reason}</li>)}</ul></div>}</> : <p className="muted">No completed classification is available.</p>}
                  <div className="diagnostics"><strong>Processing</strong><span>{current.processingStatus} · {current.processingAttemptCount} attempt(s){current.processingFailureKind ? ` · ${current.processingFailureKind}` : ""}</span>{current.processingFailureMessage && <span className="muted">{current.processingFailureMessage}</span>}</div>
                </section>

                <section className="panel section-card"><div className="section-heading"><div><p className="eyebrow">HUMAN REVIEW</p><h3>Effective information</h3></div><span className="small-meta">Version {current.version}</span></div>
                  {current.status === "PendingReview" && <p className="muted callout">Start review to claim this item before making changes.</p>}
                  {current.status === "Resolved" && <p className="success-box callout">Resolved by {current.resolvedBy} · {current.resolvedAt ? formatDate(current.resolvedAt) : ""}</p>}
                  <form onSubmit={submitReview}>
                    <div className="form-row"><label>Category<select value={category} onChange={event => setCategory(event.target.value)} disabled={!canEdit}>{categories.map(value => <option key={value}>{value}</option>)}</select></label><label>Priority<select value={priority} onChange={event => setPriority(event.target.value)} disabled={!canEdit}>{priorities.map(value => <option key={value}>{value}</option>)}</select></label></div>
                    <label>Reviewer notes<textarea rows={3} maxLength={2000} value={notes} onChange={event => setNotes(event.target.value)} disabled={!canEdit} placeholder="Add a bounded internal note…" /></label>
                    <label>Missing information <span className="label-hint">One item per line</span><textarea rows={3} maxLength={2400} value={missingInformation} onChange={event => setMissingInformation(event.target.value)} disabled={!canEdit} placeholder="What still needs clarification?" /></label>
                    <div className="facts-heading"><strong>Extracted business facts</strong><span className="muted">Verify values before downstream use.</span></div>
                    {current.facts.length === 0 && <p className="muted">No business facts were extracted.</p>}
                    <div className="facts-list">{current.facts.map(fact => <div className="fact-row" key={fact.id}><div className="fact-meta"><span className={`fact-state fact-${fact.status.toLowerCase()}`}>{fact.status}</span><strong>{fact.type}</strong><small>AI: {fact.originalValue}</small></div><input aria-label={`Reviewed ${fact.type}`} value={factValues[fact.id] ?? fact.value} maxLength={300} disabled={!canEdit || fact.status === "Rejected"} onChange={event => setFactValues({ ...factValues, [fact.id]: event.target.value })} />{fact.value !== (factValues[fact.id] ?? fact.value) && <small className="label-hint" role="status">Save the correction before verifying this fact.</small>}{fact.status !== "Rejected" && <div className="fact-actions"><button type="button" className="mini-button" disabled={!canEdit || !canVerifyFact(fact.status, fact.value, factValues[fact.id] ?? fact.value)} onClick={() => void act(`${itemBase}/facts/${fact.id}/verify`, { expectedVersion: current.version }, "POST", "Fact verified.")}>Verify</button><button type="button" className="mini-button" disabled={!canEdit || !factValues[fact.id]?.trim()} onClick={() => void act(`${itemBase}/facts/${fact.id}`, { expectedVersion: current.version, value: factValues[fact.id] }, "PATCH", "Fact correction saved; verification is required again.")}>Correct</button><button type="button" className="mini-button danger-text" disabled={!canEdit || fact.status !== "Unverified"} onClick={() => void act(`${itemBase}/facts/${fact.id}/reject`, { expectedVersion: current.version }, "POST", "Unsupported fact rejected.")}>Reject</button></div>}</div>)}</div>
                    <div className="form-actions"><button className="secondary" type="submit" disabled={!canEdit}>Save review</button><span className="muted">Changes are recorded with your reviewer identity.</span></div>
                  </form>
                  {current.status === "InReview" && <div className="resolve-row"><span>{unverifiedCount ? `${unverifiedCount} fact(s) still need a decision.` : "All extracted facts have a review decision."}</span><button className="secondary" disabled={!canEdit || unverifiedCount > 0} onClick={() => void act(`${itemBase}/resolve`, { expectedVersion: current.version }, "POST", "Work item resolved.")}>Resolve item</button></div>}
                </section>

                <section className="panel section-card"><div className="section-heading"><div><p className="eyebrow">INTERNAL ONLY</p><h3>Response draft</h3></div>{revision && <span className={`status-badge status-${revision.status.toLowerCase()}`}>{revision.status} · v{revision.revision}</span>}</div>
                  <p className="muted draft-notice">Approval does not send an email or create an external draft.</p>
                  {current.draft && <p className="recipient-line">Prepared for <strong>{current.draft.recipientEmail}</strong> from the original sender.</p>}
                  <form onSubmit={saveDraft}>
                    <label>Subject<input value={draftSubject} onChange={event => setDraftSubject(event.target.value)} maxLength={998} disabled={!canEdit} placeholder="Response subject" /></label>
                    <label>Body<textarea rows={8} value={draftBody} onChange={event => setDraftBody(event.target.value)} maxLength={20000} disabled={!canEdit} placeholder="Write a response for reviewer approval…" /></label>
                    <div className="draft-actions"><button className="secondary" type="submit" disabled={!canEdit || !draftSubject.trim() || !draftBody.trim()}>Save new revision</button>{session.aiDraftingEnabled && <button className="secondary" type="button" disabled={!canEdit} onClick={() => void act(`${itemBase}/draft/generate`, { expectedVersion: current.version }, "POST", "AI proposal saved as an unapproved draft revision.")}>Propose with AI</button>}</div>
                  </form>
                  {revision && revision.status === "Draft" && !canApproveDraft(revision.status, revision.subject, revision.body, draftSubject, draftBody) && <p className="muted callout" role="status">Unsaved changes are not part of the saved revision. Save a new revision before approval.</p>}
                  {revision && <div className="decision-box"><label>Decision feedback<textarea rows={2} maxLength={500} value={draftFeedback} onChange={event => setDraftFeedback(event.target.value)} disabled={!canEdit || revision.status !== "Draft"} placeholder="Optional reason for rejection" /></label><div className="draft-actions"><button className="primary" disabled={!canEdit || !canApproveDraft(revision.status, revision.subject, revision.body, draftSubject, draftBody)} onClick={() => void act(`${itemBase}/draft/approve`, { expectedVersion: current.version, revision: revision.revision }, "POST", "Draft approved for a future separately controlled workflow.")}>Approve revision</button><button className="danger-button" disabled={!canEdit || revision.status !== "Draft"} onClick={() => void act(`${itemBase}/draft/reject`, { expectedVersion: current.version, revision: revision.revision, feedback: draftFeedback }, "POST", "Draft rejected.")}>Reject revision</button></div>{revision.decidedAt && <p className="small-meta">{revision.status} by {revision.decidedBy} · {formatDate(revision.decidedAt)}{revision.feedback ? ` · ${revision.feedback}` : ""}</p>}{revision.generationMetrics && <p className="small-meta">Generated with {revision.generationMetrics.providerId} / {revision.generationMetrics.model}{revision.generationMetrics.outputTokens !== null ? ` · ${revision.generationMetrics.outputTokens} output tokens` : ""}{revision.generationMetrics.estimatedCostUsd !== null ? ` · estimated $${revision.generationMetrics.estimatedCostUsd.toFixed(4)}` : ""}</p>}</div>}
                </section>
              </div>

              <aside className="history-column"><section className="panel section-card history-card"><div className="section-heading"><div><p className="eyebrow">ACCOUNTABILITY</p><h3>Review history</h3></div><button className="icon-button" aria-label="Refresh review history" onClick={reload}>↻</button></div>{history.status === "loading" && <p className="muted">Loading history…</p>}{history.status === "error" && <p className="form-error">{history.message}</p>}{history.status === "ready" && history.value.length === 0 && <p className="muted">No review actions recorded yet.</p>}{history.status === "ready" && <ol className="timeline">{history.value.map(entry => <li key={entry.id}><span className="timeline-dot"/><div><strong>{entry.action.replaceAll(/([a-z])([A-Z])/g, "$1 $2")}</strong><p>{entry.summary}</p><small>{entry.actorId} · {formatDate(entry.occurredAt)}</small>{entry.revision && <small>Revision {entry.revision}</small>}</div></li>)}</ol>}</section></aside>
            </div>
          </>}
        </section>
      </div>
    </main>
  );
}
