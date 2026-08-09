[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $SourceArchivePath,
    [Parameter(Mandatory=$true)] [string] $OutputPath,
    [Parameter(Mandatory=$true)] [string] $Version,
    [Parameter(DontShow=$true)] [scriptblock] $SourceVerifier
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$utf8 = New-Object Text.UTF8Encoding($false)
$semanticVersionPattern = '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'
if ($Version -notmatch $semanticVersionPattern) {
    throw "Invalid semantic version: $Version"
}

$sourceArchive = [IO.Path]::GetFullPath($SourceArchivePath)
$output = [IO.Path]::GetFullPath($OutputPath)
if (-not (Test-Path -LiteralPath $sourceArchive -PathType Leaf)) {
    throw "Dual-runtime source archive is missing: $sourceArchive"
}
if ([IO.Path]::GetExtension($output) -cne '.zip') {
    throw 'OutputPath must name a .zip file.'
}
if (Test-Path -LiteralPath $output) {
    throw "2013 portable package output already exists: $output"
}
$sidecar = $output + '.sha256'
if (Test-Path -LiteralPath $sidecar) {
    throw "2013 portable package checksum already exists: $sidecar"
}
$outputParent = Split-Path -Parent $output
if ([string]::IsNullOrWhiteSpace($outputParent)) {
    throw 'OutputPath must have a parent directory.'
}
New-Item -ItemType Directory -Path $outputParent -Force | Out-Null

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

function Assert-OrdinaryTree {
    param([string] $Path, [string] $Label)
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Label is missing: $Path"
    }
    foreach ($item in @((Get-Item -LiteralPath $Path -Force)) +
            @(Get-ChildItem -LiteralPath $Path -Force -Recurse)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Label cannot contain a reparse point: $($item.FullName)"
        }
    }
}

function Assert-SafeZipEntry {
    param([IO.Compression.ZipArchiveEntry] $Entry)
    $name = $Entry.FullName
    if ([string]::IsNullOrWhiteSpace($name) -or $name.Contains([char]0)) {
        throw 'Dual-runtime source ZIP contains an empty or invalid member.'
    }
    $normalized = $name.Replace('\','/').TrimEnd('/')
    if ([string]::IsNullOrWhiteSpace($normalized) -or
        $normalized.StartsWith('/') -or $normalized.StartsWith('//') -or
        $normalized -match '^[A-Za-z]:' -or $normalized.Contains(':')) {
        throw "Dual-runtime source ZIP contains a rooted or alternate-stream member: $name"
    }
    foreach ($part in $normalized.Split('/')) {
        if ([string]::IsNullOrWhiteSpace($part) -or $part -ceq '.' -or
            $part -ceq '..') {
            throw "Dual-runtime source ZIP contains an unsafe path member: $name"
        }
        if ($part.EndsWith(' ') -or $part.EndsWith('.')) {
            throw "Dual-runtime source ZIP contains an unsafe Windows path component: $name"
        }
    }
    $external = [BitConverter]::ToUInt32(
        [BitConverter]::GetBytes([int]$Entry.ExternalAttributes), 0)
    $unixType = ($external -shr 16) -band 0xF000
    if (($external -band [uint32][IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $unixType -eq 0xA000) {
        throw "Dual-runtime source ZIP contains a reparse-point member: $name"
    }
    return $normalized
}

function Expand-SafeSourceZip {
    param([string] $Path, [string] $Destination)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $members = New-Object 'System.Collections.Generic.HashSet[string]' `
            ([StringComparer]::OrdinalIgnoreCase)
        $prefix = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
        foreach ($entry in $archive.Entries) {
            $relative = Assert-SafeZipEntry -Entry $entry
            if (-not $members.Add($relative)) {
                throw "Dual-runtime source ZIP contains a case-insensitive duplicate member: $($entry.FullName)"
            }
            if ([string]::IsNullOrEmpty($entry.Name)) {
                throw "Dual-runtime source ZIP contains an unnecessary directory member: $($entry.FullName)"
            }
            $target = [IO.Path]::GetFullPath(
                (Join-Path $Destination $relative.Replace('/','\')))
            if (-not $target.StartsWith($prefix,
                    [StringComparison]::OrdinalIgnoreCase)) {
                throw "Dual-runtime source ZIP member escaped staging: $($entry.FullName)"
            }
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) `
                -Force | Out-Null
            $input = $entry.Open()
            try {
                $targetStream = [IO.File]::Open($target, [IO.FileMode]::CreateNew,
                    [IO.FileAccess]::Write, [IO.FileShare]::None)
                try { $input.CopyTo($targetStream) }
                finally { $targetStream.Dispose() }
            }
            finally { $input.Dispose() }
        }
    }
    finally { $archive.Dispose() }
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

function Copy-RequiredFile {
    param([string] $Source, [string] $Destination, [string] $Label)
    [void](Assert-OrdinaryFile -Path $Source -Label $Label)
    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) `
        -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Copy-RequiredTree {
    param([string] $Source, [string] $Destination, [string] $Label)
    Assert-OrdinaryTree -Path $Source -Label $Label
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $Destination `
            -Recurse -Force
    }
}

function Copy-CommonAndX86ModTree {
    param([string] $Source, [string] $Destination, [string] $Label)
    Assert-OrdinaryTree -Path $Source -Label $Label
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $Source -File -Recurse -Force) {
        $relative = Get-RelativePath -Root $Source -Path $file.FullName
        $first = $relative.Split('/')[0]
        if ($first -ieq 'x64' -or $file.Name -ieq 'FASMX64.DLL') {
            continue
        }
        $target = Join-Path $Destination $relative.Replace('/','\')
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) `
            -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
}

function Write-PortableManifest {
    param([string] $Root, [string] $SourceSha256)
    $map = New-Object 'System.Collections.Generic.Dictionary[string,string]' `
        ([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in Get-ChildItem -LiteralPath $Root -File -Recurse -Force) {
        $relative = Get-RelativePath -Root $Root -Path $file.FullName
        if ($relative -ceq 'portable-manifest.json') { continue }
        if ($map.ContainsKey($relative)) {
            throw "Duplicate 2013 portable path: $relative"
        }
        $map.Add($relative, $file.FullName)
    }
    [string[]]$paths = @($map.Keys)
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    $records = @($paths | ForEach-Object {
        $item = Get-Item -LiteralPath $map[$_]
        [ordered]@{
            path = $_
            length = [int64]$item.Length
            sha256 = (Get-FileHash -LiteralPath $item.FullName `
                -Algorithm SHA256).Hash.ToUpperInvariant()
        }
    })
    $manifest = [ordered]@{
        schemaVersion = 1
        profile = 'legacy-x86'
        version = $Version
        sourceArchiveSha256 = $SourceSha256
        files = $records
    }
    [IO.File]::WriteAllText((Join-Path $Root 'portable-manifest.json'),
        (($manifest | ConvertTo-Json -Depth 6) + "`n"), $utf8)
}

function New-DeterministicZip {
    param([string] $Root, [string] $Destination)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $map = New-Object 'System.Collections.Generic.Dictionary[string,string]' `
        ([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in Get-ChildItem -LiteralPath $Root -File -Recurse -Force) {
        $relative = Get-RelativePath -Root $Root -Path $file.FullName
        if ($map.ContainsKey($relative)) {
            throw "Duplicate 2013 archive path: $relative"
        }
        $map.Add($relative, $file.FullName)
    }
    [string[]]$paths = @($map.Keys)
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    $stream = New-Object IO.FileStream($Destination, [IO.FileMode]::CreateNew,
        [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $archive = New-Object IO.Compression.ZipArchive($stream,
            [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $timestamp = New-Object DateTimeOffset(2000,1,1,0,0,0,
                [TimeSpan]::Zero)
            foreach ($relative in $paths) {
                $entry = $archive.CreateEntry($relative,
                    [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $timestamp
                $entry.ExternalAttributes = 0
                $input = [IO.File]::OpenRead($map[$relative])
                try {
                    $target = $entry.Open()
                    try { $input.CopyTo($target) }
                    finally { $target.Dispose() }
                }
                finally { $input.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
}

if ($null -eq $SourceVerifier) {
    $dualVerifier = Join-Path $scriptRoot 'Verify-BlindSoldierPortablePackage.ps1'
    $SourceVerifier = {
        param($ArchivePath, $ExpectedVersion)
        & $dualVerifier -ArchivePath $ArchivePath `
            -ExpectedVersion $ExpectedVersion | Out-Null
    }.GetNewClosure()
}

$sourceHashBefore = (Get-FileHash -LiteralPath $sourceArchive `
    -Algorithm SHA256).Hash.ToUpperInvariant()
& $SourceVerifier $sourceArchive $Version | Out-Null
$sourceHashAfterVerification = (Get-FileHash -LiteralPath $sourceArchive `
    -Algorithm SHA256).Hash.ToUpperInvariant()
if ($sourceHashAfterVerification -cne $sourceHashBefore) {
    throw 'Dual-runtime source archive changed during verification.'
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) `
    ('blind-soldier-2013-portable-' + [Guid]::NewGuid().ToString('N'))
$extracted = Join-Path $temporaryRoot 'source'
$staging = Join-Path $temporaryRoot 'root'
$createdOutput = $false
$createdSidecar = $false
try {
    New-Item -ItemType Directory -Path $extracted, $staging -Force | Out-Null
    Expand-SafeSourceZip -Path $sourceArchive -Destination $extracted
    $sourceHashAfterExtraction = (Get-FileHash -LiteralPath $sourceArchive `
        -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($sourceHashAfterExtraction -cne $sourceHashBefore) {
        throw 'Dual-runtime source archive changed during extraction.'
    }
    Assert-OrdinaryTree -Path $extracted -Label 'Extracted dual-runtime source'

    $modConfigPath = Join-Path $extracted `
        'Reloaded-II\Mods\ff7.accessibility.reloaded\ModConfig.json'
    [void](Assert-OrdinaryFile -Path $modConfigPath `
        -Label 'Blind Soldier ModConfig.json')
    $modConfig = [IO.File]::ReadAllText($modConfigPath) | ConvertFrom-Json
    if ([string]$modConfig.ModId -cne 'ff7.accessibility.reloaded' -or
        [string]$modConfig.ModVersion -cne $Version) {
        throw 'Dual-runtime source mod identity or version does not match the requested 2013 package.'
    }

    foreach ($relative in @(
        'Blind-Soldier\Bootstrap\x86',
        'Blind-Soldier\Runtime\dotnet\x86',
        'Blind-Soldier\Policy',
        'Blind-Soldier\Tools',
        'Reloaded-II\Loader\X86',
        'Reloaded-II\Apps',
        'Reloaded-II\User',
        'Reloaded-II\Plugins',
        'LICENSES'
    )) {
        Copy-RequiredTree -Source (Join-Path $extracted $relative) `
            -Destination (Join-Path $staging $relative) -Label $relative
    }
    foreach ($modId in @('ff7.accessibility.reloaded',
            'reloaded.sharedlib.hooks')) {
        $relative = "Reloaded-II\Mods\$modId"
        Copy-CommonAndX86ModTree -Source (Join-Path $extracted $relative) `
            -Destination (Join-Path $staging $relative) -Label $relative
    }
    foreach ($relative in @(
        'Reloaded-II\portable.txt',
        'Remove-Amethyst-Registry-Entries.cmd'
    )) {
        Copy-RequiredFile -Source (Join-Path $extracted $relative) `
            -Destination (Join-Path $staging $relative) -Label $relative
    }

    $proxySource = Join-Path $extracted 'ff7_en.exe.local\version.dll'
    foreach ($relative in @(
        'version.dll',
        'ff7_en.exe.local\version.dll',
        'ff7.exe.local\version.dll',
        'workingdir\version.dll',
        'workingdir\ff7_en.exe.local\version.dll',
        'workingdir\ff7.exe.local\version.dll'
    )) {
        Copy-RequiredFile -Source $proxySource `
            -Destination (Join-Path $staging $relative) `
            -Label 'Ghidra-verified x86 Version proxy'
    }

    $readme = @"
Blind Soldier $Version - Final Fantasy VII 2013 x86

Choose this ZIP only for the 2013 x86 game or an x86 game launched through 7th Heaven/FFNx. The dual-runtime ZIP is for Steam 2026 x64 or people who need both game versions.

Direct 2013 launch
1. Close Final Fantasy VII and 7th Heaven.
2. Extract every file beside ff7_en.exe.
3. Start Final Fantasy VII normally.

7th Heaven / FFNx
1. Close Final Fantasy VII and 7th Heaven.
2. Extract at the directory that contains workingdir. If ff7_en.exe is already inside workingdir, extract one directory above workingdir.
3. Start the game normally with an unmodified official 7th Heaven and FFNx installation.

This archive contains no Steam 2026 launcher, x64 runtime, x64 Reloaded loader, FFNx file, 7th Heaven file, game executable, or game data. It does not replace dinput.dll or edit FFNx configuration.

Do not overwrite an unknown pre-existing version.dll. Move that file aside first so it can be restored. All version.dll copies in this ZIP belong to Blind Soldier and are byte-identical.

English, French, German, Spanish, and Japanese are supported. GameLanguage defaults to auto. Logs are written under Blind-Soldier\Logs. Players never run the bootstrap executable manually.

If an older Amethyst install left launch registry entries, run Remove-Amethyst-Registry-Entries.cmd once. New portable installs do not require registry changes.
"@
    [IO.File]::WriteAllText((Join-Path $staging `
        'README-2013-PORTABLE.txt'), ($readme.Trim() + "`r`n"), $utf8)

    Assert-OrdinaryTree -Path $staging -Label '2013 portable staging tree'
    Write-PortableManifest -Root $staging -SourceSha256 $sourceHashBefore
    New-DeterministicZip -Root $staging -Destination $output
    $createdOutput = $true
    $outputHash = (Get-FileHash -LiteralPath $output `
        -Algorithm SHA256).Hash.ToUpperInvariant()
    [IO.File]::WriteAllText($sidecar,
        "$outputHash  $([IO.Path]::GetFileName($output))`n", $utf8)
    $createdSidecar = $true

    $profileVerifier = Join-Path $scriptRoot `
        'Verify-BlindSoldier2013PortablePackage.ps1'
    & $profileVerifier -ArchivePath $output -ExpectedVersion $Version `
        -ExpectedSourceArchivePath $sourceArchive | Out-Null

    [pscustomobject]@{
        OutputPath = $output
        ChecksumPath = $sidecar
        Version = $Version
        Profile = 'legacy-x86'
        SourceArchiveSha256 = $sourceHashBefore
        ArchiveSha256 = $outputHash
    }
}
catch {
    if ($createdSidecar -and (Test-Path -LiteralPath $sidecar)) {
        Remove-Item -LiteralPath $sidecar -Force
    }
    if ($createdOutput -and (Test-Path -LiteralPath $output)) {
        Remove-Item -LiteralPath $output -Force
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
