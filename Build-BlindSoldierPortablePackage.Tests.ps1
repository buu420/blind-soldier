$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$builderPath = Join-Path $scriptRoot 'Build-BlindSoldierPortablePackage.ps1'
$loaderFiles = @(
    'Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll',
    'Colorful.Console.dll',
    'DelayInjectHooks.json',
    'Indieteur.SAMAPI.dll',
    'Indieteur.VDFAPI.dll',
    'McMaster.NETCore.Plugins.dll',
    'Reloaded.Memory.dll',
    'Reloaded.Mod.Interfaces.dll',
    'Reloaded.Mod.Loader.deps.json',
    'Reloaded.Mod.Loader.dll',
    'Reloaded.Mod.Loader.IO.dll',
    'Reloaded.Mod.Loader.runtimeconfig.json'
)

function New-PortableTestPe {
    param([string] $Path, [uint16] $Machine)
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $bytes = New-Object byte[] 512
    $bytes[0] = 0x4D; $bytes[1] = 0x5A
    [BitConverter]::GetBytes([int]0x80).CopyTo($bytes, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 0x80)
    [BitConverter]::GetBytes($Machine).CopyTo($bytes, 0x84)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function New-PortableFixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('blind-soldier-portable-test-' + [Guid]::NewGuid().ToString('N'))
    $prerequisites = Join-Path $root 'prerequisites'
    $mod = Join-Path $root 'ff7.accessibility.reloaded'
    $launcher = Join-Path $root 'launcher'
    $native = Join-Path $root 'native'
    New-Item -ItemType Directory -Path $prerequisites, $mod, $launcher, $native -Force | Out-Null

    $reloaded = Join-Path $prerequisites 'reloaded'
    foreach ($architecture in @('X86','X64')) {
        foreach ($relative in $loaderFiles) {
            $path = Join-Path $reloaded "Loader\$architecture\$relative"
            New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
            [IO.File]::WriteAllText($path, "fixture $architecture $relative")
        }
    }
    New-PortableTestPe -Path (Join-Path $reloaded 'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Machine 0x014C
    New-PortableTestPe -Path (Join-Path $reloaded 'Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Machine 0x8664
    New-PortableTestPe -Path (Join-Path $reloaded '_asi_extract\ASILoader32.dll') -Machine 0x014C
    New-PortableTestPe -Path (Join-Path $reloaded '_asi_extract\ASILoader64.dll') -Machine 0x8664
    [IO.File]::WriteAllText((Join-Path $reloaded 'Reloaded-II.exe'), 'must not ship')

    $hooks = Join-Path $prerequisites 'shared-hooks'
    New-Item -ItemType Directory -Path (Join-Path $hooks 'x86'), (Join-Path $hooks 'x64') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $hooks 'ModConfig.json'), '{"ModId":"reloaded.sharedlib.hooks","ModVersion":"1.16.3"}')
    New-PortableTestPe -Path (Join-Path $hooks 'x86\Reloaded.Hooks.ReloadedII.dll') -Machine 0x014C
    New-PortableTestPe -Path (Join-Path $hooks 'x64\Reloaded.Hooks.ReloadedII.dll') -Machine 0x8664
    [IO.File]::WriteAllText((Join-Path $hooks 'Preview.png'), 'fixture preview')

    $notices = Join-Path $prerequisites 'notices'
    New-Item -ItemType Directory -Path $notices -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $notices 'THIRD-PARTY-NOTICES.md'), 'fixture notices')
    [IO.File]::WriteAllText((Join-Path $notices 'Reloaded-II-GPL-3.0.txt'), 'fixture Reloaded license')
    [IO.File]::WriteAllText((Join-Path $notices 'Reloaded-Shared-Hooks-LGPL-3.0.txt'), 'fixture hooks license')
    [IO.File]::WriteAllText((Join-Path $prerequisites 'dependency-bundle.json'), '{"schemaVersion":1,"reloaded":{"version":"1.30.3"},"sharedHooks":{"version":"1.16.3"},"dotnetDesktopRuntime":{"version":"9.0.8"}}')

    [IO.File]::WriteAllText((Join-Path $mod 'ModConfig.json'), '{"ModId":"ff7.accessibility.reloaded","ModR2RManagedDll32":"x86/Ff7.Accessibility.Reloaded.dll","ModR2RManagedDll64":"x64/Ff7.Accessibility.Steam2026X64.dll","ModDependencies":["reloaded.sharedlib.hooks"],"SupportedAppId":["ff7_en.exe","FFVII.exe"]}')
    New-PortableTestPe -Path (Join-Path $mod 'x86\Ff7.Accessibility.Reloaded.dll') -Machine 0x014C
    New-PortableTestPe -Path (Join-Path $mod 'x64\Ff7.Accessibility.Steam2026X64.dll') -Machine 0x8664
    New-PortableTestPe -Path (Join-Path $mod 'x86\prism.dll') -Machine 0x014C
    New-PortableTestPe -Path (Join-Path $mod 'x64\prism.dll') -Machine 0x8664
    [IO.File]::WriteAllText((Join-Path $mod 'x86\Ff7.Accessibility.Reloaded.pdb'), 'must not ship')
    [IO.File]::WriteAllText((Join-Path $mod 'x64\Ff7.Accessibility.Steam2026X64.pdb'), 'must not ship')
    New-Item -ItemType Directory -Path (Join-Path $mod 'Assets\movies') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $mod 'Assets\movies\opening_audio_description.ogg'), 'fixture narration')

    New-PortableTestPe -Path (Join-Path $launcher 'FFVII_LAUNCHER.exe') -Machine 0x014C
    [IO.File]::WriteAllText((Join-Path $launcher 'FFVII_LAUNCHER.exe.config'), 'fixture config')
    New-PortableTestPe -Path (Join-Path $launcher 'FFVII_LAUNCHER.prism.x86.dll') -Machine 0x014C

    New-PortableTestPe -Path (Join-Path $native 'Blind-Soldier-Installer.exe') -Machine 0x8664
    New-PortableTestPe -Path (Join-Path $native 'Blind-Soldier-Launcher-x86.exe') -Machine 0x014C
    New-PortableTestPe -Path (Join-Path $native 'Blind-Soldier-Launcher-x64.exe') -Machine 0x8664

    [pscustomobject]@{
        Root = $root
        Prerequisites = $prerequisites
        Mod = $mod
        Launcher = $launcher
        Native = $native
        First = Join-Path $root 'Blind-Soldier-Portable-1.zip'
        Second = Join-Path $root 'Blind-Soldier-Portable-2.zip'
    }
}

function Get-PortableEntries {
    param([string] $Path)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try { return @($archive.Entries | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Name) } | ForEach-Object FullName) }
    finally { $archive.Dispose() }
}

function Get-PortableEntryText {
    param([string] $Path, [string] $EntryPath)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.GetEntry($EntryPath)
        if ($null -eq $entry) { throw "Archive entry is missing: $EntryPath" }
        $reader = New-Object IO.StreamReader($entry.Open())
        try { return $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    finally { $archive.Dispose() }
}

Describe 'Blind Soldier direct-extract portable package' {
    AfterEach {
        if ($null -ne $fixture -and (Test-Path -LiteralPath $fixture.Root)) {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
        $fixture = $null
    }

    It 'ships the preserved native installer workflow and complete accessibility payload' {
        $fixture = New-PortableFixture
        & $builderPath -OutputPath $fixture.First -Version '0.1.0-pre.7' `
            -PrerequisiteBundlePath $fixture.Prerequisites -ModPackagePath $fixture.Mod `
            -LauncherBundlePath $fixture.Launcher -NativeBinaryPath $fixture.Native | Out-Null

        $entries = @(Get-PortableEntries -Path $fixture.First)
        foreach ($required in @(
            'Blind-Soldier-Installer.exe',
            'Blind-Soldier-Launcher-x86.exe',
            'Blind-Soldier-Launcher-x64.exe',
            'FFVII_LAUNCHER.exe',
            'FFVII_LAUNCHER.exe.config',
            'launcher_accessibility/native/x86/FFVII_LAUNCHER.prism.x86.dll',
            'Reloaded-II/Mods/ff7.accessibility.reloaded/ModConfig.json',
            'Reloaded-II/Mods/ff7.accessibility.reloaded/x86/Ff7.Accessibility.Reloaded.dll',
            'Reloaded-II/Mods/ff7.accessibility.reloaded/x64/Ff7.Accessibility.Steam2026X64.dll',
            'Reloaded-II/Mods/reloaded.sharedlib.hooks/ModConfig.json',
            'LICENSES/Reloaded-II-GPL-3.0.txt',
            'LICENSES/Reloaded-Shared-Hooks-LGPL-3.0.txt',
            'README-PORTABLE.txt',
            'portable-manifest.json'
        )) { ($entries -ccontains $required) | Should Be $true }

        $loaderEntries = @($entries | Where-Object { $_ -like 'Reloaded-II/Loader/*' } | Sort-Object)
        $expected = New-Object 'System.Collections.Generic.List[string]'
        foreach ($architecture in @('X86','X64')) {
            foreach ($relative in $loaderFiles) {
                $expected.Add(('Reloaded-II/Loader/{0}/{1}' -f $architecture, $relative.Replace('\','/')))
            }
        }
        (($loaderEntries | Sort-Object) -join '|') | Should Be (($expected.ToArray() | Sort-Object) -join '|')
        ($entries -contains 'Reloaded-II/Reloaded-II.exe') | Should Be $false
        @($entries | Where-Object { $_ -like '*ASILoader*' }).Count | Should Be 0
        @($entries | Where-Object { $_ -match '\.(pdb|obj|iobj|ipdb)$' }).Count | Should Be 0
    }

    It 'is deterministic for identical inputs' {
        $fixture = New-PortableFixture
        foreach ($output in @($fixture.First, $fixture.Second)) {
            & $builderPath -OutputPath $output -Version '0.1.0-pre.7' `
                -PrerequisiteBundlePath $fixture.Prerequisites -ModPackagePath $fixture.Mod `
                -LauncherBundlePath $fixture.Launcher -NativeBinaryPath $fixture.Native | Out-Null
        }
        (Get-FileHash -LiteralPath $fixture.First -Algorithm SHA256).Hash |
            Should Be (Get-FileHash -LiteralPath $fixture.Second -Algorithm SHA256).Hash
    }

    It 'uses one deterministic x64 graphics initialization hook without changing x86' {
        $fixture = New-PortableFixture
        & $builderPath -OutputPath $fixture.First -Version '0.1.0-pre.7' `
            -PrerequisiteBundlePath $fixture.Prerequisites -ModPackagePath $fixture.Mod `
            -LauncherBundlePath $fixture.Launcher -NativeBinaryPath $fixture.Native | Out-Null

        $x64 = Get-PortableEntryText -Path $fixture.First `
            -EntryPath 'Reloaded-II/Loader/X64/DelayInjectHooks.json' | ConvertFrom-Json
        @($x64).Count | Should Be 1
        [string]$x64[0].Name | Should Be 'd3d11'
        @($x64[0].Functions).Count | Should Be 1
        [string]$x64[0].Functions[0] | Should Be 'D3DKMTWaitForVerticalBlankEvent'

        Get-PortableEntryText -Path $fixture.First `
            -EntryPath 'Reloaded-II/Loader/X86/DelayInjectHooks.json' |
            Should Be 'fixture X86 DelayInjectHooks.json'
    }
}
