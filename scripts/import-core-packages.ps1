# One-shot import of sibling *-core repos into guiders-core monorepo (GUIDERS-CORE-0001).
$ErrorActionPreference = 'Stop'
$Open = 'd:\Experiments\PersonalCursorFolder\Financial\software\open'
$Root = Split-Path $PSScriptRoot -Parent
$Src = Join-Path $Root 'src'
$Tests = Join-Path $Root 'tests'

function Copy-LibRoot {
    param(
        [string]$From,
        [string]$DestName,
        [string[]]$ExcludeDir = @('bin','obj','.git','tools','.github','.cascade','docs')
    )
    if (-not (Test-Path $From)) { throw "Missing source: $From" }
    $dest = Join-Path $Src $DestName
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Get-ChildItem $From -Force | Where-Object {
        if ($_.Name -in $ExcludeDir) { return $false }
        if ($_.PSIsContainer -and $_.Name -like '*Tests*') { return $false }
        if ($_.Name -like '*.sln*') { return $false }
        return $true
    } | ForEach-Object {
        $target = Join-Path $dest $_.Name
        if ($_.PSIsContainer) {
            Copy-Item $_.FullName $target -Recurse -Force
        } else {
            Copy-Item $_.FullName $target -Force
        }
    }
    Write-Host "  lib  -> src/$DestName"
}

function Copy-Tests {
    param([string]$FromTestsDir, [string]$DestTestName)
    if (-not (Test-Path $FromTestsDir)) { return }
    $dest = Join-Path $Tests $DestTestName
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Get-ChildItem $FromTestsDir -Force | Where-Object { $_.Name -notin @('bin','obj') } | ForEach-Object {
        $target = Join-Path $dest $_.Name
        Copy-Item $_.FullName $target -Recurse -Force
    }
    Write-Host "  test -> tests/$DestTestName"
}

$flat = @(
    @{ Repo = 'agent-notes-core';           Name = 'AgentNotes.Core' }
    @{ Repo = 'agent-findings-core';        Name = 'AgentFindings.Core' }
    @{ Repo = 'agent-failures-core';        Name = 'AgentFailures.Core' }
    @{ Repo = 'agent-task-knowledge-core'; Name = 'AgentTaskKnowledge.Core' }
    @{ Repo = 'cdp-core';                   Name = 'Cdp.Core' }
    @{ Repo = 'cdp-evidence';              Name = 'Cdp.Evidence' }
    @{ Repo = 'cdp-scriptable-ide';         Name = 'Cdp.ScriptableIde' }
    @{ Repo = 'dotnet-debug-core';          Name = 'DotnetDebug.Core' }
    @{ Repo = 'dotnet-build-test-core';     Name = 'DotNetBuildTest.Core' }
    @{ Repo = 'dotnet-build-test-parsers'; Name = 'DotNetBuildTestParsers' }
    @{ Repo = 'git-mcp-core';               Name = 'GitMcp.Core' }
    @{ Repo = 'hybrid-codebase-index-core'; Name = 'HybridCodebaseIndex.Core' }
    @{ Repo = 'roslyn-mcp-core';            Name = 'RoslynMcp.Core' }
    @{ Repo = 'terminal-mcp-core';          Name = 'TerminalMcp.Core' }
    @{ Repo = 'cdp-ignite-client';         Name = 'Cdp.Ignite.Client' }
)

foreach ($p in $flat) {
    Write-Host $p.Repo
    Copy-LibRoot -From (Join-Path $Open $p.Repo) -DestName $p.Name
    $testDir = Join-Path (Join-Path $Open $p.Repo) ($p.Name + '.Tests')
    if ($p.Name -eq 'Cdp.Ignite.Client') {
        $testDir = Join-Path (Join-Path $Open $p.Repo) 'Cdp.Ignite.Client.Tests'
    }
    Copy-Tests -FromTestsDir $testDir -DestTestName ($p.Name + '.Tests')
}

Write-Host 'nested packages'
Copy-LibRoot -From (Join-Path $Open 'typescript-lang\TypescriptLang.Core') -DestName 'TypescriptLang.Core'
Copy-LibRoot -From (Join-Path $Open 'lsp-lang\Cdp.Lsp.Core') -DestName 'Cdp.Lsp.Core'
Copy-LibRoot -From (Join-Path $Open 'agent-notes-mcp\AgentNotes.Mcp.Hosting') -DestName 'AgentNotes.Mcp.Hosting'

Write-Host "Done. Root: $Root"
