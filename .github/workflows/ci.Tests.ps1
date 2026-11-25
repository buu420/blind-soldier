$ErrorActionPreference = 'Stop'

$workflowPath = Join-Path $PSScriptRoot 'ci.yml'

function Invoke-StaticGuardHostFixture {
    param([int]$FailedCount, [int]$NativeExitCode)
    $workflow = [IO.File]::ReadAllText($workflowPath)
    $match = [regex]::Match($workflow, '(?ms)^      - name: Run the static guards and workflow contracts\r?\n        shell: powershell\r?\n        run: \|\r?\n(?<body>.*?)(?=^      - name:)')
    if (-not $match.Success) { throw 'Cannot find the actual static-guard workflow step.' }
    $body = [regex]::Replace($match.Groups['body'].Value, '(?m)^          ', '')
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $fixtureRoot = Join-Path $temporaryRoot ('blind-soldier-ci-exit-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    try {
        $scriptPath = Join-Path $fixtureRoot 'guard.ps1'
        # Only the external Pester dependency is controlled. Execute the real
        # workflow body and GitHub Actions' native-exit footer in a fresh host.
        $preamble = @'
$ErrorActionPreference = 'Stop'
function Import-Module { param($Name, $RequiredVersion, [switch]$Force) }
function Invoke-Pester {
    param($Script, [switch]$PassThru)
    $global:LASTEXITCODE = NATIVE_EXIT_CODE
    [pscustomobject]@{FailedCount=FAILED_COUNT}
}
'@
        $preamble = $preamble.Replace('NATIVE_EXIT_CODE', [string]$NativeExitCode).Replace('FAILED_COUNT', [string]$FailedCount)
        $footer = "`nif (Test-Path -LiteralPath variable:LASTEXITCODE) { exit `$LASTEXITCODE }`n"
        [IO.File]::WriteAllText($scriptPath, $preamble + "`n" + $body + $footer, (New-Object Text.UTF8Encoding($false)))
        $start = New-Object Diagnostics.ProcessStartInfo
        $start.FileName = (Get-Command powershell.exe -CommandType Application).Source
        $start.Arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $scriptPath + '"'
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($start)
        try {
            $null = $process.StandardOutput.ReadToEnd()
            $null = $process.StandardError.ReadToEnd()
            $process.WaitForExit()
            return $process.ExitCode
        } finally { $process.Dispose() }
    } finally {
        $resolved = [IO.Path]::GetFullPath($fixtureRoot)
        if (-not $resolved.StartsWith($temporaryRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
            -not (Split-Path -Leaf $resolved).StartsWith('blind-soldier-ci-exit-')) { throw 'Unexpected CI fixture cleanup target.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

# The release workflow only fires on a v* tag, so until this job existed nothing
# ran on a push or a pull request. A stale branch carrying the one-byte
# PrismConfig could merge without a single check, and the regression would stay
# invisible until someone tagged. This contract pins what the merge-time job has
# to do so it cannot quietly stop doing it.
Describe 'Blind Soldier merge-time CI contract' {
    It 'runs on pull requests and branch pushes, and leaves tags to the release workflow' {
        Test-Path -LiteralPath $workflowPath | Should Be $true
        $workflow = [IO.File]::ReadAllText($workflowPath)

        $workflow | Should Match '(?m)^\s*pull_request:'
        $workflow | Should Match '(?m)^\s*push:\s*\r?\n\s*branches:'
        $workflow | Should Not Match '(?m)^\s*tags:'
    }

    It 'asks for nothing more than read access' {
        $workflow = [IO.File]::ReadAllText($workflowPath)
        $workflow | Should Match '(?m)^permissions:\s*\r?\n\s*contents:\s*read'
        $workflow | Should Not Match 'contents:\s*write'
    }

    It 'provisions the same toolchain the release gate uses' {
        $workflow = [IO.File]::ReadAllText($workflowPath)
        $workflow | Should Match 'architecture:\s*x64'
        $workflow | Should Match 'architecture:\s*x86'
        $workflow | Should Match ([regex]::Escape('Install-Module -Name Pester -RequiredVersion 4.10.1'))
        $workflow | Should Match ([regex]::Escape('Import-Module Pester -RequiredVersion 4.10.1'))
    }

    It 'runs the Prism ABI contract and the guards that pin the release gate' {
        $workflow = [IO.File]::ReadAllText($workflowPath)
        foreach ($suite in @(
            './PrismAbiContract.Tests.ps1',
            './Run-DualRuntimeVerification.Tests.ps1',
            './Build-AccessibleLauncherBundle.Tests.ps1',
            './Build-Accessible2013LauncherBundle.Tests.ps1',
            './FF7Launcher2013Install.Tests.ps1',
            './Amm2013LauncherLifecycle.Tests.ps1',
            './Build-BlindSoldierAmmSources.Tests.ps1',
            './.github/workflows/release.Tests.ps1',
            './.github/workflows/ci.Tests.ps1')) {
            $workflow | Should Match ([regex]::Escape($suite))
        }
    }

    It 'runs the native Prism probe and the dual-runtime source guard' {
        $workflow = [IO.File]::ReadAllText($workflowPath)
        # The launcher tests carry the probe that round-trips PrismConfig through
        # the shipped DLL; the Reloaded tests carry the guard that fails when a
        # reader or tracker is compiled into one runtime and not the other.
        $workflow | Should Match ([regex]::Escape(
            'launcher/Ff7.Launcher.Accessible.Tests/FFVII_LAUNCHER.Accessibility.Tests.csproj'))
        $workflow | Should Match ([regex]::Escape(
            'launcher/Ff7.Launcher2013.Accessible.Tests/FF7_Launcher.Accessibility2013.Tests.csproj'))
        $workflow | Should Match ([regex]::Escape('--dual-runtime-sources-only'))
    }

    It 'probes the Prism ABI on both mod runtimes, not only the launcher' {
        $workflow = [IO.File]::ReadAllText($workflowPath)
        # This is a dual-runtime mod. The x86 and x64 runtimes ship different
        # prism.dll builds, and the x64 layout had only ever been computed until a
        # probe measured it. Each runtime's test host has to round-trip the struct
        # through its own DLL.
        foreach ($project in @(
            'Ff7.Accessibility.Reloaded.Tests/Ff7.Accessibility.Reloaded.Tests.csproj',
            'Ff7.Accessibility.Steam2026X64.Tests/Ff7.Accessibility.Steam2026X64.Tests.csproj')) {
            $workflow | Should Match ([regex]::Escape($project) + '[^\r\n]*--prism-abi-probe-only')
        }
    }

    It 'fails the job when any Pester suite fails' {
        $workflow = [IO.File]::ReadAllText($workflowPath)
        $workflow | Should Match 'FailedCount'
    }

    It 'returns success when every Pester suite passes after an expected failing native fixture' {
        (Invoke-StaticGuardHostFixture -FailedCount 0 -NativeExitCode 1) | Should Be 0
    }

    It 'still fails when a Pester suite fails even if the last native fixture passed' {
        (Invoke-StaticGuardHostFixture -FailedCount 1 -NativeExitCode 0) | Should Not Be 0
    }

    It 'runs story state coverage in both shipping assemblies' {
        $workflow = [IO.File]::ReadAllText($workflowPath)
        foreach ($runtime in @('Reloaded', 'Steam2026X64')) {
            $project = "Ff7.Accessibility.$runtime.Tests/Ff7.Accessibility.$runtime.Tests.csproj"
            $workflow | Should Match ([regex]::Escape($project) + '[^\r\n]*--story-coverage-only')
        }
        $workflow | Should Match ([regex]::Escape('./tools/Complete-StoryCatalog.Tests.ps1'))
    }
}
