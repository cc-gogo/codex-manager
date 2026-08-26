$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root 'publish'
$exe = Join-Path $publish 'CodexConversationManager.App.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Missing portable executable: $exe" }
foreach ($required in @(
    'CodexConversationManager.App.dll',
    'CodexConversationManager.App.runtimeconfig.json',
    'Microsoft.Data.Sqlite.dll'
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $publish $required))) {
        throw "Missing portable runtime file: $required"
    }
}
$portablePaths = Get-Content -Raw (Join-Path $root 'src\CodexConversationManager.App\Services\PortablePathService.cs')
if ($portablePaths -notmatch 'AppContext.BaseDirectory') { throw 'Portable paths must be based on the executable directory for portable builds.' }
$publishScript = Get-Content -Raw (Join-Path $root 'tools\publish-windows.ps1')
if ($publishScript -notmatch [regex]::Escape("Join-Path `$root 'publish'")) { throw 'Windows publishing must use the stable publish directory.' }
$installer = Get-Content -Raw (Join-Path $root 'installer\CodexConversationManager.iss')
if ($installer -notmatch '\.\.\\publish\\\*') { throw 'Installer must consume the stable publish directory.' }
$macScript = Get-Content -Raw (Join-Path $root 'tools\package-macos.ps1')
if ($macScript -notmatch 'osx-arm64' -or $macScript -notmatch 'osx-x64' -or $macScript -notmatch 'Info.plist') { throw 'macOS packaging script is incomplete.' }
$report = Join-Path $root 'logs\real-inventory-readonly.json'
if (-not (Test-Path -LiteralPath $report)) { throw "Missing read-only inventory report: $report" }
$reportData = Get-Content -Raw $report | ConvertFrom-Json
if (-not $reportData.regressionIdOnePresent -or -not $reportData.regressionIdTwoPresent) {
    throw 'The read-only regression inventory did not find both required conversations.'
}
Write-Output 'Portable acceptance checks passed.'
