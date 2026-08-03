[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('Legacy', 'Native', 'SeventhHeaven')]
    [string] $Runtime,
    [string] $GameRoot,
    [string] $SteamRoot,
    [string] $ReloadedRoot,
    [string] $SeventhHeavenRoot,
    [string] $ParityMatrixPath,
    [switch] $AllowResearchNative
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Import-Module (Join-Path $scriptRoot 'FF7SteamInstall.psm1') -Force
if ([string]::IsNullOrWhiteSpace($ParityMatrixPath)) {
    $ParityMatrixPath = Join-Path $scriptRoot 'analysis\dual_runtime\parity-matrix.json'
}

if ($Runtime -ne 'SeventhHeaven' -and [string]::IsNullOrWhiteSpace($ReloadedRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($env:RELOADED_II_ROOT)) {
        $ReloadedRoot = $env:RELOADED_II_ROOT
    }
}
if ($Runtime -ne 'SeventhHeaven' -and [string]::IsNullOrWhiteSpace($ReloadedRoot)) {
    $reloadedSettingsPath = Join-Path ([Environment]::GetFolderPath('ApplicationData')) `
        'Reloaded-Mod-Loader-II\ReloadedII.json'
    if (Test-Path -LiteralPath $reloadedSettingsPath -PathType Leaf) {
        try {
            $reloadedSettings = [IO.File]::ReadAllText($reloadedSettingsPath) | ConvertFrom-Json
            $registeredLauncher = [string]$reloadedSettings.LauncherPath
            if (-not [string]::IsNullOrWhiteSpace($registeredLauncher) -and
                (Test-Path -LiteralPath $registeredLauncher -PathType Leaf)) {
                $ReloadedRoot = Split-Path -Parent $registeredLauncher
            }
        }
        catch {
            # Fall through to Steam and portable location discovery.
        }
    }
}
if ($Runtime -ne 'SeventhHeaven' -and [string]::IsNullOrWhiteSpace($ReloadedRoot)) {
    if ([string]::IsNullOrWhiteSpace($GameRoot)) {
        $gameArguments = @{}
        if (-not [string]::IsNullOrWhiteSpace($SteamRoot)) {
            $gameArguments.SteamRoot = $SteamRoot
        }
        $detectedInstallation = Resolve-Ff7Installation @gameArguments
        $GameRoot = [string]$detectedInstallation.GameRoot
    }
    $portableReloadedRoot = Join-Path $GameRoot 'Reloaded-II'
    foreach ($candidate in @(
        $portableReloadedRoot,
        (Join-Path (Split-Path -Parent $GameRoot) 'Reloaded-II'),
        (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Reloaded-II'),
        (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\Reloaded-II'),
        (Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Reloaded-II'),
        (Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Reloaded-II')
    )) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and
            (Test-Path -LiteralPath (Join-Path $candidate 'Reloaded-II.exe') -PathType Leaf)) {
            $ReloadedRoot = $candidate
            break
        }
    }
    if ([string]::IsNullOrWhiteSpace($ReloadedRoot)) {
        $ReloadedRoot = $portableReloadedRoot
    }
}

if ($Runtime -eq 'SeventhHeaven' -and [string]::IsNullOrWhiteSpace($SeventhHeavenRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($env:SEVENTH_HEAVEN_ROOT)) {
        $SeventhHeavenRoot = $env:SEVENTH_HEAVEN_ROOT
    }
    foreach ($registryRoot in @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
    )) {
        if (-not [string]::IsNullOrWhiteSpace($SeventhHeavenRoot)) { break }
        foreach ($key in @(Get-ChildItem -LiteralPath $registryRoot -ErrorAction SilentlyContinue)) {
            $properties = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction SilentlyContinue
            if ([string]$properties.DisplayName -match '^7th Heaven(?:\s|$)' -and
                -not [string]::IsNullOrWhiteSpace([string]$properties.InstallLocation)) {
                $SeventhHeavenRoot = [string]$properties.InstallLocation
                break
            }
        }
    }
}

if ($Runtime -eq 'SeventhHeaven') {
    if ([string]::IsNullOrWhiteSpace($SeventhHeavenRoot)) {
        throw '7th Heaven was not detected. Supply -SeventhHeavenRoot to launch it.'
    }
    $seventhHeavenExe = Join-Path $SeventhHeavenRoot '7th Heaven.exe'
    if (-not (Test-Path -LiteralPath $seventhHeavenExe -PathType Leaf)) {
        throw "7th Heaven executable is missing: $seventhHeavenExe"
    }
    Start-Process -FilePath $seventhHeavenExe -WorkingDirectory $SeventhHeavenRoot
    return
}

$nativeParityGate = if ($Runtime -eq 'Native') {
    Assert-Ff7NativeParityReleaseGate -ParityMatrixPath $ParityMatrixPath `
        -AllowResearch:$AllowResearchNative
}
else {
    $null
}
$useResearchProfile = $null -ne $nativeParityGate -and $nativeParityGate.IsResearchOverride

$reloadedExe = Join-Path $ReloadedRoot 'Reloaded-II.exe'
if (-not (Test-Path -LiteralPath $reloadedExe -PathType Leaf)) {
    throw "Reloaded-II executable is missing: $reloadedExe"
}

if ([string]::IsNullOrWhiteSpace($GameRoot)) {
    $profileDirectory = if ($Runtime -eq 'Native') {
        if ($useResearchProfile) { 'Ff7.Native.Steam2026.Research' } else { 'Ff7.Native.Steam2026' }
    }
    else {
        'Ff7.En.Steam'
    }
    $profilePath = Join-Path $ReloadedRoot (Join-Path 'Apps' (Join-Path $profileDirectory 'AppConfig.json'))
    if (Test-Path -LiteralPath $profilePath -PathType Leaf) {
        $profile = [IO.File]::ReadAllText($profilePath) | ConvertFrom-Json
        $configuredWorkingDirectory = [string]$profile.WorkingDirectory
        if (-not [string]::IsNullOrWhiteSpace($configuredWorkingDirectory) -and
            (Test-Path -LiteralPath $configuredWorkingDirectory -PathType Container)) {
            if ($Runtime -eq 'Native') {
                $GameRoot = $configuredWorkingDirectory
            }
            else {
                $possible2026Root = [IO.Path]::GetFullPath((Join-Path $configuredWorkingDirectory '..\..'))
                $GameRoot = if (Test-Path -LiteralPath (Join-Path $possible2026Root 'FFVII.exe') -PathType Leaf) {
                    $possible2026Root
                }
                else {
                    $configuredWorkingDirectory
                }
            }
        }
    }
}

$resolveArguments = @{}
if (-not [string]::IsNullOrWhiteSpace($GameRoot)) {
    $resolveArguments.GameRoot = $GameRoot
}
if (-not [string]::IsNullOrWhiteSpace($SteamRoot)) {
    $resolveArguments.SteamRoot = $SteamRoot
}
$installation = Resolve-Ff7Installation @resolveArguments

$gameExe = if ($Runtime -eq 'Native') {
    if ($null -eq $installation.NativeRuntime) {
        throw 'The selected FFVII installation does not contain the native Steam 2026 runtime.'
    }
    $validatedNativeRuntime = Assert-Ff7NativeRuntimeIdentity -Path $installation.NativeRuntime.GameExe
    Assert-Ff7NativeReloadedProfile -ReloadedRoot $ReloadedRoot `
        -NativeRuntime $validatedNativeRuntime -Research:$useResearchProfile | Out-Null
    $validatedNativeRuntime.GameExe
}
else {
    $installation.LegacyRuntime.GameExe
}
if (-not (Test-Path -LiteralPath $gameExe -PathType Leaf)) {
    throw "Selected FFVII runtime executable is missing: $gameExe"
}

# Reloaded-II's verified command-line contract is: --launch "PathToGame\Game.exe".
Start-Process -FilePath $reloadedExe `
    -ArgumentList @('--launch', ('"{0}"' -f $gameExe)) `
    -WorkingDirectory $ReloadedRoot
