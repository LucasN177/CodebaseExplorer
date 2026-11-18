#Requires -Version 7
<#
.SYNOPSIS
    Runs the QA-002 performance benchmarks of Codebase Explorer.

.DESCRIPTION
    Builds the solution in Release and runs the explicit benchmark tests. Each benchmark generates a synthetic
    repository into a temporary folder (tests/CodebaseExplorer.TestRepositories/SyntheticRepository.cs), imports it
    through the real composition root (scan, all analyzers, SQLite in a temporary data directory) and projects the
    first graph. Results are printed; record them in docs/implementation/TEST_STRATEGY.md §8.
    Generated repositories are deleted afterwards and never committed.

.PARAMETER Scenario
    All (default), DotNet100, DotNet2000, TypeScript50k or ModelStore (STORE-004: 1,000,000 relationships).

.EXAMPLE
    pwsh tools/development/Run-Benchmarks.ps1 -Scenario DotNet2000
#>
[CmdletBinding()]
param(
    [ValidateSet('All', 'DotNet100', 'DotNet2000', 'TypeScript50k', 'ModelStore')]
    [string] $Scenario = 'All'
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..' '..')

$runs = switch ($Scenario) {
    'DotNet100' { , @('tests/CodebaseExplorer.IntegrationTests', '--filter-method', '*DotNet_100Projects*') }
    'DotNet2000' { , @('tests/CodebaseExplorer.IntegrationTests', '--filter-method', '*DotNet_2000Projects*') }
    'TypeScript50k' { , @('tests/CodebaseExplorer.IntegrationTests', '--filter-method', '*TypeScript_50000Files*') }
    'ModelStore' { , @('tests/CodebaseExplorer.Storage.Tests', '--filter-method', '*SaveOneMillionRelationships*') }
    'All' {
        @('tests/CodebaseExplorer.IntegrationTests', '--filter-class', '*PerformanceBenchmarks'),
        @('tests/CodebaseExplorer.Storage.Tests', '--filter-method', '*SaveOneMillionRelationships*')
    }
}

dotnet build (Join-Path $root 'CodebaseExplorer.slnx') --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$failed = 0
foreach ($run in $runs) {
    $project, $filterOption, $filter = $run
    Write-Host "== $project $filter" -ForegroundColor Cyan
    dotnet test --project (Join-Path $root $project) --configuration Release --no-build $filterOption $filter --explicit only --output Detailed
    if ($LASTEXITCODE -ne 0) { $failed++ }
}

exit $failed
