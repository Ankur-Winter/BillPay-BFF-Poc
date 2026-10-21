import { useEffect, useState } from "react";

const bff = import.meta.env.VITE_BFF_ORIGIN || "http://localhost:5100";
const api = bff + "/api/service-b";

async function getJson(url, init = {}) {
  const response = await fetch(url, { credentials: "include", ...init });
  const body = await response.json().catch(() => ({}));
  if (response.status === 401) return null;
  if (!response.ok) throw new Error(body.detail || body.error || "Request failed (" + response.status + ")");
  return body;
}

export default function App() {
  const [user, setUser] = useState(null);
  const [transactions, setTransactions] = useState([]);
  const [preview, setPreview] = useState(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  async function load() {
    setLoading(true);
    setError("");
    try {
      const session = await getJson(bff + "/auth/me");
      setUser(session);
      if (session) setTransactions(await getJson(api + "/transactions") || []);
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { load(); }, []);

  async function logout() {
    const csrf = await getJson(bff + "/auth/csrf");
    if (!csrf?.requestToken) return;
    await fetch(bff + "/auth/logout", {
      method: "POST",
      credentials: "include",
      headers: { "X-CSRF-TOKEN": csrf.requestToken }
    });
    setUser(null);
    setTransactions([]);
    setPreview(null);
  }

  async function createPreview(event) {
    event.preventDefault();
    setError("");
    setPreview(null);
    try {
      const csrf = await getJson(bff + "/auth/csrf");
      const result = await getJson(api + "/transfer-preview", {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": csrf.requestToken },
        body: JSON.stringify({ fromAccount: "A-1001", toAccount: "A-1002", amount: 25 })
      });
      setPreview(result);
    } catch (err) {
      setError(err.message);
    }
  }

  return (
    <main className="shell">
      <header className="topbar">
        <a className="brand" href="/">NORTHSTAR <span>Bank</span></a>
        <span className="service-pill">SERVICE B</span>
        {user && <button className="quiet-button" onClick={logout}>Sign out</button>}
      </header>
      <section className="hero">
        <p className="eyebrow">BACKEND FOR FRONTEND DEMO</p>
        <h1>Activity with<br /><em>a server-side session.</em></h1>
        <p className="lede">This second React app shares the same BFF. A route key selects Service B, while the BFF adds the user's access token on the server-to-server call.</p>
      </section>
      <section className="panel">
        <div className="panel-heading">
          <div><p className="eyebrow">SERVICE B API</p><h2>Recent activity</h2></div>
          {user && <span className="user-chip">{user.name || user.subject}</span>}
        </div>
        {loading ? <p className="muted">Checking your BFF session…</p> : !user ? (
          <div className="empty-state">
            <p>Sign in through the shared BFF to view the sample activity.</p>
            <a className="primary-button" href={bff + "/auth/login?app=service-b"}>Continue to secure sign in</a>
          </div>
        ) : (
          <>
            <div className="transaction-list">
              {transactions.map((item) => (
                <article className="transaction-row" key={item.transactionId}>
                  <div><span className="transaction-date">{item.date}</span><h3>{item.description}</h3></div>
                  <strong className={item.amount < 0 ? "debit" : "credit"}>{new Intl.NumberFormat("en-US", { style: "currency", currency: item.currency }).format(item.amount)}</strong>
                </article>
              ))}
            </div>
            <div className="demo-action">
              <div><span className="eyebrow">CSRF-PROTECTED POST</span><p>Ask Service B for a transfer preview.</p></div>
              <button className="primary-button" onClick={createPreview}>Create preview</button>
            </div>
            {preview && <pre className="preview">{JSON.stringify(preview, null, 2)}</pre>}
          </>
        )}
        {error && <p className="error" role="alert">{error}</p>}
      </section>
      <footer><span>POC DATA ONLY</span><span>The transfer preview does not move money.</span></footer>
    </main>
  );
}
