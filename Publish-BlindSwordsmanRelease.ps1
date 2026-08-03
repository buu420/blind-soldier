[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $Tag,
    [Parameter(Mandatory=$true)] [string] $ArtifactPath,
    [string] $Repository = 'buu420/blind-swordsman',
    [string] $NotesPath
)

$ErrorActionPreference = 'Stop'
$artifactRoot = [IO.Path]::GetFullPath($ArtifactPath)
$channelPath = Join-Path $artifactRoot 'blind-swordsman-channel.json'
if (-not (Test-Path -LiteralPath $channelPath -PathType Leaf)) {
    throw "Release channel manifest is missing: $channelPath"
}
$channel = [IO.File]::ReadAllText($channelPath) | ConvertFrom-Json
if ([string]$channel.releaseTag -cne $Tag) {
    throw "Artifact release tag '$($channel.releaseTag)' does not match requested tag '$Tag'."
}
$assetNames = @(
    'Blind-Swordsman-Setup.exe',
    'Blind-Swordsman-Runtime.zip',
    'Blind-Swordsman-Setup.exe.sha256',
    'Blind-Swordsman-Runtime.zip.sha256',
    'blind-swordsman-channel.json'
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
& gh release view $Tag --repo $Repository *> $null
if ($LASTEXITCODE -eq 0) {
    throw "GitHub release already exists and was not changed: $Repository $Tag"
}

$temporaryNotes = $null
try {
    if ([string]::IsNullOrWhiteSpace($NotesPath)) {
        $temporaryNotes = Join-Path ([IO.Path]::GetTempPath()) ('blind-swordsman-release-notes-' + [Guid]::NewGuid().ToString('N') + '.md')
        $notes = @"
Blind Swordsman $($channel.version) is an early public test release of the dual-runtime Final Fantasy VII accessibility mod.

Download `Blind-Swordsman-Setup.exe` for the standard accessible installation experience. The installer is not code-signed yet, so Windows SmartScreen may identify the publisher as unknown.

This release supports the legacy x86 and Steam 2026 x64 game runtimes. The x64 backend remains prerelease research software.
"@
        [IO.File]::WriteAllText($temporaryNotes, $notes, (New-Object Text.UTF8Encoding($false)))
        $NotesPath = $temporaryNotes
    }
    $arguments = @('release', 'create', $Tag, '--repo', $Repository, '--title', "Blind Swordsman $($channel.version)", '--notes-file', [IO.Path]::GetFullPath($NotesPath))
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
