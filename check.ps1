# Verification Script for Candied (Windows / PowerShell)
$ErrorActionPreference = "Stop"

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  Step 1: Code Formatting & Style (dotnet format)" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
dotnet format --verify-no-changes
if ($LASTEXITCODE -ne 0) {
    Write-Error "Format check failed! Run 'dotnet format' to resolve style violations."
    exit 1
}

Write-Host "`n================================================" -ForegroundColor Cyan
Write-Host "  Step 2: Build & Type-Check (TreatWarningsAsErrors)" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
dotnet build --configuration Release /p:TreatWarningsAsErrors=true
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build or type-check failed!"
    exit 1
}

Write-Host "`n================================================" -ForegroundColor Cyan
Write-Host "  Step 3: Architecture Boundaries & Unit Tests" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
dotnet test --configuration Release --no-build --verbosity normal
if ($LASTEXITCODE -ne 0) {
    Write-Error "Architecture tests or unit tests failed!"
    exit 1
}

Write-Host "`n[SUCCESS] All checks passed cleanly!" -ForegroundColor Green
exit 0
