$ErrorActionPreference = 'Stop'

$workflowPath = Join-Path $PSScriptRoot 'ci.yml'

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
        $workflow | Should Match ([regex]::Escape('--dual-runtime-sources-only'))
    }

    It 'fails the job when any Pester suite fails' {
        $workflow = [IO.File]::ReadAllText($workflowPath)
        $workflow | Should Match 'FailedCount'
    }
}
