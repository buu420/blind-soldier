[CmdletBinding()]
param(
    [string] $GameRoot,
    [string] $SteamRoot,
    [string] $ReloadedRoot,
    [string] $SeventhHeavenRoot,
    [string] $FfnxArchivePath,
    [string] $ParityMatrixPath,
    [string] $PackagePath,
    [string] $ResultPath,
    [string] $ProductVersion,
    [string] $ReleaseTag,
    [switch] $SkipFfnx,
    [switch] $SkipSeventhHeavenSettings,
    [switch] $AllowResearchNativeProfile
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Import-Module (Join-Path $scriptRoot 'FF7SteamInstall.psm1') -Force
if ([string]::IsNullOrWhiteSpace($ParityMatrixPath)) {
    $ParityMatrixPath = Join-Path $scriptRoot 'analysis\dual_runtime\parity-matrix.json'
}

if ([string]::IsNullOrWhiteSpace($ReloadedRoot)) {
    $ReloadedRoot = if ($env:RELOADED_II_ROOT) {
        $env:RELOADED_II_ROOT
    }
    else {
        Join-Path $env:USERPROFILE 'AccessXI\external\Reloaded-II'
    }
}
if ([string]::IsNullOrWhiteSpace($SeventhHeavenRoot)) {
    $SeventhHeavenRoot = if ($env:SEVENTH_HEAVEN_ROOT) {
        $env:SEVENTH_HEAVEN_ROOT
    }
    else {
        Join-Path $env:USERPROFILE 'Tools\7thHeaven'
    }
}

if (-not (Test-Path -LiteralPath $ReloadedRoot -PathType Container)) {
    throw "Reloaded-II root is missing: $ReloadedRoot"
}
$buildPackagePath = Join-Path $scriptRoot 'Build-DualRuntimePackage.ps1'
$nativeProfileTemplate = Join-Path $scriptRoot 'templates\Ff7.Native.Steam2026.AppConfig.json'
$asiLoaderX86Source = Join-Path $ReloadedRoot '_asi_extract\ASILoader32.dll'
$bootstrapperX86Source = Join-Path $ReloadedRoot 'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll'
$asiLoaderX64Source = Join-Path $ReloadedRoot '_asi_extract\ASILoader64.dll'
$bootstrapperX64Source = Join-Path $ReloadedRoot 'Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll'
$requiredFiles = @(
    $nativeProfileTemplate,
    $asiLoaderX86Source,
    $bootstrapperX86Source,
    $asiLoaderX64Source,
    $bootstrapperX64Source
)
if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $requiredFiles += $buildPackagePath
}
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required installer file is missing: $requiredFile"
    }
}

function Assert-LoaderPeMachine {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [uint16] $ExpectedMachine
    )

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "Loader source is not a PE image: $Path"
    }
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($peOffset -lt 64 -or $peOffset + 6 -gt $bytes.Length -or
        [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x00004550) {
        throw "Loader source has an invalid PE header: $Path"
    }
    $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
    if ($machine -ne $ExpectedMachine) {
        throw ("Loader source {0} has PE machine 0x{1:X4}; expected 0x{2:X4}." -f `
            $Path, $machine, $ExpectedMachine)
    }
}

Assert-LoaderPeMachine -Path $asiLoaderX86Source -ExpectedMachine 0x014C
Assert-LoaderPeMachine -Path $bootstrapperX86Source -ExpectedMachine 0x014C
Assert-LoaderPeMachine -Path $asiLoaderX64Source -ExpectedMachine 0x8664
Assert-LoaderPeMachine -Path $bootstrapperX64Source -ExpectedMachine 0x8664

$resolveArguments = @{}
if (-not [string]::IsNullOrWhiteSpace($GameRoot)) {
    $resolveArguments.GameRoot = $GameRoot
}
if (-not [string]::IsNullOrWhiteSpace($SteamRoot)) {
    $resolveArguments.SteamRoot = $SteamRoot
}
$installation = Resolve-Ff7Installation @resolveArguments
$nativeRuntime = if ($installation.Version -eq 'Steam2026') {
    Assert-Ff7NativeRuntimeIdentity -Path $installation.NativeRuntime.GameExe
}
else {
    $null
}
$nativeParityGate = if ($null -ne $nativeRuntime) {
    # Research inspection has no side effects. Profile creation below still requires either
    # a genuinely open release gate or the caller's explicit research-profile switch.
    Assert-Ff7NativeParityReleaseGate -ParityMatrixPath $ParityMatrixPath -AllowResearch
}
else {
    $null
}

function Assert-LoaderFileTarget {
    param(
        [Parameter(Mandatory=$true)] [string] $Source,
        [Parameter(Mandatory=$true)] [string] $Target
    )

    if (Test-Path -LiteralPath $Target) {
        $targetItem = Get-Item -LiteralPath $Target -Force
        if ($targetItem.PSIsContainer) {
            throw "Refusing to replace a loader target that is a directory: $Target"
        }
        if (($targetItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to replace a reparse-point loader target: $Target"
        }
        $sourceHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
        $targetHash = (Get-FileHash -LiteralPath $Target -Algorithm SHA256).Hash
        if (-not $sourceHash.Equals($targetHash, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to overwrite an existing loader with different contents: $Target"
        }
    }
}

function Copy-LoaderFile {
    param(
        [Parameter(Mandatory=$true)] [string] $Source,
        [Parameter(Mandatory=$true)] [string] $Target
    )

    Assert-LoaderFileTarget -Source $Source -Target $Target
    if (Test-Path -LiteralPath $Target -PathType Leaf) {
        return [pscustomobject]@{ Changed = $false; Target = $Target }
    }

    try {
        Copy-Item -LiteralPath $Source -Destination $Target
    }
    catch {
        if (Test-Path -LiteralPath $Target -PathType Leaf) {
            Remove-Item -LiteralPath $Target -Force
        }
        throw
    }
    $sourceHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
    $targetHash = (Get-FileHash -LiteralPath $Target -Algorithm SHA256).Hash
    if (-not $sourceHash.Equals($targetHash, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $Target -Force
        throw "New loader copy failed verification: $Target"
    }
    return [pscustomobject]@{ Changed = $true; Target = $Target }
}

function Assert-ManagedOpeningVoiceTarget {
    param(
        [Parameter(Mandatory=$true)] [string] $Source,
        [Parameter(Mandatory=$true)] [string] $Target
    )

    if (-not (Test-Path -LiteralPath $Target)) {
        return
    }
    $targetItem = Get-Item -LiteralPath $Target -Force
    if ($targetItem.PSIsContainer -or
        ($targetItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to remove a non-file or reparse-point opening voice target: $Target"
    }
    $sourceHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
    $targetHash = (Get-FileHash -LiteralPath $Target -Algorithm SHA256).Hash
    if (-not $sourceHash.Equals($targetHash, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a different FFNx opening movie voice track: $Target"
    }
}

function Remove-NewLoaderForRollback {
    param(
        [psobject] $Result,
        [Parameter(Mandatory=$true)] [string] $Source
    )

    if ($null -eq $Result -or -not $Result.Changed) {
        return
    }
    $target = [IO.Path]::GetFullPath([string]$Result.Target)
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
        return
    }
    $targetItem = Get-Item -LiteralPath $target -Force
    if (($targetItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to roll back a reparse-point loader target: $target"
    }
    $sourceHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
    $targetHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if (-not $sourceHash.Equals($targetHash, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to roll back a loader whose contents changed after installation: $target"
    }
    Remove-Item -LiteralPath $target -Force
}

function Restore-NativeProfileForRollback {
    param(
        [psobject] $Result,
        [Parameter(Mandatory=$true)] [string] $ExpectedProfilePath
    )

    if ($null -eq $Result -or -not $Result.Changed) {
        return
    }
    $profilePath = [IO.Path]::GetFullPath([string]$Result.ProfilePath)
    $expectedPath = [IO.Path]::GetFullPath($ExpectedProfilePath)
    if (-not $profilePath.Equals($expectedPath, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Split-Path -Leaf $profilePath).Equals('AppConfig.json', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to roll back an unexpected native profile path: $profilePath"
    }
    if (Test-Path -LiteralPath $profilePath) {
        $profileItem = Get-Item -LiteralPath $profilePath -Force
        if ($profileItem.PSIsContainer -or
            ($profileItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to roll back a non-file or reparse-point native profile: $profilePath"
        }
        Remove-Item -LiteralPath $profilePath -Force
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$Result.BackupPath)) {
        $backupPath = [IO.Path]::GetFullPath([string]$Result.BackupPath)
        if (-not (Split-Path -Parent $backupPath).Equals(
                (Split-Path -Parent $profilePath), [StringComparison]::OrdinalIgnoreCase) -or
            -not (Split-Path -Leaf $backupPath).StartsWith(
                'AppConfig.json.backup-', [StringComparison]::Ordinal)) {
            throw "Refusing to restore an unexpected native profile backup: $backupPath"
        }
        if (-not (Test-Path -LiteralPath $backupPath -PathType Leaf)) {
            throw "Native profile rollback backup is missing: $backupPath"
        }
        $backupItem = Get-Item -LiteralPath $backupPath -Force
        if (($backupItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to restore a reparse-point native profile backup: $backupPath"
        }
        Move-Item -LiteralPath $backupPath -Destination $profilePath
    }
    else {
        $profileDirectory = Split-Path -Parent $profilePath
        if ((Test-Path -LiteralPath $profileDirectory -PathType Container) -and
            @(Get-ChildItem -LiteralPath $profileDirectory -Force).Count -eq 0) {
            Remove-Item -LiteralPath $profileDirectory -Force
        }
    }
}

function Assert-OwnedModDirectoryForRollback {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [string] $ExpectedParent,
        [Parameter(Mandatory=$true)] [string] $ExpectedLeafPrefix
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $parent = [IO.Path]::GetFullPath((Split-Path -Parent $fullPath)).TrimEnd('\')
    if (-not $parent.Equals([IO.Path]::GetFullPath($ExpectedParent).TrimEnd('\'),
            [StringComparison]::OrdinalIgnoreCase) -or
        -not (Split-Path -Leaf $fullPath).StartsWith($ExpectedLeafPrefix, [StringComparison]::Ordinal)) {
        throw "Refusing to roll back an unexpected mod directory: $fullPath"
    }
    if (-not (Test-Path -LiteralPath $fullPath -PathType Container)) {
        throw "Mod directory required for rollback is missing: $fullPath"
    }
    $item = Get-Item -LiteralPath $fullPath -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to roll back a reparse-point mod directory: $fullPath"
    }
    $configPath = Join-Path $fullPath 'ModConfig.json'
    try {
        $config = [IO.File]::ReadAllText($configPath) | ConvertFrom-Json
    }
    catch {
        throw "Refusing to roll back a mod directory with invalid ownership metadata: $fullPath"
    }
    if ([string]$config.ModId -cne 'ff7.accessibility.reloaded') {
        throw "Refusing to roll back a mod directory owned by another ModId: $fullPath"
    }
    return $fullPath
}

function Restore-DualRuntimePackageForRollback {
    param(
        [psobject] $Result,
        [Parameter(Mandatory=$true)] [string] $ExpectedModDirectory
    )

    if ($null -eq $Result -or -not $Result.Changed) {
        return
    }
    $target = [IO.Path]::GetFullPath([string]$Result.ModDirectory)
    $expectedTarget = [IO.Path]::GetFullPath($ExpectedModDirectory)
    if (-not $target.Equals($expectedTarget, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Split-Path -Leaf $target).Equals('ff7.accessibility.reloaded', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to roll back an unexpected installed mod path: $target"
    }
    $parent = Split-Path -Parent $target
    $backupParent = Join-Path (Split-Path -Parent $parent) 'AccessibilityBackups'
    if (Test-Path -LiteralPath $target) {
        $validatedTarget = Assert-OwnedModDirectoryForRollback -Path $target `
            -ExpectedParent $parent -ExpectedLeafPrefix 'ff7.accessibility.reloaded'
        Remove-Item -LiteralPath $validatedTarget -Recurse -Force
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$Result.BackupPath)) {
        $backup = Assert-OwnedModDirectoryForRollback -Path ([string]$Result.BackupPath) `
            -ExpectedParent $backupParent -ExpectedLeafPrefix 'ff7.accessibility.reloaded.backup-'
        Move-Item -LiteralPath $backup -Destination $target
    }
}

$ownsStagedPackage = [string]::IsNullOrWhiteSpace($PackagePath)
$stagingParent = if ($ownsStagedPackage) {
    Join-Path ([IO.Path]::GetTempPath()) ('ff7-accessibility-install-' + [Guid]::NewGuid().ToString('N'))
}
else { $null }
$stagedPackage = if ($ownsStagedPackage) {
    Join-Path $stagingParent 'ff7.accessibility.reloaded'
}
else {
    if (-not (Test-Path -LiteralPath $PackagePath -PathType Container)) {
        throw "Prebuilt dual-runtime package is missing: $PackagePath"
    }
    $packageItem = Get-Item -LiteralPath $PackagePath -Force
    if (($packageItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Prebuilt dual-runtime package cannot be a reparse point: $PackagePath"
    }
    [IO.Path]::GetFullPath($packageItem.FullName)
}
$modDirectory = Join-Path $ReloadedRoot 'Mods\ff7.accessibility.reloaded'
$asiLoaderX86Target = Join-Path $installation.LegacyRuntime.RuntimeRoot 'dsound.dll'
$bootstrapperX86Target = Join-Path $installation.LegacyRuntime.RuntimeRoot 'Reloaded.Mod.Loader.Bootstrapper.asi'
$openingVoiceTarget = Join-Path $installation.LegacyRuntime.RuntimeRoot 'override\movies\opening_va.ogg'
$ordinaryNativeProfile = Join-Path $ReloadedRoot 'Apps\Ff7.Native.Steam2026\AppConfig.json'
$shouldInstallNativeProfile = $null -ne $nativeRuntime -and
    ($nativeParityGate.IsReleaseReady -or $AllowResearchNativeProfile)
$expectedNativeProfile = if ($shouldInstallNativeProfile) {
    $profileDirectoryName = if ($nativeParityGate.IsReleaseReady) {
        'Ff7.Native.Steam2026'
    }
    else {
        'Ff7.Native.Steam2026.Research'
    }
    Join-Path $ReloadedRoot (Join-Path 'Apps' (Join-Path $profileDirectoryName 'AppConfig.json'))
}
else {
    $null
}
$asiLoaderX64Target = if ($shouldInstallNativeProfile) {
    Join-Path $nativeRuntime.RuntimeRoot 'd3d11.dll'
}
else {
    $null
}
$bootstrapperX64Target = if ($shouldInstallNativeProfile) {
    Join-Path $nativeRuntime.RuntimeRoot 'Reloaded.Mod.Loader.Bootstrapper.asi'
}
else {
    $null
}
$packageResult = $null
$profileResult = $null
$asiLoaderX86Result = $null
$bootstrapperX86Result = $null
$asiLoaderX64Result = $null
$bootstrapperX64Result = $null
$openingNarrationResult = $null
$openingVoiceWasPresent = $false
$ffnxResult = $null
$resolvedResultPath = $null
if (-not [string]::IsNullOrWhiteSpace($ResultPath)) {
    $resolvedResultPath = [IO.Path]::GetFullPath($ResultPath)
    if (Test-Path -LiteralPath $resolvedResultPath) {
        $resultItem = Get-Item -LiteralPath $resolvedResultPath -Force
        if ($resultItem.PSIsContainer -or
            ($resultItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Installer result target is not a regular file: $resolvedResultPath"
        }
    }
    $resultDirectory = Split-Path -Parent $resolvedResultPath
    $existingResultParent = $resultDirectory
    while (-not (Test-Path -LiteralPath $existingResultParent)) {
        $nextParent = Split-Path -Parent $existingResultParent
        if ([string]::IsNullOrWhiteSpace($nextParent) -or $nextParent -eq $existingResultParent) {
            throw "Installer result path has no existing parent: $resolvedResultPath"
        }
        $existingResultParent = $nextParent
    }
    $parentItem = Get-Item -LiteralPath $existingResultParent -Force
    if (-not $parentItem.PSIsContainer -or
        ($parentItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Installer result parent is unsafe: $existingResultParent"
    }
}
try {
    # Build-DualRuntimePackage validates both R2R payloads, dependencies, native PE machines,
    # configuration, and assets before any installed profile or mod directory is changed.
    if ($ownsStagedPackage) {
        & $buildPackagePath -OutputPath $stagedPackage | Out-Null
    }

    if (-not [string]::IsNullOrWhiteSpace($ResultPath)) {
        if ([string]::IsNullOrWhiteSpace($ProductVersion)) {
            $packageConfig = [IO.File]::ReadAllText((Join-Path $stagedPackage 'ModConfig.json')) | ConvertFrom-Json
            $ProductVersion = [string]$packageConfig.ModVersion
        }
        if ($ProductVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$') {
            throw "Installer product version is invalid: $ProductVersion"
        }
        if ([string]::IsNullOrWhiteSpace($ReleaseTag)) {
            $ReleaseTag = 'v' + $ProductVersion
        }
        if ($ReleaseTag -cne ('v' + $ProductVersion)) {
            throw "Installer release tag does not match product version: $ReleaseTag"
        }
    }

    # Complete every ownership, identity, parity, template, and collision check before the
    # first mutation. A stale ordinary native profile is unsafe while the release gate is closed.
    Install-Ff7DualRuntimePackage -PackagePath $stagedPackage `
        -ModDirectory $modDirectory -ValidateOnly | Out-Null
    if ($null -ne $nativeRuntime -and -not $nativeParityGate.IsReleaseReady -and
        (Test-Path -LiteralPath $ordinaryNativeProfile -PathType Leaf)) {
        throw "An ordinary native profile already exists while the release gate is closed: $ordinaryNativeProfile"
    }
    if ($shouldInstallNativeProfile) {
        Install-Ff7NativeReloadedProfile -ReloadedRoot $ReloadedRoot `
            -NativeRuntime $nativeRuntime -TemplatePath $nativeProfileTemplate `
            -ParityMatrixPath $ParityMatrixPath -AllowResearch:$AllowResearchNativeProfile `
            -ValidateOnly | Out-Null
    }
    Assert-LoaderFileTarget -Source $asiLoaderX86Source -Target $asiLoaderX86Target
    Assert-LoaderFileTarget -Source $bootstrapperX86Source -Target $bootstrapperX86Target
    if ($shouldInstallNativeProfile) {
        Assert-LoaderFileTarget -Source $asiLoaderX64Source -Target $asiLoaderX64Target
        Assert-LoaderFileTarget -Source $bootstrapperX64Source -Target $bootstrapperX64Target
    }
    $openingNarrationSource = Join-Path $stagedPackage 'Assets\movies\opening_audio_description.ogg'
    Assert-ManagedOpeningVoiceTarget -Source $openingNarrationSource -Target $openingVoiceTarget
    $openingVoiceWasPresent = Test-Path -LiteralPath $openingVoiceTarget -PathType Leaf

    $installation = Initialize-Ff7CompatibilityRuntime -Installation $installation
    if (-not $SkipFfnx -and $installation.Version -eq 'Steam2026') {
        $ffnxArguments = @{ RuntimeRoot = $installation.LegacyRuntime.RuntimeRoot }
        if (-not [string]::IsNullOrWhiteSpace($FfnxArchivePath)) {
            $ffnxArguments.ArchivePath = $FfnxArchivePath
        }
        $ffnxResult = Install-FfnxSteamRuntime @ffnxArguments
        Write-Host "Installed verified FFNx $($ffnxResult.ReleaseTag) from $($ffnxResult.AssetName)."
    }

    try {
        $packageResult = Install-Ff7DualRuntimePackage -PackagePath $stagedPackage -ModDirectory $modDirectory
        Install-Ff7DualRuntimePackage -PackagePath $modDirectory `
            -ModDirectory $modDirectory -ValidateOnly | Out-Null

        if ($shouldInstallNativeProfile) {
            $profileResult = Install-Ff7NativeReloadedProfile -ReloadedRoot $ReloadedRoot `
                -NativeRuntime $nativeRuntime -TemplatePath $nativeProfileTemplate `
                -ParityMatrixPath $ParityMatrixPath -AllowResearch:$AllowResearchNativeProfile
            Assert-Ff7NativeReloadedProfile -ReloadedRoot $ReloadedRoot `
                -NativeRuntime $nativeRuntime -Research:$profileResult.IsResearchProfile | Out-Null
        }

        $asiLoaderX86Result = Copy-LoaderFile -Source $asiLoaderX86Source -Target $asiLoaderX86Target
        $bootstrapperX86Result = Copy-LoaderFile -Source $bootstrapperX86Source -Target $bootstrapperX86Target
        if ($shouldInstallNativeProfile) {
            $asiLoaderX64Result = Copy-LoaderFile -Source $asiLoaderX64Source -Target $asiLoaderX64Target
            $bootstrapperX64Result = Copy-LoaderFile -Source $bootstrapperX64Source -Target $bootstrapperX64Target
        }

        # This is the final mutation: after all installed artifacts have been revalidated,
        # remove only the exact managed FFNx copy so Reloaded owns narration playback.
        $openingNarrationResult = Disable-Ff7OpeningMovieNativeVoiceLayer `
            -RuntimeRoot $installation.LegacyRuntime.RuntimeRoot `
            -SourcePath $openingNarrationSource
    }
    catch {
        $installationError = $_
        $rollbackErrors = New-Object System.Collections.Generic.List[string]
        foreach ($rollback in @(
            { if ($openingVoiceWasPresent -and -not (Test-Path -LiteralPath $openingVoiceTarget -PathType Leaf)) {
                    Copy-Item -LiteralPath $openingNarrationSource -Destination $openingVoiceTarget
                    Assert-ManagedOpeningVoiceTarget -Source $openingNarrationSource -Target $openingVoiceTarget
                } },
            { Remove-NewLoaderForRollback -Result $bootstrapperX64Result -Source $bootstrapperX64Source },
            { Remove-NewLoaderForRollback -Result $asiLoaderX64Result -Source $asiLoaderX64Source },
            { Remove-NewLoaderForRollback -Result $bootstrapperX86Result -Source $bootstrapperX86Source },
            { Remove-NewLoaderForRollback -Result $asiLoaderX86Result -Source $asiLoaderX86Source },
            { if ($null -ne $expectedNativeProfile) {
                    Restore-NativeProfileForRollback -Result $profileResult `
                        -ExpectedProfilePath $expectedNativeProfile
                } },
            { Restore-DualRuntimePackageForRollback -Result $packageResult `
                    -ExpectedModDirectory $modDirectory }
        )) {
            try {
                & $rollback
            }
            catch {
                $rollbackErrors.Add($_.Exception.Message)
            }
        }
        if ($rollbackErrors.Count -gt 0) {
            throw "Installation failed and rollback also reported errors. Original: $($installationError.Exception.Message) Rollback: $($rollbackErrors -join ' | ')"
        }
        throw $installationError
    }

    if ($packageResult.Changed) {
        Write-Host "Installed the verified dual-runtime package. Backup: $($packageResult.BackupPath)"
    }
    else {
        Write-Host 'The verified dual-runtime package is already installed.'
    }
    if ($null -ne $profileResult) {
        if ($profileResult.Changed) {
            Write-Host "Installed additive native Steam 2026 Reloaded profile. Backup: $($profileResult.BackupPath)"
        }
        else {
            Write-Host 'The native Steam 2026 Reloaded profile is already current.'
        }
    }
    elseif ($null -ne $nativeRuntime) {
        Write-Host 'Native Steam 2026 profile was withheld because full parity and user-led validation are not complete.'
    }
    if ($shouldInstallNativeProfile) {
        Write-Host 'Installed verified x64 bootstrap files; ordinary FFVII.exe and Steam launches now load accessibility automatically.'
    }
    if ($openingNarrationResult.Removed) {
        Write-Host "Removed the legacy FFNx opening voice copy; Reloaded plays narration independently: $($openingNarrationResult.TargetPath)"
    }

    # The protected legacy Reloaded profile and 7th Heaven settings are intentionally untouched.
    Write-Host 'Protected legacy Reloaded and 7th Heaven profiles were preserved unchanged.'
    Write-Host 'Installation completed without launching FFVII, Reloaded-II, or 7th Heaven.'

    if (-not [string]::IsNullOrWhiteSpace($ResultPath)) {
        $loaderResults = New-Object 'System.Collections.Generic.List[object]'
        foreach ($loader in @(
            [pscustomobject]@{ Id = 'legacy-asi-loader'; Result = $asiLoaderX86Result; Source = $asiLoaderX86Source },
            [pscustomobject]@{ Id = 'legacy-bootstrapper'; Result = $bootstrapperX86Result; Source = $bootstrapperX86Source },
            [pscustomobject]@{ Id = 'native-asi-loader'; Result = $asiLoaderX64Result; Source = $asiLoaderX64Source },
            [pscustomobject]@{ Id = 'native-bootstrapper'; Result = $bootstrapperX64Result; Source = $bootstrapperX64Source }
        )) {
            if ($null -eq $loader.Result) { continue }
            $loaderResults.Add([ordered]@{
                id = $loader.Id
                target = [IO.Path]::GetFullPath([string]$loader.Result.Target)
                sha256 = (Get-FileHash -LiteralPath $loader.Source -Algorithm SHA256).Hash
                changed = [bool]$loader.Result.Changed
            })
        }

        $packageBackupFingerprint = [string]$packageResult.BackupFingerprint
        $profileState = if ($null -ne $profileResult) {
            $profileBackupPath = if ([string]::IsNullOrWhiteSpace([string]$profileResult.BackupPath)) { $null } else { [IO.Path]::GetFullPath([string]$profileResult.BackupPath) }
            $profileBackupSha256 = if ($null -eq $profileBackupPath) { $null } else { (Get-FileHash -LiteralPath $profileBackupPath -Algorithm SHA256).Hash }
            [ordered]@{
                path = [IO.Path]::GetFullPath([string]$profileResult.ProfilePath)
                changed = [bool]$profileResult.Changed
                installedSha256 = (Get-FileHash -LiteralPath ([string]$profileResult.ProfilePath) -Algorithm SHA256).Hash
                backupPath = $profileBackupPath
                backupSha256 = $profileBackupSha256
                research = [bool]$profileResult.IsResearchProfile
            }
        }
        else { $null }
        $packageBackupPath = if ([string]::IsNullOrWhiteSpace([string]$packageResult.BackupPath)) { $null } else { [IO.Path]::GetFullPath([string]$packageResult.BackupPath) }
        $ffnxState = if ($null -eq $ffnxResult) { $null } else { [ordered]@{ releaseTag = [string]$ffnxResult.ReleaseTag; assetName = [string]$ffnxResult.AssetName } }
        $result = [ordered]@{
            schemaVersion = 1
            productVersion = $ProductVersion
            releaseTag = $ReleaseTag
            installedAtUtc = [DateTime]::UtcNow.ToString('O')
            game = [ordered]@{
                version = [string]$installation.Version
                gameRoot = [IO.Path]::GetFullPath([string]$installation.GameRoot)
            }
            reloadedRoot = [IO.Path]::GetFullPath($ReloadedRoot)
            mod = [ordered]@{
                directory = [IO.Path]::GetFullPath([string]$packageResult.ModDirectory)
                fingerprint = [string]$packageResult.Fingerprint
                backupPath = $packageBackupPath
                backupFingerprint = $packageBackupFingerprint
            }
            profile = $profileState
            loaders = $loaderResults.ToArray()
            openingVoice = [ordered]@{
                wasPresent = [bool]$openingVoiceWasPresent
                target = [IO.Path]::GetFullPath($openingVoiceTarget)
                sourceSha256 = (Get-FileHash -LiteralPath $openingNarrationSource -Algorithm SHA256).Hash
            }
            ffnx = $ffnxState
        }

        $resultDirectory = Split-Path -Parent $resolvedResultPath
        if (-not (Test-Path -LiteralPath $resultDirectory -PathType Container)) {
            New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
        }
        $temporaryResult = Join-Path $resultDirectory ('.install-result-' + [Guid]::NewGuid().ToString('N') + '.tmp')
        try {
            [IO.File]::WriteAllText($temporaryResult, ($result | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
            if (Test-Path -LiteralPath $resolvedResultPath -PathType Leaf) {
                [IO.File]::Replace($temporaryResult, $resolvedResultPath, $null, $true)
            }
            else {
                Move-Item -LiteralPath $temporaryResult -Destination $resolvedResultPath
            }
        }
        finally {
            if (Test-Path -LiteralPath $temporaryResult -PathType Leaf) {
                Remove-Item -LiteralPath $temporaryResult -Force
            }
        }
    }
}
finally {
    if ($ownsStagedPackage -and (Test-Path -LiteralPath $stagingParent -PathType Container)) {
        Remove-Item -LiteralPath $stagingParent -Recurse -Force
    }
}
