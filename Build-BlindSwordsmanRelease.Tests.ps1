$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$builderPath = Join-Path $scriptRoot 'Build-BlindSwordsmanRelease.ps1'

function New-ReleaseFixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('blind-swordsman-release-test-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root | Out-Null
    return [pscustomobject]@{
        Root = $root
        First = Join-Path $root 'first'
        Second = Join-Path $root 'second'
    }
}

function New-FakePe {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [uint16] $Machine = 0x8664
    )
    $parent = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $bytes = New-Object byte[] 512
    $bytes[0] = 0x4D
    $bytes[1] = 0x5A
    [BitConverter]::GetBytes([int]0x80).CopyTo($bytes, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 0x80)
    [BitConverter]::GetBytes($Machine).CopyTo($bytes, 0x84)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function New-FakePrerequisiteBundle {
    param([Parameter(Mandatory=$true)] [string] $BundlePath)

    New-Item -ItemType Directory -Path $BundlePath -Force | Out-Null
    $reloaded = Join-Path $BundlePath 'reloaded'
    $hooks = Join-Path $BundlePath 'shared-hooks'
    $dotnet = Join-Path $BundlePath 'dotnet'
    $notices = Join-Path $BundlePath 'notices'

    New-FakePe -Path (Join-Path $reloaded 'Reloaded-II.exe')
    New-FakePe -Path (Join-Path $reloaded '_asi_extract\ASILoader32.dll') -Machine 0x014C
    New-FakePe -Path (Join-Path $reloaded '_asi_extract\ASILoader64.dll') -Machine 0x8664
    New-FakePe -Path (Join-Path $reloaded 'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Machine 0x014C
    New-FakePe -Path (Join-Path $reloaded 'Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Machine 0x8664
    New-Item -ItemType Directory -Path (Join-Path $reloaded 'Loader\X86'), (Join-Path $reloaded 'Loader\X64') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $reloaded 'Loader\X86\Reloaded.Mod.Loader.dll'), 'x86 loader')
    [IO.File]::WriteAllText((Join-Path $reloaded 'Loader\X64\Reloaded.Mod.Loader.dll'), 'x64 loader')

    New-Item -ItemType Directory -Path (Join-Path $hooks 'x86'), (Join-Path $hooks 'x64') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $hooks 'ModConfig.json'), '{"ModId":"reloaded.sharedlib.hooks","ModVersion":"1.16.3"}')
    New-FakePe -Path (Join-Path $hooks 'x86\Reloaded.Hooks.ReloadedII.dll') -Machine 0x014C
    New-FakePe -Path (Join-Path $hooks 'x64\Reloaded.Hooks.ReloadedII.dll') -Machine 0x8664

    New-FakePe -Path (Join-Path $dotnet 'windowsdesktop-runtime-9.0.8-win-x86.exe') -Machine 0x014C
    New-FakePe -Path (Join-Path $dotnet 'windowsdesktop-runtime-9.0.8-win-x64.exe') -Machine 0x8664
    New-Item -ItemType Directory -Path $notices -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $notices 'THIRD-PARTY-NOTICES.md'), 'fixture notices')
    [IO.File]::WriteAllText((Join-Path $notices 'Reloaded-II-GPL-3.0.txt'), 'fixture GPL')
    [IO.File]::WriteAllText((Join-Path $notices 'Reloaded-Shared-Hooks-LGPL-3.0.txt'), 'fixture LGPL')

    $manifest = [ordered]@{
        schemaVersion = 1
        reloaded = [ordered]@{
            version = '1.30.3'
            sourceUrl = 'https://github.com/Reloaded-Project/Reloaded-II/releases/download/1.30.3/Release.zip'
            sourceSize = 24996341
            sourceSha256 = '1DD59C2C4C609E4EC1CA3EFF851F083A0E15E046EF84D58081230F6DD7A159DE'
        }
        sharedHooks = [ordered]@{
            version = '1.16.3'
            sourceUrl = 'https://github.com/Sewer56/Reloaded.SharedLib.Hooks.ReloadedII/releases/download/1.16.3/Reloaded.Hooks.ReloadedII1.16.3.7z'
            sourceSize = 741775
            sourceSha256 = '2B7C2E6118A3F1EB00A2E1E9105397B0D17A118A84596308C3A6A9FF3CB14B1B'
        }
        dotnetDesktopRuntime = [ordered]@{
            version = '9.0.8'
            installers = @(
                [ordered]@{ architecture = 'x86'; name = 'windowsdesktop-runtime-9.0.8-win-x86.exe'; sourceUrl = 'https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/9.0.8/windowsdesktop-runtime-9.0.8-win-x86.exe'; sourceSize = 1; sourceSha512 = ('A' * 128) },
                [ordered]@{ architecture = 'x64'; name = 'windowsdesktop-runtime-9.0.8-win-x64.exe'; sourceUrl = 'https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/9.0.8/windowsdesktop-runtime-9.0.8-win-x64.exe'; sourceSize = 1; sourceSha512 = ('B' * 128) }
            )
        }
    }
    [IO.File]::WriteAllText(
        (Join-Path $BundlePath 'dependency-bundle.json'),
        ($manifest | ConvertTo-Json -Depth 8),
        (New-Object Text.UTF8Encoding($false)))
}

function New-FakeRuntimePackage {
    param([Parameter(Mandatory=$true)] [string] $PackagePath)
    New-Item -ItemType Directory -Path (Join-Path $PackagePath 'Assets') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $PackagePath 'ModConfig.json'), '{"ModId":"ff7.accessibility.reloaded"}')
    [IO.File]::WriteAllText((Join-Path $PackagePath 'Assets\readme.txt'), 'deterministic payload')
}

$packageBuilder = { param($PackagePath) New-FakeRuntimePackage -PackagePath $PackagePath }
$prerequisiteBundleBuilder = { param($BundlePath) New-FakePrerequisiteBundle -BundlePath $BundlePath }
$setupPublisher = { param($Destination) New-FakePe -Path (Join-Path $Destination 'Blind-Swordsman-Setup.exe') }
$artifactValidator = { param($ManifestPath, $PayloadPath, $SetupPath, $Track) }

Describe 'Blind Swordsman release builder' {
    BeforeEach {
        $fixture = New-ReleaseFixture
    }

    AfterEach {
        if (Test-Path -LiteralPath $fixture.Root) {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
    }

    It 'produces exact prerelease assets with mutually consistent hashes' {
        & $builderPath -Version '0.1.0-pre.1' -Tag 'v0.1.0-pre.1' -OutputPath $fixture.First `
            -PackageBuilder $packageBuilder -PrerequisiteBundleBuilder $prerequisiteBundleBuilder `
            -SetupPublisher $setupPublisher -ArtifactValidator $artifactValidator | Out-Null

        $names = @(Get-ChildItem -LiteralPath $fixture.First -File | Sort-Object Name | ForEach-Object Name)
        $names | Should Be @(
            'Blind-Swordsman-Runtime.zip',
            'Blind-Swordsman-Runtime.zip.sha256',
            'Blind-Swordsman-Setup.exe',
            'Blind-Swordsman-Setup.exe.sha256',
            'blind-swordsman-channel.json'
        )
        $manifest = [IO.File]::ReadAllText((Join-Path $fixture.First 'blind-swordsman-channel.json')) | ConvertFrom-Json
        $manifest.version | Should Be '0.1.0-pre.1'
        $manifest.releaseTag | Should Be 'v0.1.0-pre.1'
        $manifest.track | Should Be 'prerelease'
        $manifest.payload.size | Should Be (Get-Item (Join-Path $fixture.First $manifest.payload.name)).Length
        $manifest.setup.size | Should Be (Get-Item (Join-Path $fixture.First $manifest.setup.name)).Length
        $manifest.payload.sha256 | Should Be (Get-FileHash (Join-Path $fixture.First $manifest.payload.name) -Algorithm SHA256).Hash
        $manifest.setup.sha256 | Should Be (Get-FileHash (Join-Path $fixture.First $manifest.setup.name) -Algorithm SHA256).Hash
        [IO.File]::ReadAllText((Join-Path $fixture.First 'Blind-Swordsman-Setup.exe.sha256')) | Should Match "^$($manifest.setup.sha256)  Blind-Swordsman-Setup.exe`r?`n?$"
    }

    It 'creates an ordinally sorted payload manifest and deterministic archive' {
        & $builderPath -Version '0.1.0-pre.1' -Tag 'v0.1.0-pre.1' -OutputPath $fixture.First `
            -PackageBuilder $packageBuilder -PrerequisiteBundleBuilder $prerequisiteBundleBuilder `
            -SetupPublisher $setupPublisher -ArtifactValidator $artifactValidator | Out-Null
        & $builderPath -Version '0.1.0-pre.1' -Tag 'v0.1.0-pre.1' -OutputPath $fixture.Second `
            -PackageBuilder $packageBuilder -PrerequisiteBundleBuilder $prerequisiteBundleBuilder `
            -SetupPublisher $setupPublisher -ArtifactValidator $artifactValidator | Out-Null

        (Get-FileHash (Join-Path $fixture.First 'Blind-Swordsman-Runtime.zip') -Algorithm SHA256).Hash |
            Should Be (Get-FileHash (Join-Path $fixture.Second 'Blind-Swordsman-Runtime.zip') -Algorithm SHA256).Hash

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $fixture.First 'Blind-Swordsman-Runtime.zip'))
        try {
            $entryNames = @($archive.Entries | ForEach-Object FullName)
            foreach ($launcherEntry in @(
                'launcher/FFVII_LAUNCHER.exe',
                'launcher/FFVII_LAUNCHER.exe.config',
                'launcher/launcher-bundle.json',
                'launcher/native/x86/FFVII_LAUNCHER.prism.x86.dll'
            )) {
                ($entryNames -contains $launcherEntry) | Should Be $true
            }
            foreach ($prerequisiteEntry in @(
                'prerequisites/dependency-bundle.json',
                'prerequisites/reloaded/Reloaded-II.exe',
                'prerequisites/reloaded/_asi_extract/ASILoader32.dll',
                'prerequisites/reloaded/_asi_extract/ASILoader64.dll',
                'prerequisites/reloaded/Loader/X86/Bootstrapper/Reloaded.Mod.Loader.Bootstrapper.dll',
                'prerequisites/reloaded/Loader/X64/Bootstrapper/Reloaded.Mod.Loader.Bootstrapper.dll',
                'prerequisites/shared-hooks/ModConfig.json',
                'prerequisites/shared-hooks/x86/Reloaded.Hooks.ReloadedII.dll',
                'prerequisites/shared-hooks/x64/Reloaded.Hooks.ReloadedII.dll',
                'prerequisites/dotnet/windowsdesktop-runtime-9.0.8-win-x86.exe',
                'prerequisites/dotnet/windowsdesktop-runtime-9.0.8-win-x64.exe',
                'prerequisites/notices/THIRD-PARTY-NOTICES.md'
            )) {
                ($entryNames -contains $prerequisiteEntry) | Should Be $true
            }
            $entry = $archive.GetEntry('payload-manifest.json')
            $reader = New-Object IO.StreamReader($entry.Open())
            try { $payloadManifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
            $paths = @($payloadManifest.files | ForEach-Object { [string]$_.path })
            [string[]] $sorted = @($paths)
            [Array]::Sort($sorted, [StringComparer]::Ordinal)
            ($paths -join '|') | Should Be ($sorted -join '|')
            ($paths -contains 'package/ff7.accessibility.reloaded/ModConfig.json') | Should Be $true
        }
        finally {
            $archive.Dispose()
        }
    }

    It 'rejects mismatched tags and cleans staging after failure' {
        { & $builderPath -Version '0.1.0-pre.1' -Tag 'v0.1.0-pre.2' -OutputPath $fixture.First `
                -PackageBuilder $packageBuilder -PrerequisiteBundleBuilder $prerequisiteBundleBuilder `
                -SetupPublisher $setupPublisher -ArtifactValidator $artifactValidator } |
            Should Throw

        $failingBuilder = { param($PackagePath) throw 'fixture package failure' }
        { & $builderPath -Version '0.1.0-pre.1' -Tag 'v0.1.0-pre.1' -OutputPath $fixture.First `
                -PackageBuilder $failingBuilder -PrerequisiteBundleBuilder $prerequisiteBundleBuilder `
                -SetupPublisher $setupPublisher -ArtifactValidator $artifactValidator } |
            Should Throw
        Test-Path -LiteralPath $fixture.First | Should Be $false
        @(Get-ChildItem -LiteralPath $fixture.Root -Force -Filter '.first.staging-*').Count | Should Be 0
    }
}
