$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$preflightPath = Join-Path $scriptRoot 'Invoke-BlindSwordsmanPreflight.ps1'

function New-PreflightFixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('blind-swordsman-preflight-test-' + [Guid]::NewGuid().ToString('N'))
    $gameRoot = Join-Path $root 'Final Fantasy VII'
    $reloadedRoot = Join-Path $root 'Reloaded-II'
    $modulePath = Join-Path $root 'FakeInstall.psm1'
    New-Item -ItemType Directory -Path $gameRoot | Out-Null
    foreach ($directory in @(
        '_asi_extract',
        'Loader\X86\Bootstrapper',
        'Loader\X64\Bootstrapper',
        'Mods\reloaded.sharedlib.hooks\x86',
        'Mods\reloaded.sharedlib.hooks\x64'
    )) {
        New-Item -ItemType Directory -Path (Join-Path $reloadedRoot $directory) -Force | Out-Null
    }

    New-TestPe -Path (Join-Path $reloadedRoot '_asi_extract\ASILoader32.dll') -Machine 0x014C
    New-TestPe -Path (Join-Path $reloadedRoot 'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Machine 0x014C
    New-TestPe -Path (Join-Path $reloadedRoot '_asi_extract\ASILoader64.dll') -Machine 0x8664
    New-TestPe -Path (Join-Path $reloadedRoot 'Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Machine 0x8664
    New-TestPe -Path (Join-Path $reloadedRoot 'Mods\reloaded.sharedlib.hooks\x86\Reloaded.Hooks.ReloadedII.dll') -Machine 0x014C
    New-TestPe -Path (Join-Path $reloadedRoot 'Mods\reloaded.sharedlib.hooks\x64\Reloaded.Hooks.ReloadedII.dll') -Machine 0x8664
    [IO.File]::WriteAllText(
        (Join-Path $reloadedRoot 'Mods\reloaded.sharedlib.hooks\ModConfig.json'),
        '{"ModId":"reloaded.sharedlib.hooks","ModVersion":"1.16.3"}')
    [IO.File]::WriteAllText((Join-Path $gameRoot 'FFVII.exe'), 'fixture')

    $module = @'
function Resolve-Ff7Installation {
    param([string] $GameRoot, [string] $SteamRoot)
    if ($env:BLIND_SWORDSMAN_PREFLIGHT_FAIL_GAME -eq '1') { throw 'No supported FFVII Steam installation was found.' }
    $root = [IO.Path]::GetFullPath($env:BLIND_SWORDSMAN_PREFLIGHT_GAME_ROOT)
    $runtimeMode = [string]$env:BLIND_SWORDSMAN_PREFLIGHT_RUNTIME_MODE
    $legacyRuntime = if ($runtimeMode -ne 'native-only') {
        [pscustomobject]@{ RuntimeId = 'ff7-steam-legacy-x86'; Architecture = 'x86'; RuntimeRoot = (Join-Path $root 'ff7\workingdir'); GameExe = (Join-Path $root 'ff7\workingdir\ff7_en.exe') }
    } else { $null }
    $nativeRuntime = if ($runtimeMode -ne 'legacy-only') {
        [pscustomobject]@{ RuntimeId = 'ff7-steam-2026-x64'; Architecture = 'x64'; RuntimeRoot = $root; GameExe = (Join-Path $root 'FFVII.exe') }
    } else { $null }
    return [pscustomobject]@{
        Version = if ($runtimeMode -eq 'legacy-only') { 'Steam2013' } else { 'Steam2026' }
        SteamAppId = if ($runtimeMode -eq 'legacy-only') { '39140' } else { '3837340' }
        GameRoot = $root
        LegacyRuntime = $legacyRuntime
        NativeRuntime = $nativeRuntime
    }
}
function Assert-Ff7NativeRuntimeIdentity { param([string] $Path) return [pscustomobject]@{ RuntimeId = 'ff7-steam-2026-x64'; RuntimeRoot = (Split-Path -Parent $Path); GameExe = $Path } }
function Get-Ff7PeMachine {
    param([string] $Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    $offset = [BitConverter]::ToInt32($bytes, 0x3C)
    return [BitConverter]::ToUInt16($bytes, $offset + 4)
}
Export-ModuleMember -Function Resolve-Ff7Installation,Assert-Ff7NativeRuntimeIdentity,Get-Ff7PeMachine
'@
    [IO.File]::WriteAllText($modulePath, $module)
    return [pscustomobject]@{
        Root = $root
        GameRoot = $gameRoot
        ReloadedRoot = $reloadedRoot
        ReloadedSettingsPath = Join-Path $root 'Reloaded-Mod-Loader-II\ReloadedII.json'
        MissingSeventhHeavenRoot = Join-Path $root '7th Heaven not installed'
        ModulePath = $modulePath
        ResultPath = Join-Path $root 'preflight.json'
    }
}

function New-TestPe {
    param([string] $Path, [uint16] $Machine)
    $bytes = New-Object byte[] 256
    $bytes[0] = 0x4D
    $bytes[1] = 0x5A
    [BitConverter]::GetBytes([int]0x80).CopyTo($bytes, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 0x80)
    [BitConverter]::GetBytes($Machine).CopyTo($bytes, 0x84)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function Invoke-FixturePreflight {
    param($Fixture, [switch] $DetectReloaded)
    $arguments = @{
        GameRoot = $Fixture.GameRoot
        SeventhHeavenRoot = $Fixture.MissingSeventhHeavenRoot
        ReloadedSettingsPath = $Fixture.ReloadedSettingsPath
        ModulePath = $Fixture.ModulePath
        ResultPath = $Fixture.ResultPath
    }
    if (-not $DetectReloaded) {
        $arguments.ReloadedRoot = $Fixture.ReloadedRoot
    }
    & $preflightPath @arguments
    return [IO.File]::ReadAllText($Fixture.ResultPath) | ConvertFrom-Json
}

Describe 'Blind Swordsman installer preflight' {
    BeforeEach {
        $fixture = New-PreflightFixture
        $env:BLIND_SWORDSMAN_PREFLIGHT_GAME_ROOT = $fixture.GameRoot
        $env:BLIND_SWORDSMAN_PREFLIGHT_FAIL_GAME = '0'
        $env:BLIND_SWORDSMAN_PREFLIGHT_RUNTIME_MODE = 'dual'
    }

    AfterEach {
        Remove-Item Env:\BLIND_SWORDSMAN_PREFLIGHT_GAME_ROOT -ErrorAction SilentlyContinue
        Remove-Item Env:\BLIND_SWORDSMAN_PREFLIGHT_FAIL_GAME -ErrorAction SilentlyContinue
        Remove-Item Env:\BLIND_SWORDSMAN_PREFLIGHT_RUNTIME_MODE -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $fixture.Root) {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
    }

    It 'reports a validated dual-runtime game and all required Reloaded dependencies' {
        $report = Invoke-FixturePreflight $fixture

        $report.schemaVersion | Should Be 1
        $report.canInstall | Should Be $true
        $report.game.version | Should Be 'Steam2026'
        $report.game.runtimes.Count | Should Be 2
        ($report.dependencies | Where-Object id -eq 'reloaded').satisfied | Should Be $true
        ($report.dependencies | Where-Object id -eq 'shared-hooks').satisfied | Should Be $true
        ($report.dependencies | Where-Object id -eq 'seventh-heaven').severity | Should Be 'optional'
    }

    It 'requires only x86 Reloaded dependencies for a legacy-only installation' {
        $env:BLIND_SWORDSMAN_PREFLIGHT_RUNTIME_MODE = 'legacy-only'
        Remove-Item -LiteralPath (Join-Path $fixture.ReloadedRoot '_asi_extract\ASILoader64.dll')
        Remove-Item -LiteralPath (Join-Path $fixture.ReloadedRoot 'Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll')
        Remove-Item -LiteralPath (Join-Path $fixture.ReloadedRoot 'Mods\reloaded.sharedlib.hooks\x64\Reloaded.Hooks.ReloadedII.dll')

        $report = Invoke-FixturePreflight $fixture

        $report.canInstall | Should Be $true
        $report.game.runtimes.Count | Should Be 1
        $report.game.runtimes[0].architecture | Should Be 'x86'
        $loaders = $report.dependencies | Where-Object id -eq 'reloaded-loaders'
        $loaders.name | Should Match 'x86'
        $loaders.name | Should Not Match 'x64'
        $loaders.message | Should Not Match 'x64'
        ($report.dependencies | Where-Object id -eq 'shared-hooks').message | Should Not Match 'x64'
    }

    It 'requires only x64 Reloaded dependencies for a native-only installation' {
        $env:BLIND_SWORDSMAN_PREFLIGHT_RUNTIME_MODE = 'native-only'
        Remove-Item -LiteralPath (Join-Path $fixture.ReloadedRoot '_asi_extract\ASILoader32.dll')
        Remove-Item -LiteralPath (Join-Path $fixture.ReloadedRoot 'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll')
        Remove-Item -LiteralPath (Join-Path $fixture.ReloadedRoot 'Mods\reloaded.sharedlib.hooks\x86\Reloaded.Hooks.ReloadedII.dll')

        $report = Invoke-FixturePreflight $fixture

        $report.canInstall | Should Be $true
        $report.game.runtimes.Count | Should Be 1
        $report.game.runtimes[0].architecture | Should Be 'x64'
        $loaders = $report.dependencies | Where-Object id -eq 'reloaded-loaders'
        $loaders.name | Should Match 'x64'
        $loaders.name | Should Not Match 'x86'
        $loaders.message | Should Not Match 'x86'
        ($report.dependencies | Where-Object id -eq 'shared-hooks').message | Should Not Match 'x86'
    }

    It 'contains no developer-specific dependency locations' {
        foreach ($scriptName in @(
            'Invoke-BlindSwordsmanPreflight.ps1',
            'Install-FF7ReloadedMod.ps1',
            'Launch-FF7Reloaded.ps1'
        )) {
            $scriptText = [IO.File]::ReadAllText((Join-Path $scriptRoot $scriptName))
            $scriptText | Should Not Match 'AccessXI\\external\\Reloaded-II'
            $scriptText | Should Not Match 'buu42'
            $scriptText | Should Not Match 'Tools\\7thHeaven'
        }
    }

    It 'keeps 7th Heaven and FFNx optional when neither is installed' {
        $report = Invoke-FixturePreflight $fixture

        $report.canInstall | Should Be $true
        $report.seventhHeavenRoot | Should Be $null
        $seventhHeaven = $report.dependencies | Where-Object id -eq 'seventh-heaven'
        $seventhHeaven.severity | Should Be 'optional'
        $seventhHeaven.satisfied | Should Be $false
        $seventhHeaven.message | Should Match 'optional'
        $ffnx = $report.dependencies | Where-Object id -eq 'ffnx'
        $ffnx.severity | Should Be 'optional'
        $ffnx.satisfied | Should Be $false
        $ffnx.message | Should Match 'optional'
    }

    It 'discovers Reloaded-II from its registered launcher instead of a developer path' {
        $settingsDirectory = Split-Path -Parent $fixture.ReloadedSettingsPath
        New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
        [IO.File]::WriteAllText(
            $fixture.ReloadedSettingsPath,
            (@{ LauncherPath = (Join-Path $fixture.ReloadedRoot 'Reloaded-II.exe') } | ConvertTo-Json))
        [IO.File]::WriteAllText((Join-Path $fixture.ReloadedRoot 'Reloaded-II.exe'), 'fixture')

        $report = Invoke-FixturePreflight $fixture -DetectReloaded

        $report.canInstall | Should Be $true
        $report.reloadedRoot | Should Be ([IO.Path]::GetFullPath($fixture.ReloadedRoot))
    }

    It 'proposes a portable Reloaded-II folder inside the detected game when none is registered' {
        $report = Invoke-FixturePreflight $fixture -DetectReloaded

        $report.canInstall | Should Be $true
        $report.reloadedRoot | Should Be ([IO.Path]::GetFullPath((Join-Path $fixture.GameRoot 'Reloaded-II')))
        foreach ($id in @('reloaded','reloaded-loaders','shared-hooks')) {
            $dependency = $report.dependencies | Where-Object id -eq $id
            $dependency.severity | Should Be 'required'
            $dependency.satisfied | Should Be $true
            $dependency.message | Should Match 'setup will install'
        }
    }

    It 'keeps a fresh legacy-only game installable without Reloaded' {
        $env:BLIND_SWORDSMAN_PREFLIGHT_RUNTIME_MODE = 'legacy-only'
        $report = Invoke-FixturePreflight $fixture -DetectReloaded

        $report.canInstall | Should Be $true
        ($report.dependencies | Where-Object id -eq 'reloaded-loaders').name | Should Match 'x86'
        ($report.dependencies | Where-Object id -eq 'reloaded-loaders').message | Should Match 'setup will install'
        ($report.dependencies | Where-Object id -eq 'shared-hooks').message | Should Match 'setup will install'
    }

    It 'keeps a fresh native-only game installable without Reloaded' {
        $env:BLIND_SWORDSMAN_PREFLIGHT_RUNTIME_MODE = 'native-only'
        $report = Invoke-FixturePreflight $fixture -DetectReloaded

        $report.canInstall | Should Be $true
        ($report.dependencies | Where-Object id -eq 'reloaded-loaders').name | Should Match 'x64'
        ($report.dependencies | Where-Object id -eq 'reloaded-loaders').message | Should Match 'setup will install'
        ($report.dependencies | Where-Object id -eq 'shared-hooks').message | Should Match 'setup will install'
    }

    It 'marks missing shared hooks as setup-managed without changing files' {
        $config = Join-Path $fixture.ReloadedRoot 'Mods\reloaded.sharedlib.hooks\ModConfig.json'
        Remove-Item -LiteralPath $config
        $before = @(Get-ChildItem -LiteralPath $fixture.ReloadedRoot -Recurse -File).Count

        $report = Invoke-FixturePreflight $fixture

        $report.canInstall | Should Be $true
        ($report.dependencies | Where-Object id -eq 'shared-hooks').severity | Should Be 'required'
        ($report.dependencies | Where-Object id -eq 'shared-hooks').satisfied | Should Be $true
        ($report.dependencies | Where-Object id -eq 'shared-hooks').message | Should Match 'setup will install or repair'
        @(Get-ChildItem -LiteralPath $fixture.ReloadedRoot -Recurse -File).Count | Should Be $before
    }

    It 'rejects a loader with the wrong architecture' {
        New-TestPe -Path (Join-Path $fixture.ReloadedRoot '_asi_extract\ASILoader64.dll') -Machine 0x014C

        $report = Invoke-FixturePreflight $fixture

        $report.canInstall | Should Be $false
        ($report.dependencies | Where-Object id -eq 'reloaded-loaders').message | Should Match 'x64'
    }

    It 'returns an actionable blocking game result instead of losing the report' {
        $env:BLIND_SWORDSMAN_PREFLIGHT_FAIL_GAME = '1'

        $report = Invoke-FixturePreflight $fixture

        $report.canInstall | Should Be $false
        $report.game | Should Be $null
        ($report.dependencies | Where-Object id -eq 'game').message | Should Match 'No supported FFVII'
    }
}
