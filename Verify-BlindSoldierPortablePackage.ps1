[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $ArchivePath,
    [string] $ExpectedVersion = '0.1.0-pre.7'
)

$ErrorActionPreference = 'Stop'
$archivePathFull = [IO.Path]::GetFullPath($ArchivePath)
$sidecarPath = $archivePathFull + '.sha256'
if (-not (Test-Path -LiteralPath $archivePathFull -PathType Leaf)) {
    throw "Portable archive is missing: $archivePathFull"
}
if (-not (Test-Path -LiteralPath $sidecarPath -PathType Leaf)) {
    throw "Portable archive checksum is missing: $sidecarPath"
}

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

function Get-PeMachine {
    param([Parameter(Mandatory=$true)] [string] $Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "File is not a PE image: $Path"
    }
    $offset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($offset -lt 64 -or $offset + 6 -gt $bytes.Length -or
        [BitConverter]::ToUInt32($bytes, $offset) -ne 0x00004550) {
        throw "File has an invalid PE header: $Path"
    }
    return [BitConverter]::ToUInt16($bytes, $offset + 4)
}

function Assert-Machine {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [uint16] $Expected,
        [Parameter(Mandatory=$true)] [string] $Label
    )

    $actual = Get-PeMachine -Path $Path
    if ($actual -ne $Expected) {
        throw ("{0} has PE machine 0x{1:X4}; expected 0x{2:X4}." -f `
            $Label, $actual, $Expected)
    }
    return $actual
}

function Get-RelativePortablePath {
    param([string] $Root, [string] $Path)

    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "File escaped the verification root: $fullPath"
    }
    return $fullPath.Substring($prefix.Length).Replace('\', '/')
}

$verificationRoot = Join-Path ([IO.Path]::GetTempPath()) `
    ('blind-soldier-portable-verification-' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $verificationRoot | Out-Null
    Expand-Archive -LiteralPath $archivePathFull -DestinationPath $verificationRoot

    $manifestPath = Join-Path $verificationRoot 'portable-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw 'portable-manifest.json is missing.'
    }
    $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
    if ([int]$manifest.schemaVersion -ne 1 -or
        [string]$manifest.version -cne $ExpectedVersion) {
        throw 'Portable manifest identity does not match the requested release.'
    }

    $actualFiles = New-Object 'System.Collections.Generic.Dictionary[string,System.IO.FileInfo]' `
        ([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in @(Get-ChildItem -LiteralPath $verificationRoot -File -Recurse -Force)) {
        if ($file.FullName -ceq $manifestPath) { continue }
        $relative = Get-RelativePortablePath -Root $verificationRoot -Path $file.FullName
        if ($actualFiles.ContainsKey($relative)) {
            throw "Case-insensitive duplicate archive path: $relative"
        }
        $actualFiles.Add($relative, $file)
    }

    foreach ($record in @($manifest.files)) {
        $relative = [string]$record.path
        if (-not $actualFiles.ContainsKey($relative)) {
            throw "Manifest file is missing: $relative"
        }
        $file = $actualFiles[$relative]
        if ([int64]$record.length -ne [int64]$file.Length) {
            throw "Manifest length mismatch: $relative"
        }
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if (-not $hash.Equals([string]$record.sha256, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Manifest SHA-256 mismatch: $relative"
        }
        [void]$actualFiles.Remove($relative)
    }
    if ($actualFiles.Count -ne 0) {
        throw "Files are absent from the portable manifest: $($actualFiles.Keys -join ', ')"
    }

    foreach ($architecture in @('X86', 'X64')) {
        $root = Join-Path $verificationRoot "Reloaded-II\Loader\$architecture"
        $actual = @(Get-ChildItem -LiteralPath $root -File -Recurse | ForEach-Object {
            Get-RelativePortablePath -Root $root -Path $_.FullName
        })
        [string[]]$actualSorted = @($actual)
        [string[]]$expectedSorted = @($loaderFiles)
        [Array]::Sort($actualSorted, [StringComparer]::Ordinal)
        [Array]::Sort($expectedSorted, [StringComparer]::Ordinal)
        if (($actualSorted -join '|') -cne ($expectedSorted -join '|')) {
            throw "$architecture Reloaded loader closure is not exact."
        }

        $runtimeConfig = [IO.File]::ReadAllText(
            (Join-Path $root 'Reloaded.Mod.Loader.runtimeconfig.json')) | ConvertFrom-Json
        if ([string]$runtimeConfig.runtimeOptions.tfm -cne 'net9.0') {
            throw "$architecture Reloaded runtime target is not net9.0."
        }
    }

    $forbidden = @(Get-ChildItem -LiteralPath $verificationRoot -File -Recurse | Where-Object {
        $_.Name -ceq 'Reloaded-II.exe' -or
        $_.Name -like 'ASILoader*.dll' -or
        $_.Extension -in @('.pdb', '.obj', '.iobj', '.ipdb')
    })
    if ($forbidden.Count -ne 0) {
        throw "Portable archive contains forbidden files: $($forbidden.FullName -join ', ')"
    }

    $machines = [ordered]@{
        Installer = Assert-Machine -Path (Join-Path $verificationRoot 'Blind-Soldier-Installer.exe') -Expected 0x8664 -Label 'installer'
        LauncherX86 = Assert-Machine -Path (Join-Path $verificationRoot 'Blind-Soldier-Launcher-x86.exe') -Expected 0x014C -Label 'x86 launcher'
        LauncherX64 = Assert-Machine -Path (Join-Path $verificationRoot 'Blind-Soldier-Launcher-x64.exe') -Expected 0x8664 -Label 'x64 launcher'
        AccessibleLauncher = Assert-Machine -Path (Join-Path $verificationRoot 'FFVII_LAUNCHER.exe') -Expected 0x014C -Label 'accessible FFVII launcher'
        BootstrapperX86 = Assert-Machine -Path (Join-Path $verificationRoot 'Reloaded-II\Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Expected 0x014C -Label 'x86 bootstrapper'
        BootstrapperX64 = Assert-Machine -Path (Join-Path $verificationRoot 'Reloaded-II\Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Expected 0x8664 -Label 'x64 bootstrapper'
        ModX86 = Assert-Machine -Path (Join-Path $verificationRoot 'Reloaded-II\Mods\ff7.accessibility.reloaded\x86\Ff7.Accessibility.Reloaded.dll') -Expected 0x014C -Label 'x86 mod entry point'
        ModX64 = Assert-Machine -Path (Join-Path $verificationRoot 'Reloaded-II\Mods\ff7.accessibility.reloaded\x64\Ff7.Accessibility.Steam2026X64.dll') -Expected 0x8664 -Label 'x64 mod entry point'
    }

    $modConfig = [IO.File]::ReadAllText(
        (Join-Path $verificationRoot 'Reloaded-II\Mods\ff7.accessibility.reloaded\ModConfig.json')) | ConvertFrom-Json
    if ([string]$modConfig.ModId -cne 'ff7.accessibility.reloaded' -or
        @($modConfig.ModDependencies) -cnotcontains 'reloaded.sharedlib.hooks') {
        throw 'Blind Soldier mod metadata is invalid.'
    }
    $hooksConfig = [IO.File]::ReadAllText(
        (Join-Path $verificationRoot 'Reloaded-II\Mods\reloaded.sharedlib.hooks\ModConfig.json')) | ConvertFrom-Json
    if ([string]$hooksConfig.ModId -cne 'reloaded.sharedlib.hooks') {
        throw 'Shared Hooks metadata is invalid.'
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePathFull)
    try {
        $entryNames = @($archive.Entries | ForEach-Object FullName)
        [string[]]$sortedNames = @($entryNames)
        [Array]::Sort($sortedNames, [StringComparer]::Ordinal)
        if (($entryNames -join '|') -cne ($sortedNames -join '|')) {
            throw 'ZIP entries are not in deterministic ordinal order.'
        }
    }
    finally {
        $archive.Dispose()
    }

    $archiveHash = (Get-FileHash -LiteralPath $archivePathFull -Algorithm SHA256).Hash.ToUpperInvariant()
    $expectedSidecar = "$archiveHash  $([IO.Path]::GetFileName($archivePathFull))"
    if ([IO.File]::ReadAllText($sidecarPath).Trim() -cne $expectedSidecar) {
        throw 'The SHA-256 sidecar does not match the archive.'
    }

    [pscustomobject]@{
        ArchivePath = $archivePathFull
        Size = (Get-Item -LiteralPath $archivePathFull).Length
        Sha256 = $archiveHash
        Version = [string]$manifest.version
        ManifestFiles = @($manifest.files).Count
        LoaderFiles = 24
        ForbiddenFiles = 0
        Machines = [pscustomobject]$machines
        ModId = [string]$modConfig.ModId
        SharedHooksId = [string]$hooksConfig.ModId
        SidecarVerified = $true
        DeterministicEntryOrder = $true
    }
}
finally {
    $temporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $verificationFull = [IO.Path]::GetFullPath($verificationRoot)
    if ((Test-Path -LiteralPath $verificationFull -PathType Container) -and
        $verificationFull.StartsWith($temporaryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $verificationFull -Recurse -Force
    }
}
