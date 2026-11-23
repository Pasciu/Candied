#!/usr/bin/env bash
set -e

echo "=================================================================="
echo "       Candied Workbench - Setup & MCP Verification               "
echo "=================================================================="

# 1. Check prerequisites
echo ""
echo "[1/4] Checking .NET SDK..."
dotnet --version

# 2. Run verification
echo ""
echo "[2/4] Running test suite and architecture boundary verification..."
./check.sh

# 3. Publish Candied.Agent
echo ""
echo "[3/4] Publishing Candied.Agent to ./publish..."
dotnet publish src/Candied.Agent -c Release -o publish

# 4. Verify published binary
echo ""
echo "[4/4] Verifying MCP Server communication..."
echo '{"jsonrpc":"2.0","id":1,"method":"ping"}' | ./publish/Candied.Agent

echo ""
echo "=================================================================="
echo "  Setup Complete! Candied MCP Server is ready for AI agents."
echo "=================================================================="
