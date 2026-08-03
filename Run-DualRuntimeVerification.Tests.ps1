$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$verificationPath = Join-Path $scriptRoot 'Run-DualRuntimeVerification.ps1'

function New-VerificationTestDirectory {
    $path = Join-Path ([IO.Path]::GetTempPath()) ('ff7-verification-gate-test-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path | Out-Null
    return $path
}

function New-ReleaseReadyParityMatrix {
    param([Parameter(Mandatory=$true)] [string] $Directory)

    $sourcePath = Join-Path $scriptRoot 'analysis\dual_runtime\parity-matrix.json'
    $matrix = [IO.File]::ReadAllText($sourcePath) | ConvertFrom-Json
    $matrix.runtimes.steam2026X64.releaseStatus = 'supported'
    foreach ($capability in @($matrix.capabilities)) {
        $capability.x64SpeechEnabled = $true
    }
    $matrix.releaseGate.steam2026X64Ready = $true
    $matrix.releaseGate.blockingCapabilities = @()
    $matrix.releaseGate.requiredUserLedValidation = $true
    $matrix.releaseGate.userLedValidationComplete = $true

    $path = Join-Path $Directory 'release-ready-parity-matrix.json'
    [IO.File]::WriteAllText($path, ($matrix | ConvertTo-Json -Depth 12))
    return $path
}

function New-AlignedPackageEvidence {
    return [pscustomobject]@{
        Fingerprint = 'FAKE-FINGERPRINT'
        ModId = 'ff7.accessibility.reloaded'
        X86EntryPath = 'x86/Ff7.Accessibility.Reloaded.dll'
        X64EntryPath = 'x64/Ff7.Accessibility.Steam2026X64.dll'
        SupportedAppIds = @('ff7_en.exe', 'FFVII.exe')
        ModDependencies = @('reloaded.sharedlib.hooks')
        X86EntryMachine = 0x014C
        X64EntryMachine = 0x8664
        X86CoreMachine = 0x014C
        X64CoreMachine = 0x8664
        X86LegacyLayoutMachine = 0x014C
        X64LegacyLayoutMachine = 0x8664
        X86AbstractionsMachine = 0x014C
        X64AbstractionsMachine = 0x8664
        X86PrismMachine = 0x014C
        X64PrismMachine = 0x8664
        X86PhononMachine = 0x014C
        X64PhononMachine = 0x8664
    }
}

Describe 'Run-DualRuntimeVerification' {
    It 'runs the complete gate in order and cleans its unique package staging directory' {
        $fixture = New-VerificationTestDirectory
        try {
            $invocations = New-Object 'System.Collections.Generic.List[string]'
            $capturedCommands = New-Object 'System.Collections.Generic.List[object]'
            $inspectedPaths = New-Object 'System.Collections.Generic.List[string]'
            $commandInvoker = {
                param($Command)

                $invocations.Add([string]$Command.Name)
                $capturedCommands.Add($Command)
                if ($Command.Name -eq 'Build dual-runtime package') {
                    $outputIndex = [Array]::IndexOf([object[]]$Command.Arguments, '-OutputPath')
                    $packagePath = [string]$Command.Arguments[$outputIndex + 1]
                    New-Item -ItemType Directory -Path $packagePath -Force | Out-Null
                    [IO.File]::WriteAllText((Join-Path $packagePath 'fake-package.marker'), 'verified-fixture')
                }
                return [pscustomobject]@{
                    ExitCode = 0
                    Output = @("fake output: $($Command.Name)")
                }
            }.GetNewClosure()
            $packageInspector = {
                param($PackagePath)

                $inspectedPaths.Add([string]$PackagePath)
                if (-not (Test-Path -LiteralPath (Join-Path $PackagePath 'fake-package.marker') -PathType Leaf)) {
                    throw 'Fake package marker is missing.'
                }
                return [pscustomobject]@{
                    Fingerprint = 'FAKE-FINGERPRINT'
                    X86EntryMachine = 0x014C
                    X64EntryMachine = 0x8664
                    X86PrismMachine = 0x014C
                    X64PrismMachine = 0x8664
                    X86PhononMachine = 0x014C
                    X64PhononMachine = 0x8664
                }
            }.GetNewClosure()

            $result = & $verificationPath -CommandInvoker $commandInvoker `
                -PackageInspector $packageInspector -TempParent $fixture

            @($invocations) | Should Be @(
                'Shared.Tests',
                'Reloaded.Tests',
                'Steam2026X64.Tests',
                'Parity.Tests',
                'Setup.Tests',
                'Verification gate Pester',
                'Setup preflight Pester',
                'Release builder Pester',
                'Installer Pester pass 1',
                'Installer Pester pass 2',
                'Launcher lifecycle Pester pass 1',
                'Launcher lifecycle Pester pass 2',
                'Installer entrypoint Pester pass 1',
                'Installer entrypoint Pester pass 2',
                '7th Heaven CrashGuardSmokeTests',
                'Build dual-runtime package'
            )
            $result.Mode | Should Be 'Research'
            $result.VerificationSucceeded | Should Be $true
            $result.ReleaseReady | Should Be $false
            $result.PSObject.Properties['Succeeded'] | Should Be $null
            $result.Steps.Count | Should Be 16
            $result.Package.Fingerprint | Should Be 'FAKE-FINGERPRINT'
            $result.Package.X86EntryMachine | Should Be 0x014C
            $result.Package.X64EntryMachine | Should Be 0x8664
            $result.Package.X86PrismMachine | Should Be 0x014C
            $result.Package.X64PrismMachine | Should Be 0x8664
            $result.PackageStagingCleaned | Should Be $true
            $pesterCommands = @($capturedCommands | Where-Object { $_.Name -like '*Pester*' })
            $pesterCommands.Count | Should Be 9
            foreach ($pesterCommand in $pesterCommands) {
                $pesterCommand.Arguments -is [object[]] | Should Be $true
                [string]$pesterCommand.Arguments[-1] | Should Match 'Invoke-Pester -Script '
                [string]$pesterCommand.Arguments[-1] | Should Not Match 'Invoke-Pester -Path '
            }
            $inspectedPaths.Count | Should Be 1
            $inspectedPaths[0].StartsWith([IO.Path]::GetFullPath($fixture), [StringComparison]::OrdinalIgnoreCase) |
                Should Be $true
            @(Get-ChildItem -LiteralPath $fixture -Force).Count | Should Be 0
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'fails strict Release mode before commands package inspection or staging when the current gate is closed' {
            $fixture = New-VerificationTestDirectory
        try {
            $invocations = New-Object 'System.Collections.Generic.List[string]'
            $inspectedPaths = New-Object 'System.Collections.Generic.List[string]'
            $commandInvoker = {
                param($Command)
                $invocations.Add([string]$Command.Name)
                return [pscustomobject]@{ ExitCode = 0; Output = @() }
            }.GetNewClosure()
            $packageInspector = {
                param($PackagePath)
                $inspectedPaths.Add([string]$PackagePath)
                return New-AlignedPackageEvidence
            }.GetNewClosure()

            { & $verificationPath -Mode release -CommandInvoker $commandInvoker `
                -PackageInspector $packageInspector -TempParent $fixture } |
                Should Throw 'Native Steam 2026 release gate is closed'
            @($invocations).Count | Should Be 0
            $inspectedPaths.Count | Should Be 0
            @(Get-ChildItem -LiteralPath $fixture -Force).Count | Should Be 0
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'resolves the default parity matrix in a fresh PowerShell File invocation' {
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $output = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass `
                -File $verificationPath -Mode Release 2>&1)
            $exitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
        $exitCode | Should Not Be 0
        ($output -join "`n") | Should Match 'Native Steam 2026 release gate is closed'
        ($output -join "`n") | Should Not Match 'Join-Path'
    }

    It 'runs open Release mode through real gate and exact backend package profile alignment' {
        $fixture = New-VerificationTestDirectory
        try {
            $matrixPath = New-ReleaseReadyParityMatrix -Directory $fixture
            $invocations = New-Object 'System.Collections.Generic.List[string]'
            $commandInvoker = {
                param($Command)

                $invocations.Add([string]$Command.Name)
                if ($Command.Name -eq 'Build dual-runtime package') {
                    $outputIndex = [Array]::IndexOf([object[]]$Command.Arguments, '-OutputPath')
                    New-Item -ItemType Directory -Path ([string]$Command.Arguments[$outputIndex + 1]) -Force | Out-Null
                }
                return [pscustomobject]@{ ExitCode = 0; Output = @() }
            }.GetNewClosure()
            $packageInspector = {
                param($PackagePath)
                return New-AlignedPackageEvidence
            }

            $result = & $verificationPath -Mode Release -ParityMatrixPath $matrixPath `
                -CommandInvoker $commandInvoker -PackageInspector $packageInspector -TempParent $fixture

            $result.Mode | Should Be 'Release'
            $result.VerificationSucceeded | Should Be $true
            $result.ReleaseReady | Should Be $true
            $result.PSObject.Properties['Succeeded'] | Should Be $null
            $result.ReleaseAlignment.IsAligned | Should Be $true
            $result.ReleaseAlignment.RuntimeId | Should Be 'ff7-steam-2026-x64'
            $result.ReleaseAlignment.X64EntryPath | Should Be 'x64/Ff7.Accessibility.Steam2026X64.dll'
            $result.ReleaseAlignment.ProfileAppId | Should Be 'FFVII.exe'
            $result.PackageStagingCleaned | Should Be $true
            @($invocations)[-1] | Should Be 'Build dual-runtime package'
            @(Get-ChildItem -LiteralPath $fixture -Force).Count | Should Be 1
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'fails mismatched Release alignment and cleans package staging' {
        $fixture = New-VerificationTestDirectory
        try {
            $matrixPath = New-ReleaseReadyParityMatrix -Directory $fixture
            $commandInvoker = {
                param($Command)
                if ($Command.Name -eq 'Build dual-runtime package') {
                    $outputIndex = [Array]::IndexOf([object[]]$Command.Arguments, '-OutputPath')
                    New-Item -ItemType Directory -Path ([string]$Command.Arguments[$outputIndex + 1]) -Force | Out-Null
                }
                return [pscustomobject]@{ ExitCode = 0; Output = @() }
            }
            $packageInspector = {
                param($PackagePath)
                $evidence = New-AlignedPackageEvidence
                $evidence.X64EntryPath = 'x64/Wrong.Backend.dll'
                return $evidence
            }

            { & $verificationPath -Mode Release -ParityMatrixPath $matrixPath `
                -CommandInvoker $commandInvoker -PackageInspector $packageInspector -TempParent $fixture } |
                Should Throw 'Release artifact alignment failed'
            @(Get-ChildItem -LiteralPath $fixture -Force).Count | Should Be 1
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'fails at the first nonzero command without running later steps' {
        $fixture = New-VerificationTestDirectory
        try {
            $invocations = New-Object 'System.Collections.Generic.List[string]'
            $commandInvoker = {
                param($Command)

                $invocations.Add([string]$Command.Name)
                if ($Command.Name -eq 'Reloaded.Tests') {
                    return [pscustomobject]@{ ExitCode = 17; Output = @('controlled failure') }
                }
                return [pscustomobject]@{ ExitCode = 0; Output = @('controlled success') }
            }.GetNewClosure()

            { & $verificationPath -CommandInvoker $commandInvoker -TempParent $fixture } |
                Should Throw "Verification step 'Reloaded.Tests' failed with exit code 17"
            @($invocations) | Should Be @('Shared.Tests', 'Reloaded.Tests')
            @(Get-ChildItem -LiteralPath $fixture -Force).Count | Should Be 0
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'supports only the explicit protected-suite and second-repeat skips' {
        $fixture = New-VerificationTestDirectory
        try {
            $invocations = New-Object 'System.Collections.Generic.List[string]'
            $commandInvoker = {
                param($Command)

                $invocations.Add([string]$Command.Name)
                if ($Command.Name -eq 'Build dual-runtime package') {
                    $outputIndex = [Array]::IndexOf([object[]]$Command.Arguments, '-OutputPath')
                    New-Item -ItemType Directory -Path ([string]$Command.Arguments[$outputIndex + 1]) -Force | Out-Null
                }
                return [pscustomobject]@{ ExitCode = 0; Output = @() }
            }.GetNewClosure()
            $packageInspector = {
                param($PackagePath)
                return [pscustomobject]@{ Fingerprint = 'SKIPPED-FIXTURE' }
            }

            $result = & $verificationPath -SkipProtectedSeventhHeaven -SkipSecondPesterRepeat `
                -CommandInvoker $commandInvoker -PackageInspector $packageInspector -TempParent $fixture

            @($invocations) -contains 'Installer Pester pass 2' | Should Be $false
            @($invocations) -contains 'Launcher lifecycle Pester pass 2' | Should Be $false
            @($invocations) -contains 'Installer entrypoint Pester pass 2' | Should Be $false
            @($invocations) -contains '7th Heaven CrashGuardSmokeTests' | Should Be $false
            @($invocations) | Should Be @(
                'Shared.Tests',
                'Reloaded.Tests',
                'Steam2026X64.Tests',
                'Parity.Tests',
                'Setup.Tests',
                'Verification gate Pester',
                'Setup preflight Pester',
                'Release builder Pester',
                'Installer Pester pass 1',
                'Launcher lifecycle Pester pass 1',
                'Installer entrypoint Pester pass 1',
                'Build dual-runtime package'
            )
            $result.Steps.Count | Should Be 12
            @(Get-ChildItem -LiteralPath $fixture -Force).Count | Should Be 0
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'cleans package staging when inspection fails' {
        $fixture = New-VerificationTestDirectory
        try {
            $commandInvoker = {
                param($Command)
                if ($Command.Name -eq 'Build dual-runtime package') {
                    $outputIndex = [Array]::IndexOf([object[]]$Command.Arguments, '-OutputPath')
                    New-Item -ItemType Directory -Path ([string]$Command.Arguments[$outputIndex + 1]) -Force | Out-Null
                }
                return [pscustomobject]@{ ExitCode = 0; Output = @() }
            }
            $packageInspector = { param($PackagePath) throw 'controlled inspection failure' }

            { & $verificationPath -CommandInvoker $commandInvoker `
                -PackageInspector $packageInspector -TempParent $fixture } |
                Should Throw 'controlled inspection failure'
            @(Get-ChildItem -LiteralPath $fixture -Force).Count | Should Be 0
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'contains no deployment profile mutation installer invocation or process launch' {
        $content = [IO.File]::ReadAllText($verificationPath)
        $content | Should Not Match 'Start-Process'
        $content | Should Not Match 'Install-FF7ReloadedMod\.ps1'
        $content | Should Not Match 'Install-Ff7DualRuntimePackage'
        $content | Should Not Match 'Install-Ff7NativeReloadedProfile'
        $content | Should Not Match 'Update-SeventhHeavenSettings'
    }
}
