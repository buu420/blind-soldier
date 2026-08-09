$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$builderPath = Join-Path $scriptRoot 'Build-BlindSoldier2013PortablePackage.ps1'
$verifierPath = Join-Path $scriptRoot 'Verify-BlindSoldier2013PortablePackage.ps1'
$testVersion = '0.2.1-beta.1'

function Write-TestFile {
    param([string] $Root, [string] $Relative, [string] $Content = 'fixture')
    $path = Join-Path $Root $Relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force |
        Out-Null
    [IO.File]::WriteAllText($path, $Content, (New-Object Text.UTF8Encoding($false)))
}

function Write-TestPe {
    param([string] $Root, [string] $Relative, [uint16] $Machine)
    $path = Join-Path $Root $Relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force |
        Out-Null
    $bytes = New-Object byte[] 512
    $bytes[0] = 0x4D
    $bytes[1] = 0x5A
    [BitConverter]::GetBytes([int]0x80).CopyTo($bytes, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 0x80)
    [BitConverter]::GetBytes($Machine).CopyTo($bytes, 0x84)
    [BitConverter]::GetBytes([uint16]0x00E0).CopyTo($bytes, 0x94)
    [BitConverter]::GetBytes([uint16]0x010B).CopyTo($bytes, 0x98)
    [IO.File]::WriteAllBytes($path, $bytes)
}

function New-DualFixtureArchive {
    param([string] $TemporaryRoot)

    $root = Join-Path $TemporaryRoot 'dual-root'
    New-Item -ItemType Directory -Path $root -Force | Out-Null

    foreach ($relative in @(
        'ff7_en.exe.local\version.dll',
        'ff7.exe.local\version.dll',
        'workingdir\version.dll',
        'workingdir\ff7_en.exe.local\version.dll',
        'workingdir\ff7.exe.local\version.dll',
        'ff7\workingdir\version.dll',
        'ff7\workingdir\ff7_en.exe.local\version.dll',
        'ff7\workingdir\ff7.exe.local\version.dll'
    )) {
        Write-TestPe -Root $root -Relative $relative -Machine 0x014C
    }

    foreach ($relative in @(
        'Blind-Soldier\Bootstrap\x86\Blind-Soldier-Bootstrap-x86.exe',
        'Blind-Soldier\Runtime\dotnet\x86\host\fxr\9.0.8\hostfxr.dll',
        'Blind-Soldier\Runtime\dotnet\x86\shared\Microsoft.NETCore.App\9.0.8\coreclr.dll',
        'Blind-Soldier\Runtime\dotnet\x86\shared\Microsoft.WindowsDesktop.App\9.0.8\PresentationFramework.dll',
        'Reloaded-II\Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll',
        'Reloaded-II\Loader\X86\Reloaded.Mod.Loader.dll',
        'Reloaded-II\Mods\ff7.accessibility.reloaded\x86\Ff7.Accessibility.Reloaded.dll',
        'Reloaded-II\Mods\ff7.accessibility.reloaded\x86\prism.dll',
        'Reloaded-II\Mods\reloaded.sharedlib.hooks\x86\Reloaded.Hooks.ReloadedII.dll'
    )) {
        Write-TestPe -Root $root -Relative $relative -Machine 0x014C
    }

    foreach ($relative in @(
        'Blind-Soldier\Bootstrap\x64\Blind-Soldier-Bootstrap-x64.exe',
        'Blind-Soldier\Runtime\dotnet\x64\host\fxr\9.0.8\hostfxr.dll',
        'Reloaded-II\Loader\X64\Reloaded.Mod.Loader.dll',
        'Reloaded-II\Mods\ff7.accessibility.reloaded\x64\Ff7.Accessibility.Steam2026X64.dll',
        'Reloaded-II\Mods\reloaded.sharedlib.hooks\x64\Reloaded.Hooks.ReloadedII.dll'
    )) {
        Write-TestPe -Root $root -Relative $relative -Machine 0x8664
    }

    foreach ($relative in @(
        'Blind-Soldier\Policy\BlindSoldier.ExternalOwnership.json',
        'Blind-Soldier\Policy\BlindSoldier.ExternalOwnership.psm1',
        'Blind-Soldier\Tools\Remove-AmethystRegistryEntries-Automatic.cmd',
        'Blind-Soldier\Tools\Remove-AmethystRegistryEntries.ps1',
        'Reloaded-II\Loader\X86\Reloaded.Mod.Loader.runtimeconfig.json',
        'Reloaded-II\Mods\ff7.accessibility.reloaded\Assets\navigation\cue.wav',
        'Reloaded-II\Mods\ff7.accessibility.reloaded\Configuration\config.json',
        'Reloaded-II\Mods\reloaded.sharedlib.hooks\Preview.png',
        'Reloaded-II\Apps\.keep',
        'Reloaded-II\User\Mods\.keep',
        'Reloaded-II\User\Misc\.keep',
        'Reloaded-II\Plugins\.keep',
        'Reloaded-II\portable.txt',
        'LICENSES\THIRD-PARTY-NOTICES.md',
        'LICENSES\dotnet-LICENSE.txt',
        'LICENSES\dotnet-THIRD-PARTY-NOTICES.txt',
        'LICENSES\Reloaded-II-GPL-3.0.txt',
        'LICENSES\Reloaded-Shared-Hooks-LGPL-3.0.txt',
        'LICENSES\Reloaded-II-1.30.3-Blind-Soldier-source.md',
        'LICENSES\Reloaded-II-1.30.3-hostfxr.patch',
        'LICENSES\FF7Tools-text-table-notice.md',
        'Remove-Amethyst-Registry-Entries.cmd',
        'FFVII_LAUNCHER.exe',
        'FFVII_LAUNCHER.exe.config',
        'launcher_accessibility\native\x86\FFVII_LAUNCHER.prism.x86.dll',
        'README-PORTABLE.txt'
    )) {
        Write-TestFile -Root $root -Relative $relative
    }

    Write-TestFile -Root $root `
        -Relative 'Reloaded-II\Mods\ff7.accessibility.reloaded\ModConfig.json' `
        -Content ('{"ModId":"ff7.accessibility.reloaded","ModVersion":"' +
            $testVersion + '","ModR2RManagedDll32":"x86/Ff7.Accessibility.Reloaded.dll",' +
            '"ModR2RManagedDll64":"x64/Ff7.Accessibility.Steam2026X64.dll",' +
            '"SupportedAppId":["ff7_en.exe","ff7.exe","FFVII.exe"]}')
    Write-TestFile -Root $root `
        -Relative 'Reloaded-II\Mods\reloaded.sharedlib.hooks\ModConfig.json' `
        -Content '{"ModId":"reloaded.sharedlib.hooks"}'

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = Join-Path $TemporaryRoot 'dual.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($root, $archive,
        [IO.Compression.CompressionLevel]::Optimal, $false)
    return $archive
}

function Get-ZipNames {
    param([string] $ArchivePath)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try { return @($archive.Entries | ForEach-Object FullName) }
    finally { $archive.Dispose() }
}

Describe 'Blind Soldier 2013 x86 portable package' {
    It 'keeps the complete x86 runtime and excludes 2026 and x64 files' {
        $temp = Join-Path ([IO.Path]::GetTempPath()) `
            ('blind-soldier-2013-package-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $temp | Out-Null
        try {
            $source = New-DualFixtureArchive -TemporaryRoot $temp
            $output = Join-Path $temp 'Blind-Soldier-2013-x86-Portable.zip'
            $sourceVerifier = {
                param($ArchivePath, $ExpectedVersion)
                [pscustomobject]@{Version=$ExpectedVersion;ArchivePath=$ArchivePath}
            }

            & $builderPath -SourceArchivePath $source -OutputPath $output `
                -Version $testVersion -SourceVerifier $sourceVerifier | Out-Null

            Test-Path -LiteralPath $output -PathType Leaf | Should Be $true
            Test-Path -LiteralPath ($output + '.sha256') -PathType Leaf |
                Should Be $true
            $names = @(Get-ZipNames -ArchivePath $output)
            foreach ($required in @(
                'version.dll',
                'ff7_en.exe.local/version.dll',
                'ff7.exe.local/version.dll',
                'workingdir/version.dll',
                'workingdir/ff7_en.exe.local/version.dll',
                'workingdir/ff7.exe.local/version.dll',
                'Blind-Soldier/Bootstrap/x86/Blind-Soldier-Bootstrap-x86.exe',
                'Blind-Soldier/Runtime/dotnet/x86/host/fxr/9.0.8/hostfxr.dll',
                'Reloaded-II/Loader/X86/Reloaded.Mod.Loader.dll',
                'Reloaded-II/Mods/ff7.accessibility.reloaded/Assets/navigation/cue.wav',
                'Reloaded-II/Mods/ff7.accessibility.reloaded/x86/Ff7.Accessibility.Reloaded.dll',
                'Reloaded-II/Mods/reloaded.sharedlib.hooks/x86/Reloaded.Hooks.ReloadedII.dll',
                'README-2013-PORTABLE.txt',
                'portable-manifest.json'
            )) {
                $names | Should Contain $required
            }
            foreach ($forbidden in @(
                'FFVII_LAUNCHER.exe',
                'launcher_accessibility/native/x86/FFVII_LAUNCHER.prism.x86.dll',
                'Blind-Soldier/Bootstrap/x64/Blind-Soldier-Bootstrap-x64.exe',
                'Blind-Soldier/Runtime/dotnet/x64/host/fxr/9.0.8/hostfxr.dll',
                'Reloaded-II/Loader/X64/Reloaded.Mod.Loader.dll',
                'Reloaded-II/Mods/ff7.accessibility.reloaded/x64/Ff7.Accessibility.Steam2026X64.dll',
                'ff7/workingdir/version.dll'
            )) {
                $names | Should Not Contain $forbidden
            }

            $result = & $verifierPath -ArchivePath $output `
                -ExpectedVersion $testVersion -ExpectedSourceArchivePath $source
            $result.Profile | Should Be 'legacy-x86'
            $result.Version | Should Be $testVersion
        }
        finally {
            if (Test-Path -LiteralPath $temp) {
                Remove-Item -LiteralPath $temp -Recurse -Force
            }
        }
    }
}
