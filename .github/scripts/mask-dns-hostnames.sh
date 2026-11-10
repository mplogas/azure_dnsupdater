#!/usr/bin/env bash
# Hides the hostnames from DNS_HOSTNAMES in the logs of this job. GitHub masks the secret's whole value,
# but the what-if output also shows each hostname as part of a resource ID (dnsZones/<zone>/A/<record>).
# The zone is the last two labels, as in the app and the Bicep template.
set -euo pipefail

if [ -z "${DNS_HOSTNAMES:-}" ]; then
  echo "::error::Set the secret DNS_HOSTNAMES in this GitHub environment, for example home.example.com."
  exit 1
fi

IFS=',' read -ra hostnames <<< "$DNS_HOSTNAMES"
for hostname in "${hostnames[@]}"; do
  hostname="${hostname//[[:space:]]/}"
  [ -n "$hostname" ] || continue
  record="${hostname%.*.*}"
  zone="${hostname#"$record".}"
  echo "::add-mask::$hostname"
  echo "::add-mask::$zone/A/$record"
done
