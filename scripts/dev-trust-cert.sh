#!/usr/bin/env sh
# Trusts the local Caddy CA used for https://localhost:8443 (run once, after the first `aspire run`).
set -eu
ENGINE="$("$(dirname "$0")/container-engine.sh")"
CONTAINER="$("$ENGINE" ps --filter "ancestor=docker.io/library/caddy:2.11.9-alpine" --format '{{.Names}}' | head -n1)"
[ -n "$CONTAINER" ] || { echo "Caddy is not running. Start the app with 'aspire run' first." >&2; exit 1; }
"$ENGINE" cp "$CONTAINER:/data/caddy/pki/authorities/local/root.crt" ./caddy-local-root.crt
case "$(uname -s)" in
  Darwin) sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain ./caddy-local-root.crt ;;
  Linux) sudo cp ./caddy-local-root.crt /usr/local/share/ca-certificates/caddy-local-root.crt && sudo update-ca-certificates ;;
  *) echo "Import ./caddy-local-root.crt into your trusted root store manually." ;;
esac
rm -f ./caddy-local-root.crt
