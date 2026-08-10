[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $Version
)

$ErrorActionPreference = 'Stop'
$semanticVersionPattern = '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'
if ($Version -notmatch $semanticVersionPattern) {
    throw "Version is not strict semantic version text: $Version"
}

$major = [int64]$Matches[1]
if ($major -eq 0 -or $Version.Contains('-')) {
    'prerelease'
}
else {
    'stable'
}
