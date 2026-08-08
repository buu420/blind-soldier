$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$builder = Join-Path $repoRoot 'Build-BlindSoldierAmmSources.ps1'

function New-FixtureFile {
    param([string] $Root, [string] $RelativePath, [string] $Content)
    $path = Join-Path $Root $RelativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force |
        Out-Null
    [IO.File]::WriteAllText($path, $Content)
}

Describe 'Blind Soldier Accessibility Mod Manager source staging' {
    It 'uses a sibling Version proxy for 2013 and keeps both runtimes isolated' {
        $temp = Join-Path ([IO.Path]::GetTempPath()) `
            ('blind-soldier-amm-sources-' + [Guid]::NewGuid().ToString('N'))
        $portable = Join-Path $temp 'portable'
        $output = Join-Path $temp 'output'
        try {
            foreach ($record in @(
                @('Blind-Soldier\Bootstrap\x86\broker.exe', 'x86 broker'),
                @('Blind-Soldier\Bootstrap\x64\broker.exe', 'x64 broker'),
                @('Blind-Soldier\Runtime\dotnet\x86\runtime.dll', 'x86 runtime'),
                @('Blind-Soldier\Runtime\dotnet\x64\runtime.dll', 'x64 runtime'),
                @('Blind-Soldier\Tools\cleanup.cmd', 'cleanup'),
                @('Blind-Soldier\Policy\policy.json', 'policy'),
                @('ff7_en.exe.local\version.dll', 'x86 version proxy'),
                @('ff7.exe.local\version.dll', 'x86 version proxy'),
                @('FFVII_LAUNCHER.exe', 'launcher'),
                @('Reloaded-II\Loader\X86\loader.dll', 'x86 loader'),
                @('Reloaded-II\Loader\X64\loader.dll', 'x64 loader'),
                @('Reloaded-II\Mods\ff7.accessibility.reloaded\ModConfig.json', '{"ModId":"ff7.accessibility.reloaded"}'),
                @('Reloaded-II\Mods\ff7.accessibility.reloaded\Assets\cue.wav', 'shared asset'),
                @('Reloaded-II\Mods\ff7.accessibility.reloaded\Configuration\config.json', '{}'),
                @('Reloaded-II\Mods\ff7.accessibility.reloaded\x86\mod.dll', 'x86 mod'),
                @('Reloaded-II\Mods\ff7.accessibility.reloaded\x64\mod.dll', 'x64 mod'),
                @('Reloaded-II\Mods\reloaded.sharedlib.hooks\ModConfig.json', '{"ModId":"reloaded.sharedlib.hooks","HasExports":true}'),
                @('Reloaded-II\Mods\reloaded.sharedlib.hooks\Preview.png', 'preview'),
                @('Reloaded-II\Mods\reloaded.sharedlib.hooks\x86\hooks.dll', 'x86 hooks'),
                @('Reloaded-II\Mods\reloaded.sharedlib.hooks\x64\hooks.dll', 'x64 hooks'),
                @('Reloaded-II\portable.txt', ''),
                @('Reloaded-II\Apps\.keep', ''),
                @('Reloaded-II\User\Mods\.keep', ''),
                @('Reloaded-II\User\Misc\.keep', ''),
                @('Reloaded-II\Plugins\.keep', ''),
                @('LICENSES\notice.txt', 'notice'),
                @('README-PORTABLE.txt', 'portable readme'),
                @('portable-manifest.json', '{}')
            )) {
                New-FixtureFile -Root $portable -RelativePath $record[0] `
                    -Content $record[1]
            }
            $readme = Join-Path $temp 'README.md'
            [IO.File]::WriteAllText($readme, '# Blind Soldier')

            & $builder -PortableRoot $portable -OutputRoot $output `
                -ReadmePath $readme

            $old = Join-Path $output 'ffviiold'
            $new = Join-Path $output 'ffviinew'
            (Get-Content -LiteralPath (Join-Path $old 'version.dll') -Raw) |
                Should Be 'x86 version proxy'
            Test-Path -LiteralPath (Join-Path $old `
                'ff7_en.exe.local\version.dll') | Should Be $false
            Test-Path -LiteralPath (Join-Path $old `
                'Blind-Soldier\Bootstrap\x86\broker.exe') | Should Be $true
            Test-Path -LiteralPath (Join-Path $old `
                'Blind-Soldier\Bootstrap\x64\broker.exe') | Should Be $false
            Test-Path -LiteralPath (Join-Path $old `
                'Reloaded-II\Loader\X86\loader.dll') | Should Be $true
            Test-Path -LiteralPath (Join-Path $old `
                'Reloaded-II\Loader\X64\loader.dll') | Should Be $false
            Test-Path -LiteralPath (Join-Path $old `
                'Reloaded-II\Mods\reloaded.sharedlib.hooks\x86\hooks.dll') |
                Should Be $true
            Test-Path -LiteralPath (Join-Path $old `
                'Reloaded-II\Mods\reloaded.sharedlib.hooks\x64\hooks.dll') |
                Should Be $false
            Test-Path -LiteralPath (Join-Path $old 'FFVII_LAUNCHER.exe') |
                Should Be $false
            (Get-Content -LiteralPath (Join-Path $old `
                'README-Blind-Soldier.md') -Raw) | Should Be '# Blind Soldier'

            Test-Path -LiteralPath (Join-Path $new 'FFVII_LAUNCHER.exe') |
                Should Be $true
            Test-Path -LiteralPath (Join-Path $new `
                'Reloaded-II\Loader\X64\loader.dll') | Should Be $true
            (Get-Content -LiteralPath (Join-Path $new `
                'README-Blind-Soldier.md') -Raw) | Should Be '# Blind Soldier'
        }
        finally {
            if (Test-Path -LiteralPath $temp) {
                Remove-Item -LiteralPath $temp -Recurse -Force
            }
        }
    }
}
