# Manual HTTP Probes

These `.http` files are **manual smoke probes**, run by hand from the editor's REST
Client (e.g. the VS Code REST Client extension or JetBrains HTTP Client). They are
NOT automated tests and are not part of any `tests/` project or CI pipeline.

## Environment

`http-client.env.json` defines a `dev` environment with `host` and `token`
variables consumed by the `.http` files via `{{host}}` / `{{token}}`.

## Minting a token

Requests under `Authorization: Bearer {{token}}` need a real Supabase access
token for a dev user:

```
POST {supabase-project-url}/auth/v1/token?grant_type=password
apikey: <anon/publishable key>
Content-Type: application/json

{ "email": "<test-email>", "password": "<test-password>" }
```

Take `access_token` from the response and paste it into `http-client.env.json`
under `dev.token`.

## Never commit

`http-client.env.json` holds a live bearer token once filled in. The token,
the test email/password, and any Supabase key used to mint it must never be
committed. The file is gitignored — verify with `git status` before staging
anything in this directory.

## Story A expectation matrix

| Request | Condition | Expected |
|---|---|---|
| `GET /me` | no token | 401 |
| `GET /me` | valid dev-tenant token | 200 |
| `GET /context` | no token | 401 |
| `GET /context` | valid dev-tenant token | 200 |
| `GET /context` | valid token, forged/cross-tenant site | 403 |
