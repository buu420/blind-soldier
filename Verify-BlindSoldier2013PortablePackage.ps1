[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $ArchivePath,
    [Parameter(Mandatory=$true)] [string] $ExpectedVersion,
    [string] $ExpectedSourceArchivePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$semanticVersionPattern = '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'
if ($ExpectedVersion -notmatch $semanticVersionPattern) {
    throw "Invalid semantic version: $ExpectedVersion"
}

$archive = [IO.Path]::GetFullPath($ArchivePath)
$sidecar = $archive + '.sha256'
if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
    throw "2013 portable archive is missing: $archive"
}
if (-not (Test-Path -LiteralPath $sidecar -PathType Leaf)) {
    throw "2013 portable archive checksum is missing: $sidecar"
}

function Assert-OrdinaryFile {
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

function Assert-SafeZipEntry {
    param(
        [IO.Compression.ZipArchiveEntry] $Entry,
        [string] $Label
    )
    $name = $Entry.FullName
    if ([string]::IsNullOrWhiteSpace($name) -or $name.Contains([char]0)) {
        throw "$Label contains an empty or invalid member."
    }
    $normalized = $name.Replace('\','/').TrimEnd('/')
    if ([string]::IsNullOrWhiteSpace($normalized) -or
        $normalized.StartsWith('/') -or $normalized.StartsWith('//') -or
        $normalized -match '^[A-Za-z]:' -or $normalized.Contains(':')) {
        throw "$Label contains a rooted or alternate-stream member: $name"
    }
    foreach ($part in $normalized.Split('/')) {
        if ([string]::IsNullOrWhiteSpace($part) -or $part -ceq '.' -or
            $part -ceq '..') {
            throw "$Label contains an unsafe path member: $name"
        }
        if ($part.EndsWith(' ') -or $part.EndsWith('.')) {
            throw "$Label contains an unsafe Windows path component: $name"
        }
    }
    $external = [BitConverter]::ToUInt32(
        [BitConverter]::GetBytes([int]$Entry.ExternalAttributes), 0)
    $unixType = ($external -shr 16) -band 0xF000
    if (($external -band [uint32][IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $unixType -eq 0xA000) {
        throw "$Label contains a reparse-point member: $name"
    }
    return $normalized
}

function Expand-SafeZip {
    param(
        [string] $Path,
        [string] $Destination,
        [string] $Label,
        [switch] $RequireDeterministicMetadata
    )
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $members = New-Object 'System.Collections.Generic.HashSet[string]' `
            ([StringComparer]::OrdinalIgnoreCase)
        $ordered = New-Object 'System.Collections.Generic.List[string]'
        $prefix = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
        $expectedTimestamp = New-Object DateTime(2000,1,1,0,0,0,
            [DateTimeKind]::Unspecified)
        foreach ($entry in $zip.Entries) {
            $relative = Assert-SafeZipEntry -Entry $entry -Label $Label
            if (-not $members.Add($relative)) {
                throw "$Label contains a case-insensitive duplicate member: $($entry.FullName)"
            }
            if ([string]::IsNullOrEmpty($entry.Name)) {
                throw "$Label contains an unnecessary directory member: $($entry.FullName)"
            }
            if ($RequireDeterministicMetadata) {
                if ($entry.LastWriteTime.DateTime -ne $expectedTimestamp) {
                    throw "$Label member has a non-deterministic timestamp: $relative"
                }
                if ($entry.ExternalAttributes -ne 0) {
                    throw "$Label member has non-deterministic external attributes: $relative"
                }
            }
            $ordered.Add($relative)
            $target = [IO.Path]::GetFullPath(
                (Join-Path $Destination $relative.Replace('/','\')))
            if (-not $target.StartsWith($prefix,
                    [StringComparison]::OrdinalIgnoreCase)) {
                throw "$Label member escaped verification staging: $($entry.FullName)"
            }
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) `
                -Force | Out-Null
            $input = $entry.Open()
            try {
                $output = [IO.File]::Open($target, [IO.FileMode]::CreateNew,
                    [IO.FileAccess]::Write, [IO.FileShare]::None)
                try { $input.CopyTo($output) }
                finally { $output.Dispose() }
            }
            finally { $input.Dispose() }
        }
        return @($ordered)
    }
    finally { $zip.Dispose() }
}

function Get-RelativePath {
    param([string] $Root, [string] $Path)
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($prefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "File escaped verification root: $full"
    }
    return $full.Substring($prefix.Length).Replace('\','/')
}

function Get-PeMachine {
    param([Parameter(Mandatory=$true)] [string] $Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 256 -or $bytes[0] -ne 0x4D -or
        $bytes[1] -ne 0x5A) {
        throw "Packaged executable is not a PE image: $Path"
    }
    $offset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($offset -lt 64 -or $offset + 24 -gt $bytes.Length -or
        [BitConverter]::ToUInt32($bytes, $offset) -ne 0x00004550) {
        throw "Packaged executable has an invalid PE header: $Path"
    }
    return [BitConverter]::ToUInt16($bytes, $offset + 4)
}

function Get-FileRecord {
    param([string] $Root, [string] $Path)
    $item = Assert-OrdinaryFile -Path $Path -Label 'Manifest file'
    [pscustomobject]@{
        path = Get-RelativePath -Root $Root -Path $item.FullName
        length = [int64]$item.Length
        sha256 = (Get-FileHash -LiteralPath $item.FullName `
            -Algorithm SHA256).Hash.ToUpperInvariant()
    }
}

$archiveHash = (Get-FileHash -LiteralPath $archive `
    -Algorithm SHA256).Hash.ToUpperInvariant()
$expectedSidecar = "$archiveHash  $([IO.Path]::GetFileName($archive))"
if ([IO.File]::ReadAllText($sidecar).Trim() -cne $expectedSidecar) {
    throw 'The 2013 portable SHA-256 sidecar does not match the archive.'
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) `
    ('blind-soldier-2013-verification-' + [Guid]::NewGuid().ToString('N'))
$packageRoot = Join-Path $temporaryRoot 'package'
$sourceRoot = Join-Path $temporaryRoot 'source'
try {
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    $entryNames = @(Expand-SafeZip -Path $archive -Destination $packageRoot `
        -Label '2013 portable ZIP' -RequireDeterministicMetadata)
    [string[]]$sortedNames = [string[]]$entryNames.Clone()
    [Array]::Sort($sortedNames, [StringComparer]::Ordinal)
    if (($entryNames -join '|') -cne ($sortedNames -join '|')) {
        throw '2013 portable ZIP entries are not in deterministic ordinal order.'
    }

    $required = @(
        'version.dll',
        'ff7_en.exe.local/version.dll',
        'ff7.exe.local/version.dll',
        'workingdir/version.dll',
        'workingdir/ff7_en.exe.local/version.dll',
        'workingdir/ff7.exe.local/version.dll',
        'Blind-Soldier/Bootstrap/x86/Blind-Soldier-Bootstrap-x86.exe',
        'Blind-Soldier/Runtime/dotnet/x86/host/fxr/9.0.8/hostfxr.dll',
        'Blind-Soldier/Runtime/dotnet/x86/shared/Microsoft.NETCore.App/9.0.8/coreclr.dll',
        'Blind-Soldier/Runtime/dotnet/x86/shared/Microsoft.WindowsDesktop.App/9.0.8/PresentationFramework.dll',
        'Blind-Soldier/Policy/BlindSoldier.ExternalOwnership.json',
        'Blind-Soldier/Policy/BlindSoldier.ExternalOwnership.psm1',
        'Blind-Soldier/Tools/Remove-AmethystRegistryEntries-Automatic.cmd',
        'Blind-Soldier/Tools/Remove-AmethystRegistryEntries.ps1',
        'Reloaded-II/Loader/X86/Bootstrapper/Reloaded.Mod.Loader.Bootstrapper.dll',
        'Reloaded-II/Loader/X86/Reloaded.Mod.Loader.dll',
        'Reloaded-II/Loader/X86/Reloaded.Mod.Loader.runtimeconfig.json',
        'Reloaded-II/Mods/ff7.accessibility.reloaded/ModConfig.json',
        'Reloaded-II/Mods/ff7.accessibility.reloaded/x86/Ff7.Accessibility.Reloaded.dll',
        'Reloaded-II/Mods/ff7.accessibility.reloaded/x86/prism.dll',
        'Reloaded-II/Mods/reloaded.sharedlib.hooks/ModConfig.json',
        'Reloaded-II/Mods/reloaded.sharedlib.hooks/x86/Reloaded.Hooks.ReloadedII.dll',
        'Reloaded-II/portable.txt',
        'LICENSES/dotnet-LICENSE.txt',
        'LICENSES/dotnet-THIRD-PARTY-NOTICES.txt',
        'LICENSES/Reloaded-II-1.30.3-Blind-Soldier-source.md',
        'LICENSES/Reloaded-II-1.30.3-hostfxr.patch',
        'LICENSES/FF7Tools-text-table-notice.md',
        'Remove-Amethyst-Registry-Entries.cmd',
        'README-2013-PORTABLE.txt',
        'portable-manifest.json'
    )
    foreach ($relative in $required) {
        if ($entryNames -cnotcontains $relative) {
            throw "2013 portable ZIP is missing required file: $relative"
        }
    }

    foreach ($relative in $entryNames) {
        $segments = $relative.Split('/')
        if ($segments -icontains 'x64' -or
            [IO.Path]::GetFileName($relative) -ieq 'FASMX64.DLL') {
            throw "2013 portable ZIP contains an x64 file: $relative"
        }
        if ($relative -ieq 'FFVII_LAUNCHER.exe' -or
            $relative -ieq 'FFVII_LAUNCHER.exe.config' -or
            $relative.StartsWith('launcher_accessibility/',
                [StringComparison]::OrdinalIgnoreCase) -or
            $relative.StartsWith('ff7/',
                [StringComparison]::OrdinalIgnoreCase) -or
            $relative -match '(?i)(^|/)(7th heaven|7thHeaven|FFNx)(/|$)' -or
            [IO.Path]::GetFileName($relative) -ieq 'dinput.dll' -or
            $relative -match '(?i)(^|/)(ff7_en|ff7|FFVII)\.exe$' -or
            $segments[0] -in @('data','direct','music','movies')) {
            throw "2013 portable ZIP contains a forbidden launcher, external-loader, or game file: $relative"
        }
    }

    $manifestPath = Join-Path $packageRoot 'portable-manifest.json'
    $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
    if ([int]$manifest.schemaVersion -ne 1 -or
        [string]$manifest.profile -cne 'legacy-x86' -or
        [string]$manifest.version -cne $ExpectedVersion -or
        [string]$manifest.sourceArchiveSha256 -notmatch '^[0-9A-F]{64}$') {
        throw '2013 portable manifest identity, version, or source provenance is invalid.'
    }

    $expectedFiles = @($entryNames | Where-Object {
        $_ -cne 'portable-manifest.json'
    })
    $manifestFiles = @($manifest.files)
    if ($manifestFiles.Count -ne $expectedFiles.Count) {
        throw '2013 portable manifest file count does not match the ZIP.'
    }
    for ($index = 0; $index -lt $expectedFiles.Count; $index++) {
        $relative = $expectedFiles[$index]
        $record = $manifestFiles[$index]
        if ([string]$record.path -cne $relative) {
            throw "2013 portable manifest path order mismatch at index $index."
        }
        $actual = Get-FileRecord -Root $packageRoot `
            -Path (Join-Path $packageRoot $relative.Replace('/','\'))
        if ([int64]$record.length -ne $actual.length -or
            [string]$record.sha256 -cne $actual.sha256) {
            throw "2013 portable manifest hash or length mismatch: $relative"
        }
    }

    $modConfig = [IO.File]::ReadAllText((Join-Path $packageRoot `
        'Reloaded-II\Mods\ff7.accessibility.reloaded\ModConfig.json')) |
        ConvertFrom-Json
    if ([string]$modConfig.ModId -cne 'ff7.accessibility.reloaded' -or
        [string]$modConfig.ModVersion -cne $ExpectedVersion) {
        throw 'Packaged Blind Soldier mod identity or version is incorrect.'
    }

    $proxyPaths = @(
        'version.dll',
        'ff7_en.exe.local/version.dll',
        'ff7.exe.local/version.dll',
        'workingdir/version.dll',
        'workingdir/ff7_en.exe.local/version.dll',
        'workingdir/ff7.exe.local/version.dll'
    )
    $proxyHashes = @($proxyPaths | ForEach-Object {
        (Get-FileHash -LiteralPath (Join-Path $packageRoot $_.Replace('/','\')) `
            -Algorithm SHA256).Hash.ToUpperInvariant()
    } | Select-Object -Unique)
    if ($proxyHashes.Count -ne 1) {
        throw 'The six 2013 Version proxy placements are not byte-identical.'
    }

    foreach ($file in Get-ChildItem -LiteralPath $packageRoot -File -Recurse |
            Where-Object Extension -In @('.dll','.exe')) {
        $machine = Get-PeMachine -Path $file.FullName
        if ($machine -ne 0x014C) {
            $relative = Get-RelativePath -Root $packageRoot -Path $file.FullName
            throw ("2013 portable executable has PE machine 0x{0:X4}, not x86: {1}" -f
                $machine, $relative)
        }
    }

    $bootstrapRelative =
        'Blind-Soldier/Bootstrap/x86/Blind-Soldier-Bootstrap-x86.exe'
    $bootstrapHash = (Get-FileHash -LiteralPath (Join-Path $packageRoot `
        $bootstrapRelative.Replace('/','\')) -Algorithm SHA256).Hash.ToUpperInvariant()
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSourceArchivePath)) {
        $sourceArchive = [IO.Path]::GetFullPath($ExpectedSourceArchivePath)
        [void](Assert-OrdinaryFile -Path $sourceArchive `
            -Label 'Expected dual-runtime source archive')
        $sourceHash = (Get-FileHash -LiteralPath $sourceArchive `
            -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($sourceHash -cne [string]$manifest.sourceArchiveSha256) {
            throw '2013 portable manifest does not identify the expected dual-runtime source archive.'
        }
        New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null
        [void](Expand-SafeZip -Path $sourceArchive -Destination $sourceRoot `
            -Label 'Expected dual-runtime source ZIP')
        $sourceProxy = Join-Path $sourceRoot 'ff7_en.exe.local\version.dll'
        $sourceBootstrap = Join-Path $sourceRoot `
            $bootstrapRelative.Replace('/','\')
        [void](Assert-OrdinaryFile -Path $sourceProxy `
            -Label 'Source x86 Version proxy')
        [void](Assert-OrdinaryFile -Path $sourceBootstrap `
            -Label 'Source x86 bootstrap')
        $sourceProxyHash = (Get-FileHash -LiteralPath $sourceProxy `
            -Algorithm SHA256).Hash.ToUpperInvariant()
        $sourceBootstrapHash = (Get-FileHash -LiteralPath $sourceBootstrap `
            -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($sourceProxyHash -cne $proxyHashes[0] -or
            $sourceBootstrapHash -cne $bootstrapHash) {
            throw '2013 portable proxy or bootstrap does not match the verified dual-runtime source archive.'
        }
    }

    [pscustomobject]@{
        Profile = 'legacy-x86'
        Version = $ExpectedVersion
        SourceArchiveSha256 = [string]$manifest.sourceArchiveSha256
        ArchiveSha256 = $archiveHash
        FileCount = $entryNames.Count
        VersionProxySha256 = $proxyHashes[0]
        BootstrapX86Sha256 = $bootstrapHash
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
