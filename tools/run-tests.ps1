[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$productRoot = Split-Path $PSScriptRoot -Parent
$dotnet = & (Join-Path $PSScriptRoot 'bootstrap-dotnet.ps1') | Select-Object -Last 1

$env:DOTNET_CLI_HOME = Join-Path $productRoot 'build-cache\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $productRoot 'build-cache\nuget'
$env:TEMP = Join-Path $productRoot 'build-cache\temp'
$env:TMP = $env:TEMP

& $dotnet test (Join-Path $productRoot 'CodexConversationManager.sln') --nologo -m:1
exit $LASTEXITCODE
