#!/usr/bin/env bash

landscape="$1"

set -euo pipefail

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
