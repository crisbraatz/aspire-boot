#!/usr/bin/env sh
# Runs every backend and frontend test. Works with Docker or Podman.
#
# Usage:
#   ./scripts/test.sh                       run all backend and frontend tests
#   ./scripts/test.sh <MTP options>         run only the backend tests, with the
#                                           given options passed to `dotnet test`
#                                           (xUnit v3 on Microsoft.Testing.Platform),
#                                           e.g. --filter-namespace 'AspireBoot.Tests.Unit*'
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

if [ "$#" -gt 0 ]; then
  dotnet test --solution AspireBoot.slnx "$@"
  exit 0
fi

dotnet test --solution AspireBoot.slnx
npm --prefix src/AspireBoot.Web run test:ci
