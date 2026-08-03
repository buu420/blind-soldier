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
    return [pscustomobject]@{
        Version = 'Steam2026'; SteamAppId = '3837340'; GameRoot = $root
        LegacyRuntime = [pscustomobject]@{ RuntimeId = 'ff7-steam-legacy-x86'; Architecture = 'x86'; RuntimeRoot = (Join-Path $root 'ff7\workingdir'); GameExe = (Join-Path $root 'ff7\workingdir\ff7_en.exe') }
        NativeRuntime = [pscustomobject]@{ RuntimeId = 'ff7-steam-2026-x64'; Architecture = 'x64'; RuntimeRoot = $root; GameExe = (Join-Path $root 'FFVII.exe') }
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
    param($Fixture)
    & $preflightPath -GameRoot $Fixture.GameRoot -ReloadedRoot $Fixture.ReloadedRoot `
        -ModulePath $Fixture.ModulePath -ResultPath $Fixture.ResultPath
    return [IO.File]::ReadAllText($Fixture.ResultPath) | ConvertFrom-Json
}

Describe 'Blind Swordsman installer preflight' {
    BeforeEach {
        $fixture = New-PreflightFixture
        $env:BLIND_SWORDSMAN_PREFLIGHT_GAME_ROOT = $fixture.GameRoot
        $env:BLIND_SWORDSMAN_PREFLIGHT_FAIL_GAME = '0'
    }

    AfterEach {
        Remove-Item Env:\BLIND_SWORDSMAN_PREFLIGHT_GAME_ROOT -ErrorAction SilentlyContinue
        Remove-Item Env:\BLIND_SWORDSMAN_PREFLIGHT_FAIL_GAME -ErrorAction SilentlyContinue
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

    It 'makes a missing shared hooks dependency blocking without changing files' {
        $config = Join-Path $fixture.ReloadedRoot 'Mods\reloaded.sharedlib.hooks\ModConfig.json'
        Remove-Item -LiteralPath $config
        $before = @(Get-ChildItem -LiteralPath $fixture.ReloadedRoot -Recurse -File).Count

        $report = Invoke-FixturePreflight $fixture

        $report.canInstall | Should Be $false
        ($report.dependencies | Where-Object id -eq 'shared-hooks').satisfied | Should Be $false
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
