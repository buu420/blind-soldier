[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $OutputPath,
    [string] $Version = '0.1.0-pre.7',
    [string] $PrerequisiteBundlePath,
    [string] $ModPackagePath,
    [string] $LauncherBundlePath,
    [string] $LauncherPrismPath,
    [string] $NativeBinaryPath
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
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
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) { throw "$Label is missing: $Root" }
    foreach ($item in @((Get-Item -LiteralPath $Root -Force)) + @(Get-ChildItem -LiteralPath $Root -Force -Recurse)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Label cannot contain a reparse point: $($item.FullName)"
        }
    }
}

function Assert-File {
    param([string] $Path, [string] $Label)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Label is missing: $Path" }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Label cannot be a reparse point: $Path" }
    return $item
}

function Get-PeMachine {
    param([string] $Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) { throw "Not a PE image: $Path" }
    $offset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($offset -lt 64 -or $offset + 6 -gt $bytes.Length -or [BitConverter]::ToUInt32($bytes, $offset) -ne 0x00004550) {
        throw "Invalid PE header: $Path"
    }
    return [BitConverter]::ToUInt16($bytes, $offset + 4)
}

function Assert-PeMachine {
    param([string] $Path, [uint16] $Machine, [string] $Label)
    [void](Assert-File -Path $Path -Label $Label)
    $actual = Get-PeMachine -Path $Path
    if ($actual -ne $Machine) { throw ("{0} has PE machine 0x{1:X4}; expected 0x{2:X4}." -f $Label, $actual, $Machine) }
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
    if (-not ($treeFull + '\').StartsWith(
            $stagingPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Build-debris cleanup target escaped portable staging: $treeFull"
    }
    foreach ($file in @(Get-ChildItem -LiteralPath $treeFull -File -Recurse -Force | Where-Object {
        $_.Extension -in @('.pdb', '.obj', '.iobj', '.ipdb')
    })) {
        Remove-Item -LiteralPath $file.FullName -Force
    }
}

function Get-RelativePath {
    param([string] $Root, [string] $Path)
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "File escaped staging root: $full" }
    return $full.Substring($prefix.Length).Replace('\','/')
}

function Write-PortableManifest {
    param([string] $Root)
    $map = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::OrdinalIgnoreCase)
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
        [ordered]@{ path=$_; length=[int64]$item.Length; sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToUpperInvariant() }
    })
    $manifest = [ordered]@{ schemaVersion=1; version=$Version; files=$records }
    [IO.File]::WriteAllText((Join-Path $Root 'portable-manifest.json'), (($manifest | ConvertTo-Json -Depth 6) + "`n"), $utf8)
}

function New-DeterministicZip {
    param([string] $Root, [string] $Destination)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $map = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -File -Recurse -Force)) {
        $relative = Get-RelativePath -Root $Root -Path $file.FullName
        if ($map.ContainsKey($relative)) { throw "Duplicate archive path: $relative" }
        $map.Add($relative, $file.FullName)
    }
    [string[]] $paths = @($map.Keys)
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    $stream = New-Object IO.FileStream($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $timestamp = New-Object DateTimeOffset(2000,1,1,0,0,0,[TimeSpan]::Zero)
            foreach ($relative in $paths) {
                $entry = $archive.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $timestamp
                $entry.ExternalAttributes = 0
                $source = [IO.File]::OpenRead($map[$relative])
                try {
                    $target = $entry.Open()
                    try { $source.CopyTo($target) } finally { $target.Dispose() }
                } finally { $source.Dispose() }
            }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
}

function Get-MsBuildPath {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    [void](Assert-File -Path $vswhere -Label 'Visual Studio discovery tool')
    $installation = (& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($installation)) { throw 'Visual Studio Build Tools are unavailable.' }
    $msbuild = Join-Path $installation 'MSBuild\Current\Bin\MSBuild.exe'
    [void](Assert-File -Path $msbuild -Label 'MSBuild')
    return $msbuild
}

$staging = Join-Path ([IO.Path]::GetTempPath()) ('blind-soldier-portable-' + [Guid]::NewGuid().ToString('N'))
$inputs = Join-Path $staging 'inputs'
$root = Join-Path $staging 'root'
try {
    New-Item -ItemType Directory -Path $inputs, $root -Force | Out-Null
    if ([string]::IsNullOrWhiteSpace($PrerequisiteBundlePath)) {
        $PrerequisiteBundlePath = Join-Path $inputs 'prerequisites'
        & (Join-Path $scriptRoot 'Build-BlindSwordsmanPrerequisiteBundle.ps1') -OutputPath $PrerequisiteBundlePath | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Prerequisite builder exited with code $LASTEXITCODE." }
    }
    if ([string]::IsNullOrWhiteSpace($ModPackagePath)) {
        $ModPackagePath = Join-Path $inputs 'ff7.accessibility.reloaded'
        & (Join-Path $scriptRoot 'Build-DualRuntimePackage.ps1') -OutputPath $ModPackagePath | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Dual-runtime package builder exited with code $LASTEXITCODE." }
    }
    if ([string]::IsNullOrWhiteSpace($LauncherBundlePath)) { $LauncherBundlePath = Join-Path $scriptRoot 'installer-assets\launcher' }
    if ([string]::IsNullOrWhiteSpace($LauncherPrismPath)) {
        $candidate = Join-Path $LauncherBundlePath 'FFVII_LAUNCHER.prism.x86.dll'
        $LauncherPrismPath = if (Test-Path -LiteralPath $candidate -PathType Leaf) { $candidate } else { Join-Path $scriptRoot 'Ff7.Accessibility.Reloaded\Native\win-x86\prism.dll' }
    }
    if ([string]::IsNullOrWhiteSpace($NativeBinaryPath)) {
        $msbuild = Get-MsBuildPath
        $installerProject = Join-Path $scriptRoot 'native\BlindSoldier.Installer\BlindSoldier.Installer.vcxproj'
        $launcherProject = Join-Path $scriptRoot 'native\BlindSoldier.Launcher\BlindSoldier.Launcher.vcxproj'
        & $msbuild $installerProject /nologo /m /p:Configuration=Release /p:Platform=x64 /v:minimal
        if ($LASTEXITCODE -ne 0) { throw "Native installer build exited with code $LASTEXITCODE." }
        & $msbuild $launcherProject /nologo /m /p:Configuration=Release /p:Platform=Win32 /v:minimal
        if ($LASTEXITCODE -ne 0) { throw "Native x86 launcher build exited with code $LASTEXITCODE." }
        & $msbuild $launcherProject /nologo /m /p:Configuration=Release /p:Platform=x64 /v:minimal
        if ($LASTEXITCODE -ne 0) { throw "Native x64 launcher build exited with code $LASTEXITCODE." }
        $NativeBinaryPath = Join-Path $inputs 'native'
        New-Item -ItemType Directory -Path $NativeBinaryPath | Out-Null
        Copy-Item -LiteralPath (Join-Path $scriptRoot 'native\BlindSoldier.Installer\bin\Release\x64\Blind-Soldier-Installer.exe') -Destination $NativeBinaryPath
        Copy-Item -LiteralPath (Join-Path $scriptRoot 'native\BlindSoldier.Launcher\bin\Release\Win32\Blind-Soldier-Launcher-x86.exe') -Destination $NativeBinaryPath
        Copy-Item -LiteralPath (Join-Path $scriptRoot 'native\BlindSoldier.Launcher\bin\Release\x64\Blind-Soldier-Launcher-x64.exe') -Destination $NativeBinaryPath
    }

    foreach ($tree in @(
        [pscustomobject]@{ Path=$PrerequisiteBundlePath; Label='Prerequisite bundle' },
        [pscustomobject]@{ Path=$ModPackagePath; Label='Blind Soldier mod package' },
        [pscustomobject]@{ Path=$LauncherBundlePath; Label='Accessible launcher bundle' },
        [pscustomobject]@{ Path=$NativeBinaryPath; Label='Native binary bundle' }
    )) { Assert-OrdinaryTree -Root ([IO.Path]::GetFullPath($tree.Path)) -Label $tree.Label }

    $nativeFiles = @(
        [pscustomobject]@{ Name='Blind-Soldier-Installer.exe'; Machine=0x8664; Label='Blind Soldier native installer' },
        [pscustomobject]@{ Name='Blind-Soldier-Launcher-x86.exe'; Machine=0x014C; Label='Blind Soldier x86 launcher' },
        [pscustomobject]@{ Name='Blind-Soldier-Launcher-x64.exe'; Machine=0x8664; Label='Blind Soldier x64 launcher' }
    )
    foreach ($file in $nativeFiles) {
        $source = Join-Path $NativeBinaryPath $file.Name
        Assert-PeMachine -Path $source -Machine $file.Machine -Label $file.Label
        Copy-Item -LiteralPath $source -Destination (Join-Path $root $file.Name)
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
    Assert-PeMachine -Path (Join-Path $targetReloaded 'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Machine 0x014C -Label 'x86 Reloaded bootstrapper'
    Assert-PeMachine -Path (Join-Path $targetReloaded 'Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Machine 0x8664 -Label 'x64 Reloaded bootstrapper'

    $modConfigPath = Join-Path $ModPackagePath 'ModConfig.json'
    [void](Assert-File -Path $modConfigPath -Label 'Blind Soldier ModConfig.json')
    $modConfig = [IO.File]::ReadAllText($modConfigPath) | ConvertFrom-Json
    if ([string]$modConfig.ModId -cne 'ff7.accessibility.reloaded' -or @($modConfig.ModDependencies) -cnotcontains 'reloaded.sharedlib.hooks') {
        throw 'Blind Soldier mod identity or Shared Hooks dependency is invalid.'
    }
    $modTarget = Join-Path $targetReloaded 'Mods\ff7.accessibility.reloaded'
    Copy-OrdinaryTree -Source $ModPackagePath -Destination $modTarget -Label 'Blind Soldier mod package'
    Remove-PortableBuildDebris -Tree $modTarget -StagingRoot $root
    Assert-PeMachine -Path (Join-Path $modTarget 'x86\Ff7.Accessibility.Reloaded.dll') -Machine 0x014C -Label 'Blind Soldier x86 entry assembly'
    Assert-PeMachine -Path (Join-Path $modTarget 'x64\Ff7.Accessibility.Steam2026X64.dll') -Machine 0x8664 -Label 'Blind Soldier x64 entry assembly'

    $hooksSource = Join-Path $PrerequisiteBundlePath 'shared-hooks'
    $hooksConfigPath = Join-Path $hooksSource 'ModConfig.json'
    [void](Assert-File -Path $hooksConfigPath -Label 'Shared Hooks ModConfig.json')
    $hooksConfig = [IO.File]::ReadAllText($hooksConfigPath) | ConvertFrom-Json
    if ([string]$hooksConfig.ModId -cne 'reloaded.sharedlib.hooks') { throw 'Shared Hooks ModId is invalid.' }
    $hooksTarget = Join-Path $targetReloaded 'Mods\reloaded.sharedlib.hooks'
    Copy-OrdinaryTree -Source $hooksSource -Destination $hooksTarget -Label 'Shared Hooks package'
    Assert-PeMachine -Path (Join-Path $hooksTarget 'x86\Reloaded.Hooks.ReloadedII.dll') -Machine 0x014C -Label 'Shared Hooks x86 entry assembly'
    Assert-PeMachine -Path (Join-Path $hooksTarget 'x64\Reloaded.Hooks.ReloadedII.dll') -Machine 0x8664 -Label 'Shared Hooks x64 entry assembly'

    $accessibleLauncher = Join-Path $LauncherBundlePath 'FFVII_LAUNCHER.exe'
    $accessibleLauncherConfig = Join-Path $LauncherBundlePath 'FFVII_LAUNCHER.exe.config'
    Assert-PeMachine -Path $accessibleLauncher -Machine 0x014C -Label 'Accessible FFVII launcher'
    Assert-PeMachine -Path $LauncherPrismPath -Machine 0x014C -Label 'Accessible launcher Prism library'
    [void](Assert-File -Path $accessibleLauncherConfig -Label 'Accessible FFVII launcher configuration')
    Copy-Item -LiteralPath $accessibleLauncher -Destination (Join-Path $root 'FFVII_LAUNCHER.exe')
    Copy-Item -LiteralPath $accessibleLauncherConfig -Destination (Join-Path $root 'FFVII_LAUNCHER.exe.config')
    $launcherPrismTarget = Join-Path $root 'launcher_accessibility\native\x86\FFVII_LAUNCHER.prism.x86.dll'
    New-Item -ItemType Directory -Path (Split-Path -Parent $launcherPrismTarget) -Force | Out-Null
    Copy-Item -LiteralPath $LauncherPrismPath -Destination $launcherPrismTarget

    $licenseTarget = Join-Path $root 'LICENSES'
    New-Item -ItemType Directory -Path $licenseTarget | Out-Null
    foreach ($name in @('THIRD-PARTY-NOTICES.md','Reloaded-II-GPL-3.0.txt','Reloaded-Shared-Hooks-LGPL-3.0.txt')) {
        $source = Join-Path $PrerequisiteBundlePath "notices\$name"
        [void](Assert-File -Path $source -Label "Portable license $name")
        Copy-Item -LiteralPath $source -Destination (Join-Path $licenseTarget $name)
    }

    $readme = @"
Blind Soldier $Version - Portable Native Installer

1. Extract every file in this ZIP into the Final Fantasy VII Steam installation folder.
2. Run Blind-Soldier-Installer.exe and accept the Windows administrator prompt.
3. Choose Yes in the standard Windows confirmation dialog.
4. Launch Final Fantasy VII normally from Steam or the accessible FFVII launcher.

The installer supports legacy x86 ff7_en.exe, Steam 2026 x64 FFVII.exe, or both. It registers the adjacent architecture-matched launcher and does not copy files. To disable automatic loading, run:

Blind-Soldier-Installer.exe /uninstall

Uninstall removes only this extraction's owned registry launch redirect and intentionally leaves extracted files. Existing third-party launch debuggers are never overwritten or removed.

Required runtime: Microsoft .NET Desktop Runtime 9.0.8 or a newer 9.0 patch matching each game architecture you use. The installer checks before changing the registry. The standard Blind Soldier Setup release installs missing runtimes automatically; this portable installer preserves the smaller supplied installer's register-only behavior.

Logs are written beside the installer and launchers.
"@
    [IO.File]::WriteAllText((Join-Path $root 'README-PORTABLE.txt'), ($readme.Trim() + "`r`n"), $utf8)

    Assert-OrdinaryTree -Root $root -Label 'Portable staging tree'
    Write-PortableManifest -Root $root
    New-DeterministicZip -Root $root -Destination $output
    $hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToUpperInvariant()
    [IO.File]::WriteAllText($sidecar, "$hash  $([IO.Path]::GetFileName($output))`n", $utf8)
    [pscustomobject]@{ OutputPath=$output; ChecksumPath=$sidecar; Sha256=$hash; Version=$Version }
}
finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
