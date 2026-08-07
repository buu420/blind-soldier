[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $ArchivePath,
    [Parameter(Mandatory=$true)] [string] $DestinationRoot,
    [string] $BackupRoot,
    [string] $ExpectedVersion = '0.1.6',
    [string] $VerifierPath,
    [string] $SupportedHostsPath,
    [string] $ReportPath,
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ownershipModulePath = Join-Path $scriptRoot `
    'BlindSoldier.ExternalOwnership.psm1'
Import-Module $ownershipModulePath -Force -ErrorAction Stop | Out-Null
$externalOwnershipPolicy = Import-BlindSoldierExternalOwnershipPolicy
if ([string]::IsNullOrWhiteSpace($VerifierPath)) {
    $VerifierPath = Join-Path (Split-Path -Parent $scriptRoot) `
        'Verify-BlindSoldierPortablePackage.ps1'
}

$utf8 = New-Object Text.UTF8Encoding($false)
$versionProxyPaths = @(
    'ff7_en.exe.local/version.dll',
    'ff7.exe.local/version.dll',
    'ff7/workingdir/ff7_en.exe.local/version.dll',
    'ff7/workingdir/ff7.exe.local/version.dll'
)

function Write-JsonUtf8 {
    param([string] $Path, [object] $Value)
    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    [IO.File]::WriteAllText($Path,
        (($Value | ConvertTo-Json -Depth 10) + "`n"), $utf8)
}

function Get-RelativePathSafe {
    param([string] $Root, [string] $Path)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $pathFull = [IO.Path]::GetFullPath($Path)
    if (-not $pathFull.StartsWith($rootFull,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escaped the expected root: $pathFull"
    }
    return $pathFull.Substring($rootFull.Length).Replace('\','/')
}

function Get-ExternalOwnershipSnapshot {
    param([Parameter(Mandatory=$true)] [string] $Root)
    $records = New-Object `
        'System.Collections.Generic.Dictionary[string,object]' `
        ([StringComparer]::OrdinalIgnoreCase)

    foreach ($relative in @(Get-BlindSoldierExternalOwnedFilePaths `
            -Policy $externalOwnershipPolicy)) {
        $path = Join-Path $Root ([string]$relative).Replace('/','\')
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $item = Get-Item -LiteralPath $path -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "A 7th Heaven or FFNx owned file is a reparse point: $path"
        }
        if ($item.PSIsContainer) {
            throw "A 7th Heaven or FFNx owned path is not a file: $path"
        }
        $key = ([string]$relative).Replace('\','/')
        if ($records.ContainsKey($key)) {
            throw "A 7th Heaven or FFNx ownership path collides: $key"
        }
        $records.Add($key, [pscustomobject][ordered]@{
            Type='File'
            RelativePath=$key
            Length=[int64]$item.Length
            Sha256=(Get-FileHash -LiteralPath $item.FullName `
                -Algorithm SHA256).Hash
        })
    }

    foreach ($relative in @(Get-BlindSoldierExternalOwnedDirectoryPaths `
            -Policy $externalOwnershipPolicy)) {
        $path = Join-Path $Root ([string]$relative).Replace('/','\')
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $rootItem = Get-Item -LiteralPath $path -Force
        if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "A 7th Heaven or FFNx owned directory is a reparse point: $path"
        }
        if (-not $rootItem.PSIsContainer) {
            throw "A 7th Heaven or FFNx owned directory is not a directory: $path"
        }
        $pending = New-Object 'System.Collections.Generic.Stack[object]'
        $pending.Push($rootItem)
        while ($pending.Count -gt 0) {
            $item = $pending.Pop()
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "A 7th Heaven or FFNx owned tree contains a reparse point: $($item.FullName)"
            }
            $key = Get-RelativePathSafe -Root $Root -Path $item.FullName
            if ($records.ContainsKey($key)) {
                throw "A 7th Heaven or FFNx ownership path collides: $key"
            }
            if ($item.PSIsContainer) {
                $records.Add($key, [pscustomobject][ordered]@{
                    Type='Directory'
                    RelativePath=$key
                    Length=$null
                    Sha256=$null
                })
                $children = @(Get-ChildItem -LiteralPath $item.FullName -Force |
                    Sort-Object FullName -Descending)
                foreach ($child in $children) { $pending.Push($child) }
            }
            else {
                $records.Add($key, [pscustomobject][ordered]@{
                    Type='File'
                    RelativePath=$key
                    Length=[int64]$item.Length
                    Sha256=(Get-FileHash -LiteralPath $item.FullName `
                        -Algorithm SHA256).Hash
                })
            }
        }
    }
    return @($records.Values | Sort-Object RelativePath)
}

function Assert-ExternalOwnershipUnchanged {
    param(
        [Parameter(Mandatory=$true)] [object[]] $Before,
        [Parameter(Mandatory=$true)] [object[]] $After
    )
    $beforeMap = New-Object `
        'System.Collections.Generic.Dictionary[string,object]' `
        ([StringComparer]::OrdinalIgnoreCase)
    $afterMap = New-Object `
        'System.Collections.Generic.Dictionary[string,object]' `
        ([StringComparer]::OrdinalIgnoreCase)
    foreach ($record in @($Before)) {
        if ($beforeMap.ContainsKey([string]$record.RelativePath)) {
            throw "External ownership snapshot contains a duplicate: $($record.RelativePath)"
        }
        $beforeMap.Add([string]$record.RelativePath, $record)
    }
    foreach ($record in @($After)) {
        if ($afterMap.ContainsKey([string]$record.RelativePath)) {
            throw "External ownership snapshot contains a duplicate: $($record.RelativePath)"
        }
        $afterMap.Add([string]$record.RelativePath, $record)
    }
    if ($beforeMap.Count -ne $afterMap.Count) {
        throw 'A 7th Heaven or FFNx owned path was added or removed during staging.'
    }
    foreach ($key in $beforeMap.Keys) {
        if (-not $afterMap.ContainsKey($key)) {
            throw "A 7th Heaven or FFNx owned path was removed during staging: $key"
        }
        $beforeRecord = $beforeMap[$key]
        $afterRecord = $afterMap[$key]
        if ([string]$beforeRecord.Type -cne [string]$afterRecord.Type) {
            throw "A 7th Heaven or FFNx owned path changed type during staging: $key"
        }
        if ([string]$beforeRecord.Type -ceq 'File' -and
            ([int64]$beforeRecord.Length -ne [int64]$afterRecord.Length -or
             -not ([string]$beforeRecord.Sha256).Equals(
                [string]$afterRecord.Sha256,
                [StringComparison]::OrdinalIgnoreCase))) {
            throw "A 7th Heaven or FFNx owned file changed during staging: $key"
        }
    }
}

function Assert-NoExternalArchiveMembers {
    param([Parameter(Mandatory=$true)] [string[]] $Members)
    foreach ($member in @($Members)) {
        if (Test-BlindSoldierExternalOwnedPath `
                -Policy $externalOwnershipPolicy `
                -RelativePath ([string]$member)) {
            throw "Portable ZIP cannot contain a 7th Heaven or FFNx external ownership path: $member"
        }
    }
}

function Assert-NotDriveRoot {
    param([string] $Path, [string] $Label)
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $drive = [IO.Path]::GetPathRoot($full).TrimEnd('\')
    if ($full.Equals($drive, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label cannot be a drive root: $full"
    }
    return $full
}

function Assert-NoReparseAncestor {
    param([string] $Path, [string] $Label)
    $current = [IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "$Label cannot use a reparse point: $($item.FullName)"
            }
        }
        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or
            $parent.Equals($current, [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $current = $parent
    }
}

function Resolve-CanonicalPathThroughExistingAncestors {
    param([string] $Path, [string] $Label)
    $current = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $missing = New-Object 'System.Collections.Generic.Stack[string]'
    while (-not (Test-Path -LiteralPath $current)) {
        $leaf = Split-Path -Leaf $current
        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($leaf) -or
            [string]::IsNullOrWhiteSpace($parent) -or
            $parent.Equals($current, [StringComparison]::OrdinalIgnoreCase)) {
            throw "$Label cannot be canonicalized: $Path"
        }
        $missing.Push($leaf)
        $current = $parent
    }
    Assert-NoReparseAncestor -Path $current -Label $Label
    $item = Get-Item -LiteralPath $current -Force
    $canonical = $item.FullName.TrimEnd('\')
    while ($missing.Count -gt 0) {
        $canonical = Join-Path $canonical $missing.Pop()
    }
    return [IO.Path]::GetFullPath($canonical).TrimEnd('\')
}

function Test-IsHardLinkItem {
    param([object] $Item)
    $linkTypeProperty = $Item.PSObject.Properties['LinkType']
    return $null -ne $linkTypeProperty -and
        [string]$linkTypeProperty.Value -ieq 'HardLink'
}

function Assert-SafeZipEntry {
    param([IO.Compression.ZipArchiveEntry] $Entry)
    $name = $Entry.FullName
    if ([string]::IsNullOrWhiteSpace($name) -or $name.Contains([char]0)) {
        throw 'Portable ZIP contains an empty or unsafe member.'
    }
    $normalized = $name.Replace('\','/').TrimEnd('/')
    if ([string]::IsNullOrWhiteSpace($normalized) -or
        $normalized.StartsWith('/') -or $normalized.StartsWith('//') -or
        $normalized -match '^[A-Za-z]:' -or $normalized.Contains(':')) {
        throw "Portable ZIP contains an unsafe rooted member: $name"
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
        throw "Portable ZIP contains an unsafe reparse-point member: $name"
    }
    if ([string]::IsNullOrEmpty($Entry.Name)) {
        throw "Portable ZIP contains an unnecessary directory member: $name"
    }
    return $normalized
}

function Expand-ValidatedArchive {
    param([string] $Path, [string] $Destination, [string] $Version)
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
                throw "Portable ZIP contains a duplicate member: $relative"
            }
            $target = [IO.Path]::GetFullPath(
                (Join-Path $Destination $relative.Replace('/','\')))
            if (-not $target.StartsWith($prefix,
                    [StringComparison]::OrdinalIgnoreCase)) {
                throw "Portable ZIP member escaped staging: $relative"
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
    }
    finally { $archive.Dispose() }

    $manifestPath = Join-Path $Destination 'portable-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw 'Portable ZIP is missing portable-manifest.json.'
    }
    $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
    if ([int]$manifest.schemaVersion -ne 1 -or
        [string]$manifest.version -cne $Version) {
        throw 'Portable ZIP manifest version does not match the requested version.'
    }

    $manifestMap = New-Object `
        'System.Collections.Generic.Dictionary[string,object]' `
        ([StringComparer]::OrdinalIgnoreCase)
    foreach ($record in @($manifest.files)) {
        $relative = [string]$record.path
        if ([string]::IsNullOrWhiteSpace($relative) -or
            -not $members.Contains($relative) -or
            $manifestMap.ContainsKey($relative)) {
            throw "Portable manifest contains an invalid file record: $relative"
        }
        $manifestMap.Add($relative, $record)
        $file = Join-Path $Destination $relative.Replace('/','\')
        $item = Get-Item -LiteralPath $file -Force
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
        if ([int64]$record.length -ne [int64]$item.Length -or
            -not $hash.Equals([string]$record.sha256,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Portable manifest does not match extracted file: $relative"
        }
    }
    if ($members.Count -ne $manifestMap.Count + 1 -or
        -not $members.Contains('portable-manifest.json')) {
        throw 'Portable ZIP members do not exactly match portable-manifest.json.'
    }
    return @($members | Sort-Object)
}

function Get-PeMachine {
    param([string] $Path)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open,
        [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        $reader = New-Object IO.BinaryReader($stream)
        try {
            if ($stream.Length -lt 256 -or $reader.ReadUInt16() -ne 0x5A4D) {
                throw "Host is not a PE image: $Path"
            }
            $stream.Position = 0x3C
            $offset = $reader.ReadInt32()
            if ($offset -lt 64 -or $offset + 24 -gt $stream.Length) {
                throw "Host has an invalid PE header: $Path"
            }
            $stream.Position = $offset
            if ($reader.ReadUInt32() -ne 0x00004550) {
                throw "Host has an invalid PE signature: $Path"
            }
            return $reader.ReadUInt16()
        }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Get-SupportedHostPolicy {
    if (-not [string]::IsNullOrWhiteSpace($SupportedHostsPath)) {
        if (-not (Test-Path -LiteralPath $SupportedHostsPath -PathType Leaf)) {
            throw "Supported host policy is missing: $SupportedHostsPath"
        }
        $policy = [IO.File]::ReadAllText(
            [IO.Path]::GetFullPath($SupportedHostsPath)) | ConvertFrom-Json
    }
    else {
        $policy = [pscustomobject]@{
            schemaVersion = 1
            hosts = @(
                [pscustomobject]@{
                    fileName='ff7_en.exe';machine=332
                    sha256='4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225'
                },
                [pscustomobject]@{
                    fileName='ff7.exe';machine=332
                    sha256='C1437392C5E4178765FBD238DCC9B33D86D2B97337310131C874F302236E4B6F'
                },
                [pscustomobject]@{
                    fileName='ff7.exe';machine=332
                    sha256='68CF1B8C1D732CC00A1DDB02CED161F7C94B06680D9E8641A11C7361417375C2'
                },
                [pscustomobject]@{
                    fileName='FFVII.exe';machine=34404
                    sha256='57A23D166D69E46B9E3339F779D4A3C4FEB402A989FA7291D0D9B4A1953ABB4B'
                }
            )
            stockLauncherSha256 =
                'B9CDAD3629703883EFC9D5C7427425CF6A8105746E674E4DD3DF783B4F044AEE'
            accessibleLauncherSha256 = @(
                '683F704F061D943A976D764233A6B3C290ACF9E5C1B150B7180A03224CA3A912',
                'F4F5651E86856306EF215A90C1EF6E2572BECF38A208AC9B569BD00F6B795E48'
            )
            accessibleVersionProxySha256 = @(
                '64E2803E3E321581FF0A58E64543BD082FFD6272941FEDB5BB3F14DCC79B7C90',
                'E46DC04803F56C880D7753003F7EED73754F6B2C07D1BCFB48BCCC4DE8AA8E82'
            )
        }
    }
    if ([int]$policy.schemaVersion -ne 1 -or @($policy.hosts).Count -eq 0 -or
        [string]$policy.stockLauncherSha256 -notmatch '^[0-9A-Fa-f]{64}$') {
        throw 'Supported host policy is invalid.'
    }
    $accessibleProperty = $policy.PSObject.Properties[
        'accessibleLauncherSha256']
    if ($null -ne $accessibleProperty) {
        foreach ($hash in @($accessibleProperty.Value)) {
            if ([string]$hash -notmatch '^[0-9A-Fa-f]{64}$') {
                throw 'Supported accessible launcher policy is invalid.'
            }
        }
    }
    $proxyProperty = $policy.PSObject.Properties[
        'accessibleVersionProxySha256']
    if ($null -ne $proxyProperty) {
        foreach ($hash in @($proxyProperty.Value)) {
            if ([string]$hash -notmatch '^[0-9A-Fa-f]{64}$') {
                throw 'Supported accessible Version proxy policy is invalid.'
            }
        }
    }
    return $policy
}

function Assert-SupportedGameRoot {
    param([string] $Root, [object] $Policy, [string] $PackageRoot)
    $accepted = New-Object 'System.Collections.Generic.List[object]'
    $presentHostNames = New-Object 'System.Collections.Generic.HashSet[string]' `
        ([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in @($Policy.hosts | ForEach-Object fileName |
            Select-Object -Unique)) {
        $candidate = Join-Path $Root ([string]$name)
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        [void]$presentHostNames.Add([string]$name)
        $hash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash
        $machine = Get-PeMachine -Path $candidate
        $match = @($Policy.hosts | Where-Object {
            [string]$_.fileName -ieq [string]$name -and
            [int]$_.machine -eq [int]$machine -and
            [string]$_.sha256 -ieq $hash
        } | Select-Object -First 1)
        if ($match.Count -eq 1) {
            $accepted.Add([pscustomobject]@{
                Path=$candidate;FileName=[string]$name;Machine=[int]$machine;Sha256=$hash
            })
        }
    }
    if ($accepted.Count -eq 0) {
        if ($presentHostNames.Count -gt 0) {
            throw 'The game executable is present but its hash or architecture is unsupported.'
        }
        throw 'The destination does not contain a supported Final Fantasy VII executable.'
    }

    if (@($accepted | Where-Object { $_.FileName -ieq 'FFVII.exe' -and
            $_.Machine -eq 34404 }).Count -gt 0) {
        $launcher = Join-Path $Root 'FFVII_LAUNCHER.exe'
        if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
            throw 'The x64 game launcher is missing.'
        }
        $launcherHash = (Get-FileHash -LiteralPath $launcher -Algorithm SHA256).Hash
        $packageLauncherPath = Join-Path $PackageRoot 'FFVII_LAUNCHER.exe'
        $packageLauncherHash = (Get-FileHash -LiteralPath $packageLauncherPath `
            -Algorithm SHA256).Hash
        $knownLauncherHashes = @(
            [string]$Policy.stockLauncherSha256,
            $packageLauncherHash
        )
        $accessibleProperty = $Policy.PSObject.Properties[
            'accessibleLauncherSha256']
        if ($null -ne $accessibleProperty) {
            $knownLauncherHashes += @($accessibleProperty.Value |
                ForEach-Object { [string]$_ })
        }
        $launcherRecognized = @($knownLauncherHashes | Where-Object {
            $launcherHash.Equals($_, [StringComparison]::OrdinalIgnoreCase)
        }).Count -gt 0
        if (-not $launcherRecognized) {
            throw 'The existing FFVII launcher is not recognized; refusing to replace it.'
        }
    }
    return $accepted.ToArray()
}

function Assert-TargetPathSegments {
    param([string] $Root, [string] $Relative)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    if (-not (Test-Path -LiteralPath $rootFull -PathType Container)) {
        throw "Portable overlay root disappeared: $rootFull"
    }
    Assert-NoReparseAncestor -Path $rootFull -Label 'Portable overlay root'
    $canonicalRoot = (Get-Item -LiteralPath $rootFull -Force).FullName.TrimEnd('\')
    if (-not $canonicalRoot.Equals($rootFull,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Portable overlay root changed identity: $rootFull"
    }
    $requested = $Relative.Replace('\','/').Trim('/')
    $current = $rootFull
    $parts = $requested.Replace('/','\').Split('\')
    for ($index = 0; $index -lt $parts.Length; $index++) {
        $candidate = Join-Path $current $parts[$index]
        if (Test-Path -LiteralPath $candidate) {
            $item = Get-Item -LiteralPath $candidate -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Portable overlay cannot cross a reparse point: $candidate"
            }
            if ($index -lt $parts.Length - 1 -and -not $item.PSIsContainer) {
                throw "Portable overlay parent is not a directory: $candidate"
            }
            if ($index -eq $parts.Length - 1 -and
                (Test-IsHardLinkItem -Item $item)) {
                throw "Portable overlay target is a hard link with external ownership risk: $candidate"
            }
            $current = $item.FullName
        }
        else {
            $current = [IO.Path]::GetFullPath($candidate)
        }
    }
    $canonicalRelative = Get-RelativePathSafe -Root $rootFull -Path $current
    if (-not $canonicalRelative.Equals($requested,
            [StringComparison]::OrdinalIgnoreCase)) {
        if (Test-BlindSoldierExternalOwnedPath `
                -Policy $externalOwnershipPolicy `
                -RelativePath $canonicalRelative) {
            throw "Portable overlay path is an alias for a 7th Heaven or FFNx external ownership path: $Relative"
        }
        throw "Portable overlay path uses a Windows alias for a different target: $Relative"
    }
    if (Test-BlindSoldierExternalOwnedPath `
            -Policy $externalOwnershipPolicy `
            -RelativePath $canonicalRelative) {
        throw "Portable overlay cannot target a 7th Heaven or FFNx external ownership path: $Relative"
    }
    return [IO.Path]::GetFullPath($current)
}

function New-PortableTargetParentDirectories {
    param(
        [string] $Root,
        [string] $Relative,
        [object] $CreatedDirectories,
        [object] $CreatedDirectorySet
    )
    $requested = $Relative.Replace('\','/').Trim('/')
    $parts = $requested.Split('/')
    $parentRelative = ''
    for ($index = 0; $index -lt $parts.Length - 1; $index++) {
        $parentRelative = if ([string]::IsNullOrEmpty($parentRelative)) {
            $parts[$index]
        }
        else { $parentRelative + '/' + $parts[$index] }
        $directory = Assert-TargetPathSegments -Root $Root `
            -Relative $parentRelative
        if (Test-Path -LiteralPath $directory) {
            if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
                throw "Portable overlay parent is not a directory: $directory"
            }
            continue
        }
        $createdItem = New-Item -ItemType Directory -Path $directory `
            -ErrorAction Stop
        $createdFull = [IO.Path]::GetFullPath($createdItem.FullName)
        if ($CreatedDirectorySet.Add($createdFull)) {
            $CreatedDirectories.Add($createdFull)
        }
        $validated = Assert-TargetPathSegments -Root $Root `
            -Relative $parentRelative
        if (-not $validated.Equals($createdFull,
                [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $validated -PathType Container)) {
            throw "Portable overlay parent changed identity: $directory"
        }
    }
    return Assert-TargetPathSegments -Root $Root -Relative $Relative
}
$archiveFull = [IO.Path]::GetFullPath($ArchivePath)
$destinationFull = Assert-NotDriveRoot -Path $DestinationRoot `
    -Label 'DestinationRoot'
if (-not (Test-Path -LiteralPath $destinationFull -PathType Container)) {
    throw "DestinationRoot is missing: $destinationFull"
}
Assert-NoReparseAncestor -Path $destinationFull -Label 'DestinationRoot'
$destinationFull = Resolve-CanonicalPathThroughExistingAncestors `
    -Path $destinationFull -Label 'DestinationRoot'

if (-not (Test-Path -LiteralPath $archiveFull -PathType Leaf)) {
    throw "Portable archive is missing: $archiveFull"
}
$archiveItem = Get-Item -LiteralPath $archiveFull -Force
if (($archiveItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'Portable archive cannot be a reparse point.'
}
$sidecar = $archiveFull + '.sha256'
if (-not (Test-Path -LiteralPath $sidecar -PathType Leaf)) {
    throw "Portable checksum is missing: $sidecar"
}
$actualArchiveHash = (Get-FileHash -LiteralPath $archiveFull -Algorithm SHA256).Hash
$sidecarText = [IO.File]::ReadAllText($sidecar).Trim()
$sidecarPattern = '^([0-9A-Fa-f]{64})  (.+)$'
if ($sidecarText -notmatch $sidecarPattern -or
    -not $actualArchiveHash.Equals($matches[1],
        [StringComparison]::OrdinalIgnoreCase) -or
    [string]$matches[2] -cne [IO.Path]::GetFileName($archiveFull)) {
    throw 'Portable checksum sidecar does not match the archive.'
}
if (-not (Test-Path -LiteralPath $VerifierPath -PathType Leaf)) {
    throw "Portable verifier is missing: $VerifierPath"
}

$destinationItems = @(Get-ChildItem -LiteralPath $destinationFull -Force)
if ($destinationItems.Count -gt 0 -and [string]::IsNullOrWhiteSpace($BackupRoot)) {
    throw 'A nonempty destination requires a BackupRoot ownership snapshot location.'
}
$backupFull = $null
if (-not [string]::IsNullOrWhiteSpace($BackupRoot)) {
    $backupFull = Assert-NotDriveRoot -Path $BackupRoot -Label 'BackupRoot'
    $backupFull = Resolve-CanonicalPathThroughExistingAncestors `
        -Path $backupFull -Label 'BackupRoot'
    $destinationPrefix = $destinationFull.TrimEnd('\') + '\'
    if ($backupFull.Equals($destinationFull,
            [StringComparison]::OrdinalIgnoreCase) -or
        $backupFull.StartsWith($destinationPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'BackupRoot cannot be inside DestinationRoot.'
    }
    if (Test-Path -LiteralPath $backupFull) {
        throw "BackupRoot must not already exist: $backupFull"
    }
    Assert-NoReparseAncestor -Path (Split-Path -Parent $backupFull) `
        -Label 'BackupRoot parent'
}

$extractRoot = Join-Path ([IO.Path]::GetTempPath()) `
    ('blind-soldier-live-stage-' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $extractRoot | Out-Null
    $members = @(Expand-ValidatedArchive -Path $archiveFull `
        -Destination $extractRoot -Version $ExpectedVersion)
    Assert-NoExternalArchiveMembers -Members $members

    $global:LASTEXITCODE = 0
    & $VerifierPath -ArchivePath $archiveFull -ExpectedVersion $ExpectedVersion |
        Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Portable package verifier failed with exit code $LASTEXITCODE."
    }

    $policy = Get-SupportedHostPolicy
    $acceptedHosts = @(Assert-SupportedGameRoot -Root $destinationFull `
        -Policy $policy -PackageRoot $extractRoot)

    $externalBefore = @(Get-ExternalOwnershipSnapshot -Root $destinationFull)
    $packageProxy = Join-Path $extractRoot 'ff7.exe.local\version.dll'
    $packageProxyHash = (Get-FileHash -LiteralPath $packageProxy `
        -Algorithm SHA256).Hash
    $knownProxyHashes = @($packageProxyHash)
    $proxyProperty = $policy.PSObject.Properties['accessibleVersionProxySha256']
    if ($null -ne $proxyProperty) {
        $knownProxyHashes += @($proxyProperty.Value | ForEach-Object {
            [string]$_
        })
    }
    foreach ($relative in $versionProxyPaths) {
        $existing = Join-Path $destinationFull $relative.Replace('/','\')
        if (Test-Path -LiteralPath $existing -PathType Leaf) {
            $existingHash = (Get-FileHash -LiteralPath $existing `
                -Algorithm SHA256).Hash
            $proxyRecognized = @($knownProxyHashes | Where-Object {
                $existingHash.Equals($_, [StringComparison]::OrdinalIgnoreCase)
            }).Count -gt 0
            if (-not $proxyRecognized) {
                throw "An unknown executable-local version.dll already exists: $existing"
            }
        }
    }

    $filePlans = New-Object 'System.Collections.Generic.List[object]'
    $canonicalTargets = New-Object `
        'System.Collections.Generic.HashSet[string]' `
        ([StringComparer]::OrdinalIgnoreCase)
    foreach ($relative in $members) {
        $source = Join-Path $extractRoot $relative.Replace('/','\')
        $target = Assert-TargetPathSegments -Root $destinationFull `
            -Relative $relative
        if (-not $canonicalTargets.Add($target)) {
            throw "Multiple portable members resolve to the same destination: $relative"
        }
        if (Test-Path -LiteralPath $target -PathType Container) {
            throw "A directory collides with a portable file: $target"
        }
        $exists = Test-Path -LiteralPath $target -PathType Leaf
        $beforeLength = $null
        $beforeHash = $null
        if ($exists) {
            $beforeItem = Get-Item -LiteralPath $target -Force
            if (($beforeItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "A portable target is a reparse point: $target"
            }
            $beforeLength = [int64]$beforeItem.Length
            $beforeHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        }
        $afterItem = Get-Item -LiteralPath $source
        $afterHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        $action = if (-not $exists) { 'Add' }
            elseif ($beforeHash.Equals($afterHash,
                    [StringComparison]::OrdinalIgnoreCase)) { 'Unchanged' }
            else { 'Replace' }
        $filePlans.Add([pscustomobject][ordered]@{
            RelativePath=$relative
            Action=$action
            BeforeExists=[bool]$exists
            BeforeLength=$beforeLength
            BeforeSha256=$beforeHash
            AfterLength=[int64]$afterItem.Length
            AfterSha256=$afterHash
        })
    }

    $report = [pscustomobject][ordered]@{
        SchemaVersion=1
        Version=$ExpectedVersion
        Operation=if ($DryRun) { 'DryRun' } else { 'Applied' }
        ArchivePath=$archiveFull
        ArchiveSha256=$actualArchiveHash
        DestinationRoot=$destinationFull
        BackupRoot=$backupFull
        RegistryMutation=$false
        AcceptedHosts=$acceptedHosts
        Files=@($filePlans.ToArray())
        ExternalFiles=@($externalBefore)
    }

    if ($DryRun) {
        $externalAfter = @(Get-ExternalOwnershipSnapshot -Root $destinationFull)
        Assert-ExternalOwnershipUnchanged -Before $externalBefore `
            -After $externalAfter
        if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
            Write-JsonUtf8 -Path ([IO.Path]::GetFullPath($ReportPath)) `
                -Value $report
        }
        return $report
    }

    $backupFilesRoot = Join-Path $backupFull 'files'
    New-Item -ItemType Directory -Path $backupFilesRoot -Force | Out-Null
    Assert-NoReparseAncestor -Path $backupFull -Label 'BackupRoot'
    $canonicalBackupFull = Resolve-CanonicalPathThroughExistingAncestors `
        -Path $backupFull -Label 'BackupRoot'
    if (-not $canonicalBackupFull.Equals($backupFull,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "BackupRoot changed identity: $backupFull"
    }
    $snapshot = [ordered]@{
        schemaVersion=1
        version=$ExpectedVersion
        createdUtc=[DateTime]::UtcNow.ToString('o')
        archiveSha256=$actualArchiveHash
        destinationRoot=$destinationFull
        files=@($filePlans | ForEach-Object {
            [ordered]@{
                path=$_.RelativePath
                existed=$_.BeforeExists
                length=$_.BeforeLength
                sha256=$_.BeforeSha256
                action=$_.Action
            }
        })
    }
    foreach ($plan in @($filePlans | Where-Object {
            $_.BeforeExists -and $_.Action -eq 'Replace' })) {
        $source = Assert-TargetPathSegments -Root $destinationFull `
            -Relative $plan.RelativePath
        $currentItem = Get-Item -LiteralPath $source -Force
        $currentHash = (Get-FileHash -LiteralPath $source `
            -Algorithm SHA256).Hash
        if ([int64]$currentItem.Length -ne [int64]$plan.BeforeLength -or
            -not $currentHash.Equals([string]$plan.BeforeSha256,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "A portable target changed after planning: $($plan.RelativePath)"
        }
        $backup = Assert-TargetPathSegments -Root $backupFilesRoot `
            -Relative $plan.RelativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $backup) `
            -Force | Out-Null
        $backup = Assert-TargetPathSegments -Root $backupFilesRoot `
            -Relative $plan.RelativePath
        [IO.File]::Copy($source, $backup, $false)
    }
    $snapshotPath = Assert-TargetPathSegments -Root $backupFull `
        -Relative 'ownership-snapshot.json'
    Write-JsonUtf8 -Path $snapshotPath -Value $snapshot

    $externalBeforeCopy = @(Get-ExternalOwnershipSnapshot -Root $destinationFull)
    Assert-ExternalOwnershipUnchanged -Before $externalBefore `
        -After $externalBeforeCopy
    $applied = New-Object 'System.Collections.Generic.List[object]'
    $createdDirectories = New-Object `
        'System.Collections.Generic.List[string]'
    $createdDirectorySet = New-Object `
        'System.Collections.Generic.HashSet[string]' `
        ([StringComparer]::OrdinalIgnoreCase)
    try {
        $copyCanonicalTargets = New-Object `
            'System.Collections.Generic.HashSet[string]' `
            ([StringComparer]::OrdinalIgnoreCase)
        foreach ($plan in @($filePlans | Where-Object Action -ne 'Unchanged')) {
            $source = Join-Path $extractRoot $plan.RelativePath.Replace('/','\')
            $target = Assert-TargetPathSegments -Root $destinationFull `
                -Relative $plan.RelativePath
            if (-not $copyCanonicalTargets.Add($target)) {
                throw "Multiple portable members resolve to the same live destination: $($plan.RelativePath)"
            }
            $target = New-PortableTargetParentDirectories `
                -Root $destinationFull -Relative $plan.RelativePath `
                -CreatedDirectories $createdDirectories `
                -CreatedDirectorySet $createdDirectorySet
            if ($plan.BeforeExists) {
                if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
                    throw "A portable target disappeared after planning: $($plan.RelativePath)"
                }
                $currentItem = Get-Item -LiteralPath $target -Force
                $currentHash = (Get-FileHash -LiteralPath $target `
                    -Algorithm SHA256).Hash
                if ([int64]$currentItem.Length -ne [int64]$plan.BeforeLength -or
                    -not $currentHash.Equals([string]$plan.BeforeSha256,
                        [StringComparison]::OrdinalIgnoreCase)) {
                    throw "A portable target changed after planning: $($plan.RelativePath)"
                }
            }
            elseif (Test-Path -LiteralPath $target) {
                throw "A portable target appeared after planning: $($plan.RelativePath)"
            }
            $applied.Add($plan)
            [IO.File]::Copy($source, $target, $true)
        }
        $externalAfter = @(Get-ExternalOwnershipSnapshot -Root $destinationFull)
        Assert-ExternalOwnershipUnchanged -Before $externalBefore `
            -After $externalAfter
    }
    catch {
        $primaryFailure = $_
        $rollbackFailures = New-Object 'System.Collections.Generic.List[string]'
        for ($index = $applied.Count - 1; $index -ge 0; $index--) {
            $plan = $applied[$index]
            try {
                $target = Assert-TargetPathSegments -Root $destinationFull `
                    -Relative $plan.RelativePath
                if ($plan.BeforeExists) {
                    $alreadyOriginal = $false
                    if (Test-Path -LiteralPath $target -PathType Leaf) {
                        $currentItem = Get-Item -LiteralPath $target -Force
                        $currentHash = (Get-FileHash -LiteralPath $target `
                            -Algorithm SHA256).Hash
                        $alreadyOriginal =
                            [int64]$currentItem.Length -eq [int64]$plan.BeforeLength -and
                            $currentHash.Equals([string]$plan.BeforeSha256,
                                [StringComparison]::OrdinalIgnoreCase)
                    }
                    if (-not $alreadyOriginal) {
                        $backup = Assert-TargetPathSegments `
                            -Root $backupFilesRoot `
                            -Relative $plan.RelativePath
                        [IO.File]::Copy($backup, $target, $true)
                    }
                }
                elseif (Test-Path -LiteralPath $target -PathType Leaf) {
                    [IO.File]::Delete($target)
                }
            }
            catch {
                $rollbackFailures.Add(
                    "$($plan.RelativePath): $($_.Exception.Message)")
            }
        }
        for ($index = $createdDirectories.Count - 1; $index -ge 0;
             $index--) {
            $directory = $createdDirectories[$index]
            try {
                if (-not (Test-Path -LiteralPath $directory)) { continue }
                $relative = Get-RelativePathSafe -Root $destinationFull `
                    -Path $directory
                $validated = Assert-TargetPathSegments `
                    -Root $destinationFull -Relative $relative
                if (-not $validated.Equals($directory,
                        [StringComparison]::OrdinalIgnoreCase) -or
                    -not (Test-Path -LiteralPath $validated `
                        -PathType Container)) {
                    throw "Created directory changed identity: $directory"
                }
                [IO.Directory]::Delete($validated, $false)
            }
            catch {
                $rollbackFailures.Add(
                    "$directory`: $($_.Exception.Message)")
            }
        }
        try {
            $externalAfterRollback = @(
                Get-ExternalOwnershipSnapshot -Root $destinationFull)
            Assert-ExternalOwnershipUnchanged -Before $externalBefore `
                -After $externalAfterRollback
        }
        catch {
            $rollbackFailures.Add(
                "external ownership: $($_.Exception.Message)")
        }
        if ($rollbackFailures.Count -gt 0) {
            throw ("Portable staging failed: {0} Rollback could not complete: {1}" -f
                $primaryFailure.Exception.Message,
                ($rollbackFailures -join '; '))
        }
        throw $primaryFailure
    }
    if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
        Write-JsonUtf8 -Path ([IO.Path]::GetFullPath($ReportPath)) `
            -Value $report
    }
    return $report
}
finally {
    if (Test-Path -LiteralPath $extractRoot) {
        Remove-Item -LiteralPath $extractRoot -Recurse -Force
    }
}
