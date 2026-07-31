#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

# Evita que API y Workers intenten generar los mismos artefactos al mismo tiempo.
find src -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
dotnet restore "DholeDataExtractionService.slnx"
dotnet build "DholeDataExtractionService.slnx" --no-restore -m:1

dotnet run --project "src/Dhole.DataExtraction.Api/Dhole.DataExtraction.Api.csproj" --no-build > "/tmp/Dhole.DataExtraction.Api.log" 2>&1 &
echo "Iniciado Dhole.DataExtraction.Api. Log: /tmp/Dhole.DataExtraction.Api.log"
dotnet run --project "src/Dhole.DataExtraction.Workers/Dhole.DataExtraction.Workers.csproj" --no-build > "/tmp/Dhole.DataExtraction.Workers.log" 2>&1 &
echo "Iniciado Dhole.DataExtraction.Workers. Log: /tmp/Dhole.DataExtraction.Workers.log"

wait
