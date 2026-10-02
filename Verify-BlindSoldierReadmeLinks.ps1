#Requires -Version 5.1
<#
.SYNOPSIS
Fails a release whose README points at a different release than the one being
built.

.DESCRIPTION
The README's download links carry an explicit tag, and the release process
rewrites them each time. Nothing checked that it had. A publish that missed the
rewrite would leave the front page of the repository offering the previous
build, and the failure is silent: the links still work, the ZIP still installs,
and the player simply does not get the fix that was just released. That is the
worst shape a fault can take in a mod whose whole job is to say things out
loud - the 0.4.1 release existed precisely because screen readers other than
NVDA got silence instead of speech, and a stale README hands exactly that
silence back to the next person who downloads it.

Links to /releases/latest/download/ are rejected rather than accepted as a
version-free alternative. GitHub resolves "latest" to the newest release that
is NOT marked as a prerelease, and Blind Soldier ships prereleases; on
2026-08-21 that URL resolved to v0.1.11, a build from before the screen reader
fix. It looks like the maintenance-free answer and is a silent downgrade.

.PARAMETER ExpectedVersion
The version being released, without the leading "v".

.PARAMETER ReadmePath
The README to check. Defaults to the one beside this script.

.EXAMPLE
./Verify-BlindSoldierReadmeLinks.ps1 -ExpectedVersion 0.4.1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $ExpectedVersion,
    [string] $ReadmePath = (Join-Path $PSScriptRoot 'README.md')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$semanticVersionPattern = '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'
if ($ExpectedVersion -notmatch $semanticVersionPattern) {
    throw "Invalid semantic version: $ExpectedVersion"
}

if (-not (Test-Path -LiteralPath $ReadmePath -PathType Leaf)) {
    throw "The README is missing: $ReadmePath"
}

# The assets the release workflow actually uploads. A link to anything else is a
# broken download however well-formed its tag is.
$publishedAssets = @(
    'Blind-Soldier-Portable.zip',
    'Blind-Soldier-Portable.zip.sha256',
    'Blind-Soldier-2013-x86-Portable.zip',
    'Blind-Soldier-2013-x86-Portable.zip.sha256'
)

$readme = Get-Content -LiteralPath $ReadmePath -Raw
$expectedTag = "v$ExpectedVersion"
$problems = New-Object System.Collections.Generic.List[string]

$latestPattern = 'https://github\.com/[^/\s)]+/[^/\s)]+/releases/latest/download/(?<asset>[^\s)]+)'
foreach ($match in [regex]::Matches($readme, $latestPattern)) {
    $problems.Add(
        ("A /releases/latest/download/ link is used for {0}. " -f $match.Groups['asset'].Value) +
        'GitHub resolves "latest" to the newest release that is not a prerelease, and ' +
        'Blind Soldier ships prereleases, so that URL serves an old build. Use an ' +
        "explicit /releases/download/$expectedTag/ link instead.")
}

$downloadPattern = 'https://github\.com/[^/\s)]+/[^/\s)]+/releases/download/(?<tag>[^/\s)]+)/(?<asset>[^\s)]+)'
$downloadLinks = [regex]::Matches($readme, $downloadPattern)

if ($downloadLinks.Count -eq 0 -and $problems.Count -eq 0) {
    # Nothing to compare means nothing was checked. A README that lost its
    # download links would otherwise pass this quietly.
    $problems.Add(
        'No release download links were found in the README. This check exists to ' +
        'keep them pointing at the release being built, so their absence is a ' +
        'failure rather than a pass.')
}

foreach ($match in $downloadLinks) {
    $tag = $match.Groups['tag'].Value
    $asset = $match.Groups['asset'].Value

    if ($tag -ne $expectedTag) {
        $problems.Add(
            "The download link for $asset points at $tag, but the release being " +
            "built is $expectedTag.")
    }

    if ($publishedAssets -notcontains $asset) {
        $problems.Add(
            "The download link for $asset does not name an asset this release " +
            'publishes. Published assets are: ' + ($publishedAssets -join ', ') + '.')
    }
}

if ($problems.Count -gt 0) {
    $detail = ($problems | ForEach-Object { "  - $_" }) -join [Environment]::NewLine
    throw @"
The README does not match the release being built ($expectedTag).
$detail
Update the download links in $ReadmePath and tag again.
"@
}

Write-Host (
    "README release links verified for {0}: {1} download link(s), all pointing at {0}." -f
    $expectedTag, $downloadLinks.Count)
