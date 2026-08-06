[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $scriptRoot 'launcher\Ff7.Launcher.Accessible\FFVII_LAUNCHER.csproj'
$templatePath = Join-Path $scriptRoot 'launcher\launcher-bundle.template.json'

function Get-PeMachine {
    param([Parameter(Mandatory = $true)][string] $Path)

    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        if ($stream.Length -lt 0x86) {
            throw "PE image is too small: $Path"
        }

        $reader = New-Object IO.BinaryReader($stream)
        try {
            if ($reader.ReadUInt16() -ne 0x5A4D) {
                throw "PE image has no MZ signature: $Path"
            }

            $stream.Position = 0x3C
            $peOffset = $reader.ReadUInt32()
            if ($peOffset -gt ($stream.Length - 6)) {
                throw "PE header is outside the file: $Path"
            }

            $stream.Position = $peOffset
            if ($reader.ReadUInt32() -ne 0x00004550) {
                throw "PE image has no PE signature: $Path"
            }

            return $reader.ReadUInt16()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-FileDescriptor {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $RelativePath
    )

    $item = Get-Item -LiteralPath $Path
    [ordered]@{
        path = $RelativePath.Replace('\', '/')
        length = [long]$item.Length
        sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
    }
}

function Reset-BundleDirectory {
    param([Parameter(Mandatory = $true)][string] $Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $rootPath = [IO.Path]::GetPathRoot($fullPath)
    if ([string]::IsNullOrWhiteSpace($fullPath) -or $fullPath -eq $rootPath -or $fullPath -eq $scriptRoot) {
        throw "Refusing to replace unsafe launcher bundle directory: $fullPath"
    }

    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
    return $fullPath
}

if (-not (Test-Path -LiteralPath $project)) {
    throw "Accessible launcher project is missing: $project"
}
if (-not (Test-Path -LiteralPath $templatePath)) {
    throw "Launcher bundle template is missing: $templatePath"
}

$template = Get-Content -LiteralPath $templatePath -Raw | ConvertFrom-Json
if ($template.schemaVersion -ne 2 -or
    $template.assemblyName -ne 'FFVII_LAUNCHER' -or
    $template.assemblyVersion -ne '2.0.0.0') {
    throw "Launcher bundle template has an unsupported identity: $templatePath"
}

& dotnet build $project -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Accessible launcher build failed with exit code $LASTEXITCODE."
}

$buildOutput = Join-Path (Split-Path -Parent $project) "bin\$Configuration\net48"
$launcherSource = Join-Path $buildOutput 'FFVII_LAUNCHER.exe'
$configSource = Join-Path $buildOutput 'FFVII_LAUNCHER.exe.config'
$prismSource = Join-Path $buildOutput 'launcher_accessibility\native\x86\FFVII_LAUNCHER.prism.x86.dll'
foreach ($required in @($launcherSource, $configSource, $prismSource)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Launcher build did not emit required file: $required"
    }
}

foreach ($pePath in @($launcherSource, $prismSource)) {
    $machine = Get-PeMachine -Path $pePath
    if ($machine -ne 0x014C) {
        throw ('Launcher bundle file is not x86 (machine 0x{0:X4}): {1}' -f $machine, $pePath)
    }
}

$assemblyName = [Reflection.AssemblyName]::GetAssemblyName($launcherSource)
if ($assemblyName.Name -ne $template.assemblyName -or
    $assemblyName.Version.ToString() -ne $template.assemblyVersion) {
    throw "Launcher managed identity is '$($assemblyName.FullName)', expected $($template.assemblyName), Version=$($template.assemblyVersion)."
}

$output = Reset-BundleDirectory -Path $OutputPath
$nativeOutput = Join-Path $output 'native\x86'
New-Item -ItemType Directory -Path $nativeOutput -Force | Out-Null

$launcherTarget = Join-Path $output 'FFVII_LAUNCHER.exe'
$configTarget = Join-Path $output 'FFVII_LAUNCHER.exe.config'
$prismTarget = Join-Path $nativeOutput 'FFVII_LAUNCHER.prism.x86.dll'
Copy-Item -LiteralPath $launcherSource -Destination $launcherTarget
Copy-Item -LiteralPath $configSource -Destination $configTarget
Copy-Item -LiteralPath $prismSource -Destination $prismTarget

$manifest = [ordered]@{
    schemaVersion = 2
    stockLauncherSha256 = [string]$template.stockLauncherSha256
    launcher = Get-FileDescriptor -Path $launcherTarget -RelativePath 'FFVII_LAUNCHER.exe'
    config = Get-FileDescriptor -Path $configTarget -RelativePath 'FFVII_LAUNCHER.exe.config'
    prism = Get-FileDescriptor -Path $prismTarget -RelativePath 'native\x86\FFVII_LAUNCHER.prism.x86.dll'
    assemblyName = 'FFVII_LAUNCHER'
    assemblyVersion = '2.0.0.0'
}
$manifestPath = Join-Path $output 'launcher-bundle.json'
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8

Write-Output $output
