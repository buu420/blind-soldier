[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $DestinationRoot,
    [Parameter(Mandatory=$true)] [string] $BundlePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'FF7Launcher2013Install.psm1') -Force
$bundle = Test-Ff7Accessible2013LauncherBundle -BundlePath $BundlePath
$root = [IO.Path]::GetFullPath($DestinationRoot)
$payload = Join-Path $root 'Blind-Soldier\Launcher2013'
if (Test-Path -LiteralPath $payload) {
    throw "The 2013 launcher payload already exists: $payload"
}
New-Item -ItemType Directory -Path (Join-Path $payload 'bundle') -Force | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $bundle.Root -File) {
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $payload 'bundle')
}
foreach ($record in @(
    @('FF7Launcher2013Install.psm1', 'Blind-Soldier\Launcher2013\FF7Launcher2013Install.psm1'),
    @('portable-assets\Manage-2013Launcher.ps1', 'Blind-Soldier\Launcher2013\Manage-2013Launcher.ps1'),
    @('portable-assets\Manager-Install2013Launcher.ps1', 'Blind-Soldier\Launcher2013\Manager-Install2013Launcher.ps1'),
    @('portable-assets\Manager-Restore2013Launcher.ps1', 'Blind-Soldier\Launcher2013\Manager-Restore2013Launcher.ps1'),
    @('portable-assets\Install-2013-Accessible-Launcher.cmd', 'Install-2013-Accessible-Launcher.cmd'),
    @('portable-assets\Restore-2013-Original-Launcher.cmd', 'Restore-2013-Original-Launcher.cmd')
)) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $record[0]) -Destination (Join-Path $root $record[1])
}

# The native launcher is deliberately nested. Extracting a ZIP must never
# overwrite the stock 2013 launcher before the install module backs it up.
Test-Ff7Accessible2013LauncherBundle -BundlePath (Join-Path $payload 'bundle') | Out-Null
