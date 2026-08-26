$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath
$keepPublish = Join-Path $root 'publish-next-6'
$targets = [System.Collections.Generic.List[string]]::new()

Get-ChildItem -LiteralPath $root -Directory -Force |
    Where-Object { $_.Name -like 'publish*' -and $_.FullName -ne $keepPublish } |
    ForEach-Object { $targets.Add($_.FullName) }

foreach ($sourceRoot in @('src', 'tests')) {
    $fullSourceRoot = Join-Path $root $sourceRoot
    if (Test-Path -LiteralPath $fullSourceRoot) {
        Get-ChildItem -LiteralPath $fullSourceRoot -Directory -Force -Recurse |
            Where-Object { $_.Name -in @('bin', 'obj') } |
            ForEach-Object { $targets.Add($_.FullName) }
    }
}

$logs = Join-Path $root 'logs'
if (Test-Path -LiteralPath $logs) {
    $targets.Add($logs)
}

$targets = $targets | Sort-Object -Unique | ForEach-Object {
    $fullPath = [IO.Path]::GetFullPath($_)
    if (-not $fullPath.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to delete a path outside the manager folder: $fullPath"
    }
    $fullPath
}

foreach ($target in $targets) {
    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}

Write-Host "Cleanup complete. Kept source, test source, build tools, build cache, and publish-next-6."
