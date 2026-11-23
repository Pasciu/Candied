#!/usr/bin/env bash
set -euo pipefail

echo "================================================"
echo "  Step 1: Code Formatting & Style (dotnet format)"
echo "================================================"
dotnet format --verify-no-changes

echo ""
echo "================================================"
echo "  Step 2: Build & Type-Check (TreatWarningsAsErrors)"
echo "================================================"
dotnet build --configuration Release /p:TreatWarningsAsErrors=true

echo ""
echo "================================================"
echo "  Step 3: Architecture Boundaries & Unit Tests"
echo "================================================"
dotnet test --configuration Release --no-build --verbosity normal

echo ""
echo "[SUCCESS] All checks passed cleanly!"
