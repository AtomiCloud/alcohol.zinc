#!/usr/bin/env bash

# shellcheck disable=SC2124
scopes="$1"

set -eou pipefail

curl -X POST "${AUTH_DOMAIN}/oidc/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials" \
  -d "client_id=${AUTH_CLIENT_ID}" \
  -d "client_secret=${AUTH_CLIENT_SECRET}" \
  -d "resource=${AUTH_RESOURCES}" \
  -d "scope=$scopes" | jq
