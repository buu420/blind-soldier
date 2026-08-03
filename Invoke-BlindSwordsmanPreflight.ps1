[CmdletBinding()]
param(
    [string] $GameRoot,
    [string] $SteamRoot,
    [string] $ReloadedRoot,
    [string] $SeventhHeavenRoot,
    [Parameter(Mandatory=$true)] [string] $ResultPath,
    [Parameter(DontShow=$true)] [string] $ModulePath,
    [Parameter(DontShow=$true)] [string] $ReloadedSettingsPath
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($ModulePath)) {
    $ModulePath = Join-Path $scriptRoot 'FF7SteamInstall.psm1'
}
Import-Module $ModulePath -Force

$dependencies = New-Object 'System.Collections.Generic.List[object]'

function Add-Dependency {
    param(
        [Parameter(Mandatory=$true)] [string] $Id,
        [Parameter(Mandatory=$true)] [string] $Name,
        [Parameter(Mandatory=$true)] [ValidateSet('required', 'blocking', 'optional')] [string] $Severity,
        [Parameter(Mandatory=$true)] [bool] $Satisfied,
        [Parameter(Mandatory=$true)] [string] $Message,
        [AllowNull()] [string] $Path
    )

    $dependencies.Add([ordered]@{
        id = $Id
        name = $Name
        severity = $Severity
        satisfied = $Satisfied
        message = $Message
        path = if ([string]::IsNullOrWhiteSpace($Path)) { $null } else { [IO.Path]::GetFullPath($Path) }
    })
}

function Resolve-ReloadedDirectory {
    param([string] $ExplicitPath, [string] $GameRoot, [string] $SettingsPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        return [IO.Path]::GetFullPath($ExplicitPath)
    }
    $environmentValue = [Environment]::GetEnvironmentVariable('RELOADED_II_ROOT')
    if (-not [string]::IsNullOrWhiteSpace($environmentValue)) {
        return [IO.Path]::GetFullPath($environmentValue)
    }

    if ([string]::IsNullOrWhiteSpace($SettingsPath)) {
        $SettingsPath = Join-Path ([Environment]::GetFolderPath('ApplicationData')) `
            'Reloaded-Mod-Loader-II\ReloadedII.json'
    }
    if (Test-Path -LiteralPath $SettingsPath -PathType Leaf) {
        try {
            $settings = [IO.File]::ReadAllText($SettingsPath) | ConvertFrom-Json
            $launcherPath = [string]$settings.LauncherPath
            if (-not [string]::IsNullOrWhiteSpace($launcherPath) -and
                (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
                return [IO.Path]::GetFullPath((Split-Path -Parent $launcherPath))
            }
        }
        catch {
            # A stale or malformed global Reloaded setting is ignored. The
            # dependency report below will still provide an actionable path.
        }
    }

    $candidates = New-Object 'System.Collections.Generic.List[string]'
    if (-not [string]::IsNullOrWhiteSpace($GameRoot)) {
        $candidates.Add((Join-Path $GameRoot 'Reloaded-II'))
        $gameParent = Split-Path -Parent $GameRoot
        if (-not [string]::IsNullOrWhiteSpace($gameParent)) {
            $candidates.Add((Join-Path $gameParent 'Reloaded-II'))
        }
    }
    foreach ($candidate in @(
        (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Reloaded-II'),
        (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\Reloaded-II'),
        (Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Reloaded-II'),
        (Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Reloaded-II')
    )) {
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            $candidates.Add($candidate)
        }
    }
    foreach ($candidate in $candidates) {
        $fullPath = [IO.Path]::GetFullPath($candidate)
        if (Test-Path -LiteralPath (Join-Path $fullPath 'Reloaded-II.exe') -PathType Leaf) {
            return $fullPath
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($GameRoot)) {
        return [IO.Path]::GetFullPath((Join-Path $GameRoot 'Reloaded-II'))
    }
    return [IO.Path]::GetFullPath((Join-Path `
        ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\Reloaded-II'))
}

function Resolve-SeventhHeavenDirectory {
    param([string] $ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        return [IO.Path]::GetFullPath($ExplicitPath)
    }
    $environmentValue = [Environment]::GetEnvironmentVariable('SEVENTH_HEAVEN_ROOT')
    if (-not [string]::IsNullOrWhiteSpace($environmentValue)) {
        return [IO.Path]::GetFullPath($environmentValue)
    }

    foreach ($registryRoot in @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
    )) {
        foreach ($key in @(Get-ChildItem -LiteralPath $registryRoot -ErrorAction SilentlyContinue)) {
            try {
                $properties = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction Stop
                if ([string]$properties.DisplayName -notmatch '^7th Heaven(?:\s|$)') { continue }
                $installLocation = [string]$properties.InstallLocation
                if (-not [string]::IsNullOrWhiteSpace($installLocation) -and
                    (Test-Path -LiteralPath $installLocation -PathType Container)) {
                    return [IO.Path]::GetFullPath($installLocation)
                }
            }
            catch {
                continue
            }
        }
    }

    foreach ($candidate in @(
        (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\7th Heaven'),
        (Join-Path ([Environment]::GetFolderPath('ProgramFiles')) '7th Heaven'),
        (Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) '7th Heaven')
    )) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and
            (Test-Path -LiteralPath $candidate -PathType Container)) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }
    return $null
}

$installation = $null
$gameResult = $null
$requiredArchitectures = @()
try {
    $resolveArguments = @{}
    if (-not [string]::IsNullOrWhiteSpace($GameRoot)) { $resolveArguments.GameRoot = $GameRoot }
    if (-not [string]::IsNullOrWhiteSpace($SteamRoot)) { $resolveArguments.SteamRoot = $SteamRoot }
    $installation = Resolve-Ff7Installation @resolveArguments
    if ($null -ne $installation.NativeRuntime) {
        Assert-Ff7NativeRuntimeIdentity -Path ([string]$installation.NativeRuntime.GameExe) | Out-Null
    }

    $runtimes = New-Object 'System.Collections.Generic.List[object]'
    foreach ($runtime in @($installation.LegacyRuntime, $installation.NativeRuntime)) {
        if ($null -eq $runtime) { continue }
        $runtimes.Add([ordered]@{
            id = [string]$runtime.RuntimeId
            architecture = [string]$runtime.Architecture
            root = [IO.Path]::GetFullPath([string]$runtime.RuntimeRoot)
            executable = [IO.Path]::GetFullPath([string]$runtime.GameExe)
        })
    }
    $gameResult = [ordered]@{
        version = [string]$installation.Version
        steamAppId = [string]$installation.SteamAppId
        gameRoot = [IO.Path]::GetFullPath([string]$installation.GameRoot)
        runtimes = $runtimes.ToArray()
    }
    $requiredArchitectures = @($runtimes | ForEach-Object { [string]$_.architecture } | Select-Object -Unique)
    Add-Dependency -Id 'game' -Name 'Final Fantasy VII' -Severity required -Satisfied $true `
        -Message "Supported $($installation.Version) installation detected." -Path ([string]$installation.GameRoot)
}
catch {
    Add-Dependency -Id 'game' -Name 'Final Fantasy VII' -Severity blocking -Satisfied $false `
        -Message $_.Exception.Message -Path $GameRoot
}

$resolvedReloadedRoot = Resolve-ReloadedDirectory -ExplicitPath $ReloadedRoot `
    -GameRoot $(if ($null -ne $gameResult) { [string]$gameResult.gameRoot } else { $null }) `
    -SettingsPath $ReloadedSettingsPath
$reloadedAvailable = Test-Path -LiteralPath $resolvedReloadedRoot -PathType Container
Add-Dependency -Id 'reloaded' -Name 'Reloaded-II' `
    -Severity $(if ($reloadedAvailable) { 'required' } else { 'blocking' }) `
    -Satisfied $reloadedAvailable `
    -Message $(if ($reloadedAvailable) { 'Reloaded-II folder detected.' } else { "Reloaded-II was not found. A portable installation can be placed at '$resolvedReloadedRoot', or choose an existing folder." }) `
    -Path $resolvedReloadedRoot

$loaderFailures = New-Object 'System.Collections.Generic.List[string]'
if ($reloadedAvailable) {
    foreach ($loader in @(
        [pscustomobject]@{ Architecture = 'x86'; Label = 'x86 ASI loader'; Relative = '_asi_extract\ASILoader32.dll'; Machine = 0x014C },
        [pscustomobject]@{ Architecture = 'x86'; Label = 'x86 bootstrapper'; Relative = 'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll'; Machine = 0x014C },
        [pscustomobject]@{ Architecture = 'x64'; Label = 'x64 ASI loader'; Relative = '_asi_extract\ASILoader64.dll'; Machine = 0x8664 },
        [pscustomobject]@{ Architecture = 'x64'; Label = 'x64 bootstrapper'; Relative = 'Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll'; Machine = 0x8664 }
    ) | Where-Object { $requiredArchitectures -contains $_.Architecture }) {
        $loaderPath = Join-Path $resolvedReloadedRoot $loader.Relative
        if (-not (Test-Path -LiteralPath $loaderPath -PathType Leaf)) {
            $loaderFailures.Add("$($loader.Label) is missing")
            continue
        }
        try {
            $machine = Get-Ff7PeMachine -Path $loaderPath
            if ($machine -ne $loader.Machine) {
                $loaderFailures.Add(("{0} has machine 0x{1:X4}, expected 0x{2:X4}" -f `
                    $loader.Label, $machine, $loader.Machine))
            }
        }
        catch {
            $loaderFailures.Add("$($loader.Label) is invalid: $($_.Exception.Message)")
        }
    }
}
else {
    $loaderFailures.Add('Reloaded-II is unavailable')
}
$loadersReady = $loaderFailures.Count -eq 0
$requiredArchitectureLabel = $requiredArchitectures -join ' and '
$loaderDependencyName = if ($requiredArchitectures.Count -gt 0) {
    "Reloaded-II $requiredArchitectureLabel loaders"
}
else { 'Reloaded-II loaders' }
$loaderReadyMessage = if ($requiredArchitectures.Count -gt 0) {
    "Required $requiredArchitectureLabel loader files are valid."
}
else { 'Loader requirements are unavailable until a supported game runtime is detected.' }
Add-Dependency -Id 'reloaded-loaders' -Name $loaderDependencyName `
    -Severity $(if ($loadersReady) { 'required' } else { 'blocking' }) -Satisfied $loadersReady `
    -Message $(if ($loadersReady) { $loaderReadyMessage } else { $loaderFailures -join '; ' }) `
    -Path $resolvedReloadedRoot

$sharedHooksRoot = Join-Path $resolvedReloadedRoot 'Mods\reloaded.sharedlib.hooks'
$sharedHooksFailures = New-Object 'System.Collections.Generic.List[string]'
$sharedConfigPath = Join-Path $sharedHooksRoot 'ModConfig.json'
if (-not (Test-Path -LiteralPath $sharedConfigPath -PathType Leaf)) {
    $sharedHooksFailures.Add('ModConfig.json is missing')
}
else {
    try {
        $sharedConfig = [IO.File]::ReadAllText($sharedConfigPath) | ConvertFrom-Json
        if ([string]$sharedConfig.ModId -cne 'reloaded.sharedlib.hooks') {
            $sharedHooksFailures.Add('ModConfig.json has the wrong ModId')
        }
    }
    catch {
        $sharedHooksFailures.Add("ModConfig.json is invalid: $($_.Exception.Message)")
    }
}
foreach ($hook in @(
    [pscustomobject]@{ Architecture = 'x86'; Label = 'x86 shared hooks'; Relative = 'x86\Reloaded.Hooks.ReloadedII.dll'; Machine = 0x014C },
    [pscustomobject]@{ Architecture = 'x64'; Label = 'x64 shared hooks'; Relative = 'x64\Reloaded.Hooks.ReloadedII.dll'; Machine = 0x8664 }
) | Where-Object { $requiredArchitectures -contains $_.Architecture }) {
    $hookPath = Join-Path $sharedHooksRoot $hook.Relative
    if (-not (Test-Path -LiteralPath $hookPath -PathType Leaf)) {
        $sharedHooksFailures.Add("$($hook.Label) is missing")
        continue
    }
    try {
        $machine = Get-Ff7PeMachine -Path $hookPath
        if ($machine -ne $hook.Machine) {
            $sharedHooksFailures.Add(("{0} has machine 0x{1:X4}, expected 0x{2:X4}" -f `
                $hook.Label, $machine, $hook.Machine))
        }
    }
    catch {
        $sharedHooksFailures.Add("$($hook.Label) is invalid: $($_.Exception.Message)")
    }
}
$sharedHooksReady = $sharedHooksFailures.Count -eq 0
Add-Dependency -Id 'shared-hooks' -Name 'Reloaded-II Shared Hooks' `
    -Severity $(if ($sharedHooksReady) { 'required' } else { 'blocking' }) -Satisfied $sharedHooksReady `
    -Message $(if ($sharedHooksReady) {
        if ($requiredArchitectures.Count -gt 0) {
            "Reloaded-II Shared Hooks $requiredArchitectureLabel files are ready."
        }
        else { 'Shared Hooks requirements are unavailable until a supported game runtime is detected.' }
    } else { $sharedHooksFailures -join '; ' }) `
    -Path $sharedHooksRoot

$resolvedSeventhHeavenRoot = Resolve-SeventhHeavenDirectory -ExplicitPath $SeventhHeavenRoot
$seventhHeavenAvailable = -not [string]::IsNullOrWhiteSpace($resolvedSeventhHeavenRoot) -and `
    (Test-Path -LiteralPath $resolvedSeventhHeavenRoot -PathType Container)
Add-Dependency -Id 'seventh-heaven' -Name '7th Heaven' -Severity optional -Satisfied $seventhHeavenAvailable `
    -Message $(if ($seventhHeavenAvailable) { 'Optional integration detected for the legacy x86 path.' } else { 'Not installed. This integration is optional; Blind Swordsman can still be installed.' }) `
    -Path $(if ($seventhHeavenAvailable) { $resolvedSeventhHeavenRoot } else { $null })

$ffnxPath = if ($null -ne $installation -and $null -ne $installation.LegacyRuntime) {
    Join-Path ([string]$installation.LegacyRuntime.RuntimeRoot) 'AF3DN.P'
}
else { $null }
$ffnxConfigPath = if ($null -ne $installation -and $null -ne $installation.LegacyRuntime) {
    Join-Path ([string]$installation.LegacyRuntime.RuntimeRoot) 'FFNx.toml'
}
else { $null }
$ffnxAvailable = -not [string]::IsNullOrWhiteSpace($ffnxPath) -and `
    (Test-Path -LiteralPath $ffnxPath -PathType Leaf) -and `
    (Test-Path -LiteralPath $ffnxConfigPath -PathType Leaf)
Add-Dependency -Id 'ffnx' -Name 'FFNx' -Severity optional -Satisfied $ffnxAvailable `
    -Message $(if ($ffnxAvailable) { 'Optional integration detected for the legacy x86 path.' } else { 'Not installed. This integration is optional; Blind Swordsman can still be installed.' }) `
    -Path $(if ($ffnxAvailable) { $ffnxPath } else { $null })

$canInstall = $null -ne $gameResult -and @($dependencies | Where-Object {
    $_.severity -ne 'optional' -and -not $_.satisfied
}).Count -eq 0
$seventhHeavenResult = if ($seventhHeavenAvailable) { $resolvedSeventhHeavenRoot } else { $null }
$dependencyResults = $dependencies.ToArray()
$result = [ordered]@{
    schemaVersion = 1
    canInstall = $canInstall
    game = $gameResult
    reloadedRoot = $resolvedReloadedRoot
    seventhHeavenRoot = $seventhHeavenResult
    dependencies = $dependencyResults
}

$resolvedResultPath = [IO.Path]::GetFullPath($ResultPath)
$resultDirectory = Split-Path -Parent $resolvedResultPath
if (-not (Test-Path -LiteralPath $resultDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
}
$temporaryPath = Join-Path $resultDirectory ('.preflight-' + [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    [IO.File]::WriteAllText(
        $temporaryPath,
        ($result | ConvertTo-Json -Depth 8),
        (New-Object Text.UTF8Encoding($false)))
    if (Test-Path -LiteralPath $resolvedResultPath -PathType Leaf) {
        [IO.File]::Replace($temporaryPath, $resolvedResultPath, $null, $true)
    }
    else {
        Move-Item -LiteralPath $temporaryPath -Destination $resolvedResultPath
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}
