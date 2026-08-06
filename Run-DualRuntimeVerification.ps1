[CmdletBinding()]
param(
    [ValidateSet('Research', 'Release')]
    [string] $Mode = 'Research',
    [string] $ParityMatrixPath,
    [string] $GameRuntimePath,
    [switch] $RequireGameDataIntegration,

    # Retained for command-line compatibility with older local automation.
    [switch] $SkipProtectedSeventhHeaven,
    [switch] $SkipSecondPesterRepeat,

    [Parameter(DontShow=$true)]
    [scriptblock] $CommandInvoker,
    [Parameter(DontShow=$true)]
    [string] $TempParent = [IO.Path]::GetTempPath(),
    [Parameter(DontShow=$true)]
    [string] $LogDirectory
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($ParityMatrixPath)) {
    $ParityMatrixPath = Join-Path $scriptRoot `
        'analysis\dual_runtime\parity-matrix.json'
}
if ([string]::IsNullOrWhiteSpace($LogDirectory)) {
    $LogDirectory = Join-Path $scriptRoot 'artifacts\verification'
}
$LogDirectory = [IO.Path]::GetFullPath($LogDirectory)
New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null

Import-Module (Join-Path $scriptRoot 'FF7SteamInstall.psm1') -Force
$parityGate = if ($Mode -ceq 'Release') {
    Assert-Ff7NativeParityReleaseGate -ParityMatrixPath $ParityMatrixPath
}
else {
    Assert-Ff7NativeParityReleaseGate -ParityMatrixPath $ParityMatrixPath `
        -AllowResearch
}

if ([string]::IsNullOrWhiteSpace($GameRuntimePath)) {
    $GameRuntimePath = [Environment]::GetEnvironmentVariable(
        'FF7_ACCESSIBILITY_RUNTIME')
}
$hasGameDataRuntime = -not [string]::IsNullOrWhiteSpace($GameRuntimePath)
if ($hasGameDataRuntime) {
    $GameRuntimePath = [IO.Path]::GetFullPath($GameRuntimePath)
    if (-not (Test-Path -LiteralPath (Join-Path $GameRuntimePath `
                'ff7_en.exe') -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $GameRuntimePath 'data') `
            -PathType Container)) {
        throw "GameRuntimePath does not identify a licensed legacy FFVII runtime: $GameRuntimePath"
    }
}
elseif ($RequireGameDataIntegration) {
    throw 'A licensed legacy FFVII GameRuntimePath is required for the full game-data integration suite.'
}

function New-VerificationCommand {
    param(
        [Parameter(Mandatory=$true)] [string] $Name,
        [Parameter(Mandatory=$true)] [string] $FilePath,
        [Parameter(Mandatory=$true)] [object[]] $Arguments,
        [Parameter(Mandatory=$true)] [string] $WorkingDirectory,
        [hashtable] $Environment = @{}
    )
    $safeName = $Name -replace '[^A-Za-z0-9_.-]', '_'
    [pscustomobject]@{
        Name = $Name
        FilePath = $FilePath
        Arguments = $Arguments
        WorkingDirectory = $WorkingDirectory
        LogPath = Join-Path $LogDirectory ($safeName + '.log')
        Environment = $Environment
    }
}

function Invoke-VerificationCommand {
    param(
        [Parameter(Mandatory=$true)] [psobject] $Command,
        [scriptblock] $Invoker
    )
    Write-Host "[$($Command.Name)] $($Command.FilePath) $($Command.Arguments -join ' ')"
    $output = @()
    $priorEnvironment = @{}
    try {
        foreach ($name in @($Command.Environment.Keys)) {
            $priorEnvironment[$name] = [Environment]::GetEnvironmentVariable(
                $name, [EnvironmentVariableTarget]::Process)
            [Environment]::SetEnvironmentVariable($name,
                [string]$Command.Environment[$name],
                [EnvironmentVariableTarget]::Process)
        }
        if ($null -ne $Invoker) {
            $invocation = & $Invoker $Command
            if ($null -eq $invocation -or
                $null -eq $invocation.PSObject.Properties['ExitCode']) {
                throw "CommandInvoker returned no integer exit code."
            }
            $exitCode = [int]$invocation.ExitCode
            $output = @($invocation.Output | ForEach-Object { [string]$_ })
        }
        else {
            Push-Location -LiteralPath $Command.WorkingDirectory
            try {
                $priorPreference = $ErrorActionPreference
                try {
                    $ErrorActionPreference = 'Continue'
                    $output = @(& $Command.FilePath @($Command.Arguments) 2>&1 |
                        ForEach-Object { [string]$_ })
                    $exitCode = $LASTEXITCODE
                }
                finally { $ErrorActionPreference = $priorPreference }
            }
            finally { Pop-Location }
        }
    }
    catch {
        $output += [string]$_.Exception.Message
        [IO.File]::WriteAllLines($Command.LogPath, $output,
            [Text.UTF8Encoding]::new($false))
        throw "Verification step '$($Command.Name)' failed before returning an exit code. Log: $($Command.LogPath). $($_.Exception.Message)"
    }
    finally {
        foreach ($name in @($Command.Environment.Keys)) {
            [Environment]::SetEnvironmentVariable($name,
                $priorEnvironment[$name],
                [EnvironmentVariableTarget]::Process)
        }
    }
    [IO.File]::WriteAllLines($Command.LogPath, $output,
        [Text.UTF8Encoding]::new($false))
    foreach ($line in $output) { Write-Host "[$($Command.Name)] $line" }
    if ($exitCode -ne 0) {
        throw "Verification step '$($Command.Name)' failed with exit code $exitCode. Log: $($Command.LogPath)"
    }
    [pscustomobject]@{
        Name = $Command.Name
        ExitCode = $exitCode
        LogPath = $Command.LogPath
        Output = $output
    }
}

function New-PesterCommand {
    param(
        [Parameter(Mandatory=$true)] [string] $Name,
        [Parameter(Mandatory=$true)] [string] $Path,
        [string] $TestName
    )
    $escaped = ([IO.Path]::GetFullPath($Path)).Replace("'", "''")
    $command = "Import-Module Pester -RequiredVersion 4.10.1 -Force; " +
        "Invoke-Pester -Script '$escaped'"
    if (-not [string]::IsNullOrWhiteSpace($TestName)) {
        $command += " -TestName '$($TestName.Replace("'", "''"))'"
    }
    $command += ' -EnableExit'
    New-VerificationCommand -Name $Name -FilePath 'powershell.exe' `
        -Arguments @('-NoProfile','-NonInteractive','-ExecutionPolicy',
            'Bypass','-Command',$command) -WorkingDirectory $scriptRoot
}

if (-not (Test-Path -LiteralPath $TempParent -PathType Container)) {
    New-Item -ItemType Directory -Path $TempParent -Force | Out-Null
}
$verificationRoot = Join-Path ([IO.Path]::GetFullPath($TempParent)) `
    ('blind-soldier-release-gate-' + [Guid]::NewGuid().ToString('N'))
$portableArchive = Join-Path $verificationRoot 'Blind-Soldier-Portable.zip'
$commands = New-Object 'System.Collections.Generic.List[object]'

$commands.Add((New-VerificationCommand -Name 'Shared.Tests' `
    -FilePath 'dotnet' -Arguments @('run','--project',
        (Join-Path $scriptRoot `
            'Ff7.Accessibility.Shared.Tests\Ff7.Accessibility.Shared.Tests.csproj'),
        '-c','Release') -WorkingDirectory $scriptRoot))
$reloadedProject = Join-Path $scriptRoot `
    'Ff7.Accessibility.Reloaded.Tests\Ff7.Accessibility.Reloaded.Tests.csproj'
if ($hasGameDataRuntime) {
    $commands.Add((New-VerificationCommand -Name 'Reloaded.Tests' `
        -FilePath 'dotnet' -Arguments @('run','--project',$reloadedProject,
            '-c','Release') -WorkingDirectory $scriptRoot `
        -Environment @{
            FF7_ACCESSIBILITY_RUNTIME = $GameRuntimePath
        }))
}
else {
    $escapedProject = $reloadedProject.Replace("'", "''")
    $portableModes = @(
        '--runtime-lease-only','--host-validation-only')
    $portableCommand = "foreach (`$mode in @('$($portableModes -join "','")')) { " +
        "& dotnet run --project '$escapedProject' -c Release -- `$mode; " +
        "if (`$LASTEXITCODE -ne 0) { exit `$LASTEXITCODE } }"
    $commands.Add((New-VerificationCommand -Name 'Reloaded.Tests' `
        -FilePath 'powershell.exe' -Arguments @('-NoProfile',
            '-NonInteractive','-ExecutionPolicy','Bypass','-Command',
            $portableCommand) -WorkingDirectory $scriptRoot))
}
$commands.Add((New-VerificationCommand -Name 'Steam2026X64.Tests' `
    -FilePath 'dotnet' -Arguments @('run','--project',
        (Join-Path $scriptRoot `
            'Ff7.Accessibility.Steam2026X64.Tests\Ff7.Accessibility.Steam2026X64.Tests.csproj'),
        '-c','Release','--','--module-tests-only') `
    -WorkingDirectory $scriptRoot))
foreach ($managed in @(
    @('Parity.Tests', 'Ff7.Accessibility.Parity.Tests\Ff7.Accessibility.Parity.Tests.csproj'),
    @('AccessibleLauncher.Tests', 'launcher\Ff7.Launcher.Accessible.Tests\FFVII_LAUNCHER.Accessibility.Tests.csproj')
)) {
    $commands.Add((New-VerificationCommand -Name $managed[0] `
        -FilePath 'dotnet' -Arguments @('run','--project',
            (Join-Path $scriptRoot $managed[1]),'-c','Release') `
        -WorkingDirectory $scriptRoot))
}
$commands.Add((New-PesterCommand -Name 'VerificationGate.Tests' `
    -Path (Join-Path $scriptRoot 'Run-DualRuntimeVerification.Tests.ps1')))
$commands.Add((New-PesterCommand -Name 'AccessibleLauncherBundle.Tests' `
    -Path (Join-Path $scriptRoot 'Build-AccessibleLauncherBundle.Tests.ps1')))
$commands.Add((New-PesterCommand -Name 'NativeHost.Tests' `
    -Path (Join-Path $scriptRoot 'native\BlindSoldier.Native.Tests.ps1')))
$bootstrapTestPaths = @(
    (Join-Path $scriptRoot `
        'native\BlindSoldier.Bootstrap.Tests\bin\Release\Win32\BlindSoldier.Bootstrap.Tests.exe'),
    (Join-Path $scriptRoot `
        'native\BlindSoldier.Bootstrap.Tests\bin\Release\x64\BlindSoldier.Bootstrap.Tests.exe'))
$escapedBootstrapTests = @($bootstrapTestPaths | ForEach-Object {
    ([IO.Path]::GetFullPath($_)).Replace("'", "''")
})
$bootstrapCommand = "foreach (`$path in @('$($escapedBootstrapTests -join "','")')) { " +
    "if (-not (Test-Path -LiteralPath `$path -PathType Leaf)) { " +
    "throw ('Built broker test is unavailable: ' + `$path) }; " +
    "& `$path; if (`$LASTEXITCODE -ne 0) { exit `$LASTEXITCODE } }"
$commands.Add((New-VerificationCommand -Name 'Bootstrap.Tests x86/x64' `
    -FilePath 'powershell.exe' -Arguments @('-NoProfile','-NonInteractive',
        '-ExecutionPolicy','Bypass','-Command',$bootstrapCommand) `
    -WorkingDirectory $scriptRoot))
$commands.Add((New-PesterCommand -Name 'WinMMProxy.Tests' `
    -Path (Join-Path $scriptRoot 'native\BlindSoldier.WinMMProxy.Tests.ps1')))
$commands.Add((New-PesterCommand -Name 'PortableDotNetRuntime.Tests' `
    -Path (Join-Path $scriptRoot 'PortableDotNetRuntime.Tests.ps1')))
$commands.Add((New-PesterCommand -Name 'PortablePackage.Tests' `
    -Path (Join-Path $scriptRoot 'Build-BlindSoldierPortablePackage.Tests.ps1')))
$commands.Add((New-VerificationCommand -Name 'PortablePackage.Build' `
    -FilePath 'powershell.exe' -Arguments @(
        '-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',
        (Join-Path $scriptRoot 'Build-BlindSoldierPortablePackage.ps1'),
        '-OutputPath',$portableArchive,'-Version','0.1.5') `
    -WorkingDirectory $scriptRoot))
$commands.Add((New-VerificationCommand -Name 'PortablePackage.Verify' `
    -FilePath 'powershell.exe' -Arguments @(
        '-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',
        (Join-Path $scriptRoot 'Verify-BlindSoldierPortablePackage.ps1'),
        '-ArchivePath',$portableArchive,'-ExpectedVersion','0.1.5') `
    -WorkingDirectory $scriptRoot))
$commands.Add((New-VerificationCommand -Name 'Ghidra.NativeEvidence' `
    -FilePath 'powershell.exe' -Arguments @(
        '-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',
        (Join-Path $scriptRoot `
            'tools\Invoke-BlindSoldierGhidraVerification.ps1')) `
    -WorkingDirectory $scriptRoot))

$results = New-Object 'System.Collections.Generic.List[object]'
$archiveSha256 = $null
try {
    New-Item -ItemType Directory -Path $verificationRoot | Out-Null
    foreach ($command in $commands) {
        $results.Add((Invoke-VerificationCommand -Command $command `
            -Invoker $CommandInvoker))
    }
    if ($null -eq $CommandInvoker) {
        if (-not (Test-Path -LiteralPath $portableArchive -PathType Leaf)) {
            throw "Portable release gate did not produce its archive: $portableArchive"
        }
        $archiveSha256 = (Get-FileHash -LiteralPath $portableArchive `
            -Algorithm SHA256).Hash
    }
}
finally {
    if (Test-Path -LiteralPath $verificationRoot -PathType Container) {
        $resolved = [IO.Path]::GetFullPath($verificationRoot)
        $tempPrefix = [IO.Path]::GetFullPath($TempParent).TrimEnd('\') + '\'
        if ($resolved.StartsWith($tempPrefix,
                [StringComparison]::OrdinalIgnoreCase) -and
            (Split-Path -Leaf $resolved).StartsWith(
                'blind-soldier-release-gate-',
                [StringComparison]::Ordinal)) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}

[pscustomobject]@{
    Mode = $Mode
    VerificationSucceeded = $true
    ReleaseReady = [bool]$parityGate.IsReleaseReady
    Steps = $results.ToArray()
    PortableArchiveSha256 = $archiveSha256
    GameDataIntegrationRan = $hasGameDataRuntime
    PackageStagingCleaned = -not (Test-Path -LiteralPath $verificationRoot)
    LogDirectory = $LogDirectory
}
