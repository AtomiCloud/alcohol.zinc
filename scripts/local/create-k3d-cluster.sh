#!/usr/bin/env bash

dev_config="$1"

set -eou pipefail

[ "$dev_config" = '' ] && dev_config="./config/dev.yaml"

# check if dev config exists
if [ ! -f "$dev_config" ]; then
  echo "❌ Dev config '$dev_config' does not exist!"
  exit 1
fi

landscape="$(yq '.landscape' "$dev_config")"
secrets="$(yq '.secrets' "$dev_config")"
config="./infra/k3d.$landscape.yaml"
echo "🧬 Attempting to start cluster '$landscape' using '$config'..."

# obtain existing cluster
current="$(k3d cluster ls -o json | jq -r --arg landscape "${landscape}" '.[] | select(.name == $landscape) | .name')"
if [ "$current" = "$landscape" ]; then
  echo "✅ Cluster already exist!"
else
  # ask if to create cluster
  echo "🥟 Cluster does not exist, creating..."
  k3d cluster create "$landscape" --config "$config" --wait
  echo "🚀 Cluster created!"
fi

echo "🛠 Generating kubeconfig"
mkdir -p "$HOME/.kube/configs"
mkdir -p "$HOME/.kube/k3dconfigs"
mkdir -p "$HOME/.kube/atomiconfigs"

echo "📝 Writing to '$HOME/.kube/k3dconfigs/k3d-$landscape'"
k3d kubeconfig get "$landscape" >"$HOME/.kube/k3dconfigs/k3d-$landscape"

# Build KUBECONFIG from existing files only
cfgs=()
while IFS= read -r -d '' f; do cfgs+=("$f"); done < <(find "$HOME/.kube/configs" "$HOME/.kube/k3dconfigs" "$HOME/.kube/atomiconfigs" -maxdepth 1 -type f -print0 2>/dev/null)

KUBECONFIG="$(
  IFS=:
  echo "${cfgs[*]}"
)" kubectl config view --flatten >"$HOME/.kube/config"
chmod 600 ~/.kube/config

echo "✅ Generated kube config file"
# wait for cluster to be ready
echo "🕑 Waiting for cluster to be ready..."
kubectl --context "k3d-$landscape" -n kube-system wait --for=jsonpath=.status.readyReplicas=1 --timeout=300s deployment metrics-server
kubectl --context "k3d-$landscape" -n kube-system wait --for=jsonpath=.status.readyReplicas=1 --timeout=300s deployment coredns
kubectl --context "k3d-$landscape" -n kube-system wait --for=jsonpath=.status.readyReplicas=1 --timeout=300s deployment local-path-provisioner
kubectl --context "k3d-$landscape" -n kube-system wait --for=jsonpath=.status.succeeded=1 --timeout=300s job helm-install-traefik-crd
kubectl --context "k3d-$landscape" -n kube-system wait --for=jsonpath=.status.succeeded=1 --timeout=300s job helm-install-traefik
kubectl --context "k3d-$landscape" -n kube-system wait --for=jsonpath=.status.readyReplicas=1 --timeout=300s deployment traefik
echo "✅ Cluster is ready!"

# install external-secrets operator
echo "🛠 Installing external-secrets operator..."
helm repo add external-secrets https://charts.external-secrets.io
helm upgrade --install --kube-context "k3d-$landscape" external-secrets external-secrets/external-secrets -n external-secrets --create-namespace
kubectl --context "k3d-$landscape" -n external-secrets wait --for=jsonpath=.status.readyReplicas=1 --timeout=300s deployment external-secrets-webhook
kubectl --context "k3d-$landscape" -n external-secrets wait --for=jsonpath=.status.readyReplicas=1 --timeout=300s deployment external-secrets-cert-controller
kubectl --context "k3d-$landscape" -n external-secrets wait --for=jsonpath=.status.readyReplicas=1 --timeout=300s deployment external-secrets
echo "✅ Installed external-secrets operator!"

if [ "$secrets" = 'true' ]; then
  ./scripts/local/install-infisical.sh "$landscape"
fi
