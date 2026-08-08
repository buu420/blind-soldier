if ($MyInvocation.InvocationName -ne '&') {
    # Prerequisite: Windows PowerShell with Pester 4.10.1 installed; no fallback.
    # Pester invokes test scripts with &, while direct -File invocation owns
    # the process exit code and reports the pinned Pester result.
    Import-Module Pester -RequiredVersion 4.10.1 -Force -ErrorAction Stop
    $result = Invoke-Pester -Script $MyInvocation.MyCommand.Path -PassThru
    if ($result.FailedCount -gt 0) {
        exit 1
    }
    exit 0
}
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$builderPath = Join-Path $scriptRoot 'Build-BlindSoldierPortablePackage.ps1'
$verifierPath = Join-Path $scriptRoot 'Verify-BlindSoldierPortablePackage.ps1'
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
    param([string] $Path, [uint16] $Machine, [byte] $Marker = 0)
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $bytes = New-Object byte[] 512
    $bytes[0] = 0x4D; $bytes[1] = 0x5A; $bytes[2] = $Marker
    [BitConverter]::GetBytes([int]0x80).CopyTo($bytes, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 0x80)
    [BitConverter]::GetBytes($Machine).CopyTo($bytes, 0x84)
    $magic = if ($Machine -eq 0x014C) { [uint16]0x10B } else { [uint16]0x20B }
    [BitConverter]::GetBytes($magic).CopyTo($bytes, 0x98)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function New-FixtureZip {
    param([string] $Source, [string] $Destination)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew)
    try {
        $zip = New-Object IO.Compression.ZipArchive(
            $stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            [string[]]$paths = @(Get-ChildItem -LiteralPath $Source -File -Recurse |
                ForEach-Object FullName)
            [Array]::Sort($paths, [StringComparer]::Ordinal)
            foreach ($path in $paths) {
                $file = Get-Item -LiteralPath $path

                $relative = $file.FullName.Substring($Source.Length + 1).Replace('\','/')
                $entry = $zip.CreateEntry($relative)
                $entry.ExternalAttributes = 0
                $input = [IO.File]::OpenRead($file.FullName)
                $output = $entry.Open()
                try { $input.CopyTo($output) }
                finally { $output.Dispose(); $input.Dispose() }
            }
        }
        finally { $zip.Dispose() }
    }
    finally { $stream.Dispose() }
}

function New-RuntimeFixture {
    param([string] $Root)
    $cache = Join-Path $Root 'runtime-cache'
    $sources = Join-Path $Root 'runtime-sources'
    New-Item -ItemType Directory -Path $cache, $sources -Force | Out-Null
    $records = New-Object 'System.Collections.Generic.List[object]'
    foreach ($architecture in @('x86','x64')) {
        $machine = if ($architecture -ceq 'x86') { 0x014C } else { 0x8664 }
        $core = Join-Path $sources "$architecture-core"
        $desktop = Join-Path $sources "$architecture-desktop"
        New-Item -ItemType Directory -Path $core, $desktop -Force | Out-Null
        New-PortableTestPe -Path (Join-Path $core 'dotnet.exe') -Machine $machine
        New-PortableTestPe -Path (Join-Path $core 'host\fxr\9.0.8\hostfxr.dll') -Machine $machine
        New-PortableTestPe -Path (Join-Path $core 'shared\Microsoft.NETCore.App\9.0.8\coreclr.dll') -Machine $machine
        [IO.File]::WriteAllText((Join-Path $core 'LICENSE.txt'), 'fixture dotnet license')
        [IO.File]::WriteAllText((Join-Path $core 'ThirdPartyNotices.txt'), 'fixture dotnet notices')
        New-PortableTestPe -Path (Join-Path $desktop 'shared\Microsoft.WindowsDesktop.App\9.0.8\PresentationFramework.dll') -Machine $machine

        foreach ($component in @('core','windowsDesktop')) {
            $name = if ($component -ceq 'core') {
                "dotnet-runtime-9.0.8-win-$architecture.zip"
            } else {
                "windowsdesktop-runtime-9.0.8-win-$architecture.zip"
            }
            $source = if ($component -ceq 'core') { $core } else { $desktop }
            $archive = Join-Path $cache $name
            New-FixtureZip -Source $source -Destination $archive
            $records.Add([ordered]@{
                architecture = $architecture
                component = $component
                name = $name
                url = "https://fixture.invalid/$name"
                sha512 = (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash
            })
        }
    }
    $lock = Join-Path $Root 'runtime-lock.json'
    [ordered]@{
        schemaVersion = 1
        dotnetDesktopRuntime = [ordered]@{
            version = '9.0.8'
            portableArchives = @($records.ToArray())
        }
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $lock -Encoding utf8
    [pscustomobject]@{ Cache=$cache; Lock=$lock }
}

$script:portableTestVersionProxyRoot = $null
$script:portableTestVersionProxy = $null

function Get-PortableTestMsBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} `
        'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
        throw 'Visual Studio vswhere.exe is unavailable for the portable package test proxy build.'
    }
    $installation = (& $vswhere -latest -products '*' `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($installation)) {
        throw 'Visual Studio C++ Build Tools are unavailable for the portable package test proxy build.'
    }
    $msbuild = Join-Path $installation 'MSBuild\Current\Bin\MSBuild.exe'
    if (-not (Test-Path -LiteralPath $msbuild -PathType Leaf)) {
        throw "MSBuild is unavailable for the portable package test proxy build: $msbuild"
    }
    return $msbuild
}

function Remove-PortableTestVersionProxy {
    if ($null -ne $script:portableTestVersionProxyRoot -and
            (Test-Path -LiteralPath $script:portableTestVersionProxyRoot)) {
        Remove-Item -LiteralPath $script:portableTestVersionProxyRoot -Recurse -Force
    }
    $script:portableTestVersionProxyRoot = $null
    $script:portableTestVersionProxy = $null
}

function Get-PortableTestVersionProxy {
    if ($null -ne $script:portableTestVersionProxy -and
            (Test-Path -LiteralPath $script:portableTestVersionProxy -PathType Leaf)) {
        return $script:portableTestVersionProxy
    }
    $project = Join-Path $scriptRoot `
        'native\BlindSoldier.VersionProxy\BlindSoldier.VersionProxy.vcxproj'
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "Version proxy project is unavailable for the portable package test: $project"
    }
    $root = Join-Path ([IO.Path]::GetTempPath()) (
        'blind-soldier-portable-version-proxy-' + [Guid]::NewGuid().ToString('N'))
    try {
        $output = Join-Path $root 'out'
        $intermediate = Join-Path $root 'obj'
        New-Item -ItemType Directory -Path $output, $intermediate -Force | Out-Null
        $outputProperty = $output.TrimEnd('\') + '\'
        $intermediateProperty = $intermediate.TrimEnd('\') + '\'
        $arguments = @(
            $project, '/nologo', '/m', '/t:Rebuild', '/p:Configuration=Release',
            '/p:Platform=Win32', "/p:OutDir=$outputProperty",
            "/p:IntDir=$intermediateProperty")
        & (Get-PortableTestMsBuild) @arguments | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Version proxy test build failed with exit code $LASTEXITCODE."
        }
        $proxy = Join-Path $output 'version.dll'
        if (-not (Test-Path -LiteralPath $proxy -PathType Leaf)) {
            throw "Version proxy test build did not produce version.dll: $proxy"
        }
        $script:portableTestVersionProxyRoot = $root
        $script:portableTestVersionProxy = $proxy
        return $proxy
    }
    catch {
        if (Test-Path -LiteralPath $root) {
            Remove-Item -LiteralPath $root -Recurse -Force
        }
        throw
    }
}
function New-PortableFixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) (
        'blind-soldier-portable-test-' + [Guid]::NewGuid().ToString('N'))
    $prerequisites = Join-Path $root 'prerequisites'
    $mod = Join-Path $root 'ff7.accessibility.reloaded'
    $launcher = Join-Path $root 'launcher'
    $bootstrap = Join-Path $root 'bootstrap'
    New-Item -ItemType Directory -Path $prerequisites, $mod, $launcher, $bootstrap -Force | Out-Null

    $reloaded = Join-Path $prerequisites 'reloaded'
    foreach ($architecture in @('X86','X64')) {
        foreach ($relative in $loaderFiles) {
            $path = Join-Path $reloaded "Loader\$architecture\$relative"
            New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
            if ($relative -ceq 'Reloaded.Mod.Loader.runtimeconfig.json') {
                [IO.File]::WriteAllText($path, '{"runtimeOptions":{"tfm":"net9.0"}}')
            }
            elseif ([IO.Path]::GetExtension($relative) -ieq '.dll') {
                $machine = if ($architecture -ceq 'X86') { 0x014C } else { 0x8664 }
                New-PortableTestPe -Path $path -Machine $machine
            }
            else {
                [IO.File]::WriteAllText($path, "fixture $architecture $relative")
            }
        }
    }
    New-PortableTestPe -Path (Join-Path $reloaded 'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Machine 0x014C
    New-PortableTestPe -Path (Join-Path $reloaded 'Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Machine 0x8664
    New-PortableTestPe -Path (Join-Path $reloaded 'Loader\X86\Reloaded.Mod.Loader.dll') -Machine 0x014C
    New-PortableTestPe -Path (Join-Path $reloaded 'Loader\X64\Reloaded.Mod.Loader.dll') -Machine 0x8664

    $hooks = Join-Path $prerequisites 'shared-hooks'
    New-Item -ItemType Directory -Path (Join-Path $hooks 'x86'), (Join-Path $hooks 'x64') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $hooks 'ModConfig.json'), '{"ModId":"reloaded.sharedlib.hooks","ModVersion":"1.16.3"}')
    New-PortableTestPe -Path (Join-Path $hooks 'x86\Reloaded.Hooks.ReloadedII.dll') -Machine 0x014C
    New-PortableTestPe -Path (Join-Path $hooks 'x64\Reloaded.Hooks.ReloadedII.dll') -Machine 0x8664

    $ffnx = Join-Path $prerequisites 'ffnx'
    New-PortableTestPe -Path (Join-Path $ffnx 'AF3DN.P') -Machine 0x014C
    New-PortableTestPe -Path (Join-Path $ffnx 'AF4DN.P') -Machine 0x014C
    New-PortableTestPe -Path (Join-Path $ffnx 'steam_api.dll') -Machine 0x014C
    [IO.File]::WriteAllText((Join-Path $ffnx 'FFNx.toml'), 'fixture FFNx config')
    [IO.File]::WriteAllText((Join-Path $ffnx 'FFNx.pdb'), 'must not ship')
    New-Item -ItemType Directory -Path (Join-Path $ffnx 'shaders') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $ffnx 'shaders\fixture.fx'), 'fixture shader')

    $notices = Join-Path $prerequisites 'notices'
    New-Item -ItemType Directory -Path $notices -Force | Out-Null
    foreach ($name in @(
        'THIRD-PARTY-NOTICES.md', 'Reloaded-II-GPL-3.0.txt',
        'Reloaded-Shared-Hooks-LGPL-3.0.txt', 'dotnet-LICENSE.txt',
        'dotnet-THIRD-PARTY-NOTICES.txt', 'FFNx-GPL-3.0.txt')) {
        [IO.File]::WriteAllText((Join-Path $notices $name), "fixture $name")
    }

    [IO.File]::WriteAllText((Join-Path $mod 'ModConfig.json'),
        '{"ModId":"ff7.accessibility.reloaded","ModVersion":"0.1.7","ModR2RManagedDll32":"x86/Ff7.Accessibility.Reloaded.dll","ModR2RManagedDll64":"x64/Ff7.Accessibility.Steam2026X64.dll","ModDependencies":["reloaded.sharedlib.hooks"],"SupportedAppId":["ff7_en.exe","ff7.exe","FFVII.exe"]}')
    New-PortableTestPe -Path (Join-Path $mod 'x86\Ff7.Accessibility.Reloaded.dll') -Machine 0x014C
    New-PortableTestPe -Path (Join-Path $mod 'x64\Ff7.Accessibility.Steam2026X64.dll') -Machine 0x8664
    New-PortableTestPe -Path (Join-Path $mod 'x86\prism.dll') -Machine 0x014C
    New-PortableTestPe -Path (Join-Path $mod 'x64\prism.dll') -Machine 0x8664
    [IO.File]::WriteAllText((Join-Path $mod 'x86\debug.pdb'), 'must not ship')
    New-Item -ItemType Directory -Path (Join-Path $mod 'Assets\movies') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $mod 'Assets\movies\opening_audio_description.ogg'), 'fixture narration')

    New-PortableTestPe -Path (Join-Path $launcher 'FFVII_LAUNCHER.exe') -Machine 0x014C
    [IO.File]::WriteAllText((Join-Path $launcher 'FFVII_LAUNCHER.exe.config'), 'fixture config')
    New-PortableTestPe -Path (Join-Path $launcher 'native\x86\FFVII_LAUNCHER.prism.x86.dll') -Machine 0x014C
    $launcherManifest = [ordered]@{
        schemaVersion=2
        stockLauncherSha256=('A' * 64)
        launcher=[ordered]@{path='FFVII_LAUNCHER.exe';length=(Get-Item (Join-Path $launcher 'FFVII_LAUNCHER.exe')).Length;sha256=(Get-FileHash (Join-Path $launcher 'FFVII_LAUNCHER.exe') -Algorithm SHA256).Hash}
        config=[ordered]@{path='FFVII_LAUNCHER.exe.config';length=(Get-Item (Join-Path $launcher 'FFVII_LAUNCHER.exe.config')).Length;sha256=(Get-FileHash (Join-Path $launcher 'FFVII_LAUNCHER.exe.config') -Algorithm SHA256).Hash}
        prism=[ordered]@{path='native/x86/FFVII_LAUNCHER.prism.x86.dll';length=(Get-Item (Join-Path $launcher 'native\x86\FFVII_LAUNCHER.prism.x86.dll')).Length;sha256=(Get-FileHash (Join-Path $launcher 'native\x86\FFVII_LAUNCHER.prism.x86.dll') -Algorithm SHA256).Hash}
        assemblyName='FFVII_LAUNCHER'; assemblyVersion='2.0.0.0'
    }
    $launcherManifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $launcher 'launcher-bundle.json') -Encoding utf8

    New-PortableTestPe -Path (Join-Path $bootstrap 'Blind-Soldier-Bootstrap-x86.exe') -Machine 0x014C
    New-PortableTestPe -Path (Join-Path $bootstrap 'Blind-Soldier-Bootstrap-x64.exe') -Machine 0x8664
    $versionProxy = Get-PortableTestVersionProxy
    $runtime = New-RuntimeFixture -Root $root

    [pscustomobject]@{
        Root=$root; Prerequisites=$prerequisites; Mod=$mod; Launcher=$launcher
        Bootstrap=$bootstrap; VersionProxy=$versionProxy; VersionProxyBuildRoot=$script:portableTestVersionProxyRoot; RuntimeCache=$runtime.Cache
        RuntimeLock=$runtime.Lock
        First=(Join-Path $root 'Blind-Soldier-Portable-1.zip')
        Second=(Join-Path $root 'Blind-Soldier-Portable-2.zip')
    }
}

function Invoke-FixtureBuild {
    param([psobject] $Fixture, [string] $Output)
    & $builderPath -OutputPath $Output -Version '0.1.7' `
        -PrerequisiteBundlePath $Fixture.Prerequisites `
        -ModPackagePath $Fixture.Mod `
        -LauncherBundlePath $Fixture.Launcher `
        -BootstrapBinaryPath $Fixture.Bootstrap `
        -VersionProxyPath $Fixture.VersionProxy `
        -DependencyCachePath $Fixture.RuntimeCache `
        -DependencyLockPath $Fixture.RuntimeLock | Out-Null
}

function Get-PortableEntries {
    param([string] $Path)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try { return @($archive.Entries | Where-Object Name | ForEach-Object FullName) }
    finally { $archive.Dispose() }
}

function Get-PortableEntryText {
    param([string] $Path, [string] $EntryPath)
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

function Get-PortableEntryHash {
    param([string] $Path, [string] $EntryPath)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.GetEntry($EntryPath)
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            $stream = $entry.Open()
            try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','') }
            finally { $stream.Dispose() }
        }
        finally { $sha.Dispose() }
    }
    finally { $archive.Dispose() }
}

function New-UnsafePortableZip {
    param([string] $Path, [string] $EntryName = '../escaped.txt')
    Add-Type -AssemblyName System.IO.Compression
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew)
    try {
        $archive = New-Object IO.Compression.ZipArchive(
            $stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $entry = $archive.CreateEntry($EntryName)
            $writer = New-Object IO.StreamWriter($entry.Open())
            try { $writer.Write('unsafe') } finally { $writer.Dispose() }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    [IO.File]::WriteAllText($Path + '.sha256',
        "$hash  $([IO.Path]::GetFileName($Path))`n")
}
function New-PortableArchiveVariant {
    param(
        [string] $Source,
        [string] $Destination,
        [string] $AdditionalPath,
        [switch] $ReverseManifest
    )
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stage = Join-Path ([IO.Path]::GetTempPath()) (
        'blind-soldier-portable-variant-' + [Guid]::NewGuid().ToString('N'))
    try {
        [IO.Compression.ZipFile]::ExtractToDirectory($Source, $stage)
        if (-not [string]::IsNullOrWhiteSpace($AdditionalPath)) {
            $additional = Join-Path $stage $AdditionalPath.Replace('/','\')
            New-Item -ItemType Directory -Path (Split-Path -Parent $additional) `
                -Force | Out-Null
            [IO.File]::WriteAllBytes($additional, @(0x4D,0x5A,0x00))
        }
        [string[]]$variantFiles = @(Get-ChildItem -LiteralPath $stage -File -Recurse |
            Where-Object Name -cne 'portable-manifest.json' |
            ForEach-Object FullName)
        [Array]::Sort($variantFiles, [StringComparer]::Ordinal)
        $records = @(
            foreach ($path in $variantFiles) {
                $file = Get-Item -LiteralPath $path
                $relative = $file.FullName.Substring($stage.Length + 1).Replace('\','/')
                [ordered]@{
                    path=$relative
                    length=$file.Length
                    sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
                }
            }
        )
        if ($ReverseManifest) {
            $records = @($records | Sort-Object { $_.path } -Descending)
        }
        [ordered]@{schemaVersion=1;version='0.1.7';files=$records} |
            ConvertTo-Json -Depth 6 |
            Set-Content -LiteralPath (Join-Path $stage 'portable-manifest.json') `
                -Encoding utf8
        New-FixtureZip -Source $stage -Destination $Destination
        $hash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
        [IO.File]::WriteAllText($Destination + '.sha256',
            "$hash  $([IO.Path]::GetFileName($Destination))`n")
    }
    finally {
        if (Test-Path -LiteralPath $stage) {
            Remove-Item -LiteralPath $stage -Recurse -Force
        }
    }
}

Describe 'Blind Soldier direct-extract portable package' {
    AfterEach {
        if ($null -ne $fixture -and (Test-Path -LiteralPath $fixture.Root)) {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
        $fixture = $null
    }

    AfterAll {
        Remove-PortableTestVersionProxy
    }

    It 'ships the complete no-installer accessibility payload in all supported layouts' {
        $fixture = New-PortableFixture
        Invoke-FixtureBuild -Fixture $fixture -Output $fixture.First
        $entries = @(Get-PortableEntries -Path $fixture.First)
        foreach ($required in @(
            'FFVII_LAUNCHER.exe', 'FFVII_LAUNCHER.exe.config',
            'launcher_accessibility/native/x86/FFVII_LAUNCHER.prism.x86.dll',
            'ff7_en.exe.local/version.dll',
            'ff7.exe.local/version.dll',
            'workingdir/version.dll',
            'workingdir/ff7_en.exe.local/version.dll',
            'workingdir/ff7.exe.local/version.dll',
            'ff7/workingdir/version.dll',
            'ff7/workingdir/ff7_en.exe.local/version.dll',
            'ff7/workingdir/ff7.exe.local/version.dll',
            'Blind-Soldier/Bootstrap/x86/Blind-Soldier-Bootstrap-x86.exe',
            'Blind-Soldier/Bootstrap/x64/Blind-Soldier-Bootstrap-x64.exe',
            'Blind-Soldier/Policy/BlindSoldier.ExternalOwnership.json',
            'Blind-Soldier/Policy/BlindSoldier.ExternalOwnership.psm1',
            'Blind-Soldier/Runtime/dotnet/x86/host/fxr/9.0.8/hostfxr.dll',
            'Blind-Soldier/Runtime/dotnet/x64/host/fxr/9.0.8/hostfxr.dll',
            'Reloaded-II/portable.txt',
            'Reloaded-II/Loader/X86/Reloaded.Mod.Loader.dll',
            'Reloaded-II/Loader/X64/Reloaded.Mod.Loader.dll',
            'Reloaded-II/Loader/X86/Bootstrapper/Reloaded.Mod.Loader.Bootstrapper.dll',
            'Reloaded-II/Loader/X64/Bootstrapper/Reloaded.Mod.Loader.Bootstrapper.dll',
            'Reloaded-II/Mods/ff7.accessibility.reloaded/ModConfig.json',
            'Reloaded-II/Mods/ff7.accessibility.reloaded/x86/Ff7.Accessibility.Reloaded.dll',
            'Reloaded-II/Mods/ff7.accessibility.reloaded/x64/Ff7.Accessibility.Steam2026X64.dll',
            'Reloaded-II/Mods/ff7.accessibility.reloaded/x86/prism.dll',
            'Reloaded-II/Mods/ff7.accessibility.reloaded/x64/prism.dll',
            'Reloaded-II/Mods/ff7.accessibility.reloaded/Assets/movies/opening_audio_description.ogg',
            'Reloaded-II/Mods/reloaded.sharedlib.hooks/ModConfig.json',
            'Reloaded-II/Mods/reloaded.sharedlib.hooks/x86/Reloaded.Hooks.ReloadedII.dll',
            'Reloaded-II/Mods/reloaded.sharedlib.hooks/x64/Reloaded.Hooks.ReloadedII.dll',
            'LICENSES/dotnet-LICENSE.txt',
            'LICENSES/dotnet-THIRD-PARTY-NOTICES.txt',
            'LICENSES/Reloaded-II-1.30.3-Blind-Soldier-source.md',
            'LICENSES/Reloaded-II-1.30.3-hostfxr.patch',
            'Remove-Amethyst-Registry-Entries.cmd',
            'Blind-Soldier/Tools/Remove-AmethystRegistryEntries.ps1',
            'README-PORTABLE.txt', 'portable-manifest.json'
        )) { ($entries -ccontains $required) | Should Be $true }

        foreach ($forbidden in @(
            'Blind-Soldier-Installer.exe', 'Blind-Soldier-Launcher-x86.exe',
            'Blind-Soldier-Launcher-x64.exe', 'winmm.dll', 'version.dll',
            'dinput.dll',
            'Reloaded-II/ReloadedII.json')) {
            ($entries -ccontains $forbidden) | Should Be $false
        }
        @($entries | Where-Object { $_ -match '(?i)(windowsdesktop-runtime-.+\.exe|\.(pdb|obj|iobj|ipdb)$)' }).Count | Should Be 0
        ($entries -ccontains 'AF3DN.P') | Should Be $false
        ($entries -ccontains 'FFNx.toml') | Should Be $false
        @($entries | Where-Object { $_ -match '(?i)(^|/)(winmm|dsound)\.dll$' }).Count |
            Should Be 0
        @($entries | Where-Object { $_ -match '(?i)\.asi$' }).Count | Should Be 0
        $forbiddenExternal = @(
            'AF3DN.P','AF4DN.P','FFNx.dll','7H_GameDriver.dll','FFNx.toml',
            'steam_api.dll','steam_api64.dll','dinput.dll',
            'AppProxy.dll','AppProxy.runtimeconfig.json','AppWrapper.dll','nethost.dll',
            'winmm.dll'
        )
        foreach ($forbidden in $forbiddenExternal) {
            @($entries | Where-Object {
                [IO.Path]::GetFileName($_.Replace('/','\\')) -ieq $forbidden
            }).Count | Should Be 0
        }
    }

    It 'uses eight layout-scoped Version proxies and no root x86 proxy' {
        $fixture = New-PortableFixture
        Invoke-FixtureBuild -Fixture $fixture -Output $fixture.First
        $hashes = @(
            'ff7_en.exe.local/version.dll', 'ff7.exe.local/version.dll',
            'workingdir/version.dll',
            'workingdir/ff7_en.exe.local/version.dll',
            'workingdir/ff7.exe.local/version.dll',
            'ff7/workingdir/version.dll',
            'ff7/workingdir/ff7_en.exe.local/version.dll',
            'ff7/workingdir/ff7.exe.local/version.dll'
        ) | ForEach-Object { Get-PortableEntryHash -Path $fixture.First -EntryPath $_ }
        @($hashes | Select-Object -Unique).Count | Should Be 1
        (Get-PortableEntries -Path $fixture.First) -ccontains 'version.dll' |
            Should Be $false
    }
    It 'builds the verifier Version proxy from current source into owned temporary output' {
        $fixture = New-PortableFixture
        $ignoredProxyOutput = Join-Path $scriptRoot `
            'native\BlindSoldier.VersionProxy\bin\Release\Win32\version.dll'
        $fixture.VersionProxy | Should Not Be $ignoredProxyOutput
        $fixture.VersionProxy | Should Match ([regex]::Escape(
            [IO.Path]::GetFullPath($fixture.VersionProxyBuildRoot)))
        Test-Path -LiteralPath $fixture.VersionProxyBuildRoot -PathType Container |
            Should Be $true
        Test-Path -LiteralPath $fixture.VersionProxy -PathType Leaf |
            Should Be $true
    }

    It 'contains exactly the eight allowed Windows-canonical Version proxy paths' {
        $fixture = New-PortableFixture
        Invoke-FixtureBuild -Fixture $fixture -Output $fixture.First
        $allowed = @(
            'ff7_en.exe.local/version.dll', 'ff7.exe.local/version.dll',
            'workingdir/version.dll',
            'workingdir/ff7_en.exe.local/version.dll',
            'workingdir/ff7.exe.local/version.dll',
            'ff7/workingdir/version.dll',
            'ff7/workingdir/ff7_en.exe.local/version.dll',
            'ff7/workingdir/ff7.exe.local/version.dll'
        )
        $canonicalVersionPaths = @(
            Get-PortableEntries -Path $fixture.First | Where-Object {
                [IO.Path]::GetFileName($_.Replace('/','\')).TrimEnd(' ','.') -ieq 'version.dll'
            } | Sort-Object
        )
        $canonicalVersionPaths.Count | Should Be 8
        ($canonicalVersionPaths -join '|') | Should Be (($allowed | Sort-Object) -join '|')
    }

    It 'rejects an extra nested Windows-canonical Version proxy path' {
        $fixture = New-PortableFixture
        Invoke-FixtureBuild -Fixture $fixture -Output $fixture.First
        $variant = Join-Path $fixture.Root 'extra-version.zip'
        New-PortableArchiveVariant -Source $fixture.First -Destination $variant `
            -AdditionalPath 'hidden/version.dll'
        { & $verifierPath -ArchivePath $variant -ExpectedVersion '0.1.7' } |
            Should Throw 'exactly eight Version proxy entries'
    }

    It 'ships the live-tested x86 host compatibility assets without changing x64 Reloaded' {
        $fixture = New-PortableFixture
        Invoke-FixtureBuild -Fixture $fixture -Output $fixture.First
        Get-PortableEntryHash -Path $fixture.First -EntryPath `
            'Reloaded-II/Loader/X86/Bootstrapper/Reloaded.Mod.Loader.Bootstrapper.dll' |
            Should Be '997A8EC95434239AFEFF0802849043EC49ED51459394D5DC97375D1914606329'

        $runtime = Get-PortableEntryText -Path $fixture.First -EntryPath `
            'Reloaded-II/Loader/X86/Reloaded.Mod.Loader.runtimeconfig.json' |
            ConvertFrom-Json
        $runtime.runtimeOptions.framework.name | Should Be 'Microsoft.NETCore.App'
        $runtime.runtimeOptions.framework.version | Should Be '9.0.0'
        $runtime.runtimeOptions.rollForward | Should Be 'LatestMinor'
        ($null -eq $runtime.runtimeOptions.PSObject.Properties['frameworks']) | Should Be $true

        $x64Hash = Get-PortableEntryHash -Path $fixture.First -EntryPath `
            'Reloaded-II/Loader/X64/Bootstrapper/Reloaded.Mod.Loader.Bootstrapper.dll'
        $fixtureX64Hash = (Get-FileHash -LiteralPath (Join-Path $fixture.Prerequisites `
            'reloaded\Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') `
            -Algorithm SHA256).Hash
        $x64Hash | Should Be $fixtureX64Hash
    }

    It 'rejects every recognized external FFNx runtime entry point' {
        $fixture = New-PortableFixture
        Invoke-FixtureBuild -Fixture $fixture -Output $fixture.First
        $unexpected = New-Object 'System.Collections.Generic.List[string]'
        foreach ($fileName in @('FFNx.dll','7H_GameDriver.dll','steam_api64.dll')) {
            $variant = Join-Path $fixture.Root ('external-{0}.zip' -f $fileName)
            New-PortableArchiveVariant -Source $fixture.First -Destination $variant `
                -AdditionalPath ('zz-external/{0}' -f $fileName)
            try {
                & $verifierPath -ArchivePath $variant -ExpectedVersion '0.1.7' |
                    Out-Null
                $unexpected.Add(('{0}: completed' -f $fileName))
            }
            catch {
                if ($_.Exception.Message -notlike '*forbidden files*') {
                    $unexpected.Add(('{0}: {1}' -f $fileName,
                        $_.Exception.Message))
                }
            }
        }
        ($unexpected -join '; ') | Should Be ''
    }

    It 'rejects the pinned FFNx tree at each canonical deployment root' {
        $fixture = New-PortableFixture
        Invoke-FixtureBuild -Fixture $fixture -Output $fixture.First
        $unexpected = New-Object 'System.Collections.Generic.List[string]'
        foreach ($relative in @(
            'COPYING.TXT',
            'ambient/nested/field.wav',
            'workingdir/FFNx.pdb',
            'workingdir/ShAdErS/nested/effect.fx',
            'workingdir/AppLoader.log',
            'ff7/workingdir/FFNx.pdb',
            'ff7/workingdir/ShAdErS/nested/effect.fx',
            '.7thWrapperProfile',
            'ff7/workingdir/AppLoader.log')) {
            $safeName = $relative.Replace('/','-').Replace('\','-')
            $variant = Join-Path $fixture.Root ("external-$safeName.zip")
            New-PortableArchiveVariant -Source $fixture.First `
                -Destination $variant -AdditionalPath $relative
            try {
                & $verifierPath -ArchivePath $variant -ExpectedVersion '0.1.7' |
                    Out-Null
                $unexpected.Add("$relative`: completed")
            }
            catch {
                if ($_.Exception.Message -notmatch '7th Heaven|FFNx|external') {
                    $unexpected.Add("$relative`: $($_.Exception.Message)")
                }
            }
        }
        ($unexpected -join '; ') | Should Be ''
    }

    It 'allows Blind Soldier owned paths whose leaf matches an FFNx directory prefix' {
        $fixture = New-PortableFixture
        Invoke-FixtureBuild -Fixture $fixture -Output $fixture.First
        $variant = Join-Path $fixture.Root 'owned-music.zip'
        New-PortableArchiveVariant -Source $fixture.First -Destination $variant `
            -AdditionalPath 'Blind-Soldier/Assets/music/owned.ogg'
        $result = & $verifierPath -ArchivePath $variant -ExpectedVersion '0.1.7'
        $result.Version | Should Be '0.1.7'
    }
    It 'rejects an otherwise-valid archive with unordered manifest records' {
        $fixture = New-PortableFixture
        Invoke-FixtureBuild -Fixture $fixture -Output $fixture.First
        $variant = Join-Path $fixture.Root 'unordered-manifest.zip'
        New-PortableArchiveVariant -Source $fixture.First -Destination $variant `
            -ReverseManifest:$true
        $inspection = Join-Path $fixture.Root 'unordered-manifest-inspection'
        [IO.Compression.ZipFile]::ExtractToDirectory($variant, $inspection)
        $originalManifest = Get-PortableEntryText -Path $fixture.First `
            -EntryPath 'portable-manifest.json' | ConvertFrom-Json
        $variantManifest = [IO.File]::ReadAllText(
            (Join-Path $inspection 'portable-manifest.json')) | ConvertFrom-Json
        $variantManifest.files[0].path |
            Should Be ([string]$originalManifest.files[-1].path)

        $manifestFailure = $null
        try {
            & $verifierPath -ArchivePath $variant -ExpectedVersion '0.1.7'
        }
        catch {
            $manifestFailure = $_
        }
        $manifestFailure.Exception.Message |
            Should BeLike '*Portable manifest records are not in ordinal order*'
    }

    It 'passes the production verifier with the real Version proxy fixture' {
        $fixture = New-PortableFixture
        Invoke-FixtureBuild -Fixture $fixture -Output $fixture.First
        $result = & $verifierPath -ArchivePath $fixture.First -ExpectedVersion '0.1.7'
        $result.Version | Should Be '0.1.7'
        $result.VersionProxyExports | Should Be 17
        $result.SidecarVerified | Should Be $true
        $result.SafeExtraction | Should Be $true
    }
    It 'starts with the two-step accessible instructions and contains no registry workflow' {
        $fixture = New-PortableFixture
        Invoke-FixtureBuild -Fixture $fixture -Output $fixture.First
        $readme = Get-PortableEntryText -Path $fixture.First -EntryPath 'README-PORTABLE.txt'
        $readme.StartsWith("Blind Soldier 0.1.7`r`n`r`n1. Extract every file in this ZIP into your Final Fantasy VII game folder.`r`n2. Start the game normally from Steam or 7th Heaven.") | Should Be $true
        $readme | Should Not Match '(?i)Blind-Soldier-Installer|/install|/uninstall|Image File Execution Options'
        $readme | Should Match '(?i)no administrator'
        $readme | Should Match '(?i)\.local\\version\.dll'
        $readme | Should Match '(?i)machine.s own Windows version library'
        $readme | Should Not Match '(?i)winmm\.dll'
        $readme | Should Match '(?i)7th Heaven manages.*FFNx'
        $readme | Should Not Match '(?i)FFNx 1\.24\.3\.0.*included'
    }

    It 'ships a narrowly scoped cleanup for Amethyst lifecycle registry entries' {
        $fixture = New-PortableFixture
        Invoke-FixtureBuild -Fixture $fixture -Output $fixture.First
        $command = Get-PortableEntryText -Path $fixture.First `
            -EntryPath 'Remove-Amethyst-Registry-Entries.cmd'
        $cleanup = Get-PortableEntryText -Path $fixture.First `
            -EntryPath 'Blind-Soldier/Tools/Remove-AmethystRegistryEntries.ps1'

        $command | Should Match '(?i)Remove-AmethystRegistryEntries\.ps1'
        $cleanup | Should Match 'BlindSoldierDebuggerOwner'
        $cleanup | Should Match "'ff7_en\.exe'"
        $cleanup | Should Match "'FFVII\.exe'"
        $cleanup | Should Match "'BlindSoldier_Launcher\.exe'"
        $cleanup | Should Match 'RegistryView\]::Registry32'
        $cleanup | Should Match 'RegistryView\]::Registry64'
        $cleanup | Should Not Match `
            '(?i)CurrentVersion\\Uninstall|7th Heaven|Steam\\|dotnet\\Setup'
        $cleanup | Should Not Match '(?i)DeleteSubKeyTree|Remove-Item'
    }

    It 'rejects a modified registry cleanup implementation' {
        $fixture = New-PortableFixture
        Invoke-FixtureBuild -Fixture $fixture -Output $fixture.First
        $variant = Join-Path $fixture.Root 'modified-cleanup.zip'
        New-PortableArchiveVariant -Source $fixture.First -Destination $variant `
            -AdditionalPath 'Blind-Soldier/Tools/Remove-AmethystRegistryEntries.ps1'
        { & $verifierPath -ArchivePath $variant -ExpectedVersion '0.1.7' } |
            Should Throw 'differs from the reviewed source'
    }

    It 'is byte-for-byte deterministic for identical inputs' {
        $fixture = New-PortableFixture
        Invoke-FixtureBuild -Fixture $fixture -Output $fixture.First
        Invoke-FixtureBuild -Fixture $fixture -Output $fixture.Second
        (Get-FileHash -LiteralPath $fixture.First -Algorithm SHA256).Hash |
            Should Be (Get-FileHash -LiteralPath $fixture.Second -Algorithm SHA256).Hash
        [IO.File]::ReadAllText($fixture.First + '.sha256') |
            Should Be ((Get-FileHash $fixture.First -Algorithm SHA256).Hash + '  ' +
                [IO.Path]::GetFileName($fixture.First) + "`n")
    }

    It 'rejects an unsafe ZIP member before extraction' {
        $fixture = New-PortableFixture
        New-UnsafePortableZip -Path $fixture.First
        { & $verifierPath -ArchivePath $fixture.First -ExpectedVersion '0.1.7' } |
            Should Throw 'unsafe path member'
        Test-Path -LiteralPath (Join-Path $fixture.Root 'escaped.txt') |
            Should Be $false
    }

    It 'rejects a raw ZIP component that ends in a period' {
        $fixture = New-PortableFixture
        New-UnsafePortableZip -Path $fixture.First `
            -EntryName 'ff7/workingdir/AF3DN.P.'
        { & $verifierPath -ArchivePath $fixture.First -ExpectedVersion '0.1.7' } |
            Should Throw 'unsafe Windows path component'
    }

    It 'rejects a raw ZIP component that ends in a space' {
        $fixture = New-PortableFixture
        New-UnsafePortableZip -Path $fixture.First `
            -EntryName 'payload.asi '
        { & $verifierPath -ArchivePath $fixture.First -ExpectedVersion '0.1.7' } |
            Should Throw 'unsafe Windows path component'
    }

    It 'rejects a wrong-architecture bootstrap without producing an archive' {
        $fixture = New-PortableFixture
        New-PortableTestPe -Path (Join-Path $fixture.Bootstrap `
            'Blind-Soldier-Bootstrap-x64.exe') -Machine 0x014C
        { Invoke-FixtureBuild -Fixture $fixture -Output $fixture.First } |
            Should Throw 'expected 0x8664'
        Test-Path -LiteralPath $fixture.First | Should Be $false
    }

    It 'rejects text that leaks the obsolete registry launch workflow' {
        $fixture = New-PortableFixture
        [IO.File]::WriteAllText((Join-Path $fixture.Prerequisites `
            'notices\THIRD-PARTY-NOTICES.md'),
            'Image File Execution Options must not ship')
        Invoke-FixtureBuild -Fixture $fixture -Output $fixture.First
        { & $verifierPath -ArchivePath $fixture.First -ExpectedVersion '0.1.7' } |
            Should Throw 'obsolete registry workflow'
    }
    if ($env:BLIND_SOLDIER_PESTER_FORCE_FAILURE -eq '1') {
        It 'propagates a deliberate Pester failure to the invoking process' {
            $true | Should Be $false
        }
    }
}
