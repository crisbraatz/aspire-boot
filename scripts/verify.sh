#!/usr/bin/env sh
# Full local quality gate: format, lint, build, migrations check and tests.
set -eu
cd "$(dirname "$0")/.."
dotnet tool restore
dotnet format AspireBoot.slnx --verify-no-changes
dotnet build AspireBoot.slnx -c Release
dotnet ef migrations has-pending-model-changes -p src/AspireBoot.Core -s src/AspireBoot.Api
npm --prefix src/AspireBoot.Web ci
npm --prefix src/AspireBoot.Web run format:check
npm --prefix src/AspireBoot.Web run lint
npm --prefix src/AspireBoot.Web run build
./scripts/test.sh
