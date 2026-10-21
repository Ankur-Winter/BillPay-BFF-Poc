#!/usr/bin/env python3
"""Generate untracked, random credentials for the local banking BFF demo."""

from __future__ import annotations

import json
import secrets
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ENV_FILE = ROOT / ".env"
REALMS = {
    "bank-a": {
        "template": ROOT / "infrastructure" / "keycloak" / "bank-a-realm.json",
        "client_id": "bank-a-bff",
        "client_secret_key": "BANK_A_BFF_CLIENT_SECRET",
        "username": "bank-a-user",
        "password_key": "BANK_A_USER_PASSWORD",
    },
    "bank-b": {
        "template": ROOT / "infrastructure" / "keycloak" / "bank-b-realm.json",
        "client_id": "bank-b-bff",
        "client_secret_key": "BANK_B_BFF_CLIENT_SECRET",
        "username": "bank-b-user",
        "password_key": "BANK_B_USER_PASSWORD",
    },
}
GENERATED_DIR = ROOT / "infrastructure" / "keycloak" / "generated"


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
    for key in (
        "KEYCLOAK_ADMIN_PASSWORD",
        "BANK_A_BFF_CLIENT_SECRET",
        "BANK_B_BFF_CLIENT_SECRET",
        "BANK_A_USER_PASSWORD",
        "BANK_B_USER_PASSWORD",
    ):
        ensure_random(values, key)

    env_content = "".join(f"{key}={value}\n" for key, value in values.items())
    ENV_FILE.write_text(env_content, encoding="utf-8")
    try:
        ENV_FILE.chmod(0o600)
    except OSError:
        pass

    GENERATED_DIR.mkdir(parents=True, exist_ok=True)
    try:
        GENERATED_DIR.chmod(0o700)
    except OSError:
        pass

    for realm_name, config in REALMS.items():
        realm = json.loads(config["template"].read_text(encoding="utf-8"))
        for client in realm.get("clients", []):
            if client.get("clientId") == config["client_id"]:
                client["secret"] = values[config["client_secret_key"]]
        for user in realm.get("users", []):
            if user.get("username") == config["username"]:
                for credential in user.get("credentials", []):
                    if credential.get("type") == "password":
                        credential["value"] = values[config["password_key"]]

        generated_realm = GENERATED_DIR / f"{realm_name}-realm.json"
        generated_realm.write_text(json.dumps(realm, indent=2) + "\n", encoding="utf-8")
        try:
            generated_realm.chmod(0o600)
        except OSError:
            pass

    print("Generated independent Bank A and Bank B local credentials and Keycloak realms.")
    print("Keep both files private; they are excluded from Git.")


if __name__ == "__main__":
    main()
