Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-PortableExactProperties {
    param([psobject] $Value, [string[]] $Expected, [string] $Label)
    $actual = @($Value.PSObject.Properties | ForEach-Object Name)
    if ($actual.Count -ne $Expected.Count) {
        throw "$Label must contain exactly: $($Expected -join ', ')."
    }
    foreach ($name in $Expected) {
        if (-not ($actual -ccontains $name)) {
            throw "$Label is missing property '$name'."
        }
    }
}

function Assert-PortableOrdinaryPath {
    param([string] $Path, [string] $Label, [switch] $Container)
    $pathType = if ($Container) { 'Container' } else { 'Leaf' }
    if (-not (Test-Path -LiteralPath $Path -PathType $pathType)) {
        throw "$Label is unavailable: $Path"
    }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label cannot be a reparse point: $Path"
    }
    return $item
}

function Assert-PortableArchiveMember {
    param([IO.Compression.ZipArchiveEntry] $Entry)
    $name = $Entry.FullName
    if ([string]::IsNullOrWhiteSpace($name) -or $name.Contains([char]0)) {
        throw 'Portable .NET archive contains an empty or invalid member.'
    }
    $normalized = $name.Replace('\', '/').TrimEnd('/')
    if ([string]::IsNullOrWhiteSpace($normalized) -or
        $normalized.StartsWith('/') -or $normalized.StartsWith('//') -or
        $normalized -match '^[A-Za-z]:' -or $normalized.Contains(':')) {
        throw "Portable .NET archive contains a rooted or alternate-stream member: $name"
    }
    foreach ($part in $normalized.Split('/')) {
        if ([string]::IsNullOrWhiteSpace($part) -or $part -ceq '.' -or
            $part -ceq '..') {
            throw "Portable .NET archive contains an unsafe path member: $name"
        }
    }
    $external = [BitConverter]::ToUInt32(
        [BitConverter]::GetBytes([int]$Entry.ExternalAttributes), 0)
    $unixType = ($external -shr 16) -band 0xF000
    if (($external -band [uint32][IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $unixType -eq 0xA000) {
        throw "Portable .NET archive contains a reparse-point member: $name"
    }
    return $normalized
}

function Get-PortableRuntimeRecords {
    param([string] $LockPath, [string] $Architecture)
    [void](Assert-PortableOrdinaryPath -Path $LockPath -Label 'Dependency lock')
    try {
        $lock = [IO.File]::ReadAllText([IO.Path]::GetFullPath($LockPath)) |
            ConvertFrom-Json
    }
    catch {
        throw "Dependency lock is invalid JSON: $($_.Exception.Message)"
    }
    if ([int]$lock.schemaVersion -ne 1 -or
        [string]$lock.dotnetDesktopRuntime.version -cne '9.0.8') {
        throw 'Dependency lock does not describe portable .NET 9.0.8.'
    }
    $archives = @($lock.dotnetDesktopRuntime.portableArchives)
    $keys = @($archives | ForEach-Object {
        "$($_.architecture)|$($_.component)"
    } | Sort-Object)
    $expectedKeys = @(
        'x64|core', 'x64|windowsDesktop',
        'x86|core', 'x86|windowsDesktop'
    )
    if ($archives.Count -ne 4 -or
        ($keys -join ',') -cne ($expectedKeys -join ',')) {
        throw 'Dependency lock must contain core and Windows Desktop portable archives for x86 and x64.'
    }
    foreach ($record in $archives) {
        Assert-PortableExactProperties -Value $record `
            -Expected @('architecture','component','name','url','sha512') `
            -Label ".NET $($record.architecture) $($record.component) portable archive"
        if ([string]$record.architecture -cnotmatch '^(x86|x64)$') {
            throw 'Portable archive contains an unsupported architecture.'
        }
        if ([string]$record.component -cnotmatch '^(core|windowsDesktop)$') {
            throw 'Portable archive contains an unsupported component.'
        }
        $name = [string]$record.name
        if ([string]::IsNullOrWhiteSpace($name) -or
            $name -cne [IO.Path]::GetFileName($name) -or
            [IO.Path]::GetExtension($name) -cne '.zip') {
            throw "Portable archive name is unsafe: $name"
        }
        $uri = $null
        if (-not [Uri]::TryCreate([string]$record.url,
                [UriKind]::Absolute, [ref]$uri) -or
            $uri.Scheme -cne 'https') {
            throw 'Portable archive URL must use HTTPS.'
        }
        if ([string]$record.sha512 -cnotmatch '^[0-9A-F]{128}$') {
            throw 'Portable archive SHA-512 must be uppercase hexadecimal.'
        }
        $expectedName = if ([string]$record.component -ceq 'core') {
            "dotnet-runtime-9.0.8-win-$($record.architecture).zip"
        }
        else {
            "windowsdesktop-runtime-9.0.8-win-$($record.architecture).zip"
        }
        if ($name -cne $expectedName) {
            throw "Portable archive name does not match its locked component: $name"
        }
    }
    return @($archives | Where-Object architecture -CEQ $Architecture |
        Sort-Object @{Expression={ if ($_.component -ceq 'core') { 0 } else { 1 } }})
}

function Expand-VerifiedPortableDotNetRuntime {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [ValidateSet('x86','x64')]
        [string] $Architecture,
        [Parameter(Mandatory=$true)] [string] $Destination,
        [Parameter(Mandatory=$true)] [string] $CachePath,
        [Parameter(Mandatory=$true)] [string] $LockPath
    )

    $records = @(Get-PortableRuntimeRecords -LockPath $LockPath `
        -Architecture $Architecture)
    $destinationPath = [IO.Path]::GetFullPath($Destination)
    if (Test-Path -LiteralPath $destinationPath) {
        throw "Portable runtime destination already exists: $destinationPath"
    }
    $destinationParent = Split-Path -Parent $destinationPath
    if ([string]::IsNullOrWhiteSpace($destinationParent)) {
        throw 'Portable runtime destination must have a parent directory.'
    }
    New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
    [void](Assert-PortableOrdinaryPath -Path $destinationParent `
        -Label 'Portable runtime destination parent' -Container)

    $cacheRoot = [IO.Path]::GetFullPath($CachePath)
    New-Item -ItemType Directory -Path $cacheRoot -Force | Out-Null
    [void](Assert-PortableOrdinaryPath -Path $cacheRoot `
        -Label 'Portable runtime cache' -Container)
    $archivePaths = New-Object 'System.Collections.Generic.List[string]'
    foreach ($record in $records) {
        $archivePath = Join-Path $cacheRoot ([string]$record.name)
        if (-not (Test-Path -LiteralPath $archivePath)) {
            $download = Join-Path $cacheRoot `
                ('.download-' + [Guid]::NewGuid().ToString('N'))
            try {
                Invoke-WebRequest -UseBasicParsing -Uri ([string]$record.url) `
                    -OutFile $download
                Move-Item -LiteralPath $download -Destination $archivePath
            }
            finally {
                if (Test-Path -LiteralPath $download -PathType Leaf) {
                    Remove-Item -LiteralPath $download -Force
                }
            }
        }
        [void](Assert-PortableOrdinaryPath -Path $archivePath `
            -Label "Portable $($record.component) runtime archive")
        $digest = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA512).Hash
        if (-not $digest.Equals([string]$record.sha512,
                [StringComparison]::Ordinal)) {
            throw "Portable runtime archive failed its locked SHA-512: $archivePath"
        }
        $archivePaths.Add($archivePath)
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $members = New-Object 'System.Collections.Generic.HashSet[string]' `
        ([StringComparer]::OrdinalIgnoreCase)
    foreach ($archivePath in $archivePaths) {
        $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
        try {
            foreach ($entry in $archive.Entries) {
                $normalized = Assert-PortableArchiveMember -Entry $entry
                if (-not $members.Add($normalized)) {
                    throw "Portable .NET archives contain a case-insensitive duplicate member: $($entry.FullName)"
                }
            }
        }
        finally { $archive.Dispose() }
    }
    foreach ($required in @(
        'dotnet.exe',
        'host/fxr/9.0.8/hostfxr.dll',
        'shared/Microsoft.NETCore.App/9.0.8/coreclr.dll',
        'shared/Microsoft.WindowsDesktop.App/9.0.8/PresentationFramework.dll',
        'LICENSE.txt',
        'ThirdPartyNotices.txt'
    )) {
        if (-not $members.Contains($required)) {
            throw "Portable .NET archive set omits required runtime file: $required"
        }
    }

    $staging = Join-Path $destinationParent `
        ('.blind-soldier-dotnet-' + [Guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Path $staging | Out-Null
        $prefix = [IO.Path]::GetFullPath($staging).TrimEnd('\') + '\'
        foreach ($archivePath in $archivePaths) {
            $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
            try {
                foreach ($entry in $archive.Entries) {
                    $normalized = Assert-PortableArchiveMember -Entry $entry
                    $target = [IO.Path]::GetFullPath(
                        (Join-Path $staging $normalized.Replace('/', '\')))
                    if (-not $target.StartsWith($prefix,
                            [StringComparison]::OrdinalIgnoreCase)) {
                        throw "Portable runtime member escaped staging: $($entry.FullName)"
                    }
                    if ([string]::IsNullOrEmpty($entry.Name)) {
                        New-Item -ItemType Directory -Path $target -Force | Out-Null
                        continue
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
        }
        foreach ($item in @(Get-Item -LiteralPath $staging -Force) +
                @(Get-ChildItem -LiteralPath $staging -Recurse -Force)) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Portable runtime extraction created a reparse point: $($item.FullName)"
            }
        }
        Move-Item -LiteralPath $staging -Destination $destinationPath
    }
    finally {
        if (Test-Path -LiteralPath $staging) {
            Remove-Item -LiteralPath $staging -Recurse -Force
        }
    }

    return Get-Item -LiteralPath $destinationPath
}

Export-ModuleMember -Function Expand-VerifiedPortableDotNetRuntime
