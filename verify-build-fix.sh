#!/usr/bin/env bash
set -Eeuo pipefail

echo "Verificando prueba MSTest..."
if grep -RIn --include='*.cs' 'ThrowsExceptionAsync\|ThrowsExactlyAsync' tests/Dhole.DataExtraction.UnitTests; then
  echo "ERROR: todavía existe una API Throws* dependiente de versión." >&2
  exit 1
fi

echo "Verificando EF Core..."
if grep -RIn --include='*.csproj' 'Microsoft.EntityFrameworkCore[^"]*" Version="10\.0\.10' .; then
  echo "ERROR: todavía existe EF Core 10.0.10." >&2
  exit 1
fi

grep -n 'InvalidOperationException? exception = null' tests/Dhole.DataExtraction.UnitTests/AutomatedPricingExtractionServiceTests.cs
grep -RIn --include='*.csproj' 'Microsoft.EntityFrameworkCore.*10.0.8' src tests

echo "OK: parche aplicado."
