[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [ValidateSet('Install','Uninstall')] [string] $Action,
    [Parameter(Mandatory=$true)] [string] $GameRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
try {
    Import-Module (Join-Path $PSScriptRoot 'FF7Launcher2013Install.psm1') -Force
    if ($Action -ceq 'Install') {
        $result = Install-Ff7Accessible2013Launcher -GameRoot $GameRoot -BundlePath (Join-Path $PSScriptRoot 'bundle')
        Write-Output 'The accessible 2013 launcher is installed. Start Final Fantasy VII normally from Steam.'
        Write-Output 'The Original launcher button opens the preserved Square Enix launcher.'
    }
    else {
        $result = Uninstall-Ff7Accessible2013Launcher -GameRoot $GameRoot
        foreach ($message in $result.Preserved) { Write-Warning $message }
        Write-Output 'The original 2013 launcher has been restored wherever the installed files were unchanged.'
    }
    exit 0
}
catch {
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    exit 1
}
