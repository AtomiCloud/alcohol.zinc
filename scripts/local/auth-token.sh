#!/usr/bin/env bash

file="$1"

# shellcheck disable=SC2124
scope=${@:2}
set -eou pipefail

[ "$file" = '' ] && file="./config/dev.yaml"

landscape="$(yq -r '.landscape' "$file")"
platform="$(yq -r '.platform' "$file")"
service="$(yq -r '.service' "$file")"

AUTH_RESOURCES="https://api.${service}.${platform}.${landscape}" infisical run --env "$landscape" -- ./scripts/local/get-auth-token.sh "$scope"
