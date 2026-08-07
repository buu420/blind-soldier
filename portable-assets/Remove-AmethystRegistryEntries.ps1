[CmdletBinding()]
param([switch] $Elevated)

$ErrorActionPreference = 'Stop'
$ownerValueName = 'BlindSoldierDebuggerOwner'
$legacyLauncherName = 'BlindSoldier_Launcher.exe'
$defaultIfeoRoot = `
    'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options'
$targets = @(
    [pscustomobject]@{
        Executable = 'ff7_en.exe'
        Launchers = @('Blind-Soldier-Launcher-x86.exe', $legacyLauncherName)
    },
    [pscustomobject]@{
        Executable = 'FFVII.exe'
        Launchers = @('Blind-Soldier-Launcher-x64.exe', $legacyLauncherName)
    }
)
$registryViews = @(
    [Microsoft.Win32.RegistryView]::Registry32,
    [Microsoft.Win32.RegistryView]::Registry64
)

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-RegistryString {
    param([Microsoft.Win32.RegistryKey] $Key, [string] $Name)
    if (-not ($Key.GetValueNames() -ccontains $Name)) { return $null }
    if ($Key.GetValueKind($Name) -ne
            [Microsoft.Win32.RegistryValueKind]::String) {
        throw "Registry value '$Name' is not REG_SZ."
    }
    [string] $Key.GetValue($Name, $null,
        [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
}

function Test-LauncherValue {
    param([string] $Value, [string[]] $AllowedLaunchers)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    try {
        $leaf = [IO.Path]::GetFileName($Value.Trim().Trim('"'))
        foreach ($allowed in $AllowedLaunchers) {
            if ($leaf.Equals($allowed,
                    [StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
    }
    catch { return $false }
    return $false
}

$testMode = $env:BLIND_SOLDIER_CLEANUP_TEST_MODE -eq '1'
$hiveName = 'LocalMachine'
$ifeoRoot = $defaultIfeoRoot
if ($testMode) {
    if ($env:BLIND_SOLDIER_CLEANUP_HIVE -ne 'CurrentUser' -or
        [string]::IsNullOrWhiteSpace($env:BLIND_SOLDIER_CLEANUP_IFEO_ROOT) -or
        -not $env:BLIND_SOLDIER_CLEANUP_IFEO_ROOT.StartsWith(
            'Software\BlindSoldier\CleanupTests\',
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The registry cleanup test override is invalid.'
    }
    $hiveName = 'CurrentUser'
    $ifeoRoot = $env:BLIND_SOLDIER_CLEANUP_IFEO_ROOT
}
elseif ($env:BLIND_SOLDIER_CLEANUP_HIVE -or
        $env:BLIND_SOLDIER_CLEANUP_IFEO_ROOT) {
    throw 'Registry target overrides are allowed only in cleanup test mode.'
}

if (-not $testMode -and -not (Test-IsAdministrator)) {
    Write-Host 'Requesting administrator access for the legacy entries...'
    $arguments = @(
        '-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', ('"{0}"' -f $PSCommandPath), '-Elevated'
    )
    try {
        $child = Start-Process -FilePath 'powershell.exe' `
            -ArgumentList $arguments -Verb RunAs -Wait -PassThru
        exit $child.ExitCode
    }
    catch {
        Write-Error ('Administrator access was not granted. ' +
            $_.Exception.Message)
        exit 1
    }
}

Write-Host 'This cleanup checks only these legacy Blind Soldier entries:'
foreach ($view in $registryViews) {
    foreach ($target in $targets) {
        Write-Host ('  {0} view: {1}\{2}' -f $view, $ifeoRoot,
            $target.Executable)
    }
}
Write-Host 'It recognizes the exact launchers used by the old manager packages.'
Write-Host 'All unrelated values and foreign debugger registrations are preserved.'
Write-Host ''
if ($env:BLIND_SOLDIER_CLEANUP_CONFIRM -ne 'Y') {
    $answer = Read-Host 'Type Y and press Enter to continue, or N to cancel'
    if ($answer -ne 'Y') {
        Write-Host 'Cleanup cancelled. Nothing was changed.'
        exit 0
    }
}

if ($hiveName -eq 'CurrentUser') {
    $registryHive = [Microsoft.Win32.RegistryHive]::CurrentUser
}
else {
    $registryHive = [Microsoft.Win32.RegistryHive]::LocalMachine
}
$removed = 0
$unchanged = 0
$failures = 0
foreach ($view in $registryViews) {
    $baseKey = $null
    try {
        $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
            $registryHive, $view)
        foreach ($target in $targets) {
            $keyPath = '{0}\{1}' -f $ifeoRoot, $target.Executable
            $key = $null
            try {
                $key = $baseKey.OpenSubKey($keyPath, $true)
                if ($null -eq $key) {
                    Write-Host ("Not present in {0}: {1}" -f $view,
                        $target.Executable)
                    $unchanged++
                    continue
                }
                $debugger = Get-RegistryString -Key $key -Name 'Debugger'
                $owner = Get-RegistryString -Key $key -Name $ownerValueName
                if ($null -eq $owner) {
                    $isLegacy = Test-LauncherValue -Value $debugger `
                        -AllowedLaunchers @($legacyLauncherName)
                    if ($isLegacy) {
                        $key.DeleteValue('Debugger', $false)
                        Write-Host (
                            "Removed old manager launch entry in {0}: {1}" -f `
                            $view, $target.Executable)
                        $removed++
                    }
                    else {
                        Write-Host (
                            "Preserved unowned entry in {0}: {1}" -f `
                            $view, $target.Executable)
                        $unchanged++
                    }
                    continue
                }
                if ($null -eq $debugger) {
                    if (Test-LauncherValue -Value $owner `
                            -AllowedLaunchers $target.Launchers) {
                        $key.DeleteValue($ownerValueName, $false)
                        Write-Host (
                            "Removed stale owner marker in {0}: {1}" -f `
                            $view, $target.Executable)
                        $removed++
                    }
                    else {
                        Write-Host (
                            "Preserved unfamiliar owner marker in {0}: {1}" -f `
                            $view, $target.Executable)
                        $unchanged++
                    }
                    continue
                }
                $sameValue = $debugger.Equals(
                    $owner, [StringComparison]::OrdinalIgnoreCase)
                $ownedLauncher = Test-LauncherValue -Value $debugger `
                    -AllowedLaunchers $target.Launchers
                if (-not $sameValue -or -not $ownedLauncher) {
                    Write-Host (
                        "Preserved foreign or mismatched entry in {0}: {1}" -f `
                        $view, $target.Executable)
                    $unchanged++
                    continue
                }
                $key.DeleteValue('Debugger', $false)
                $key.DeleteValue($ownerValueName, $false)
                Write-Host (
                    "Removed owned Blind Soldier entry in {0}: {1}" -f `
                    $view, $target.Executable)
                $removed++
            }
            catch {
                Write-Error ("Could not inspect {0} in {1}: {2}" -f `
                    $target.Executable, $view, $_.Exception.Message)
                $failures++
            }
            finally {
                if ($null -ne $key) { $key.Dispose() }
            }
        }
    }
    finally {
        if ($null -ne $baseKey) { $baseKey.Dispose() }
    }
}

Write-Host ''
Write-Host ("Cleanup complete. Removed: {0}. Preserved or absent: {1}." -f `
    $removed, $unchanged)
if ($failures -ne 0) {
    Write-Error ("Cleanup finished with {0} error(s)." -f $failures)
    exit 1
}
exit 0
