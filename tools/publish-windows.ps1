$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root 'build-tools\dotnet\dotnet.exe'
$publish = Join-Path $root 'publish'
$installerScript = Join-Path $root 'installer\CodexConversationManager.iss'
$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'

if (-not (Test-Path -LiteralPath $dotnet)) { throw "Bundled .NET SDK not found: $dotnet" }
if (-not (Test-Path -LiteralPath $iscc)) { throw "Inno Setup compiler not found: $iscc" }

& $dotnet test (Join-Path $root 'CodexConversationManager.sln') --no-restore
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
& $dotnet publish (Join-Path $root 'src\CodexConversationManager.App\CodexConversationManager.App.csproj') -c Release -o $publish --no-restore
if ($LASTEXITCODE -ne 0) { throw "Windows publish failed." }
& $iscc $installerScript
if ($LASTEXITCODE -ne 0) { throw "Installer build failed." }

Write-Output "Windows portable output: $publish"
Write-Output "Windows installer: $(Join-Path $root 'installer-output\CodexConversationManager-Setup.exe')"
