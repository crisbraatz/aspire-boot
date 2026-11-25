#!/usr/bin/env sh
# Builds the API and Worker images, checks that libSkiaSharp.so resolves all its
# native dependencies inside the API runtime image, and runs the image-processor
# unit tests on Linux. Works with Docker or Podman; POSIX sh.
#
# The sources are copied into the test container (never bind-mounted), so the
# host bin/ and obj/ folders are left untouched.
set -eu
cd "$(dirname "$0")/.."
ENGINE="$(./scripts/container-engine.sh)"
SDK_IMAGE="mcr.microsoft.com/dotnet/sdk:10.0"
TEST_CONTAINER="aspireboot-verify-tests-$$"

cleanup() {
  "$ENGINE" rm -f "$TEST_CONTAINER" >/dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM

echo "==> Building images with $ENGINE"
"$ENGINE" build -f src/AspireBoot.Api/Dockerfile -t aspireboot-api:verify .
"$ENGINE" build -f src/AspireBoot.Worker/Dockerfile -t aspireboot-worker:verify .

echo "==> Checking libSkiaSharp.so in the API image"
"$ENGINE" run --rm --entrypoint sh aspireboot-api:verify -c '
  set -eu
  case "$(uname -m)" in
    x86_64) arch=x64 ;;
    aarch64) arch=arm64 ;;
    *) echo "Unsupported architecture: $(uname -m)" >&2; exit 1 ;;
  esac
  lib="$(find /app -path "*/runtimes/linux-$arch/native/libSkiaSharp.so" | head -n 1)"
  if [ -z "$lib" ]; then
    echo "libSkiaSharp.so for linux-$arch not found under /app" >&2
    exit 1
  fi
  echo "Found $lib"
  out="$(ldd "$lib" 2>&1)" || true
  echo "$out"
  if echo "$out" | grep -q "not found"; then
    echo "libSkiaSharp.so has unresolved dependencies" >&2
    exit 1
  fi
'

echo "==> Running image-processor unit tests in $SDK_IMAGE"
"$ENGINE" create --name "$TEST_CONTAINER" -w /tmp "$SDK_IMAGE" \
  dotnet test --solution AspireBoot.slnx \
  --filter-namespace 'AspireBoot.Tests.Unit.Infrastructure.Images*' >/dev/null
COPYFILE_DISABLE=1 tar -c --exclude='._*' \
  --exclude='./.git' --exclude='./.aiw' --exclude='./TestResults' \
  --exclude='*/bin' --exclude='*/obj' --exclude='*/node_modules' \
  --exclude='*/dist' --exclude='*/.angular' . \
  | "$ENGINE" cp - "$TEST_CONTAINER:/tmp"
"$ENGINE" start -a "$TEST_CONTAINER"

echo "==> Images verified"
