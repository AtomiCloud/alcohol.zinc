# E2E test tooling

## `get-user-token.sh` — real user tokens without a browser

Mints a genuine Logto user access token via impersonation
([docs](https://docs.logto.io/developers/user-impersonation)): M2M creds →
management token → subject token for the target user → OAuth token exchange.
zinc cannot tell the result apart from a normal login token.

### Usage

```bash
TOKEN=$(
  LOGTO_ENDPOINT=https://api.lithium.alcohol.pichu.cluster.atomi.cloud \
  LOGTO_M2M_ID=<management m2m app id> \
  LOGTO_M2M_SECRET=<management m2m secret> \
  EXCHANGE_APP_ID=<user-facing app id, e.g. the alcohol.neon Native app> \
  API_RESOURCE=https://api.zinc.alcohol.pichu \
  USER_EMAIL=testuser@lazytax.club \
  scripts/e2e/get-user-token.sh
)
curl -H "Authorization: Bearer $TOKEN" \
  https://api.zinc.alcohol.pichu.cluster.atomi.cloud/api/v1.0/Subscription/<userId>
```

Notes:

- On lithium's current Logto version, the token-exchange grant works for
  **user-facing** apps (Native/SPA/Traditional) and is rejected for M2M apps.
  Native apps are public clients — `EXCHANGE_APP_SECRET` is not needed.
- The script prints ONLY the token on stdout; all diagnostics go to stderr.

### Security handling rules

The script contains no credentials, but it consumes one powerful secret and
emits one live token. Handle both accordingly:

1. **`LOGTO_M2M_SECRET` is the Management API key** — whoever holds it can
   administer all users. In CI, store it as a GitHub Actions **secret**
   (auto-masked); locally, pull it from Infisical into the environment, never
   into files or shell history.
2. **Never log the output token.** Capture it into a variable; in GitHub
   Actions, `echo "::add-mask::$TOKEN"` immediately after capture. Never run
   the script under `set -x`.
3. **Blast radius**: the token is short-lived and belongs to a dedicated test
   user — keep it that way. Do not impersonate real users in tests.
4. **pichu-only policy.** Do not wire raichu (production) management secrets
   into any automated job. Production impersonation must remain a deliberate,
   manual, audited act.

### Test identities (pichu)

| What | Value |
| ------------------ | ----------------------------------------- |
| Test user | `testuser@lazytax.club` (`tajahe2jbu4f`) |
| Exchange app | alcohol.neon Native (public) |
| Management M2M app | zinc's `Auth.Management` (Id from config, secret in Infisical `ATOMI_AUTH__MANAGEMENT__SECRET`) |
