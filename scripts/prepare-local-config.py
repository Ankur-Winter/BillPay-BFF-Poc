#!/usr/bin/env python3
"""Generate untracked, random credentials for the local banking BFF demo."""

from __future__ import annotations

import json
import secrets
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ENV_FILE = ROOT / ".env"
REALM_TEMPLATE = ROOT / "infrastructure" / "keycloak" / "bank-demo-realm.json"
GENERATED_DIR = ROOT / "infrastructure" / "keycloak" / "generated"
GENERATED_REALM = GENERATED_DIR / "bank-demo-realm.json"


def read_env(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    if not path.exists():
        return values
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        values[key.strip()] = value.strip().strip("\"'")
    return values


def ensure_random(values: dict[str, str], key: str) -> None:
    value = values.get(key, "")
    if not value or value.startswith("REPLACE_") or value.startswith("GENERATE_"):
        values[key] = secrets.token_urlsafe(32)


def main() -> None:
    values = read_env(ENV_FILE)
    values.setdefault("KEYCLOAK_ADMIN", "admin")
    for key in ("KEYCLOAK_ADMIN_PASSWORD", "BFF_CLIENT_SECRET", "DEMO_USER_PASSWORD"):
        ensure_random(values, key)

    env_content = "".join(f"{key}={value}\n" for key, value in values.items())
    ENV_FILE.write_text(env_content, encoding="utf-8")
    try:
        ENV_FILE.chmod(0o600)
    except OSError:
        pass

    realm = json.loads(REALM_TEMPLATE.read_text(encoding="utf-8"))
    for client in realm.get("clients", []):
        if client.get("clientId") == "bank-bff":
            client["secret"] = values["BFF_CLIENT_SECRET"]
    for user in realm.get("users", []):
        if user.get("username") == "demo":
            for credential in user.get("credentials", []):
                if credential.get("type") == "password":
                    credential["value"] = values["DEMO_USER_PASSWORD"]

    GENERATED_DIR.mkdir(parents=True, exist_ok=True)
    GENERATED_REALM.write_text(json.dumps(realm, indent=2) + "\n", encoding="utf-8")
    print("Generated local credentials in .env and the ignored Keycloak import file.")
    print("Keep both files private; they are excluded from Git.")


if __name__ == "__main__":
    main()
