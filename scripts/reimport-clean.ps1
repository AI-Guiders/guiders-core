# Wipe and re-import all Core packages (idempotent).
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
foreach ($d in @('src','tests')) {
    $path = Join-Path $Root $d
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
    New-Item -ItemType Directory -Path $path | Out-Null
}
& (Join-Path $PSScriptRoot 'import-core-packages.ps1')
& (Join-Path $PSScriptRoot 'fix-references.ps1')
Write-Host 'Clean re-import done.'
