[CmdletBinding()]
param(
    [ValidateSet('Research', 'Release')]
    [string] $Mode = 'Research',

    [string] $ParityMatrixPath,

    [switch] $SkipProtectedSeventhHeaven,
    [switch] $SkipSecondPesterRepeat,

    [Parameter(DontShow=$true)]
    [scriptblock] $CommandInvoker,

    [Parameter(DontShow=$true)]
    [scriptblock] $PackageInspector,

    [Parameter(DontShow=$true)]
    [string] $TempParent = [IO.Path]::GetTempPath()
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$prototypeRoot = Split-Path -Parent $scriptRoot
if ([string]::IsNullOrWhiteSpace($ParityMatrixPath)) {
    $ParityMatrixPath = Join-Path $scriptRoot 'analysis\dual_runtime\parity-matrix.json'
}

function New-VerificationCommand {
    param(
        [Parameter(Mandatory=$true)] [string] $Name,
        [Parameter(Mandatory=$true)] [string] $FilePath,
        [Parameter(Mandatory=$true)] [object[]] $Arguments,
        [Parameter(Mandatory=$true)] [string] $WorkingDirectory
    )

    return [pscustomobject]@{
        Name = $Name
        FilePath = $FilePath
        Arguments = $Arguments
        WorkingDirectory = $WorkingDirectory
    }
}

function Invoke-VerificationCommand {
    param(
        [Parameter(Mandatory=$true)] [psobject] $Command,
        [Parameter()] [scriptblock] $Invoker
    )

    Write-Host "[$($Command.Name)] $($Command.FilePath) $($Command.Arguments -join ' ')"
    if ($null -ne $Invoker) {
        $invocationResult = & $Invoker $Command
        if ($null -eq $invocationResult -or
            $null -eq $invocationResult.PSObject.Properties['ExitCode']) {
            throw "CommandInvoker returned no integer exit code for '$($Command.Name)'."
        }
        $exitCode = [int]$invocationResult.ExitCode
        $output = @($invocationResult.Output | ForEach-Object { [string]$_ })
    }
    else {
        Push-Location -LiteralPath $Command.WorkingDirectory
        try {
            $captured = @(& $Command.FilePath @($Command.Arguments) 2>&1)
            $exitCode = $LASTEXITCODE
            $output = @($captured | ForEach-Object { [string]$_ })
        }
        finally {
            Pop-Location
        }
    }

    foreach ($line in $output) {
        Write-Host "[$($Command.Name)] $line"
    }
    if ($exitCode -ne 0) {
        $detail = if ($output.Count -gt 0) { ' Output: ' + ($output -join "`n") } else { '' }
        throw "Verification step '$($Command.Name)' failed with exit code $exitCode.$detail"
    }

    return [pscustomobject]@{
        Name = $Command.Name
        ExitCode = $exitCode
        Output = $output
    }
}

function Get-DualRuntimePackageEvidence {
    param([Parameter(Mandatory=$true)] [string] $PackagePath)

    Import-Module (Join-Path $scriptRoot 'FF7SteamInstall.psm1') -Force
    $installModule = Get-Module FF7SteamInstall
    return & $installModule {
        param($Path)

        $validation = Assert-Ff7DualRuntimePackage -PackagePath $Path
        $config = [IO.File]::ReadAllText((Join-Path $Path 'ModConfig.json')) | ConvertFrom-Json
        [pscustomobject]@{
            Fingerprint = $validation.Fingerprint
            ModId = [string]$config.ModId
            X86EntryPath = [string]$config.ModR2RManagedDll32
            X64EntryPath = [string]$config.ModR2RManagedDll64
            SupportedAppIds = @($config.SupportedAppId | ForEach-Object { [string]$_ })
            ModDependencies = @($config.ModDependencies | ForEach-Object { [string]$_ })
            X86EntryMachine = Get-Ff7PeMachine -Path (Join-Path $Path ([string]$config.ModR2RManagedDll32))
            X64EntryMachine = Get-Ff7PeMachine -Path (Join-Path $Path ([string]$config.ModR2RManagedDll64))
            X86CoreMachine = Get-Ff7PeMachine -Path (Join-Path $Path 'x86\Ff7.Accessibility.Core.dll')
            X64CoreMachine = Get-Ff7PeMachine -Path (Join-Path $Path 'x64\Ff7.Accessibility.Core.dll')
            X86LegacyLayoutMachine = Get-Ff7PeMachine -Path (Join-Path $Path 'x86\Ff7.Accessibility.LegacyLayout.dll')
            X64LegacyLayoutMachine = Get-Ff7PeMachine -Path (Join-Path $Path 'x64\Ff7.Accessibility.LegacyLayout.dll')
            X86AbstractionsMachine = Get-Ff7PeMachine -Path (Join-Path $Path 'x86\Ff7.Accessibility.Runtime.Abstractions.dll')
            X64AbstractionsMachine = Get-Ff7PeMachine -Path (Join-Path $Path 'x64\Ff7.Accessibility.Runtime.Abstractions.dll')
            X86PrismMachine = Get-Ff7PeMachine -Path (Join-Path $Path 'x86\prism.dll')
            X64PrismMachine = Get-Ff7PeMachine -Path (Join-Path $Path 'x64\prism.dll')
            X86PhononMachine = Get-Ff7PeMachine -Path (Join-Path $Path 'x86\phonon.dll')
            X64PhononMachine = Get-Ff7PeMachine -Path (Join-Path $Path 'x64\phonon.dll')
        }
    } $PackagePath
}

function Test-ExactOrderedStringList {
    param(
        [object[]] $Actual,
        [Parameter(Mandatory=$true)] [string[]] $Expected
    )

    $values = @($Actual)
    if ($values.Count -ne $Expected.Count) {
        return $false
    }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if ([string]$values[$index] -cne $Expected[$index]) {
            return $false
        }
    }
    return $true
}

function Assert-ReleaseArtifactAlignment {
    param(
        [Parameter(Mandatory=$true)] [psobject] $GateEvidence,
        [Parameter(Mandatory=$true)] [psobject] $PackageEvidence,
        [Parameter(Mandatory=$true)] [string] $ProfileTemplatePath
    )

    $expectedRuntimeId = 'ff7-steam-2026-x64'
    $expectedRuntimeSha256 = '57A23D166D69E46B9E3339F779D4A3C4FEB402A989FA7291D0D9B4A1953ABB4B'
    if ($GateEvidence.IsReleaseReady -ne $true -or
        [string]$GateEvidence.RuntimeId -cne $expectedRuntimeId -or
        -not ([string]$GateEvidence.RuntimeSha256).Equals(
            $expectedRuntimeSha256,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release artifact alignment failed: parity evidence does not identify the supported Steam 2026 backend.'
    }

    if ([string]$PackageEvidence.ModId -cne 'ff7.accessibility.reloaded' -or
        [string]$PackageEvidence.X86EntryPath -cne 'x86/Ff7.Accessibility.Reloaded.dll' -or
        [string]$PackageEvidence.X64EntryPath -cne 'x64/Ff7.Accessibility.Steam2026X64.dll' -or
        -not (Test-ExactOrderedStringList -Actual @($PackageEvidence.SupportedAppIds) `
            -Expected @('ff7_en.exe', 'FFVII.exe')) -or
        -not (Test-ExactOrderedStringList -Actual @($PackageEvidence.ModDependencies) `
            -Expected @('reloaded.sharedlib.hooks'))) {
        throw 'Release artifact alignment failed: package metadata does not select the expected backends and supported applications.'
    }

    foreach ($machineCheck in @(
        [pscustomobject]@{ Name = 'X86EntryMachine'; Actual = $PackageEvidence.X86EntryMachine; Expected = 0x014C },
        [pscustomobject]@{ Name = 'X64EntryMachine'; Actual = $PackageEvidence.X64EntryMachine; Expected = 0x8664 },
        [pscustomobject]@{ Name = 'X86CoreMachine'; Actual = $PackageEvidence.X86CoreMachine; Expected = 0x014C },
        [pscustomobject]@{ Name = 'X64CoreMachine'; Actual = $PackageEvidence.X64CoreMachine; Expected = 0x8664 },
        [pscustomobject]@{ Name = 'X86LegacyLayoutMachine'; Actual = $PackageEvidence.X86LegacyLayoutMachine; Expected = 0x014C },
        [pscustomobject]@{ Name = 'X64LegacyLayoutMachine'; Actual = $PackageEvidence.X64LegacyLayoutMachine; Expected = 0x8664 },
        [pscustomobject]@{ Name = 'X86AbstractionsMachine'; Actual = $PackageEvidence.X86AbstractionsMachine; Expected = 0x014C },
        [pscustomobject]@{ Name = 'X64AbstractionsMachine'; Actual = $PackageEvidence.X64AbstractionsMachine; Expected = 0x8664 },
        [pscustomobject]@{ Name = 'X86PrismMachine'; Actual = $PackageEvidence.X86PrismMachine; Expected = 0x014C },
        [pscustomobject]@{ Name = 'X64PrismMachine'; Actual = $PackageEvidence.X64PrismMachine; Expected = 0x8664 },
        [pscustomobject]@{ Name = 'X86PhononMachine'; Actual = $PackageEvidence.X86PhononMachine; Expected = 0x014C },
        [pscustomobject]@{ Name = 'X64PhononMachine'; Actual = $PackageEvidence.X64PhononMachine; Expected = 0x8664 }
    )) {
        if ([int]$machineCheck.Actual -ne [int]$machineCheck.Expected) {
            throw "Release artifact alignment failed: $($machineCheck.Name) does not match the required PE architecture."
        }
    }

    if (-not (Test-Path -LiteralPath $ProfileTemplatePath -PathType Leaf)) {
        throw "Release artifact alignment failed: native profile template is unavailable: $ProfileTemplatePath"
    }
    try {
        $profile = [IO.File]::ReadAllText($ProfileTemplatePath) | ConvertFrom-Json
    }
    catch {
        throw "Release artifact alignment failed: native profile template is invalid JSON. $($_.Exception.Message)"
    }

    $requiredMods = @('reloaded.sharedlib.hooks', 'ff7.accessibility.reloaded')
    $profileAligned =
        $profile.AppId -is [string] -and [string]$profile.AppId -ceq 'FFVII.exe' -and
        $profile.AppName -is [string] -and
            [string]$profile.AppName -ceq 'Final Fantasy VII (Steam 2026 Native x64)' -and
        $profile.AppLocation -is [string] -and
            [string]::IsNullOrWhiteSpace([string]$profile.AppLocation) -and
        $profile.WorkingDirectory -is [string] -and
            [string]::IsNullOrWhiteSpace([string]$profile.WorkingDirectory) -and
        $profile.AutoInject -eq $false -and
        $profile.DontInject -eq $false -and
        $profile.IsMsStore -eq $false -and
        $profile.PreserveDisabledModOrder -eq $true -and
        (Test-ExactOrderedStringList -Actual @($profile.EnabledMods) -Expected $requiredMods) -and
        (Test-ExactOrderedStringList -Actual @($profile.SortedMods) -Expected $requiredMods)
    if (-not $profileAligned) {
        throw 'Release artifact alignment failed: native profile template does not match the release injection contract.'
    }

    return [pscustomobject]@{
        IsAligned = $true
        RuntimeId = [string]$GateEvidence.RuntimeId
        RuntimeSha256 = [string]$GateEvidence.RuntimeSha256
        PackageFingerprint = [string]$PackageEvidence.Fingerprint
        X64EntryPath = [string]$PackageEvidence.X64EntryPath
        ProfileAppId = [string]$profile.AppId
        ProfileTemplatePath = [IO.Path]::GetFullPath($ProfileTemplatePath)
    }
}

Import-Module (Join-Path $scriptRoot 'FF7SteamInstall.psm1') -Force
$isReleaseMode = $Mode -ieq 'Release'
$effectiveMode = if ($isReleaseMode) { 'Release' } else { 'Research' }
$parityGate = if ($isReleaseMode) {
    Assert-Ff7NativeParityReleaseGate -ParityMatrixPath $ParityMatrixPath
}
else {
    Assert-Ff7NativeParityReleaseGate -ParityMatrixPath $ParityMatrixPath -AllowResearch
}

$commands = New-Object 'System.Collections.Generic.List[object]'
$commands.Add((New-VerificationCommand -Name 'Shared.Tests' -FilePath 'dotnet' -Arguments @(
    'run', '--project', (Join-Path $scriptRoot 'Ff7.Accessibility.Shared.Tests\Ff7.Accessibility.Shared.Tests.csproj'), '-c', 'Release'
) -WorkingDirectory $scriptRoot))
$commands.Add((New-VerificationCommand -Name 'Reloaded.Tests' -FilePath 'dotnet' -Arguments @(
    'run', '--project', (Join-Path $scriptRoot 'Ff7.Accessibility.Reloaded.Tests\Ff7.Accessibility.Reloaded.Tests.csproj'), '-c', 'Release'
) -WorkingDirectory $scriptRoot))
$commands.Add((New-VerificationCommand -Name 'Steam2026X64.Tests' -FilePath 'dotnet' -Arguments @(
    'run', '--project', (Join-Path $scriptRoot 'Ff7.Accessibility.Steam2026X64.Tests\Ff7.Accessibility.Steam2026X64.Tests.csproj'), '-c', 'Release'
) -WorkingDirectory $scriptRoot))
$commands.Add((New-VerificationCommand -Name 'Parity.Tests' -FilePath 'dotnet' -Arguments @(
    'run', '--project', (Join-Path $scriptRoot 'Ff7.Accessibility.Parity.Tests\Ff7.Accessibility.Parity.Tests.csproj'), '-c', 'Release'
) -WorkingDirectory $scriptRoot))
$commands.Add((New-VerificationCommand -Name 'Setup.Tests' -FilePath 'dotnet' -Arguments @(
    'run', '--project', (Join-Path $scriptRoot 'installer\BlindSwordsman.Setup.Tests\BlindSwordsman.Setup.Tests.csproj'), '-c', 'Release'
) -WorkingDirectory $scriptRoot))

$verificationPesterCommand = "Invoke-Pester -Script '$((Join-Path $scriptRoot 'Run-DualRuntimeVerification.Tests.ps1').Replace("'", "''"))' -EnableExit"
$commands.Add((New-VerificationCommand -Name 'Verification gate Pester' -FilePath 'powershell.exe' -Arguments @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $verificationPesterCommand
) -WorkingDirectory $scriptRoot))

$preflightPesterCommand = "Invoke-Pester -Script '$((Join-Path $scriptRoot 'Invoke-BlindSwordsmanPreflight.Tests.ps1').Replace("'", "''"))' -EnableExit"
$commands.Add((New-VerificationCommand -Name 'Setup preflight Pester' -FilePath 'powershell.exe' -Arguments @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $preflightPesterCommand
) -WorkingDirectory $scriptRoot))

$pesterCommand = "Invoke-Pester -Script '$((Join-Path $scriptRoot 'FF7SteamInstall.Tests.ps1').Replace("'", "''"))' -EnableExit"
$commands.Add((New-VerificationCommand -Name 'Installer Pester pass 1' -FilePath 'powershell.exe' -Arguments @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $pesterCommand
) -WorkingDirectory $scriptRoot))
if (-not $SkipSecondPesterRepeat) {
    $commands.Add((New-VerificationCommand -Name 'Installer Pester pass 2' -FilePath 'powershell.exe' -Arguments @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $pesterCommand
    ) -WorkingDirectory $scriptRoot))
}

$entrypointPesterCommand = "Invoke-Pester -Script '$((Join-Path $scriptRoot 'InstallerEntrypoint.Tests.ps1').Replace("'", "''"))' -EnableExit"
$commands.Add((New-VerificationCommand -Name 'Installer entrypoint Pester pass 1' -FilePath 'powershell.exe' -Arguments @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $entrypointPesterCommand
) -WorkingDirectory $scriptRoot))
if (-not $SkipSecondPesterRepeat) {
    $commands.Add((New-VerificationCommand -Name 'Installer entrypoint Pester pass 2' -FilePath 'powershell.exe' -Arguments @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $entrypointPesterCommand
    ) -WorkingDirectory $scriptRoot))
}

if (-not $SkipProtectedSeventhHeaven) {
    $crashGuardProject = Join-Path $prototypeRoot 'analysis\7th-heaven-4.5.2-patch\CrashGuardSmokeTests\CrashGuardSmokeTests.csproj'
    $commands.Add((New-VerificationCommand -Name '7th Heaven CrashGuardSmokeTests' -FilePath 'dotnet' -Arguments @(
        'run', '--project', $crashGuardProject, '-c', 'Release'
    ) -WorkingDirectory (Split-Path -Parent $crashGuardProject)))
}

$stepResults = New-Object 'System.Collections.Generic.List[object]'
foreach ($command in $commands) {
    $stepResults.Add((Invoke-VerificationCommand -Command $command -Invoker $CommandInvoker))
}

if (-not (Test-Path -LiteralPath $TempParent -PathType Container)) {
    New-Item -ItemType Directory -Path $TempParent -Force | Out-Null
}
$verificationRoot = Join-Path ([IO.Path]::GetFullPath($TempParent)) `
    ('ff7-dual-runtime-verification-' + [Guid]::NewGuid().ToString('N'))
$packagePath = Join-Path $verificationRoot 'ff7.accessibility.reloaded'
$packageEvidence = $null
$releaseAlignment = $null
try {
    New-Item -ItemType Directory -Path $verificationRoot | Out-Null
    $buildCommand = New-VerificationCommand -Name 'Build dual-runtime package' `
        -FilePath 'powershell.exe' -Arguments @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            (Join-Path $scriptRoot 'Build-DualRuntimePackage.ps1'),
            '-OutputPath',
            $packagePath
        ) -WorkingDirectory $scriptRoot
    $stepResults.Add((Invoke-VerificationCommand -Command $buildCommand -Invoker $CommandInvoker))

    $packageEvidence = if ($null -ne $PackageInspector) {
        & $PackageInspector $packagePath
    }
    else {
        Get-DualRuntimePackageEvidence -PackagePath $packagePath
    }
    if ($isReleaseMode) {
        $releaseAlignment = Assert-ReleaseArtifactAlignment -GateEvidence $parityGate `
            -PackageEvidence $packageEvidence `
            -ProfileTemplatePath (Join-Path $scriptRoot 'templates\Ff7.Native.Steam2026.AppConfig.json')
    }
}
finally {
    if (Test-Path -LiteralPath $verificationRoot -PathType Container) {
        Remove-Item -LiteralPath $verificationRoot -Recurse -Force
    }
}

[pscustomobject]@{
    Mode = $effectiveMode
    VerificationSucceeded = $true
    ReleaseReady = [bool]$parityGate.IsReleaseReady
    Steps = $stepResults.ToArray()
    Package = $packageEvidence
    ReleaseAlignment = $releaseAlignment
    PackageStagingCleaned = -not (Test-Path -LiteralPath $verificationRoot)
}
