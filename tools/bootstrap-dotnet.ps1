[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$productRoot = Split-Path $PSScriptRoot -Parent
$buildTools = Join-Path $productRoot 'build-tools'
$buildCache = Join-Path $productRoot 'build-cache'
$sdkRoot = Join-Path $buildTools 'dotnet'
$installer = Join-Path $buildTools 'dotnet-install.ps1'

$env:DOTNET_CLI_HOME = Join-Path $buildCache 'dotnet-home'
$env:NUGET_PACKAGES = Join-Path $buildCache 'nuget'
$env:TEMP = Join-Path $buildCache 'temp'
$env:TMP = $env:TEMP

foreach ($path in @($buildTools, $sdkRoot, $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, $env:TEMP)) {
    [void](New-Item -ItemType Directory -Path $path -Force)
}

$dotnet = Join-Path $sdkRoot 'dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $installer `
        -Channel '8.0' -Quality 'GA' -InstallDir $sdkRoot -NoPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet-install failed with exit code $LASTEXITCODE"
    }
}

if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw "Portable dotnet SDK was not found at $dotnet"
}

Write-Output $dotnet
