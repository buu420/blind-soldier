Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Get-LauncherSha256 {
    param([Parameter(Mandatory=$true)] [string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Test-LauncherHashEqual {
    param(
        [AllowNull()] [string] $Left,
        [AllowNull()] [string] $Right
    )
    if ($null -eq $Left -or $null -eq $Right -or $Left.Length -ne $Right.Length) {
        return $false
    }
    $difference = 0
    for ($index = 0; $index -lt $Left.Length; $index++) {
        $difference = $difference -bor ([int][char]$Left[$index] -bxor [int][char]$Right[$index])
    }
    return $difference -eq 0
}

function Assert-LauncherExactProperties {
    param(
        [Parameter(Mandatory=$true)] [object] $Value,
        [Parameter(Mandatory=$true)] [string[]] $Expected,
        [Parameter(Mandatory=$true)] [string] $Label
    )
    $actual = @($Value.PSObject.Properties | ForEach-Object Name)
    if ($actual.Count -ne $Expected.Count -or
        @($actual | Where-Object { $Expected -cnotcontains $_ }).Count -ne 0 -or
        @($Expected | Where-Object { $actual -cnotcontains $_ }).Count -ne 0) {
        throw "$Label properties are invalid."
    }
}

function Assert-LauncherRegularFile {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [string] $Label
    )
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label is missing: $Path"
    }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label cannot be a reparse point: $Path"
    }
    return $item
}

function Assert-LauncherDirectory {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [string] $Label
    )
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Label is missing: $Path"
    }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label cannot be a reparse point: $Path"
    }
    return $item
}

function Test-LauncherPathWithin {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [string] $Root
    )
    $fullPath = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    return $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-LauncherPeMachine {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [uint16] $ExpectedMachine,
        [Parameter(Mandatory=$true)] [string] $Label
    )
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "$Label is not a PE image: $Path"
    }
    $offset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($offset -lt 64 -or $offset + 6 -gt $bytes.Length -or
        [BitConverter]::ToUInt32($bytes, $offset) -ne 0x00004550) {
        throw "$Label has an invalid PE header: $Path"
    }
    $machine = [BitConverter]::ToUInt16($bytes, $offset + 4)
    if ($machine -ne $ExpectedMachine) {
        throw ("{0} has PE machine 0x{1:X4}; expected 0x{2:X4}: {3}" -f $Label, $machine, $ExpectedMachine, $Path)
    }
}

function Get-LauncherDescriptor {
    param(
        [Parameter(Mandatory=$true)] [object] $Descriptor,
        [Parameter(Mandatory=$true)] [string] $ExpectedName,
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [string] $Label
    )
    Assert-LauncherExactProperties -Value $Descriptor -Expected @('name', 'size', 'sha256') -Label $Label
    if ([string]$Descriptor.name -cne $ExpectedName -or
        [string]$Descriptor.sha256 -notmatch '^[0-9A-F]{64}$' -or
        [int64]$Descriptor.size -le 0) {
        throw "$Label metadata is invalid."
    }
    $item = Assert-LauncherRegularFile -Path $Path -Label $Label
    if ([int64]$item.Length -ne [int64]$Descriptor.size -or
        -not (Test-LauncherHashEqual (Get-LauncherSha256 -Path $item.FullName) ([string]$Descriptor.sha256))) {
        throw "$Label length or SHA-256 does not match its manifest."
    }
    return [pscustomobject]@{
        Name = $ExpectedName
        Path = [IO.Path]::GetFullPath($item.FullName)
        Size = [int64]$item.Length
        Sha256 = [string]$Descriptor.sha256
    }
}

function Test-Ff7AccessibleLauncherBundle {
    [CmdletBinding()]
    param([Parameter(Mandatory=$true)] [string] $BundlePath)

    $root = [IO.Path]::GetFullPath($BundlePath).TrimEnd('\')
    [void](Assert-LauncherDirectory -Path $root -Label 'Accessible launcher bundle')
    $expectedFiles = @(
        'FFVII_LAUNCHER.exe',
        'FFVII_LAUNCHER.exe.config',
        'launcher-bundle.json',
        'native\x86\FFVII_LAUNCHER.prism.x86.dll'
    )
    $actualFiles = New-Object 'System.Collections.Generic.List[string]'
    foreach ($file in @(Get-ChildItem -LiteralPath $root -File -Recurse -Force)) {
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            -not (Test-LauncherPathWithin -Path $file.FullName -Root $root)) {
            throw "Accessible launcher bundle contains an unsafe file: $($file.FullName)"
        }
        $actualFiles.Add($file.FullName.Substring($root.Length + 1))
    }
    if ($actualFiles.Count -ne $expectedFiles.Count -or
        @($actualFiles | Where-Object { $expectedFiles -cnotcontains $_ }).Count -ne 0) {
        throw 'Accessible launcher bundle contains missing or unexpected files.'
    }

    $manifestPath = Join-Path $root 'launcher-bundle.json'
    try {
        $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
    }
    catch {
        throw "Accessible launcher bundle manifest is invalid JSON: $($_.Exception.Message)"
    }
    Assert-LauncherExactProperties -Value $manifest -Expected @(
        'schemaVersion', 'stockLauncherSha256', 'launcher', 'config', 'prism',
        'assemblyName', 'assemblyVersion') -Label 'Accessible launcher bundle manifest'
    if ([int]$manifest.schemaVersion -ne 1 -or
        [string]$manifest.stockLauncherSha256 -notmatch '^[0-9A-F]{64}$' -or
        [string]$manifest.assemblyName -cne 'FFVII_LAUNCHER' -or
        [string]$manifest.assemblyVersion -cne '2.0.0.0') {
        throw 'Accessible launcher bundle identity is invalid.'
    }

    $launcher = Get-LauncherDescriptor -Descriptor $manifest.launcher -ExpectedName 'FFVII_LAUNCHER.exe' `
        -Path (Join-Path $root 'FFVII_LAUNCHER.exe') -Label 'Accessible launcher executable'
    $config = Get-LauncherDescriptor -Descriptor $manifest.config -ExpectedName 'FFVII_LAUNCHER.exe.config' `
        -Path (Join-Path $root 'FFVII_LAUNCHER.exe.config') -Label 'Accessible launcher configuration'
    $prism = Get-LauncherDescriptor -Descriptor $manifest.prism -ExpectedName 'FFVII_LAUNCHER.prism.x86.dll' `
        -Path (Join-Path $root 'native\x86\FFVII_LAUNCHER.prism.x86.dll') -Label 'Accessible launcher Prism library'
    Assert-LauncherPeMachine -Path $launcher.Path -ExpectedMachine 0x014C -Label 'Accessible launcher executable'
    Assert-LauncherPeMachine -Path $prism.Path -ExpectedMachine 0x014C -Label 'Accessible launcher Prism library'
    try {
        $identity = [Reflection.AssemblyName]::GetAssemblyName($launcher.Path)
    }
    catch {
        throw "Accessible launcher managed identity cannot be read: $($_.Exception.Message)"
    }
    if ($identity.Name -cne [string]$manifest.assemblyName -or
        $identity.Version.ToString() -cne [string]$manifest.assemblyVersion) {
        throw 'Accessible launcher managed identity does not match its bundle manifest.'
    }

    return [pscustomobject]@{
        Root = $root
        ManifestPath = $manifestPath
        StockLauncherSha256 = [string]$manifest.stockLauncherSha256
        Launcher = $launcher
        Configuration = $config
        Prism = $prism
    }
}

function New-LauncherFileState {
    param(
        [Parameter(Mandatory=$true)] [string] $Target,
        [Parameter(Mandatory=$true)] [string] $InstalledSha256,
        [Parameter(Mandatory=$true)] [bool] $Changed,
        [AllowNull()] [string] $BackupPath,
        [AllowNull()] [string] $BackupSha256
    )
    return [pscustomobject][ordered]@{
        target = [IO.Path]::GetFullPath($Target)
        installedSha256 = $InstalledSha256.ToUpperInvariant()
        changed = $Changed
        backupPath = if ([string]::IsNullOrWhiteSpace($BackupPath)) { $null } else { [IO.Path]::GetFullPath($BackupPath) }
        backupSha256 = if ([string]::IsNullOrWhiteSpace($BackupSha256)) { $null } else { $BackupSha256.ToUpperInvariant() }
    }
}

function ConvertTo-NormalizedLauncherFileState {
    param(
        [Parameter(Mandatory=$true)] [object] $Value,
        [Parameter(Mandatory=$true)] [string] $ExpectedTarget,
        [Parameter(Mandatory=$true)] [string] $BackupRoot,
        [Parameter(Mandatory=$true)] [string] $Label
    )
    Assert-LauncherExactProperties -Value $Value -Expected @(
        'target', 'installedSha256', 'changed', 'backupPath', 'backupSha256') -Label $Label
    $target = [IO.Path]::GetFullPath([string]$Value.target)
    if (-not $target.Equals([IO.Path]::GetFullPath($ExpectedTarget), [StringComparison]::OrdinalIgnoreCase) -or
        [string]$Value.installedSha256 -notmatch '^[0-9A-Fa-f]{64}$') {
        throw "$Label target or installed hash is invalid."
    }
    $backupPath = if ([string]::IsNullOrWhiteSpace([string]$Value.backupPath)) { $null } else { [IO.Path]::GetFullPath([string]$Value.backupPath) }
    $backupHash = if ([string]::IsNullOrWhiteSpace([string]$Value.backupSha256)) { $null } else { [string]$Value.backupSha256 }
    if (($null -eq $backupPath) -ne ($null -eq $backupHash) -or
        ($null -ne $backupHash -and $backupHash -notmatch '^[0-9A-Fa-f]{64}$') -or
        ($null -ne $backupPath -and -not (Test-LauncherPathWithin -Path $backupPath -Root $BackupRoot))) {
        throw "$Label backup identity is invalid."
    }
    return New-LauncherFileState -Target $target -InstalledSha256 ([string]$Value.installedSha256) `
        -Changed ([bool]$Value.changed) -BackupPath $backupPath -BackupSha256 $backupHash
}

function Read-ManagedLauncherManifest {
    param(
        [Parameter(Mandatory=$true)] [string] $ManifestPath,
        [Parameter(Mandatory=$true)] [string] $GameRoot,
        [Parameter(Mandatory=$true)] [string] $BackupRoot,
        [Parameter(Mandatory=$true)] [string] $StockLauncherSha256,
        [Parameter(Mandatory=$true)] [hashtable] $Targets
    )
    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        return $null
    }
    [void](Assert-LauncherRegularFile -Path $ManifestPath -Label 'Launcher accessibility ownership manifest')
    try {
        $manifest = [IO.File]::ReadAllText($ManifestPath) | ConvertFrom-Json
    }
    catch {
        throw "Launcher accessibility ownership manifest is invalid JSON: $ManifestPath"
    }
    $schemaVersion = [int]$manifest.schemaVersion
    if ($schemaVersion -eq 1) {
        Assert-LauncherExactProperties -Value $manifest -Expected @(
            'SchemaVersion', 'InstalledAt', 'GameRoot', 'LauncherPath',
            'OriginalStockSha256', 'OriginalStockBackupPath', 'ReplacedLauncherSha256',
            'InstalledLauncherSha256', 'LauncherConfigPath', 'InstalledLauncherConfigSha256',
            'PrismPath', 'PrismSha256', 'PrismArchitecture') `
            -Label 'Legacy launcher accessibility ownership manifest'
        if (-not ([IO.Path]::GetFullPath([string]$manifest.GameRoot)).Equals(
                [IO.Path]::GetFullPath($GameRoot), [StringComparison]::OrdinalIgnoreCase) -or
            -not ([IO.Path]::GetFullPath([string]$manifest.LauncherPath)).Equals(
                [IO.Path]::GetFullPath($Targets.Executable), [StringComparison]::OrdinalIgnoreCase) -or
            -not ([IO.Path]::GetFullPath([string]$manifest.LauncherConfigPath)).Equals(
                [IO.Path]::GetFullPath($Targets.Configuration), [StringComparison]::OrdinalIgnoreCase) -or
            -not ([IO.Path]::GetFullPath([string]$manifest.PrismPath)).Equals(
                [IO.Path]::GetFullPath($Targets.Prism), [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Legacy launcher accessibility ownership manifest paths are invalid.'
        }
        if ([string]::IsNullOrWhiteSpace([string]$manifest.InstalledAt) -or
            [string]$manifest.OriginalStockSha256 -notmatch '^[0-9A-Fa-f]{64}$' -or
            [string]$manifest.ReplacedLauncherSha256 -notmatch '^[0-9A-Fa-f]{64}$' -or
            [string]$manifest.InstalledLauncherSha256 -notmatch '^[0-9A-Fa-f]{64}$' -or
            [string]$manifest.InstalledLauncherConfigSha256 -notmatch '^[0-9A-Fa-f]{64}$' -or
            [string]$manifest.PrismSha256 -notmatch '^[0-9A-Fa-f]{64}$' -or
            [string]$manifest.PrismArchitecture -cne 'x86' -or
            -not (Test-LauncherHashEqual ([string]$manifest.OriginalStockSha256) $StockLauncherSha256)) {
            throw 'Legacy launcher accessibility ownership manifest identity is invalid.'
        }
        $legacyBackup = [IO.Path]::GetFullPath([string]$manifest.OriginalStockBackupPath)
        [void](Assert-LauncherRegularFile -Path $legacyBackup -Label 'Legacy stock launcher backup')
        if (-not (Test-LauncherHashEqual (Get-LauncherSha256 -Path $legacyBackup) $StockLauncherSha256)) {
            throw 'Legacy stock launcher backup does not match the supported stock launcher.'
        }
        return [pscustomobject]@{
            IsLegacy = $true
            LegacyStockBackupPath = $legacyBackup
            InstalledAtUtc = [string]$manifest.InstalledAt
            Executable = New-LauncherFileState -Target $Targets.Executable `
                -InstalledSha256 ([string]$manifest.InstalledLauncherSha256) `
                -Changed $true -BackupPath $null -BackupSha256 $null
            Configuration = New-LauncherFileState -Target $Targets.Configuration `
                -InstalledSha256 ([string]$manifest.InstalledLauncherConfigSha256) `
                -Changed $true -BackupPath $null -BackupSha256 $null
            Prism = New-LauncherFileState -Target $Targets.Prism `
                -InstalledSha256 ([string]$manifest.PrismSha256) `
                -Changed $true -BackupPath $null -BackupSha256 $null
        }
    }
    if ($schemaVersion -ne 2) {
        throw "Unsupported launcher accessibility ownership manifest schema: $schemaVersion"
    }
    Assert-LauncherExactProperties -Value $manifest -Expected @(
        'schemaVersion', 'installedAtUtc', 'gameRoot', 'stockLauncherSha256', 'files') `
        -Label 'Launcher accessibility ownership manifest'
    Assert-LauncherExactProperties -Value $manifest.files -Expected @(
        'executable', 'configuration', 'prism') -Label 'Launcher accessibility ownership files'
    if (-not ([IO.Path]::GetFullPath([string]$manifest.gameRoot)).Equals(
            [IO.Path]::GetFullPath($GameRoot), [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-LauncherHashEqual ([string]$manifest.stockLauncherSha256) $StockLauncherSha256)) {
        throw 'Launcher accessibility ownership manifest belongs to another game or stock launcher.'
    }
    return [pscustomobject]@{
        IsLegacy = $false
        LegacyStockBackupPath = $null
        InstalledAtUtc = [string]$manifest.installedAtUtc
        Executable = ConvertTo-NormalizedLauncherFileState -Value $manifest.files.executable `
            -ExpectedTarget $Targets.Executable -BackupRoot $BackupRoot -Label 'Managed launcher executable'
        Configuration = ConvertTo-NormalizedLauncherFileState -Value $manifest.files.configuration `
            -ExpectedTarget $Targets.Configuration -BackupRoot $BackupRoot -Label 'Managed launcher configuration'
        Prism = ConvertTo-NormalizedLauncherFileState -Value $manifest.files.prism `
            -ExpectedTarget $Targets.Prism -BackupRoot $BackupRoot -Label 'Managed launcher Prism library'
    }
}

function New-LauncherSnapshot {
    param([Parameter(Mandatory=$true)] [hashtable] $Targets)
    $root = Join-Path ([IO.Path]::GetTempPath()) ('blind-swordsman-launcher-transaction-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root | Out-Null
    $entries = New-Object 'System.Collections.Generic.List[object]'
    $number = 0
    foreach ($name in @('Executable', 'Configuration', 'Prism', 'Manifest')) {
        $target = [IO.Path]::GetFullPath([string]$Targets[$name])
        $exists = Test-Path -LiteralPath $target -PathType Leaf
        $snapshot = $null
        if ($exists) {
            [void](Assert-LauncherRegularFile -Path $target -Label "Launcher transaction target $name")
            $snapshot = Join-Path $root (('{0:D2}-{1}.snapshot' -f $number, $name))
            Copy-Item -LiteralPath $target -Destination $snapshot
        }
        $entries.Add([pscustomobject]@{
            Name = $name
            Target = $target
            Existed = [bool]$exists
            Snapshot = $snapshot
            Sha256 = if ($exists) { Get-LauncherSha256 -Path $snapshot } else { $null }
        })
        $number++
    }
    return [pscustomobject]@{ Root = $root; Entries = $entries.ToArray() }
}

function Copy-LauncherFileAtomically {
    param(
        [Parameter(Mandatory=$true)] [string] $Source,
        [Parameter(Mandatory=$true)] [string] $Target,
        [Parameter(Mandatory=$true)] [string] $ExpectedSha256
    )
    $parent = Split-Path -Parent $Target
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    [void](Assert-LauncherDirectory -Path $parent -Label 'Launcher target directory')
    $temporary = Join-Path $parent ('.blind-swordsman-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $replaceBackup = $null
    try {
        Copy-Item -LiteralPath $Source -Destination $temporary
        if (-not (Test-LauncherHashEqual (Get-LauncherSha256 -Path $temporary) $ExpectedSha256)) {
            throw "Launcher temporary copy hash mismatch: $Target"
        }
        if (Test-Path -LiteralPath $Target -PathType Leaf) {
            $replaceBackup = Join-Path $parent ('.blind-swordsman-replace-' + [Guid]::NewGuid().ToString('N') + '.bak')
            [IO.File]::Replace($temporary, $Target, $replaceBackup, $true)
        }
        else {
            Move-Item -LiteralPath $temporary -Destination $Target
        }
        if (-not (Test-LauncherHashEqual (Get-LauncherSha256 -Path $Target) $ExpectedSha256)) {
            throw "Launcher installed file hash mismatch: $Target"
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporary -PathType Leaf) {
            Remove-Item -LiteralPath $temporary -Force
        }
        if (-not [string]::IsNullOrWhiteSpace($replaceBackup) -and
            (Test-Path -LiteralPath $replaceBackup -PathType Leaf)) {
            Remove-Item -LiteralPath $replaceBackup -Force
        }
    }
}

function Write-LauncherTextAtomically {
    param(
        [Parameter(Mandatory=$true)] [string] $Target,
        [Parameter(Mandatory=$true)] [string] $Text
    )
    $parent = Split-Path -Parent $Target
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    [void](Assert-LauncherDirectory -Path $parent -Label 'Launcher manifest directory')
    $temporary = Join-Path $parent ('.launcher-manifest-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $replaceBackup = $null
    try {
        [IO.File]::WriteAllText($temporary, $Text, (New-Object Text.UTF8Encoding($false)))
        if (Test-Path -LiteralPath $Target -PathType Leaf) {
            $replaceBackup = Join-Path $parent ('.launcher-manifest-replace-' + [Guid]::NewGuid().ToString('N') + '.bak')
            [IO.File]::Replace($temporary, $Target, $replaceBackup, $true)
        }
        else {
            Move-Item -LiteralPath $temporary -Destination $Target
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporary -PathType Leaf) {
            Remove-Item -LiteralPath $temporary -Force
        }
        if (-not [string]::IsNullOrWhiteSpace($replaceBackup) -and
            (Test-Path -LiteralPath $replaceBackup -PathType Leaf)) {
            Remove-Item -LiteralPath $replaceBackup -Force
        }
    }
}

function Restore-LauncherSnapshot {
    param([Parameter(Mandatory=$true)] [object] $Transaction)
    foreach ($entry in @($Transaction.Entries)) {
        if ([bool]$entry.Existed) {
            if (-not (Test-Path -LiteralPath ([string]$entry.Snapshot) -PathType Leaf) -or
                -not (Test-LauncherHashEqual (Get-LauncherSha256 -Path ([string]$entry.Snapshot)) ([string]$entry.Sha256))) {
                throw "Launcher transaction snapshot is missing or changed: $($entry.Snapshot)"
            }
            Copy-LauncherFileAtomically -Source ([string]$entry.Snapshot) -Target ([string]$entry.Target) `
                -ExpectedSha256 ([string]$entry.Sha256)
        }
        elseif (Test-Path -LiteralPath ([string]$entry.Target) -PathType Leaf) {
            Remove-Item -LiteralPath ([string]$entry.Target) -Force
        }
    }
}

function Remove-LauncherTransactionRoot {
    param([Parameter(Mandatory=$true)] [object] $Transaction)
    $root = [IO.Path]::GetFullPath([string]$Transaction.Root)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $root.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not ([IO.Path]::GetFileName($root)).StartsWith('blind-swordsman-launcher-transaction-', [StringComparison]::Ordinal)) {
        throw "Refusing unsafe launcher transaction cleanup: $root"
    }
    if (Test-Path -LiteralPath $root -PathType Container) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}

function Get-ExistingFileHashOrNull {
    param([Parameter(Mandatory=$true)] [string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }
    [void](Assert-LauncherRegularFile -Path $Path -Label 'Existing launcher file')
    return Get-LauncherSha256 -Path $Path
}

function Copy-PersistentLauncherBackup {
    param(
        [Parameter(Mandatory=$true)] [string] $Source,
        [Parameter(Mandatory=$true)] [string] $BackupDirectory,
        [Parameter(Mandatory=$true)] [string] $LeafName
    )
    if (-not (Test-Path -LiteralPath $BackupDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null
    }
    [void](Assert-LauncherDirectory -Path $BackupDirectory -Label 'Launcher persistent backup directory')
    $target = Join-Path $BackupDirectory $LeafName
    if (Test-Path -LiteralPath $target) {
        throw "Launcher backup collision: $target"
    }
    Copy-Item -LiteralPath $Source -Destination $target
    $sourceHash = Get-LauncherSha256 -Path $Source
    if (-not (Test-LauncherHashEqual (Get-LauncherSha256 -Path $target) $sourceHash)) {
        throw "Launcher persistent backup verification failed: $target"
    }
    return [pscustomobject]@{ Path = $target; Sha256 = $sourceHash }
}

function Install-Ff7AccessibleLauncher {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)] [string] $GameRoot,
        [Parameter(Mandatory=$true)] [string] $ReloadedRoot,
        [Parameter(Mandatory=$true)] [string] $BundlePath,
        [switch] $ValidateOnly
    )

    $game = [IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
    $reloaded = [IO.Path]::GetFullPath($ReloadedRoot).TrimEnd('\')
    [void](Assert-LauncherDirectory -Path $game -Label 'Final Fantasy VII game root')
    [void](Assert-LauncherDirectory -Path $reloaded -Label 'Reloaded-II root')
    $bundle = Test-Ff7AccessibleLauncherBundle -BundlePath $BundlePath
    $targets = @{
        Executable = Join-Path $game 'FFVII_LAUNCHER.exe'
        Configuration = Join-Path $game 'FFVII_LAUNCHER.exe.config'
        Prism = Join-Path $game 'launcher_accessibility\native\x86\FFVII_LAUNCHER.prism.x86.dll'
        Manifest = Join-Path $game 'launcher_accessibility\install-manifest.json'
    }
    [void](Assert-LauncherRegularFile -Path $targets.Executable -Label 'FFVII Steam launcher')
    Assert-LauncherPeMachine -Path $targets.Executable -ExpectedMachine 0x014C -Label 'FFVII Steam launcher'
    $running = @(Get-Process -Name 'FFVII', 'FFVII_LAUNCHER' -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        throw "Close FFVII and FFVII_LAUNCHER before installation. Running process IDs: $($running.Id -join ', ')"
    }

    $backupRoot = Join-Path $reloaded 'AccessibilityBackups'
    $existingOwnership = Read-ManagedLauncherManifest -ManifestPath $targets.Manifest -GameRoot $game `
        -BackupRoot $backupRoot -StockLauncherSha256 $bundle.StockLauncherSha256 -Targets $targets
    $targetHash = Get-LauncherSha256 -Path $targets.Executable
    $isStock = Test-LauncherHashEqual $targetHash $bundle.StockLauncherSha256
    $isCurrent = Test-LauncherHashEqual $targetHash $bundle.Launcher.Sha256
    $isManagedPrior = $null -ne $existingOwnership -and
        (Test-LauncherHashEqual $targetHash ([string]$existingOwnership.Executable.installedSha256))
    if (-not ($isStock -or $isCurrent -or $isManagedPrior)) {
        throw "Refusing unknown FFVII_LAUNCHER.exe identity: $targetHash"
    }

    $currentConfigHash = Get-ExistingFileHashOrNull -Path $targets.Configuration
    $currentPrismHash = Get-ExistingFileHashOrNull -Path $targets.Prism
    if ($null -ne $existingOwnership -and [bool]$existingOwnership.IsLegacy) {
        if ($null -ne $currentConfigHash -and
            -not (Test-LauncherHashEqual $currentConfigHash ([string]$existingOwnership.Configuration.installedSha256)) -and
            -not (Test-LauncherHashEqual $currentConfigHash $bundle.Configuration.Sha256)) {
            throw "Refusing changed legacy FFVII launcher configuration: $currentConfigHash"
        }
        if ($null -ne $currentPrismHash -and
            -not (Test-LauncherHashEqual $currentPrismHash ([string]$existingOwnership.Prism.installedSha256)) -and
            -not (Test-LauncherHashEqual $currentPrismHash $bundle.Prism.Sha256)) {
            throw "Refusing changed legacy FFVII launcher Prism library: $currentPrismHash"
        }
    }
    $executableState = if ($null -ne $existingOwnership) {
        New-LauncherFileState -Target $targets.Executable -InstalledSha256 $bundle.Launcher.Sha256 `
            -Changed ([bool]$existingOwnership.Executable.changed) `
            -BackupPath ([string]$existingOwnership.Executable.backupPath) `
            -BackupSha256 ([string]$existingOwnership.Executable.backupSha256)
    }
    else {
        New-LauncherFileState -Target $targets.Executable -InstalledSha256 $bundle.Launcher.Sha256 `
            -Changed ([bool]$isStock) -BackupPath $null -BackupSha256 $null
    }
    $configurationState = if ($null -ne $existingOwnership) {
        New-LauncherFileState -Target $targets.Configuration -InstalledSha256 $bundle.Configuration.Sha256 `
            -Changed ([bool]$existingOwnership.Configuration.changed) `
            -BackupPath ([string]$existingOwnership.Configuration.backupPath) `
            -BackupSha256 ([string]$existingOwnership.Configuration.backupSha256)
    }
    else {
        New-LauncherFileState -Target $targets.Configuration -InstalledSha256 $bundle.Configuration.Sha256 `
            -Changed (-not (Test-LauncherHashEqual $currentConfigHash $bundle.Configuration.Sha256)) `
            -BackupPath $null -BackupSha256 $null
    }
    $prismState = if ($null -ne $existingOwnership) {
        New-LauncherFileState -Target $targets.Prism -InstalledSha256 $bundle.Prism.Sha256 `
            -Changed ([bool]$existingOwnership.Prism.changed) `
            -BackupPath ([string]$existingOwnership.Prism.backupPath) `
            -BackupSha256 ([string]$existingOwnership.Prism.backupSha256)
    }
    else {
        New-LauncherFileState -Target $targets.Prism -InstalledSha256 $bundle.Prism.Sha256 `
            -Changed (-not (Test-LauncherHashEqual $currentPrismHash $bundle.Prism.Sha256)) `
            -BackupPath $null -BackupSha256 $null
    }

    $fileChangesNeeded = -not $isCurrent -or
        -not (Test-LauncherHashEqual $currentConfigHash $bundle.Configuration.Sha256) -or
        -not (Test-LauncherHashEqual $currentPrismHash $bundle.Prism.Sha256)
    if ($ValidateOnly) {
        return [pscustomobject]@{
            Changed = [bool]$fileChangesNeeded
            State = [pscustomobject]@{
                stockLauncherSha256 = $bundle.StockLauncherSha256
                executable = $executableState
                configuration = $configurationState
                prism = $prismState
                manifestPath = $targets.Manifest
                manifestSha256 = $null
            }
            Transaction = $null
            ValidatedOnly = $true
        }
    }

    $transaction = New-LauncherSnapshot -Targets $targets
    try {
        $backupDirectory = $null
        if ($null -ne $existingOwnership -and [bool]$existingOwnership.IsLegacy) {
            if (-not (Test-Path -LiteralPath $backupRoot -PathType Container)) {
                New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
            }
            [void](Assert-LauncherDirectory -Path $backupRoot -Label 'Reloaded-II accessibility backup root')
            $backupDirectory = Join-Path $backupRoot ('ff7-launcher.backup-' + [Guid]::NewGuid().ToString('N'))
            $migratedBackup = Copy-PersistentLauncherBackup `
                -Source ([string]$existingOwnership.LegacyStockBackupPath) `
                -BackupDirectory $backupDirectory -LeafName 'FFVII_LAUNCHER.exe'
            $executableState.backupPath = [IO.Path]::GetFullPath($migratedBackup.Path)
            $executableState.backupSha256 = [string]$migratedBackup.Sha256
        }
        foreach ($candidate in @(
            [pscustomobject]@{ State = $executableState; CurrentHash = $targetHash; Leaf = 'FFVII_LAUNCHER.exe' },
            [pscustomobject]@{ State = $configurationState; CurrentHash = $currentConfigHash; Leaf = 'FFVII_LAUNCHER.exe.config' },
            [pscustomobject]@{ State = $prismState; CurrentHash = $currentPrismHash; Leaf = 'FFVII_LAUNCHER.prism.x86.dll' }
        )) {
            if ($null -ne $existingOwnership -or -not [bool]$candidate.State.changed -or $null -eq $candidate.CurrentHash -or
                -not [string]::IsNullOrWhiteSpace([string]$candidate.State.backupPath)) {
                continue
            }
            if ($null -eq $backupDirectory) {
                if (-not (Test-Path -LiteralPath $backupRoot -PathType Container)) {
                    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
                }
                [void](Assert-LauncherDirectory -Path $backupRoot -Label 'Reloaded-II accessibility backup root')
                $backupDirectory = Join-Path $backupRoot ('ff7-launcher.backup-' + [Guid]::NewGuid().ToString('N'))
            }
            $backup = Copy-PersistentLauncherBackup -Source ([string]$candidate.State.target) `
                -BackupDirectory $backupDirectory -LeafName ([string]$candidate.Leaf)
            $candidate.State.backupPath = [IO.Path]::GetFullPath($backup.Path)
            $candidate.State.backupSha256 = [string]$backup.Sha256
        }

        if (-not $isCurrent) {
            Copy-LauncherFileAtomically -Source $bundle.Launcher.Path -Target $targets.Executable `
                -ExpectedSha256 $bundle.Launcher.Sha256
        }
        if (-not (Test-LauncherHashEqual $currentConfigHash $bundle.Configuration.Sha256)) {
            Copy-LauncherFileAtomically -Source $bundle.Configuration.Path -Target $targets.Configuration `
                -ExpectedSha256 $bundle.Configuration.Sha256
        }
        if (-not (Test-LauncherHashEqual $currentPrismHash $bundle.Prism.Sha256)) {
            Copy-LauncherFileAtomically -Source $bundle.Prism.Path -Target $targets.Prism `
                -ExpectedSha256 $bundle.Prism.Sha256
        }

        $installedAt = if ($null -ne $existingOwnership -and
            -not [string]::IsNullOrWhiteSpace([string]$existingOwnership.InstalledAtUtc)) {
            [string]$existingOwnership.InstalledAtUtc
        }
        else { [DateTime]::UtcNow.ToString('O') }
        $ownershipManifest = [ordered]@{
            schemaVersion = 2
            installedAtUtc = $installedAt
            gameRoot = $game
            stockLauncherSha256 = $bundle.StockLauncherSha256
            files = [ordered]@{
                executable = $executableState
                configuration = $configurationState
                prism = $prismState
            }
        }
        $manifestText = $ownershipManifest | ConvertTo-Json -Depth 8
        $manifestChanged = -not (Test-Path -LiteralPath $targets.Manifest -PathType Leaf) -or
            [IO.File]::ReadAllText($targets.Manifest) -cne $manifestText
        if ($manifestChanged) {
            Write-LauncherTextAtomically -Target $targets.Manifest -Text $manifestText
        }
        $manifestHash = Get-LauncherSha256 -Path $targets.Manifest
        $state = [pscustomobject][ordered]@{
            stockLauncherSha256 = $bundle.StockLauncherSha256
            executable = $executableState
            configuration = $configurationState
            prism = $prismState
            manifestPath = [IO.Path]::GetFullPath($targets.Manifest)
            manifestSha256 = $manifestHash
        }
        return [pscustomobject]@{
            Changed = [bool]($fileChangesNeeded -or $manifestChanged)
            State = $state
            Transaction = $transaction
            ValidatedOnly = $false
        }
    }
    catch {
        $installError = $_
        try { Restore-LauncherSnapshot -Transaction $transaction } catch { }
        try { Remove-LauncherTransactionRoot -Transaction $transaction } catch { }
        throw $installError
    }
}

function Undo-Ff7AccessibleLauncherTransaction {
    [CmdletBinding()]
    param([Parameter(Mandatory=$true)] [object] $Result)
    if ($null -eq $Result.Transaction) {
        return
    }
    try {
        Restore-LauncherSnapshot -Transaction $Result.Transaction
    }
    finally {
        Remove-LauncherTransactionRoot -Transaction $Result.Transaction
    }
}

function Complete-Ff7AccessibleLauncherTransaction {
    [CmdletBinding()]
    param([Parameter(Mandatory=$true)] [object] $Result)
    if ($null -ne $Result.Transaction) {
        Remove-LauncherTransactionRoot -Transaction $Result.Transaction
    }
}

function Restore-Ff7AccessibleLauncherFromState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)] [string] $GameRoot,
        [Parameter(Mandatory=$true)] [string] $ReloadedRoot,
        [Parameter(Mandatory=$true)] [object] $State
    )
    $game = [IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
    $reloaded = [IO.Path]::GetFullPath($ReloadedRoot).TrimEnd('\')
    [void](Assert-LauncherDirectory -Path $game -Label 'Final Fantasy VII game root')
    [void](Assert-LauncherDirectory -Path $reloaded -Label 'Reloaded-II root')
    Assert-LauncherExactProperties -Value $State -Expected @(
        'stockLauncherSha256', 'executable', 'configuration', 'prism', 'manifestPath', 'manifestSha256') `
        -Label 'Installed launcher state'
    if ([string]$State.stockLauncherSha256 -notmatch '^[0-9A-Fa-f]{64}$' -or
        [string]$State.manifestSha256 -notmatch '^[0-9A-Fa-f]{64}$') {
        throw 'Installed launcher state hashes are invalid.'
    }
    $targets = @{
        Executable = Join-Path $game 'FFVII_LAUNCHER.exe'
        Configuration = Join-Path $game 'FFVII_LAUNCHER.exe.config'
        Prism = Join-Path $game 'launcher_accessibility\native\x86\FFVII_LAUNCHER.prism.x86.dll'
        Manifest = Join-Path $game 'launcher_accessibility\install-manifest.json'
    }
    $backupRoot = Join-Path $reloaded 'AccessibilityBackups'
    $files = @(
        (ConvertTo-NormalizedLauncherFileState -Value $State.executable -ExpectedTarget $targets.Executable `
            -BackupRoot $backupRoot -Label 'Installed launcher executable')
        (ConvertTo-NormalizedLauncherFileState -Value $State.configuration -ExpectedTarget $targets.Configuration `
            -BackupRoot $backupRoot -Label 'Installed launcher configuration')
        (ConvertTo-NormalizedLauncherFileState -Value $State.prism -ExpectedTarget $targets.Prism `
            -BackupRoot $backupRoot -Label 'Installed launcher Prism library')
    )
    $manifestPath = [IO.Path]::GetFullPath([string]$State.manifestPath)
    if (-not $manifestPath.Equals([IO.Path]::GetFullPath($targets.Manifest), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Installed launcher manifest path is unsafe.'
    }
    $running = @(Get-Process -Name 'FFVII', 'FFVII_LAUNCHER' -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        throw "Close FFVII and FFVII_LAUNCHER before uninstall. Running process IDs: $($running.Id -join ', ')"
    }

    $removed = New-Object 'System.Collections.Generic.List[string]'
    $restored = New-Object 'System.Collections.Generic.List[string]'
    $preserved = New-Object 'System.Collections.Generic.List[string]'
    foreach ($file in $files) {
        if (-not [bool]$file.changed -or -not (Test-Path -LiteralPath ([string]$file.target) -PathType Leaf)) {
            continue
        }
        [void](Assert-LauncherRegularFile -Path ([string]$file.target) -Label 'Installed launcher file')
        $currentHash = Get-LauncherSha256 -Path ([string]$file.target)
        if (-not (Test-LauncherHashEqual $currentHash ([string]$file.installedSha256))) {
            $preserved.Add("Launcher file changed after installation: $($file.target)")
            continue
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$file.backupPath)) {
            if (-not (Test-Path -LiteralPath ([string]$file.backupPath) -PathType Leaf) -or
                -not (Test-LauncherHashEqual (Get-LauncherSha256 -Path ([string]$file.backupPath)) ([string]$file.backupSha256))) {
                $preserved.Add("Launcher backup is missing or changed: $($file.backupPath)")
                continue
            }
            Copy-LauncherFileAtomically -Source ([string]$file.backupPath) -Target ([string]$file.target) `
                -ExpectedSha256 ([string]$file.backupSha256)
            $restored.Add([string]$file.target)
        }
        else {
            Remove-Item -LiteralPath ([string]$file.target) -Force
            $removed.Add([string]$file.target)
        }
    }

    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        [void](Assert-LauncherRegularFile -Path $manifestPath -Label 'Launcher ownership manifest')
        if (Test-LauncherHashEqual (Get-LauncherSha256 -Path $manifestPath) ([string]$State.manifestSha256)) {
            Remove-Item -LiteralPath $manifestPath -Force
            $removed.Add($manifestPath)
        }
        else {
            $preserved.Add("Launcher ownership manifest changed after installation: $manifestPath")
        }
    }
    return [pscustomobject]@{
        Removed = $removed.ToArray()
        Restored = $restored.ToArray()
        Preserved = $preserved.ToArray()
    }
}

Export-ModuleMember -Function @(
    'Test-Ff7AccessibleLauncherBundle',
    'Install-Ff7AccessibleLauncher',
    'Undo-Ff7AccessibleLauncherTransaction',
    'Complete-Ff7AccessibleLauncherTransaction',
    'Restore-Ff7AccessibleLauncherFromState'
)
