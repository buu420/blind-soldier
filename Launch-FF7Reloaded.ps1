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

if ($Runtime -eq 'SeventhHeaven') {
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
