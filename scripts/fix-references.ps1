# Rewire ProjectReference paths for guiders-core monorepo layout.
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent

# tests/* -> ../../src/<lib>/<lib>.csproj
Get-ChildItem (Join-Path $Root 'tests') -Filter '*.csproj' -Recurse | ForEach-Object {
    $testDir = $_.Directory.Name
    if (-not $testDir.EndsWith('.Tests')) { return }
    $lib = $testDir.Substring(0, $testDir.Length - '.Tests'.Length)
    $newRef = "..\..\src\$lib\$lib.csproj"
    $text = Get-Content $_.FullName -Raw
    $updated = $text -replace 'Include="\.\.\\[^"]+\.csproj"', "Include=`"$newRef`""
    if ($updated -ne $text) {
        Set-Content -Path $_.FullName -Value $updated -NoNewline
        Write-Host "fixed test: $testDir"
    }
}

$replacements = @{
    '..\cdp-evidence\Cdp.Evidence.csproj' = '..\Cdp.Evidence\Cdp.Evidence.csproj'
    '..\dotnet-build-test-parsers\DotNetBuildTestParsers.csproj' = '..\DotNetBuildTestParsers\DotNetBuildTestParsers.csproj'
    '..\..\agent-notes-core\AgentNotes.Core.csproj' = '..\AgentNotes.Core\AgentNotes.Core.csproj'
    "Exists('..\..\agent-notes-core\AgentNotes.Core.csproj')" = "Exists('..\AgentNotes.Core\AgentNotes.Core.csproj')"
}

Get-ChildItem (Join-Path $Root 'src') -Filter '*.csproj' -Recurse | ForEach-Object {
    $text = Get-Content $_.FullName -Raw
    $updated = $text
    foreach ($k in $replacements.Keys) {
        $updated = $updated.Replace($k, $replacements[$k])
    }
    if ($updated -ne $text) {
        Set-Content -Path $_.FullName -Value $updated -NoNewline
        Write-Host "fixed src: $($_.Name)"
    }
}

Write-Host 'References rewired.'
