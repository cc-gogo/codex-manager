$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$required = @(
    'src\CodexConversationManager.Core\CodexConversationManager.Core.csproj',
    'src\CodexConversationManager.App\CodexConversationManager.App.csproj',
    'tests\CodexConversationManager.Tests\CodexConversationManager.Tests.csproj'
)

foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $relative))) {
        throw "Missing $relative"
    }
}

$appProject = Get-Content -Raw -LiteralPath (Join-Path $root $required[1])
if ($appProject -notmatch '<TargetFramework>net8\.0-windows</TargetFramework>') {
    throw 'WPF project does not target net8.0-windows.'
}
if ($appProject -notmatch '<UseWPF>true</UseWPF>') {
    throw 'WPF project does not enable UseWPF.'
}

$bootstrapPath = Join-Path $root 'tools\bootstrap-dotnet.ps1'
if (-not (Test-Path -LiteralPath $bootstrapPath)) {
    throw 'Missing tools\bootstrap-dotnet.ps1'
}
$bootstrap = Get-Content -Raw -LiteralPath $bootstrapPath
foreach ($name in 'DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'TEMP', 'TMP') {
    if ($bootstrap -notmatch [regex]::Escape("env:$name")) {
        throw "Missing D-drive cache variable $name"
    }
}

Write-Output 'PASS: build layout is complete and D-drive isolated.'
