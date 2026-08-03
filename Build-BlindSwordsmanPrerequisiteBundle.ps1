[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $OutputPath,
    [string] $LockPath,
    [string] $NoticePath,
    [string] $CachePath,
    [Parameter(DontShow=$true)] [scriptblock] $ArtifactResolver,
    [Parameter(DontShow=$true)] [scriptblock] $SevenZipExtractor,
    [Parameter(DontShow=$true)] [string] $BootstrapperX86Override,
    [Parameter(DontShow=$true)] [string] $BootstrapperX64Override
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($LockPath)) {
    $LockPath = Join-Path $scriptRoot 'installer-dependencies\dependency-lock.json'
}
if ([string]::IsNullOrWhiteSpace($NoticePath)) {
    $NoticePath = Join-Path $scriptRoot 'installer-dependencies\THIRD-PARTY-NOTICES.md'
}
if ([string]::IsNullOrWhiteSpace($CachePath)) {
    $CachePath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'BlindSwordsman\BuildCache\prerequisites'
}

function Assert-ExactProperties {
    param(
        [Parameter(Mandatory=$true)] [psobject] $Value,
        [Parameter(Mandatory=$true)] [string[]] $Expected,
        [Parameter(Mandatory=$true)] [string] $Label
    )

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

function Assert-SafeLeafName {
    param([Parameter(Mandatory=$true)] [string] $Name, [Parameter(Mandatory=$true)] [string] $Label)
    if ([string]::IsNullOrWhiteSpace($Name) -or $Name -cne [IO.Path]::GetFileName($Name) -or
        $Name.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) {
        throw "$Label is not a safe file name: $Name"
    }
}

function Assert-HttpsUrl {
    param([Parameter(Mandatory=$true)] [string] $Url, [Parameter(Mandatory=$true)] [string] $Label)
    $parsed = $null
    if (-not [Uri]::TryCreate($Url, [UriKind]::Absolute, [ref]$parsed) -or $parsed.Scheme -cne 'https') {
        throw "$Label must be an absolute HTTPS URL."
    }
}

function Assert-HexDigest {
    param(
        [Parameter(Mandatory=$true)] [string] $Digest,
        [Parameter(Mandatory=$true)] [ValidateSet(64,128)] [int] $Length,
        [Parameter(Mandatory=$true)] [string] $Label
    )
    if ($Digest -cnotmatch ('^[0-9A-F]{' + $Length + '}$')) {
        throw "$Label must be $Length uppercase hexadecimal characters."
    }
}

function Test-FileRecord {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [long] $Size,
        [Parameter(Mandatory=$true)] [string] $Sha256,
        [string] $Sha512
    )
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $item.Length -ne $Size) { return $false }
    if (-not (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.Equals($Sha256, [StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }
    if (-not [string]::IsNullOrWhiteSpace($Sha512) -and
        -not (Get-FileHash -LiteralPath $Path -Algorithm SHA512).Hash.Equals($Sha512, [StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }
    return $true
}

function Assert-FileRecord {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [long] $Size,
        [Parameter(Mandatory=$true)] [string] $Sha256,
        [string] $Sha512,
        [Parameter(Mandatory=$true)] [string] $Label
    )
    if (-not (Test-FileRecord -Path $Path -Size $Size -Sha256 $Sha256 -Sha512 $Sha512)) {
        throw "$Label failed its locked size or cryptographic digest check."
    }
}

function Get-LockedArtifact {
    param(
        [Parameter(Mandatory=$true)] [string] $Url,
        [Parameter(Mandatory=$true)] [string] $Name,
        [Parameter(Mandatory=$true)] [long] $Size,
        [Parameter(Mandatory=$true)] [string] $Sha256,
        [string] $Sha512,
        [Parameter(Mandatory=$true)] [string] $DownloadRoot,
        [Parameter(Mandatory=$true)] [string] $Label
    )

    $destination = Join-Path $DownloadRoot $Name
    if ($null -ne $ArtifactResolver) {
        & $ArtifactResolver $Url $destination
        Assert-FileRecord -Path $destination -Size $Size -Sha256 $Sha256 -Sha512 $Sha512 -Label $Label
        return $destination
    }

    New-Item -ItemType Directory -Path $CachePath -Force | Out-Null
    $cacheName = $Sha256 + '-' + $Name
    $cached = Join-Path $CachePath $cacheName
    if (-not (Test-FileRecord -Path $cached -Size $Size -Sha256 $Sha256 -Sha512 $Sha512)) {
        if (Test-Path -LiteralPath $cached) {
            $cachedItem = Get-Item -LiteralPath $cached -Force
            if ($cachedItem.PSIsContainer -or ($cachedItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Prerequisite cache target is not an ordinary file: $cached"
            }
            Remove-Item -LiteralPath $cached -Force
        }
        $temporary = Join-Path $CachePath ('.download-' + [Guid]::NewGuid().ToString('N'))
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $temporary
            Assert-FileRecord -Path $temporary -Size $Size -Sha256 $Sha256 -Sha512 $Sha512 -Label $Label
            Move-Item -LiteralPath $temporary -Destination $cached
        }
        finally {
            if (Test-Path -LiteralPath $temporary -PathType Leaf) {
                Remove-Item -LiteralPath $temporary -Force
            }
        }
    }
    Copy-Item -LiteralPath $cached -Destination $destination
    Assert-FileRecord -Path $destination -Size $Size -Sha256 $Sha256 -Sha512 $Sha512 -Label $Label
    return $destination
}

function Assert-SafeArchiveMember {
    param([Parameter(Mandatory=$true)] [string] $Name, [Parameter(Mandatory=$true)] [string] $Label)
    if ([string]::IsNullOrWhiteSpace($Name) -or $Name.Contains([char]0)) {
        throw "$Label contains an empty or invalid archive member."
    }
    $normalized = $Name.Replace('\', '/').TrimEnd('/')
    if ([string]::IsNullOrWhiteSpace($normalized) -or $normalized.StartsWith('/') -or
        $normalized.StartsWith('//') -or $normalized -match '^[A-Za-z]:' -or $normalized.Contains(':')) {
        throw "$Label contains a rooted or alternate-stream member: $Name"
    }
    foreach ($part in $normalized.Split('/')) {
        if ($part -ceq '..' -or $part -ceq '.' -or [string]::IsNullOrWhiteSpace($part)) {
            throw "$Label contains an unsafe path member: $Name"
        }
    }
    return $normalized
}

function Assert-NoReparsePoint {
    param([Parameter(Mandatory=$true)] [string] $Root, [Parameter(Mandatory=$true)] [string] $Label)
    $rootItem = Get-Item -LiteralPath $Root -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label root cannot be a reparse point: $Root"
    }
    foreach ($item in @(Get-ChildItem -LiteralPath $Root -Recurse -Force)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Label cannot contain a reparse point: $($item.FullName)"
        }
    }
}

function Expand-SafeZip {
    param([Parameter(Mandatory=$true)] [string] $ArchivePath, [Parameter(Mandatory=$true)] [string] $Destination)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $members = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $archive.Entries) {
            $normalized = Assert-SafeArchiveMember -Name $entry.FullName -Label 'Reloaded-II ZIP'
            if (-not $members.Add($normalized)) {
                throw "Reloaded-II ZIP contains a case-insensitive duplicate member: $($entry.FullName)"
            }
        }
    }
    finally {
        $archive.Dispose()
    }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($ArchivePath, $Destination)
    Assert-NoReparsePoint -Root $Destination -Label 'Reloaded-II ZIP extraction'
}

function Expand-SafeSevenZip {
    param([Parameter(Mandatory=$true)] [string] $ArchivePath, [Parameter(Mandatory=$true)] [string] $Destination)
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    if ($null -ne $SevenZipExtractor) {
        & $SevenZipExtractor $ArchivePath $Destination
        Assert-NoReparsePoint -Root $Destination -Label 'Injected 7-Zip extraction'
        return
    }
    $tar = Get-Command tar.exe -ErrorAction Stop
    $listing = @(& $tar.Source -tf $ArchivePath 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to list 7-Zip archive '$ArchivePath': $($listing -join [Environment]::NewLine)"
    }
    $members = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($member in $listing) {
        $normalized = Assert-SafeArchiveMember -Name ([string]$member) -Label '7-Zip archive'
        if (-not $members.Add($normalized)) {
            throw "7-Zip archive contains a case-insensitive duplicate member: $member"
        }
    }
    if ($members.Count -eq 0) { throw "7-Zip archive is empty: $ArchivePath" }
    $output = @(& $tar.Source -xf $ArchivePath -C $Destination 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to extract 7-Zip archive '$ArchivePath': $($output -join [Environment]::NewLine)"
    }
    Assert-NoReparsePoint -Root $Destination -Label '7-Zip extraction'
}

function Get-PeMachine {
    param([Parameter(Mandatory=$true)] [string] $Path)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $buffer = New-Object byte[] 4
        if ($stream.Read($buffer, 0, 2) -ne 2 -or $buffer[0] -ne 0x4D -or $buffer[1] -ne 0x5A) {
            throw "File is not a PE image: $Path"
        }
        $stream.Position = 0x3C
        if ($stream.Read($buffer, 0, 4) -ne 4) { throw "PE header is truncated: $Path" }
        $headerOffset = [BitConverter]::ToInt32($buffer, 0)
        $stream.Position = $headerOffset
        if ($stream.Read($buffer, 0, 4) -ne 4 -or [BitConverter]::ToUInt32($buffer, 0) -ne 0x00004550) {
            throw "PE signature is invalid: $Path"
        }
        if ($stream.Read($buffer, 0, 2) -ne 2) { throw "PE machine is truncated: $Path" }
        return [BitConverter]::ToUInt16($buffer, 0)
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-PeMachine {
    param([Parameter(Mandatory=$true)] [string] $Path, [Parameter(Mandatory=$true)] [uint16] $Expected, [Parameter(Mandatory=$true)] [string] $Label)
    if ((Get-PeMachine -Path $Path) -ne $Expected) {
        throw "$Label has the wrong PE architecture: $Path"
    }
}

function Write-JsonNoBom {
    param([Parameter(Mandatory=$true)] [object] $Value, [Parameter(Mandatory=$true)] [string] $Path)
    $json = ($Value | ConvertTo-Json -Depth 10) + "`n"
    [IO.File]::WriteAllText($Path, $json, (New-Object Text.UTF8Encoding($false)))
}

$reloadedLoaderAllowlist = @(
    'Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll',
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

if (-not (Test-Path -LiteralPath $LockPath -PathType Leaf)) { throw "Dependency lock is unavailable: $LockPath" }
if (-not (Test-Path -LiteralPath $NoticePath -PathType Leaf)) { throw "Third-party notice is unavailable: $NoticePath" }
try {
    $lock = [IO.File]::ReadAllText([IO.Path]::GetFullPath($LockPath)) | ConvertFrom-Json
}
catch {
    throw "Dependency lock is invalid JSON: $($_.Exception.Message)"
}
Assert-ExactProperties -Value $lock -Expected @('schemaVersion','reloaded','sharedHooks','dotnetDesktopRuntime') -Label 'Dependency lock'
if ([int]$lock.schemaVersion -ne 1) { throw 'Dependency lock schemaVersion must be 1.' }
Assert-ExactProperties -Value $lock.reloaded -Expected @('version','assetName','url','size','sha256','sourceCodeUrl','licensePath','licenseSize','licenseSha256') -Label 'Reloaded lock'
Assert-ExactProperties -Value $lock.sharedHooks -Expected @('version','assetName','url','size','sha256','sourceCodeUrl','licenseName','licenseUrl','licenseSize','licenseSha256') -Label 'Shared Hooks lock'
Assert-ExactProperties -Value $lock.dotnetDesktopRuntime -Expected @('version','sourceCodeUrl','licenseName','licenseUrl','licenseSize','licenseSha256','thirdPartyNoticesName','thirdPartyNoticesUrl','thirdPartyNoticesSize','thirdPartyNoticesSha256','installers') -Label '.NET lock'
foreach ($urlRecord in @(
    [pscustomobject]@{ Url=[string]$lock.reloaded.url; Label='Reloaded asset URL' },
    [pscustomobject]@{ Url=[string]$lock.reloaded.sourceCodeUrl; Label='Reloaded source URL' },
    [pscustomobject]@{ Url=[string]$lock.sharedHooks.url; Label='Shared Hooks asset URL' },
    [pscustomobject]@{ Url=[string]$lock.sharedHooks.sourceCodeUrl; Label='Shared Hooks source URL' },
    [pscustomobject]@{ Url=[string]$lock.sharedHooks.licenseUrl; Label='Shared Hooks license URL' },
    [pscustomobject]@{ Url=[string]$lock.dotnetDesktopRuntime.sourceCodeUrl; Label='.NET source URL' },
    [pscustomobject]@{ Url=[string]$lock.dotnetDesktopRuntime.licenseUrl; Label='.NET license URL' },
    [pscustomobject]@{ Url=[string]$lock.dotnetDesktopRuntime.thirdPartyNoticesUrl; Label='.NET notices URL' }
)) { Assert-HttpsUrl -Url $urlRecord.Url -Label $urlRecord.Label }
foreach ($nameRecord in @(
    [pscustomobject]@{ Name=[string]$lock.reloaded.assetName; Label='Reloaded asset name' },
    [pscustomobject]@{ Name=[string]$lock.sharedHooks.assetName; Label='Shared Hooks asset name' },
    [pscustomobject]@{ Name=[string]$lock.sharedHooks.licenseName; Label='Shared Hooks license name' },
    [pscustomobject]@{ Name=[string]$lock.dotnetDesktopRuntime.licenseName; Label='.NET license name' },
    [pscustomobject]@{ Name=[string]$lock.dotnetDesktopRuntime.thirdPartyNoticesName; Label='.NET notices name' }
)) { Assert-SafeLeafName -Name $nameRecord.Name -Label $nameRecord.Label }
Assert-HexDigest -Digest ([string]$lock.reloaded.sha256) -Length 64 -Label 'Reloaded SHA-256'
Assert-HexDigest -Digest ([string]$lock.reloaded.licenseSha256) -Length 64 -Label 'Reloaded license SHA-256'
Assert-HexDigest -Digest ([string]$lock.sharedHooks.sha256) -Length 64 -Label 'Shared Hooks SHA-256'
Assert-HexDigest -Digest ([string]$lock.sharedHooks.licenseSha256) -Length 64 -Label 'Shared Hooks license SHA-256'
Assert-HexDigest -Digest ([string]$lock.dotnetDesktopRuntime.licenseSha256) -Length 64 -Label '.NET license SHA-256'
Assert-HexDigest -Digest ([string]$lock.dotnetDesktopRuntime.thirdPartyNoticesSha256) -Length 64 -Label '.NET notices SHA-256'
$installers = @($lock.dotnetDesktopRuntime.installers)
if ($installers.Count -ne 2 -or @($installers.architecture | Sort-Object) -join ',' -cne 'x64,x86') {
    throw '.NET lock must contain exactly one x86 and one x64 installer.'
}
foreach ($installer in $installers) {
    Assert-ExactProperties -Value $installer -Expected @('architecture','name','url','size','sha256','sha512') -Label ".NET $($installer.architecture) installer lock"
    if ([string]$installer.architecture -cnotmatch '^(x86|x64)$') { throw 'Unsupported .NET installer architecture.' }
    Assert-SafeLeafName -Name ([string]$installer.name) -Label '.NET installer name'
    Assert-HttpsUrl -Url ([string]$installer.url) -Label '.NET installer URL'
    Assert-HexDigest -Digest ([string]$installer.sha256) -Length 64 -Label '.NET installer SHA-256'
    Assert-HexDigest -Digest ([string]$installer.sha512) -Length 128 -Label '.NET installer SHA-512'
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $resolvedOutput) { throw "Prerequisite output already exists: $resolvedOutput" }
$outputParent = Split-Path -Parent $resolvedOutput
if ([string]::IsNullOrWhiteSpace($outputParent)) { throw 'Prerequisite output must have a parent directory.' }
New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
# bsdtar cannot reliably chdir into a near-MAX_PATH destination even when .NET
# long-path support is enabled. Keep private extraction short, then publish the
# fully validated tree to the caller's requested output in one move.
$staging = Join-Path ([IO.Path]::GetTempPath()) ('bs-prereq-' + [Guid]::NewGuid().ToString('N'))
$downloads = Join-Path $staging 'downloads'
$bundle = Join-Path $staging 'bundle'
$temporary = Join-Path $staging 'temporary'
try {
    New-Item -ItemType Directory -Path $downloads, $bundle, $temporary -Force | Out-Null
    $reloadedArchive = Get-LockedArtifact -Url ([string]$lock.reloaded.url) -Name ([string]$lock.reloaded.assetName) `
        -Size ([long]$lock.reloaded.size) -Sha256 ([string]$lock.reloaded.sha256) -DownloadRoot $downloads -Label 'Reloaded-II archive'
    $hooksArchive = Get-LockedArtifact -Url ([string]$lock.sharedHooks.url) -Name ([string]$lock.sharedHooks.assetName) `
        -Size ([long]$lock.sharedHooks.size) -Sha256 ([string]$lock.sharedHooks.sha256) -DownloadRoot $downloads -Label 'Shared Hooks archive'
    $hooksLicense = Get-LockedArtifact -Url ([string]$lock.sharedHooks.licenseUrl) -Name ([string]$lock.sharedHooks.licenseName) `
        -Size ([long]$lock.sharedHooks.licenseSize) -Sha256 ([string]$lock.sharedHooks.licenseSha256) -DownloadRoot $downloads -Label 'Shared Hooks license'
    $dotnetLicense = Get-LockedArtifact -Url ([string]$lock.dotnetDesktopRuntime.licenseUrl) -Name ([string]$lock.dotnetDesktopRuntime.licenseName) `
        -Size ([long]$lock.dotnetDesktopRuntime.licenseSize) -Sha256 ([string]$lock.dotnetDesktopRuntime.licenseSha256) -DownloadRoot $downloads -Label '.NET license'
    $dotnetNotices = Get-LockedArtifact -Url ([string]$lock.dotnetDesktopRuntime.thirdPartyNoticesUrl) -Name ([string]$lock.dotnetDesktopRuntime.thirdPartyNoticesName) `
        -Size ([long]$lock.dotnetDesktopRuntime.thirdPartyNoticesSize) -Sha256 ([string]$lock.dotnetDesktopRuntime.thirdPartyNoticesSha256) -DownloadRoot $downloads -Label '.NET third-party notices'
    $runtimeArtifacts = @{}
    foreach ($installer in $installers) {
        $runtimeArtifacts[[string]$installer.architecture] = Get-LockedArtifact -Url ([string]$installer.url) -Name ([string]$installer.name) `
            -Size ([long]$installer.size) -Sha256 ([string]$installer.sha256) -Sha512 ([string]$installer.sha512) `
            -DownloadRoot $downloads -Label ".NET $($installer.architecture) desktop runtime"
    }

    $reloadedFull = Join-Path $temporary 'reloaded-full'
    Expand-SafeZip -ArchivePath $reloadedArchive -Destination $reloadedFull
    if (-not [string]::IsNullOrWhiteSpace($BootstrapperX86Override)) {
        Copy-Item -LiteralPath $BootstrapperX86Override -Destination (Join-Path $reloadedFull 'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Force
    }
    if (-not [string]::IsNullOrWhiteSpace($BootstrapperX64Override)) {
        Copy-Item -LiteralPath $BootstrapperX64Override -Destination (Join-Path $reloadedFull 'Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Force
    }
    $reloadedRoot = Join-Path $bundle 'reloaded'
    New-Item -ItemType Directory -Path $reloadedRoot | Out-Null
    foreach ($architecture in @('X86','X64')) {
        foreach ($relative in $reloadedLoaderAllowlist) {
            $source = Join-Path $reloadedFull ("Loader\$architecture\$relative")
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
                throw "Reloaded-II archive is missing Loader\$architecture\$relative."
            }
            $destination = Join-Path $reloadedRoot ("Loader\$architecture\$relative")
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination $destination
        }
    }
    $asiArchive = Join-Path $reloadedFull 'Loader\Asi\UltimateAsiLoader.7z'
    if (-not (Test-Path -LiteralPath $asiArchive -PathType Leaf)) { throw 'Reloaded-II archive does not contain UltimateAsiLoader.7z.' }
    $asiTemporary = Join-Path $temporary 'asi'
    Expand-SafeSevenZip -ArchivePath $asiArchive -Destination $asiTemporary
    $asiDestination = Join-Path $reloadedRoot '_asi_extract'
    New-Item -ItemType Directory -Path $asiDestination | Out-Null
    foreach ($name in @('ASILoader32.dll','ASILoader64.dll')) {
        $source = Join-Path $asiTemporary $name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Ultimate ASI Loader archive is missing $name." }
        Copy-Item -LiteralPath $source -Destination (Join-Path $asiDestination $name)
    }

    $hooksRoot = Join-Path $bundle 'shared-hooks'
    Expand-SafeSevenZip -ArchivePath $hooksArchive -Destination $hooksRoot
    $hooksConfigPath = Join-Path $hooksRoot 'ModConfig.json'
    if (-not (Test-Path -LiteralPath $hooksConfigPath -PathType Leaf)) { throw 'Shared Hooks archive is missing ModConfig.json.' }
    try { $hooksConfig = [IO.File]::ReadAllText($hooksConfigPath) | ConvertFrom-Json } catch { throw "Shared Hooks ModConfig.json is invalid: $($_.Exception.Message)" }
    if ([string]$hooksConfig.ModId -cne 'reloaded.sharedlib.hooks') { throw 'Shared Hooks archive has an unexpected ModId.' }

    $dotnetRoot = Join-Path $bundle 'dotnet'
    New-Item -ItemType Directory -Path $dotnetRoot | Out-Null
    foreach ($installer in $installers) {
        Copy-Item -LiteralPath $runtimeArtifacts[[string]$installer.architecture] -Destination (Join-Path $dotnetRoot ([string]$installer.name))
    }
    $noticesRoot = Join-Path $bundle 'notices'
    New-Item -ItemType Directory -Path $noticesRoot | Out-Null
    Copy-Item -LiteralPath $NoticePath -Destination (Join-Path $noticesRoot 'THIRD-PARTY-NOTICES.md')
    $reloadedLicense = Join-Path $reloadedFull ([string]$lock.reloaded.licensePath)
    Assert-FileRecord -Path $reloadedLicense -Size ([long]$lock.reloaded.licenseSize) -Sha256 ([string]$lock.reloaded.licenseSha256) -Label 'Reloaded-II license'
    Copy-Item -LiteralPath $reloadedLicense -Destination (Join-Path $noticesRoot 'Reloaded-II-GPL-3.0.txt')
    Copy-Item -LiteralPath $hooksLicense -Destination (Join-Path $noticesRoot ([string]$lock.sharedHooks.licenseName))
    Copy-Item -LiteralPath $dotnetLicense -Destination (Join-Path $noticesRoot ([string]$lock.dotnetDesktopRuntime.licenseName))
    Copy-Item -LiteralPath $dotnetNotices -Destination (Join-Path $noticesRoot ([string]$lock.dotnetDesktopRuntime.thirdPartyNoticesName))

    $requiredReloadedFiles = New-Object 'System.Collections.Generic.List[string]'
    foreach ($architecture in @('X86','X64')) {
        foreach ($relative in $reloadedLoaderAllowlist) {
            $requiredReloadedFiles.Add("Loader\$architecture\$relative")
        }
    }
    $requiredReloadedFiles.Add('_asi_extract\ASILoader32.dll')
    $requiredReloadedFiles.Add('_asi_extract\ASILoader64.dll')
    foreach ($required in $requiredReloadedFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $reloadedRoot $required) -PathType Leaf)) { throw "Reloaded-II bundle is missing $required." }
    }
    $actualReloadedFiles = @(Get-ChildItem -LiteralPath $reloadedRoot -File -Recurse)
    if ($actualReloadedFiles.Count -ne $requiredReloadedFiles.Count) {
        throw "Reloaded-II bundle contains unexpected files; expected $($requiredReloadedFiles.Count), found $($actualReloadedFiles.Count)."
    }
    foreach ($required in @('x86\Reloaded.Hooks.ReloadedII.dll','x64\Reloaded.Hooks.ReloadedII.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $hooksRoot $required) -PathType Leaf)) { throw "Shared Hooks bundle is missing $required." }
    }
    Assert-PeMachine -Path (Join-Path $reloadedRoot '_asi_extract\ASILoader32.dll') -Expected 0x014C -Label 'x86 ASI loader'
    Assert-PeMachine -Path (Join-Path $reloadedRoot '_asi_extract\ASILoader64.dll') -Expected 0x8664 -Label 'x64 ASI loader'
    Assert-PeMachine -Path (Join-Path $reloadedRoot 'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Expected 0x014C -Label 'x86 Reloaded bootstrapper'
    Assert-PeMachine -Path (Join-Path $reloadedRoot 'Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Expected 0x8664 -Label 'x64 Reloaded bootstrapper'
    Assert-NoReparsePoint -Root $bundle -Label 'Prerequisite bundle'

    $bundleManifest = [ordered]@{
        schemaVersion = 1
        reloaded = [ordered]@{
            version = [string]$lock.reloaded.version
            sourceUrl = [string]$lock.reloaded.url
            sourceSize = [long]$lock.reloaded.size
            sourceSha256 = [string]$lock.reloaded.sha256
            sourceCodeUrl = [string]$lock.reloaded.sourceCodeUrl
        }
        sharedHooks = [ordered]@{
            version = [string]$lock.sharedHooks.version
            sourceUrl = [string]$lock.sharedHooks.url
            sourceSize = [long]$lock.sharedHooks.size
            sourceSha256 = [string]$lock.sharedHooks.sha256
            sourceCodeUrl = [string]$lock.sharedHooks.sourceCodeUrl
        }
        dotnetDesktopRuntime = [ordered]@{
            version = [string]$lock.dotnetDesktopRuntime.version
            sourceCodeUrl = [string]$lock.dotnetDesktopRuntime.sourceCodeUrl
            installers = @($installers | ForEach-Object {
                [ordered]@{
                    architecture = [string]$_.architecture
                    name = [string]$_.name
                    sourceUrl = [string]$_.url
                    sourceSize = [long]$_.size
                    sourceSha256 = [string]$_.sha256
                    sourceSha512 = [string]$_.sha512
                }
            })
        }
    }
    Write-JsonNoBom -Value $bundleManifest -Path (Join-Path $bundle 'dependency-bundle.json')
    Move-Item -LiteralPath $bundle -Destination $resolvedOutput
    [pscustomobject]@{
        OutputPath = $resolvedOutput
        ReloadedVersion = [string]$lock.reloaded.version
        SharedHooksVersion = [string]$lock.sharedHooks.version
        DotNetDesktopRuntimeVersion = [string]$lock.dotnetDesktopRuntime.version
    }
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}
