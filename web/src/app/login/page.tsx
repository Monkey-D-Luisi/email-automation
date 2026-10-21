"use client";

import { useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { ApiError, signIn } from "../../lib/api";

export default function LoginPage() {
  const router = useRouter();
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError("");
    try {
      await signIn(password);
      router.replace("/");
      router.refresh();
    } catch (cause) {
      setError(cause instanceof ApiError && cause.status === 401 ? "The password was not accepted." : cause instanceof Error ? cause.message : "Sign-in failed.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="login-shell">
      <form className="login-card" onSubmit={submit}>
        <div className="brand-mark" aria-hidden="true">SI</div>
        <p className="eyebrow">SMART INBOX</p>
        <h1>Reviewer sign in</h1>
        <p className="muted">Use the local reviewer password configured for this demo.</p>
        <label htmlFor="password">Password</label>
        <input id="password" type="password" autoComplete="current-password" value={password} onChange={event => setPassword(event.target.value)} required maxLength={256} />
        {error && <p className="form-error" role="alert">{error}</p>}
        <button className="primary full-width" type="submit" disabled={busy}>{busy ? "Signing in…" : "Sign in"}</button>
      </form>
    </main>
  );
}
