[CmdletBinding()]
param(
    [string] $GhidraRoot,
    [string] $X86BrokerPath,
    [string] $X64BrokerPath,
    [string] $ProxyPath,
    [string[]] $HostPaths = @(),
    [string] $OutputDirectory,

    [Parameter(DontShow=$true)]
    [scriptblock] $AnalysisInvoker
)

$ErrorActionPreference = 'Stop'
$toolsRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $toolsRoot
$pinnedDigest = 'B62E81A0390618466C019C60D8C2F796CED2509C4C1AEA4A37644A77272CF99D'
$scriptDirectory = Join-Path $repoRoot 'analysis\ghidra'
$exportManifestPath = Join-Path $repoRoot `
    'analysis\native-bootstrap\winmm-exports-10.0.26100.8737.json'

if ([string]::IsNullOrWhiteSpace($X86BrokerPath)) {
    $X86BrokerPath = Join-Path $repoRoot `
        'native\BlindSoldier.Bootstrap\bin\Release\Win32\Blind-Soldier-Bootstrap-x86.exe'
}
if ([string]::IsNullOrWhiteSpace($X64BrokerPath)) {
    $X64BrokerPath = Join-Path $repoRoot `
        'native\BlindSoldier.Bootstrap\bin\Release\x64\Blind-Soldier-Bootstrap-x64.exe'
}
if ([string]::IsNullOrWhiteSpace($ProxyPath)) {
    $ProxyPath = Join-Path $repoRoot `
        'native\BlindSoldier.WinMMProxy\bin\Release\Win32\winmm.dll'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts\ghidra'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

function Get-VerifiedPeMachine {
    param([Parameter(Mandatory=$true)] [string] $Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 0x86 -or $bytes[0] -ne 0x4D -or
        $bytes[1] -ne 0x5A) {
        throw "Ghidra input is not a PE program: $Path"
    }
    $offset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($offset -lt 0x40 -or $offset + 6 -gt $bytes.Length -or
        [BitConverter]::ToUInt32($bytes, $offset) -ne 0x00004550) {
        throw "Ghidra input has an invalid PE header: $Path"
    }
    return [BitConverter]::ToUInt16($bytes, $offset + 4)
}

function Assert-GhidraInstallation {
    param([Parameter(Mandatory=$true)] [string] $Root)
    $resolved = [IO.Path]::GetFullPath($Root)
    $headless = Join-Path $resolved 'support\analyzeHeadless.bat'
    $marker = Join-Path $resolved '.blind-soldier-ghidra.json'
    if (-not (Test-Path -LiteralPath $headless -PathType Leaf)) {
        throw "Pinned Ghidra analyzeHeadless is unavailable: $headless"
    }
    if (-not (Test-Path -LiteralPath $marker -PathType Leaf)) {
        throw "Pinned Ghidra evidence marker is unavailable: $marker"
    }
    try { $metadata = [IO.File]::ReadAllText($marker) | ConvertFrom-Json }
    catch { throw "Pinned Ghidra evidence marker is invalid: $marker" }
    if ([int]$metadata.schemaVersion -ne 1 -or
        -not ([string]$metadata.archiveSha256).Equals($pinnedDigest,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Pinned Ghidra evidence marker has the wrong archive SHA-256: $marker"
    }
    return [pscustomobject]@{
        Root = $resolved
        AnalyzeHeadless = $headless
        ArchiveSha256 = $pinnedDigest
    }
}

function Test-ExactExports {
    param([object[]] $Actual, [object[]] $Expected)
    $actualValues = @($Actual)
    $expectedValues = @($Expected)
    if ($actualValues.Count -ne $expectedValues.Count) { return $false }
    for ($index = 0; $index -lt $expectedValues.Count; ++$index) {
        $a = $actualValues[$index]
        $e = $expectedValues[$index]
        if ([int]$a.ordinal -ne [int]$e.ordinal -or
            [bool]$a.noname -ne [bool]$e.noname -or
            [string]$a.name -cne [string]$e.name) {
            return $false
        }
    }
    return $true
}

function Assert-EvidenceRecord {
    param(
        [Parameter(Mandatory=$true)] [psobject] $Request,
        [Parameter(Mandatory=$true)] [psobject] $Evidence,
        [Parameter(Mandatory=$true)] [object[]] $ExpectedExports
    )
    if ([int]$Evidence.schemaVersion -ne 1 -or
        [string]$Evidence.marker -cne 'BLIND_SOLDIER_GHIDRA_EVIDENCE') {
        throw "Ghidra report for $($Request.Kind) is missing the required evidence marker. Log: $($Request.LogPath)"
    }
    if ([string]$Evidence.kind -cne $Request.Kind) {
        throw "Ghidra report kind mismatch for $($Request.ProgramPath). Log: $($Request.LogPath)"
    }
    $actualHash = (Get-FileHash -LiteralPath $Request.ProgramPath `
        -Algorithm SHA256).Hash
    if (-not ([string]$Evidence.sha256).Equals($actualHash,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Ghidra report SHA-256 mismatch for $($Request.ProgramPath). Log: $($Request.LogPath)"
    }
    if ([int]$Evidence.machine -ne [int]$Request.ExpectedMachine) {
        throw "Ghidra report PE architecture mismatch for $($Request.ProgramPath). Log: $($Request.LogPath)"
    }
    $forbidden = @($Evidence.forbidden | ForEach-Object { [string]$_ })
    if ($forbidden.Count -ne 0) {
        throw "Ghidra found forbidden native evidence in $($Request.ProgramPath): $($forbidden -join ', '). Log: $($Request.LogPath)"
    }
    foreach ($name in @($Request.RequiredEvidence)) {
        $property = $Evidence.required.PSObject.Properties[$name]
        if ($null -eq $property -or $property.Value -ne $true) {
            throw "Ghidra required evidence '$name' is missing from $($Request.ProgramPath). Log: $($Request.LogPath)"
        }
    }
    if ($Request.Kind -ceq 'winmm-proxy' -and
        -not (Test-ExactExports -Actual @($Evidence.exports) `
            -Expected $ExpectedExports)) {
        throw "Ghidra WinMM export table is incomplete or differs from the locked manifest. Log: $($Request.LogPath)"
    }
}

if ([string]::IsNullOrWhiteSpace($GhidraRoot)) {
    $installed = & (Join-Path $toolsRoot 'Install-PinnedGhidra.ps1')
    $GhidraRoot = [string]$installed.GhidraRoot
}
$ghidra = Assert-GhidraInstallation -Root $GhidraRoot

foreach ($script in @(
    'BlindSoldierNativeEvidence.java',
    'BlindSoldierBootstrapEvidence.java',
    'BlindSoldierWinmmEvidence.java'
)) {
    $path = Join-Path $scriptDirectory $script
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Ghidra script is unavailable: $path"
    }
}
if (-not (Test-Path -LiteralPath $exportManifestPath -PathType Leaf)) {
    throw "Locked WinMM export manifest is unavailable: $exportManifestPath"
}
$expectedExports = @(([IO.File]::ReadAllText($exportManifestPath) |
    ConvertFrom-Json).exports | Select-Object ordinal,name,noname)
if ($expectedExports.Count -ne 193) {
    throw "Locked WinMM export manifest does not contain 193 exports: $exportManifestPath"
}

$specifications = New-Object 'System.Collections.Generic.List[object]'
$specifications.Add([pscustomobject]@{
    Kind = 'bootstrap-x86'; ProgramPath = $X86BrokerPath
    ExpectedMachine = 0x014C
    ScriptName = 'BlindSoldierBootstrapEvidence.java'
    RequiredEvidence = @(
        'OpenProcess','QueryFullProcessImageNameW','VirtualAllocEx',
        'WriteProcessMemory','CreateRemoteThread','LoadLibraryW',
        'CreateMutexW','MoveFileExW','SetEvent','PrivateRuntime')
})
$specifications.Add([pscustomobject]@{
    Kind = 'bootstrap-x64'; ProgramPath = $X64BrokerPath
    ExpectedMachine = 0x8664
    ScriptName = 'BlindSoldierBootstrapEvidence.java'
    RequiredEvidence = @(
        'CreateProcessW','VirtualAllocEx','WriteProcessMemory',
        'CreateRemoteThread','LoadLibraryW','CreateMutexW','MoveFileExW',
        'ResumeThread','PrivateRuntime')
})
$specifications.Add([pscustomobject]@{
    Kind = 'winmm-proxy'; ProgramPath = $ProxyPath
    ExpectedMachine = 0x014C
    ScriptName = 'BlindSoldierWinmmEvidence.java'
    RequiredEvidence = @(
        'GetSystemWow64DirectoryW','AbsoluteSystemWinmm','CreateThread',
        'WaitForSingleObject','WaitForMultipleObjects','MessageBoxW',
        'TerminateProcess','HostGuards','BoundedParentSearch')
})
foreach ($hostPath in @($HostPaths)) {
    if ([string]::IsNullOrWhiteSpace($hostPath)) { continue }
    $machine = Get-VerifiedPeMachine -Path $hostPath
    if ($machine -ne 0x014C -and $machine -ne 0x8664) {
        throw "Supported host has an unsupported architecture: $hostPath"
    }
    $specifications.Add([pscustomobject]@{
        Kind = if ($machine -eq 0x014C) { 'host-x86' } else { 'host-x64' }
        ProgramPath = $hostPath
        ExpectedMachine = $machine
        ScriptName = 'BlindSoldierNativeEvidence.java'
        RequiredEvidence = @('HostIdentity')
    })
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$records = New-Object 'System.Collections.Generic.List[object]'
foreach ($specification in $specifications) {
    if (-not (Test-Path -LiteralPath $specification.ProgramPath `
            -PathType Leaf)) {
        throw "Ghidra program is unavailable: $($specification.ProgramPath)"
    }
    $specification.ProgramPath = [IO.Path]::GetFullPath(
        $specification.ProgramPath)
    $machine = Get-VerifiedPeMachine -Path $specification.ProgramPath
    if ($machine -ne [int]$specification.ExpectedMachine) {
        throw "Ghidra program architecture mismatch: $($specification.ProgramPath)"
    }
    $safeKind = $specification.Kind -replace '[^A-Za-z0-9_.-]', '_'
    $reportPath = Join-Path $OutputDirectory "$safeKind.json"
    $logPath = Join-Path $OutputDirectory "$safeKind.log"
    Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue
    $request = [pscustomobject]@{
        Kind = [string]$specification.Kind
        ProgramPath = [string]$specification.ProgramPath
        ExpectedMachine = [int]$specification.ExpectedMachine
        RequiredEvidence = @($specification.RequiredEvidence)
        ScriptName = [string]$specification.ScriptName
        ReportPath = $reportPath
        LogPath = $logPath
        AnalyzeHeadlessPath = [string]$ghidra.AnalyzeHeadless
        GhidraRoot = [string]$ghidra.Root
    }
    if ($null -ne $AnalysisInvoker) {
        $invocation = & $AnalysisInvoker $request
        if ($null -eq $invocation -or
            $null -eq $invocation.PSObject.Properties['ExitCode']) {
            throw "AnalysisInvoker returned no exit code for $($request.Kind)."
        }
        $exitCode = [int]$invocation.ExitCode
        $analysisOutput = @($invocation.Output | ForEach-Object { [string]$_ })
    }
    else {
        $projectRoot = Join-Path ([IO.Path]::GetTempPath()) `
            ('blind-soldier-ghidra-' + [Guid]::NewGuid().ToString('N'))
        try {
            New-Item -ItemType Directory -Path $projectRoot | Out-Null
            $projectDirectory = Join-Path $projectRoot 'project'
            New-Item -ItemType Directory -Path $projectDirectory | Out-Null
            $extension = [IO.Path]::GetExtension($request.ProgramPath)
            if ([string]::IsNullOrWhiteSpace($extension)) {
                $extension = '.bin'
            }
            $analysisProgram = Join-Path $projectRoot ('input' + $extension)
            Copy-Item -LiteralPath $request.ProgramPath `
                -Destination $analysisProgram
            $arguments = @(
                $projectDirectory, 'BlindSoldierEvidence',
                '-import', $analysisProgram,
                '-overwrite',
                '-analysisTimeoutPerFile', '300',
                '-scriptPath', $scriptDirectory,
                '-postScript', $request.ScriptName,
                $request.ReportPath, $request.Kind,
                ([string]$request.ExpectedMachine), $exportManifestPath,
                '-deleteProject')
            $analysisOutput = @(& $request.AnalyzeHeadlessPath @arguments 2>&1 |
                ForEach-Object { [string]$_ })
            $exitCode = $LASTEXITCODE
        }
        finally {
            if ((Test-Path -LiteralPath $projectRoot -PathType Container) -and
                ([string](Split-Path -Leaf $projectRoot)).StartsWith(
                    'blind-soldier-ghidra-', [StringComparison]::Ordinal) -and
                ([IO.Path]::GetFullPath($projectRoot)).StartsWith(
                    [IO.Path]::GetFullPath([IO.Path]::GetTempPath()),
                    [StringComparison]::OrdinalIgnoreCase)) {
                Remove-Item -LiteralPath $projectRoot -Recurse -Force
            }
        }
    }
    [IO.File]::WriteAllLines($logPath, @($analysisOutput),
        [Text.UTF8Encoding]::new($false))
    if ($exitCode -ne 0) {
        throw "Ghidra analysis failed for $($request.ProgramPath) with exit code $exitCode. Log: $logPath"
    }
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw "Ghidra analysis produced no evidence report for $($request.ProgramPath). Log: $logPath"
    }
    try { $evidence = [IO.File]::ReadAllText($reportPath) | ConvertFrom-Json }
    catch { throw "Ghidra evidence report is invalid JSON: $reportPath. Log: $logPath" }
    Assert-EvidenceRecord -Request $request -Evidence $evidence `
        -ExpectedExports $expectedExports
    $evidence.program = $request.ProgramPath
    [IO.File]::WriteAllText($reportPath,
        ($evidence | ConvertTo-Json -Depth 16),
        [Text.UTF8Encoding]::new($false))
    $records.Add($evidence)
}

$summaryPath = Join-Path $OutputDirectory 'summary.json'
$summary = [ordered]@{
    schemaVersion = 1
    marker = 'BLIND_SOLDIER_GHIDRA_VERIFICATION'
    ghidraArchiveSha256 = $pinnedDigest
    verificationSucceeded = $true
    evidence = @($records.ToArray())
}
[IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 16),
    [Text.UTF8Encoding]::new($false))

[pscustomobject]@{
    VerificationSucceeded = $true
    GhidraRoot = [string]$ghidra.Root
    GhidraArchiveSha256 = $pinnedDigest
    SummaryPath = $summaryPath
    Evidence = $records.ToArray()
}
