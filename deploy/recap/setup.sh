#!/usr/bin/env bash
# Creates (or updates) everything the nightly recap show needs in Azure, with the az CLI. Safe to run
# again. See docs/deploying.md, "Nightly recap show".
#
#   deploy/recap/setup.sh                 # storage, Foundry (writer model + Speech), registry, image, job
#   deploy/recap/setup.sh --wire-server   # also give the web app its two settings (restarts the site!)
#
# Settings (environment): SUBSCRIPTION (default: the faculty subscription that holds rg-netrts),
# RESOURCE_GROUP (rg-netrts), WEB_APP (netrts-jallen), GITHUB_IDENTITY (id-netrts-github).
set -euo pipefail

SUBSCRIPTION=${SUBSCRIPTION:-faculty_Jonathan_Allen}
RG=${RESOURCE_GROUP:-rg-netrts}
WEB_APP=${WEB_APP:-netrts-jallen}
GITHUB_IDENTITY=${GITHUB_IDENTITY:-id-netrts-github}
WIRE_SERVER=false
[[ "${1:-}" == "--wire-server" ]] && WIRE_SERVER=true

here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
az() { command az "$@" --subscription "$SUBSCRIPTION"; }
say() { echo "[recap setup] $*"; }

server_id=$(az webapp identity show -g "$RG" -n "$WEB_APP" --query principalId -o tsv)
github_id=$(az identity show -g "$RG" -n "$GITHUB_IDENTITY" --query principalId -o tsv 2>/dev/null || true)

# The job's key for the server: keep the one already in use, else make one. Never printed.
key=$(az webapp config appsettings list -g "$RG" -n "$WEB_APP" --query "[?name=='Recap__ApiKey'].value | [0]" -o tsv 2>/dev/null || true)
if [[ -z "$key" || "$key" == "None" ]]; then
  key=$(az containerapp job secret show -g "$RG" -n netrts-recap --secret-name recap-key --query value -o tsv 2>/dev/null || true)
fi
if [[ -z "$key" || "$key" == "None" ]]; then
  key=$(openssl rand -hex 24)
fi

deploy() {
  az deployment group create -g "$RG" -n "netrts-recap-$1" -f "$here/main.bicep" \
    -p serverPrincipalId="$server_id" recapKey="$key" githubPrincipalId="$github_id" deployJob="$2" \
    --query properties.outputs -o json
}

say "storage, Foundry, registry and identities"
outputs=$(deploy infra false)
output() { echo "$outputs" | node -pe "JSON.parse(require('fs').readFileSync(0, 'utf8')).$1.value"; }
registry=$(output registry)
blob_url=$(output blobUrl)
say "registry $registry, videos in $blob_url"

say "building the job's image in the registry"
az acr build -r "$registry" -t netrts-recap:latest "$repo/recap" --no-logs -o none

say "the nightly job"
deploy job true > /dev/null

if $WIRE_SERVER; then
  say "web app settings (the site restarts)"
  az webapp config appsettings set -g "$RG" -n "$WEB_APP" -o none --settings \
    "ConnectionStrings__recaps=$blob_url" "Recap__ApiKey=$key"
else
  current=$(az webapp config appsettings list -g "$RG" -n "$WEB_APP" --query "[?name=='ConnectionStrings__recaps'].value | [0]" -o tsv)
  if [[ "$current" != "$blob_url" ]]; then
    say "the web app isn't wired up yet; run again with --wire-server when a restart is OK"
  fi
fi

say "done. Run it now with: az containerapp job start -g $RG -n netrts-recap --subscription \"$SUBSCRIPTION\""
