$ErrorActionPreference = 'Stop'
$productRoot = Split-Path $PSScriptRoot -Parent
$cacheRoot = Join-Path $productRoot 'build-cache'
$env:DOTNET_CLI_HOME = Join-Path $cacheRoot 'dotnet-home'
$env:NUGET_PACKAGES = Join-Path $cacheRoot 'nuget'
$env:TEMP = Join-Path $cacheRoot 'temp'
$env:TMP = $env:TEMP
foreach ($path in @($env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, $env:TEMP)) {
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

$dotnet = Join-Path $productRoot 'build-tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { throw "Missing local .NET SDK: $dotnet" }
$publish = Join-Path $productRoot 'publish'
$staging = Join-Path $productRoot 'publish-staging'
if (Test-Path -LiteralPath $publish) { throw "Refusing to overwrite existing publish directory: $publish" }
if (Test-Path -LiteralPath $staging) { throw "Remove or inspect stale staging directory before publishing: $staging" }

& powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $productRoot 'tools\run-tests.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

try {
    & $dotnet publish (Join-Path $productRoot 'src\CodexConversationManager.App\CodexConversationManager.App.csproj') `
        -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $staging
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Rename-Item -LiteralPath (Join-Path $staging 'CodexConversationManager.App.exe') -NewName 'CodexConversationManager.exe'
    $readme = Get-ChildItem -LiteralPath $productRoot -Filter 'README-*.md' | Select-Object -First 1
    if ($null -eq $readme) { throw 'Missing usage README.' }
    Copy-Item -LiteralPath $readme.FullName -Destination (Join-Path $staging 'README.md')
    Copy-Item -LiteralPath (Join-Path $productRoot 'LICENSE') -Destination $staging
    Move-Item -LiteralPath $staging -Destination $publish
}
catch {
    throw
}
