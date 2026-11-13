#!/usr/bin/env bash
# Creates (or updates) the uptime test on netrts.snowse.io/health and the alert that emails when it
# fails. Safe to run again. See docs/deploying.md, "Health and uptime alerts".
#
#   ALERT_EMAIL=you@example.edu deploy/monitoring/setup.sh
#
# Settings (environment): ALERT_EMAIL (required; kept out of the repo), SUBSCRIPTION (default: the
# faculty subscription that holds rg-netrts), RESOURCE_GROUP (rg-netrts).
set -euo pipefail

SUBSCRIPTION=${SUBSCRIPTION:-faculty_Jonathan_Allen}
RG=${RESOURCE_GROUP:-rg-netrts}
: "${ALERT_EMAIL:?Set ALERT_EMAIL to the address the alert should email.}"

here=$(cd "$(dirname "$0")" && pwd)

echo "[monitoring setup] uptime test, alert and action group in $RG"
command az deployment group create --subscription "$SUBSCRIPTION" -g "$RG" -n netrts-monitoring \
  -f "$here/main.bicep" -p alertEmail="$ALERT_EMAIL" -o none
echo "[monitoring setup] done: see netrts-insights > Availability in the portal."
