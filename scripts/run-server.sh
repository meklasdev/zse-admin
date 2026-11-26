#!/usr/bin/env bash
# Uruchamia serwer ZSE-Admin na danych przykładowych (tryb development).
set -euo pipefail

cd "$(dirname "$0")/.."

export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"

echo "ZSE-Admin: build i start serwera..."
dotnet run --project src/ZseAdmin.Server --urls "${ZSE_ADMIN_URLS:-http://0.0.0.0:5080}"
