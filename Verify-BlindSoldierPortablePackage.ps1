[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $ArchivePath,
    [string] $ExpectedVersion = '0.1.6'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$archivePathFull = [IO.Path]::GetFullPath($ArchivePath)
$sidecarPath = $archivePathFull + '.sha256'
if (-not (Test-Path -LiteralPath $archivePathFull -PathType Leaf)) {
    throw "Portable archive is missing: $archivePathFull"
}
if (-not (Test-Path -LiteralPath $sidecarPath -PathType Leaf)) {
    throw "Portable archive checksum is missing: $sidecarPath"
}

$required = @(
    'FFVII_LAUNCHER.exe',
    'FFVII_LAUNCHER.exe.config',
    'launcher_accessibility/native/x86/FFVII_LAUNCHER.prism.x86.dll',
    'ff7_en.exe.local/version.dll',
    'ff7.exe.local/version.dll',
    'ff7/workingdir/ff7_en.exe.local/version.dll',
    'ff7/workingdir/ff7.exe.local/version.dll',
    'Blind-Soldier/Bootstrap/x86/Blind-Soldier-Bootstrap-x86.exe',
    'Blind-Soldier/Bootstrap/x64/Blind-Soldier-Bootstrap-x64.exe',
    'Blind-Soldier/Runtime/dotnet/x86/host/fxr/9.0.8/hostfxr.dll',
    'Blind-Soldier/Runtime/dotnet/x64/host/fxr/9.0.8/hostfxr.dll',
    'Blind-Soldier/Runtime/dotnet/x86/shared/Microsoft.NETCore.App/9.0.8/coreclr.dll',
    'Blind-Soldier/Runtime/dotnet/x64/shared/Microsoft.NETCore.App/9.0.8/coreclr.dll',
    'Blind-Soldier/Runtime/dotnet/x86/shared/Microsoft.WindowsDesktop.App/9.0.8/PresentationFramework.dll',
    'Blind-Soldier/Runtime/dotnet/x64/shared/Microsoft.WindowsDesktop.App/9.0.8/PresentationFramework.dll',
    'Reloaded-II/portable.txt',
    'Reloaded-II/Mods/ff7.accessibility.reloaded/ModConfig.json',
    'Reloaded-II/Mods/reloaded.sharedlib.hooks/ModConfig.json',
    'LICENSES/dotnet-LICENSE.txt',
    'LICENSES/dotnet-THIRD-PARTY-NOTICES.txt',
    'README-PORTABLE.txt',
    'portable-manifest.json'
)
$forbiddenExternalFileNames = @(
    'AF3DN.P','AF4DN.P','FFNx.toml','steam_api.dll','dinput.dll',
    'AppProxy.dll','AppProxy.runtimeconfig.json','AppWrapper.dll','nethost.dll',
    'winmm.dll'
)

$loaderFiles = @(
    'Bootstrapper/Reloaded.Mod.Loader.Bootstrapper.dll',
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

function Assert-SafeZipEntry {
    param([IO.Compression.ZipArchiveEntry] $Entry)
    $name = $Entry.FullName
    if ([string]::IsNullOrWhiteSpace($name) -or $name.Contains([char]0)) {
        throw 'Portable ZIP contains an empty or invalid member.'
    }
    $normalized = $name.Replace('\','/').TrimEnd('/')
    if ([string]::IsNullOrWhiteSpace($normalized) -or
        $normalized.StartsWith('/') -or $normalized.StartsWith('//') -or
        $normalized -match '^[A-Za-z]:' -or $normalized.Contains(':')) {
        throw "Portable ZIP contains a rooted or alternate-stream member: $name"
    }
    foreach ($part in $normalized.Split('/')) {
        if ([string]::IsNullOrWhiteSpace($part) -or $part -ceq '.' -or
            $part -ceq '..') {
            throw "Portable ZIP contains an unsafe path member: $name"
        }
        if ($part.EndsWith(' ') -or $part.EndsWith('.')) {
            throw "Portable ZIP contains an unsafe Windows path component: $name"
        }
    }
    $external = [BitConverter]::ToUInt32(
        [BitConverter]::GetBytes([int]$Entry.ExternalAttributes), 0)
    $unixType = ($external -shr 16) -band 0xF000
    if (($external -band [uint32][IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $unixType -eq 0xA000) {
        throw "Portable ZIP contains a reparse-point member: $name"
    }
    return $normalized
}

function Expand-SafePortableZip {
    param([string] $Path, [string] $Destination)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $orderedMembers = New-Object 'System.Collections.Generic.List[string]'

        $members = New-Object 'System.Collections.Generic.HashSet[string]' `
            ([StringComparer]::OrdinalIgnoreCase)
        $prefix = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
        foreach ($entry in $archive.Entries) {
            $relative = Assert-SafeZipEntry -Entry $entry
            if (-not $members.Add($relative)) {
                throw "Portable ZIP contains a case-insensitive duplicate member: $($entry.FullName)"
            }
            $orderedMembers.Add($relative)
            if ([string]::IsNullOrEmpty($entry.Name)) {
                throw "Portable ZIP contains an unnecessary directory member: $($entry.FullName)"
            }
            $target = [IO.Path]::GetFullPath(
                (Join-Path $Destination $relative.Replace('/','\')))
            if (-not $target.StartsWith($prefix,
                    [StringComparison]::OrdinalIgnoreCase)) {
                throw "Portable ZIP member escaped verification staging: $($entry.FullName)"
            }
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) `
                -Force | Out-Null
            $source = $entry.Open()
            try {
                $output = [IO.File]::Open($target, [IO.FileMode]::CreateNew,
                    [IO.FileAccess]::Write, [IO.FileShare]::None)
                try { $source.CopyTo($output) }
                finally { $output.Dispose() }
            }
            finally { $source.Dispose() }
        }
        return @($orderedMembers)
    }
    finally { $archive.Dispose() }
}

function Get-PeInfo {
    param([Parameter(Mandatory=$true)] [string] $Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 256 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "File is not a PE image: $Path"
    }
    $offset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($offset -lt 64 -or $offset + 24 -gt $bytes.Length -or
        [BitConverter]::ToUInt32($bytes, $offset) -ne 0x00004550) {
        throw "File has an invalid PE header: $Path"
    }
    $machine = [BitConverter]::ToUInt16($bytes, $offset + 4)
    $optional = $offset + 24
    $magic = [BitConverter]::ToUInt16($bytes, $optional)
    $directory = if ($magic -eq 0x10B) { $optional + 96 } `
        elseif ($magic -eq 0x20B) { $optional + 112 } `
        else { throw "File has an unsupported PE optional header: $Path" }
    $clrOffset = $directory + (14 * 8)
    $hasClr = $clrOffset + 8 -le $bytes.Length -and
        [BitConverter]::ToUInt32($bytes, $clrOffset) -ne 0
    [pscustomobject]@{ Machine=$machine; HasClr=$hasClr }
}

function Assert-Machine {
    param([string] $Path, [uint16] $Expected, [string] $Label)
    $actual = (Get-PeInfo -Path $Path).Machine
    if ($actual -ne $Expected) {
        throw ("{0} has PE machine 0x{1:X4}; expected 0x{2:X4}." -f
            $Label, $actual, $Expected)
    }
    return $actual
}

function Get-RelativePortablePath {
    param([string] $Root, [string] $Path)
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($prefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "File escaped the verification root: $fullPath"
    }
    return $fullPath.Substring($prefix.Length).Replace('\','/')
}

function Get-Dumpbin {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} `
        'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
        throw 'Visual Studio vswhere.exe is unavailable for native export verification.'
    }
    $install = (& $vswhere -latest -products '*' `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath | Select-Object -First 1)
    $dumpbin = Get-ChildItem -LiteralPath (Join-Path $install 'VC\Tools\MSVC') `
        -Recurse -Filter dumpbin.exe |
        Where-Object FullName -Match '\\Hostx64\\x64\\dumpbin\.exe$' |
        Sort-Object FullName -Descending | Select-Object -First 1 `
        -ExpandProperty FullName
    if (-not (Test-Path -LiteralPath $dumpbin -PathType Leaf)) {
        throw 'MSVC dumpbin.exe is unavailable for native export verification.'
    }
    return $dumpbin
}

function Get-DumpbinExports {
    param([string] $Dumpbin, [string] $Path)
    $lines = @(& $Dumpbin /exports $Path)
    if ($LASTEXITCODE -ne 0) { throw "dumpbin failed for $Path" }
    $exports = New-Object 'System.Collections.Generic.List[object]'
    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if ($trimmed -match '^(\d+)\s+([0-9A-F]+)\s+\[NONAME\]$') {
            $exports.Add([pscustomobject]@{
                ordinal=[int]$matches[1]; name=$null; noname=$true
            })
        }
        elseif ($trimmed -match '^(\d+)\s+[0-9A-F]+\s+[0-9A-F]+\s+(\S+)') {
            $exports.Add([pscustomobject]@{
                ordinal=[int]$matches[1]; name=[string]$matches[2]; noname=$false
            })
        }
    }
    return @($exports | Sort-Object ordinal)
}

$archiveHash = (Get-FileHash -LiteralPath $archivePathFull -Algorithm SHA256).Hash.ToUpperInvariant()
$expectedSidecar = "$archiveHash  $([IO.Path]::GetFileName($archivePathFull))"
if ([IO.File]::ReadAllText($sidecarPath).Trim() -cne $expectedSidecar) {
    throw 'The SHA-256 sidecar does not match the archive.'
}

$verificationRoot = Join-Path ([IO.Path]::GetTempPath()) `
    ('blind-soldier-portable-verification-' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $verificationRoot | Out-Null
    $entryNames = @(Expand-SafePortableZip -Path $archivePathFull `
        -Destination $verificationRoot)
    [string[]]$sortedNames = [string[]]$entryNames.Clone()
    [Array]::Sort($sortedNames, [StringComparer]::Ordinal)
    if (($entryNames -join '|') -cne ($sortedNames -join '|')) {
        throw 'ZIP entries are not in deterministic ordinal order.'
    }
    [string[]]$allowedVersionProxyPaths = @(
        'ff7_en.exe.local/version.dll',
        'ff7.exe.local/version.dll',
        'ff7/workingdir/ff7_en.exe.local/version.dll',
        'ff7/workingdir/ff7.exe.local/version.dll'
    )
    [string[]]$versionProxyEntries = @($entryNames | Where-Object {
        $baseName = [IO.Path]::GetFileName($_.Replace('/','\')).TrimEnd(
            [char[]]@(' ', '.'))
        $baseName -ieq 'version.dll'
    })
    [string[]]$sortedVersionProxyEntries = [string[]]$versionProxyEntries.Clone()
    [string[]]$sortedAllowedVersionProxyPaths = [string[]]$allowedVersionProxyPaths.Clone()
    [Array]::Sort($sortedVersionProxyEntries, [StringComparer]::Ordinal)
    [Array]::Sort($sortedAllowedVersionProxyPaths, [StringComparer]::Ordinal)
    if ($versionProxyEntries.Count -ne 4 -or
            ($sortedVersionProxyEntries -join '|') -cne
            ($sortedAllowedVersionProxyPaths -join '|')) {
        throw 'Portable archive must contain exactly four Version proxy entries at the approved .local paths.'
    }

    foreach ($item in @(Get-Item -LiteralPath $verificationRoot -Force) +
            @(Get-ChildItem -LiteralPath $verificationRoot -Recurse -Force)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Portable extraction contains a reparse point: $($item.FullName)"
        }
    }

    foreach ($relative in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $verificationRoot `
                $relative.Replace('/','\')) -PathType Leaf)) {
            throw "Portable archive is missing required file: $relative"
        }
    }
    if ((Get-Item -LiteralPath (Join-Path $verificationRoot `
            'Reloaded-II\portable.txt')).Length -ne 0) {
        throw 'Reloaded-II/portable.txt must be empty.'
    }

    $manifestPath = Join-Path $verificationRoot 'portable-manifest.json'
    $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
    if ([int]$manifest.schemaVersion -ne 1 -or
        [string]$manifest.version -cne $ExpectedVersion) {
        throw 'Portable manifest identity does not match the requested release.'
    }
    [string[]]$manifestRecordPaths = @($manifest.files | ForEach-Object {
        [string]$_.path
    })
    [string[]]$sortedManifestRecordPaths = [string[]]$manifestRecordPaths.Clone()
    [Array]::Sort($sortedManifestRecordPaths, [StringComparer]::Ordinal)
    if (($manifestRecordPaths -join '|') -cne
            ($sortedManifestRecordPaths -join '|')) {
        throw 'Portable manifest records are not in ordinal order.'
    }

    $actualFiles = New-Object `
        'System.Collections.Generic.Dictionary[string,System.IO.FileInfo]' `
        ([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in @(Get-ChildItem -LiteralPath $verificationRoot -File `
            -Recurse -Force)) {
        if ($file.FullName -ceq $manifestPath) { continue }
        $relative = Get-RelativePortablePath -Root $verificationRoot `
            -Path $file.FullName
        if ($actualFiles.ContainsKey($relative)) {
            throw "Case-insensitive duplicate archive path: $relative"
        }
        $actualFiles.Add($relative, $file)
    }
    $manifestNames = New-Object 'System.Collections.Generic.HashSet[string]' `
        ([StringComparer]::OrdinalIgnoreCase)
    foreach ($record in @($manifest.files)) {
        $relative = [string]$record.path
        if (-not $manifestNames.Add($relative)) {
            throw "Portable manifest contains a duplicate path: $relative"
        }
        if (-not $actualFiles.ContainsKey($relative)) {
            throw "Manifest file is missing: $relative"
        }
        $file = $actualFiles[$relative]
        if ([int64]$record.length -ne [int64]$file.Length) {
            throw "Manifest length mismatch: $relative"
        }
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if (-not $hash.Equals([string]$record.sha256,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Manifest SHA-256 mismatch: $relative"
        }
        [void]$actualFiles.Remove($relative)
    }
    if ($actualFiles.Count -ne 0) {
        throw "Files are absent from the portable manifest: $($actualFiles.Keys -join ', ')"
    }

    foreach ($architecture in @('X86','X64')) {
        $loaderRoot = Join-Path $verificationRoot `
            "Reloaded-II\Loader\$architecture"
        [string[]]$actualLoader = @(Get-ChildItem -LiteralPath $loaderRoot `
            -File -Recurse | ForEach-Object {
                Get-RelativePortablePath -Root $loaderRoot -Path $_.FullName
            })
        [string[]]$expectedLoader = @($loaderFiles)
        [Array]::Sort($actualLoader, [StringComparer]::Ordinal)
        [Array]::Sort($expectedLoader, [StringComparer]::Ordinal)
        if (($actualLoader -join '|') -cne ($expectedLoader -join '|')) {
            throw "$architecture Reloaded loader closure is not exact."
        }
        $runtimeConfig = [IO.File]::ReadAllText((Join-Path $loaderRoot `
            'Reloaded.Mod.Loader.runtimeconfig.json')) | ConvertFrom-Json
        if ([string]$runtimeConfig.runtimeOptions.tfm -cne 'net9.0') {
            throw "$architecture Reloaded runtime target is not net9.0."
        }
    }

    $forbidden = @(Get-ChildItem -LiteralPath $verificationRoot -File -Recurse |
        Where-Object {
            $_.Name -in $forbiddenExternalFileNames -or
            $_.Name -ieq 'winmm.dll' -or
            $_.Name -ieq 'dsound.dll' -or
            $_.Extension -ieq '.asi' -or
            $_.Name -in @(
                'Blind-Soldier-Installer.exe',
                'Blind-Soldier-Launcher-x86.exe',
                'Blind-Soldier-Launcher-x64.exe',
                'Reloaded-II.exe', 'ReloadedII.json') -or
            $_.Name -like 'ASILoader*.dll' -or
            $_.Name -match '^windowsdesktop-runtime-.+\.exe$' -or
            $_.Extension -in @('.pdb','.obj','.iobj','.ipdb')
        })
    foreach ($rootForbidden in @('version.dll')) {
        if (Test-Path -LiteralPath (Join-Path $verificationRoot $rootForbidden)) {
            $forbidden += Get-Item -LiteralPath (Join-Path $verificationRoot $rootForbidden)
        }
    }
    if ($forbidden.Count -ne 0) {
        throw "Portable archive contains forbidden files: $($forbidden.FullName -join ', ')"
    }

    $developmentPattern = '(?i)([A-Z]:\\Users\\[^\\]+\\|\.worktrees\\|blind-soldier-source|Image File Execution Options|RegCreateKeyEx(?:A|W)?|RegSetValue(?:Ex)?(?:A|W)?|"Debugger"\s*:|(?:^|\s)/install(?:\s|$)|(?:^|\s)/uninstall(?:\s|$))'
    foreach ($file in @(Get-ChildItem -LiteralPath $verificationRoot -File `
            -Recurse | Where-Object Extension -In @('.txt','.json','.md'))) {
        $text = [IO.File]::ReadAllText($file.FullName)
        if ($text -match $developmentPattern) {
            throw "Portable text leaks a development path or obsolete registry workflow: $($file.FullName)"
        }
    }

    $modConfig = [IO.File]::ReadAllText((Join-Path $verificationRoot `
        'Reloaded-II\Mods\ff7.accessibility.reloaded\ModConfig.json')) |
        ConvertFrom-Json
    if ([string]$modConfig.ModId -cne 'ff7.accessibility.reloaded' -or
        [string]$modConfig.ModVersion -cne $ExpectedVersion -or
        (@($modConfig.ModDependencies) -join ',') -cne 'reloaded.sharedlib.hooks' -or
        (@($modConfig.SupportedAppId) -join ',') -cne
            'ff7_en.exe,ff7.exe,FFVII.exe') {
        throw 'Blind Soldier mod metadata or ordered application IDs are invalid.'
    }
    $hooksConfig = [IO.File]::ReadAllText((Join-Path $verificationRoot `
        'Reloaded-II\Mods\reloaded.sharedlib.hooks\ModConfig.json')) |
        ConvertFrom-Json
    if ([string]$hooksConfig.ModId -cne 'reloaded.sharedlib.hooks') {
        throw 'Shared Hooks metadata is invalid.'
    }

    $machines = [ordered]@{
        AccessibleLauncher = Assert-Machine -Path (Join-Path $verificationRoot `
            'FFVII_LAUNCHER.exe') -Expected 0x014C -Label 'accessible FFVII launcher'
        LauncherPrism = Assert-Machine -Path (Join-Path $verificationRoot `
            'launcher_accessibility\native\x86\FFVII_LAUNCHER.prism.x86.dll') `
            -Expected 0x014C -Label 'launcher Prism'
        BootstrapX86 = Assert-Machine -Path (Join-Path $verificationRoot `
            'Blind-Soldier\Bootstrap\x86\Blind-Soldier-Bootstrap-x86.exe') `
            -Expected 0x014C -Label 'x86 bootstrap'
        BootstrapX64 = Assert-Machine -Path (Join-Path $verificationRoot `
            'Blind-Soldier\Bootstrap\x64\Blind-Soldier-Bootstrap-x64.exe') `
            -Expected 0x8664 -Label 'x64 bootstrap'
        BootstrapperX86 = Assert-Machine -Path (Join-Path $verificationRoot `
            'Reloaded-II\Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') `
            -Expected 0x014C -Label 'x86 Reloaded bootstrapper'
        BootstrapperX64 = Assert-Machine -Path (Join-Path $verificationRoot `
            'Reloaded-II\Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') `
            -Expected 0x8664 -Label 'x64 Reloaded bootstrapper'
        LoaderX86 = Assert-Machine -Path (Join-Path $verificationRoot `
            'Reloaded-II\Loader\X86\Reloaded.Mod.Loader.dll') `
            -Expected 0x014C -Label 'x86 Reloaded loader'
        LoaderX64 = Assert-Machine -Path (Join-Path $verificationRoot `
            'Reloaded-II\Loader\X64\Reloaded.Mod.Loader.dll') `
            -Expected 0x8664 -Label 'x64 Reloaded loader'
        ModX86 = Assert-Machine -Path (Join-Path $verificationRoot `
            'Reloaded-II\Mods\ff7.accessibility.reloaded\x86\Ff7.Accessibility.Reloaded.dll') `
            -Expected 0x014C -Label 'x86 mod entry point'
        ModX64 = Assert-Machine -Path (Join-Path $verificationRoot `
            'Reloaded-II\Mods\ff7.accessibility.reloaded\x64\Ff7.Accessibility.Steam2026X64.dll') `
            -Expected 0x8664 -Label 'x64 mod entry point'
        HostFxrX86 = Assert-Machine -Path (Join-Path $verificationRoot `
            'Blind-Soldier\Runtime\dotnet\x86\host\fxr\9.0.8\hostfxr.dll') `
            -Expected 0x014C -Label 'x86 private hostfxr'
        HostFxrX64 = Assert-Machine -Path (Join-Path $verificationRoot `
            'Blind-Soldier\Runtime\dotnet\x64\host\fxr\9.0.8\hostfxr.dll') `
            -Expected 0x8664 -Label 'x64 private hostfxr'
        VersionProxy = Assert-Machine -Path (Join-Path $verificationRoot `
            'ff7_en.exe.local\version.dll') -Expected 0x014C `
            -Label 'x86 Blind Soldier Version proxy'
    }

    $peCount = 0
    foreach ($file in @(Get-ChildItem -LiteralPath $verificationRoot -File `
            -Recurse | Where-Object Extension -In @('.exe','.dll'))) {
        $relative = Get-RelativePortablePath -Root $verificationRoot `
            -Path $file.FullName
        $info = Get-PeInfo -Path $file.FullName
        $peCount++
        if ($file.Name -ceq 'FASM.DLL') {
            if ($info.Machine -ne 0x014C) {
                throw "Reloaded.Assembler FASM.DLL is not x86: $relative"
            }
            continue
        }
        if ($file.Name -ceq 'FASMX64.DLL') {
            if ($info.Machine -ne 0x8664) {
                throw "Reloaded.Assembler FASMX64.DLL is not x64: $relative"
            }
            continue
        }
        if ($relative -match '(?i)(?:^|/)x86(?:/|$)|Reloaded-II/Loader/X86/' -and
            $info.Machine -ne 0x014C) {
            throw "x86 package path contains a non-x86 PE: $relative"
        }
        if ($relative -match '(?i)(?:^|/)x64(?:/|$)|Reloaded-II/Loader/X64/' -and
            $info.Machine -ne 0x8664 -and
            -not ($info.Machine -eq 0x014C -and $info.HasClr)) {
            throw "x64 package path contains a wrong-architecture PE: $relative"
        }
    }

    $versionProxyPaths = @(
        'ff7_en.exe.local\version.dll', 'ff7.exe.local\version.dll',
        'ff7\workingdir\ff7_en.exe.local\version.dll',
        'ff7\workingdir\ff7.exe.local\version.dll') |
        ForEach-Object { Join-Path $verificationRoot $_ }
    $versionProxyHashes = @($versionProxyPaths | ForEach-Object {
        (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash
    } | Select-Object -Unique)
    if ($versionProxyHashes.Count -ne 1) {
        throw 'The four Blind Soldier Version proxy copies are not byte-identical.'
    }
    foreach ($proxy in $versionProxyPaths) {
        [void](Assert-Machine -Path $proxy -Expected 0x014C `
            -Label 'x86 Blind Soldier Version proxy')
    }
    $expectedVersionExports = @(
        'GetFileVersionInfoA', 'GetFileVersionInfoByHandle',
        'GetFileVersionInfoExA', 'GetFileVersionInfoExW',
        'GetFileVersionInfoSizeA', 'GetFileVersionInfoSizeExA',
        'GetFileVersionInfoSizeExW', 'GetFileVersionInfoSizeW',
        'GetFileVersionInfoW', 'VerFindFileA', 'VerFindFileW',
        'VerInstallFileA', 'VerInstallFileW', 'VerLanguageNameA',
        'VerLanguageNameW', 'VerQueryValueA', 'VerQueryValueW')
    $versionExports = @(Get-DumpbinExports -Dumpbin (Get-Dumpbin) `
        -Path $versionProxyPaths[0])
    if ($versionExports.Count -ne $expectedVersionExports.Count) {
        throw 'The Blind Soldier Version proxy does not export exactly 17 APIs.'
    }
    for ($index = 0; $index -lt $expectedVersionExports.Count; $index++) {
        $actual = $versionExports[$index]
        if ($actual.ordinal -ne ($index + 1) -or
            [string]$actual.name -cne $expectedVersionExports[$index]) {
            throw 'The Blind Soldier Version proxy export table is not an exact Windows Version match.'
        }
    }
    $versionDependencies = (& (Get-Dumpbin) /dependents $versionProxyPaths[0]) -join "`n"
    if ($versionDependencies -match '(?i)VCRUNTIME|MSVCP|ucrtbase') {
        throw 'The Blind Soldier Version proxy depends on a dynamic C runtime.'
    }

    $readme = [IO.File]::ReadAllText((Join-Path $verificationRoot `
        'README-PORTABLE.txt'))
    $expectedReadmeStart = "Blind Soldier $ExpectedVersion`r`n`r`n" +
        '1. Extract every file in this ZIP into your Final Fantasy VII game folder.' +
        "`r`n2. Start the game normally from Steam or 7th Heaven."
    if (-not $readme.StartsWith($expectedReadmeStart,
            [StringComparison]::Ordinal)) {
        throw 'README-PORTABLE.txt does not begin with the approved two-step instructions.'
    }

    [pscustomobject]@{
        ArchivePath=$archivePathFull
        Size=(Get-Item -LiteralPath $archivePathFull).Length
        Sha256=$archiveHash
        Version=[string]$manifest.version
        ManifestFiles=@($manifest.files).Count
        LoaderFiles=24
        PeFiles=$peCount
        ForbiddenFiles=0
        VersionProxySha256=$versionProxyHashes[0]
        VersionProxyExports=$versionExports.Count
        Machines=[pscustomobject]$machines
        ModId=[string]$modConfig.ModId
        SharedHooksId=[string]$hooksConfig.ModId
        SidecarVerified=$true
        DeterministicEntryOrder=$true
        SafeExtraction=$true
    }
}
finally {
    $temporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $verificationFull = [IO.Path]::GetFullPath($verificationRoot)
    if ((Test-Path -LiteralPath $verificationFull -PathType Container) -and
        $verificationFull.StartsWith($temporaryPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $verificationFull -Recurse -Force
    }
}
