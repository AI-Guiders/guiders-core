# Strip per-csproj repo URLs (now in src/Directory.Build.props) and stale KarataevDmitry comments.
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
$stripProps = @(
    'PackageProjectUrl', 'RepositoryUrl', 'RepositoryType', 'PublishRepositoryUrl',
    'EmbedUntrackedSources', 'IncludeSymbols', 'SymbolPackageFormat', 'PackageLicenseExpression'
)
Get-ChildItem (Join-Path $Root 'src') -Filter '*.csproj' -Recurse | ForEach-Object {
    $lines = Get-Content $_.FullName
    $out = foreach ($line in $lines) {
        $drop = $false
        foreach ($p in $stripProps) {
            if ($line -match "<$p>") { $drop = $true; break }
        }
        if ($line -match 'KarataevDmitry|Trusted Publishing привязывай') { $drop = $true }
        if (-not $drop) { $line }
    }
    $text = ($out -join "`n").TrimEnd() + "`n"
  Set-Content -Path $_.FullName -Value $text -NoNewline
    Write-Host "normalized: $($_.Name)"
}
