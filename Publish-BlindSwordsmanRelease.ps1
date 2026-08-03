[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $Tag,
    [Parameter(Mandatory=$true)] [string] $ArtifactPath,
    [string] $Repository = 'buu420/blind-soldier',
    [string] $NotesPath
)

$ErrorActionPreference = 'Stop'
$artifactRoot = [IO.Path]::GetFullPath($ArtifactPath)
$channelPath = Join-Path $artifactRoot 'blind-soldier-channel.json'
if (-not (Test-Path -LiteralPath $channelPath -PathType Leaf)) {
    throw "Release channel manifest is missing: $channelPath"
}
$channel = [IO.File]::ReadAllText($channelPath) | ConvertFrom-Json
if ([string]$channel.releaseTag -cne $Tag) {
    throw "Artifact release tag '$($channel.releaseTag)' does not match requested tag '$Tag'."
}
$assetNames = @(
    'Blind-Soldier-Setup.exe',
    'Blind-Soldier-Runtime.zip',
    'Blind-Soldier-Setup.exe.sha256',
    'Blind-Soldier-Runtime.zip.sha256',
    'blind-soldier-channel.json'
)
$assets = foreach ($name in $assetNames) {
    $path = Join-Path $artifactRoot $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Release asset is missing: $path"
    }
    $path
}

& gh auth status | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw 'GitHub CLI is not authenticated.'
}
$releaseListJson = (& gh release list --repo $Repository --limit 1000 --json tagName | Out-String)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to query existing GitHub releases for $Repository."
}
$existingRelease = @($releaseListJson | ConvertFrom-Json) |
    Where-Object { [string]$_.tagName -ceq $Tag } |
    Select-Object -First 1
if ($null -ne $existingRelease) {
    throw "GitHub release already exists and was not changed: $Repository $Tag"
}

$temporaryNotes = $null
try {
    if ([string]::IsNullOrWhiteSpace($NotesPath)) {
        $temporaryNotes = Join-Path ([IO.Path]::GetTempPath()) ('blind-soldier-release-notes-' + [Guid]::NewGuid().ToString('N') + '.md')
        $notes = @"
Blind Soldier $($channel.version) is an early public test release of the dual-runtime Final Fantasy VII accessibility mod.

Download `Blind-Soldier-Setup.exe` for the standard accessible installation experience. The installer is not code-signed yet, so Windows SmartScreen may identify the publisher as unknown.

This release supports the legacy x86 and Steam 2026 x64 game runtimes. The x64 backend remains prerelease research software.
"@
        [IO.File]::WriteAllText($temporaryNotes, $notes, (New-Object Text.UTF8Encoding($false)))
        $NotesPath = $temporaryNotes
    }
    $arguments = @('release', 'create', $Tag, '--repo', $Repository, '--title', "Blind Soldier $($channel.version)", '--notes-file', [IO.Path]::GetFullPath($NotesPath))
    if ([string]$channel.track -ceq 'prerelease') {
        $arguments += '--prerelease'
    }
    $arguments += @($assets)
    & gh @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub release creation failed with exit code $LASTEXITCODE."
    }
}
finally {
    if ($null -ne $temporaryNotes -and (Test-Path -LiteralPath $temporaryNotes)) {
        Remove-Item -LiteralPath $temporaryNotes -Force
    }
}
