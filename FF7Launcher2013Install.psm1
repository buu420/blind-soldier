Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# Installs the accessible FF7_Launcher.exe into a FINAL FANTASY VII (2013)
# Steam folder (app 39140). Steam starts FF7_Launcher.exe, so the accessible
# launcher takes that name and the original is kept beside the game as
# FF7_Launcher.original.exe, where the accessible launcher can still start it.
# Nothing is overwritten that this module cannot identify, every copy is hash
# verified, and uninstall restores only files that are still exactly as
# installed. Saves, settings and other mods are never touched.

$script:LauncherName = 'FF7_Launcher.exe'
$script:ConfigName = 'FF7_Launcher.exe.config'
$script:BackupName = 'FF7_Launcher.original.exe'
$script:ConfigBackupName = 'FF7_Launcher.exe.config.original'
$script:ManifestRelative = 'Blind-Soldier\Launcher2013\install-manifest.json'
$script:GameExecutables = @('ff7_de.exe', 'ff7_en.exe', 'ff7_es.exe', 'ff7_fr.exe')
$script:GameProcesses = @('FF7_Launcher', 'FF7_Launcher.original', 'ff7_de', 'ff7_en', 'ff7_es', 'ff7_fr')

function Get-Legacy2013Sha256 {
    param([Parameter(Mandatory=$true)] [string] $Path)
    # A redirected, fresh Windows PowerShell host may not have imported the
    # utility module's Get-FileHash function. Hash through the framework so
    # validation does not depend on commands preloaded by a caller or Pester.
    $stream = $null
    $hasher = $null
    try {
        $stream = [IO.File]::OpenRead($Path)
        $hasher = [Security.Cryptography.SHA256]::Create()
        return [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '')
    }
    finally {
        if ($null -ne $hasher) { $hasher.Dispose() }
        if ($null -ne $stream) { $stream.Dispose() }
    }
}

function Test-Legacy2013HashEqual {
    param([AllowNull()] [string] $Left, [AllowNull()] [string] $Right)
    return $null -ne $Left -and $null -ne $Right -and $Left.Equals($Right, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-Legacy2013RegularFile {
    param([Parameter(Mandatory=$true)] [string] $Path, [Parameter(Mandatory=$true)] [string] $Label)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label is missing: $Path"
    }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label cannot be a reparse point: $Path"
    }
    return $item
}

function Get-Legacy2013HashOrNull {
    param([Parameter(Mandatory=$true)] [string] $Path, [Parameter(Mandatory=$true)] [string] $Label)
    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }
    [void](Assert-Legacy2013RegularFile -Path $Path -Label $Label)
    return Get-Legacy2013Sha256 -Path $Path
}

function Get-Legacy2013PeIdentity {
    param([Parameter(Mandatory=$true)] [string] $Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    $machine = $null
    if ($bytes.Length -ge 64 -and $bytes[0] -eq 0x4D -and $bytes[1] -eq 0x5A) {
        $offset = [BitConverter]::ToInt32($bytes, 0x3C)
        if ($offset -ge 64 -and $offset + 6 -le $bytes.Length -and [BitConverter]::ToUInt32($bytes, $offset) -eq 0x00004550) {
            $machine = [BitConverter]::ToUInt16($bytes, $offset + 4)
        }
    }
    $assembly = $null
    try {
        $assembly = [Reflection.AssemblyName]::GetAssemblyName($Path)
    }
    catch {
        $assembly = $null
    }
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    return [pscustomobject]@{
        Machine = $machine
        IsManaged = $null -ne $assembly
        AssemblyName = if ($null -ne $assembly) { $assembly.Name } else { $null }
        AssemblyVersion = if ($null -ne $assembly) { $assembly.Version.ToString() } else { $null }
        CompanyName = $version.CompanyName
        ProductName = $version.ProductName
        FileDescription = $version.FileDescription
        FileVersion = $version.FileVersion
        LegalCopyright = $version.LegalCopyright
    }
}

function Assert-Legacy2013ExactProperties {
    param([Parameter(Mandatory=$true)] [object] $Value, [Parameter(Mandatory=$true)] [string[]] $Expected, [Parameter(Mandatory=$true)] [string] $Label)
    $actual = @($Value.PSObject.Properties | ForEach-Object Name)
    if ($actual.Count -ne $Expected.Count -or
        @($actual | Where-Object { $Expected -cnotcontains $_ }).Count -ne 0) {
        throw "$Label properties are invalid."
    }
}

function Test-Ff7Accessible2013LauncherBundle {
    [CmdletBinding()]
    param([Parameter(Mandatory=$true)] [string] $BundlePath)

    $root = [IO.Path]::GetFullPath($BundlePath).TrimEnd('\')
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "Accessible 2013 launcher bundle is missing: $root"
    }
    $expected = @($script:LauncherName, $script:ConfigName, 'launcher-2013-bundle.json')
    $actual = @(Get-ChildItem -LiteralPath $root -Recurse -Force | ForEach-Object {
        if (($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Accessible 2013 launcher bundle contains a reparse point: $($_.FullName)"
        }
        $_.FullName.Substring($root.Length + 1)
    })
    if ($actual.Count -ne $expected.Count -or @($actual | Where-Object { $expected -cnotcontains $_ }).Count -ne 0) {
        throw 'Accessible 2013 launcher bundle contains missing or unexpected files.'
    }

    try {
        $manifest = [IO.File]::ReadAllText((Join-Path $root 'launcher-2013-bundle.json')) | ConvertFrom-Json
    }
    catch {
        throw "Accessible 2013 launcher bundle manifest is invalid JSON: $($_.Exception.Message)"
    }
    Assert-Legacy2013ExactProperties -Value $manifest -Expected @('schemaVersion', 'assemblyName', 'assemblyVersion', 'launcher', 'config') -Label 'Accessible 2013 launcher bundle manifest'
    if ([int]$manifest.schemaVersion -ne 1 -or [string]$manifest.assemblyName -cne 'FF7_Launcher' -or
        [string]$manifest.assemblyVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
        throw 'Accessible 2013 launcher bundle identity is invalid.'
    }

    $files = @{}
    foreach ($record in @(
            @{ Key = 'Launcher'; Descriptor = $manifest.launcher; Name = $script:LauncherName },
            @{ Key = 'Configuration'; Descriptor = $manifest.config; Name = $script:ConfigName })) {
        Assert-Legacy2013ExactProperties -Value $record.Descriptor -Expected @('path', 'length', 'sha256') -Label "$($record.Name) descriptor"
        $path = Join-Path $root $record.Name
        $item = Assert-Legacy2013RegularFile -Path $path -Label "Bundled $($record.Name)"
        if ([string]$record.Descriptor.path -cne $record.Name -or
            [string]$record.Descriptor.sha256 -notmatch '^[0-9A-F]{64}$' -or
            [int64]$record.Descriptor.length -ne [int64]$item.Length -or
            -not (Test-Legacy2013HashEqual (Get-Legacy2013Sha256 -Path $path) ([string]$record.Descriptor.sha256))) {
            throw "Bundled $($record.Name) does not match its manifest."
        }
        $files[$record.Key] = [pscustomobject]@{ Path = $path; Sha256 = ([string]$record.Descriptor.sha256).ToUpperInvariant() }
    }

    $identity = Get-Legacy2013PeIdentity -Path $files.Launcher.Path
    if ($identity.Machine -ne 0x014C -or -not $identity.IsManaged -or $identity.AssemblyName -cne 'FF7_Launcher' -or
        $identity.AssemblyVersion -cne [string]$manifest.assemblyVersion) {
        throw 'Bundled FF7_Launcher.exe is not the x86 accessible launcher named in its manifest.'
    }
    return [pscustomobject]@{
        Root = $root
        AssemblyVersion = [string]$manifest.assemblyVersion
        Launcher = $files.Launcher
        Configuration = $files.Configuration
    }
}

function Get-Legacy2013Targets {
    param([Parameter(Mandatory=$true)] [string] $GameRoot)
    $game = [IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
    return [pscustomobject]@{
        Game = $game
        Launcher = Join-Path $game $script:LauncherName
        Configuration = Join-Path $game $script:ConfigName
        Backup = Join-Path $game $script:BackupName
        ConfigurationBackup = Join-Path $game $script:ConfigBackupName
        Manifest = Join-Path $game $script:ManifestRelative
    }
}

function Assert-Legacy2013GameFolder {
    param([Parameter(Mandatory=$true)] [object] $Targets)
    $game = $Targets.Game
    $isLegacy = (Test-Path -LiteralPath $game -PathType Container) -and
        (Test-Path -LiteralPath (Join-Path $game 'data') -PathType Container) -and
        (Test-Path -LiteralPath $Targets.Launcher -PathType Leaf) -and
        -not (Test-Path -LiteralPath (Join-Path $game 'FFVII.exe')) -and
        @($script:GameExecutables | Where-Object { Test-Path -LiteralPath (Join-Path $game $_) -PathType Leaf }).Count -gt 0
    if (-not $isLegacy) {
        throw "This is not a FINAL FANTASY VII (2013) game folder with FF7_Launcher.exe, data and an ff7_<language>.exe: $game"
    }
    $item = Get-Item -LiteralPath $game -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "The game folder cannot be a reparse point: $game"
    }
}

function Assert-Legacy2013NotRunning {
    param([Parameter(Mandatory=$true)] [string] $Operation)
    $running = @(Get-Process -Name $script:GameProcesses -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        throw "Close FINAL FANTASY VII and its launcher before $Operation. Running: $(($running | ForEach-Object { $_.ProcessName + ' ' + $_.Id }) -join ', ')"
    }
}

function Read-Legacy2013Manifest {
    param([Parameter(Mandatory=$true)] [object] $Targets)
    if (-not (Test-Path -LiteralPath $Targets.Manifest)) {
        return $null
    }
    [void](Assert-Legacy2013RegularFile -Path $Targets.Manifest -Label 'Accessible 2013 launcher ownership record')
    try {
        $text = [IO.File]::ReadAllText($Targets.Manifest)
        $manifest = $text | ConvertFrom-Json
    }
    catch {
        throw "The accessible 2013 launcher ownership record is invalid JSON: $($Targets.Manifest)"
    }
    Assert-Legacy2013ExactProperties -Value $manifest -Expected @('schemaVersion', 'installedAtUtc', 'gameRoot', 'launcher', 'configuration') -Label 'Ownership record'
    # PowerShell 7 turns ISO dates into DateTime values; keep the recorded text
    # so a repair rewrites nothing.
    $installedAt = [regex]::Match($text, '"installedAtUtc"\s*:\s*"([^"\\]*)"')
    if ($installedAt.Success) {
        $manifest.installedAtUtc = $installedAt.Groups[1].Value
    }
    Assert-Legacy2013ExactProperties -Value $manifest.launcher -Expected @('target', 'installedSha256', 'originalBackupPath', 'originalSha256', 'originalLength', 'originalIdentity') -Label 'Ownership record launcher'
    Assert-Legacy2013ExactProperties -Value $manifest.configuration -Expected @('target', 'installedSha256', 'backupPath', 'backupSha256') -Label 'Ownership record configuration'
    if ([int]$manifest.schemaVersion -ne 1) {
        throw "Unsupported accessible 2013 launcher ownership record schema: $($manifest.schemaVersion)"
    }
    if (-not ([IO.Path]::GetFullPath([string]$manifest.gameRoot)).TrimEnd('\').Equals($Targets.Game, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The accessible 2013 launcher ownership record belongs to another game folder: $($manifest.gameRoot)"
    }
    $pathsMatch = ([IO.Path]::GetFullPath([string]$manifest.launcher.target)).Equals($Targets.Launcher, [StringComparison]::OrdinalIgnoreCase) -and
        ([IO.Path]::GetFullPath([string]$manifest.launcher.originalBackupPath)).Equals($Targets.Backup, [StringComparison]::OrdinalIgnoreCase) -and
        ([IO.Path]::GetFullPath([string]$manifest.configuration.target)).Equals($Targets.Configuration, [StringComparison]::OrdinalIgnoreCase) -and
        ([string]::IsNullOrEmpty([string]$manifest.configuration.backupPath) -or
         ([IO.Path]::GetFullPath([string]$manifest.configuration.backupPath)).Equals($Targets.ConfigurationBackup, [StringComparison]::OrdinalIgnoreCase))
    $hashesValid = [string]$manifest.launcher.installedSha256 -match '^[0-9A-F]{64}$' -and
        [string]$manifest.launcher.originalSha256 -match '^[0-9A-F]{64}$' -and
        [string]$manifest.configuration.installedSha256 -match '^[0-9A-F]{64}$' -and
        ([string]::IsNullOrEmpty([string]$manifest.configuration.backupSha256) -eq [string]::IsNullOrEmpty([string]$manifest.configuration.backupPath)) -and
        ([string]::IsNullOrEmpty([string]$manifest.configuration.backupSha256) -or [string]$manifest.configuration.backupSha256 -match '^[0-9A-F]{64}$')
    if (-not $pathsMatch -or -not $hashesValid) {
        throw 'The accessible 2013 launcher ownership record has unexpected paths or hashes.'
    }
    return $manifest
}

function Copy-Legacy2013FileAtomically {
    param([Parameter(Mandatory=$true)] [string] $Source, [Parameter(Mandatory=$true)] [string] $Target, [Parameter(Mandatory=$true)] [string] $ExpectedSha256)
    $parent = Split-Path -Parent $Target
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $temporary = Join-Path $parent ('.blind-soldier-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $replaced = $null
    try {
        Copy-Item -LiteralPath $Source -Destination $temporary
        if (-not (Test-Legacy2013HashEqual (Get-Legacy2013Sha256 -Path $temporary) $ExpectedSha256)) {
            throw "Copy verification failed for $Target"
        }
        if (Test-Path -LiteralPath $Target -PathType Leaf) {
            $replaced = Join-Path $parent ('.blind-soldier-replaced-' + [Guid]::NewGuid().ToString('N') + '.tmp')
            [IO.File]::Replace($temporary, $Target, $replaced, $true)
        }
        else {
            Move-Item -LiteralPath $temporary -Destination $Target
        }
        if (-not (Test-Legacy2013HashEqual (Get-Legacy2013Sha256 -Path $Target) $ExpectedSha256)) {
            throw "Installed file verification failed for $Target"
        }
    }
    finally {
        foreach ($leftover in @($temporary, $replaced)) {
            if (-not [string]::IsNullOrEmpty($leftover) -and (Test-Path -LiteralPath $leftover -PathType Leaf)) {
                Remove-Item -LiteralPath $leftover -Force
            }
        }
    }
}

function New-Legacy2013Snapshot {
    param([Parameter(Mandatory=$true)] [object] $Targets)
    $root = Join-Path ([IO.Path]::GetTempPath()) ('blind-soldier-2013-launcher-transaction-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root | Out-Null
    $entries = foreach ($name in 'Launcher', 'Configuration', 'Backup', 'ConfigurationBackup', 'Manifest') {
        $target = [string]$Targets.$name
        $snapshot = $null
        $hash = $null
        if (Test-Path -LiteralPath $target -PathType Leaf) {
            $snapshot = Join-Path $root ($name + '.snapshot')
            Copy-Item -LiteralPath $target -Destination $snapshot
            $hash = Get-Legacy2013Sha256 -Path $snapshot
        }
        [pscustomobject]@{ Target = $target; Snapshot = $snapshot; Sha256 = $hash }
    }
    return [pscustomobject]@{ Root = $root; Entries = @($entries) }
}

function Restore-Legacy2013Snapshot {
    param([Parameter(Mandatory=$true)] [object] $Transaction)
    foreach ($entry in $Transaction.Entries) {
        if ($null -ne $entry.Snapshot) {
            Copy-Legacy2013FileAtomically -Source $entry.Snapshot -Target $entry.Target -ExpectedSha256 $entry.Sha256
        }
        elseif (Test-Path -LiteralPath $entry.Target -PathType Leaf) {
            Remove-Item -LiteralPath $entry.Target -Force
        }
    }
}

function Remove-Legacy2013Snapshot {
    param([Parameter(Mandatory=$true)] [object] $Transaction)
    $root = [IO.Path]::GetFullPath($Transaction.Root)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($root.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $root).StartsWith('blind-soldier-2013-launcher-transaction-', [StringComparison]::Ordinal) -and
        (Test-Path -LiteralPath $root)) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}

function Install-Ff7Accessible2013Launcher {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)] [string] $GameRoot,
        [Parameter(Mandatory=$true)] [string] $BundlePath,
        [switch] $ValidateOnly
    )

    $bundle = Test-Ff7Accessible2013LauncherBundle -BundlePath $BundlePath
    $targets = Get-Legacy2013Targets -GameRoot $GameRoot
    Assert-Legacy2013GameFolder -Targets $targets
    $manifest = Read-Legacy2013Manifest -Targets $targets

    $launcherHash = Get-Legacy2013Sha256 -Path ([string](Assert-Legacy2013RegularFile -Path $targets.Launcher -Label 'FF7_Launcher.exe').FullName)
    $backupHash = Get-Legacy2013HashOrNull -Path $targets.Backup -Label 'FF7_Launcher.original.exe'
    $configHash = Get-Legacy2013HashOrNull -Path $targets.Configuration -Label 'FF7_Launcher.exe.config'
    $configBackupHash = Get-Legacy2013HashOrNull -Path $targets.ConfigurationBackup -Label 'FF7_Launcher.exe.config.original'

    # Decide what FF7_Launcher.exe currently is.
    $originalSha256 = $null
    $originalLength = $null
    $originalIdentity = $null
    $copyOriginal = $false
    if (Test-Legacy2013HashEqual $launcherHash $bundle.Launcher.Sha256) {
        $action = 'Repaired'
    }
    elseif ($null -ne $manifest -and (Test-Legacy2013HashEqual $launcherHash ([string]$manifest.launcher.installedSha256))) {
        $action = 'Updated'
    }
    elseif ($null -ne $manifest -and (Test-Legacy2013HashEqual $launcherHash ([string]$manifest.launcher.originalSha256))) {
        # Steam's file verification restored the original over the accessible launcher.
        $action = 'Reinstalled'
    }
    else {
        $identity = Get-Legacy2013PeIdentity -Path $targets.Launcher
        if ($identity.Machine -ne 0x014C -or $identity.IsManaged -or $null -ne $manifest) {
            throw "Refusing to replace an unrecognized FF7_Launcher.exe (SHA-256 $launcherHash). Restore the original with Steam's Verify integrity of game files first."
        }
        $action = 'Installed'
        $originalSha256 = $launcherHash
        $originalLength = (Get-Item -LiteralPath $targets.Launcher).Length
        $originalIdentity = [ordered]@{
            companyName = $identity.CompanyName
            productName = $identity.ProductName
            fileDescription = $identity.FileDescription
            fileVersion = $identity.FileVersion
            legalCopyright = $identity.LegalCopyright
        }
        if ($null -eq $backupHash) {
            $copyOriginal = $true
        }
        elseif (-not (Test-Legacy2013HashEqual $backupHash $launcherHash)) {
            throw "A different FF7_Launcher.original.exe already exists beside the game; it was left unchanged: $($targets.Backup)"
        }
    }

    if ($action -ne 'Installed') {
        if ($null -eq $manifest) {
            throw "The accessible 2013 launcher is installed without its ownership record. Restore the original with Steam's Verify integrity of game files, then install again."
        }
        $originalSha256 = [string]$manifest.launcher.originalSha256
        $originalLength = [int64]$manifest.launcher.originalLength
        $originalIdentity = $manifest.launcher.originalIdentity
        if ($null -eq $backupHash -or -not (Test-Legacy2013HashEqual $backupHash $originalSha256)) {
            throw "The original launcher backup is missing or changed: $($targets.Backup)"
        }
    }

    # FF7_Launcher.exe.config: ours, a prior one of ours, absent, or someone else's.
    $configPreexisting = $false
    $copyConfigBackup = $false
    $configBackupSha256 = $null
    if ($null -ne $manifest -and -not [string]::IsNullOrEmpty([string]$manifest.configuration.backupSha256)) {
        $configBackupSha256 = [string]$manifest.configuration.backupSha256
        $configPreexisting = $true
        if (-not (Test-Legacy2013HashEqual $configBackupHash $configBackupSha256)) {
            throw "The original launcher configuration backup is missing or changed: $($targets.ConfigurationBackup)"
        }
    }
    if ($null -ne $configHash -and -not (Test-Legacy2013HashEqual $configHash $bundle.Configuration.Sha256) -and
        -not ($null -ne $manifest -and (Test-Legacy2013HashEqual $configHash ([string]$manifest.configuration.installedSha256)))) {
        if ($null -ne $configBackupHash) {
            throw "A different FF7_Launcher.exe.config.original already exists beside the game; it was left unchanged: $($targets.ConfigurationBackup)"
        }
        $copyConfigBackup = $true
        $configPreexisting = $true
        $configBackupSha256 = $configHash
    }

    $record = [ordered]@{
        schemaVersion = 1
        installedAtUtc = if ($null -ne $manifest) { [string]$manifest.installedAtUtc } else { [DateTime]::UtcNow.ToString('o') }
        gameRoot = $targets.Game
        launcher = [ordered]@{
            target = $targets.Launcher
            installedSha256 = $bundle.Launcher.Sha256
            originalBackupPath = $targets.Backup
            originalSha256 = $originalSha256.ToUpperInvariant()
            originalLength = [int64]$originalLength
            originalIdentity = $originalIdentity
        }
        configuration = [ordered]@{
            target = $targets.Configuration
            installedSha256 = $bundle.Configuration.Sha256
            backupPath = if ($configPreexisting) { $targets.ConfigurationBackup } else { $null }
            backupSha256 = if ($configPreexisting) { $configBackupSha256.ToUpperInvariant() } else { $null }
        }
    }
    $recordText = ($record | ConvertTo-Json -Depth 6) + "`n"
    $manifestChanged = -not (Test-Path -LiteralPath $targets.Manifest) -or [IO.File]::ReadAllText($targets.Manifest) -cne $recordText
    $changed = $copyOriginal -or $copyConfigBackup -or $manifestChanged -or
        -not (Test-Legacy2013HashEqual $launcherHash $bundle.Launcher.Sha256) -or
        -not (Test-Legacy2013HashEqual $configHash $bundle.Configuration.Sha256)

    if ($ValidateOnly) {
        return [pscustomobject]@{ Action = $action; Changed = [bool]$changed; ValidatedOnly = $true; ManifestPath = $targets.Manifest; State = $record }
    }
    if (-not $changed) {
        return [pscustomobject]@{ Action = $action; Changed = $false; ValidatedOnly = $false; ManifestPath = $targets.Manifest; State = $record }
    }

    Assert-Legacy2013NotRunning -Operation 'installing the accessible launcher'
    $transaction = New-Legacy2013Snapshot -Targets $targets
    try {
        if ($copyOriginal) {
            Copy-Legacy2013FileAtomically -Source $targets.Launcher -Target $targets.Backup -ExpectedSha256 $originalSha256
        }
        if ($copyConfigBackup) {
            Copy-Legacy2013FileAtomically -Source $targets.Configuration -Target $targets.ConfigurationBackup -ExpectedSha256 $configBackupSha256
        }
        if (-not (Test-Legacy2013HashEqual $launcherHash $bundle.Launcher.Sha256)) {
            Copy-Legacy2013FileAtomically -Source $bundle.Launcher.Path -Target $targets.Launcher -ExpectedSha256 $bundle.Launcher.Sha256
        }
        if (-not (Test-Legacy2013HashEqual $configHash $bundle.Configuration.Sha256)) {
            Copy-Legacy2013FileAtomically -Source $bundle.Configuration.Path -Target $targets.Configuration -ExpectedSha256 $bundle.Configuration.Sha256
        }
        if ($manifestChanged) {
            New-Item -ItemType Directory -Path (Split-Path -Parent $targets.Manifest) -Force | Out-Null
            $temporary = $targets.Manifest + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
            [IO.File]::WriteAllText($temporary, $recordText, (New-Object Text.UTF8Encoding($false)))
            Copy-Legacy2013FileAtomically -Source $temporary -Target $targets.Manifest -ExpectedSha256 (Get-Legacy2013Sha256 -Path $temporary)
            Remove-Item -LiteralPath $temporary -Force
        }
    }
    catch {
        $failure = $_
        try { Restore-Legacy2013Snapshot -Transaction $transaction } catch { }
        try { Remove-Legacy2013Snapshot -Transaction $transaction } catch { }
        throw $failure
    }
    Remove-Legacy2013Snapshot -Transaction $transaction
    return [pscustomobject]@{ Action = $action; Changed = $true; ValidatedOnly = $false; ManifestPath = $targets.Manifest; State = $record }
}

function Uninstall-Ff7Accessible2013Launcher {
    [CmdletBinding()]
    param([Parameter(Mandatory=$true)] [string] $GameRoot)

    $targets = Get-Legacy2013Targets -GameRoot $GameRoot
    $manifest = Read-Legacy2013Manifest -Targets $targets
    $restored = New-Object 'System.Collections.Generic.List[string]'
    $removed = New-Object 'System.Collections.Generic.List[string]'
    $preserved = New-Object 'System.Collections.Generic.List[string]'
    if ($null -eq $manifest) {
        return [pscustomobject]@{ Restored = @(); Removed = @(); Preserved = @('No accessible 2013 launcher ownership record was found; nothing was changed.') }
    }
    Assert-Legacy2013NotRunning -Operation 'removing the accessible launcher'

    $originalSha256 = [string]$manifest.launcher.originalSha256
    $launcherHash = Get-Legacy2013HashOrNull -Path $targets.Launcher -Label 'FF7_Launcher.exe'
    $backupHash = Get-Legacy2013HashOrNull -Path $targets.Backup -Label 'FF7_Launcher.original.exe'
    $launcherSettled = $false
    if (Test-Legacy2013HashEqual $launcherHash ([string]$manifest.launcher.installedSha256)) {
        if (Test-Legacy2013HashEqual $backupHash $originalSha256) {
            Copy-Legacy2013FileAtomically -Source $targets.Backup -Target $targets.Launcher -ExpectedSha256 $originalSha256
            $restored.Add($targets.Launcher)
            $launcherSettled = $true
        }
        else {
            $preserved.Add("The original launcher backup is missing or changed, so the accessible launcher was kept: $($targets.Backup)")
        }
    }
    elseif (Test-Legacy2013HashEqual $launcherHash $originalSha256) {
        $launcherSettled = $true
    }
    else {
        $preserved.Add("FF7_Launcher.exe changed after installation and was kept: $($targets.Launcher)")
    }
    if ($launcherSettled -and (Test-Legacy2013HashEqual $backupHash $originalSha256)) {
        Remove-Item -LiteralPath $targets.Backup -Force
        $removed.Add($targets.Backup)
    }

    $configHash = Get-Legacy2013HashOrNull -Path $targets.Configuration -Label 'FF7_Launcher.exe.config'
    $configBackupSha256 = [string]$manifest.configuration.backupSha256
    if (Test-Legacy2013HashEqual $configHash ([string]$manifest.configuration.installedSha256)) {
        if (-not [string]::IsNullOrEmpty($configBackupSha256)) {
            $configBackupHash = Get-Legacy2013HashOrNull -Path $targets.ConfigurationBackup -Label 'FF7_Launcher.exe.config.original'
            if (Test-Legacy2013HashEqual $configBackupHash $configBackupSha256) {
                Copy-Legacy2013FileAtomically -Source $targets.ConfigurationBackup -Target $targets.Configuration -ExpectedSha256 $configBackupSha256
                Remove-Item -LiteralPath $targets.ConfigurationBackup -Force
                $restored.Add($targets.Configuration)
                $removed.Add($targets.ConfigurationBackup)
            }
            else {
                $preserved.Add("The original launcher configuration backup is missing or changed: $($targets.ConfigurationBackup)")
            }
        }
        else {
            Remove-Item -LiteralPath $targets.Configuration -Force
            $removed.Add($targets.Configuration)
        }
    }
    elseif ($null -ne $configHash) {
        $preserved.Add("FF7_Launcher.exe.config changed after installation and was kept: $($targets.Configuration)")
    }

    if ($preserved.Count -eq 0) {
        Remove-Item -LiteralPath $targets.Manifest -Force
        $removed.Add($targets.Manifest)
        $manifestFolder = Split-Path -Parent $targets.Manifest
        foreach ($folder in @($manifestFolder, (Split-Path -Parent $manifestFolder))) {
            if ((Test-Path -LiteralPath $folder -PathType Container) -and @(Get-ChildItem -LiteralPath $folder -Force).Count -eq 0) {
                Remove-Item -LiteralPath $folder -Force
            }
        }
    }
    return [pscustomobject]@{ Restored = $restored.ToArray(); Removed = $removed.ToArray(); Preserved = $preserved.ToArray() }
}

Export-ModuleMember -Function @(
    'Test-Ff7Accessible2013LauncherBundle',
    'Install-Ff7Accessible2013Launcher',
    'Uninstall-Ff7Accessible2013Launcher'
)
