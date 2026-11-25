#!/usr/bin/env sh
# Runs every backend and frontend test. Works with Docker or Podman.
set -eu
cd "$(dirname "$0")/.."
ENGINE="$(./scripts/container-engine.sh)"

case "$ENGINE" in
  *podman)
    # Testcontainers speaks the Docker API: point it at the Podman socket.
    if [ -z "${DOCKER_HOST:-}" ]; then
      SOCKET="$("$ENGINE" machine inspect --format '{{.ConnectionInfo.PodmanSocket.Path}}' 2>/dev/null || true)"
      [ -n "$SOCKET" ] && export DOCKER_HOST="unix://$SOCKET"
    fi
    export TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE="${TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE:-/run/podman/podman.sock}"
    ;;
esac

dotnet test --solution AspireBoot.slnx
npm --prefix src/AspireBoot.Web run test:ci
