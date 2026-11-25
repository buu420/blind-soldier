[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    # Optional build output root, so a build leaves the source tree untouched.
    [string] $ArtifactsPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $scriptRoot 'launcher\Ff7.Launcher2013.Accessible\FF7_Launcher.csproj'
$utf8 = New-Object Text.UTF8Encoding($false)

function Get-PeMachine {
    param([Parameter(Mandatory = $true)][string] $Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "Not a PE image: $Path"
    }
    $offset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($offset -lt 64 -or $offset + 6 -gt $bytes.Length -or [BitConverter]::ToUInt32($bytes, $offset) -ne 0x00004550) {
        throw "Invalid PE header: $Path"
    }
    return [BitConverter]::ToUInt16($bytes, $offset + 4)
}

function Get-FileDescriptor {
    param([string] $Path, [string] $RelativePath)
    $item = Get-Item -LiteralPath $Path
    [ordered]@{
        path = $RelativePath
        length = [long]$item.Length
        sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
    }
}

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Accessible 2013 launcher project is missing: $project"
}

$output = [IO.Path]::GetFullPath($OutputPath)
$outputRoot = [IO.Path]::GetPathRoot($output)
if ($output -eq $outputRoot -or $output.TrimEnd('\') -eq $scriptRoot.TrimEnd('\')) {
    throw "Refusing to replace an unsafe bundle directory: $output"
}

$arguments = @('build', $project, '-c', $Configuration, '--nologo')
if (-not [string]::IsNullOrWhiteSpace($ArtifactsPath)) {
    $artifacts = [IO.Path]::GetFullPath($ArtifactsPath)
    $arguments += @('--artifacts-path', $artifacts)
    $buildOutput = Join-Path $artifacts ('bin\FF7_Launcher\' + $Configuration.ToLowerInvariant())
}
else {
    $buildOutput = Join-Path (Split-Path -Parent $project) "bin\$Configuration\net48"
}
& dotnet @arguments | Write-Verbose
if ($LASTEXITCODE -ne 0) {
    throw "Accessible 2013 launcher build failed with exit code $LASTEXITCODE."
}

$launcherSource = Join-Path $buildOutput 'FF7_Launcher.exe'
$configSource = Join-Path $buildOutput 'FF7_Launcher.exe.config'
foreach ($required in @($launcherSource, $configSource)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "The launcher build did not emit: $required"
    }
}
$machine = Get-PeMachine -Path $launcherSource
if ($machine -ne 0x014C) {
    throw ('The 2013 launcher is not x86 (machine 0x{0:X4}).' -f $machine)
}
$identity = [Reflection.AssemblyName]::GetAssemblyName($launcherSource)
if ($identity.Name -cne 'FF7_Launcher') {
    throw "Unexpected launcher identity: $($identity.FullName)."
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$launcherTarget = Join-Path $output 'FF7_Launcher.exe'
$configTarget = Join-Path $output 'FF7_Launcher.exe.config'
Copy-Item -LiteralPath $launcherSource -Destination $launcherTarget
Copy-Item -LiteralPath $configSource -Destination $configTarget

$manifest = [ordered]@{
    schemaVersion = 1
    assemblyName = 'FF7_Launcher'
    assemblyVersion = $identity.Version.ToString()
    launcher = Get-FileDescriptor -Path $launcherTarget -RelativePath 'FF7_Launcher.exe'
    config = Get-FileDescriptor -Path $configTarget -RelativePath 'FF7_Launcher.exe.config'
}
[IO.File]::WriteAllText((Join-Path $output 'launcher-2013-bundle.json'),
    (($manifest | ConvertTo-Json -Depth 4) + "`n"), $utf8)

Write-Output $output
