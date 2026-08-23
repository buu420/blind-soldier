param(
    [Parameter()]
    [string] $OutputPath,

    [Parameter(DontShow=$true)]
    [scriptblock] $PublishInvoker,

    [Parameter(DontShow=$true)]
    [string] $ModConfigSourceOverride,

    [Parameter(DontShow=$true)]
    [string] $ExpectedModVersion
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $scriptRoot 'dist\ff7.accessibility.reloaded'
}

function Get-PeMachine {
    param([Parameter(Mandatory=$true)] [string] $Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "Package validation failed because '$Path' is not a PE file."
    }

    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($peOffset -lt 64 -or $peOffset + 6 -gt $bytes.Length) {
        throw "Package validation failed because '$Path' has an invalid PE header offset."
    }
    if ([BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x00004550) {
        throw "Package validation failed because '$Path' has no PE signature."
    }

    return [BitConverter]::ToUInt16($bytes, $peOffset + 4)
}

function Resolve-PeBackedFileRange {
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
        throw "Package validation failed because '$Path' has an empty $Description."
    }
    for ($index = 0; $index -lt $NumberOfSections; $index++) {
        $currentSectionOffset = [uint64]$SectionTableOffset + ([uint64]$index * 40)
        if ($currentSectionOffset + 40 -gt [uint64]$Bytes.Length) {
            throw "Package validation failed because '$Path' has a truncated PE section table."
        }
        $virtualSize = [BitConverter]::ToUInt32($Bytes, [int]$currentSectionOffset + 8)
        $virtualAddress = [BitConverter]::ToUInt32($Bytes, [int]$currentSectionOffset + 12)
        $rawSize = [BitConverter]::ToUInt32($Bytes, [int]$currentSectionOffset + 16)
        $rawOffset = [BitConverter]::ToUInt32($Bytes, [int]$currentSectionOffset + 20)
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
            throw "Package validation failed because '$Path' has an unbacked $Description in a virtual-only section tail."
        }
        $fileOffset = [uint64]$rawOffset + $delta
        if ($fileOffset + $rangeSize -gt [uint64]$Bytes.Length) {
            throw "Package validation failed because '$Path' has a truncated $Description."
        }
        return $fileOffset
    }
    throw "Package validation failed because '$Path' has an unmappable $Description."
}

function Get-PeManagedNativeHeaderDirectory {
    param([Parameter(Mandatory=$true)] [string] $Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "Package validation failed because '$Path' is not a PE file."
    }

    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($peOffset -lt 64 -or $peOffset + 24 -gt $bytes.Length -or
        [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x00004550) {
        throw "Package validation failed because '$Path' has an invalid PE header offset."
    }

    $numberOfSections = [BitConverter]::ToUInt16($bytes, $peOffset + 6)
    $optionalHeaderSize = [BitConverter]::ToUInt16($bytes, $peOffset + 20)
    $optionalHeaderOffset = $peOffset + 24
    if ([uint64]$optionalHeaderOffset + [uint64]$optionalHeaderSize -gt [uint64]$bytes.Length) {
        throw "Package validation failed because '$Path' has a truncated PE optional header."
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
        throw "Package validation failed because '$Path' has an unsupported PE optional header."
    }

    if ($numberOfDirectoriesOffset + 4 -gt $optionalHeaderOffset + $optionalHeaderSize -or
        [BitConverter]::ToUInt32($bytes, $numberOfDirectoriesOffset) -le 14) {
        throw "Package validation failed because '$Path' has no CLR data directory."
    }

    $clrDirectoryOffset = $directoriesOffset + (14 * 8)
    if ($clrDirectoryOffset + 8 -gt $optionalHeaderOffset + $optionalHeaderSize) {
        throw "Package validation failed because '$Path' has a CLR directory outside its optional header."
    }

    $clrRva = [BitConverter]::ToUInt32($bytes, $clrDirectoryOffset)
    $clrSize = [BitConverter]::ToUInt32($bytes, $clrDirectoryOffset + 4)
    if ($clrRva -eq 0 -or $clrSize -lt 72) {
        throw "Package validation failed because '$Path' has no complete CLR header."
    }

    $sectionOffset = $optionalHeaderOffset + $optionalHeaderSize
    $clrFileOffset = Resolve-PeBackedFileRange -Bytes $bytes `
        -SectionTableOffset $sectionOffset -NumberOfSections $numberOfSections `
        -Rva $clrRva -Size $clrSize -Path $Path -Description 'CLR header'
    $nativeRva = [BitConverter]::ToUInt32($bytes, [int]$clrFileOffset + 64)
    $nativeSize = [BitConverter]::ToUInt32($bytes, [int]$clrFileOffset + 68)
    if ($nativeRva -ne 0 -or $nativeSize -ne 0) {
        if ($nativeRva -eq 0 -or $nativeSize -eq 0) {
            throw "Package validation failed because '$Path' has an incomplete ManagedNativeHeaderDirectory."
        }
        [void](Resolve-PeBackedFileRange -Bytes $bytes `
            -SectionTableOffset $sectionOffset -NumberOfSections $numberOfSections `
            -Rva $nativeRva -Size $nativeSize -Path $Path `
            -Description 'ManagedNativeHeaderDirectory')
    }

    [pscustomobject]@{
        VirtualAddress = $nativeRva
        Size = $nativeSize
    }
}

function Assert-PackageFile {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Package validation failed: missing $Description at '$Path'."
    }
}

function Resolve-PackageRelativeFile {
    param(
        [Parameter(Mandatory=$true)] [string] $PackageRoot,
        [Parameter(Mandatory=$true)] [string] $RelativePath,
        [Parameter(Mandatory=$true)] [string] $Description
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath)) {
        throw "Package validation failed: $Description must be a non-empty relative path."
    }

    $rootPrefix = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\') + '\'
    $resolvedPath = [IO.Path]::GetFullPath((Join-Path $PackageRoot $RelativePath))
    if (-not $resolvedPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Package validation failed: $Description escapes the package root."
    }

    Assert-PackageFile -Path $resolvedPath -Description $Description
    return $resolvedPath
}

function Test-PackageDependencyManifestEntry {
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

function Assert-PeMachine {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [int] $ExpectedMachine,
        [Parameter(Mandatory=$true)] [string] $Description
    )

    $actualMachine = Get-PeMachine -Path $Path
    if ($actualMachine -ne $ExpectedMachine) {
        throw ("Package validation failed: {0} has PE machine 0x{1:X4}; expected 0x{2:X4}." -f `
            $Description, $actualMachine, $ExpectedMachine)
    }
}

function Assert-ManagedReadyToRunPe {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [int] $ExpectedMachine,
        [Parameter(Mandatory=$true)] [string] $Description
    )

    Assert-PeMachine -Path $Path -ExpectedMachine $ExpectedMachine -Description $Description
    $managedNativeHeader = Get-PeManagedNativeHeaderDirectory -Path $Path
    if ($managedNativeHeader.VirtualAddress -eq 0 -or $managedNativeHeader.Size -eq 0) {
        throw "Package validation failed: $Description has an empty CLR ManagedNativeHeaderDirectory."
    }
}

function Invoke-R2RPublish {
    param(
        [Parameter(Mandatory=$true)] [string] $Project,
        [Parameter(Mandatory=$true)] [string] $RuntimeIdentifier,
        [Parameter(Mandatory=$true)] [string] $Destination,
        [Parameter(Mandatory=$true)] [string] $FailureDescription,
        [Parameter()] [scriptblock] $Invoker
    )

    if ($null -ne $Invoker) {
        $publishResult = @(& $Invoker $Project $RuntimeIdentifier $Destination)
        if ($publishResult.Count -ne 1 -or $publishResult[0] -isnot [int]) {
            throw 'PublishInvoker must return exactly one integer exit code.'
        }
        $exitCode = [int]$publishResult[0]
    }
    else {
        & dotnet publish $Project `
            -c Release `
            -r $RuntimeIdentifier `
            --self-contained false `
            --nologo `
            -p:PublishReadyToRun=true `
            -p:PublishReadyToRunShowWarnings=true `
            -p:PublishSingleFile=false `
            -o $Destination
        $exitCode = $LASTEXITCODE
    }

    if ($exitCode -ne 0) {
        throw "$FailureDescription failed with exit code $exitCode."
    }
}

function Assert-DualRuntimePackage {
    param(
        [Parameter(Mandatory=$true)] [string] $PackageRoot,
        [Parameter(Mandatory=$true)] [string] $ExpectedVersion
    )

    $modConfigPath = Join-Path $PackageRoot 'ModConfig.json'
    Assert-PackageFile -Path $modConfigPath -Description 'Reloaded ModConfig.json'
    try {
        $modConfig = [IO.File]::ReadAllText($modConfigPath) | ConvertFrom-Json
    }
    catch {
        throw "Package validation failed: ModConfig.json is invalid JSON. $($_.Exception.Message)"
    }

    if ([string]$modConfig.ModId -cne 'ff7.accessibility.reloaded') {
        throw 'Package validation failed: ModConfig.json has an unexpected ModId.'
    }
    if ([string]$modConfig.ModVersion -cne $ExpectedVersion) {
        throw 'Package validation failed: ModConfig.json has an unexpected ModVersion.'
    }

    $supportedApps = @($modConfig.SupportedAppId)
    if ($supportedApps.Count -ne 3 -or
        [string]$supportedApps[0] -cne 'ff7_en.exe' -or
        [string]$supportedApps[1] -cne 'ff7.exe' -or
        [string]$supportedApps[2] -cne 'FFVII.exe') {
        throw 'Package validation failed: ModConfig.json does not select the exact supported FFVII executables.'
    }
    if ([string]$modConfig.ModR2RManagedDll32 -cne 'x86/Ff7.Accessibility.Reloaded.dll' -or
        [string]$modConfig.ModR2RManagedDll64 -cne 'x64/Ff7.Accessibility.Steam2026X64.dll') {
        throw 'Package validation failed: ModConfig.json does not select the exact dual-runtime entry assemblies.'
    }

    $x86Assembly = Resolve-PackageRelativeFile -PackageRoot $PackageRoot `
        -RelativePath ([string]$modConfig.ModR2RManagedDll32) -Description 'x86 managed entry assembly'
    $x64Assembly = Resolve-PackageRelativeFile -PackageRoot $PackageRoot `
        -RelativePath ([string]$modConfig.ModR2RManagedDll64) -Description 'x64 managed entry assembly'

    Assert-ManagedReadyToRunPe -Path $x86Assembly -ExpectedMachine 0x014C `
        -Description 'x86 managed entry assembly'
    Assert-ManagedReadyToRunPe -Path $x64Assembly -ExpectedMachine 0x8664 `
        -Description 'x64 managed entry assembly'

    $architectures = @(
        [pscustomobject]@{
            Name = 'x86'
            Machine = 0x014C
            Deps = 'Ff7.Accessibility.Reloaded.deps.json'
            ManagedDependencies = @(
                'Ff7.Accessibility.Core.dll',
                'Ff7.Accessibility.LegacyLayout.dll',
                'Ff7.Accessibility.Runtime.Abstractions.dll'
            )
        },
        [pscustomobject]@{
            Name = 'x64'
            Machine = 0x8664
            Deps = 'Ff7.Accessibility.Steam2026X64.deps.json'
            ManagedDependencies = @(
                'Ff7.Accessibility.Core.dll',
                'Ff7.Accessibility.LegacyLayout.dll',
                'Ff7.Accessibility.Runtime.Abstractions.dll'
            )
        }
    )

    foreach ($architecture in $architectures) {
        $architectureRoot = Join-Path $PackageRoot $architecture.Name
        foreach ($dependency in $architecture.ManagedDependencies) {
            $managedDependencyPath = Join-Path $architectureRoot $dependency
            Assert-PackageFile -Path $managedDependencyPath `
                -Description "$($architecture.Name) managed dependency $dependency"
            Assert-ManagedReadyToRunPe -Path $managedDependencyPath -ExpectedMachine $architecture.Machine `
                -Description "$($architecture.Name) managed dependency $dependency"
        }

        foreach ($nativeDependency in @('prism.dll', 'phonon.dll')) {
            $nativeDependencyPath = Join-Path $architectureRoot $nativeDependency
            Assert-PackageFile -Path $nativeDependencyPath `
                -Description "$($architecture.Name) native dependency $nativeDependency"
            Assert-PeMachine -Path $nativeDependencyPath -ExpectedMachine $architecture.Machine `
                -Description "$($architecture.Name) native dependency $nativeDependency"
        }

        $depsPath = Join-Path $architectureRoot $architecture.Deps
        Assert-PackageFile -Path $depsPath -Description "$($architecture.Name) dependency manifest"
        try {
            $depsManifest = [IO.File]::ReadAllText($depsPath) | ConvertFrom-Json
        }
        catch {
            throw "Package validation failed: $($architecture.Name) dependency manifest is invalid JSON. $($_.Exception.Message)"
        }
        foreach ($dependencyName in @(
            'Ff7.Accessibility.Core',
            'Ff7.Accessibility.LegacyLayout',
            'Ff7.Accessibility.Runtime.Abstractions'
        )) {
            if (-not (Test-PackageDependencyManifestEntry `
                    -Manifest $depsManifest -Dependency $dependencyName)) {
                throw "Package validation failed: $($architecture.Name) dependency manifest omits $dependencyName."
            }
        }
    }

    $configurationPath = Join-Path $PackageRoot 'Configuration\config.json'
    Assert-PackageFile -Path $configurationPath `
        -Description 'Reloaded configuration'
    try {
        $configuration = [IO.File]::ReadAllText($configurationPath) | ConvertFrom-Json
    }
    catch {
        throw "Package validation failed: invalid Configuration/config.json. $($_.Exception.Message)"
    }
    if ($configuration -isnot [Management.Automation.PSCustomObject]) {
        throw 'Package validation failed: invalid Configuration/config.json root; expected an object.'
    }
    if ([string]$configuration.GameLanguage -cne 'auto') {
        throw 'Package validation failed: Configuration/config.json must ship with GameLanguage set to auto.'
    }
    Assert-PackageFile -Path (Join-Path $PackageRoot 'Assets\movies\opening_audio_description.ogg') `
        -Description 'opening movie audio description asset'
    Assert-PackageFile -Path (Join-Path $PackageRoot 'Assets\world\field-id-to-world-map-coords.json') `
        -Description 'world-map entrance coordinate metadata'
    Assert-PackageFile -Path (Join-Path $PackageRoot 'Assets\world\wm-field-menu-names.txt') `
        -Description 'world-map location name metadata'
    Assert-PackageFile -Path (Join-Path $PackageRoot 'Assets\footsteps\cosmo\config.toml') `
        -Description 'Cosmo Memory footstep mapping'
    foreach ($sourceOnlyAsset in $script:AssetSourceOnlyDirectories) {
        $unexpectedSourceAsset = Join-Path $PackageRoot $sourceOnlyAsset
        if (Test-Path -LiteralPath $unexpectedSourceAsset) {
            throw "Package validation failed: $sourceOnlyAsset is sound-sourcing material and must not ship."
        }
    }
    Assert-PackageFile -Path (Join-Path $PackageRoot 'LICENSES\FF7Tools-text-table-notice.md') `
        -Description 'FF7Tools text-table license notice'
    foreach ($fieldCueAsset in @(
        'field_zone_transition.wav',
        'object_materia_190_pitch70.wav',
        'object_chest_253_pitch70.wav',
        'object_item_357_pitch70.wav',
        'ladder_061.wav',
        'ladder_approach_214.wav',
        'floor60_statue_134.wav'
    )) {
        Assert-PackageFile -Path (Join-Path $PackageRoot "Assets\navigation\$fieldCueAsset") `
            -Description "field accessibility cue asset $fieldCueAsset"
    }
}

$legacyProjectRoot = Join-Path $scriptRoot 'Ff7.Accessibility.Reloaded'
$legacyProject = Join-Path $legacyProjectRoot 'Ff7.Accessibility.Reloaded.csproj'
$nativeProject = Join-Path $scriptRoot 'Ff7.Accessibility.Steam2026X64\Ff7.Accessibility.Steam2026X64.csproj'
$modConfigSource = if ([string]::IsNullOrWhiteSpace($ModConfigSourceOverride)) {
    Join-Path $legacyProjectRoot 'ModConfig.json'
}
else {
    [IO.Path]::GetFullPath($ModConfigSourceOverride)
}
if ([string]::IsNullOrWhiteSpace($ExpectedModVersion)) {
    try {
        $ExpectedModVersion = [string](
            [IO.File]::ReadAllText($modConfigSource) | ConvertFrom-Json).ModVersion
    }
    catch {
        throw "Required dual-runtime ModConfig.json is invalid. $($_.Exception.Message)"
    }
}
if ([string]::IsNullOrWhiteSpace($ExpectedModVersion)) {
    throw 'ExpectedModVersion must be a non-empty package version.'
}
$configurationSource = Join-Path $legacyProjectRoot 'Configuration'
$assetsSource = Join-Path $legacyProjectRoot 'Assets'
# Sound-sourcing material that lives in the repo but must never ship to players.
$script:AssetSourceOnlyDirectories = @('Assets\footsteps\real_samples')
$worldCoordinateSource = Join-Path $scriptRoot 'external\kujata\field-id-to-world-map-coords.json'
$worldMenuNameSource = Join-Path $scriptRoot 'external\kujata\wm-field-menu-names.txt'
$ff7ToolsNoticeSource = Join-Path $scriptRoot 'docs\third-party\ff7tools-notice.md'

foreach ($requiredPath in @(
    $legacyProject,
    $nativeProject,
    $modConfigSource,
    $configurationSource,
    $assetsSource,
    $worldCoordinateSource,
    $worldMenuNameSource,
    $ff7ToolsNoticeSource
)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required dual-runtime package input is missing: $requiredPath"
    }
}

$outputDirectory = New-Object IO.DirectoryInfo ([IO.Path]::GetFullPath($OutputPath))
if ($null -eq $outputDirectory.Parent) {
    throw 'OutputPath must name a package directory rather than a volume root.'
}
if (-not $outputDirectory.Name.Equals('ff7.accessibility.reloaded', [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath must end in the owned package directory 'ff7.accessibility.reloaded': $($outputDirectory.FullName)"
}
if (Test-Path -LiteralPath $outputDirectory.FullName) {
    $existingOutput = Get-Item -LiteralPath $outputDirectory.FullName -Force
    if (-not $existingOutput.PSIsContainer) {
        throw "Refusing to replace a non-directory package output: $($outputDirectory.FullName)"
    }
    if (($existingOutput.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to replace a reparse-point package output: $($outputDirectory.FullName)"
    }

    $existingModConfigPath = Join-Path $outputDirectory.FullName 'ModConfig.json'
    if (-not (Test-Path -LiteralPath $existingModConfigPath -PathType Leaf)) {
        throw "Refusing to replace an unowned directory without ModConfig.json: $($outputDirectory.FullName)"
    }
    try {
        $existingModConfig = [IO.File]::ReadAllText($existingModConfigPath) | ConvertFrom-Json
    }
    catch {
        throw "Refusing to replace an unowned directory with invalid ModConfig.json: $($outputDirectory.FullName)"
    }
    if ([string]$existingModConfig.ModId -cne 'ff7.accessibility.reloaded') {
        throw "Refusing to replace a directory owned by another mod: $($outputDirectory.FullName)"
    }
}

$outputRoot = $outputDirectory.FullName
$outputParent = $outputDirectory.Parent.FullName
$outputLeaf = $outputDirectory.Name
New-Item -ItemType Directory -Force -Path $outputParent | Out-Null

$operationId = [Guid]::NewGuid().ToString('N')
$stagingRoot = Join-Path $outputParent ('.{0}.staging-{1}' -f $outputLeaf, $operationId)
$backupRoot = Join-Path $outputParent ('.{0}.backup-{1}' -f $outputLeaf, $operationId)
$x86Staging = Join-Path $stagingRoot 'x86'
$x64Staging = Join-Path $stagingRoot 'x64'
$priorOutputMoved = $false

try {
    New-Item -ItemType Directory -Path $x86Staging, $x64Staging | Out-Null

    Invoke-R2RPublish -Project $legacyProject -RuntimeIdentifier 'win-x86' -Destination $x86Staging `
        -FailureDescription 'The legacy x86 ReadyToRun publish' -Invoker $PublishInvoker
    Invoke-R2RPublish -Project $nativeProject -RuntimeIdentifier 'win-x64' -Destination $x64Staging `
        -FailureDescription 'The native Steam 2026 x64 ReadyToRun publish' -Invoker $PublishInvoker

    Copy-Item -LiteralPath $modConfigSource -Destination (Join-Path $stagingRoot 'ModConfig.json') -Force
    Copy-Item -LiteralPath $configurationSource -Destination $stagingRoot -Recurse -Force
    Copy-Item -LiteralPath $assetsSource -Destination $stagingRoot -Recurse -Force
    foreach ($sourceOnlyAsset in $script:AssetSourceOnlyDirectories) {
        $stagedSourceOnly = Join-Path $stagingRoot $sourceOnlyAsset
        if (Test-Path -LiteralPath $stagedSourceOnly) {
            Remove-Item -LiteralPath $stagedSourceOnly -Recurse -Force
        }
    }
    $worldAssetDirectory = Join-Path $stagingRoot 'Assets\world'
    New-Item -ItemType Directory -Path $worldAssetDirectory -Force | Out-Null
    Copy-Item -LiteralPath $worldCoordinateSource -Destination $worldAssetDirectory -Force
    Copy-Item -LiteralPath $worldMenuNameSource -Destination $worldAssetDirectory -Force
    $licenseDirectory = Join-Path $stagingRoot 'LICENSES'
    New-Item -ItemType Directory -Path $licenseDirectory -Force | Out-Null
    Copy-Item -LiteralPath $ff7ToolsNoticeSource `
        -Destination (Join-Path $licenseDirectory 'FF7Tools-text-table-notice.md') -Force

    Assert-DualRuntimePackage -PackageRoot $stagingRoot -ExpectedVersion $ExpectedModVersion

    if (Test-Path -LiteralPath $outputRoot) {
        Move-Item -LiteralPath $outputRoot -Destination $backupRoot
        $priorOutputMoved = $true
    }

    try {
        Move-Item -LiteralPath $stagingRoot -Destination $outputRoot
    }
    catch {
        $replacementError = $_
        if ($priorOutputMoved -and -not (Test-Path -LiteralPath $outputRoot) -and
            (Test-Path -LiteralPath $backupRoot)) {
            try {
                Move-Item -LiteralPath $backupRoot -Destination $outputRoot
                $priorOutputMoved = $false
            }
            catch {
                throw "Package replacement failed and the prior package could not be restored. " +
                    "Replacement error: $($replacementError.Exception.Message) Restore error: $($_.Exception.Message) " +
                    "The recoverable prior package remains at '$backupRoot'."
            }
        }
        throw "Package replacement failed; the prior package was preserved. $($replacementError.Exception.Message)"
    }

    if ($priorOutputMoved -and (Test-Path -LiteralPath $backupRoot)) {
        try {
            Remove-Item -LiteralPath $backupRoot -Recurse -Force
            $priorOutputMoved = $false
        }
        catch {
            Write-Warning "The new package is installed, but its prior backup could not be removed: $backupRoot"
        }
    }
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}

[pscustomobject]@{
    OutputPath = $outputRoot
    X86Assembly = Join-Path $outputRoot 'x86\Ff7.Accessibility.Reloaded.dll'
    X64Assembly = Join-Path $outputRoot 'x64\Ff7.Accessibility.Steam2026X64.dll'
    ModConfig = Join-Path $outputRoot 'ModConfig.json'
}
