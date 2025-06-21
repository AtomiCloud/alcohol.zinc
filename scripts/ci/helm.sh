#!/usr/bin/env bash

[ "${DOMAIN}" = '' ] && echo "❌ 'DOMAIN' env var not set" && exit 1

[ "${DOCKER_PASSWORD}" = '' ] && echo "❌ 'DOCKER_PASSWORD' env var not set" && exit 1
[ "${DOCKER_USER}" = '' ] && echo "❌ 'DOCKER_USER' env var not set" && exit 1

[ "${GITHUB_SHA}" = '' ] && echo "❌ 'GITHUB_SHA' env var not set" && exit 1
[ "${GITHUB_BRANCH}" = '' ] && echo "❌ 'GITHUB_BRANCH' env var not set" && exit 1
[ "${GITHUB_REPO_REF}" = '' ] && echo "❌ 'GITHUB_REPO_REF' env var not set" && exit 1

chart_path="$1"
SEMVER_VERSION="$2"

set -euo pipefail

SHA="$(echo "${GITHUB_SHA}" | head -c 6)"
# shellcheck disable=SC2001
BRANCH="$(echo "${GITHUB_BRANCH}" | sed 's/[._-]*$//')"
COMMIT_VERSION="${SHA}-${BRANCH}"

if [ "${SEMVER_VERSION}" = '' ]; then
  HELM_VERSION="v0.0.0-${COMMIT_VERSION}"
  IMAGE_VERSION="${COMMIT_VERSION}"
else
  HELM_VERSION="${SEMVER_VERSION}"
  IMAGE_VERSION="${SEMVER_VERSION}"
fi

echo "📝 Generating Image tags..."
echo "📝 Helm version: ${HELM_VERSION}"
echo "📝 Image version: ${IMAGE_VERSION}"

onExit() {
  rc="$?"
  rm -rf ./uploads
  if [ "$rc" = '0' ]; then
    echo "✅ Successfully published helm chart"
  else
    echo "❌ Failed to publish helm chart"
  fi
}
trap onExit EXIT

cd "${chart_path}" || exit

# login to registry
echo "🔐 Logging into docker registry..."
echo "${DOCKER_PASSWORD}" | helm registry login "${DOMAIN}" -u "${DOCKER_USER}" --password-stdin

# packaging helm chart
echo "📦 Packaging helm chart..."
helm dependency build
helm package . -u --version "${HELM_VERSION}" --app-version "${IMAGE_VERSION}" -d ./uploads

# push helm chart
echo "📤 Pushing helm chart..."
for filename in ./uploads/*.tgz; do
  OCI_REF="$(echo "oci://${DOMAIN}/${GITHUB_REPO_REF}" | tr '[:upper:]' '[:lower:]')"
  echo "📤 Pushing ${filename} to ${OCI_REF}"
  helm push "$filename" "${OCI_REF}"
done
