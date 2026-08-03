[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $StatePath,
    [Parameter(Mandatory=$true)] [string] $ResultPath,
    [Parameter(DontShow=$true)] [string] $ModulePath,
    [Parameter(DontShow=$true)] [string] $LauncherModulePath
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($ModulePath)) {
    $ModulePath = Join-Path $scriptRoot 'FF7SteamInstall.psm1'
}
Import-Module $ModulePath -Force
if ([string]::IsNullOrWhiteSpace($LauncherModulePath)) {
    $LauncherModulePath = Join-Path $scriptRoot 'FF7LauncherInstall.psm1'
}
Import-Module $LauncherModulePath -Force

if (-not (Test-Path -LiteralPath $StatePath -PathType Leaf)) {
    throw "Blind Swordsman install state is missing: $StatePath"
}
try {
    $state = [IO.File]::ReadAllText([IO.Path]::GetFullPath($StatePath)) | ConvertFrom-Json
}
catch {
    throw "Blind Swordsman install state is invalid JSON: $($_.Exception.Message)"
}
$schemaVersion = [int]$state.schemaVersion
if ($schemaVersion -notin @(1, 2) -or $null -eq $state.game -or $null -eq $state.mod -or
    [string]::IsNullOrWhiteSpace([string]$state.reloadedRoot)) {
    throw 'Blind Swordsman install state has an unsupported schema or is incomplete.'
}
if ($schemaVersion -eq 2 -and -not ($state.PSObject.Properties.Name -ccontains 'legacyProfile')) {
    throw 'Blind Swordsman schema-two install state is missing legacy profile state.'
}

$installation = Resolve-Ff7Installation -GameRoot ([string]$state.game.gameRoot)
$reloadedRoot = [IO.Path]::GetFullPath([string]$state.reloadedRoot).TrimEnd('\')
$expectedModDirectory = [IO.Path]::GetFullPath((Join-Path $reloadedRoot 'Mods\ff7.accessibility.reloaded'))
$recordedModDirectory = [IO.Path]::GetFullPath([string]$state.mod.directory)
if (-not $recordedModDirectory.Equals($expectedModDirectory, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Blind Swordsman install state points to an unexpected mod directory.'
}

$allowedLoaderTargets = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
if ($null -ne $installation.LegacyRuntime) {
    [void]$allowedLoaderTargets.Add([IO.Path]::GetFullPath((Join-Path $installation.LegacyRuntime.RuntimeRoot 'dsound.dll')))
    [void]$allowedLoaderTargets.Add([IO.Path]::GetFullPath((Join-Path $installation.LegacyRuntime.RuntimeRoot 'Reloaded.Mod.Loader.Bootstrapper.asi')))
}
if ($null -ne $installation.NativeRuntime) {
    [void]$allowedLoaderTargets.Add([IO.Path]::GetFullPath((Join-Path $installation.NativeRuntime.RuntimeRoot 'd3d11.dll')))
    [void]$allowedLoaderTargets.Add([IO.Path]::GetFullPath((Join-Path $installation.NativeRuntime.RuntimeRoot 'Reloaded.Mod.Loader.Bootstrapper.asi')))
}

$loaderPlans = New-Object 'System.Collections.Generic.List[object]'
$seenLoaderTargets = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($loader in @($state.loaders)) {
    $target = [IO.Path]::GetFullPath([string]$loader.target)
    if (-not $allowedLoaderTargets.Contains($target) -or -not $seenLoaderTargets.Add($target) -or
        [string]$loader.sha256 -notmatch '^[0-9A-Fa-f]{64}$') {
        throw "Blind Swordsman install state contains an unsafe loader target: $target"
    }
    $loaderPlans.Add([pscustomobject]@{
        Target = $target
        Hash = [string]$loader.sha256
        Changed = [bool]$loader.changed
    })
}

function New-ProfileUninstallPlan {
    param(
        [psobject] $Profile,
        [Parameter(Mandatory=$true)] [string[]] $AllowedPaths,
        [Parameter(Mandatory=$true)] [string] $Label
    )
    if ($null -eq $Profile) { return $null }
    $profilePath = [IO.Path]::GetFullPath([string]$Profile.path)
    $installedHash = [string]$Profile.installedSha256
    if ($AllowedPaths -notcontains $profilePath -or $installedHash -notmatch '^[0-9A-Fa-f]{64}$') {
        throw "Blind Swordsman install state contains an unsafe $Label profile."
    }
    $backupPath = if ([string]::IsNullOrWhiteSpace([string]$Profile.backupPath)) {
        $null
    }
    else {
        [IO.Path]::GetFullPath([string]$Profile.backupPath)
    }
    $backupHash = if ([string]::IsNullOrWhiteSpace([string]$Profile.backupSha256)) { $null } else { [string]$Profile.backupSha256 }
    if (($null -eq $backupPath) -ne ($null -eq $backupHash) -or
        ($null -ne $backupHash -and $backupHash -notmatch '^[0-9A-Fa-f]{64}$')) {
        throw "Blind Swordsman install state contains invalid $Label profile backup metadata."
    }
    if ($null -ne $backupPath -and
        (-not (Split-Path -Parent $backupPath).Equals((Split-Path -Parent $profilePath), [StringComparison]::OrdinalIgnoreCase) -or
         -not (Split-Path -Leaf $backupPath).StartsWith('AppConfig.json.backup-', [StringComparison]::Ordinal))) {
        throw "Blind Swordsman install state contains an unsafe $Label profile backup."
    }
    return [pscustomobject]@{
        Label = $Label
        Path = $profilePath
        Changed = [bool]$Profile.changed
        InstalledHash = $installedHash
        BackupPath = $backupPath
        BackupHash = $backupHash
    }
}

$profilePlans = New-Object 'System.Collections.Generic.List[object]'
$nativeProfilePlan = New-ProfileUninstallPlan -Profile $state.profile -Label 'native' -AllowedPaths @(
    [IO.Path]::GetFullPath((Join-Path $reloadedRoot 'Apps\Ff7.Native.Steam2026\AppConfig.json')),
    [IO.Path]::GetFullPath((Join-Path $reloadedRoot 'Apps\Ff7.Native.Steam2026.Research\AppConfig.json'))
)
if ($null -ne $nativeProfilePlan) { $profilePlans.Add($nativeProfilePlan) }
if ($schemaVersion -eq 2) {
    $legacyProfilePlan = New-ProfileUninstallPlan -Profile $state.legacyProfile -Label 'legacy' -AllowedPaths @(
        [IO.Path]::GetFullPath((Join-Path $reloadedRoot 'Apps\Ff7.En.Steam\AppConfig.json'))
    )
    if ($null -ne $legacyProfilePlan) { $profilePlans.Add($legacyProfilePlan) }
}

$backupRoot = [IO.Path]::GetFullPath((Join-Path $reloadedRoot 'AccessibilityBackups')).TrimEnd('\')
$modBackupPath = if ([string]::IsNullOrWhiteSpace([string]$state.mod.backupPath)) {
    $null
}
else {
    $candidate = [IO.Path]::GetFullPath([string]$state.mod.backupPath)
    if (-not $candidate.StartsWith($backupRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Blind Swordsman install state contains an unsafe package backup path.'
    }
    $candidate
}

$removed = New-Object 'System.Collections.Generic.List[string]'
$restored = New-Object 'System.Collections.Generic.List[string]'
$preserved = New-Object 'System.Collections.Generic.List[string]'

if ($null -ne $state.launcher) {
    $launcherOutcome = Restore-Ff7AccessibleLauncherFromState `
        -GameRoot ([string]$installation.GameRoot) -ReloadedRoot $reloadedRoot `
        -State $state.launcher
    foreach ($path in @($launcherOutcome.Removed)) { $removed.Add([string]$path) }
    foreach ($path in @($launcherOutcome.Restored)) { $restored.Add([string]$path) }
    foreach ($message in @($launcherOutcome.Preserved)) { $preserved.Add([string]$message) }
}

# Restore the exact FFNx narration copy only when setup recorded removing it and
# no replacement has appeared since installation.
if ($null -ne $state.openingVoice -and [bool]$state.openingVoice.wasPresent) {
    if ($null -eq $installation.LegacyRuntime) {
        throw 'Blind Swordsman install state contains legacy opening-voice state without a legacy runtime.'
    }
    $voiceTarget = [IO.Path]::GetFullPath([string]$state.openingVoice.target)
    $expectedVoiceTarget = [IO.Path]::GetFullPath((Join-Path $installation.LegacyRuntime.RuntimeRoot 'override\movies\opening_va.ogg'))
    $voiceSource = Join-Path $recordedModDirectory 'Assets\movies\opening_audio_description.ogg'
    if (-not $voiceTarget.Equals($expectedVoiceTarget, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Blind Swordsman install state contains an unsafe opening voice target.'
    }
    if (-not (Test-Path -LiteralPath $voiceTarget) -and (Test-Path -LiteralPath $voiceSource -PathType Leaf) -and
        (Get-FileHash -LiteralPath $voiceSource -Algorithm SHA256).Hash.Equals([string]$state.openingVoice.sourceSha256, [StringComparison]::OrdinalIgnoreCase)) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $voiceTarget) -Force | Out-Null
        Copy-Item -LiteralPath $voiceSource -Destination $voiceTarget
        $restored.Add($voiceTarget)
    }
}

foreach ($profilePlan in $profilePlans) {
    if (-not $profilePlan.Changed -or -not (Test-Path -LiteralPath $profilePlan.Path -PathType Leaf)) { continue }
    $currentHash = (Get-FileHash -LiteralPath $profilePlan.Path -Algorithm SHA256).Hash
    if (-not $currentHash.Equals($profilePlan.InstalledHash, [StringComparison]::OrdinalIgnoreCase)) {
        $preserved.Add("$($profilePlan.Label) profile changed after installation: $($profilePlan.Path)")
    }
    elseif ($null -ne $profilePlan.BackupPath) {
        if (-not (Test-Path -LiteralPath $profilePlan.BackupPath -PathType Leaf) -or
            -not (Get-FileHash -LiteralPath $profilePlan.BackupPath -Algorithm SHA256).Hash.Equals($profilePlan.BackupHash, [StringComparison]::OrdinalIgnoreCase)) {
            $preserved.Add("$($profilePlan.Label) profile backup is missing or changed: $($profilePlan.BackupPath)")
        }
        else {
            $temporaryCurrent = Join-Path (Split-Path -Parent $profilePlan.Path) ('.uninstall-profile-' + [Guid]::NewGuid().ToString('N'))
            Move-Item -LiteralPath $profilePlan.Path -Destination $temporaryCurrent
            try {
                Move-Item -LiteralPath $profilePlan.BackupPath -Destination $profilePlan.Path
                Remove-Item -LiteralPath $temporaryCurrent -Force
                $restored.Add($profilePlan.Path)
            }
            catch {
                if (-not (Test-Path -LiteralPath $profilePlan.Path) -and (Test-Path -LiteralPath $temporaryCurrent -PathType Leaf)) {
                    Move-Item -LiteralPath $temporaryCurrent -Destination $profilePlan.Path
                }
                throw
            }
        }
    }
    else {
        Remove-Item -LiteralPath $profilePlan.Path -Force
        $removed.Add($profilePlan.Path)
    }
}

foreach ($loader in $loaderPlans) {
    if (-not $loader.Changed -or -not (Test-Path -LiteralPath $loader.Target -PathType Leaf)) { continue }
    $currentHash = (Get-FileHash -LiteralPath $loader.Target -Algorithm SHA256).Hash
    if ($currentHash.Equals($loader.Hash, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $loader.Target -Force
        $removed.Add($loader.Target)
    }
    else {
        $preserved.Add("Loader changed after installation: $($loader.Target)")
    }
}

if (Test-Path -LiteralPath $recordedModDirectory -PathType Container) {
    $currentPackage = Assert-Ff7DualRuntimePackage -PackagePath $recordedModDirectory
    if (-not ([string]$currentPackage.Fingerprint).Equals([string]$state.mod.fingerprint, [StringComparison]::OrdinalIgnoreCase)) {
        $preserved.Add("Mod package changed after installation: $recordedModDirectory")
    }
    elseif ($null -ne $modBackupPath) {
        if (-not (Test-Path -LiteralPath $modBackupPath -PathType Container)) {
            $preserved.Add("Prior mod backup is missing: $modBackupPath")
        }
        else {
            $backupPackage = Assert-Ff7DualRuntimePackage -PackagePath $modBackupPath
            if (-not ([string]$backupPackage.Fingerprint).Equals([string]$state.mod.backupFingerprint, [StringComparison]::OrdinalIgnoreCase)) {
                $preserved.Add("Prior mod backup changed after installation: $modBackupPath")
            }
            else {
                $temporaryCurrent = Join-Path (Split-Path -Parent $recordedModDirectory) ('.uninstall-' + [Guid]::NewGuid().ToString('N'))
                Move-Item -LiteralPath $recordedModDirectory -Destination $temporaryCurrent
                try {
                    Move-Item -LiteralPath $modBackupPath -Destination $recordedModDirectory
                    Remove-Item -LiteralPath $temporaryCurrent -Recurse -Force
                    $restored.Add($recordedModDirectory)
                }
                catch {
                    if (-not (Test-Path -LiteralPath $recordedModDirectory) -and (Test-Path -LiteralPath $temporaryCurrent)) {
                        Move-Item -LiteralPath $temporaryCurrent -Destination $recordedModDirectory
                    }
                    throw
                }
            }
        }
    }
    else {
        Remove-Item -LiteralPath $recordedModDirectory -Recurse -Force
        $removed.Add($recordedModDirectory)
    }
}

$result = [ordered]@{
    schemaVersion = 1
    completed = $true
    removed = $removed.ToArray()
    restored = $restored.ToArray()
    preserved = $preserved.ToArray()
}
$resolvedResultPath = [IO.Path]::GetFullPath($ResultPath)
$resultDirectory = Split-Path -Parent $resolvedResultPath
if (-not (Test-Path -LiteralPath $resultDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
}
$temporaryResult = Join-Path $resultDirectory ('.uninstall-result-' + [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    [IO.File]::WriteAllText($temporaryResult, ($result | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
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
