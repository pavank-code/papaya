# Flow Vault - Development Startup Script
# Starts both Backend and UI processes

param(
    [switch]$BackendOnly,
    [switch]$UIOnly,
    [switch]$Build
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
$srcDir = Join-Path $scriptDir "src"

Write-Host "===== Flow Vault Development Environment =====" -ForegroundColor Cyan

# Build if requested
if ($Build) {
    Write-Host "`nBuilding solution..." -ForegroundColor Yellow
    dotnet build "$scriptDir\FlowVault.sln" --configuration Debug
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed!" -ForegroundColor Red
        exit 1
    }
    Write-Host "Build successful!" -ForegroundColor Green
}

# Start Backend
if (-not $UIOnly) {
    Write-Host "`nStarting Backend Host..." -ForegroundColor Yellow
    $backendPath = Join-Path $srcDir "FlowVault.BackendHost"
    
    Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", $backendPath -WindowStyle Normal
    
    Write-Host "Backend Host started!" -ForegroundColor Green
    
    # Wait for backend to initialize
    Start-Sleep -Seconds 2
}

# Start UI
if (-not $BackendOnly) {
    Write-Host "`nStarting UI..." -ForegroundColor Yellow
    $uiPath = Join-Path $srcDir "FlowVault.UI"
    
    Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", $uiPath -WindowStyle Normal
    
    Write-Host "UI started!" -ForegroundColor Green
}

Write-Host "`n===== Flow Vault is running! =====" -ForegroundColor Cyan
Write-Host "Press Ctrl+. to toggle click-through mode" -ForegroundColor Gray
Write-Host "Close the UI window to stop the application" -ForegroundColor Gray
