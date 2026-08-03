[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $Version,
    [string] $Tag,
    [string] $OutputPath,
    [ValidateSet('stable', 'prerelease')] [string] $Track,
    [string] $MinimumSetupVersion = '0.1.0-pre.1',
    [string] $Repository = 'buu420/blind-swordsman',
    [Parameter(DontShow=$true)] [scriptblock] $PackageBuilder,
    [Parameter(DontShow=$true)] [scriptblock] $SetupPublisher,
    [Parameter(DontShow=$true)] [scriptblock] $ArtifactValidator
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$semanticVersionPattern = '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'
if ($Version -notmatch $semanticVersionPattern) {
    throw "Version is not strict semantic version text: $Version"
}
if ($MinimumSetupVersion -notmatch $semanticVersionPattern) {
    throw "MinimumSetupVersion is not strict semantic version text: $MinimumSetupVersion"
}
if ([string]::IsNullOrWhiteSpace($Tag)) {
    $Tag = 'v' + $Version
}
if ($Tag -cne ('v' + $Version)) {
    throw "Tag must exactly match v<Version>. Expected v$Version, received $Tag."
}
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw "Repository must be an owner/name GitHub repository identifier."
}
$inferredTrack = if (($Version -split '\+', 2)[0].Contains('-')) { 'prerelease' } else { 'stable' }
if ([string]::IsNullOrWhiteSpace($Track)) {
    $Track = $inferredTrack
}
elseif ($Track -cne $inferredTrack) {
    throw "Release track '$Track' disagrees with semantic version '$Version'."
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $scriptRoot "artifacts\release\$Tag"
}

$outputDirectory = New-Object IO.DirectoryInfo ([IO.Path]::GetFullPath($OutputPath))
if ($null -eq $outputDirectory.Parent) {
    throw 'OutputPath cannot be a volume root.'
}
if (Test-Path -LiteralPath $outputDirectory.FullName) {
    throw "Release output already exists; choose an empty new path: $($outputDirectory.FullName)"
}
New-Item -ItemType Directory -Path $outputDirectory.Parent.FullName -Force | Out-Null
$stagingRoot = Join-Path $outputDirectory.Parent.FullName ('.{0}.staging-{1}' -f $outputDirectory.Name, [Guid]::NewGuid().ToString('N'))
$assetRoot = Join-Path $stagingRoot 'assets'
$runtimeRoot = Join-Path $stagingRoot 'runtime'
$packagePath = Join-Path $runtimeRoot 'package\ff7.accessibility.reloaded'
$launcherSourceRoot = Join-Path $scriptRoot 'installer-assets\launcher'
$launcherPrismSource = Join-Path $scriptRoot 'Ff7.Accessibility.Reloaded\Native\win-x86\prism.dll'
$launcherBundlePath = Join-Path $runtimeRoot 'launcher'
$setupPublishRoot = Join-Path $stagingRoot 'setup-publish'
$payloadAsset = Join-Path $assetRoot 'Blind-Swordsman-Runtime.zip'
$setupAsset = Join-Path $assetRoot 'Blind-Swordsman-Setup.exe'
$channelAsset = Join-Path $assetRoot 'blind-swordsman-channel.json'
$utf8WithoutBom = New-Object Text.UTF8Encoding($false)

function Assert-NoReparsePoint {
    param([Parameter(Mandatory=$true)] [string] $Root)
    $rootItem = Get-Item -LiteralPath $Root -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Release staging cannot use a reparse point: $Root"
    }
    foreach ($item in @(Get-ChildItem -LiteralPath $Root -Force -Recurse)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Release payload cannot contain a reparse point: $($item.FullName)"
        }
    }
}

function Get-RelativeArchivePath {
    param(
        [Parameter(Mandatory=$true)] [string] $Root,
        [Parameter(Mandatory=$true)] [string] $Path
    )
    $rootPrefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release file escaped its staging root: $fullPath"
    }
    return $fullPath.Substring($rootPrefix.Length).Replace('\', '/')
}

function Write-DeterministicJson {
    param(
        [Parameter(Mandatory=$true)] [object] $Value,
        [Parameter(Mandatory=$true)] [string] $Path,
        [int] $Depth = 8
    )
    $json = $Value | ConvertTo-Json -Depth $Depth
    [IO.File]::WriteAllText($Path, $json + "`n", $utf8WithoutBom)
}

function New-PayloadManifest {
    param([Parameter(Mandatory=$true)] [string] $Root)
    Assert-NoReparsePoint -Root $Root
    $pathToFile = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -File -Recurse -Force)) {
        $relative = Get-RelativeArchivePath -Root $Root -Path $file.FullName
        if ($relative -ceq 'payload-manifest.json') {
            continue
        }
        if ($pathToFile.ContainsKey($relative)) {
            throw "Release payload has a case-insensitive duplicate path: $relative"
        }
        $pathToFile.Add($relative, $file.FullName)
    }
    [string[]]$paths = @($pathToFile.Keys)
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    if ($paths.Count -eq 0) {
        throw 'Release payload contains no files.'
    }
    $records = foreach ($relative in $paths) {
        $file = Get-Item -LiteralPath $pathToFile[$relative]
        [ordered]@{
            path = $relative
            length = [int64]$file.Length
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
        }
    }
    $manifest = [ordered]@{ schemaVersion = 1; files = @($records) }
    Write-DeterministicJson -Value $manifest -Path (Join-Path $Root 'payload-manifest.json')
}

function New-DeterministicZip {
    param(
        [Parameter(Mandatory=$true)] [string] $Root,
        [Parameter(Mandatory=$true)] [string] $Destination
    )
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $pathToFile = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -File -Recurse -Force)) {
        $relative = Get-RelativeArchivePath -Root $Root -Path $file.FullName
        if ($pathToFile.ContainsKey($relative)) {
            throw "Release archive has a case-insensitive duplicate path: $relative"
        }
        $pathToFile.Add($relative, $file.FullName)
    }
    [string[]]$paths = @($pathToFile.Keys)
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    $stream = New-Object IO.FileStream($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $timestamp = New-Object DateTimeOffset(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            foreach ($relative in $paths) {
                $entry = $archive.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $timestamp
                $entry.ExternalAttributes = 0
                $source = [IO.File]::OpenRead($pathToFile[$relative])
                try {
                    $destinationStream = $entry.Open()
                    try { $source.CopyTo($destinationStream) } finally { $destinationStream.Dispose() }
                }
                finally {
                    $source.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-PeMachine {
    param([Parameter(Mandatory=$true)] [string] $Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "Setup publisher did not produce a PE executable: $Path"
    }
    $offset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($offset -lt 64 -or $offset + 6 -gt $bytes.Length -or [BitConverter]::ToUInt32($bytes, $offset) -ne 0x00004550) {
        throw "Setup publisher produced an invalid PE executable: $Path"
    }
    return [BitConverter]::ToUInt16($bytes, $offset + 4)
}

function Assert-ExactJsonProperties {
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

function Assert-LauncherAsset {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [object] $Descriptor,
        [Parameter(Mandatory=$true)] [string] $ExpectedName,
        [Parameter(Mandatory=$true)] [string] $Label
    )
    Assert-ExactJsonProperties -Value $Descriptor -Expected @('name', 'size', 'sha256') -Label $Label
    if ([string]$Descriptor.name -cne $ExpectedName -or
        [string]$Descriptor.sha256 -notmatch '^[0-9A-F]{64}$' -or
        [int64]$Descriptor.size -le 0) {
        throw "$Label metadata is invalid."
    }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label file is missing: $Path"
    }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        [int64]$item.Length -ne [int64]$Descriptor.size) {
        throw "$Label size or file type is invalid: $Path"
    }
    $actualHash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actualHash -cne [string]$Descriptor.sha256) {
        throw "$Label SHA-256 is invalid: $Path"
    }
}

function Copy-ValidatedLauncherBundle {
    param(
        [Parameter(Mandatory=$true)] [string] $SourceRoot,
        [Parameter(Mandatory=$true)] [string] $PrismSource,
        [Parameter(Mandatory=$true)] [string] $DestinationRoot
    )
    if (-not (Test-Path -LiteralPath $SourceRoot -PathType Container)) {
        throw "Accessible launcher source is missing: $SourceRoot"
    }
    Assert-NoReparsePoint -Root $SourceRoot
    $sourceFiles = @(Get-ChildItem -LiteralPath $SourceRoot -File -Recurse -Force |
        ForEach-Object { Get-RelativeArchivePath -Root $SourceRoot -Path $_.FullName })
    $expectedSourceFiles = @('FFVII_LAUNCHER.exe', 'FFVII_LAUNCHER.exe.config', 'launcher-bundle.json')
    if ($sourceFiles.Count -ne $expectedSourceFiles.Count -or
        @($sourceFiles | Where-Object { $expectedSourceFiles -cnotcontains $_ }).Count -ne 0) {
        throw 'Accessible launcher source contains unexpected files.'
    }

    $manifestPath = Join-Path $SourceRoot 'launcher-bundle.json'
    try {
        $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
    }
    catch {
        throw "Accessible launcher manifest is invalid JSON: $($_.Exception.Message)"
    }
    Assert-ExactJsonProperties -Value $manifest -Expected @(
        'schemaVersion', 'stockLauncherSha256', 'launcher', 'config', 'prism',
        'assemblyName', 'assemblyVersion') -Label 'Accessible launcher manifest'
    if ([int]$manifest.schemaVersion -ne 1 -or
        [string]$manifest.stockLauncherSha256 -notmatch '^[0-9A-F]{64}$' -or
        [string]$manifest.assemblyName -cne 'FFVII_LAUNCHER' -or
        [string]$manifest.assemblyVersion -cne '2.0.0.0') {
        throw 'Accessible launcher manifest identity is invalid.'
    }

    $launcherSource = Join-Path $SourceRoot 'FFVII_LAUNCHER.exe'
    $configSource = Join-Path $SourceRoot 'FFVII_LAUNCHER.exe.config'
    Assert-LauncherAsset -Path $launcherSource -Descriptor $manifest.launcher `
        -ExpectedName 'FFVII_LAUNCHER.exe' -Label 'Accessible launcher'
    Assert-LauncherAsset -Path $configSource -Descriptor $manifest.config `
        -ExpectedName 'FFVII_LAUNCHER.exe.config' -Label 'Accessible launcher configuration'
    Assert-LauncherAsset -Path $PrismSource -Descriptor $manifest.prism `
        -ExpectedName 'FFVII_LAUNCHER.prism.x86.dll' -Label 'Launcher Prism'
    if ((Get-PeMachine -Path $launcherSource) -ne 0x014C -or
        (Get-PeMachine -Path $PrismSource) -ne 0x014C) {
        throw 'Accessible launcher and launcher Prism must both be x86 PE images.'
    }
    $assembly = [Reflection.AssemblyName]::GetAssemblyName($launcherSource)
    if ($assembly.Name -cne [string]$manifest.assemblyName -or
        $assembly.Version.ToString() -cne [string]$manifest.assemblyVersion) {
        throw 'Accessible launcher managed assembly identity is invalid.'
    }

    New-Item -ItemType Directory -Path (Join-Path $DestinationRoot 'native\x86') -Force | Out-Null
    Copy-Item -LiteralPath $launcherSource -Destination (Join-Path $DestinationRoot 'FFVII_LAUNCHER.exe')
    Copy-Item -LiteralPath $configSource -Destination (Join-Path $DestinationRoot 'FFVII_LAUNCHER.exe.config')
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $DestinationRoot 'launcher-bundle.json')
    Copy-Item -LiteralPath $PrismSource -Destination (Join-Path $DestinationRoot 'native\x86\FFVII_LAUNCHER.prism.x86.dll')
    Assert-NoReparsePoint -Root $DestinationRoot
}

function Write-HashSidecar {
    param([Parameter(Mandatory=$true)] [string] $Path)
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
    [IO.File]::WriteAllText($Path + '.sha256', "$hash  $([IO.Path]::GetFileName($Path))`n", $utf8WithoutBom)
    return $hash
}

try {
    New-Item -ItemType Directory -Path $assetRoot, $runtimeRoot, $setupPublishRoot -Force | Out-Null
    if ($null -ne $PackageBuilder) {
        & $PackageBuilder $packagePath
    }
    else {
        & (Join-Path $scriptRoot 'Build-DualRuntimePackage.ps1') -OutputPath $packagePath | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Dual-runtime package builder exited with code $LASTEXITCODE." }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $packagePath 'ModConfig.json') -PathType Leaf)) {
        throw 'Runtime package builder did not produce ff7.accessibility.reloaded/ModConfig.json.'
    }
    Copy-ValidatedLauncherBundle -SourceRoot $launcherSourceRoot `
        -PrismSource $launcherPrismSource -DestinationRoot $launcherBundlePath
    New-PayloadManifest -Root $runtimeRoot
    New-DeterministicZip -Root $runtimeRoot -Destination $payloadAsset

    if ($null -ne $SetupPublisher) {
        & $SetupPublisher $setupPublishRoot
    }
    else {
        & dotnet publish (Join-Path $scriptRoot 'installer\BlindSwordsman.Setup\BlindSwordsman.Setup.csproj') `
            -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
            -p:DebugType=None -p:DebugSymbols=false -o $setupPublishRoot
        if ($LASTEXITCODE -ne 0) { throw "Setup publisher exited with code $LASTEXITCODE." }
    }
    $publishedSetup = Join-Path $setupPublishRoot 'Blind-Swordsman-Setup.exe'
    if (-not (Test-Path -LiteralPath $publishedSetup -PathType Leaf)) {
        throw 'Setup publisher did not produce Blind-Swordsman-Setup.exe.'
    }
    if ((Get-PeMachine -Path $publishedSetup) -ne 0x8664) {
        throw 'Blind-Swordsman-Setup.exe is not an x64 PE executable.'
    }
    Copy-Item -LiteralPath $publishedSetup -Destination $setupAsset

    $payloadHash = Write-HashSidecar -Path $payloadAsset
    $setupHash = Write-HashSidecar -Path $setupAsset
    $baseUrl = "https://github.com/$Repository/releases/download/$Tag"
    $channel = [ordered]@{
        schemaVersion = 1
        version = $Version
        releaseTag = $Tag
        track = $Track
        minimumSetupVersion = $MinimumSetupVersion
        payload = [ordered]@{
            name = 'Blind-Swordsman-Runtime.zip'
            url = "$baseUrl/Blind-Swordsman-Runtime.zip"
            sha256 = $payloadHash
            size = [int64](Get-Item -LiteralPath $payloadAsset).Length
        }
        setup = [ordered]@{
            name = 'Blind-Swordsman-Setup.exe'
            url = "$baseUrl/Blind-Swordsman-Setup.exe"
            sha256 = $setupHash
            size = [int64](Get-Item -LiteralPath $setupAsset).Length
        }
    }
    Write-DeterministicJson -Value $channel -Path $channelAsset

    if ($null -ne $ArtifactValidator) {
        & $ArtifactValidator $channelAsset $payloadAsset $setupAsset $Track
    }
    else {
        & dotnet run --project (Join-Path $scriptRoot 'installer\BlindSwordsman.Setup.Tests\BlindSwordsman.Setup.Tests.csproj') `
            -c Release -- validate-release $channelAsset $payloadAsset $setupAsset $Track
        if ($LASTEXITCODE -ne 0) { throw "Release artifact validator exited with code $LASTEXITCODE." }
    }

    Move-Item -LiteralPath $assetRoot -Destination $outputDirectory.FullName
    [pscustomobject]@{
        Version = $Version
        Tag = $Tag
        Track = $Track
        OutputPath = $outputDirectory.FullName
        SetupPath = Join-Path $outputDirectory.FullName 'Blind-Swordsman-Setup.exe'
        PayloadPath = Join-Path $outputDirectory.FullName 'Blind-Swordsman-Runtime.zip'
        ChannelPath = Join-Path $outputDirectory.FullName 'blind-swordsman-channel.json'
    }
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
