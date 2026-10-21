# Banking BFF POC

A small local proof of concept for two React clients, two .NET APIs, and one reusable .NET Backend-for-Frontend.

## Architecture

~~~mermaid
flowchart LR
  A["React Service A<br/>localhost:5173"] -->|"HttpOnly session cookie"| BFF["Shared .NET BFF<br/>localhost:5100"]
  B["React Service B<br/>localhost:5174"] -->|"HttpOnly session cookie"| BFF
  BFF <-->|"OIDC code + PKCE"| IDP["Keycloak<br/>keycloak.localhost:8080"]
  BFF -->|"Bearer access token"| APIA["Service A .NET API"]
  BFF -->|"Bearer access token"| APIB["Service B .NET API"]
~~~

The React apps call the same BFF origin. The URL route key selects the downstream service: /api/service-a/... or /api/service-b/.... The BFF maps that key to a configured backend URL; the browser cannot choose the URL or override it with a header. This route key identifies the requested backend, not the UI that originated the request; browser-supplied origin or source headers are not authentication. The APIs independently validate the token's issuer, audience, and service-specific role.

The browser receives an opaque, HttpOnly session cookie. ASP.NET Core's cookie ITicketStore stores the authentication ticket, including OAuth tokens, in the BFF process. The access token is attached to the server-to-server API request and is never returned to React. Both frontends share the same BFF session on localhost.

## Run locally

Requirements: Docker Compose and a browser. The container builds use .NET 10 and Node 22.

1. Generate private local credentials and the Keycloak realm import:

   ~~~sh
   python3 scripts/prepare-local-config.py
   ~~~

   This writes a private `.env` and a generated realm file under an ignored directory. The script creates random Keycloak admin, BFF client, and demo user passwords; they are not stored in Git.
2. Start the sample:

   ~~~sh
   docker compose up --build
   ~~~

3. Wait until the [Keycloak realm discovery document](http://keycloak.localhost:8080/realms/bank-demo/.well-known/openid-configuration) returns JSON, then open [Service A](http://localhost:5173) or [Service B](http://localhost:5174).
4. Choose **Continue to secure sign in**. The local demo account is `demo`; its generated password is in the private `.env` file as `DEMO_USER_PASSWORD`.
5. Service A shows static example accounts. Service B shows static transactions and a CSRF-protected transfer preview. The preview never moves money.

Local endpoints:

| Component | URL |
| --- | --- |
| React Service A | http://localhost:5173 |
| React Service B | http://localhost:5174 |
| Shared BFF | http://localhost:5100 |
| Keycloak | http://keycloak.localhost:8080 |

Stop the containers with docker compose down. Realm data is generated from `infrastructure/keycloak/bank-demo-realm.json` when you run the local setup script. Browser-facing ports are bound to loopback, and the APIs are only on the internal Compose network.

## Useful request paths

- GET /auth/me — current BFF session, or 401.
- GET /auth/csrf — issues the antiforgery cookie and returns the request token used in X-CSRF-TOKEN.
- POST /auth/logout — clears the BFF session; requires the CSRF token.
- GET /api/service-a/accounts — proxied to Service A with a server-side bearer token.
- GET /api/service-b/transactions — proxied to Service B with a server-side bearer token.
- POST /api/service-b/transfer-preview — same proxy path and CSRF validation; returns a preview only.

The BFF's allow-listed service keys, destination URLs, and OAuth scopes are configured under Bff:Services in services/bff/appsettings.json (or environment variables in Compose). The BFF reads scopes and destinations from that section, so adding a downstream service does not require changing the proxy code. Add its identity-provider scope/audience and API-side authorization configuration as well.

## What to inspect in the browser

Open the browser's Application/Storage panel. BffSession is marked HttpOnly; JavaScript cannot read it. The browser's calls go to the BFF and contain a cookie, not an Authorization: Bearer header. The BFF's ticket store contains the OIDC access and refresh tokens in server memory, and the BFF adds the access token only on its call to a downstream API.

The local demo intentionally uses HTTP and development credentials, static fake banking data, an in-memory session store, and one BFF instance. It demonstrates the flow, not a production banking security baseline. The local access token is configured for one hour and this POC does not refresh it. The logout endpoint clears the BFF session but does not end the identity provider's SSO session. Before adapting it for a bank, integrate the bank's identity provider and policies; require HTTPS; use a shared, encrypted, expiry-aware ticket store and shared ASP.NET Data Protection keys; implement token refresh/revocation and identity-provider logout; add audit logging, rate limits, request-size limits, and operational monitoring; define user-, account-, and action-level authorization in the APIs; and configure deployment-specific trusted proxy IPs under ForwardedHeaders:KnownProxies, cookie, CORS, secrets, and network rules. The APIs must remain independently authorized even when all requests currently arrive through the BFF.

The BFF keeps OAuth tokens out of JavaScript, but it does not stop malicious JavaScript from making authenticated requests through a user's active session. Protect the frontends against XSS and compromised dependencies, and keep authorization and transaction controls in the APIs.

This sample does not implement transfer execution, transaction signing, consent, step-up authentication, fraud checks, idempotency, or any real account data.
