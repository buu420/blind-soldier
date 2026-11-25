[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $gameFolder,
    [Parameter(Mandatory=$true)] [string] $modFolder
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
try {
    Import-Module (Join-Path $PSScriptRoot 'FF7Launcher2013Install.psm1') -Force
    $bundle = Join-Path $PSScriptRoot 'bundle'
    # A corrupt bundle is a package failure. An absent/custom launcher is an
    # optional replacement refusal, and must not block the reader or migration
    # on a converted 7th Heaven game.
    Test-Ff7Accessible2013LauncherBundle -BundlePath $bundle | Out-Null
    $replaceLauncher = $false
    if (Test-Path -LiteralPath (Join-Path $gameFolder 'FF7_Launcher.exe') -PathType Leaf) {
        try {
            Install-Ff7Accessible2013Launcher -GameRoot $gameFolder -BundlePath $bundle -ValidateOnly | Out-Null
            $replaceLauncher = $true
        }
        catch {
            Write-Warning ('Your existing launcher was kept. The accessible 2013 launcher was not installed: ' + $_.Exception.Message)
        }
    }
    else {
        Write-Output 'No original 2013 launcher was found. Your existing launch method is unchanged.'
    }
    $cleanup = Join-Path $PSScriptRoot '..\Tools\Remove-AmethystRegistryEntries.ps1'
    $previousConfirmation = $env:BLIND_SOLDIER_CLEANUP_CONFIRM
    try {
        # The manager's script consent describes this migration. The child
        # removes only recognized obsolete Blind Soldier launch registrations.
        $env:BLIND_SOLDIER_CLEANUP_CONFIRM = 'Y'
        & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $cleanup
        if ($LASTEXITCODE -ne 0) { throw "Legacy launch-registration cleanup failed ($LASTEXITCODE)." }
    }
    finally { $env:BLIND_SOLDIER_CLEANUP_CONFIRM = $previousConfirmation }
    if ($replaceLauncher) {
        Install-Ff7Accessible2013Launcher -GameRoot $gameFolder -BundlePath $bundle | Out-Null
        Write-Output 'The accessible 2013 launcher is ready. Start Final Fantasy VII normally from Steam.'
    }
    exit 0
}
catch {
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    exit 1
}
