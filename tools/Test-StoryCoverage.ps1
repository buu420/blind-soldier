param(
    [Parameter(Mandatory=$true)][string[]] $GameRoots,
    [Parameter(Mandatory=$true)][string] $OutputDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$checkpoints = Join-Path $PSScriptRoot 'StoryCoverageAudit\checkpoints.json'
$results = @()
foreach ($runtime in @('x86', 'x64')) {
    $assembly = if ($runtime -eq 'x86') { 'Reloaded' } else { 'Steam2026X64' }
    $auditProject = Join-Path $PSScriptRoot 'StoryCoverageAudit\StoryCoverageAudit.csproj'
    & dotnet build $auditProject -c Release "-p:AuditRuntime=$runtime" -v:q
    if ($LASTEXITCODE -ne 0) { throw "$runtime audit build failed." }
    $testProject = Join-Path $projectRoot "Ff7.Accessibility.$assembly.Tests\Ff7.Accessibility.$assembly.Tests.csproj"
    & dotnet build $testProject -c Release -v:q
    if ($LASTEXITCODE -ne 0) { throw "$runtime story tests build failed." }
    $audit = Join-Path $PSScriptRoot "StoryCoverageAudit\bin\Release\$runtime\net8.0-windows\StoryCoverageAudit.exe"
    $test = Join-Path $projectRoot "Ff7.Accessibility.$assembly.Tests\bin\Release\net8.0-windows\Ff7.Accessibility.$assembly.Tests.exe"
    for ($index = 0; $index -lt $GameRoots.Count; $index++) {
        $archive = [IO.Path]::GetFullPath($GameRoots[$index])
        if (-not (Test-Path -LiteralPath (Join-Path $archive 'data\field\flevel.lgp'))) {
            throw "Licensed field archive missing: $archive"
        }
        $report = Join-Path $outputRoot "$runtime-archive-$index.json"
        & $audit $archive $report $checkpoints
        $auditExit = $LASTEXITCODE
        $document = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        if ($document.runtime -ne $runtime -or $document.runtimeAssembly -ne "Ff7.Accessibility.$assembly") {
            throw "The $runtime audit loaded the wrong shipping assembly."
        }
        $previousDataRoot = $env:FF7_ACCESSIBILITY_DATA_ROOT
        $previousSourceRoot = $env:FF7_ACCESSIBILITY_SOURCE_ROOT
        try {
            $env:FF7_ACCESSIBILITY_DATA_ROOT = $archive
            $env:FF7_ACCESSIBILITY_SOURCE_ROOT = $projectRoot
            $testLog = Join-Path $outputRoot "$runtime-archive-$index-tests.log"
            & $test --story-coverage-only *> $testLog
            $testExit = $LASTEXITCODE
        }
        finally {
            $env:FF7_ACCESSIBILITY_DATA_ROOT = $previousDataRoot
            $env:FF7_ACCESSIBILITY_SOURCE_ROOT = $previousSourceRoot
        }
        $results += [pscustomobject]@{Runtime=$runtime; Archive=$archive; AuditExit=$auditExit;
            TestExit=$testExit; Report=$report; TestLog=$testLog; Checkpoints=$document.checkpoints.Count}
        if ($auditExit -ne 0 -or $testExit -ne 0) {
            $document.failures | Write-Output
            Get-Content -LiteralPath $testLog -Tail 12
        }
    }
}
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'results.json') -Encoding UTF8
if (@($results | Where-Object { $_.AuditExit -ne 0 -or $_.TestExit -ne 0 }).Count) {
    throw 'Story verification failed. See results.json and individual reports.'
}
Write-Output "Story verification passed for $($results.Count) runtime/archive combinations."
