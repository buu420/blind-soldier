[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $gameFolder,
    [Parameter(Mandatory=$true)] [string] $modFolder
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
try {
    # Amethyst caches this script alone and calls it before restoring its file
    # journal. The module is still present in the installed payload at that time.
    $module = Join-Path $gameFolder 'Blind-Soldier\Launcher2013\FF7Launcher2013Install.psm1'
    Import-Module $module -Force
    $result = Uninstall-Ff7Accessible2013Launcher -GameRoot $gameFolder
    foreach ($message in $result.Preserved) { Write-Warning $message }
    Write-Output 'The original 2013 launcher has been restored wherever the installed files were unchanged.'
    exit 0
}
catch {
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    exit 1
}
