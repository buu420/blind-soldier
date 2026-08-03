Set-StrictMode -Version 2.0

$script:Steam2013AppId = '39140'
$script:Steam2026AppId = '3837340'
$script:Steam2026SourceSha1 = 'AC306AE92615AF75FF36BBA6347C67CA1284151D'
$script:Steam2026LargeAddressSha1 = 'D270E690A0EA2C9D57AF506D102CF1A794E2ADCD'
$script:Steam2026NativeSha256 = '57A23D166D69E46B9E3339F779D4A3C4FEB402A989FA7291D0D9B4A1953ABB4B'
$script:Steam2026NativeRuntimeId = 'ff7-steam-2026-x64'
$script:RequiredNativeCapabilities = @(
    'Lifecycle',
    'ForegroundInput',
    'Menus',
    'Dialogue',
    'Field',
    'Navigation',
    'Battle',
    'Movies',
    'Saves'
)
$script:ModuleRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Get-CanonicalExistingDirectory {
    param([Parameter(Mandatory=$true)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Directory is unavailable: $Path"
    }

    return [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path).Path).TrimEnd('\')
}

function ConvertFrom-ValveEscapedString {
    param([Parameter(Mandatory=$true)] [string] $Value)

    return $Value.Replace('\\', '\').Replace('\"', '"')
}

function Get-Ff7SteamLibraryPaths {
    [CmdletBinding()]
    param([Parameter(Mandatory=$true)] [string] $SteamRoot)

    $resolvedSteamRoot = Get-CanonicalExistingDirectory -Path $SteamRoot
    $libraryFile = Join-Path $resolvedSteamRoot 'steamapps\libraryfolders.vdf'
    if (-not (Test-Path -LiteralPath $libraryFile -PathType Leaf)) {
        throw "Steam library registry is missing: $libraryFile"
    }

    $ordered = New-Object System.Collections.Generic.List[string]
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

    if ($seen.Add($resolvedSteamRoot)) {
        $ordered.Add($resolvedSteamRoot)
    }

    $content = [IO.File]::ReadAllText($libraryFile)
    $matches = [regex]::Matches($content, '"path"\s*"(?<path>(?:\\.|[^"\\])*)"', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    foreach ($match in $matches) {
        $rawPath = ConvertFrom-ValveEscapedString -Value $match.Groups['path'].Value
        if (-not (Test-Path -LiteralPath $rawPath -PathType Container)) {
            continue
        }

        $resolved = Get-CanonicalExistingDirectory -Path $rawPath
        if ($seen.Add($resolved)) {
            $ordered.Add($resolved)
        }
    }

    return $ordered.ToArray()
}

function Get-SteamManifestInstallDirectory {
    param([Parameter(Mandatory=$true)] [string] $ManifestPath)

    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        return $null
    }

    $content = [IO.File]::ReadAllText($ManifestPath)
    $match = [regex]::Match($content, '"installdir"\s*"(?<dir>(?:\\.|[^"\\])*)"', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $match.Success) {
        throw "Steam manifest does not contain installdir: $ManifestPath"
    }

    return ConvertFrom-ValveEscapedString -Value $match.Groups['dir'].Value
}

function New-Ff7InstallationResult {
    param(
        [Parameter(Mandatory=$true)] [ValidateSet('Steam2013', 'Steam2026')] [string] $Version,
        [Parameter(Mandatory=$true)] [string] $GameRoot
    )

    $resolvedRoot = Get-CanonicalExistingDirectory -Path $GameRoot
    if ($Version -eq 'Steam2026') {
        $legacyRuntimeRoot = Join-Path $resolvedRoot 'ff7\workingdir'
        $legacyGameExe = Join-Path $legacyRuntimeRoot 'ff7_en.exe'
        $nativeGameExe = Join-Path $resolvedRoot 'FFVII.exe'
        return [pscustomobject]@{
            Version = 'Steam2026'
            SteamAppId = $script:Steam2026AppId
            GameRoot = $resolvedRoot
            RuntimeRoot = $legacyRuntimeRoot
            GameExe = $legacyGameExe
            LauncherExe = Join-Path $resolvedRoot 'FFVII_LAUNCHER.exe'
            SourceExe = Join-Path $resolvedRoot 'ff7\resources\ff7_1.02\ff7_en'
            LegacyRuntime = [pscustomobject]@{
                RuntimeId = 'ff7-steam-legacy-x86'
                Architecture = 'x86'
                RuntimeRoot = $legacyRuntimeRoot
                GameExe = $legacyGameExe
            }
            NativeRuntime = [pscustomobject]@{
                RuntimeId = $script:Steam2026NativeRuntimeId
                Architecture = 'x64'
                RuntimeRoot = $resolvedRoot
                GameExe = $nativeGameExe
            }
        }
    }

    $legacyGameExe = Join-Path $resolvedRoot 'ff7_en.exe'
    return [pscustomobject]@{
        Version = 'Steam2013'
        SteamAppId = $script:Steam2013AppId
        GameRoot = $resolvedRoot
        RuntimeRoot = $resolvedRoot
        GameExe = $legacyGameExe
        LauncherExe = Join-Path $resolvedRoot 'FF7_Launcher.exe'
        SourceExe = $null
        LegacyRuntime = [pscustomobject]@{
            RuntimeId = 'ff7-steam-legacy-x86'
            Architecture = 'x86'
            RuntimeRoot = $resolvedRoot
            GameExe = $legacyGameExe
        }
        NativeRuntime = $null
    }
}

function Get-Ff7InstallationAtRoot {
    param(
        [Parameter(Mandatory=$true)] [string] $GameRoot,
        [string] $ExpectedAppId
    )

    if (-not (Test-Path -LiteralPath $GameRoot -PathType Container)) {
        return $null
    }

    $is2026 =
        (Test-Path -LiteralPath (Join-Path $GameRoot 'FFVII.exe') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $GameRoot 'FFVII_LAUNCHER.exe') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $GameRoot 'steam_api64.dll') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $GameRoot 'ff7\resources\ff7_1.02\ff7_en') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $GameRoot 'ff7\workingdir\data') -PathType Container)

    if ($is2026 -and ([string]::IsNullOrWhiteSpace($ExpectedAppId) -or $ExpectedAppId -eq $script:Steam2026AppId)) {
        return New-Ff7InstallationResult -Version Steam2026 -GameRoot $GameRoot
    }

    $is2013 =
        (Test-Path -LiteralPath (Join-Path $GameRoot 'ff7_en.exe') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $GameRoot 'FF7_Launcher.exe') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $GameRoot 'data') -PathType Container)

    if ($is2013 -and ([string]::IsNullOrWhiteSpace($ExpectedAppId) -or $ExpectedAppId -eq $script:Steam2013AppId)) {
        return New-Ff7InstallationResult -Version Steam2013 -GameRoot $GameRoot
    }

    return $null
}

function Get-RegisteredSteamRoot {
    $registryPaths = @(
        'HKCU:\Software\Valve\Steam',
        'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam',
        'HKLM:\SOFTWARE\Valve\Steam'
    )

    foreach ($registryPath in $registryPaths) {
        $properties = Get-ItemProperty -Path $registryPath -ErrorAction SilentlyContinue
        if ($null -eq $properties) {
            continue
        }

        foreach ($name in @('SteamPath', 'InstallPath')) {
            $value = $properties.$name
            if (-not [string]::IsNullOrWhiteSpace($value) -and (Test-Path -LiteralPath $value -PathType Container)) {
                return Get-CanonicalExistingDirectory -Path $value
            }
        }
    }

    throw 'Steam installation could not be found in the registry.'
}

function Resolve-Ff7Installation {
    [CmdletBinding()]
    param(
        [string] $GameRoot,
        [string] $SteamRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($GameRoot)) {
        if (-not (Test-Path -LiteralPath $GameRoot -PathType Container)) {
            throw "FFVII game root is unavailable: $GameRoot"
        }

        $explicit = Get-Ff7InstallationAtRoot -GameRoot $GameRoot
        if ($null -eq $explicit) {
            throw "Directory is not a supported FFVII installation: $GameRoot"
        }

        return $explicit
    }

    if ([string]::IsNullOrWhiteSpace($SteamRoot)) {
        $SteamRoot = Get-RegisteredSteamRoot
    }

    $installs = New-Object System.Collections.Generic.List[object]
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($library in @(Get-Ff7SteamLibraryPaths -SteamRoot $SteamRoot)) {
        foreach ($appId in @($script:Steam2013AppId, $script:Steam2026AppId)) {
            $manifest = Join-Path $library ("steamapps\appmanifest_{0}.acf" -f $appId)
            $installDir = Get-SteamManifestInstallDirectory -ManifestPath $manifest
            if ([string]::IsNullOrWhiteSpace($installDir)) {
                continue
            }

            $candidateRoot = Join-Path $library ("steamapps\common\{0}" -f $installDir)
            $candidate = Get-Ff7InstallationAtRoot -GameRoot $candidateRoot -ExpectedAppId $appId
            if ($null -ne $candidate -and $seen.Add($candidate.GameRoot)) {
                $installs.Add($candidate)
            }
        }
    }

    if ($installs.Count -eq 0) {
        throw 'No supported FFVII Steam installation was found.'
    }

    if ($installs.Count -gt 1) {
        $roots = ($installs | ForEach-Object { $_.GameRoot }) -join '; '
        throw "Multiple supported FFVII installations were found. Pass -GameRoot explicitly. Found: $roots"
    }

    return $installs[0]
}

function Get-Ff7PeMachine {
    param([Parameter(Mandatory=$true)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "PE file is unavailable: $Path"
    }

    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
        ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try {
        if ($stream.Length -lt 64) {
            throw "File is too short to contain a PE header: $Path"
        }

        $reader = New-Object IO.BinaryReader $stream
        if ($reader.ReadUInt16() -ne 0x5A4D) {
            throw "File does not have an MZ header: $Path"
        }
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 64 -or $peOffset + 6 -gt $stream.Length) {
            throw "File has an invalid PE header offset: $Path"
        }
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) {
            throw "File does not have a PE signature: $Path"
        }
        return $reader.ReadUInt16()
    }
    finally {
        $stream.Dispose()
    }
}

function Resolve-Ff7PeBackedFileRange {
    param(
        [Parameter(Mandatory=$true)] [byte[]] $Bytes,
        [Parameter(Mandatory=$true)] [int] $SectionTableOffset,
        [Parameter(Mandatory=$true)] [int] $NumberOfSections,
        [Parameter(Mandatory=$true)] [uint32] $Rva,
        [Parameter(Mandatory=$true)] [uint32] $Size,
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [string] $Description
    )

    if ($Rva -eq 0 -or $Size -eq 0) {
        throw "File has an empty ${Description}: $Path"
    }
    for ($index = 0; $index -lt $NumberOfSections; $index++) {
        $sectionOffset = [uint64]$SectionTableOffset + ([uint64]$index * 40)
        if ($sectionOffset + 40 -gt [uint64]$Bytes.Length) {
            throw "File has a truncated PE section table: $Path"
        }
        $virtualSize = [BitConverter]::ToUInt32($Bytes, [int]$sectionOffset + 8)
        $virtualAddress = [BitConverter]::ToUInt32($Bytes, [int]$sectionOffset + 12)
        $rawSize = [BitConverter]::ToUInt32($Bytes, [int]$sectionOffset + 16)
        $rawOffset = [BitConverter]::ToUInt32($Bytes, [int]$sectionOffset + 20)
        if ([uint64]$Rva -lt [uint64]$virtualAddress) {
            continue
        }
        $delta = [uint64]$Rva - [uint64]$virtualAddress
        $rangeSize = [uint64]$Size
        $mappedSize = [Math]::Max([uint64]$virtualSize, [uint64]$rawSize)
        if ($delta -ge $mappedSize -or $delta + $rangeSize -gt $mappedSize) {
            continue
        }
        if ($delta + $rangeSize -gt [uint64]$rawSize) {
            throw "File has an unmappable ${Description} in a virtual-only PE section tail: $Path"
        }
        $fileOffset = [uint64]$rawOffset + $delta
        if ($fileOffset + $rangeSize -gt [uint64]$Bytes.Length) {
            throw "File has a truncated ${Description}: $Path"
        }
        return $fileOffset
    }
    throw "File has an unmappable ${Description}: $Path"
}

function Get-Ff7PeManagedNativeHeaderDirectory {
    param([Parameter(Mandatory=$true)] [string] $Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or [BitConverter]::ToUInt16($bytes, 0) -ne 0x5A4D) {
        throw "File does not have an MZ header: $Path"
    }
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($peOffset -lt 64 -or [uint64]$peOffset + 24 -gt [uint64]$bytes.Length -or
        [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x00004550) {
        throw "File does not have a valid PE signature: $Path"
    }
    $numberOfSections = [BitConverter]::ToUInt16($bytes, $peOffset + 6)
    $optionalHeaderSize = [BitConverter]::ToUInt16($bytes, $peOffset + 20)
    $optionalHeaderOffset = $peOffset + 24
    if ([uint64]$optionalHeaderOffset + [uint64]$optionalHeaderSize -gt [uint64]$bytes.Length) {
        throw "File has a truncated PE optional header: $Path"
    }
    $optionalMagic = [BitConverter]::ToUInt16($bytes, $optionalHeaderOffset)
    if ($optionalMagic -eq 0x010B) {
        $numberOfDirectoriesOffset = $optionalHeaderOffset + 92
        $directoriesOffset = $optionalHeaderOffset + 96
    }
    elseif ($optionalMagic -eq 0x020B) {
        $numberOfDirectoriesOffset = $optionalHeaderOffset + 108
        $directoriesOffset = $optionalHeaderOffset + 112
    }
    else {
        throw "File has an unsupported PE optional header: $Path"
    }
    if ($numberOfDirectoriesOffset + 4 -gt $optionalHeaderOffset + $optionalHeaderSize -or
        [BitConverter]::ToUInt32($bytes, $numberOfDirectoriesOffset) -le 14) {
        throw "File has no CLR data directory: $Path"
    }
    $clrDirectoryOffset = $directoriesOffset + (14 * 8)
    if ($clrDirectoryOffset + 8 -gt $optionalHeaderOffset + $optionalHeaderSize) {
        throw "File has a CLR data directory outside its optional header: $Path"
    }
    $clrRva = [BitConverter]::ToUInt32($bytes, $clrDirectoryOffset)
    $clrSize = [BitConverter]::ToUInt32($bytes, $clrDirectoryOffset + 4)
    if ($clrSize -lt 72) {
        throw "File has no complete CLR header: $Path"
    }
    $sectionTableOffset = $optionalHeaderOffset + $optionalHeaderSize
    $clrFileOffset = Resolve-Ff7PeBackedFileRange -Bytes $bytes `
        -SectionTableOffset $sectionTableOffset -NumberOfSections $numberOfSections `
        -Rva $clrRva -Size $clrSize -Path $Path -Description 'CLR header'
    $nativeRva = [BitConverter]::ToUInt32($bytes, [int]$clrFileOffset + 64)
    $nativeSize = [BitConverter]::ToUInt32($bytes, [int]$clrFileOffset + 68)
    if ($nativeRva -ne 0 -or $nativeSize -ne 0) {
        if ($nativeRva -eq 0 -or $nativeSize -eq 0) {
            throw "File has an incomplete ManagedNativeHeaderDirectory: $Path"
        }
        [void](Resolve-Ff7PeBackedFileRange -Bytes $bytes `
            -SectionTableOffset $sectionTableOffset -NumberOfSections $numberOfSections `
            -Rva $nativeRva -Size $nativeSize -Path $Path `
            -Description 'ManagedNativeHeaderDirectory')
    }
    return [pscustomobject]@{
        VirtualAddress = $nativeRva
        Size = $nativeSize
    }
}

function Assert-Ff7ManagedReadyToRunPe {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [int] $ExpectedMachine,
        [Parameter(Mandatory=$true)] [string] $Description
    )

    $machine = Get-Ff7PeMachine -Path $Path
    if ($machine -ne $ExpectedMachine) {
        throw ("Dual-runtime package validation failed: {0} has PE machine 0x{1:X4}; expected 0x{2:X4}." -f `
            $Description, $machine, $ExpectedMachine)
    }
    try {
        $nativeHeader = Get-Ff7PeManagedNativeHeaderDirectory -Path $Path
    }
    catch {
        throw "Dual-runtime package validation failed: $Description has an invalid ReadyToRun image. $($_.Exception.Message)"
    }
    if ($nativeHeader.VirtualAddress -eq 0 -or $nativeHeader.Size -eq 0) {
        throw "Dual-runtime package validation failed: $Description is not ReadyToRun."
    }
}

function Test-Ff7DependencyManifestEntry {
    param(
        [Parameter(Mandatory=$true)] [psobject] $Manifest,
        [Parameter(Mandatory=$true)] [string] $Dependency
    )

    $prefix = $Dependency + '/'
    $targetMatch = $false
    if ($null -ne $Manifest.targets) {
        foreach ($target in $Manifest.targets.PSObject.Properties) {
            if (@($target.Value.PSObject.Properties | Where-Object {
                $_.Name.StartsWith($prefix, [StringComparison]::Ordinal)
            }).Count -gt 0) {
                $targetMatch = $true
                break
            }
        }
    }
    $libraryMatch = $null -ne $Manifest.libraries -and
        @($Manifest.libraries.PSObject.Properties | Where-Object {
            $_.Name.StartsWith($prefix, [StringComparison]::Ordinal)
        }).Count -gt 0
    return $targetMatch -and $libraryMatch
}

function Assert-Ff7NativeRuntimeIdentity {
    [CmdletBinding()]
    param([Parameter(Mandatory=$true)] [string] $Path)

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $machine = Get-Ff7PeMachine -Path $resolvedPath
    if ($machine -ne 0x8664) {
        throw ("Native FFVII executable has PE machine 0x{0:X4}; expected native x64 machine 0x8664: {1}" -f `
            $machine, $resolvedPath)
    }

    $sha256 = (Get-FileHash -LiteralPath $resolvedPath -Algorithm SHA256).Hash
    if (-not $sha256.Equals($script:Steam2026NativeSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Native FFVII executable does not match the supported Steam 2026 native SHA-256. " +
            "Expected $($script:Steam2026NativeSha256); actual $sha256 at $resolvedPath"
    }

    return [pscustomobject]@{
        RuntimeId = $script:Steam2026NativeRuntimeId
        Architecture = 'x64'
        RuntimeRoot = Split-Path -Parent $resolvedPath
        GameExe = $resolvedPath
        Machine = $machine
        Sha256 = $sha256
    }
}

function Set-PeLargeAddressAware {
    [CmdletBinding()]
    param([Parameter(Mandatory=$true)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "PE executable is missing: $Path"
    }

    $needsPatch = $false
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $reader = New-Object IO.BinaryReader($stream, [Text.Encoding]::ASCII, $true)
        try {
            if ($stream.Length -lt 0x40) {
                throw "File is too small to be a PE executable: $Path"
            }

            $stream.Position = 0
            if ($reader.ReadUInt16() -ne 0x5A4D) {
                throw "Executable does not contain an MZ header: $Path"
            }

            $stream.Position = 0x3C
            $peOffset = $reader.ReadInt32()
            if ($peOffset -lt 0 -or ($peOffset + 24) -gt $stream.Length) {
                throw "Executable contains an invalid PE header offset: $Path"
            }

            $stream.Position = $peOffset
            if ($reader.ReadUInt32() -ne 0x00004550) {
                throw "Executable does not contain a PE signature: $Path"
            }

            $characteristicsOffset = $peOffset + 22
            $stream.Position = $characteristicsOffset
            $characteristics = $reader.ReadUInt16()
            $needsPatch = (($characteristics -band 0x20) -eq 0)
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    if (-not $needsPatch) {
        return
    }

    $toolProject = Join-Path $script:ModuleRoot 'tools\Ff7PePatcher\Ff7PePatcher.csproj'
    $toolDll = Join-Path $script:ModuleRoot 'tools\Ff7PePatcher\bin\Release\net8.0\Ff7PePatcher.dll'
    $toolSource = Join-Path $script:ModuleRoot 'tools\Ff7PePatcher\Program.cs'
    if (-not (Test-Path -LiteralPath $toolProject -PathType Leaf)) {
        throw "FFVII PE patcher project is missing: $toolProject"
    }

    $x86DotNet = Join-Path ${env:ProgramFiles(x86)} 'dotnet\dotnet.exe'
    $runDotNetHost = if (Test-Path -LiteralPath $x86DotNet -PathType Leaf) {
        $x86DotNet
    }
    else {
        (Get-Command dotnet -ErrorAction Stop).Source
    }
    $buildDotNetHost = (Get-Command dotnet -ErrorAction Stop).Source

    $needsBuild = -not (Test-Path -LiteralPath $toolDll -PathType Leaf)
    if (-not $needsBuild) {
        $toolWriteTime = (Get-Item -LiteralPath $toolDll).LastWriteTimeUtc
        $needsBuild =
            (Get-Item -LiteralPath $toolProject).LastWriteTimeUtc -gt $toolWriteTime -or
            (Get-Item -LiteralPath $toolSource).LastWriteTimeUtc -gt $toolWriteTime
    }

    if ($needsBuild) {
        & $buildDotNetHost build $toolProject -c Release --nologo
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $toolDll -PathType Leaf)) {
            throw "Failed to build the FFVII PE patcher: $toolProject"
        }
    }

    & $runDotNetHost $toolDll --large-address-aware $Path
    if ($LASTEXITCODE -ne 0) {
        throw "FFVII PE patcher failed with exit code $LASTEXITCODE for $Path"
    }
}

function Initialize-Ff7CompatibilityRuntime {
    [CmdletBinding()]
    param([Parameter(Mandatory=$true)] [psobject] $Installation)

    if ($Installation.Version -eq 'Steam2013') {
        return $Installation
    }

    if ($Installation.Version -ne 'Steam2026') {
        throw "Unsupported FFVII installation version: $($Installation.Version)"
    }

    if (-not (Test-Path -LiteralPath $Installation.RuntimeRoot -PathType Container)) {
        throw "Steam 2026 runtime directory is unavailable: $($Installation.RuntimeRoot)"
    }

    if (-not (Test-Path -LiteralPath $Installation.SourceExe -PathType Leaf)) {
        throw "Steam 2026 bundled executable is missing: $($Installation.SourceExe)"
    }

    $sourceHash = (Get-FileHash -LiteralPath $Installation.SourceExe -Algorithm SHA1).Hash
    if (-not $sourceHash.Equals($script:Steam2026SourceSha1, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsupported Steam 2026 source executable SHA-1 $sourceHash at $($Installation.SourceExe)"
    }

    $sourceItem = Get-Item -LiteralPath $Installation.SourceExe -Force
    if (($sourceItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing a reparse-point Steam 2026 source executable: $($Installation.SourceExe)"
    }
    $windowSource = Join-Path $Installation.RuntimeRoot 'data\lang-ja\kernel\window.bin'
    $windowTarget = Join-Path $Installation.RuntimeRoot 'data\kernel\window.bin'
    if (Test-Path -LiteralPath $windowTarget) {
        $windowTargetItem = Get-Item -LiteralPath $windowTarget -Force
        if ($windowTargetItem.PSIsContainer -or
            ($windowTargetItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing a non-file or reparse-point window.bin target: $windowTarget"
        }
    }
    elseif (-not (Test-Path -LiteralPath $windowSource -PathType Leaf)) {
        throw "Steam 2026 window.bin source is missing: $windowSource"
    }
    if (Test-Path -LiteralPath $windowSource -PathType Leaf) {
        $windowSourceItem = Get-Item -LiteralPath $windowSource -Force
        if (($windowSourceItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing a reparse-point Steam 2026 window.bin source: $windowSource"
        }
    }
    $steamAppIdPath = Join-Path $Installation.RuntimeRoot 'steam_appid.txt'
    if (Test-Path -LiteralPath $steamAppIdPath) {
        $steamAppIdItem = Get-Item -LiteralPath $steamAppIdPath -Force
        if ($steamAppIdItem.PSIsContainer -or
            ($steamAppIdItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing a non-file or reparse-point Steam app-id target: $steamAppIdPath"
        }
    }

    $gameExeHadPrior = Test-Path -LiteralPath $Installation.GameExe -PathType Leaf
    if ((Test-Path -LiteralPath $Installation.GameExe) -and -not $gameExeHadPrior) {
        throw "Refusing a non-file compatibility executable target: $($Installation.GameExe)"
    }
    if ($gameExeHadPrior) {
        $gameExeItem = Get-Item -LiteralPath $Installation.GameExe -Force
        if (($gameExeItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing a reparse-point compatibility executable: $($Installation.GameExe)"
        }
        $targetHash = (Get-FileHash -LiteralPath $Installation.GameExe -Algorithm SHA1).Hash
        $knownTarget =
            $targetHash.Equals($script:Steam2026SourceSha1, [StringComparison]::OrdinalIgnoreCase) -or
            $targetHash.Equals($script:Steam2026LargeAddressSha1, [StringComparison]::OrdinalIgnoreCase)
        if (-not $knownTarget) {
            throw "Unsupported existing compatibility executable SHA-1 $targetHash at $($Installation.GameExe)"
        }
    }
    $gameExeOriginalBytes = if ($gameExeHadPrior) {
        [IO.File]::ReadAllBytes($Installation.GameExe)
    }
    else {
        $null
    }
    $gameExeOriginalAttributes = if ($gameExeHadPrior) {
        (Get-Item -LiteralPath $Installation.GameExe -Force).Attributes
    }
    else {
        [IO.FileAttributes]::Normal
    }
    $windowHadPrior = Test-Path -LiteralPath $windowTarget -PathType Leaf
    $windowTargetDirectory = Split-Path -Parent $windowTarget
    $windowDirectoryHadPrior = Test-Path -LiteralPath $windowTargetDirectory -PathType Container
    $appIdHadPrior = Test-Path -LiteralPath $steamAppIdPath -PathType Leaf
    $appIdOriginalBytes = if ($appIdHadPrior) {
        [IO.File]::ReadAllBytes($steamAppIdPath)
    }
    else {
        $null
    }
    $appIdOriginalAttributes = if ($appIdHadPrior) {
        (Get-Item -LiteralPath $steamAppIdPath -Force).Attributes
    }
    else {
        [IO.FileAttributes]::Normal
    }
    $currentAppId = if (Test-Path -LiteralPath $steamAppIdPath -PathType Leaf) {
        [IO.File]::ReadAllText($steamAppIdPath).Trim()
    }
    else {
        $null
    }

    $gameExeChanged = $false
    $windowAttempted = $false
    $appIdAttempted = $false
    $appIdTemporaryPath = Join-Path $Installation.RuntimeRoot `
        ('.steam_appid.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        if (-not $gameExeHadPrior) {
            Copy-Item -LiteralPath $Installation.SourceExe -Destination $Installation.GameExe
            $gameExeChanged = $true
        }
        elseif ($targetHash.Equals($script:Steam2026SourceSha1, [StringComparison]::OrdinalIgnoreCase)) {
            $gameExeChanged = $true
        }

        Set-PeLargeAddressAware -Path $Installation.GameExe
        $patchedHash = (Get-FileHash -LiteralPath $Installation.GameExe -Algorithm SHA1).Hash
        if (-not $patchedHash.Equals($script:Steam2026LargeAddressSha1, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Compatibility executable did not match the expected large-address-aware SHA-1. Actual: $patchedHash"
        }

        if (-not $windowHadPrior) {
            $windowAttempted = $true
            if (-not $windowDirectoryHadPrior) {
                New-Item -ItemType Directory -Path $windowTargetDirectory -Force | Out-Null
            }
            Copy-Item -LiteralPath $windowSource -Destination $windowTarget
            $sourceWindowHash = (Get-FileHash -LiteralPath $windowSource -Algorithm SHA256).Hash
            $targetWindowHash = (Get-FileHash -LiteralPath $windowTarget -Algorithm SHA256).Hash
            if (-not $sourceWindowHash.Equals($targetWindowHash, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Compatibility window.bin failed hash verification: $windowTarget"
            }
        }

        if ($currentAppId -ne $script:Steam2026AppId) {
            $appIdAttempted = $true
            [IO.File]::WriteAllText($appIdTemporaryPath, $script:Steam2026AppId, [Text.Encoding]::ASCII)
            if ($appIdHadPrior) {
                [IO.File]::Replace($appIdTemporaryPath, $steamAppIdPath, $null, $true)
            }
            else {
                Move-Item -LiteralPath $appIdTemporaryPath -Destination $steamAppIdPath
            }
            if ([IO.File]::ReadAllText($steamAppIdPath).Trim() -ne $script:Steam2026AppId) {
                throw "Compatibility Steam app-id failed verification: $steamAppIdPath"
            }
        }
    }
    catch {
        $initializeError = $_
        $rollbackErrors = New-Object System.Collections.Generic.List[string]
        if ($appIdAttempted) {
            try {
                if (Test-Path -LiteralPath $steamAppIdPath -PathType Leaf) {
                    (Get-Item -LiteralPath $steamAppIdPath -Force).Attributes = [IO.FileAttributes]::Normal
                }
                if ($appIdHadPrior) {
                    [IO.File]::WriteAllBytes($steamAppIdPath, $appIdOriginalBytes)
                    (Get-Item -LiteralPath $steamAppIdPath -Force).Attributes = $appIdOriginalAttributes
                }
                elseif (Test-Path -LiteralPath $steamAppIdPath -PathType Leaf) {
                    Remove-Item -LiteralPath $steamAppIdPath -Force
                }
            }
            catch {
                $rollbackErrors.Add($_.Exception.Message)
            }
        }
        if ($windowAttempted -and -not $windowHadPrior) {
            try {
                if (Test-Path -LiteralPath $windowTarget -PathType Leaf) {
                    Remove-Item -LiteralPath $windowTarget -Force
                }
                if (-not $windowDirectoryHadPrior -and
                    (Test-Path -LiteralPath $windowTargetDirectory -PathType Container) -and
                    @(Get-ChildItem -LiteralPath $windowTargetDirectory -Force).Count -eq 0) {
                    Remove-Item -LiteralPath $windowTargetDirectory -Force
                }
            }
            catch {
                $rollbackErrors.Add($_.Exception.Message)
            }
        }
        if ($gameExeChanged) {
            try {
                if ($gameExeHadPrior) {
                    if (Test-Path -LiteralPath $Installation.GameExe -PathType Leaf) {
                        (Get-Item -LiteralPath $Installation.GameExe -Force).Attributes = [IO.FileAttributes]::Normal
                    }
                    [IO.File]::WriteAllBytes($Installation.GameExe, $gameExeOriginalBytes)
                    (Get-Item -LiteralPath $Installation.GameExe -Force).Attributes = $gameExeOriginalAttributes
                }
                elseif (Test-Path -LiteralPath $Installation.GameExe -PathType Leaf) {
                    Remove-Item -LiteralPath $Installation.GameExe -Force
                }
            }
            catch {
                $rollbackErrors.Add($_.Exception.Message)
            }
        }
        if ($rollbackErrors.Count -gt 0) {
            throw "Compatibility runtime initialization failed and rollback also reported errors. Original: $($initializeError.Exception.Message) Rollback: $($rollbackErrors -join ' | ')"
        }
        throw $initializeError
    }
    finally {
        if (Test-Path -LiteralPath $appIdTemporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $appIdTemporaryPath -Force
        }
    }

    return $Installation
}

function Select-FfnxSteamAsset {
    [CmdletBinding()]
    param([Parameter(Mandatory=$true)] [psobject] $Release)

    $assetsProperty = $Release.PSObject.Properties['assets']
    if ($null -eq $assetsProperty) {
        throw 'FFNx release metadata does not contain any assets.'
    }

    $steamAssets = @($assetsProperty.Value | Where-Object {
        $nameProperty = $_.PSObject.Properties['name']
        $null -ne $nameProperty -and $nameProperty.Value -like 'FFNx-Steam-*.zip'
    })
    if ($steamAssets.Count -ne 1) {
        throw "FFNx release must contain exactly one FFNx Steam archive; found $($steamAssets.Count)."
    }

    $asset = $steamAssets[0]
    $digestProperty = $asset.PSObject.Properties['digest']
    $digest = if ($null -ne $digestProperty) { [string] $digestProperty.Value } else { $null }
    if ([string]::IsNullOrWhiteSpace($digest) -or $digest -notmatch '^sha256:[0-9a-fA-F]{64}$') {
        throw "FFNx Steam archive $($asset.name) does not provide a valid SHA-256 digest."
    }

    $urlProperty = $asset.PSObject.Properties['browser_download_url']
    $downloadUrl = if ($null -ne $urlProperty) { [string] $urlProperty.Value } else { $null }
    if ([string]::IsNullOrWhiteSpace($downloadUrl)) {
        throw "FFNx Steam archive $($asset.name) does not provide a download URL."
    }

    return $asset
}

function Install-FfnxSteamRuntime {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)] [string] $RuntimeRoot,
        [psobject] $Release,
        [string] $ArchivePath
    )

    $resolvedRuntimeRoot = Get-CanonicalExistingDirectory -Path $RuntimeRoot
    if ($null -eq $Release) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $Release = Invoke-RestMethod `
            -Uri 'https://api.github.com/repos/julianxhokaxhiu/FFNx/releases/latest' `
            -Headers @{ 'User-Agent' = 'FF7-Accessibility-Installer' } `
            -UseBasicParsing
    }

    $asset = Select-FfnxSteamAsset -Release $Release
    $downloadedArchive = $false
    $stagingRoot = $null
    if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
        $ArchivePath = Join-Path ([IO.Path]::GetTempPath()) ('FFNx-Steam-' + [Guid]::NewGuid().ToString('N') + '.zip')
        Invoke-WebRequest `
            -Uri ([string] $asset.browser_download_url) `
            -OutFile $ArchivePath `
            -Headers @{ 'User-Agent' = 'FF7-Accessibility-Installer' } `
            -UseBasicParsing
        $downloadedArchive = $true
    }

    try {
        if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf)) {
            throw "FFNx archive is missing: $ArchivePath"
        }

        $expectedDigest = ([string] $asset.digest).Substring('sha256:'.Length)
        $actualDigest = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash
        if (-not $actualDigest.Equals($expectedDigest, [StringComparison]::OrdinalIgnoreCase)) {
            throw "FFNx archive SHA-256 mismatch. Expected $expectedDigest, actual $actualDigest."
        }

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $stagingRoot = Join-Path ([IO.Path]::GetTempPath()) ('ffnx-stage-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

        $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
        try {
            $canonicalStagePrefix = [IO.Path]::GetFullPath($stagingRoot).TrimEnd('\') + '\'
            foreach ($entry in $archive.Entries) {
                $destination = [IO.Path]::GetFullPath((Join-Path $stagingRoot $entry.FullName))
                if (-not $destination.StartsWith($canonicalStagePrefix, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "FFNx archive contains an unsafe path: $($entry.FullName)"
                }
            }
        }
        finally {
            $archive.Dispose()
        }

        [IO.Compression.ZipFile]::ExtractToDirectory($ArchivePath, $stagingRoot)
        if (-not (Test-Path -LiteralPath (Join-Path $stagingRoot 'AF3DN.P') -PathType Leaf)) {
            throw 'FFNx Steam archive does not contain AF3DN.P.'
        }
        if (-not (Test-Path -LiteralPath (Join-Path $stagingRoot 'FFNx.toml') -PathType Leaf)) {
            throw 'FFNx Steam archive does not contain FFNx.toml.'
        }

        $preservedConfig = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($relativePath in @(
            'FFNx.toml',
            'ambient\config.toml',
            'lighting\config.toml',
            'music\vgmstream\config.toml',
            'sfx\config.toml',
            'time\config.toml',
            'voice\config.toml'
        )) {
            [void] $preservedConfig.Add($relativePath)
        }

        $stagePrefixLength = $stagingRoot.TrimEnd('\').Length + 1
        $runtimePrefix = $resolvedRuntimeRoot.TrimEnd('\') + '\'
        $installPlan = New-Object System.Collections.Generic.List[object]
        $directoriesNeeded = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($sourceFile in Get-ChildItem -LiteralPath $stagingRoot -File -Recurse | Sort-Object FullName) {
            $relativePath = $sourceFile.FullName.Substring($stagePrefixLength)
            $targetPath = [IO.Path]::GetFullPath((Join-Path $resolvedRuntimeRoot $relativePath))
            if (-not $targetPath.StartsWith($runtimePrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "FFNx staged file escapes the selected runtime: $relativePath"
            }
            if ($preservedConfig.Contains($relativePath) -and (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
                continue
            }

            if (Test-Path -LiteralPath $targetPath) {
                $targetItem = Get-Item -LiteralPath $targetPath -Force
                if ($targetItem.PSIsContainer -or
                    ($targetItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "FFNx target is not a regular file: $targetPath"
                }
            }

            $targetDirectory = Split-Path -Parent $targetPath
            $directory = $targetDirectory
            while (-not $directory.Equals($resolvedRuntimeRoot, [StringComparison]::OrdinalIgnoreCase)) {
                if (-not $directory.StartsWith($runtimePrefix, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "FFNx target directory escapes the selected runtime: $directory"
                }
                if (Test-Path -LiteralPath $directory) {
                    $directoryItem = Get-Item -LiteralPath $directory -Force
                    if (-not $directoryItem.PSIsContainer -or
                        ($directoryItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                        throw "FFNx target directory is not a regular directory: $directory"
                    }
                }
                else {
                    [void]$directoriesNeeded.Add($directory)
                }
                $directory = Split-Path -Parent $directory
            }

            $installPlan.Add([pscustomobject]@{
                SourcePath = $sourceFile.FullName
                SourceHash = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash
                TargetPath = $targetPath
                TargetDirectory = $targetDirectory
                HadPrior = (Test-Path -LiteralPath $targetPath -PathType Leaf)
            })
        }

        $createdDirectories = New-Object System.Collections.Generic.List[string]
        $journal = New-Object System.Collections.Generic.List[object]
        try {
            foreach ($directory in @($directoriesNeeded) | Sort-Object Length) {
                if (-not (Test-Path -LiteralPath $directory)) {
                    New-Item -ItemType Directory -Path $directory | Out-Null
                    $createdDirectories.Add($directory)
                }
            }

            foreach ($item in $installPlan) {
                $operationId = [Guid]::NewGuid().ToString('N')
                $temporaryPath = Join-Path $item.TargetDirectory ('.ffnx-accessibility-' + $operationId + '.tmp')
                $backupPath = if ($item.HadPrior) {
                    Join-Path $item.TargetDirectory ('.ffnx-accessibility-' + $operationId + '.backup')
                }
                else {
                    $null
                }
                $entry = [pscustomobject]@{
                    TargetPath = $item.TargetPath
                    BackupPath = $backupPath
                    HadPrior = $item.HadPrior
                    SourceHash = $item.SourceHash
                    Installed = $false
                }
                $journal.Add($entry)
                try {
                    Copy-Item -LiteralPath $item.SourcePath -Destination $temporaryPath
                    $temporaryHash = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash
                    if (-not $temporaryHash.Equals($item.SourceHash, [StringComparison]::OrdinalIgnoreCase)) {
                        throw "FFNx staged copy failed hash verification: $($item.TargetPath)"
                    }
                    if ($item.HadPrior) {
                        [IO.File]::Replace($temporaryPath, $item.TargetPath, $backupPath, $true)
                    }
                    else {
                        Move-Item -LiteralPath $temporaryPath -Destination $item.TargetPath
                    }
                    $entry.Installed = $true
                    $installedHash = (Get-FileHash -LiteralPath $item.TargetPath -Algorithm SHA256).Hash
                    if (-not $installedHash.Equals($item.SourceHash, [StringComparison]::OrdinalIgnoreCase)) {
                        throw "FFNx installed file failed hash verification: $($item.TargetPath)"
                    }
                }
                finally {
                    if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
                        Remove-Item -LiteralPath $temporaryPath -Force
                    }
                }
            }
        }
        catch {
            $installError = $_
            $rollbackErrors = New-Object System.Collections.Generic.List[string]
            for ($index = $journal.Count - 1; $index -ge 0; $index--) {
                $entry = $journal[$index]
                try {
                    $hasBackup = -not [string]::IsNullOrWhiteSpace([string]$entry.BackupPath) -and
                        (Test-Path -LiteralPath $entry.BackupPath -PathType Leaf)
                    if ($entry.Installed -or $hasBackup) {
                        if (Test-Path -LiteralPath $entry.TargetPath -PathType Leaf) {
                            $targetItem = Get-Item -LiteralPath $entry.TargetPath -Force
                            if (($targetItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                                throw "Refusing to roll back a reparse-point FFNx file: $($entry.TargetPath)"
                            }
                            $currentHash = (Get-FileHash -LiteralPath $entry.TargetPath -Algorithm SHA256).Hash
                            if (-not $currentHash.Equals($entry.SourceHash, [StringComparison]::OrdinalIgnoreCase)) {
                                throw "Refusing to roll back an FFNx file changed during installation: $($entry.TargetPath)"
                            }
                            Remove-Item -LiteralPath $entry.TargetPath -Force
                        }
                        if ($entry.HadPrior) {
                            if (-not $hasBackup) {
                                throw "FFNx rollback backup is missing: $($entry.BackupPath)"
                            }
                            $backupItem = Get-Item -LiteralPath $entry.BackupPath -Force
                            if (($backupItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                                throw "Refusing to restore a reparse-point FFNx backup: $($entry.BackupPath)"
                            }
                            Move-Item -LiteralPath $entry.BackupPath -Destination $entry.TargetPath
                        }
                    }
                }
                catch {
                    $rollbackErrors.Add($_.Exception.Message)
                }
            }
            foreach ($directory in @($createdDirectories) | Sort-Object Length -Descending) {
                try {
                    if ((Test-Path -LiteralPath $directory -PathType Container) -and
                        @(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) {
                        Remove-Item -LiteralPath $directory -Force
                    }
                }
                catch {
                    $rollbackErrors.Add($_.Exception.Message)
                }
            }
            if ($rollbackErrors.Count -gt 0) {
                throw "FFNx installation failed and rollback also reported errors. Original: $($installError.Exception.Message) Rollback: $($rollbackErrors -join ' | ')"
            }
            throw $installError
        }

        foreach ($entry in $journal) {
            if (-not [string]::IsNullOrWhiteSpace([string]$entry.BackupPath) -and
                (Test-Path -LiteralPath $entry.BackupPath -PathType Leaf)) {
                try {
                    Remove-Item -LiteralPath $entry.BackupPath -Force
                }
                catch {
                    Write-Warning "FFNx was installed, but a transaction backup could not be removed: $($entry.BackupPath)"
                }
            }
        }

        return [pscustomobject]@{
            RuntimeRoot = $resolvedRuntimeRoot
            ReleaseTag = [string] $Release.tag_name
            AssetName = [string] $asset.name
            Sha256 = $actualDigest
        }
    }
    finally {
        if ($null -ne $stagingRoot -and (Test-Path -LiteralPath $stagingRoot -PathType Container)) {
            Remove-Item -LiteralPath $stagingRoot -Recurse -Force
        }
        if ($downloadedArchive -and (Test-Path -LiteralPath $ArchivePath -PathType Leaf)) {
            Remove-Item -LiteralPath $ArchivePath -Force
        }
    }
}

function Install-Ff7OpeningMovieAudioDescription {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)] [string] $RuntimeRoot,
        [Parameter(Mandatory=$true)] [string] $SourcePath
    )

    $resolvedRuntimeRoot = Get-CanonicalExistingDirectory -Path $RuntimeRoot
    if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        throw "Opening movie audio-description source is missing: $SourcePath"
    }

    $resolvedSourcePath = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $SourcePath).Path)
    $nativeMoviePath = Join-Path $resolvedRuntimeRoot 'data\movies\opening.avi'
    if (-not (Test-Path -LiteralPath $nativeMoviePath -PathType Leaf)) {
        throw "Native FFVII opening movie is missing: $nativeMoviePath"
    }

    $resolvedNativeMoviePath = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $nativeMoviePath).Path)
    $targetMoviePath = Join-Path $resolvedRuntimeRoot 'override\movies\opening.avi'
    $targetPath = Join-Path $resolvedRuntimeRoot 'override\movies\opening_va.ogg'
    $sourceHash = (Get-FileHash -LiteralPath $resolvedSourcePath -Algorithm SHA256).Hash
    $nativeMovieHash = (Get-FileHash -LiteralPath $resolvedNativeMoviePath -Algorithm SHA256).Hash
    $voiceChanged = -not (Test-Path -LiteralPath $targetPath -PathType Leaf)
    $movieChanged = -not (Test-Path -LiteralPath $targetMoviePath -PathType Leaf)

    if (-not $voiceChanged) {
        $targetHash = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash
        if (-not $sourceHash.Equals($targetHash, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to overwrite a different FFNx opening movie voice track: $targetPath"
        }
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $targetPath) -Force | Out-Null

    if ($movieChanged) {
        Copy-Item -LiteralPath $resolvedNativeMoviePath -Destination $targetMoviePath
        $installedMovieHash = (Get-FileHash -LiteralPath $targetMoviePath -Algorithm SHA256).Hash
        if (-not $nativeMovieHash.Equals($installedMovieHash, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Installed FFNx opening movie failed verification: $targetMoviePath"
        }
    }

    if ($voiceChanged) {
        Copy-Item -LiteralPath $resolvedSourcePath -Destination $targetPath
        $installedHash = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash
        if (-not $sourceHash.Equals($installedHash, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Installed FFNx opening movie voice track failed verification: $targetPath"
        }
    }

    return [pscustomobject]@{
        Changed = $movieChanged -or $voiceChanged
        MovieChanged = $movieChanged
        VoiceChanged = $voiceChanged
        SourcePath = $resolvedSourcePath
        TargetPath = $targetPath
        TargetMoviePath = $targetMoviePath
        Sha256 = $sourceHash
        MovieSha256 = if ($movieChanged) { $nativeMovieHash } else { $null }
    }
}

function Disable-Ff7OpeningMovieNativeVoiceLayer {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)] [string] $RuntimeRoot,
        [Parameter(Mandatory=$true)] [string] $SourcePath
    )

    $resolvedRuntimeRoot = Get-CanonicalExistingDirectory -Path $RuntimeRoot
    if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        throw "Opening movie audio-description source is missing: $SourcePath"
    }

    $resolvedSourcePath = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $SourcePath).Path)
    $targetPath = Join-Path $resolvedRuntimeRoot 'override\movies\opening_va.ogg'
    if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
        return [pscustomobject]@{
            Removed = $false
            SourcePath = $resolvedSourcePath
            TargetPath = $targetPath
        }
    }

    $sourceHash = (Get-FileHash -LiteralPath $resolvedSourcePath -Algorithm SHA256).Hash
    $targetHash = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash
    if (-not $sourceHash.Equals($targetHash, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a different FFNx opening movie voice track: $targetPath"
    }

    Remove-Item -LiteralPath $targetPath -Force
    if (Test-Path -LiteralPath $targetPath -PathType Leaf) {
        throw "Failed to remove the managed FFNx opening movie voice track: $targetPath"
    }

    return [pscustomobject]@{
        Removed = $true
        SourcePath = $resolvedSourcePath
        TargetPath = $targetPath
    }
}

function Update-SeventhHeavenSettings {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)] [string] $SettingsPath,
        [Parameter(Mandatory=$true)] [psobject] $Installation
    )

    if (-not (Test-Path -LiteralPath $SettingsPath -PathType Leaf)) {
        throw "7th Heaven settings file is missing: $SettingsPath"
    }

    $targetInstalledVersion = switch ($Installation.Version) {
        'Steam2026' { 'SteamReRelease' }
        'Steam2013' { 'Steam' }
        default { throw "Unsupported FFVII installation version for 7th Heaven: $($Installation.Version)" }
    }

    $document = New-Object Xml.XmlDocument
    $document.PreserveWhitespace = $true
    $document.XmlResolver = $null
    $document.LoadXml([IO.File]::ReadAllText($SettingsPath))

    $exeNode = $document.SelectSingleNode('/Settings/FF7Exe')
    $versionNode = $document.SelectSingleNode('/Settings/FF7InstalledVersion')
    if ($null -eq $exeNode -or $null -eq $versionNode) {
        throw "7th Heaven settings are missing FF7Exe or FF7InstalledVersion: $SettingsPath"
    }

    $needsChange =
        -not $exeNode.InnerText.Equals([string] $Installation.GameExe, [StringComparison]::OrdinalIgnoreCase) -or
        $versionNode.InnerText -ne $targetInstalledVersion
    if (-not $needsChange) {
        return [pscustomobject]@{
            Changed = $false
            SettingsPath = $SettingsPath
            BackupPath = $null
        }
    }

    $settingsDirectory = Split-Path -Parent $SettingsPath
    $backupName = '{0}.accessibility-backup-{1}-{2}' -f `
        (Split-Path -Leaf $SettingsPath),
        (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmssfff'),
        [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $backupPath = Join-Path $settingsDirectory $backupName

    $exeNode.InnerText = [string] $Installation.GameExe
    $versionNode.InnerText = $targetInstalledVersion
    $temporaryPath = Join-Path $settingsDirectory ('.' + (Split-Path -Leaf $SettingsPath) + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $writerSettings = New-Object Xml.XmlWriterSettings
        $writerSettings.Encoding = New-Object Text.UTF8Encoding($false)
        $writerSettings.Indent = $false
        $writerSettings.NewLineHandling = [Xml.NewLineHandling]::None
        $writer = [Xml.XmlWriter]::Create($temporaryPath, $writerSettings)
        try {
            $document.Save($writer)
        }
        finally {
            $writer.Dispose()
        }

        [IO.File]::Replace($temporaryPath, $SettingsPath, $backupPath, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }

    return [pscustomobject]@{
        Changed = $true
        SettingsPath = $SettingsPath
        BackupPath = $backupPath
    }
}

function Get-Ff7DirectoryFingerprint {
    param([Parameter(Mandatory=$true)] [string] $Root)

    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $entries = foreach ($file in Get-ChildItem -LiteralPath $rootPath -File -Recurse | Sort-Object FullName) {
        $relativePath = $file.FullName.Substring($rootPath.Length).TrimStart('\')
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        '{0}|{1}|{2}' -f $relativePath, $file.Length, $hash
    }

    $bytes = [Text.Encoding]::UTF8.GetBytes(($entries -join "`n"))
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha256.ComputeHash($bytes))).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
    }
}

function Assert-Ff7DualRuntimePackage {
    param([Parameter(Mandatory=$true)] [string] $PackagePath)

    if (-not (Test-Path -LiteralPath $PackagePath -PathType Container)) {
        throw "Dual-runtime package validation failed: package directory is unavailable: $PackagePath"
    }

    $requiredFiles = @(
        'ModConfig.json',
        'Configuration\config.json',
        'Assets\movies\opening_audio_description.ogg',
        'Assets\world\field-id-to-world-map-coords.json',
        'Assets\world\wm-field-menu-names.txt',
        'Assets\footsteps\cosmo\config.toml',
        'Assets\navigation\field_zone_transition.wav',
        'Assets\navigation\object_materia_190_pitch70.wav',
        'Assets\navigation\object_chest_253_pitch70.wav',
        'Assets\navigation\object_item_357_pitch70.wav',
        'Assets\navigation\ladder_061.wav',
        'Assets\navigation\floor60_statue_134.wav',
        'x86\Ff7.Accessibility.Reloaded.dll',
        'x86\Ff7.Accessibility.Core.dll',
        'x86\Ff7.Accessibility.LegacyLayout.dll',
        'x86\Ff7.Accessibility.Runtime.Abstractions.dll',
        'x86\Ff7.Accessibility.Reloaded.deps.json',
        'x86\prism.dll',
        'x86\phonon.dll',
        'x64\Ff7.Accessibility.Steam2026X64.dll',
        'x64\Ff7.Accessibility.Core.dll',
        'x64\Ff7.Accessibility.LegacyLayout.dll',
        'x64\Ff7.Accessibility.Runtime.Abstractions.dll',
        'x64\Ff7.Accessibility.Steam2026X64.deps.json',
        'x64\prism.dll',
        'x64\phonon.dll'
    )
    foreach ($relativePath in $requiredFiles) {
        $path = Join-Path $PackagePath $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Dual-runtime package validation failed: missing $relativePath"
        }
    }

    try {
        $modConfig = [IO.File]::ReadAllText((Join-Path $PackagePath 'ModConfig.json')) | ConvertFrom-Json
    }
    catch {
        throw "Dual-runtime package validation failed: invalid ModConfig.json. $($_.Exception.Message)"
    }
    if ([string]$modConfig.ModId -cne 'ff7.accessibility.reloaded') {
        throw 'Dual-runtime package validation failed: ModConfig.json has an unexpected ModId.'
    }
    if ([string]$modConfig.ModR2RManagedDll32 -ne 'x86/Ff7.Accessibility.Reloaded.dll' -or
        [string]$modConfig.ModR2RManagedDll64 -ne 'x64/Ff7.Accessibility.Steam2026X64.dll') {
        throw 'Dual-runtime package validation failed: ModConfig.json does not select the expected x86 and x64 entry assemblies.'
    }
    $supportedAppIds = @($modConfig.SupportedAppId)
    if (-not (Test-Ff7ExactOrderedStringList -Actual $supportedAppIds `
            -Expected @('ff7_en.exe', 'FFVII.exe'))) {
        throw 'Dual-runtime package validation failed: ModConfig.json does not contain the exact supported executable identities.'
    }

    foreach ($architecture in @(
        [pscustomobject]@{
            Name = 'x86'
            Machine = 0x014C
            Entry = 'Ff7.Accessibility.Reloaded.dll'
            Deps = 'Ff7.Accessibility.Reloaded.deps.json'
        },
        [pscustomobject]@{
            Name = 'x64'
            Machine = 0x8664
            Entry = 'Ff7.Accessibility.Steam2026X64.dll'
            Deps = 'Ff7.Accessibility.Steam2026X64.deps.json'
        }
    )) {
        foreach ($managedFile in @(
            $architecture.Entry,
            'Ff7.Accessibility.Core.dll',
            'Ff7.Accessibility.LegacyLayout.dll',
            'Ff7.Accessibility.Runtime.Abstractions.dll'
        )) {
            $path = Join-Path $PackagePath (Join-Path $architecture.Name $managedFile)
            Assert-Ff7ManagedReadyToRunPe -Path $path -ExpectedMachine $architecture.Machine `
                -Description "$($architecture.Name) managed payload $managedFile"
        }
        foreach ($nativeFile in @('prism.dll', 'phonon.dll')) {
            $path = Join-Path $PackagePath (Join-Path $architecture.Name $nativeFile)
            $machine = Get-Ff7PeMachine -Path $path
            if ($machine -ne $architecture.Machine) {
                throw ("Dual-runtime package validation failed: {0}\{1} has PE machine 0x{2:X4}; expected 0x{3:X4}." -f `
                    $architecture.Name, $nativeFile, $machine, $architecture.Machine)
            }
        }

        $depsPath = Join-Path $PackagePath (Join-Path $architecture.Name $architecture.Deps)
        try {
            $depsManifest = [IO.File]::ReadAllText($depsPath) | ConvertFrom-Json
        }
        catch {
            throw "Dual-runtime package validation failed: $($architecture.Name) dependency manifest is invalid JSON. $($_.Exception.Message)"
        }
        foreach ($dependency in @(
            'Ff7.Accessibility.Core',
            'Ff7.Accessibility.LegacyLayout',
            'Ff7.Accessibility.Runtime.Abstractions'
        )) {
            if (-not (Test-Ff7DependencyManifestEntry -Manifest $depsManifest -Dependency $dependency)) {
                throw "Dual-runtime package validation failed: $($architecture.Name) dependency manifest omits $dependency."
            }
        }
    }

    $configurationPath = Join-Path $PackagePath 'Configuration\config.json'
    try {
        $configuration = [IO.File]::ReadAllText($configurationPath) | ConvertFrom-Json
    }
    catch {
        throw "Dual-runtime package validation failed: invalid Configuration/config.json. $($_.Exception.Message)"
    }
    if ($configuration -isnot [Management.Automation.PSCustomObject]) {
        throw 'Dual-runtime package validation failed: invalid Configuration/config.json root; expected an object.'
    }

    return [pscustomobject]@{
        PackagePath = [IO.Path]::GetFullPath($PackagePath)
        Fingerprint = Get-Ff7DirectoryFingerprint -Root $PackagePath
    }
}

function Install-Ff7DualRuntimePackage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)] [string] $PackagePath,
        [Parameter(Mandatory=$true)] [string] $ModDirectory,
        [switch] $ValidateOnly
    )

    $sourceValidation = Assert-Ff7DualRuntimePackage -PackagePath $PackagePath
    $target = New-Object IO.DirectoryInfo ([IO.Path]::GetFullPath($ModDirectory))
    if ($null -eq $target.Parent) {
        throw 'ModDirectory must not be a volume root.'
    }
    if ($null -eq $target.Parent.Parent) {
        throw 'ModDirectory must have a non-root loader directory above its mod directory.'
    }
    if (-not $target.Name.Equals('ff7.accessibility.reloaded', [StringComparison]::OrdinalIgnoreCase)) {
        throw "ModDirectory must end in the owned mod directory 'ff7.accessibility.reloaded': $($target.FullName)"
    }
    if (Test-Path -LiteralPath $target.FullName) {
        $existingTarget = Get-Item -LiteralPath $target.FullName -Force
        if (-not $existingTarget.PSIsContainer) {
            throw "Refusing to replace a non-directory mod target: $($target.FullName)"
        }
        if (($existingTarget.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to replace a reparse-point mod target: $($target.FullName)"
        }
        $existingConfigPath = Join-Path $target.FullName 'ModConfig.json'
        if (-not (Test-Path -LiteralPath $existingConfigPath -PathType Leaf)) {
            throw "Refusing to replace an unowned mod directory without ModConfig.json: $($target.FullName)"
        }
        try {
            $existingConfig = [IO.File]::ReadAllText($existingConfigPath) | ConvertFrom-Json
        }
        catch {
            throw "Refusing to replace an unowned mod directory with invalid ModConfig.json: $($target.FullName)"
        }
        if ([string]$existingConfig.ModId -cne 'ff7.accessibility.reloaded') {
            throw "Refusing to replace a mod directory owned by another ModId: $($target.FullName)"
        }
    }
    if ($ValidateOnly) {
        return [pscustomobject]@{
            Validated = $true
            PackagePath = $sourceValidation.PackagePath
            ModDirectory = $target.FullName
            ExistingTarget = (Test-Path -LiteralPath $target.FullName -PathType Container)
        }
    }
    New-Item -ItemType Directory -Path $target.Parent.FullName -Force | Out-Null

    $operationId = [Guid]::NewGuid().ToString('N')
    $candidate = Join-Path $target.Parent.FullName ('.{0}.candidate-{1}' -f $target.Name, $operationId)
    # Reloaded discovers ModConfig files beneath Mods and de-duplicates matching
    # ModIds by enumeration order. A rollback copy there becomes a second live
    # candidate and can make x64 select an older x86-only payload. Keep
    # recoverable backups outside the loader's discovery directory instead.
    $backupRoot = New-Object IO.DirectoryInfo `
        (Join-Path $target.Parent.Parent.FullName 'AccessibilityBackups')
    $backup = Join-Path $backupRoot.FullName ('{0}.backup-{1}' -f $target.Name, $operationId)
    $priorMoved = $false
    $installedFingerprint = $null
    try {
        New-Item -ItemType Directory -Path $candidate | Out-Null
        Get-ChildItem -LiteralPath $sourceValidation.PackagePath -Force |
            Copy-Item -Destination $candidate -Recurse -Force
        $candidateValidation = Assert-Ff7DualRuntimePackage -PackagePath $candidate
        if ($candidateValidation.Fingerprint -ne $sourceValidation.Fingerprint) {
            throw 'Dual-runtime package validation failed: staged package hashes differ from the verified source package.'
        }

        $installedConfiguration = Join-Path $target.FullName 'Configuration\config.json'
        if (Test-Path -LiteralPath $installedConfiguration -PathType Leaf) {
            Copy-Item -LiteralPath $installedConfiguration `
                -Destination (Join-Path $candidate 'Configuration\config.json') -Force
        }
        Assert-Ff7DualRuntimePackage -PackagePath $candidate | Out-Null
        $candidateFingerprint = Get-Ff7DirectoryFingerprint -Root $candidate

        if (Test-Path -LiteralPath $target.FullName -PathType Container) {
            $installedFingerprint = Get-Ff7DirectoryFingerprint -Root $target.FullName
            if ($installedFingerprint -eq $candidateFingerprint) {
                return [pscustomobject]@{
                    Changed = $false
                    ModDirectory = $target.FullName
                    BackupPath = $null
                    BackupFingerprint = $null
                    Fingerprint = $installedFingerprint
                }
            }

            if (Test-Path -LiteralPath $backupRoot.FullName) {
                $existingBackupRoot = Get-Item -LiteralPath $backupRoot.FullName -Force
                if (-not $existingBackupRoot.PSIsContainer -or
                    ($existingBackupRoot.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Refusing to use a non-directory or reparse-point backup root: $($backupRoot.FullName)"
                }
            }
            else {
                New-Item -ItemType Directory -Path $backupRoot.FullName | Out-Null
            }
            Move-Item -LiteralPath $target.FullName -Destination $backup
            $priorMoved = $true
        }

        try {
            Move-Item -LiteralPath $candidate -Destination $target.FullName
        }
        catch {
            $replacementError = $_
            if ($priorMoved -and -not (Test-Path -LiteralPath $target.FullName) -and
                (Test-Path -LiteralPath $backup -PathType Container)) {
                Move-Item -LiteralPath $backup -Destination $target.FullName
                $priorMoved = $false
            }
            throw "Dual-runtime package replacement failed; prior package was preserved. $($replacementError.Exception.Message)"
        }

        return [pscustomobject]@{
            Changed = $true
            ModDirectory = $target.FullName
            BackupPath = if ($priorMoved) { $backup } else { $null }
            BackupFingerprint = if ($priorMoved) { $installedFingerprint } else { $null }
            Fingerprint = $candidateFingerprint
        }
    }
    finally {
        if (Test-Path -LiteralPath $candidate -PathType Container) {
            Remove-Item -LiteralPath $candidate -Recurse -Force
        }
    }
}

function Assert-Ff7NativeParityReleaseGate {
    [CmdletBinding()]
    param(
        [string] $ParityMatrixPath = (Join-Path $script:ModuleRoot '..\analysis\dual_runtime\parity-matrix.json'),
        [switch] $AllowResearch
    )

    if ([string]::IsNullOrWhiteSpace($ParityMatrixPath) -or
        -not (Test-Path -LiteralPath $ParityMatrixPath -PathType Leaf)) {
        throw "Native Steam 2026 parity matrix is unavailable: $ParityMatrixPath"
    }

    $resolvedPath = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $ParityMatrixPath).Path)
    try {
        $matrix = [IO.File]::ReadAllText($resolvedPath) | ConvertFrom-Json
    }
    catch {
        throw "Native Steam 2026 parity matrix is invalid JSON: $resolvedPath. $($_.Exception.Message)"
    }

    if ($matrix.schemaVersion -ne 1 -or $null -eq $matrix.policy -or $null -eq $matrix.runtimes -or
        $null -eq $matrix.runtimes.steam2026X64 -or $null -eq $matrix.releaseGate) {
        throw "Native Steam 2026 parity matrix has an unsupported schema or is missing required policy, runtime, or release-gate fields: $resolvedPath"
    }

    $capabilities = @($matrix.capabilities)
    if ($capabilities.Count -eq 0) {
        throw "Native Steam 2026 parity matrix contains no capability rows: $resolvedPath"
    }

    $capabilityNames = New-Object System.Collections.Generic.List[string]
    $uniqueCapabilityNames = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($capability in $capabilities) {
        $name = [string]$capability.capability
        if ([string]::IsNullOrWhiteSpace($name) -or -not $uniqueCapabilityNames.Add($name)) {
            throw "Native Steam 2026 parity matrix contains a blank or duplicate capability row: $resolvedPath"
        }
        $capabilityNames.Add($name)
    }
    $missingCapabilities = @($script:RequiredNativeCapabilities | Where-Object {
        -not $uniqueCapabilityNames.Contains($_)
    })
    $unknownCapabilities = @($capabilityNames | Where-Object {
        $script:RequiredNativeCapabilities -notcontains $_
    })
    if ($missingCapabilities.Count -gt 0 -or $unknownCapabilities.Count -gt 0 -or
        $capabilityNames.Count -ne $script:RequiredNativeCapabilities.Count) {
        throw "Native Steam 2026 parity matrix capability coverage is not exact: missing=$($missingCapabilities -join ','), unknown=$($unknownCapabilities -join ',')."
    }

    $blockingCapabilities = @(
        @($matrix.releaseGate.blockingCapabilities) |
            Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }
    )
    $disabledCapabilities = @($capabilities | Where-Object { $_.x64SpeechEnabled -ne $true } |
        ForEach-Object { [string]$_.capability })
    $releaseStatus = [string]$matrix.runtimes.steam2026X64.releaseStatus
    $runtimeId = [string]$matrix.runtimes.steam2026X64.runtimeId
    $runtimeSha256 = [string]$matrix.runtimes.steam2026X64.sha256
    $isReleaseReady =
        $runtimeId.Equals($script:Steam2026NativeRuntimeId, [StringComparison]::Ordinal) -and
        $runtimeSha256.Equals($script:Steam2026NativeSha256, [StringComparison]::OrdinalIgnoreCase) -and
        $matrix.policy.partialRuntimeMayBeReleased -eq $false -and
        $matrix.policy.staticEvidenceMayEnableSpeech -eq $false -and
        $matrix.releaseGate.steam2026X64Ready -eq $true -and
        $matrix.releaseGate.requiredUserLedValidation -eq $true -and
        $matrix.releaseGate.userLedValidationComplete -eq $true -and
        $blockingCapabilities.Count -eq 0 -and
        $disabledCapabilities.Count -eq 0 -and
        $releaseStatus.Equals('supported', [StringComparison]::Ordinal)

    if (-not $isReleaseReady -and -not $AllowResearch) {
        $blockerText = if ($blockingCapabilities.Count -gt 0) {
            $blockingCapabilities -join ', '
        }
        elseif ($disabledCapabilities.Count -gt 0) {
            $disabledCapabilities -join ', '
        }
        else {
            'release policy or user-led validation is incomplete'
        }
        throw "Native Steam 2026 release gate is closed ($blockerText). Use an explicit research override only for controlled validation."
    }

    return [pscustomobject]@{
        MatrixPath = $resolvedPath
        IsReleaseReady = $isReleaseReady
        IsResearchOverride = (-not $isReleaseReady -and [bool]$AllowResearch)
        BlockingCapabilities = $blockingCapabilities
        DisabledCapabilities = $disabledCapabilities
        CapabilityCount = $capabilities.Count
        ReleaseStatus = $releaseStatus
        RuntimeId = $runtimeId
        RuntimeSha256 = $runtimeSha256
    }
}

function Test-Ff7ExactOrderedStringList {
    param(
        [object[]] $Actual,
        [Parameter(Mandatory=$true)] [string[]] $Expected
    )

    $values = @($Actual)
    if ($values.Count -ne $Expected.Count) {
        return $false
    }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if ([string]$values[$index] -cne $Expected[$index]) {
            return $false
        }
    }
    return $true
}

function Install-Ff7NativeReloadedProfile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)] [string] $ReloadedRoot,
        [Parameter(Mandatory=$true)] [psobject] $NativeRuntime,
        [Parameter(Mandatory=$true)] [string] $TemplatePath,
        [string] $ParityMatrixPath = (Join-Path $script:ModuleRoot '..\analysis\dual_runtime\parity-matrix.json'),
        [switch] $AllowResearch,
        [switch] $ValidateOnly
    )

    $validatedRuntime = Assert-Ff7NativeRuntimeIdentity -Path ([string]$NativeRuntime.GameExe)
    $suppliedRuntimeRoot = [IO.Path]::GetFullPath([string]$NativeRuntime.RuntimeRoot).TrimEnd('\')
    if (-not $suppliedRuntimeRoot.Equals($validatedRuntime.RuntimeRoot.TrimEnd('\'),
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Native Reloaded profile runtime root does not match the validated executable identity.'
    }
    if (-not (Test-Path -LiteralPath $TemplatePath -PathType Leaf)) {
        throw "Native Reloaded profile template is missing: $TemplatePath"
    }
    $parityGate = Assert-Ff7NativeParityReleaseGate -ParityMatrixPath $ParityMatrixPath -AllowResearch:$AllowResearch

    $root = [IO.Path]::GetFullPath($ReloadedRoot)
    $legacyProfile = Join-Path $root 'Apps\Ff7.En.Steam\AppConfig.json'
    $legacyHash = if (Test-Path -LiteralPath $legacyProfile -PathType Leaf) {
        (Get-FileHash -LiteralPath $legacyProfile -Algorithm SHA256).Hash
    }
    else {
        $null
    }

    try {
        $profile = [IO.File]::ReadAllText($TemplatePath) | ConvertFrom-Json
    }
    catch {
        throw "Native Reloaded profile template is invalid JSON: $($_.Exception.Message)"
    }

    $requiredMods = @('reloaded.sharedlib.hooks', 'ff7.accessibility.reloaded')
    $enabledMods = @($profile.EnabledMods)
    $sortedMods = @($profile.SortedMods)
    $hasExactEnabledMods = Test-Ff7ExactOrderedStringList -Actual $enabledMods -Expected $requiredMods
    $hasExactSortedMods = Test-Ff7ExactOrderedStringList -Actual $sortedMods -Expected $requiredMods
    $templateIsValid =
        $profile.AppId -is [string] -and [string]$profile.AppId -ceq 'FFVII.exe' -and
        $profile.AppName -is [string] -and
            [string]$profile.AppName -ceq 'Final Fantasy VII (Steam 2026 Native x64)' -and
        $profile.AppLocation -is [string] -and [string]::IsNullOrWhiteSpace([string]$profile.AppLocation) -and
        $profile.WorkingDirectory -is [string] -and [string]::IsNullOrWhiteSpace([string]$profile.WorkingDirectory) -and
        $profile.AutoInject -eq $false -and
        $profile.DontInject -eq $false -and
        $profile.IsMsStore -eq $false -and
        $profile.PreserveDisabledModOrder -eq $true -and
        $hasExactEnabledMods -and $hasExactSortedMods
    if (-not $templateIsValid) {
        throw 'Native Reloaded profile template does not match the required accessibility injection contract.'
    }

    $profile.AppLocation = $validatedRuntime.GameExe
    $profile.WorkingDirectory = $validatedRuntime.RuntimeRoot
    if ($parityGate.IsResearchOverride) {
        $profile.AppName = 'RESEARCH ONLY - FFVII Steam 2026 - ACCESSIBILITY INCOMPLETE'
    }
    $profileJson = $profile | ConvertTo-Json -Depth 8
    $profileBytes = (New-Object Text.UTF8Encoding($false)).GetBytes($profileJson)

    $profileDirectoryName = if ($parityGate.IsResearchOverride) {
        'Ff7.Native.Steam2026.Research'
    }
    else {
        'Ff7.Native.Steam2026'
    }
    if ($parityGate.IsResearchOverride) {
        $ordinaryProfilePath = Join-Path $root 'Apps\Ff7.Native.Steam2026\AppConfig.json'
        if (Test-Path -LiteralPath $ordinaryProfilePath -PathType Leaf) {
            throw "An ordinary native profile already exists while the release gate is closed: $ordinaryProfilePath"
        }
    }
    $profileDirectory = Join-Path $root (Join-Path 'Apps' $profileDirectoryName)
    $profilePath = Join-Path $profileDirectory 'AppConfig.json'
    if ($ValidateOnly) {
        return [pscustomobject]@{
            Validated = $true
            ProfilePath = $profilePath
            IsResearchProfile = $parityGate.IsResearchOverride
            ExistingProfile = (Test-Path -LiteralPath $profilePath -PathType Leaf)
        }
    }
    New-Item -ItemType Directory -Path $profileDirectory -Force | Out-Null
    if (Test-Path -LiteralPath $profilePath -PathType Leaf) {
        $existingBytes = [IO.File]::ReadAllBytes($profilePath)
        if ([Convert]::ToBase64String($existingBytes) -eq [Convert]::ToBase64String($profileBytes)) {
            if ($null -ne $legacyHash -and
                (Get-FileHash -LiteralPath $legacyProfile -Algorithm SHA256).Hash -ne $legacyHash) {
                throw 'Protected legacy Reloaded profile changed during native profile verification.'
            }
            return [pscustomobject]@{
                Changed = $false
                ProfilePath = $profilePath
                BackupPath = $null
                IsResearchProfile = $parityGate.IsResearchOverride
            }
        }
    }

    $temporaryPath = Join-Path $profileDirectory ('.AppConfig.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $backupPath = if (Test-Path -LiteralPath $profilePath -PathType Leaf) {
        Join-Path $profileDirectory ('AppConfig.json.backup-' + [Guid]::NewGuid().ToString('N'))
    }
    else {
        $null
    }
    $writeCompleted = $false
    try {
        [IO.File]::WriteAllBytes($temporaryPath, $profileBytes)
        if ($null -ne $backupPath) {
            [IO.File]::Replace($temporaryPath, $profilePath, $backupPath, $true)
        }
        else {
            Move-Item -LiteralPath $temporaryPath -Destination $profilePath
        }
        $writeCompleted = $true

        $installedBytes = [IO.File]::ReadAllBytes($profilePath)
        if ([Convert]::ToBase64String($installedBytes) -ne
            [Convert]::ToBase64String($profileBytes)) {
            throw 'Native Reloaded profile failed post-install byte verification.'
        }
        if ($null -ne $legacyHash -and
            (Get-FileHash -LiteralPath $legacyProfile -Algorithm SHA256).Hash -ne $legacyHash) {
            throw 'Protected legacy Reloaded profile changed during native profile installation.'
        }
    }
    catch {
        $profileError = $_
        if ($writeCompleted) {
            try {
                if (Test-Path -LiteralPath $profilePath -PathType Leaf) {
                    Remove-Item -LiteralPath $profilePath -Force
                }
                if ($null -ne $backupPath -and
                    (Test-Path -LiteralPath $backupPath -PathType Leaf)) {
                    Move-Item -LiteralPath $backupPath -Destination $profilePath
                }
            }
            catch {
                throw "Native Reloaded profile installation failed and rollback also failed. Original: $($profileError.Exception.Message) Rollback: $($_.Exception.Message)"
            }
        }
        throw $profileError
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }

    return [pscustomobject]@{
        Changed = $true
        ProfilePath = $profilePath
        BackupPath = $backupPath
        IsResearchProfile = $parityGate.IsResearchOverride
    }
}

function Assert-Ff7NativeReloadedProfile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)] [string] $ReloadedRoot,
        [Parameter(Mandatory=$true)] [psobject] $NativeRuntime,
        [switch] $Research
    )

    $validatedRuntime = Assert-Ff7NativeRuntimeIdentity -Path ([string]$NativeRuntime.GameExe)
    $suppliedRuntimeRoot = [IO.Path]::GetFullPath([string]$NativeRuntime.RuntimeRoot).TrimEnd('\')
    if (-not $suppliedRuntimeRoot.Equals($validatedRuntime.RuntimeRoot.TrimEnd('\'),
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Native Reloaded profile preflight runtime root does not match the validated executable identity.'
    }

    $profileDirectoryName = if ($Research) {
        'Ff7.Native.Steam2026.Research'
    }
    else {
        'Ff7.Native.Steam2026'
    }
    $profilePath = Join-Path ([IO.Path]::GetFullPath($ReloadedRoot)) `
        (Join-Path 'Apps' (Join-Path $profileDirectoryName 'AppConfig.json'))
    if (-not (Test-Path -LiteralPath $profilePath -PathType Leaf)) {
        throw "Required native Reloaded profile is missing: $profilePath"
    }

    try {
        $profile = [IO.File]::ReadAllText($profilePath) | ConvertFrom-Json
    }
    catch {
        throw "Native Reloaded profile is invalid JSON: $profilePath. $($_.Exception.Message)"
    }

    $expectedName = if ($Research) {
        'RESEARCH ONLY - FFVII Steam 2026 - ACCESSIBILITY INCOMPLETE'
    }
    else {
        'Final Fantasy VII (Steam 2026 Native x64)'
    }
    $requiredMods = @('reloaded.sharedlib.hooks', 'ff7.accessibility.reloaded')
    try {
        $appLocation = [IO.Path]::GetFullPath([string]$profile.AppLocation)
        $workingDirectory = [IO.Path]::GetFullPath([string]$profile.WorkingDirectory).TrimEnd('\')
    }
    catch {
        throw "Native Reloaded profile contains an invalid executable or working-directory path: $profilePath"
    }

    $isValid =
        $profile.AppId -is [string] -and [string]$profile.AppId -ceq 'FFVII.exe' -and
        $profile.AppName -is [string] -and [string]$profile.AppName -ceq $expectedName -and
        $profile.AppLocation -is [string] -and
            $appLocation.Equals($validatedRuntime.GameExe, [StringComparison]::OrdinalIgnoreCase) -and
        $profile.WorkingDirectory -is [string] -and
            $workingDirectory.Equals($validatedRuntime.RuntimeRoot.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase) -and
        $profile.AutoInject -eq $false -and
        $profile.DontInject -eq $false -and
        $profile.IsMsStore -eq $false -and
        (Test-Ff7ExactOrderedStringList -Actual @($profile.EnabledMods) -Expected $requiredMods) -and
        (Test-Ff7ExactOrderedStringList -Actual @($profile.SortedMods) -Expected $requiredMods)
    if (-not $isValid) {
        throw "Native Reloaded profile does not match the validated runtime and accessibility injection contract: $profilePath"
    }

    return [pscustomobject]@{
        ProfilePath = $profilePath
        IsResearchProfile = [bool]$Research
        AppLocation = $appLocation
        WorkingDirectory = $workingDirectory
    }
}

Export-ModuleMember -Function @(
    'Get-Ff7SteamLibraryPaths',
    'Resolve-Ff7Installation',
    'Get-Ff7PeMachine',
    'Assert-Ff7NativeRuntimeIdentity',
    'Set-PeLargeAddressAware',
    'Initialize-Ff7CompatibilityRuntime',
    'Select-FfnxSteamAsset',
    'Install-FfnxSteamRuntime',
    'Install-Ff7OpeningMovieAudioDescription',
    'Disable-Ff7OpeningMovieNativeVoiceLayer',
    'Update-SeventhHeavenSettings',
    'Install-Ff7DualRuntimePackage',
    'Assert-Ff7NativeParityReleaseGate',
    'Install-Ff7NativeReloadedProfile',
    'Assert-Ff7NativeReloadedProfile'
)
