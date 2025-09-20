#!/usr/bin/env bash

landscape="$1"

set -euo pipefail

# install external-secrets operator
echo "🛠 Installing external-secrets operator..."
helm repo add external-secrets https://charts.external-secrets.io
helm upgrade --install --kube-context "k3d-$landscape" external-secrets external-secrets/external-secrets -n external-secrets --create-namespace
kubectl --context "k3d-$landscape" -n external-secrets wait --for=jsonpath=.status.readyReplicas=1 --timeout=300s deployment external-secrets-webhook
kubectl --context "k3d-$landscape" -n external-secrets wait --for=jsonpath=.status.readyReplicas=1 --timeout=300s deployment external-secrets-cert-controller
kubectl --context "k3d-$landscape" -n external-secrets wait --for=jsonpath=.status.readyReplicas=1 --timeout=300s deployment external-secrets
echo "✅ Installed external-secrets operator!"

# create infisical secret
echo "🛠 Creating infisical secret..."
root_client_id="$(infisical secrets get "--projectId=$SOS_PROJECT_ID" "--env=$landscape" SULFOXIDE_SOS_CLIENT_ID --plain | base64 -w 0)"
root_client_secret="$(infisical secrets get "--projectId=$SOS_PROJECT_ID" "--env=$landscape" SULFOXIDE_SOS_CLIENT_SECRET --plain | base64 -w 0)"

kubectl --context "k3d-$landscape" -n external-secrets apply -f - <<EOF
apiVersion: v1
kind: Secret
metadata:
  name: root-token
type: Opaque
data:
  "CLIENT_ID": "$root_client_id"
  "CLIENT_SECRET": "$root_client_secret"
EOF
echo "✅ Created infisical secret!"

# create doppler cluster secret store
echo "🛠 Creating infisical cluster secret store..."
kubectl --context "k3d-$landscape" -n external-secrets apply -f - <<EOF
apiVersion: external-secrets.io/v1
kind: ClusterSecretStore
metadata:
  name: infisical
spec:
  provider:
    infisical:
      auth:
        universalAuthCredentials:
          clientId:
            key: CLIENT_ID
            name: root-token
            namespace: external-secrets
          clientSecret:
            key: CLIENT_SECRET
            name: root-token
            namespace: external-secrets
      hostAPI: https://secrets.atomi.cloud
      secretsScope:
        environmentSlug: "$landscape"
        projectSlug: sulfoxide-sos
        recursive: false
        secretsPath: /
EOF
echo "✅ Created infisical cluster secret store!"
