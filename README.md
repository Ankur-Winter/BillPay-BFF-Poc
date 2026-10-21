# Banking BFF POC

A local proof of concept for two independent bank deployments using the same .NET BFF codebase. Bank A and Bank B each have a React frontend, a BFF instance, a downstream API, and a separate Keycloak realm. The BFF image is built from the same source for both banks; configuration and runtime state are isolated.

## Architecture

~~~mermaid
flowchart LR
  subgraph A[Bank A boundary]
    AWeb[React Bank A<br/>localhost:5173] -->|BankASession cookie| ABFF[Shared BFF code<br/>instance A: localhost:5101]
    ABFF -->|Bank A access token| AAPI[Bank A API]
    ABFF <-->|OIDC| AIdP[Keycloak realm bank-a]
  end
  subgraph B[Bank B boundary]
    BWeb[React Bank B<br/>localhost:5174] -->|BankBSession cookie| BBFF[Shared BFF code<br/>instance B: localhost:5102]
    BBFF -->|Bank B access token| BAPI[Bank B API]
    BBFF <-->|OIDC| BIdP[Keycloak realm bank-b]
  end
  Keycloak[Keycloak process]
  Keycloak --- AIdP
  Keycloak --- BIdP
~~~

The local demo models separate bank deployments: both BFF containers run the same `billpay-bff-poc:local` image, while each has its own session cookie name, OIDC client, issuer/realm, user, service scope, and configured API destination. The Compose network also places each BFF only on the network for its own API and Keycloak. The same BFF code can therefore be deployed with different bank-specific configuration without adding bank-specific routing logic to the BFF implementation.

The browser holds an opaque HttpOnly session cookie. OAuth tokens remain in that bank's BFF process and are attached only to its downstream API request. Each API independently validates the expected issuer, audience, and bank-specific role.

## Run locally

Requirements: Docker Compose, Python 3, and a browser. The container builds use .NET 10 and Node 22.

1. Generate private credentials and realm imports:

   ~~~sh
   python3 scripts/prepare-local-config.py
   ~~~

   This creates a private `.env` plus ignored Keycloak import files for both realms. Bank A and Bank B have different BFF client secrets and user passwords. The demo usernames are `bank-a-user` and `bank-b-user`; their generated passwords are in `.env` as `BANK_A_USER_PASSWORD` and `BANK_B_USER_PASSWORD`.

2. If an older version of this demo is running, remove its containers first so Keycloak imports the two new realms:

   ~~~sh
   docker compose down --remove-orphans
   ~~~

3. Build and start the demo:

   ~~~sh
   docker compose up --build
   ~~~

4. Wait until both realm discovery documents return JSON:

   - [Bank A OIDC discovery](http://keycloak.localhost:8080/realms/bank-a/.well-known/openid-configuration)
   - [Bank B OIDC discovery](http://keycloak.localhost:8080/realms/bank-b/.well-known/openid-configuration)

5. Open either bank app and sign in with that bank's demo user:

   - [Bank A](http://localhost:5173) — `bank-a-user`
   - [Bank B](http://localhost:5174) — `bank-b-user`

## Verify the isolation

1. Sign in to Bank A. In browser developer tools, confirm the browser uses the `BankASession` cookie for BFF A (`localhost:5101`).
2. Open Bank B in the same browser. It should show the signed-out view; its `/auth/me` call to BFF B (`localhost:5102`) should return `401` until Bank B is signed into separately.
3. Sign into Bank B using `bank-b-user`. Bank B then uses `BankBSession`; Bank A remains signed in with its own session.
4. Try Bank A credentials on Bank B's Keycloak login page, and vice versa. The separate realms should reject the other bank's account.
5. Inspect network requests: Bank A calls `localhost:5101`, Bank B calls `localhost:5102`; neither frontend sends an `Authorization: Bearer` header. Each BFF is configured with only its own service key and API URL.
6. While signed into Bank A, a request to `/api/service-b/transactions` on BFF A (or, while signed into Bank B, `/api/service-a/accounts` on BFF B) should return `404` because that service is not configured in that BFF deployment. Each API also rejects tokens issued by the other realm because its issuer and audience do not match.

Note: browsers scope cookies to a host, not a port. Both local BFFs use `localhost`, so the browser may send both named cookies to either port. Each BFF reads only its own cookie name. In a real deployment, each bank should use its own hostname and deployment boundary as well as independent session storage, OIDC configuration, keys, and downstream network access.

## Local endpoints

| Component | URL |
| --- | --- |
| React Bank A | http://localhost:5173 |
| Bank A BFF | http://localhost:5101 |
| Bank A API | Internal to the Bank A Compose network |
| React Bank B | http://localhost:5174 |
| Bank B BFF | http://localhost:5102 |
| Bank B API | Internal to the Bank B Compose network |
| Keycloak | http://keycloak.localhost:8080 |
| Bank A realm | http://keycloak.localhost:8080/realms/bank-a |
| Bank B realm | http://keycloak.localhost:8080/realms/bank-b |

## POC limitations

The local demo intentionally uses HTTP, development credentials, fake banking data, one Keycloak process hosting two separate realms, one BFF process per bank, in-memory BFF ticket stores, and one-hour access tokens without refresh. It demonstrates tenant/session isolation and the BFF request flow; it is not a production banking security baseline.

Before a bank deployment, use HTTPS, bank-managed identity configuration and secrets, separate deployment/network boundaries, an encrypted shared ticket store and shared ASP.NET Data Protection keys for each bank's BFF replicas, token refresh/revocation and identity-provider logout, audit logging, rate and request-size limits, and operational monitoring. Keep user-, account-, and action-level authorization in each API even though requests arrive through a BFF. Protect the frontend against XSS and compromised dependencies: a BFF keeps access tokens out of JavaScript but cannot stop malicious JavaScript from using the active browser session to send requests.
