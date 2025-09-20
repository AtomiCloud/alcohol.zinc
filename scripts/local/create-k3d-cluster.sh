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

echo "📝 Writing to '$HOME/.kube/k3dconfigs/k3d-$landscape'"
k3d kubeconfig get "$landscape" >"$HOME/.kube/k3dconfigs/k3d-$landscape"
KUBECONFIG=$(cd ~/.kube/configs && find "$(pwd)"/* | awk 'ORS=":"')$(cd ~/.kube/k3dconfigs && find "$(pwd)"/* | awk 'ORS=":"')$(cd ~/.kube/atomiconfigs && find "$(pwd)"/* | awk 'ORS=":"') kubectl config view --flatten >~/.kube/config
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

if [ "$secrets" = 'true' ]; then
  ./scripts/local/install-infisical.sh "$landscape"
fi
