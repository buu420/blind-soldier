[CmdletBinding()]
param(
    [string] $GameRoot,
    [string] $SteamRoot,
    [string] $ReloadedRoot,
    [string] $SeventhHeavenRoot,
    [Parameter(Mandatory=$true)] [string] $ResultPath,
    [Parameter(DontShow=$true)] [string] $ModulePath
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

function Resolve-OptionalDirectory {
    param([string] $ExplicitPath, [string] $EnvironmentName, [string] $DefaultPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        return [IO.Path]::GetFullPath($ExplicitPath)
    }
    $environmentValue = [Environment]::GetEnvironmentVariable($EnvironmentName)
    if (-not [string]::IsNullOrWhiteSpace($environmentValue)) {
        return [IO.Path]::GetFullPath($environmentValue)
    }
    return [IO.Path]::GetFullPath($DefaultPath)
}

$installation = $null
$gameResult = $null
try {
    $resolveArguments = @{}
    if (-not [string]::IsNullOrWhiteSpace($GameRoot)) { $resolveArguments.GameRoot = $GameRoot }
    if (-not [string]::IsNullOrWhiteSpace($SteamRoot)) { $resolveArguments.SteamRoot = $SteamRoot }
    $installation = Resolve-Ff7Installation @resolveArguments
    if ($installation.Version -eq 'Steam2026') {
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
    Add-Dependency -Id 'game' -Name 'Final Fantasy VII' -Severity required -Satisfied $true `
        -Message "Supported $($installation.Version) installation detected." -Path ([string]$installation.GameRoot)
}
catch {
    Add-Dependency -Id 'game' -Name 'Final Fantasy VII' -Severity blocking -Satisfied $false `
        -Message $_.Exception.Message -Path $GameRoot
}

$defaultReloadedRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AccessXI\external\Reloaded-II'
$resolvedReloadedRoot = Resolve-OptionalDirectory -ExplicitPath $ReloadedRoot `
    -EnvironmentName 'RELOADED_II_ROOT' -DefaultPath $defaultReloadedRoot
$reloadedAvailable = Test-Path -LiteralPath $resolvedReloadedRoot -PathType Container
Add-Dependency -Id 'reloaded' -Name 'Reloaded-II' `
    -Severity $(if ($reloadedAvailable) { 'required' } else { 'blocking' }) `
    -Satisfied $reloadedAvailable `
    -Message $(if ($reloadedAvailable) { 'Reloaded-II folder found.' } else { 'Reloaded-II folder was not found. Install Reloaded-II or choose its folder.' }) `
    -Path $resolvedReloadedRoot

$loaderFailures = New-Object 'System.Collections.Generic.List[string]'
if ($reloadedAvailable) {
    foreach ($loader in @(
        [pscustomobject]@{ Label = 'x86 ASI loader'; Relative = '_asi_extract\ASILoader32.dll'; Machine = 0x014C },
        [pscustomobject]@{ Label = 'x86 bootstrapper'; Relative = 'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll'; Machine = 0x014C },
        [pscustomobject]@{ Label = 'x64 ASI loader'; Relative = '_asi_extract\ASILoader64.dll'; Machine = 0x8664 },
        [pscustomobject]@{ Label = 'x64 bootstrapper'; Relative = 'Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll'; Machine = 0x8664 }
    )) {
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
Add-Dependency -Id 'reloaded-loaders' -Name 'Reloaded-II x86 and x64 loaders' `
    -Severity $(if ($loadersReady) { 'required' } else { 'blocking' }) -Satisfied $loadersReady `
    -Message $(if ($loadersReady) { 'Required x86 and x64 loader files are valid.' } else { $loaderFailures -join '; ' }) `
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
    [pscustomobject]@{ Label = 'x86 shared hooks'; Relative = 'x86\Reloaded.Hooks.ReloadedII.dll'; Machine = 0x014C },
    [pscustomobject]@{ Label = 'x64 shared hooks'; Relative = 'x64\Reloaded.Hooks.ReloadedII.dll'; Machine = 0x8664 }
)) {
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
    -Message $(if ($sharedHooksReady) { 'Reloaded-II Shared Hooks x86 and x64 files are ready.' } else { $sharedHooksFailures -join '; ' }) `
    -Path $sharedHooksRoot

$defaultSeventhHeavenRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Tools\7thHeaven'
$resolvedSeventhHeavenRoot = Resolve-OptionalDirectory -ExplicitPath $SeventhHeavenRoot `
    -EnvironmentName 'SEVENTH_HEAVEN_ROOT' -DefaultPath $defaultSeventhHeavenRoot
$seventhHeavenAvailable = Test-Path -LiteralPath $resolvedSeventhHeavenRoot -PathType Container
Add-Dependency -Id 'seventh-heaven' -Name '7th Heaven' -Severity optional -Satisfied $seventhHeavenAvailable `
    -Message $(if ($seventhHeavenAvailable) { '7th Heaven detected for the legacy x86 path.' } else { 'Not found. This is optional.' }) `
    -Path $resolvedSeventhHeavenRoot

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
    -Message $(if ($ffnxAvailable) { 'FFNx detected for the legacy x86 path.' } else { 'Not found. The deployment path can add a verified FFNx runtime when required.' }) `
    -Path $ffnxPath

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
