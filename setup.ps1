# ==============================================================================
# Candied Workbench - Automated Environment Setup & MCP Publisher
# ==============================================================================
[CmdletBinding()]
param(
    [switch]$SkipTests = $false
)

$ErrorActionPreference = "Stop"

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host "       Candied Workbench - Setup & MCP Verification               " -ForegroundColor Cyan
Write-Host "==================================================================" -ForegroundColor Cyan

# 1. Check prerequisites
Write-Host "`n[1/4] Checking .NET SDK..." -ForegroundColor Yellow
$dotnetVersion = dotnet --version
if ($LASTEXITCODE -ne 0) {
    Write-Error ".NET SDK is not installed or not in PATH. Please install .NET 9 SDK."
    exit 1
}
Write-Host "Found .NET SDK version: $dotnetVersion" -ForegroundColor Green

# 2. Run verification script if not skipped
if (-not $SkipTests) {
    Write-Host "`n[2/4] Running test suite and architecture boundary verification..." -ForegroundColor Yellow
    powershell -ExecutionPolicy Bypass -File .\check.ps1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Verification script failed. Please fix any errors before deploying."
        exit $LASTEXITCODE
    }
} else {
    Write-Host "`n[2/4] Skipping tests (-SkipTests specified)..." -ForegroundColor DarkGray
}

# 3. Publish Candied.Agent standalone binary
Write-Host "`n[3/4] Publishing Candied.Agent to ./publish..." -ForegroundColor Yellow
# If previous instance is running, try to stop it gracefully
Get-Process -Name "Candied.Agent" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Stopping existing Candied.Agent process (PID: $($_.Id))..." -ForegroundColor DarkYellow
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
}

dotnet publish src/Candied.Agent -c Release -o publish
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to publish Candied.Agent."
    exit $LASTEXITCODE
}

$publishedExe = (Get-Item .\publish\Candied.Agent.exe).FullName
Write-Host "Published successfully: $publishedExe" -ForegroundColor Green

# 4. Verify published binary responds to MCP ping
Write-Host "`n[4/4] Verifying MCP Server communication..." -ForegroundColor Yellow
$pingPayload = '{"jsonrpc":"2.0","id":1,"method":"ping"}'
$response = $pingPayload | .\publish\Candied.Agent.exe
if ($response -match '"result":\{\}') {
    Write-Host "MCP Server ping verification PASSED!" -ForegroundColor Green
} else {
    Write-Warning "MCP Server ping verification returned unexpected output: $response"
}

Write-Host "`n==================================================================" -ForegroundColor Cyan
Write-Host "  Setup Complete! Candied MCP Server is ready for AI agents." -ForegroundColor Green
Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host "To configure in Antigravity or Claude Desktop, point your mcp_config.json to:" -ForegroundColor White
Write-Host @"
{
  "mcpServers": {
    "Candied": {
      "command": "$($publishedExe -replace '\\', '\\')",
      "args": []
    }
  }
}
"@ -ForegroundColor Gray
