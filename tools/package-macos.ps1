$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root 'build-tools\dotnet\dotnet.exe'
$project = Join-Path $root 'src\CodexConversationManager.Mac\CodexConversationManager.Mac.csproj'
$version = '0.2.4'
if (-not (Test-Path -LiteralPath $dotnet)) { throw "Bundled .NET SDK not found: $dotnet" }

foreach ($rid in @('osx-arm64', 'osx-x64')) {
    $name = if ($rid -eq 'osx-arm64') { 'arm64' } else { 'x64' }
    $publish = Join-Path $root "publish-macos-$name"
    $app = Join-Path $publish 'Codex Conversation Manager.app'
    $contents = Join-Path $app 'Contents'
    $macos = Join-Path $contents 'MacOS'
    $resources = Join-Path $contents 'Resources'
    if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
    New-Item -ItemType Directory -Path $macos, $resources -Force | Out-Null
    $staging = Join-Path $root "build-cache\macos-$name"
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    & $dotnet publish $project -c Release -r $rid --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $staging --no-restore
    if ($LASTEXITCODE -ne 0) { throw "macOS publish failed for $rid." }
    $binary = Join-Path $macos 'CodexConversationManager.Mac'
    Copy-Item -LiteralPath (Join-Path $staging 'CodexConversationManager.Mac') -Destination $binary
    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleDisplayName</key><string>Codex Conversation Manager</string>
<key>CFBundleExecutable</key><string>CodexConversationManager.Mac</string>
<key>CFBundleIdentifier</key><string>com.codexconversationmanager.mac</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>$version</string>
<key>CFBundleVersion</key><string>$version</string>
</dict></plist>
"@
    Set-Content -LiteralPath (Join-Path $contents 'Info.plist') -Value $plist -Encoding utf8
    Write-Output "macOS app bundle: $app"
}
