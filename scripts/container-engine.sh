#!/usr/bin/env sh
# Prints the container engine to use: docker if available, otherwise podman.
set -eu
if command -v docker >/dev/null 2>&1; then
  echo docker
elif command -v podman >/dev/null 2>&1; then
  echo podman
elif [ -x /opt/podman/bin/podman ]; then
  echo /opt/podman/bin/podman
else
  echo "No container engine found. Install Docker or Podman." >&2
  exit 1
fi
