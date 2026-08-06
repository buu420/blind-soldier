$ErrorActionPreference = 'Stop'

$toolsRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $toolsRoot
$installerPath = Join-Path $toolsRoot 'Install-PinnedGhidra.ps1'
$verifierPath = Join-Path $toolsRoot 'Invoke-BlindSoldierGhidraVerification.ps1'
$exportManifestPath = Join-Path $repoRoot `
    'analysis\native-bootstrap\winmm-exports-10.0.26100.8737.json'
$pinnedDigest = 'B62E81A0390618466C019C60D8C2F796CED2509C4C1AEA4A37644A77272CF99D'

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw "ASSERTION FAILED: $Message" }
}

function Assert-ThrowsLike {
    param([scriptblock] $Action, [string] $Pattern, [string] $Name)
    try {
        & $Action
        throw "ASSERTION FAILED: $Name did not throw."
    }
    catch {
        if ($_.Exception.Message -like 'ASSERTION FAILED:*') { throw }
        if ($_.Exception.Message -notmatch $Pattern) {
            throw "ASSERTION FAILED: $Name threw '$($_.Exception.Message)', expected /$Pattern/."
        }
    }
}

function New-TestPe {
    param([string] $Path, [uint16] $Machine)
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force |
        Out-Null
    $bytes = New-Object byte[] 512
    $bytes[0] = 0x4D
    $bytes[1] = 0x5A
    [BitConverter]::GetBytes([int]0x80).CopyTo($bytes, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 0x80)
    [BitConverter]::GetBytes($Machine).CopyTo($bytes, 0x84)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function New-TestGhidraArchive {
    param([string] $Path)
    Add-Type -AssemblyName System.IO.Compression
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew)
    try {
        $zip = [IO.Compression.ZipArchive]::new(
            $stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $entry = $zip.CreateEntry(
                'ghidra_12.1.2_PUBLIC/support/analyzeHeadless.bat')
            $writer = [IO.StreamWriter]::new($entry.Open())
            try { $writer.WriteLine('@exit /b 0') }
            finally { $writer.Dispose() }
        }
        finally { $zip.Dispose() }
    }
    finally { $stream.Dispose() }
}

function New-EvidenceFixture {
    param([string] $Root)
    $programs = Join-Path $Root 'programs'
    $ghidra = Join-Path $Root 'ghidra_12.1.2_PUBLIC'
    $output = Join-Path $Root 'evidence'
    New-Item -ItemType Directory -Path (Join-Path $ghidra 'support') -Force |
        Out-Null
    [IO.File]::WriteAllText((Join-Path $ghidra 'support\analyzeHeadless.bat'),
        '@exit /b 0')
    [ordered]@{
        schemaVersion = 1
        archiveSha256 = $pinnedDigest
        release = '12.1.2'
    } | ConvertTo-Json | Set-Content -LiteralPath (
        Join-Path $ghidra '.blind-soldier-ghidra.json') -Encoding utf8
    $x86 = Join-Path $programs 'Blind-Soldier-Bootstrap-x86.exe'
    $x64 = Join-Path $programs 'Blind-Soldier-Bootstrap-x64.exe'
    $proxy = Join-Path $programs 'winmm.dll'
    $hostProgram = Join-Path $programs 'FFVII.exe'
    New-TestPe -Path $x86 -Machine 0x014C
    New-TestPe -Path $x64 -Machine 0x8664
    New-TestPe -Path $proxy -Machine 0x014C
    New-TestPe -Path $hostProgram -Machine 0x8664
    [pscustomobject]@{
        GhidraRoot = $ghidra
        Output = $output
        X86 = $x86
        X64 = $x64
        Proxy = $proxy
        HostProgram = $hostProgram
    }
}

function Get-GoodEvidence {
    param([psobject] $Request)
    $required = [ordered]@{}
    if ($Request.Kind -ceq 'bootstrap-x86') {
        foreach ($name in @(
            'OpenProcess','QueryFullProcessImageNameW','VirtualAllocEx',
            'WriteProcessMemory','CreateRemoteThread','LoadLibraryW',
            'CreateMutexW','MoveFileExW','SetEvent','PrivateRuntime')) {
            $required[$name] = $true
        }
    }
    elseif ($Request.Kind -ceq 'bootstrap-x64') {
        foreach ($name in @(
            'CreateProcessW','VirtualAllocEx','WriteProcessMemory',
            'CreateRemoteThread','LoadLibraryW','CreateMutexW','MoveFileExW',
            'ResumeThread','PrivateRuntime')) {
            $required[$name] = $true
        }
    }
    elseif ($Request.Kind -ceq 'winmm-proxy') {
        foreach ($name in @(
            'GetSystemWow64DirectoryW','AbsoluteSystemWinmm','CreateThread',
            'WaitForSingleObject','WaitForMultipleObjects','MessageBoxW',
            'TerminateProcess','HostGuards','BoundedParentSearch')) {
            $required[$name] = $true
        }
    }
    else {
        $required['HostIdentity'] = $true
    }
    $exports = @()
    if ($Request.Kind -ceq 'winmm-proxy') {
        $exports = @(([IO.File]::ReadAllText($exportManifestPath) |
            ConvertFrom-Json).exports | Select-Object ordinal,name,noname)
    }
    return [ordered]@{
        schemaVersion = 1
        marker = 'BLIND_SOLDIER_GHIDRA_EVIDENCE'
        kind = $Request.Kind
        program = [IO.Path]::GetFullPath($Request.ProgramPath)
        sha256 = (Get-FileHash -LiteralPath $Request.ProgramPath `
            -Algorithm SHA256).Hash
        machine = $Request.ExpectedMachine
        required = $required
        forbidden = @()
        exports = $exports
        tool = 'Ghidra 12.1.2 fixture'
    }
}

function Write-Evidence {
    param([psobject] $Request, [hashtable] $Changes)
    $evidence = Get-GoodEvidence -Request $Request
    foreach ($name in $Changes.Keys) { $evidence[$name] = $Changes[$name] }
    New-Item -ItemType Directory -Path (Split-Path -Parent $Request.ReportPath) `
        -Force | Out-Null
    [IO.File]::WriteAllText($Request.ReportPath,
        ($evidence | ConvertTo-Json -Depth 12),
        [Text.UTF8Encoding]::new($false))
    return [pscustomobject]@{ ExitCode = 0; Output = @('fixture analyzed') }
}

foreach ($requiredFile in @($installerPath, $verifierPath)) {
    Assert-True (Test-Path -LiteralPath $requiredFile -PathType Leaf) `
        "Required Ghidra tool is missing: $requiredFile"
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) `
    ('blind-soldier-ghidra-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $archive = Join-Path $testRoot 'fixture-ghidra.zip'
    New-TestGhidraArchive -Path $archive
    $archiveDigest = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash

    Assert-ThrowsLike -Name 'wrong Ghidra digest' -Pattern 'SHA-256' -Action {
        & $installerPath -ToolRoot (Join-Path $testRoot 'wrong-digest') `
            -ArchivePath $archive -ExpectedSha256 ('0' * 64) `
            -ExpectedRootName 'ghidra_12.1.2_PUBLIC' -JavaPath 'java.exe'
    }

    Assert-ThrowsLike -Name 'missing Java' -Pattern 'Java.*unavailable' -Action {
        & $installerPath -ToolRoot (Join-Path $testRoot 'missing-java') `
            -ArchivePath $archive -ExpectedSha256 $archiveDigest `
            -ExpectedRootName 'ghidra_12.1.2_PUBLIC' `
            -JavaPath (Join-Path $testRoot 'does-not-exist\java.exe')
    }

    $fixture = New-EvidenceFixture -Root (Join-Path $testRoot 'wrapper')
    $goodInvoker = {
        param($Request)
        Write-Evidence -Request $Request -Changes @{}
    }.GetNewClosure()
    Assert-ThrowsLike -Name 'missing program' -Pattern 'program.*unavailable' `
        -Action {
            & $verifierPath -GhidraRoot $fixture.GhidraRoot `
                -X86BrokerPath $fixture.X86 `
                -X64BrokerPath (Join-Path $testRoot 'missing.exe') `
                -ProxyPath $fixture.Proxy -OutputDirectory $fixture.Output `
                -AnalysisInvoker $goodInvoker
        }

    $missingMarkerInvoker = {
        param($Request)
        Write-Evidence -Request $Request -Changes @{
            marker = 'NOT_THE_REQUIRED_MARKER'
        }
    }.GetNewClosure()
    Assert-ThrowsLike -Name 'missing evidence marker' `
        -Pattern 'evidence marker' -Action {
            & $verifierPath -GhidraRoot $fixture.GhidraRoot `
                -X86BrokerPath $fixture.X86 -X64BrokerPath $fixture.X64 `
                -ProxyPath $fixture.Proxy -OutputDirectory $fixture.Output `
                -AnalysisInvoker $missingMarkerInvoker
        }

    $registryInvoker = {
        param($Request)
        Write-Evidence -Request $Request -Changes @{
            forbidden = @('RegSetValueExW')
        }
    }.GetNewClosure()
    Assert-ThrowsLike -Name 'registry-writing import' `
        -Pattern 'forbidden native evidence' -Action {
            & $verifierPath -GhidraRoot $fixture.GhidraRoot `
                -X86BrokerPath $fixture.X86 -X64BrokerPath $fixture.X64 `
                -ProxyPath $fixture.Proxy -OutputDirectory $fixture.Output `
                -AnalysisInvoker $registryInvoker
        }

    $incompleteExportsInvoker = {
        param($Request)
        $changes = @{}
        if ($Request.Kind -ceq 'winmm-proxy') {
            $all = @(([IO.File]::ReadAllText($exportManifestPath) |
                ConvertFrom-Json).exports | Select-Object ordinal,name,noname)
            $changes.exports = @($all | Select-Object -First 192)
        }
        Write-Evidence -Request $Request -Changes $changes
    }.GetNewClosure()
    Assert-ThrowsLike -Name 'incomplete WinMM exports' `
        -Pattern 'WinMM export table' -Action {
            & $verifierPath -GhidraRoot $fixture.GhidraRoot `
                -X86BrokerPath $fixture.X86 -X64BrokerPath $fixture.X64 `
                -ProxyPath $fixture.Proxy -OutputDirectory $fixture.Output `
                -AnalysisInvoker $incompleteExportsInvoker
        }

    $result = & $verifierPath -GhidraRoot $fixture.GhidraRoot `
        -X86BrokerPath $fixture.X86 -X64BrokerPath $fixture.X64 `
        -ProxyPath $fixture.Proxy -OutputDirectory $fixture.Output `
        -HostPaths @($fixture.HostProgram) `
        -AnalysisInvoker $goodInvoker
    Assert-True ($result.VerificationSucceeded -eq $true) `
        'Successful fixture verification did not return success.'
    Assert-True (@($result.Evidence).Count -eq 4) `
        'Successful fixture verification did not return four evidence records.'

    Write-Host 'PASS: pinned acquisition rejects a wrong archive digest'
    Write-Host 'PASS: pinned acquisition rejects missing 64-bit Java 21'
    Write-Host 'PASS: wrapper rejects a missing program'
    Write-Host 'PASS: wrapper rejects a missing evidence marker'
    Write-Host 'PASS: wrapper rejects registry-writing evidence'
    Write-Host 'PASS: wrapper rejects an incomplete WinMM export table'
    Write-Host 'PASS: wrapper accepts complete machine-readable evidence'
}
finally {
    if (Test-Path -LiteralPath $testRoot -PathType Container) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
