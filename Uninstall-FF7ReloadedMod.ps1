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
if ($state.schemaVersion -ne 1 -or $null -eq $state.game -or $null -eq $state.mod -or
    [string]::IsNullOrWhiteSpace([string]$state.reloadedRoot)) {
    throw 'Blind Swordsman install state has an unsupported schema or is incomplete.'
}

$installation = Resolve-Ff7Installation -GameRoot ([string]$state.game.gameRoot)
$reloadedRoot = [IO.Path]::GetFullPath([string]$state.reloadedRoot).TrimEnd('\')
$expectedModDirectory = [IO.Path]::GetFullPath((Join-Path $reloadedRoot 'Mods\ff7.accessibility.reloaded'))
$recordedModDirectory = [IO.Path]::GetFullPath([string]$state.mod.directory)
if (-not $recordedModDirectory.Equals($expectedModDirectory, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Blind Swordsman install state points to an unexpected mod directory.'
}

$allowedLoaderTargets = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
[void]$allowedLoaderTargets.Add([IO.Path]::GetFullPath((Join-Path $installation.LegacyRuntime.RuntimeRoot 'dsound.dll')))
[void]$allowedLoaderTargets.Add([IO.Path]::GetFullPath((Join-Path $installation.LegacyRuntime.RuntimeRoot 'Reloaded.Mod.Loader.Bootstrapper.asi')))
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

$profilePlan = $null
if ($null -ne $state.profile) {
    $profilePath = [IO.Path]::GetFullPath([string]$state.profile.path)
    $allowedProfiles = @(
        [IO.Path]::GetFullPath((Join-Path $reloadedRoot 'Apps\Ff7.Native.Steam2026\AppConfig.json')),
        [IO.Path]::GetFullPath((Join-Path $reloadedRoot 'Apps\Ff7.Native.Steam2026.Research\AppConfig.json'))
    )
    if ($allowedProfiles -notcontains $profilePath -or [string]$state.profile.installedSha256 -notmatch '^[0-9A-Fa-f]{64}$') {
        throw 'Blind Swordsman install state contains an unsafe native profile.'
    }
    $profilePlan = [pscustomobject]@{
        Path = $profilePath
        Changed = [bool]$state.profile.changed
        InstalledHash = [string]$state.profile.installedSha256
        BackupPath = if ([string]::IsNullOrWhiteSpace([string]$state.profile.backupPath)) { $null } else { [IO.Path]::GetFullPath([string]$state.profile.backupPath) }
        BackupHash = [string]$state.profile.backupSha256
    }
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

if ($null -ne $profilePlan -and $profilePlan.Changed -and (Test-Path -LiteralPath $profilePlan.Path -PathType Leaf)) {
    $currentHash = (Get-FileHash -LiteralPath $profilePlan.Path -Algorithm SHA256).Hash
    if (-not $currentHash.Equals($profilePlan.InstalledHash, [StringComparison]::OrdinalIgnoreCase)) {
        $preserved.Add("Native profile changed after installation: $($profilePlan.Path)")
    }
    elseif ($null -ne $profilePlan.BackupPath) {
        if (-not (Test-Path -LiteralPath $profilePlan.BackupPath -PathType Leaf) -or
            -not (Get-FileHash -LiteralPath $profilePlan.BackupPath -Algorithm SHA256).Hash.Equals($profilePlan.BackupHash, [StringComparison]::OrdinalIgnoreCase)) {
            $preserved.Add("Native profile backup is missing or changed: $($profilePlan.BackupPath)")
        }
        else {
            [IO.File]::Replace($profilePlan.BackupPath, $profilePlan.Path, $null, $true)
            $restored.Add($profilePlan.Path)
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
