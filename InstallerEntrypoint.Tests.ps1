$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$installPath = Join-Path $scriptRoot 'Install-FF7ReloadedMod.ps1'
$uninstallPath = Join-Path $scriptRoot 'Uninstall-FF7ReloadedMod.ps1'

function New-EntrypointFixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('blind-soldier-entrypoint-test-' + [Guid]::NewGuid().ToString('N'))
    $gameRoot = Join-Path $root 'game'
    $runtimeRoot = Join-Path $gameRoot 'runtime'
    $reloadedRoot = Join-Path $root 'Reloaded-II'
    $modDirectory = Join-Path $reloadedRoot 'Mods\ff7.accessibility.reloaded'
    $modulePath = Join-Path $root 'FakeInstall.psm1'
    New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $modDirectory -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $modDirectory 'fingerprint.txt'), 'INSTALLED')
    [IO.File]::WriteAllText((Join-Path $runtimeRoot 'dsound.dll'), 'loader')
    [IO.File]::WriteAllText((Join-Path $reloadedRoot 'Reloaded-II.exe'), 'shared prerequisite')
    $legacyProfilePath = Join-Path $reloadedRoot 'Apps\Ff7.En.Steam\AppConfig.json'
    New-Item -ItemType Directory -Path (Split-Path -Parent $legacyProfilePath) -Force | Out-Null
    [IO.File]::WriteAllText($legacyProfilePath, '{"AppId":"ff7_en.exe"}')

    $module = @'
function Resolve-Ff7Installation {
    param([string] $GameRoot)
    $root = [IO.Path]::GetFullPath($GameRoot)
    return [pscustomobject]@{
        Version = 'Steam2013'; SteamAppId = '39140'; GameRoot = $root
        LegacyRuntime = [pscustomobject]@{ RuntimeId = 'ff7-steam-legacy-x86'; Architecture = 'x86'; RuntimeRoot = (Join-Path $root 'runtime'); GameExe = (Join-Path $root 'runtime\ff7_en.exe') }
        NativeRuntime = $null
    }
}

function Assert-Ff7DualRuntimePackage {
    param([string] $PackagePath)
    $marker = Join-Path $PackagePath 'fingerprint.txt'
    if (-not (Test-Path -LiteralPath $marker -PathType Leaf)) { throw 'invalid package fixture' }
    return [pscustomobject]@{ Fingerprint = [IO.File]::ReadAllText($marker) }
}
Export-ModuleMember -Function Resolve-Ff7Installation,Assert-Ff7DualRuntimePackage
'@
    [IO.File]::WriteAllText($modulePath, $module)
    $loaderPath = Join-Path $runtimeRoot 'dsound.dll'
    $state = [ordered]@{
        schemaVersion = 2
        productVersion = '0.1.0-pre.1'
        releaseTag = 'v0.1.0-pre.1'
        game = [ordered]@{ gameRoot = $gameRoot }
        reloadedRoot = $reloadedRoot
        mod = [ordered]@{
            directory = $modDirectory
            fingerprint = 'INSTALLED'
            backupPath = $null
            backupFingerprint = $null
        }
        profile = $null
        legacyProfile = [ordered]@{
            path = $legacyProfilePath
            changed = $true
            installedSha256 = (Get-FileHash -LiteralPath $legacyProfilePath -Algorithm SHA256).Hash
            backupPath = $null
            backupSha256 = $null
            research = $false
        }
        loaders = @([ordered]@{
            id = 'legacy-asi-loader'
            target = $loaderPath
            sha256 = (Get-FileHash -LiteralPath $loaderPath -Algorithm SHA256).Hash
            changed = $true
        })
        openingVoice = [ordered]@{ wasPresent = $false; target = (Join-Path $runtimeRoot 'override\movies\opening_va.ogg'); sourceSha256 = $null }
    }
    $statePath = Join-Path $root 'install-state.json'
    [IO.File]::WriteAllText($statePath, ($state | ConvertTo-Json -Depth 8))
    return [pscustomobject]@{
        Root = $root
        GameRoot = $gameRoot
        RuntimeRoot = $runtimeRoot
        ReloadedRoot = $reloadedRoot
        ModDirectory = $modDirectory
        LoaderPath = $loaderPath
        LegacyProfilePath = $legacyProfilePath
        ModulePath = $modulePath
        StatePath = $statePath
        ResultPath = Join-Path $root 'uninstall-result.json'
    }
}

function New-EntrypointTestPe {
    param([string] $Path, [uint16] $Machine)
    $parent = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $bytes = New-Object byte[] 256
    $bytes[0] = 0x4D
    $bytes[1] = 0x5A
    [BitConverter]::GetBytes([int]0x80).CopyTo($bytes, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 0x80)
    [BitConverter]::GetBytes($Machine).CopyTo($bytes, 0x84)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function New-InstallEntrypointFixture {
    param([ValidateSet('legacy-only', 'native-only', 'dual')] [string] $RuntimeMode)

    $root = Join-Path ([IO.Path]::GetTempPath()) ('blind-soldier-install-entrypoint-test-' + [Guid]::NewGuid().ToString('N'))
    $gameRoot = Join-Path $root 'game'
    $legacyRoot = Join-Path $gameRoot 'legacy'
    $nativeRoot = Join-Path $gameRoot 'native'
    $reloadedRoot = Join-Path $root 'Reloaded-II'
    $packagePath = Join-Path $root 'package'
    $launcherBundlePath = Join-Path $root 'launcher'
    $prerequisiteBundlePath = Join-Path $root 'prerequisites'
    New-Item -ItemType Directory -Path $legacyRoot,$nativeRoot,$launcherBundlePath,$prerequisiteBundlePath -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $packagePath 'Assets\movies') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $packagePath 'ModConfig.json'), '{"ModId":"ff7.accessibility.reloaded","ModVersion":"0.1.0-pre.2"}')
    [IO.File]::WriteAllText((Join-Path $packagePath 'fingerprint.txt'), 'TEST-PACKAGE')
    [IO.File]::WriteAllText((Join-Path $packagePath 'Assets\movies\opening_audio_description.ogg'), 'voice')
    if ($RuntimeMode -ne 'native-only') {
        [IO.File]::WriteAllText((Join-Path $legacyRoot 'ff7_en.exe'), 'legacy game fixture')
    }

    $modulePath = Join-Path $root 'FakeInstall.psm1'
    $module = @'
function Resolve-Ff7Installation {
    param([string] $GameRoot, [string] $SteamRoot)
    $game = [IO.Path]::GetFullPath($env:BLIND_SWORDSMAN_INSTALL_TEST_GAME_ROOT)
    $legacy = [IO.Path]::GetFullPath($env:BLIND_SWORDSMAN_INSTALL_TEST_LEGACY_ROOT)
    $native = [IO.Path]::GetFullPath($env:BLIND_SWORDSMAN_INSTALL_TEST_NATIVE_ROOT)
    if ($env:BLIND_SWORDSMAN_INSTALL_TEST_RUNTIME_MODE -eq 'legacy-only') {
        $legacyRuntime = [pscustomobject]@{ RuntimeId = 'ff7-steam-legacy-x86'; Architecture = 'x86'; RuntimeRoot = $legacy; GameExe = (Join-Path $legacy 'ff7_en.exe') }
        return [pscustomobject]@{ Version = 'Steam2013'; SteamAppId = '39140'; GameRoot = $game; RuntimeRoot = $legacy; GameExe = $legacyRuntime.GameExe; SourceExe = $null; LegacyRuntime = $legacyRuntime; NativeRuntime = $null }
    }
    $nativeRuntime = [pscustomobject]@{ RuntimeId = 'ff7-steam-2026-x64'; Architecture = 'x64'; RuntimeRoot = $native; GameExe = (Join-Path $native 'FFVII.exe') }
    $legacyRuntime = if ($env:BLIND_SWORDSMAN_INSTALL_TEST_RUNTIME_MODE -eq 'dual') {
        [pscustomobject]@{ RuntimeId = 'ff7-steam-legacy-x86'; Architecture = 'x86'; RuntimeRoot = $legacy; GameExe = (Join-Path $legacy 'ff7_en.exe') }
    } else { $null }
    return [pscustomobject]@{ Version = 'Steam2026'; SteamAppId = '3837340'; GameRoot = $game; RuntimeRoot = $null; GameExe = $nativeRuntime.GameExe; SourceExe = $null; LegacyRuntime = $legacyRuntime; NativeRuntime = $nativeRuntime }
}
function Assert-Ff7NativeRuntimeIdentity { param([string] $Path) return (Resolve-Ff7Installation).NativeRuntime }
function Assert-Ff7NativeParityReleaseGate { param([string] $ParityMatrixPath, [switch] $AllowResearch) return [pscustomobject]@{ IsReleaseReady = $true } }
function Initialize-Ff7CompatibilityRuntime { param([psobject] $Installation) return $Installation }
function Install-Ff7DualRuntimePackage {
    param([string] $PackagePath, [string] $ModDirectory, [switch] $ValidateOnly)
    if ($ValidateOnly) { return [pscustomobject]@{ Fingerprint = 'TEST-PACKAGE' } }
    New-Item -ItemType Directory -Path $ModDirectory -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PackagePath 'ModConfig.json') -Destination (Join-Path $ModDirectory 'ModConfig.json') -Force
    return [pscustomobject]@{ Changed = $true; ModDirectory = $ModDirectory; Fingerprint = 'TEST-PACKAGE'; BackupPath = $null; BackupFingerprint = $null }
}
function Install-Ff7NativeReloadedProfile {
    param([string] $ReloadedRoot, [psobject] $NativeRuntime, [string] $TemplatePath, [string] $ParityMatrixPath, [switch] $AllowResearch, [switch] $ValidateOnly)
    if ($ValidateOnly) { return }
    $profilePath = Join-Path $ReloadedRoot 'Apps\Ff7.Native.Steam2026\AppConfig.json'
    New-Item -ItemType Directory -Path (Split-Path -Parent $profilePath) -Force | Out-Null
    [IO.File]::WriteAllText($profilePath, '{}')
    return [pscustomobject]@{ Changed = $true; ProfilePath = $profilePath; BackupPath = $null; IsResearchProfile = $false }
}
function Install-Ff7LegacyReloadedProfile {
    param([string] $ReloadedRoot, [psobject] $LegacyRuntime, [string] $TemplatePath, [switch] $ValidateOnly)
    if ($ValidateOnly) { return }
    $profilePath = Join-Path $ReloadedRoot 'Apps\Ff7.En.Steam\AppConfig.json'
    New-Item -ItemType Directory -Path (Split-Path -Parent $profilePath) -Force | Out-Null
    [IO.File]::WriteAllText($profilePath, '{}')
    return [pscustomobject]@{ Changed = $true; ProfilePath = $profilePath; BackupPath = $null; IsResearchProfile = $false }
}
function Assert-Ff7NativeReloadedProfile { param([string] $ReloadedRoot, [psobject] $NativeRuntime, [switch] $Research) }
function Disable-Ff7OpeningMovieNativeVoiceLayer {
    param([string] $RuntimeRoot, [string] $SourcePath)
    return [pscustomobject]@{ Removed = $false; TargetPath = (Join-Path $RuntimeRoot 'override\movies\opening_va.ogg') }
}
Export-ModuleMember -Function *
'@
    [IO.File]::WriteAllText($modulePath, $module)

    $launcherModulePath = Join-Path $root 'FakeLauncher.psm1'
    $launcherModule = @'
function Install-Ff7AccessibleLauncher {
    param([string] $GameRoot, [string] $ReloadedRoot, [string] $BundlePath, [switch] $ValidateOnly)
    if ($ValidateOnly) { return }
    return [pscustomobject]@{ Changed = $false; State = $null }
}
function Complete-Ff7AccessibleLauncherTransaction { param([psobject] $Result) }
function Undo-Ff7AccessibleLauncherTransaction { param([psobject] $Result) }
Export-ModuleMember -Function *
'@
    [IO.File]::WriteAllText($launcherModulePath, $launcherModule)

    $prerequisiteModulePath = Join-Path $root 'FakePrerequisite.psm1'
    $prerequisiteModule = @'
function New-FakePe {
    param([string] $Path, [uint16] $Machine)
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $bytes = New-Object byte[] 256
    $bytes[0] = 0x4D; $bytes[1] = 0x5A
    [BitConverter]::GetBytes([int]0x80).CopyTo($bytes, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 0x80)
    [BitConverter]::GetBytes($Machine).CopyTo($bytes, 0x84)
    [IO.File]::WriteAllBytes($Path, $bytes)
}
function Install-BlindSwordsmanReloadedPrerequisites {
    param([string] $BundlePath, [string] $ReloadedRoot, [string[]] $RequiredArchitectures, [string] $SettingsPath)
    New-Item -ItemType Directory -Path $ReloadedRoot -Force | Out-Null
    if ($RequiredArchitectures -contains 'x86') {
        New-FakePe -Path (Join-Path $ReloadedRoot '_asi_extract\ASILoader32.dll') -Machine 0x014C
        New-FakePe -Path (Join-Path $ReloadedRoot 'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Machine 0x014C
    }
    if ($RequiredArchitectures -contains 'x64') {
        New-FakePe -Path (Join-Path $ReloadedRoot '_asi_extract\ASILoader64.dll') -Machine 0x8664
        New-FakePe -Path (Join-Path $ReloadedRoot 'Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Machine 0x8664
    }
    return [pscustomobject]@{ ReloadedRoot = [IO.Path]::GetFullPath($ReloadedRoot) }
}
Export-ModuleMember -Function Install-BlindSwordsmanReloadedPrerequisites
'@
    [IO.File]::WriteAllText($prerequisiteModulePath, $prerequisiteModule)

    return [pscustomobject]@{
        Root = $root
        RuntimeMode = $RuntimeMode
        GameRoot = $gameRoot
        LegacyRoot = $legacyRoot
        NativeRoot = $nativeRoot
        ReloadedRoot = $reloadedRoot
        PackagePath = $packagePath
        PrerequisiteBundlePath = $prerequisiteBundlePath
        PrerequisiteModulePath = $prerequisiteModulePath
        LauncherBundlePath = $launcherBundlePath
        ModulePath = $modulePath
        LauncherModulePath = $launcherModulePath
        ReloadedSettingsPath = Join-Path $root 'appdata\Reloaded-Mod-Loader-II\ReloadedII.json'
        ResultPath = Join-Path $root 'install-result.json'
    }
}

function Invoke-InstallEntrypointFixture {
    param($Fixture, [switch] $DiscoverHeadlessRoot)
    $env:BLIND_SWORDSMAN_INSTALL_TEST_RUNTIME_MODE = $Fixture.RuntimeMode
    $env:BLIND_SWORDSMAN_INSTALL_TEST_GAME_ROOT = $Fixture.GameRoot
    $env:BLIND_SWORDSMAN_INSTALL_TEST_LEGACY_ROOT = $Fixture.LegacyRoot
    $env:BLIND_SWORDSMAN_INSTALL_TEST_NATIVE_ROOT = $Fixture.NativeRoot
    $arguments = @{
        GameRoot = $Fixture.GameRoot
        PackagePath = $Fixture.PackagePath
        PrerequisiteBundlePath = $Fixture.PrerequisiteBundlePath
        PrerequisiteModulePath = $Fixture.PrerequisiteModulePath
        ModulePath = $Fixture.ModulePath
        LauncherModulePath = $Fixture.LauncherModulePath
        ResultPath = $Fixture.ResultPath
        ProductVersion = '0.1.0-pre.2'
        ReleaseTag = 'v0.1.0-pre.2'
        SkipFfnx = $true
    }
    if ($DiscoverHeadlessRoot) {
        $arguments.ReloadedSettingsPath = $Fixture.ReloadedSettingsPath
    }
    else {
        $arguments.ReloadedRoot = $Fixture.ReloadedRoot
    }
    if ($Fixture.RuntimeMode -ne 'legacy-only') {
        $arguments.LauncherBundlePath = $Fixture.LauncherBundlePath
        $arguments.AllowResearchNativeProfile = $true
    }
    & $installPath @arguments
}

Describe 'Blind Soldier installer entry points' {
    AfterEach {
        Remove-Item Env:\BLIND_SWORDSMAN_INSTALL_TEST_RUNTIME_MODE -ErrorAction SilentlyContinue
        Remove-Item Env:\BLIND_SWORDSMAN_INSTALL_TEST_GAME_ROOT -ErrorAction SilentlyContinue
        Remove-Item Env:\BLIND_SWORDSMAN_INSTALL_TEST_LEGACY_ROOT -ErrorAction SilentlyContinue
        Remove-Item Env:\BLIND_SWORDSMAN_INSTALL_TEST_NATIVE_ROOT -ErrorAction SilentlyContinue
    }

    It 'accepts a verified prebuilt package and structured result path without requiring a source build' {
        $command = Get-Command $installPath
        ($command.Parameters.Keys -contains 'PackagePath') | Should Be $true
        ($command.Parameters.Keys -contains 'ResultPath') | Should Be $true
        ($command.Parameters.Keys -contains 'LauncherBundlePath') | Should Be $true
        ($command.Parameters.Keys -contains 'PrerequisiteBundlePath') | Should Be $true
        ($command.Parameters.Keys -contains 'PrerequisiteModulePath') | Should Be $true
        $content = [IO.File]::ReadAllText($installPath)
        $content | Should Match 'IsNullOrWhiteSpace\(\$PackagePath\)'
        $content | Should Match 'Install-Ff7DualRuntimePackage -PackagePath \$stagedPackage'
        $content | Should Match 'install-result-'
    }

    It 'wires the accessible launcher transaction into install state, rollback, and uninstall' {
        $installCommand = Get-Command $installPath
        $uninstallCommand = Get-Command $uninstallPath
        ($installCommand.Parameters.Keys -contains 'LauncherBundlePath') | Should Be $true
        ($uninstallCommand.Parameters.Keys -contains 'LauncherModulePath') | Should Be $true

        $installContent = [IO.File]::ReadAllText($installPath)
        $installContent | Should Match 'Install-Ff7AccessibleLauncher[\s\S]+-ValidateOnly'
        $installContent | Should Match 'Undo-Ff7AccessibleLauncherTransaction'
        $installContent | Should Match 'Complete-Ff7AccessibleLauncherTransaction'
        $installContent | Should Match 'launcher\s*=\s*\$launcherState'

        $uninstallContent = [IO.File]::ReadAllText($uninstallPath)
        $uninstallContent | Should Match 'Restore-Ff7AccessibleLauncherFromState'
        $uninstallContent | Should Match '\$state\.launcher'
    }

    It 'deploys a legacy-only installation without any x64 loader sources' {
        $fixture = New-InstallEntrypointFixture -RuntimeMode legacy-only
        try {
            Invoke-InstallEntrypointFixture $fixture

            Test-Path -LiteralPath (Join-Path $fixture.LegacyRoot 'dsound.dll') -PathType Leaf | Should Be $true
            Test-Path -LiteralPath (Join-Path $fixture.LegacyRoot 'Reloaded.Mod.Loader.Bootstrapper.asi') -PathType Leaf | Should Be $true
            Test-Path -LiteralPath (Join-Path $fixture.ReloadedRoot '_asi_extract\ASILoader64.dll') | Should Be $false
            Test-Path -LiteralPath (Join-Path $fixture.ReloadedRoot 'Apps\Ff7.En.Steam\AppConfig.json') -PathType Leaf | Should Be $true
            $result = [IO.File]::ReadAllText($fixture.ResultPath) | ConvertFrom-Json
            $result.schemaVersion | Should Be 2
            [string]$result.legacyProfile.path | Should Be (Join-Path $fixture.ReloadedRoot 'Apps\Ff7.En.Steam\AppConfig.json')
        }
        finally {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
    }

    It 'deploys a native-only installation without any x86 loader or legacy voice path' {
        $fixture = New-InstallEntrypointFixture -RuntimeMode native-only
        try {
            Invoke-InstallEntrypointFixture $fixture

            Test-Path -LiteralPath (Join-Path $fixture.NativeRoot 'd3d11.dll') -PathType Leaf | Should Be $true
            Test-Path -LiteralPath (Join-Path $fixture.NativeRoot 'Reloaded.Mod.Loader.Bootstrapper.asi') -PathType Leaf | Should Be $true
            Test-Path -LiteralPath (Join-Path $fixture.ReloadedRoot '_asi_extract\ASILoader32.dll') | Should Be $false
            $result = [IO.File]::ReadAllText($fixture.ResultPath) | ConvertFrom-Json
            $null -eq $result.legacyProfile | Should Be $true
        }
        finally {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
    }

    It 'removes only the exact recorded mod and installer-created unchanged loader' {
        $fixture = New-EntrypointFixture
        try {
            & $uninstallPath -StatePath $fixture.StatePath -ResultPath $fixture.ResultPath `
                -ModulePath $fixture.ModulePath

            (Test-Path -LiteralPath $fixture.ModDirectory) | Should Be $false
            (Test-Path -LiteralPath $fixture.LoaderPath) | Should Be $false
            (Test-Path -LiteralPath $fixture.LegacyProfilePath) | Should Be $false
            (Test-Path -LiteralPath (Join-Path $fixture.ReloadedRoot 'Reloaded-II.exe') -PathType Leaf) | Should Be $true
            $result = [IO.File]::ReadAllText($fixture.ResultPath) | ConvertFrom-Json
            $result.completed | Should Be $true
            (@($result.removed) -contains $fixture.ModDirectory) | Should Be $true
            (@($result.removed) -contains $fixture.LoaderPath) | Should Be $true
        }
        finally {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
    }

    It 'deploys both runtime profiles when the game contains both supported versions' {
        $fixture = New-InstallEntrypointFixture -RuntimeMode dual
        try {
            Invoke-InstallEntrypointFixture $fixture

            Test-Path -LiteralPath (Join-Path $fixture.LegacyRoot 'dsound.dll') -PathType Leaf | Should Be $true
            Test-Path -LiteralPath (Join-Path $fixture.NativeRoot 'd3d11.dll') -PathType Leaf | Should Be $true
            Test-Path -LiteralPath (Join-Path $fixture.ReloadedRoot 'Apps\Ff7.En.Steam\AppConfig.json') -PathType Leaf | Should Be $true
            Test-Path -LiteralPath (Join-Path $fixture.ReloadedRoot 'Apps\Ff7.Native.Steam2026\AppConfig.json') -PathType Leaf | Should Be $true
            $result = [IO.File]::ReadAllText($fixture.ResultPath) | ConvertFrom-Json
            $result.schemaVersion | Should Be 2
            $null -ne $result.profile | Should Be $true
            $null -ne $result.legacyProfile | Should Be $true
        }
        finally {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
    }

    It 'discovers a headless Reloaded root from loader paths without a manager executable' {
        $fixture = New-InstallEntrypointFixture -RuntimeMode legacy-only
        try {
            $loaderPath = Join-Path $fixture.ReloadedRoot 'Loader\X86\Reloaded.Mod.Loader.dll'
            New-Item -ItemType Directory -Path (Split-Path -Parent $loaderPath) -Force | Out-Null
            [IO.File]::WriteAllText($loaderPath, 'headless loader fixture')
            New-Item -ItemType Directory -Path (Split-Path -Parent $fixture.ReloadedSettingsPath) -Force | Out-Null
            [IO.File]::WriteAllText(
                $fixture.ReloadedSettingsPath,
                (@{ LoaderPath32 = $loaderPath; LauncherPath = '' } | ConvertTo-Json))

            Invoke-InstallEntrypointFixture $fixture -DiscoverHeadlessRoot

            $result = [IO.File]::ReadAllText($fixture.ResultPath) | ConvertFrom-Json
            [string]$result.reloadedRoot | Should Be ([IO.Path]::GetFullPath($fixture.ReloadedRoot))
            Test-Path -LiteralPath (Join-Path $fixture.ReloadedRoot 'Reloaded-II.exe') | Should Be $false
        }
        finally {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
    }

    It 'remains backward compatible with schema-one state and preserves untracked Reloaded files' {
        $fixture = New-EntrypointFixture
        try {
            $state = [IO.File]::ReadAllText($fixture.StatePath) | ConvertFrom-Json
            $state.schemaVersion = 1
            $state.PSObject.Properties.Remove('legacyProfile')
            [IO.File]::WriteAllText($fixture.StatePath, ($state | ConvertTo-Json -Depth 8))

            & $uninstallPath -StatePath $fixture.StatePath -ResultPath $fixture.ResultPath `
                -ModulePath $fixture.ModulePath

            (Test-Path -LiteralPath $fixture.LegacyProfilePath -PathType Leaf) | Should Be $true
            (Test-Path -LiteralPath (Join-Path $fixture.ReloadedRoot 'Reloaded-II.exe') -PathType Leaf) | Should Be $true
        }
        finally {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
    }

    It 'preserves a loader changed after installation and reports why' {
        $fixture = New-EntrypointFixture
        try {
            [IO.File]::WriteAllText($fixture.LoaderPath, 'changed after install')

            & $uninstallPath -StatePath $fixture.StatePath -ResultPath $fixture.ResultPath `
                -ModulePath $fixture.ModulePath

            (Test-Path -LiteralPath $fixture.LoaderPath -PathType Leaf) | Should Be $true
            $result = [IO.File]::ReadAllText($fixture.ResultPath) | ConvertFrom-Json
            ($result.preserved -join "`n") | Should Match 'changed after installation'
        }
        finally {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
    }

    It 'restores the exact recorded prior mod package during uninstall' {
        $fixture = New-EntrypointFixture
        try {
            $backup = Join-Path $fixture.ReloadedRoot 'AccessibilityBackups\ff7.accessibility.reloaded.backup-test'
            New-Item -ItemType Directory -Path $backup -Force | Out-Null
            [IO.File]::WriteAllText((Join-Path $backup 'fingerprint.txt'), 'PREVIOUS')
            $state = [IO.File]::ReadAllText($fixture.StatePath) | ConvertFrom-Json
            $state.mod.backupPath = $backup
            $state.mod.backupFingerprint = 'PREVIOUS'
            [IO.File]::WriteAllText($fixture.StatePath, ($state | ConvertTo-Json -Depth 8))

            & $uninstallPath -StatePath $fixture.StatePath -ResultPath $fixture.ResultPath `
                -ModulePath $fixture.ModulePath

            [IO.File]::ReadAllText((Join-Path $fixture.ModDirectory 'fingerprint.txt')) | Should Be 'PREVIOUS'
            (Test-Path -LiteralPath $backup) | Should Be $false
        }
        finally {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
    }
}
