[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $PortableRoot,
    [Parameter(Mandatory=$true)] [string] $OutputRoot,
    [string] $ReadmePath = (Join-Path $PSScriptRoot 'README.md')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-OrdinaryTree {
    param([string] $Path, [string] $Label)
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Label is missing: $Path"
    }
    foreach ($item in @((Get-Item -LiteralPath $Path -Force)) +
            @(Get-ChildItem -LiteralPath $Path -Force -Recurse)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Label cannot contain a reparse point: $($item.FullName)"
        }
    }
}

function Assert-OrdinaryFile {
    param([string] $Path, [string] $Label)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label is missing: $Path"
    }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label cannot be a reparse point: $Path"
    }
}

function Copy-RequiredTree {
    param([string] $Source, [string] $Destination, [string] $Label)
    Assert-OrdinaryTree -Path $Source -Label $Label
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $Destination `
            -Recurse -Force
    }
}

function Copy-RequiredFile {
    param([string] $Source, [string] $Destination, [string] $Label)
    Assert-OrdinaryFile -Path $Source -Label $Label
    $parent = Split-Path -Parent $Destination
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Copy-CommonAndX86Tree {
    param([string] $Source, [string] $Destination, [string] $Label)
    Assert-OrdinaryTree -Path $Source -Label $Label
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        if ($item.Name -ieq 'x64') { continue }
        Copy-Item -LiteralPath $item.FullName -Destination $Destination `
            -Recurse -Force
    }
}

$portable = [IO.Path]::GetFullPath($PortableRoot)
$output = [IO.Path]::GetFullPath($OutputRoot)
$readme = [IO.Path]::GetFullPath($ReadmePath)
Assert-OrdinaryTree -Path $portable -Label 'Portable Blind Soldier root'
Assert-OrdinaryFile -Path $readme -Label 'Blind Soldier README'

if (Test-Path -LiteralPath $output) {
    throw "OutputRoot already exists: $output"
}

$old = Join-Path $output 'ffviiold'
$new = Join-Path $output 'ffviinew'
try {
    New-Item -ItemType Directory -Path $old, $new -Force | Out-Null

    Copy-RequiredTree -Source $portable -Destination $new `
        -Label 'Portable Blind Soldier root'
    Copy-RequiredFile -Source $readme -Destination (Join-Path $new `
        'README-Blind-Soldier.md') -Label 'Blind Soldier README'

    foreach ($relative in @(
        'Blind-Soldier\Bootstrap\x86',
        'Blind-Soldier\Runtime\dotnet\x86',
        'Blind-Soldier\Tools',
        'Blind-Soldier\Policy',
        'LICENSES',
        'Reloaded-II\Loader\X86'
    )) {
        Copy-RequiredTree -Source (Join-Path $portable $relative) `
            -Destination (Join-Path $old $relative) -Label $relative
    }

    Copy-RequiredFile -Source (Join-Path $portable `
        'ff7_en.exe.local\version.dll') -Destination (Join-Path $old `
        'version.dll') -Label 'x86 sibling Version proxy'

    foreach ($modId in @(
        'ff7.accessibility.reloaded',
        'reloaded.sharedlib.hooks'
    )) {
        $relative = "Reloaded-II\Mods\$modId"
        Copy-CommonAndX86Tree -Source (Join-Path $portable $relative) `
            -Destination (Join-Path $old $relative) -Label $relative
    }

    foreach ($relative in @(
        'Reloaded-II\Apps',
        'Reloaded-II\User',
        'Reloaded-II\Plugins'
    )) {
        Copy-RequiredTree -Source (Join-Path $portable $relative) `
            -Destination (Join-Path $old $relative) -Label $relative
    }
    Copy-RequiredFile -Source (Join-Path $portable `
        'Reloaded-II\portable.txt') -Destination (Join-Path $old `
        'Reloaded-II\portable.txt') -Label 'Reloaded portable marker'
    Copy-RequiredFile -Source $readme -Destination (Join-Path $old `
        'README-Blind-Soldier.md') -Label 'Blind Soldier README'

    [pscustomobject]@{
        OldSource = $old
        NewSource = $new
    }
}
catch {
    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Recurse -Force
    }
    throw
}
