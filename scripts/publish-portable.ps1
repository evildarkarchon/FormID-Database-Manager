param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputPath = "publish/FormID-Database-Manager-portable-win-x64.zip"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "FormID Database Manager.WinUI/FormID Database Manager.WinUI.csproj"

# Resolve the TargetFramework from the project so the output path tracks the csproj
# instead of hardcoding a TFM/RID string that silently breaks when the csproj changes.
$targetFramework = (dotnet msbuild $projectPath -getProperty:TargetFramework 2>$null | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($targetFramework)) {
    throw "Could not resolve TargetFramework from $projectPath (exit code: $LASTEXITCODE)."
}

$portableDir = Join-Path $repoRoot "FormID Database Manager.WinUI/bin/x64/$Configuration/$targetFramework/win-x64"

if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $zipPath = $OutputPath
}
else {
    $zipPath = Join-Path $repoRoot $OutputPath
}

$zipDirectory = Split-Path -Parent $zipPath
if (!(Test-Path $zipDirectory)) {
    New-Item -ItemType Directory -Path $zipDirectory -Force | Out-Null
}

if (Test-Path $portableDir) {
    Remove-Item -Path $portableDir -Recurse -Force
}

Write-Host "Building portable self-contained WinUI app..."
$buildArgs = @(
    "build"
    $projectPath
    "-c"
    $Configuration
    "-p:Platform=x64"
    "-r"
    "win-x64"
    "--self-contained"
)

dotnet @buildArgs

if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed for the portable WinUI app."
}

if (!(Test-Path $portableDir)) {
    throw "Expected portable directory was not created: $portableDir"
}

$requiredFiles = @(
    "FormID Database Manager.WinUI.exe"
    "FormID Database Manager.WinUI.pri"
    "App.xbf"
    "MainWindow.xbf"
    "coreclr.dll"
    "hostfxr.dll"
    "Microsoft.WindowsAppRuntime.dll"
)

foreach ($requiredFile in $requiredFiles) {
    $requiredPath = Join-Path $portableDir $requiredFile
    if (!(Test-Path $requiredPath)) {
        throw "Expected portable file was not created: $requiredPath"
    }
}

$msixArtifacts = @(Get-ChildItem -Path $portableDir -Recurse -File -Include "*.msix", "*.appx", "*.appxrecipe")
if ($msixArtifacts.Count -gt 0) {
    throw "MSIX artifacts are not allowed in the portable output: $($msixArtifacts[0].FullName)"
}

if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}

Write-Host "Creating portable zip..."
Compress-Archive -Path (Join-Path $portableDir "*") -DestinationPath $zipPath -Force

Write-Host ""
Write-Host "Portable output:"
Write-Host "  - $portableDir"
Write-Host "Portable zip:"
Write-Host "  - $zipPath"
