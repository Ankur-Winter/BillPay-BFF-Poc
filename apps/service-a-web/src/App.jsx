import { useEffect, useState } from "react";

const bff = import.meta.env.VITE_BFF_ORIGIN || "http://localhost:5100";
const appId = import.meta.env.VITE_APP_ID || "service-a";
const api = bff + "/api/" + appId;

async function getJson(url, init = {}) {
  const response = await fetch(url, { credentials: "include", ...init });
  if (response.status === 401) return null;
  const body = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(body.detail || body.error || "Request failed (" + response.status + ")");
  return body;
}

export default function App() {
  const [user, setUser] = useState(null);
  const [accounts, setAccounts] = useState([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  async function load() {
    setLoading(true);
    setError("");
    try {
      const session = await getJson(bff + "/auth/me");
      setUser(session);
      if (session) setAccounts(await getJson(api + "/accounts") || []);
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
    setAccounts([]);
  }

  return (
    <main className="shell">
      <header className="topbar">
        <a className="brand" href="/">NORTHSTAR <span>Bank</span></a>
        <span className="service-pill">SERVICE A</span>
        {user && <button className="quiet-button" onClick={logout}>Sign out</button>}
      </header>
      <section className="hero">
        <p className="eyebrow">BACKEND FOR FRONTEND DEMO</p>
        <h1>Your accounts,<br /><em>kept behind the BFF.</em></h1>
        <p className="lede">This React app calls one shared .NET BFF. The BFF keeps the OAuth tokens server-side and routes the request to Service A.</p>
      </section>
      <section className="panel">
        <div className="panel-heading">
          <div><p className="eyebrow">SERVICE A API</p><h2>Accounts</h2></div>
          {user && <span className="user-chip">{user.name || user.subject}</span>}
        </div>
        {loading ? <p className="muted">Checking your BFF session…</p> : !user ? (
          <div className="empty-state">
            <p>Sign in through the BFF to view the sample account data.</p>
            <a className="primary-button" href={bff + "/auth/login?app=" + encodeURIComponent(appId)}>Continue to secure sign in</a>
          </div>
        ) : (
          <div className="account-list">
            {accounts.map((account) => (
              <article className="account-row" key={account.accountId}>
                <div><span className="account-type">{account.accountType}</span><h3>{account.accountId}</h3></div>
                <div className="balance"><span>Available balance</span><strong>{new Intl.NumberFormat("en-US", { style: "currency", currency: account.currency }).format(account.availableBalance)}</strong></div>
              </article>
            ))}
          </div>
        )}
        {error && <p className="error" role="alert">{error}</p>}
      </section>
      <footer><span>POC DATA ONLY</span><span>Browser holds a session cookie, never an access token.</span></footer>
    </main>
  );
}
