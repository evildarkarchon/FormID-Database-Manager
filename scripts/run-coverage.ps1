param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$BlameHangTimeout = "10m",
    [string]$ResultsDir = "coverage/test-results",
    [switch]$IncludeStressAndLoad,
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
$resultsDirPath = Join-Path $repoRoot $ResultsDir

if (!(Test-Path $coverageDir)) {
    New-Item -ItemType Directory -Path $coverageDir | Out-Null
}

if (!(Test-Path $resultsDirPath)) {
    New-Item -ItemType Directory -Path $resultsDirPath -Force | Out-Null
}

Write-Host "Running tests with Coverlet (Cobertura + JSON)..."
$defaultFilter = "Category!=LoadTest&Category!=StressTest"
$testArgs = @(
    "test"
    "FormID Database Manager.Tests"
    "-c"
    $Configuration
    "--blame-hang"
    "--blame-hang-timeout"
    $BlameHangTimeout
    "--blame-hang-dump-type"
    "mini"
    "--results-directory"
    $resultsDirPath
    "--logger"
    "trx;LogFileName=coverage.trx"
)

# By default, exclude long-running stress/load categories from coverage runs.
# Use -IncludeStressAndLoad to intentionally include them.
if (!$IncludeStressAndLoad)
{
    $testArgs += @("--filter", $defaultFilter)
}

$testArgs += @(
    "/p:CollectCoverage=true"
    "/p:CoverletOutput=$coverageOutputPrefix"
    "/p:CoverletOutputFormat=cobertura%2cjson"
)

dotnet @testArgs

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
