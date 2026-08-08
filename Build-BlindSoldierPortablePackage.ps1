[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $OutputPath,
    [string] $Version = '0.1.9',
    [string] $PrerequisiteBundlePath,
    [string] $ModPackagePath,
    [string] $BootstrapBinaryPath,
    [string] $VersionProxyPath,
    [string] $LauncherBundlePath,
    [string] $DependencyCachePath,
    [string] $DependencyLockPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$reloadedX86CompatibilityRoot = Join-Path $scriptRoot `
    'installer-assets\reloaded\x86'
$reloadedX86Bootstrapper = Join-Path $reloadedX86CompatibilityRoot `
    'Reloaded.Mod.Loader.Bootstrapper.dll'
$reloadedX86RuntimeConfig = Join-Path $reloadedX86CompatibilityRoot `
    'Reloaded.Mod.Loader.runtimeconfig.json'
$reloadedX86BootstrapperSha256 = `
    '997A8EC95434239AFEFF0802849043EC49ED51459394D5DC97375D1914606329'
$reloadedX86RuntimeConfigSha256 = `
    '2CD7DD1EBB7A203244AFEF13B91A64780EC0BAEDA3086A6EA3FAD5073531AA3F'
$semanticVersionPattern = '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'
if ($Version -notmatch $semanticVersionPattern) { throw "Invalid semantic version: $Version" }

$output = [IO.Path]::GetFullPath($OutputPath)
if ([IO.Path]::GetExtension($output) -cne '.zip') { throw 'OutputPath must name a .zip file.' }
if (Test-Path -LiteralPath $output) { throw "Portable package output already exists: $output" }
$sidecar = $output + '.sha256'
if (Test-Path -LiteralPath $sidecar) { throw "Portable checksum output already exists: $sidecar" }
$outputParent = Split-Path -Parent $output
if ([string]::IsNullOrWhiteSpace($outputParent)) { throw 'OutputPath must have a parent directory.' }
New-Item -ItemType Directory -Path $outputParent -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($DependencyLockPath)) {
    $DependencyLockPath = Join-Path $scriptRoot 'installer-dependencies\dependency-lock.json'
}
if ([string]::IsNullOrWhiteSpace($DependencyCachePath)) {
    $DependencyCachePath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) `
        'BlindSwordsman\BuildCache\portable-dotnet-9.0.8'
}

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
$utf8 = New-Object Text.UTF8Encoding($false)

function Assert-OrdinaryTree {
    param([string] $Root, [string] $Label)
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        throw "$Label is missing: $Root"
    }
    foreach ($item in @((Get-Item -LiteralPath $Root -Force)) +
            @(Get-ChildItem -LiteralPath $Root -Force -Recurse)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Label cannot contain a reparse point: $($item.FullName)"
        }
    }
}

function Assert-File {
    param([string] $Path, [string] $Label)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label is missing: $Path"
    }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label cannot be a reparse point: $Path"
    }
    return $item
}

function Assert-ExactFile {
    param(
        [string] $Path,
        [int64] $Length,
        [string] $Sha256,
        [string] $Label
    )
    $item = Assert-File -Path $Path -Label $Label
    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($item.Length -ne $Length -or $actualHash -cne $Sha256) {
        throw "$Label does not match the live-tested release asset."
    }
    return $item
}

function Get-PeMachine {
    param([string] $Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "Not a PE image: $Path"
    }
    $offset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($offset -lt 64 -or $offset + 6 -gt $bytes.Length -or
        [BitConverter]::ToUInt32($bytes, $offset) -ne 0x00004550) {
        throw "Invalid PE header: $Path"
    }
    return [BitConverter]::ToUInt16($bytes, $offset + 4)
}

function Assert-PeMachine {
    param([string] $Path, [uint16] $Machine, [string] $Label)
    [void](Assert-File -Path $Path -Label $Label)
    $actual = Get-PeMachine -Path $Path
    if ($actual -ne $Machine) {
        throw ("{0} has PE machine 0x{1:X4}; expected 0x{2:X4}." -f
            $Label, $actual, $Machine)
    }
}

function Copy-OrdinaryTree {
    param([string] $Source, [string] $Destination, [string] $Label)
    Assert-OrdinaryTree -Root $Source -Label $Label
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($item in @(Get-ChildItem -LiteralPath $Source -Force)) {
        Copy-Item -LiteralPath $item.FullName -Destination $Destination -Recurse
    }
}

function Remove-PortableBuildDebris {
    param([string] $Tree, [string] $StagingRoot)
    $stagingPrefix = [IO.Path]::GetFullPath($StagingRoot).TrimEnd('\') + '\'
    $treeFull = [IO.Path]::GetFullPath($Tree)
    if (-not ($treeFull + '\').StartsWith($stagingPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Build-debris cleanup target escaped portable staging: $treeFull"
    }
    foreach ($file in @(Get-ChildItem -LiteralPath $treeFull -File -Recurse -Force |
            Where-Object Extension -In @('.pdb','.obj','.iobj','.ipdb'))) {
        Remove-Item -LiteralPath $file.FullName -Force
    }
}

function Get-RelativePath {
    param([string] $Root, [string] $Path)
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "File escaped staging root: $full"
    }
    return $full.Substring($prefix.Length).Replace('\','/')
}

function Write-PortableManifest {
    param([string] $Root)
    $map = New-Object 'System.Collections.Generic.Dictionary[string,string]' `
        ([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -File -Recurse -Force)) {
        $relative = Get-RelativePath -Root $Root -Path $file.FullName
        if ($relative -ceq 'portable-manifest.json') { continue }
        if ($map.ContainsKey($relative)) { throw "Duplicate portable path: $relative" }
        $map.Add($relative, $file.FullName)
    }
    [string[]] $paths = @($map.Keys)
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    $records = @($paths | ForEach-Object {
        $item = Get-Item -LiteralPath $map[$_]
        [ordered]@{
            path=$_
            length=[int64]$item.Length
            sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
        }
    })
    $manifest = [ordered]@{ schemaVersion=1; version=$Version; files=$records }
    [IO.File]::WriteAllText((Join-Path $Root 'portable-manifest.json'),
        (($manifest | ConvertTo-Json -Depth 6) + "`n"), $utf8)
}

function New-DeterministicZip {
    param([string] $Root, [string] $Destination)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $map = New-Object 'System.Collections.Generic.Dictionary[string,string]' `
        ([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -File -Recurse -Force)) {
        $relative = Get-RelativePath -Root $Root -Path $file.FullName
        if ($map.ContainsKey($relative)) { throw "Duplicate archive path: $relative" }
        $map.Add($relative, $file.FullName)
    }
    [string[]] $paths = @($map.Keys)
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    $stream = New-Object IO.FileStream($Destination, [IO.FileMode]::CreateNew,
        [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $archive = New-Object IO.Compression.ZipArchive($stream,
            [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $timestamp = New-Object DateTimeOffset(2000,1,1,0,0,0,[TimeSpan]::Zero)
            foreach ($relative in $paths) {
                $entry = $archive.CreateEntry($relative,
                    [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $timestamp
                $entry.ExternalAttributes = 0
                $source = [IO.File]::OpenRead($map[$relative])
                try {
                    $target = $entry.Open()
                    try { $source.CopyTo($target) } finally { $target.Dispose() }
                }
                finally { $source.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Get-MsBuildPath {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} `
        'Microsoft Visual Studio\Installer\vswhere.exe'
    [void](Assert-File -Path $vswhere -Label 'Visual Studio discovery tool')
    $installation = (& $vswhere -latest -products '*' `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($installation)) {
        throw 'Visual Studio C++ Build Tools are unavailable.'
    }
    $msbuild = Join-Path $installation 'MSBuild\Current\Bin\MSBuild.exe'
    [void](Assert-File -Path $msbuild -Label 'MSBuild')
    return $msbuild
}

function Assert-LauncherBundle {
    param([string] $Path)
    Assert-OrdinaryTree -Root $Path -Label 'Accessible launcher bundle'
    $expected = @(
        'FFVII_LAUNCHER.exe', 'FFVII_LAUNCHER.exe.config',
        'launcher-bundle.json', 'native\x86\FFVII_LAUNCHER.prism.x86.dll')
    $actual = @(Get-ChildItem -LiteralPath $Path -File -Recurse | ForEach-Object {
        $_.FullName.Substring([IO.Path]::GetFullPath($Path).TrimEnd('\').Length + 1)
    })
    if ($actual.Count -ne $expected.Count -or
        @($actual | Where-Object { $expected -cnotcontains $_ }).Count -ne 0) {
        throw 'Accessible launcher bundle contains missing or unexpected files.'
    }
    $manifest = [IO.File]::ReadAllText((Join-Path $Path 'launcher-bundle.json')) |
        ConvertFrom-Json
    if ([int]$manifest.schemaVersion -ne 2 -or
        [string]$manifest.assemblyName -cne 'FFVII_LAUNCHER' -or
        [string]$manifest.assemblyVersion -cne '2.0.0.0') {
        throw 'Accessible launcher bundle must use schema two and the stock launcher identity.'
    }
    foreach ($record in @(
        [pscustomobject]@{ Descriptor=$manifest.launcher; Relative='FFVII_LAUNCHER.exe' },
        [pscustomobject]@{ Descriptor=$manifest.config; Relative='FFVII_LAUNCHER.exe.config' },
        [pscustomobject]@{ Descriptor=$manifest.prism; Relative='native/x86/FFVII_LAUNCHER.prism.x86.dll' }
    )) {
        if ([string]$record.Descriptor.path -cne $record.Relative -or
            [string]$record.Descriptor.sha256 -cnotmatch '^[0-9A-F]{64}$') {
            throw "Accessible launcher descriptor is invalid: $($record.Relative)"
        }
        $file = Join-Path $Path $record.Relative.Replace('/','\')
        [void](Assert-File -Path $file -Label "Launcher bundle $($record.Relative)")
        if ([int64](Get-Item -LiteralPath $file).Length -ne
                [int64]$record.Descriptor.length -or
            -not (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.Equals(
                [string]$record.Descriptor.sha256,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Accessible launcher descriptor does not match: $($record.Relative)"
        }
    }
    Assert-PeMachine -Path (Join-Path $Path 'FFVII_LAUNCHER.exe') `
        -Machine 0x014C -Label 'Accessible FFVII launcher'
    Assert-PeMachine -Path (Join-Path $Path 'native\x86\FFVII_LAUNCHER.prism.x86.dll') `
        -Machine 0x014C -Label 'Accessible launcher Prism library'
}

$staging = Join-Path ([IO.Path]::GetTempPath()) `
    ('blind-soldier-portable-' + [Guid]::NewGuid().ToString('N'))
$inputs = Join-Path $staging 'inputs'
$root = Join-Path $staging 'root'
try {
    New-Item -ItemType Directory -Path $inputs, $root -Force | Out-Null

    if ([string]::IsNullOrWhiteSpace($PrerequisiteBundlePath)) {
        $PrerequisiteBundlePath = Join-Path $inputs 'prerequisites'
        & (Join-Path $scriptRoot 'Build-BlindSwordsmanPrerequisiteBundle.ps1') `
            -OutputPath $PrerequisiteBundlePath `
            -LockPath $DependencyLockPath `
            -CachePath (Join-Path (Split-Path -Parent $DependencyCachePath) `
                'prerequisites') | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Prerequisite builder exited with code $LASTEXITCODE." }
    }
    if ([string]::IsNullOrWhiteSpace($ModPackagePath)) {
        $ModPackagePath = Join-Path $inputs 'ff7.accessibility.reloaded'
        & (Join-Path $scriptRoot 'Build-DualRuntimePackage.ps1') `
            -OutputPath $ModPackagePath | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Dual-runtime package builder exited with code $LASTEXITCODE." }
    }
    if ([string]::IsNullOrWhiteSpace($LauncherBundlePath)) {
        $LauncherBundlePath = Join-Path $inputs 'launcher'
        & (Join-Path $scriptRoot 'Build-AccessibleLauncherBundle.ps1') `
            -OutputPath $LauncherBundlePath -Configuration Release | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Accessible launcher builder exited with code $LASTEXITCODE." }
    }

    if ([string]::IsNullOrWhiteSpace($BootstrapBinaryPath) -or
        [string]::IsNullOrWhiteSpace($VersionProxyPath)) {
        $msbuild = Get-MsBuildPath
        if ([string]::IsNullOrWhiteSpace($BootstrapBinaryPath)) {
            $project = Join-Path $scriptRoot `
                'native\BlindSoldier.Bootstrap\BlindSoldier.Bootstrap.vcxproj'
            foreach ($platform in @('Win32','x64')) {
                & $msbuild $project /nologo /m /t:Rebuild `
                    /p:Configuration=Release /p:Platform=$platform /v:minimal
                if ($LASTEXITCODE -ne 0) {
                    throw "Native bootstrap $platform build exited with code $LASTEXITCODE."
                }
            }
            $BootstrapBinaryPath = Join-Path $inputs 'bootstrap'
            New-Item -ItemType Directory -Path $BootstrapBinaryPath | Out-Null
            Copy-Item -LiteralPath (Join-Path $scriptRoot `
                'native\BlindSoldier.Bootstrap\bin\Release\Win32\Blind-Soldier-Bootstrap-x86.exe') `
                -Destination $BootstrapBinaryPath
            Copy-Item -LiteralPath (Join-Path $scriptRoot `
                'native\BlindSoldier.Bootstrap\bin\Release\x64\Blind-Soldier-Bootstrap-x64.exe') `
                -Destination $BootstrapBinaryPath
        }
        if ([string]::IsNullOrWhiteSpace($VersionProxyPath)) {
            $project = Join-Path $scriptRoot `
                'native\BlindSoldier.VersionProxy\BlindSoldier.VersionProxy.vcxproj'
            & $msbuild $project /nologo /m /t:Rebuild `
                /p:Configuration=Release /p:Platform=Win32 /v:minimal
            if ($LASTEXITCODE -ne 0) {
                throw "Native Version proxy build exited with code $LASTEXITCODE."
            }
            $VersionProxyPath = Join-Path $scriptRoot `
                'native\BlindSoldier.VersionProxy\bin\Release\Win32\version.dll'
        }
    }

    foreach ($tree in @(
        [pscustomobject]@{Path=$PrerequisiteBundlePath;Label='Prerequisite bundle'},
        [pscustomobject]@{Path=$ModPackagePath;Label='Blind Soldier mod package'},
        [pscustomobject]@{Path=$BootstrapBinaryPath;Label='Bootstrap binary bundle'}
    )) {
        Assert-OrdinaryTree -Root ([IO.Path]::GetFullPath($tree.Path)) -Label $tree.Label
    }
    Assert-LauncherBundle -Path ([IO.Path]::GetFullPath($LauncherBundlePath))
    Assert-PeMachine -Path $VersionProxyPath -Machine 0x014C `
        -Label 'Blind Soldier x86 Version proxy'

    $bootstrapX86 = Join-Path $BootstrapBinaryPath 'Blind-Soldier-Bootstrap-x86.exe'
    $bootstrapX64 = Join-Path $BootstrapBinaryPath 'Blind-Soldier-Bootstrap-x64.exe'
    Assert-PeMachine -Path $bootstrapX86 -Machine 0x014C -Label 'Blind Soldier x86 bootstrap'
    Assert-PeMachine -Path $bootstrapX64 -Machine 0x8664 -Label 'Blind Soldier x64 bootstrap'
    foreach ($record in @(
        [pscustomobject]@{Source=$bootstrapX86;Relative='Blind-Soldier\Bootstrap\x86\Blind-Soldier-Bootstrap-x86.exe'},
        [pscustomobject]@{Source=$bootstrapX64;Relative='Blind-Soldier\Bootstrap\x64\Blind-Soldier-Bootstrap-x64.exe'}
    )) {
        $destination = Join-Path $root $record.Relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $record.Source -Destination $destination
    }
    foreach ($directory in @(
        'ff7_en.exe.local', 'ff7.exe.local',
        'ff7\workingdir\ff7_en.exe.local',
        'ff7\workingdir\ff7.exe.local')) {
        $targetDirectory = Join-Path $root $directory
        New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
        Copy-Item -LiteralPath $VersionProxyPath `
            -Destination (Join-Path $targetDirectory 'version.dll')
    }

    foreach ($directory in @(
        'workingdir\ff7_en.exe.local', 'workingdir\ff7.exe.local')) {
        $targetDirectory = Join-Path $root $directory
        New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
        Copy-Item -LiteralPath $VersionProxyPath `
            -Destination (Join-Path $targetDirectory 'version.dll')
    }
    foreach ($relative in @(
        'workingdir\version.dll', 'ff7\workingdir\version.dll')) {
        $destination = Join-Path $root $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) `
            -Force | Out-Null
        Copy-Item -LiteralPath $VersionProxyPath -Destination $destination
    }

    $sourceReloaded = Join-Path $PrerequisiteBundlePath 'reloaded'
    $targetReloaded = Join-Path $root 'Reloaded-II'
    foreach ($architecture in @('X86','X64')) {
        foreach ($relative in $loaderFiles) {
            $source = Join-Path $sourceReloaded "Loader\$architecture\$relative"
            [void](Assert-File -Path $source -Label "Reloaded $architecture $relative")
            $destination = Join-Path $targetReloaded "Loader\$architecture\$relative"
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination $destination
        }
    }
    [void](Assert-ExactFile -Path $reloadedX86Bootstrapper -Length 132096 `
        -Sha256 $reloadedX86BootstrapperSha256 `
        -Label 'Blind Soldier x86 Reloaded compatibility bootstrapper')
    [void](Assert-ExactFile -Path $reloadedX86RuntimeConfig -Length 521 `
        -Sha256 $reloadedX86RuntimeConfigSha256 `
        -Label 'Blind Soldier x86 Reloaded compatibility runtime configuration')
    Copy-Item -LiteralPath $reloadedX86Bootstrapper -Destination (Join-Path `
        $targetReloaded `
        'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Force
    Copy-Item -LiteralPath $reloadedX86RuntimeConfig -Destination (Join-Path `
        $targetReloaded 'Loader\X86\Reloaded.Mod.Loader.runtimeconfig.json') `
        -Force
    $x64DelayHooksOverride = Join-Path $scriptRoot `
        'installer-assets\reloaded\x64\DelayInjectHooks.json'
    [void](Assert-File -Path $x64DelayHooksOverride `
        -Label 'Blind Soldier x64 delayed-injection configuration')
    Copy-Item -LiteralPath $x64DelayHooksOverride `
        -Destination (Join-Path $targetReloaded 'Loader\X64\DelayInjectHooks.json') -Force
    Assert-PeMachine -Path (Join-Path $targetReloaded `
        'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') `
        -Machine 0x014C -Label 'x86 Reloaded bootstrapper'
    Assert-PeMachine -Path (Join-Path $targetReloaded `
        'Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') `
        -Machine 0x8664 -Label 'x64 Reloaded bootstrapper'
    Assert-PeMachine -Path (Join-Path $targetReloaded `
        'Loader\X86\Reloaded.Mod.Loader.dll') -Machine 0x014C `
        -Label 'x86 Reloaded loader'
    Assert-PeMachine -Path (Join-Path $targetReloaded `
        'Loader\X64\Reloaded.Mod.Loader.dll') -Machine 0x8664 `
        -Label 'x64 Reloaded loader'

    $modConfigPath = Join-Path $ModPackagePath 'ModConfig.json'
    [void](Assert-File -Path $modConfigPath -Label 'Blind Soldier ModConfig.json')
    $modConfig = [IO.File]::ReadAllText($modConfigPath) | ConvertFrom-Json
    if ([string]$modConfig.ModId -cne 'ff7.accessibility.reloaded' -or
        [string]$modConfig.ModVersion -cne $Version -or
        (@($modConfig.ModDependencies) -join ',') -cne 'reloaded.sharedlib.hooks' -or
        (@($modConfig.SupportedAppId) -join ',') -cne 'ff7_en.exe,ff7.exe,FFVII.exe') {
        throw 'Blind Soldier mod identity, version, dependency, or application order is invalid.'
    }
    $modTarget = Join-Path $targetReloaded 'Mods\ff7.accessibility.reloaded'
    Copy-OrdinaryTree -Source $ModPackagePath -Destination $modTarget `
        -Label 'Blind Soldier mod package'
    Remove-PortableBuildDebris -Tree $modTarget -StagingRoot $root
    Assert-PeMachine -Path (Join-Path $modTarget `
        'x86\Ff7.Accessibility.Reloaded.dll') -Machine 0x014C `
        -Label 'Blind Soldier x86 entry assembly'
    Assert-PeMachine -Path (Join-Path $modTarget `
        'x64\Ff7.Accessibility.Steam2026X64.dll') -Machine 0x8664 `
        -Label 'Blind Soldier x64 entry assembly'
    Assert-PeMachine -Path (Join-Path $modTarget 'x86\prism.dll') `
        -Machine 0x014C -Label 'Blind Soldier x86 Prism'
    Assert-PeMachine -Path (Join-Path $modTarget 'x64\prism.dll') `
        -Machine 0x8664 -Label 'Blind Soldier x64 Prism'

    $hooksSource = Join-Path $PrerequisiteBundlePath 'shared-hooks'
    $hooksConfigPath = Join-Path $hooksSource 'ModConfig.json'
    [void](Assert-File -Path $hooksConfigPath -Label 'Shared Hooks ModConfig.json')
    $hooksConfig = [IO.File]::ReadAllText($hooksConfigPath) | ConvertFrom-Json
    if ([string]$hooksConfig.ModId -cne 'reloaded.sharedlib.hooks') {
        throw 'Shared Hooks ModId is invalid.'
    }
    $hooksTarget = Join-Path $targetReloaded 'Mods\reloaded.sharedlib.hooks'
    Copy-OrdinaryTree -Source $hooksSource -Destination $hooksTarget `
        -Label 'Shared Hooks package'
    Assert-PeMachine -Path (Join-Path $hooksTarget `
        'x86\Reloaded.Hooks.ReloadedII.dll') -Machine 0x014C `
        -Label 'Shared Hooks x86 entry assembly'
    Assert-PeMachine -Path (Join-Path $hooksTarget `
        'x64\Reloaded.Hooks.ReloadedII.dll') -Machine 0x8664 `
        -Label 'Shared Hooks x64 entry assembly'

    New-Item -ItemType Directory -Path (Join-Path $targetReloaded 'Apps'),
        (Join-Path $targetReloaded 'User\Mods'),
        (Join-Path $targetReloaded 'User\Misc'),
        (Join-Path $targetReloaded 'Plugins') -Force | Out-Null
    foreach ($relative in @('Apps\.keep','User\Mods\.keep','User\Misc\.keep','Plugins\.keep')) {
        [IO.File]::WriteAllBytes((Join-Path $targetReloaded $relative), @())
    }
    [IO.File]::WriteAllBytes((Join-Path $targetReloaded 'portable.txt'), @())

    foreach ($record in @(
        [pscustomobject]@{Source='FFVII_LAUNCHER.exe';Target='FFVII_LAUNCHER.exe'},
        [pscustomobject]@{Source='FFVII_LAUNCHER.exe.config';Target='FFVII_LAUNCHER.exe.config'},
        [pscustomobject]@{Source='native\x86\FFVII_LAUNCHER.prism.x86.dll';Target='launcher_accessibility\native\x86\FFVII_LAUNCHER.prism.x86.dll'}
    )) {
        $source = Join-Path $LauncherBundlePath $record.Source
        $destination = Join-Path $root $record.Target
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination
    }

    Import-Module (Join-Path $scriptRoot 'PortableDotNetRuntime.psm1') -Force
    foreach ($architecture in @('x86','x64')) {
        $runtimeTarget = Join-Path $root "Blind-Soldier\Runtime\dotnet\$architecture"
        Expand-VerifiedPortableDotNetRuntime -Architecture $architecture `
            -Destination $runtimeTarget -CachePath $DependencyCachePath `
            -LockPath $DependencyLockPath | Out-Null
        $machine = if ($architecture -ceq 'x86') { 0x014C } else { 0x8664 }
        Assert-PeMachine -Path (Join-Path $runtimeTarget `
            'host\fxr\9.0.8\hostfxr.dll') -Machine $machine `
            -Label "Private .NET $architecture hostfxr"
    }

    $licenseTarget = Join-Path $root 'LICENSES'
    New-Item -ItemType Directory -Path $licenseTarget | Out-Null
    foreach ($name in @(
        'THIRD-PARTY-NOTICES.md', 'Reloaded-II-GPL-3.0.txt',
        'Reloaded-Shared-Hooks-LGPL-3.0.txt', 'dotnet-LICENSE.txt',
        'dotnet-THIRD-PARTY-NOTICES.txt')) {
        $source = Join-Path $PrerequisiteBundlePath "notices\$name"
        [void](Assert-File -Path $source -Label "Portable license $name")
        Copy-Item -LiteralPath $source -Destination (Join-Path $licenseTarget $name)
    }
    foreach ($record in @(
        [pscustomobject]@{
            Source='README.md'
            Target='Reloaded-II-1.30.3-Blind-Soldier-source.md'
        },
        [pscustomobject]@{
            Source='Reloaded-II-1.30.3-hostfxr.patch'
            Target='Reloaded-II-1.30.3-hostfxr.patch'
        }
    )) {
        $source = Join-Path $reloadedX86CompatibilityRoot $record.Source
        [void](Assert-File -Path $source -Label "Reloaded source material $($record.Source)")
        Copy-Item -LiteralPath $source -Destination (Join-Path $licenseTarget $record.Target)
    }

    $policyTarget = Join-Path $root 'Blind-Soldier\Policy'
    New-Item -ItemType Directory -Path $policyTarget -Force | Out-Null
    foreach ($name in @(
        'BlindSoldier.ExternalOwnership.json',
        'BlindSoldier.ExternalOwnership.psm1')) {
        $source = Join-Path (Join-Path $scriptRoot 'tools') $name
        [void](Assert-File -Path $source -Label "External ownership policy $name")
        Copy-Item -LiteralPath $source -Destination `
            (Join-Path $policyTarget $name)
    }

    $cleanupAssetRoot = Join-Path $scriptRoot 'portable-assets'
    $cleanupCommand = Join-Path $cleanupAssetRoot `
        'Remove-Amethyst-Registry-Entries.cmd'
    $automaticCleanupCommand = Join-Path $cleanupAssetRoot `
        'Remove-AmethystRegistryEntries-Automatic.cmd'
    $cleanupScript = Join-Path $cleanupAssetRoot `
        'Remove-AmethystRegistryEntries.ps1'
    [void](Assert-File -Path $cleanupCommand `
        -Label 'Amethyst registry cleanup command')
    [void](Assert-File -Path $cleanupScript `
        -Label 'Amethyst registry cleanup implementation')
    [void](Assert-File -Path $automaticCleanupCommand `
        -Label 'Automatic Amethyst registry cleanup command')
    Copy-Item -LiteralPath $cleanupCommand -Destination (Join-Path $root `
        'Remove-Amethyst-Registry-Entries.cmd')
    $cleanupTarget = Join-Path $root 'Blind-Soldier\Tools'
    New-Item -ItemType Directory -Path $cleanupTarget -Force | Out-Null
    Copy-Item -LiteralPath $automaticCleanupCommand -Destination (Join-Path `
        $cleanupTarget 'Remove-AmethystRegistryEntries-Automatic.cmd')
    Copy-Item -LiteralPath $cleanupScript -Destination (Join-Path $cleanupTarget `
        'Remove-AmethystRegistryEntries.ps1')

    $readme = @"
Blind Soldier $Version

1. Extract every file in this ZIP into your Final Fantasy VII game folder.
2. Start the game normally from Steam or 7th Heaven.

No installer is required. No administrator access, registry change, or separate .NET installation is required.

Supported layouts
- Steam 2026 x64: extract beside FFVII.exe and FFVII_LAUNCHER.exe. Use Steam or the included accessible launcher. Its Play button starts the packaged x64 accessibility bootstrap automatically.
- Stock Steam 2013 without 7th Heaven: use the Blind Soldier (2013) Accessibility Mod Manager entry. Its game-specific package installs the x86 Version proxy directly beside ff7_en.exe.
- Stock 7th Heaven x86: extract at the directory that contains workingdir, or at the Steam root that contains ff7\workingdir. Blind Soldier's sibling workingdir\version.dll starts accessibility when FFNx loads it; no file in the 7th Heaven installation is replaced or edited.
- 7th Heaven nested x86: extract at the Steam 2026 root. The copies under ff7\workingdir support 7th Heaven's converted game layout.

Official stable 7th Heaven manages its own FFNx installation; this release is verified against 7th Heaven 4.5.2. Blind Soldier coexists without shipping, replacing, or overwriting any FFNx file. Development builds targeting .NET 10 are outside this release's verified compatibility set.
This ZIP contains only manifest-owned Blind Soldier files.

Do not replace an existing layout-scoped version.dll unless it belongs to Blind Soldier. This includes a sibling workingdir\version.dll used with stock 7th Heaven. Move an unknown file aside first so it can be restored. If FFNx redirects the system Version library back to the proxy, Blind Soldier privately caches a byte-for-byte copy of this machine's own Windows version library under Local AppData and loads that distinct copy; no Windows binary is shipped in the ZIP.

Logs are written under Blind-Soldier\Logs. Players never need to run either bootstrap program themselves.

If an older Amethyst mod-manager installation left automatic-launch settings behind, run Remove-Amethyst-Registry-Entries.cmd once. It requests administrator access, removes only matching Blind Soldier entries, and preserves unrelated registry values. New portable installs do not need this cleanup.

Steam Verify Files may restore the stock FFVII launcher. Extract this ZIP again afterward to restore the accessible launcher.

To remove Blind Soldier, close FFVII, read portable-manifest.json from this ZIP, and delete only the Blind Soldier files named in that manifest. Restore any launcher or version.dll you backed up before extraction.
"@
    $readme = ($readme.Trim() -replace "`r?`n", "`r`n") + "`r`n"
    [IO.File]::WriteAllText((Join-Path $root 'README-PORTABLE.txt'),
        $readme, $utf8)

    Assert-OrdinaryTree -Root $root -Label 'Portable staging tree'
    Write-PortableManifest -Root $root
    New-DeterministicZip -Root $root -Destination $output
    $hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToUpperInvariant()
    [IO.File]::WriteAllText($sidecar,
        "$hash  $([IO.Path]::GetFileName($output))`n", $utf8)
    [pscustomobject]@{
        OutputPath=$output
        ChecksumPath=$sidecar
        Sha256=$hash
        Version=$Version
    }
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}
