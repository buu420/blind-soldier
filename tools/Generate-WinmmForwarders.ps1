[CmdletBinding()]
param(
    [string] $SystemWinmmPath = "$env:WINDIR\SysWOW64\winmm.dll",
    [string] $ManifestPath,
    [string] $IncludePath,
    [string] $DefinitionPath,
    [switch] $AllowCompatibleSystemWinmm
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $repoRoot `
        'analysis\native-bootstrap\winmm-exports-10.0.26100.8737.json'
}
if ([string]::IsNullOrWhiteSpace($IncludePath)) {
    $IncludePath = Join-Path $repoRoot `
        'native\BlindSoldier.WinMMProxy\winmm_exports.inc'
}
if ([string]::IsNullOrWhiteSpace($DefinitionPath)) {
    $DefinitionPath = Join-Path $repoRoot `
        'native\BlindSoldier.WinMMProxy\winmm.def'
}
$expectedSha256 = `
    '761E7285BDCA295F82E9EC88FE73D7CF23FBDCB1757F0E043DC701BB3ECD3A51'
$expectedVersion = '10.0.26100.8737'
$evidenceManifestPath = Join-Path $repoRoot `
    'analysis\native-bootstrap\winmm-exports-10.0.26100.8737.json'

function Write-Utf8NoBom {
    param([string] $Path, [string] $Content)
    $parent = Split-Path -Parent ([IO.Path]::GetFullPath($Path))
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($Path), $Content,
        [Text.UTF8Encoding]::new($false))
}

function Get-Dumpbin {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} `
        'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
        throw 'Visual Studio vswhere.exe is unavailable.'
    }
    $install = (& $vswhere -latest -products '*' `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath | Select-Object -First 1)
    $dumpbin = Get-ChildItem -LiteralPath (Join-Path $install 'VC\Tools\MSVC') `
        -Recurse -Filter dumpbin.exe |
        Where-Object FullName -Match '\\Hostx64\\x64\\dumpbin\.exe$' |
        Sort-Object FullName -Descending | Select-Object -First 1 `
        -ExpandProperty FullName
    if (-not (Test-Path -LiteralPath $dumpbin -PathType Leaf)) {
        throw 'MSVC dumpbin.exe is unavailable.'
    }
    return $dumpbin
}

function Get-PeMachine {
    param([string] $Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or
        $bytes[1] -ne 0x5A) { throw 'WinMM is not a PE image.' }
    $offset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($offset -lt 64 -or $offset + 6 -gt $bytes.Length -or
        [BitConverter]::ToUInt32($bytes, $offset) -ne 0x00004550) {
        throw 'WinMM has an invalid PE header.'
    }
    return [BitConverter]::ToUInt16($bytes, $offset + 4)
}

$resolvedWinmm = [IO.Path]::GetFullPath($SystemWinmmPath)
if (-not (Test-Path -LiteralPath $resolvedWinmm -PathType Leaf)) {
    throw "System WinMM is unavailable: $resolvedWinmm"
}
$item = Get-Item -LiteralPath $resolvedWinmm
$sha256 = (Get-FileHash -LiteralPath $resolvedWinmm -Algorithm SHA256).Hash
$version = [string]$item.VersionInfo.ProductVersion
$machine = Get-PeMachine -Path $resolvedWinmm
if ($machine -ne 0x014C) {
    throw "System WinMM is not x86: machine=$machine"
}
$matchesEvidenceBinary = $sha256 -ceq $expectedSha256 -and
    $version -ceq $expectedVersion
if (-not $matchesEvidenceBinary -and -not $AllowCompatibleSystemWinmm) {
    throw "System WinMM does not match the evidence lock: version=$version machine=$machine sha256=$sha256"
}

$dumpbin = Get-Dumpbin
$lines = @(& $dumpbin /exports $resolvedWinmm)
if ($LASTEXITCODE -ne 0) { throw 'dumpbin failed to enumerate WinMM exports.' }
$exports = New-Object 'System.Collections.Generic.List[object]'
foreach ($line in $lines) {
    $trimmed = $line.Trim()
    if ($trimmed -match '^(\d+)\s+([0-9A-F]+)\s+\[NONAME\]$') {
        $exports.Add([ordered]@{
            ordinal = [int]$matches[1]
            name = $null
            noname = $true
        })
    }
    elseif ($trimmed -match '^(\d+)\s+[0-9A-F]+\s+[0-9A-F]+\s+(\S+)') {
        $exports.Add([ordered]@{
            ordinal = [int]$matches[1]
            name = [string]$matches[2]
            noname = $false
        })
    }
}
$orderedExports = @($exports | Sort-Object { [int]$_.ordinal })
if ($orderedExports.Count -ne 193 -or
    @($orderedExports | Where-Object { -not $_.noname }).Count -ne 192 -or
    @($orderedExports | Where-Object noname).Count -ne 1) {
    throw "Unexpected WinMM export counts: total=$($orderedExports.Count)"
}
for ($index = 0; $index -lt $orderedExports.Count; $index++) {
    if ([int]$orderedExports[$index].ordinal -ne $index + 2) {
        throw 'WinMM export ordinals are not the expected contiguous 2 through 194 range.'
    }
}
if ([int]@($orderedExports | Where-Object noname)[0].ordinal -ne 2) {
    throw 'The expected ordinal-only WinMM export is missing.'
}
if ($AllowCompatibleSystemWinmm) {
    if (-not (Test-Path -LiteralPath $evidenceManifestPath -PathType Leaf)) {
        throw "WinMM evidence manifest is unavailable: $evidenceManifestPath"
    }
    $evidenceManifest = [IO.File]::ReadAllText($evidenceManifestPath) |
        ConvertFrom-Json
    $actualSurface = $orderedExports | ConvertTo-Json -Compress
    $evidenceSurface = $evidenceManifest.exports |
        Select-Object ordinal,name,noname | ConvertTo-Json -Compress
    if ($actualSurface -cne $evidenceSurface) {
        throw 'System WinMM export surface does not match the evidence lock.'
    }
}

$manifest = [ordered]@{
    schemaVersion = 1
    source = 'Windows SysWOW64 winmm.dll'
    fileVersion = $version
    fileSize = [long]$item.Length
    sha256 = $sha256
    machine = [int]$machine
    ordinalBase = 2
    functionCount = 193
    nameCount = 192
    exports = $orderedExports
}
$manifestText = ($manifest | ConvertTo-Json -Depth 5) + "`n"

$includeLines = New-Object 'System.Collections.Generic.List[string]'
$includeLines.Add('// Generated by tools/Generate-WinmmForwarders.ps1. Do not edit.')
$definitionLines = New-Object 'System.Collections.Generic.List[string]'
$definitionLines.Add('; Generated by tools/Generate-WinmmForwarders.ps1. Do not edit.')
$definitionLines.Add('LIBRARY "winmm"')
$definitionLines.Add('EXPORTS')
for ($index = 0; $index -lt $orderedExports.Count; $index++) {
    $entry = $orderedExports[$index]
    $stub = 'BlindSoldierForward_{0:D3}' -f $index
    $includeLines.Add("BS_WINMM_FORWARD($stub, $index)")
    if ([bool]$entry.noname) {
        $definitionLines.Add("    $stub @$($entry.ordinal) NONAME")
    }
    else {
        $definitionLines.Add("    $($entry.name)=$stub @$($entry.ordinal)")
    }
}

Write-Utf8NoBom -Path $ManifestPath -Content $manifestText
Write-Utf8NoBom -Path $IncludePath `
    -Content (($includeLines -join "`n") + "`n")
Write-Utf8NoBom -Path $DefinitionPath `
    -Content (($definitionLines -join "`n") + "`n")

[pscustomobject]@{
    ManifestPath = [IO.Path]::GetFullPath($ManifestPath)
    IncludePath = [IO.Path]::GetFullPath($IncludePath)
    DefinitionPath = [IO.Path]::GetFullPath($DefinitionPath)
    ExportCount = $orderedExports.Count
    Sha256 = $sha256
}
