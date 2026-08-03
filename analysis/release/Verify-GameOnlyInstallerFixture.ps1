[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $RuntimeZipPath,
    [string] $RepositoryRoot
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $scriptRoot '..\..'))
}
$runtimeZip = [IO.Path]::GetFullPath($RuntimeZipPath)
if (-not (Test-Path -LiteralPath $runtimeZip -PathType Leaf)) {
    throw "Release runtime archive is missing: $runtimeZip"
}

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) `
    ('bs-game-only-fixture-' + [Guid]::NewGuid().ToString('N'))
$expandedRuntime = Join-Path $fixtureRoot 'runtime'
try {
    New-Item -ItemType Directory -Path $expandedRuntime -Force | Out-Null
    Expand-Archive -LiteralPath $runtimeZip -DestinationPath $expandedRuntime

    $gameRoot = Join-Path $fixtureRoot 'game'
    $legacyRoot = Join-Path $gameRoot 'ff7\workingdir'
    $reloadedRoot = Join-Path $gameRoot 'Reloaded-II'
    $settingsPath = Join-Path $fixtureRoot 'appdata\Reloaded-Mod-Loader-II\ReloadedII.json'
    New-Item -ItemType Directory -Path $legacyRoot -Force | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $legacyRoot 'ff7_en.exe'),
        'controlled x86 game fixture',
        (New-Object Text.UTF8Encoding($false)))

    Import-Module (Join-Path $RepositoryRoot 'ReloadedPrerequisiteInstall.psm1') -Force
    Import-Module (Join-Path $RepositoryRoot 'FF7SteamInstall.psm1') -Force

    $probeCalls = New-Object 'System.Collections.Generic.List[string]'
    $runtimeProbe = {
        param($Architecture, $MinimumVersion)
        $probeCalls.Add([string]$Architecture)
        return $true
    }.GetNewClosure()
    $runtimeInstaller = {
        param($Architecture, $InstallerPath)
        throw 'The controlled fixture unexpectedly invoked a .NET installer.'
    }
    $prerequisite = Install-BlindSwordsmanReloadedPrerequisites `
        -BundlePath (Join-Path $expandedRuntime 'prerequisites') `
        -ReloadedRoot $reloadedRoot `
        -RequiredArchitectures @('x86') `
        -SettingsPath $settingsPath `
        -RuntimeProbe $runtimeProbe `
        -RuntimeInstaller $runtimeInstaller

    $package = Install-Ff7DualRuntimePackage `
        -PackagePath (Join-Path $expandedRuntime 'package\ff7.accessibility.reloaded') `
        -ModDirectory (Join-Path $reloadedRoot 'Mods\ff7.accessibility.reloaded')
    $legacyRuntime = [pscustomobject]@{
        Architecture = 'x86'
        RuntimeRoot = $legacyRoot
        GameExe = Join-Path $legacyRoot 'ff7_en.exe'
    }
    $profile = Install-Ff7LegacyReloadedProfile `
        -ReloadedRoot $reloadedRoot `
        -LegacyRuntime $legacyRuntime `
        -TemplatePath (Join-Path $RepositoryRoot 'templates\Ff7.Legacy.Steam.AppConfig.json')
    Copy-Item -LiteralPath (Join-Path $reloadedRoot '_asi_extract\ASILoader32.dll') `
        -Destination (Join-Path $legacyRoot 'dsound.dll')
    Copy-Item -LiteralPath (Join-Path $reloadedRoot 'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') `
        -Destination (Join-Path $legacyRoot 'Reloaded.Mod.Loader.Bootstrapper.asi')

    $checks = [ordered]@{
        ReloadedExe = Test-Path -LiteralPath (Join-Path $reloadedRoot 'Reloaded-II.exe') -PathType Leaf
        SharedHooksX86 = Test-Path -LiteralPath (Join-Path $reloadedRoot 'Mods\reloaded.sharedlib.hooks\x86\Reloaded.Hooks.ReloadedII.dll') -PathType Leaf
        Mod = Test-Path -LiteralPath (Join-Path $reloadedRoot 'Mods\ff7.accessibility.reloaded\ModConfig.json') -PathType Leaf
        LegacyProfile = Test-Path -LiteralPath (Join-Path $reloadedRoot 'Apps\Ff7.En.Steam\AppConfig.json') -PathType Leaf
        X86AsiTarget = Test-Path -LiteralPath (Join-Path $legacyRoot 'dsound.dll') -PathType Leaf
        X86BootstrapTarget = Test-Path -LiteralPath (Join-Path $legacyRoot 'Reloaded.Mod.Loader.Bootstrapper.asi') -PathType Leaf
        NoNativeGameTarget = -not (Test-Path -LiteralPath (Join-Path $gameRoot 'd3d11.dll'))
        NoNativeProfile = -not (Test-Path -LiteralPath (Join-Path $reloadedRoot 'Apps\Ff7.Native.Steam2026\AppConfig.json'))
    }
    $failed = @($checks.GetEnumerator() | Where-Object Value -ne $true)
    if ($failed.Count -ne 0 -or ($probeCalls.ToArray() -join ',') -cne 'x86' -or
        @($prerequisite.InstalledDotNetArchitectures).Count -ne 0) {
        throw 'Controlled game-only fixture verification failed.'
    }

    [pscustomobject]@{
        Fixture = 'fresh x86-only game'
        PackageFingerprint = [string]$package.Fingerprint
        ProfilePath = [string]$profile.ProfilePath
        ProbedArchitectures = $probeCalls.ToArray()
        DotNetInstallerInvoked = $false
        Checks = [pscustomobject]$checks
    }
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot -PathType Container) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
