#!/usr/bin/env bash
# Mint a REAL user access token via Logto impersonation (token exchange),
# without any browser or password — for automated E2E tests against a live
# landscape. https://docs.logto.io/developers/user-impersonation
#
#   M2M creds ──▶ management token ──▶ POST /api/subject-tokens {userId}
#             ──▶ POST /oidc/token (grant_type=token-exchange) ──▶ user JWT
#
# The resulting token is indistinguishable from a normal login token: same
# issuer, audience and claims — zinc's auth accepts it as the user.
#
# Required env:
#   LOGTO_ENDPOINT        e.g. https://api.lithium.alcohol.pichu.cluster.atomi.cloud
#   LOGTO_M2M_ID          management M2M app id   (zinc's Auth.Management.Id works)
#   LOGTO_M2M_SECRET      management M2M app secret
#   EXCHANGE_APP_ID       app used for the exchange. On lithium's current Logto
#                         version, USER-FACING apps (Native/SPA/Traditional)
#                         accept the token-exchange grant out of the box, while
#                         M2M apps reject it — use e.g. the alcohol.neon Native
#                         app id. Verified on pichu 2026-07-04.
#   EXCHANGE_APP_SECRET   its secret (omit for public apps like Native)
#   API_RESOURCE          audience, e.g. https://api.zinc.alcohol.pichu
#   USER_ID               Logto user id to impersonate (or set USER_EMAIL)
# Optional:
#   USER_EMAIL            resolved to USER_ID via the Management API
#   MGMT_RESOURCE         default https://default.logto.app/api (self-hosted)
#   SCOPES                extra scopes for the user token (space-separated)
#
# Output: the user access token on stdout (nothing else), so callers can do
#   TOKEN=$(scripts/e2e/get-user-token.sh)
set -euo pipefail

: "${LOGTO_ENDPOINT:?}" "${LOGTO_M2M_ID:?}" "${LOGTO_M2M_SECRET:?}"
: "${EXCHANGE_APP_ID:?}" "${API_RESOURCE:?}"
MGMT_RESOURCE="${MGMT_RESOURCE:-https://default.logto.app/api}"

err() {
  echo "get-user-token: $*" >&2
  exit 1
}
need() { command -v "$1" >/dev/null || err "missing dependency: $1"; }
need curl
need jq

# 1. Management token (client credentials) — the part that IS automatable today.
mgmt_token=$(curl -sf -X POST "$LOGTO_ENDPOINT/oidc/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -u "$LOGTO_M2M_ID:$LOGTO_M2M_SECRET" \
  --data-urlencode "grant_type=client_credentials" \
  --data-urlencode "resource=$MGMT_RESOURCE" \
  --data-urlencode "scope=all" | jq -r .access_token) || err "management token request failed"
if [ -z "$mgmt_token" ] || [ "$mgmt_token" = "null" ]; then err "no management token in response"; fi

# 2. Resolve email -> user id if needed.
if [ -z "${USER_ID:-}" ]; then
  : "${USER_EMAIL:?set USER_ID or USER_EMAIL}"
  USER_ID=$(curl -sf "$LOGTO_ENDPOINT/api/users?search=$(jq -rn --arg e "$USER_EMAIL" '$e|@uri')" \
    -H "Authorization: Bearer $mgmt_token" |
    jq -r --arg e "$USER_EMAIL" '.[] | select(.primaryEmail==$e) | .id' | head -1)
  [ -n "$USER_ID" ] || err "no user found for $USER_EMAIL"
fi

# 3. Subject token for the target user (Management API; short-lived, single-use).
subject_token=$(curl -sf -X POST "$LOGTO_ENDPOINT/api/subject-tokens" \
  -H "Authorization: Bearer $mgmt_token" -H "Content-Type: application/json" \
  -d "{\"userId\": \"$USER_ID\"}" | jq -r .subjectToken) || err "subject-token request failed (Logto too old, or missing permission)"
if [ -z "$subject_token" ] || [ "$subject_token" = "null" ]; then err "no subjectToken in response"; fi

# 4. Exchange it for a real user access token scoped to the API resource.
auth_args=(-u "$EXCHANGE_APP_ID:${EXCHANGE_APP_SECRET:-}")
[ -z "${EXCHANGE_APP_SECRET:-}" ] && auth_args=(--data-urlencode "client_id=$EXCHANGE_APP_ID")
user_token=$(curl -sf -X POST "$LOGTO_ENDPOINT/oidc/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  "${auth_args[@]}" \
  --data-urlencode "grant_type=urn:ietf:params:oauth:grant-type:token-exchange" \
  --data-urlencode "subject_token=$subject_token" \
  --data-urlencode "subject_token_type=urn:ietf:params:oauth:token-type:access_token" \
  --data-urlencode "resource=$API_RESOURCE" \
  ${SCOPES:+--data-urlencode "scope=$SCOPES"} |
  jq -r .access_token) || err "token exchange failed (is the grant enabled on $EXCHANGE_APP_ID?)"
if [ -z "$user_token" ] || [ "$user_token" = "null" ]; then err "no access_token in exchange response"; fi

echo "$user_token"
