param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$OpenReport
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$coverageDir = Join-Path $repoRoot "coverage"
$coverageOutputPrefix = Join-Path $coverageDir "coverage"
$coberturaPath = Join-Path $coverageDir "coverage.cobertura.xml"
$jsonPath = Join-Path $coverageDir "coverage.json"
$reportDir = Join-Path $coverageDir "report"
$indexPath = Join-Path $reportDir "index.html"

if (!(Test-Path $coverageDir)) {
    New-Item -ItemType Directory -Path $coverageDir | Out-Null
}

Write-Host "Running tests with Coverlet (Cobertura + JSON)..."
dotnet test "FormID Database Manager.Tests" -c $Configuration `
    /p:CollectCoverage=true `
    "/p:CoverletOutput=$coverageOutputPrefix" `
    "/p:CoverletOutputFormat=cobertura%2cjson"

if ($LASTEXITCODE -ne 0) {
    throw "dotnet test failed while collecting coverage."
}

Write-Host "Generating HTML report..."
dotnet tool restore | Out-Null
dotnet tool run reportgenerator `
    "-reports:$coberturaPath" `
    "-targetdir:$reportDir" `
    "-reporttypes:Html;TextSummary"

if ($LASTEXITCODE -ne 0) {
    throw "reportgenerator failed to create the HTML report."
}

Write-Host ""
Write-Host "Machine-readable outputs:"
Write-Host "  - $coberturaPath"
Write-Host "  - $jsonPath"
Write-Host "Human-readable output:"
Write-Host "  - $indexPath"

if ($OpenReport) {
    Start-Process $indexPath
}
