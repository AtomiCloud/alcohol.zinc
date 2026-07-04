#!/usr/bin/env bash
# E2E for the neon->web handoff (POST /api/v1.0/Auth/web-handoff) against a
# live landscape (pichu). Proves the acceptance criteria that don't need a
# browser: the endpoint is authed, returns a well-formed magic-link URL whose
# one-time token is REAL and SINGLE-USE, and the CTA matrix resolves per
# storefront with restricted regions failing closed.
#
# Security: same rules as get-user-token.sh (see README.md) — the user token
# and the minted OTT are live credentials; neither is ever echoed. pichu only.
#
# Required env (same as get-user-token.sh):
#   LOGTO_ENDPOINT, LOGTO_M2M_ID, LOGTO_M2M_SECRET, EXCHANGE_APP_ID, API_RESOURCE
# Optional:
#   ZINC_ENDPOINT   default https://api.zinc.alcohol.pichu.cluster.atomi.cloud
#   USER_EMAIL      default testuser@lazytax.club
#   MGMT_RESOURCE   default https://default.logto.app/api
set -euo pipefail

: "${LOGTO_ENDPOINT:?}" "${LOGTO_M2M_ID:?}" "${LOGTO_M2M_SECRET:?}"
ZINC_ENDPOINT="${ZINC_ENDPOINT:-https://api.zinc.alcohol.pichu.cluster.atomi.cloud}"
USER_EMAIL="${USER_EMAIL:-testuser@lazytax.club}"
MGMT_RESOURCE="${MGMT_RESOURCE:-https://default.logto.app/api}"

pass=0
fail=0
ok() {
  echo "  ✅ $1" >&2
  pass=$((pass + 1))
}
ko() {
  echo "  ❌ $1" >&2
  fail=$((fail + 1))
}

need() { command -v "$1" >/dev/null || {
  echo "missing dependency: $1" >&2
  exit 1
}; }
need curl
need jq

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "== minting user token for $USER_EMAIL ==" >&2
TOKEN=$(USER_EMAIL="$USER_EMAIL" "$here/get-user-token.sh")
sub=$(echo "$TOKEN" | cut -d. -f2 | tr '_-' '/+' | {
  p=$(cat)
  pad=$(((4 - ${#p} % 4) % 4))
  printf '%s' "$p"
  if [ "$pad" -gt 0 ]; then printf '=%.0s' $(seq 1 "$pad"); fi
} | base64 -d 2>/dev/null | jq -r .sub)
if [ -z "$sub" ] || [ "$sub" = "null" ]; then
  echo "could not extract sub from token" >&2
  exit 1
fi
echo "== caller sub: $sub ==" >&2

echo "== management token (for OTT verification) ==" >&2
mgmt=$(curl -sf -X POST "$LOGTO_ENDPOINT/oidc/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -u "$LOGTO_M2M_ID:$LOGTO_M2M_SECRET" \
  --data-urlencode "grant_type=client_credentials" \
  --data-urlencode "resource=$MGMT_RESOURCE" \
  --data-urlencode "scope=all" | jq -r .access_token)

echo "== 1. POST /api/v1.0/Auth/web-handoff (ios, US) ==" >&2
res=$(curl -s -w '\n%{http_code}' -X POST "$ZINC_ENDPOINT/api/v1.0/Auth/web-handoff" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"platform": "ios", "storefront": "US"}')
code=$(echo "$res" | tail -1)
body=$(echo "$res" | sed '$d')
url=$(echo "$body" | jq -r '.url // empty')
if [ "$code" = "200" ] && [[ $url =~ ^https://.+/auth/handoff\?one_time_token=.+\&login_hint=.+\&redirect=.+$ ]]; then
  ok "handoff returned 200 with well-formed magic-link URL"
else
  ko "handoff failed: HTTP $code, url shape mismatch"
fi

ott=$(echo "$url" | sed -n 's/.*one_time_token=\([^&]*\).*/\1/p')
if [ -n "$ott" ]; then
  echo "== 2. OTT is real + single-use (verify twice on lithium) ==" >&2
  v1=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$LOGTO_ENDPOINT/api/one-time-tokens/verify" \
    -H "Authorization: Bearer $mgmt" -H "Content-Type: application/json" \
    -d "{\"email\": \"$USER_EMAIL\", \"token\": \"$ott\"}")
  v2=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$LOGTO_ENDPOINT/api/one-time-tokens/verify" \
    -H "Authorization: Bearer $mgmt" -H "Content-Type: application/json" \
    -d "{\"email\": \"$USER_EMAIL\", \"token\": \"$ott\"}")
  if [ "$v1" = "200" ]; then ok "OTT verified as real (200)"; else ko "OTT verify returned $v1"; fi
  if [ "$v2" != "200" ]; then ok "OTT is single-use (2nd verify: $v2)"; else ko "OTT verified twice — not single-use"; fi
fi

echo "== 3. CTA matrix ==" >&2
tier=$(curl -sf "$ZINC_ENDPOINT/api/v1.0/Subscription/$sub" -H "Authorization: Bearer $TOKEN" | jq -r .tier)
echo "   (caller tier: $tier)" >&2
cta_sg=$(curl -sf "$ZINC_ENDPOINT/api/v1.0/Subscription/$sub/cta?platform=ios&storefront=SG" \
  -H "Authorization: Bearer $TOKEN" | jq -r .variant)
cta_us=$(curl -sf "$ZINC_ENDPOINT/api/v1.0/Subscription/$sub/cta?platform=ios&storefront=US" \
  -H "Authorization: Bearer $TOKEN" | jq -r .variant)
if [ "$tier" = "free" ]; then
  if [ "$cta_sg" = "neutral" ]; then ok "free + SG storefront -> neutral"; else ko "free + SG -> $cta_sg (want neutral)"; fi
  if [ "$cta_us" = "subscribe" ]; then ok "free + US storefront -> subscribe"; else ko "free + US -> $cta_us (want subscribe)"; fi
else
  if [ "$cta_sg" = "manage" ]; then ok "paid + SG storefront -> manage"; else ko "paid + SG -> $cta_sg (want manage)"; fi
  if [ "$cta_us" = "manage" ]; then ok "paid + US storefront -> manage"; else ko "paid + US -> $cta_us (want manage)"; fi
fi

if [ "$tier" = "free" ]; then
  echo "== 3b. restricted-storefront handoff must be refused (403) ==" >&2
  refused=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$ZINC_ENDPOINT/api/v1.0/Auth/web-handoff" \
    -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
    -d '{"platform": "ios", "storefront": "SG"}')
  if [ "$refused" = "403" ]; then ok "free + SG handoff refused (403)"; else ko "free + SG handoff returned $refused (want 403)"; fi
fi

echo "== 4. own-account guard (CTA for another user must 403) ==" >&2
guard=$(curl -s -o /dev/null -w '%{http_code}' \
  "$ZINC_ENDPOINT/api/v1.0/Subscription/someone-else/cta?platform=ios&storefront=US" \
  -H "Authorization: Bearer $TOKEN")
if [ "$guard" = "403" ]; then ok "cross-user CTA rejected (403)"; else ko "cross-user CTA returned $guard (want 403)"; fi

echo "== 5. unauthenticated handoff must 401 ==" >&2
anon=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$ZINC_ENDPOINT/api/v1.0/Auth/web-handoff" \
  -H "Content-Type: application/json" -d '{"platform": "ios", "storefront": "US"}')
if [ "$anon" = "401" ]; then ok "anonymous handoff rejected (401)"; else ko "anonymous handoff returned $anon (want 401)"; fi

echo >&2
echo "== results: $pass passed, $fail failed ==" >&2
[ "$fail" = "0" ]
