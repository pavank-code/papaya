# Flow Vault - Test Runner Script
# Runs all tests with optional coverage

param(
    [switch]$Coverage,
    [string]$Filter = ""
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
$testProject = Join-Path $scriptDir "src\FlowVault.Tests\FlowVault.Tests.csproj"

Write-Host "===== Flow Vault Test Runner =====" -ForegroundColor Cyan

$args = @("test", $testProject, "--configuration", "Debug", "--verbosity", "normal")

if ($Filter) {
    $args += "--filter"
    $args += $Filter
    Write-Host "Filter: $Filter" -ForegroundColor Yellow
}

if ($Coverage) {
    Write-Host "Running with coverage..." -ForegroundColor Yellow
    $args += "--collect:XPlat Code Coverage"
    $args += "--results-directory"
    $args += (Join-Path $scriptDir "TestResults")
}

Write-Host "`nRunning tests..." -ForegroundColor Yellow
& dotnet $args

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✓ All tests passed!" -ForegroundColor Green
} else {
    Write-Host "`n✗ Some tests failed!" -ForegroundColor Red
    exit 1
}

if ($Coverage) {
    Write-Host "`nCoverage report generated in TestResults folder" -ForegroundColor Gray
}
