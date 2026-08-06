[CmdletBinding()]
param(
    [string] $ToolRoot,
    [string] $ArchivePath,

    [Parameter(DontShow=$true)]
    [string] $ExpectedSha256 = 'B62E81A0390618466C019C60D8C2F796CED2509C4C1AEA4A37644A77272CF99D',

    [Parameter(DontShow=$true)]
    [string] $ExpectedRootName = 'ghidra_12.1.2_PUBLIC',

    [Parameter(DontShow=$true)]
    [string] $JavaPath = 'java.exe',

    [Parameter(DontShow=$true)]
    [scriptblock] $DownloadInvoker
)

$ErrorActionPreference = 'Stop'
$toolsScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $toolsScriptRoot
if ([string]::IsNullOrWhiteSpace($ToolRoot)) {
    $ToolRoot = Join-Path $repoRoot '.tools'
}
$ToolRoot = [IO.Path]::GetFullPath($ToolRoot)
$archiveName = 'ghidra_12.1.2_PUBLIC_20260605.zip'
$downloadUri = 'https://github.com/NationalSecurityAgency/ghidra/releases/download/Ghidra_12.1.2_build/ghidra_12.1.2_PUBLIC_20260605.zip'
if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
    $ArchivePath = Join-Path $ToolRoot "downloads\$archiveName"
}
$ArchivePath = [IO.Path]::GetFullPath($ArchivePath)
$destination = Join-Path $ToolRoot $ExpectedRootName
$markerName = '.blind-soldier-ghidra.json'

function Resolve-PinnedJava {
    param([Parameter(Mandatory=$true)] [string] $Candidate)
    $resolved = $null
    if ([IO.Path]::IsPathRooted($Candidate) -or
        $Candidate.Contains([IO.Path]::DirectorySeparatorChar) -or
        $Candidate.Contains([IO.Path]::AltDirectorySeparatorChar)) {
        if (Test-Path -LiteralPath $Candidate -PathType Leaf) {
            $resolved = [IO.Path]::GetFullPath($Candidate)
        }
    }
    else {
        $command = Get-Command $Candidate -CommandType Application `
            -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -ne $command) { $resolved = [string]$command.Source }
    }
    if ([string]::IsNullOrWhiteSpace($resolved)) {
        throw "Java is unavailable: $Candidate. Install a 64-bit JDK 21."
    }
    $priorPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $resolved -XshowSettings:properties -version 2>&1 |
            ForEach-Object { [string]$_ })
        $javaExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $priorPreference }
    if ($javaExitCode -ne 0) {
        throw "Java is unavailable or failed to start: $resolved"
    }
    $joined = $output -join "`n"
    if ($joined -notmatch '(?m)^\s*java\.version\s*=\s*21(?:\.|\s|$)' -and
        $joined -notmatch '(?m)^\s*(?:openjdk|java) version "21(?:\.|\")') {
        throw "Java $resolved is not JDK 21."
    }
    if ($joined -notmatch '(?m)^\s*sun\.arch\.data\.model\s*=\s*64\s*$') {
        throw "Java $resolved is not a 64-bit JDK."
    }
    return $resolved
}

function Assert-SafeGhidraArchive {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [string] $RootName
    )
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $seen = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $archive.Entries) {
            $name = ([string]$entry.FullName).Replace('\','/')
            if ([string]::IsNullOrWhiteSpace($name) -or
                $name.StartsWith('/') -or $name.StartsWith('\\') -or
                $name -match '^[A-Za-z]:' -or
                @($name.Split('/') | Where-Object { $_ -ceq '..' }).Count -gt 0) {
                throw "Pinned Ghidra archive contains an unsafe member: $name"
            }
            $trimmed = $name.TrimEnd('/')
            if ([string]::IsNullOrWhiteSpace($trimmed) -or
                -not ($trimmed -ceq $RootName -or
                    $trimmed.StartsWith($RootName + '/',
                        [StringComparison]::Ordinal))) {
                throw "Pinned Ghidra archive contains a member outside $RootName`: $name"
            }
            if (-not $seen.Add($trimmed)) {
                throw "Pinned Ghidra archive contains a duplicate member: $name"
            }
            $rawAttributes = [BitConverter]::ToUInt32(
                [BitConverter]::GetBytes([int]$entry.ExternalAttributes), 0)
            $dosAttributes = $rawAttributes -band 0xFFFF
            $unixMode = ($rawAttributes -shr 16) -band 0xF000
            if (($dosAttributes -band 0x0400) -ne 0 -or $unixMode -eq 0xA000) {
                throw "Pinned Ghidra archive contains a reparse or symbolic-link member: $name"
            }
        }
        return @($archive.Entries)
    }
    finally { $archive.Dispose() }
}

function Expand-SafeGhidraArchive {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [string] $StagingRoot,
        [Parameter(Mandatory=$true)] [string] $RootName
    )
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $prefix = [IO.Path]::GetFullPath($StagingRoot).TrimEnd('\') + '\'
        foreach ($entry in $archive.Entries) {
            $name = ([string]$entry.FullName).Replace('/','\')
            $target = [IO.Path]::GetFullPath((Join-Path $StagingRoot $name))
            if (-not $target.StartsWith($prefix,
                    [StringComparison]::OrdinalIgnoreCase)) {
                throw "Pinned Ghidra archive escaped staging: $name"
            }
            if ([string]::IsNullOrEmpty($entry.Name)) {
                New-Item -ItemType Directory -Path $target -Force | Out-Null
                continue
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
    }
    finally { $archive.Dispose() }
    $root = Join-Path $StagingRoot $RootName
    $headless = Join-Path $root 'support\analyzeHeadless.bat'
    if (-not (Test-Path -LiteralPath $headless -PathType Leaf)) {
        throw "Pinned Ghidra archive omits support\analyzeHeadless.bat."
    }
    return $root
}

New-Item -ItemType Directory -Path (Split-Path -Parent $ArchivePath) -Force |
    Out-Null
if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf)) {
    $download = $ArchivePath + '.download-' + [Guid]::NewGuid().ToString('N')
    try {
        if ($null -ne $DownloadInvoker) {
            & $DownloadInvoker $download $downloadUri
        }
        else {
            Invoke-WebRequest -UseBasicParsing -Uri $downloadUri -OutFile $download
        }
        if (-not (Test-Path -LiteralPath $download -PathType Leaf)) {
            throw 'Pinned Ghidra download did not produce an archive.'
        }
        Move-Item -LiteralPath $download -Destination $ArchivePath
    }
    finally {
        if (Test-Path -LiteralPath $download -PathType Leaf) {
            Remove-Item -LiteralPath $download -Force
        }
    }
}

$actualDigest = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash
if (-not $actualDigest.Equals($ExpectedSha256,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Pinned Ghidra archive failed SHA-256. Expected $ExpectedSha256; got $actualDigest."
}
$resolvedJava = Resolve-PinnedJava -Candidate $JavaPath
Assert-SafeGhidraArchive -Path $ArchivePath -RootName $ExpectedRootName |
    Out-Null

$existingMarker = Join-Path $destination $markerName
if (Test-Path -LiteralPath $destination -PathType Container) {
    try {
        $metadata = [IO.File]::ReadAllText($existingMarker) | ConvertFrom-Json
        $existingValid = [int]$metadata.schemaVersion -eq 1 -and
            ([string]$metadata.archiveSha256).Equals($ExpectedSha256,
                [StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath (Join-Path $destination `
                'support\analyzeHeadless.bat') -PathType Leaf)
    }
    catch { $existingValid = $false }
    if ($existingValid) {
        return [pscustomobject]@{
            GhidraRoot = $destination
            AnalyzeHeadlessPath = Join-Path $destination `
                'support\analyzeHeadless.bat'
            ArchivePath = $ArchivePath
            ArchiveSha256 = $actualDigest
            JavaPath = $resolvedJava
            Reused = $true
        }
    }
}

New-Item -ItemType Directory -Path $ToolRoot -Force | Out-Null
$staging = Join-Path $ToolRoot ('.ghidra-extract-' +
    [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $staging | Out-Null
    $stagedRoot = Expand-SafeGhidraArchive -Path $ArchivePath `
        -StagingRoot $staging -RootName $ExpectedRootName
    if (Test-Path -LiteralPath $destination) {
        $resolvedDestination = [IO.Path]::GetFullPath($destination)
        $toolPrefix = $ToolRoot.TrimEnd('\') + '\'
        if (-not $resolvedDestination.StartsWith($toolPrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to replace a Ghidra directory outside the tool root: $resolvedDestination"
        }
        Remove-Item -LiteralPath $resolvedDestination -Recurse -Force
    }
    Move-Item -LiteralPath $stagedRoot -Destination $destination
    [ordered]@{
        schemaVersion = 1
        release = '12.1.2'
        archiveName = [IO.Path]::GetFileName($ArchivePath)
        archiveSha256 = $actualDigest.ToUpperInvariant()
    } | ConvertTo-Json | Set-Content -LiteralPath $existingMarker `
        -Encoding utf8
}
finally {
    if (Test-Path -LiteralPath $staging -PathType Container) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}

[pscustomobject]@{
    GhidraRoot = $destination
    AnalyzeHeadlessPath = Join-Path $destination `
        'support\analyzeHeadless.bat'
    ArchivePath = $ArchivePath
    ArchiveSha256 = $actualDigest
    JavaPath = $resolvedJava
    Reused = $false
}
