$ErrorActionPreference = 'Stop'

$toolsRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $toolsRoot
$installerPath = Join-Path $toolsRoot 'Install-PinnedGhidra.ps1'
$verifierPath = Join-Path $toolsRoot 'Invoke-BlindSoldierGhidraVerification.ps1'
$versionCollectorPath = Join-Path $repoRoot `
    'analysis\ghidra\BlindSoldierVersionEvidence.java'
$versionRulesPath = Join-Path $repoRoot `
    'analysis\ghidra\BlindSoldierVersionEvidenceRules.java'
$versionRulesTestPath = Join-Path $repoRoot `
    'analysis\ghidra\BlindSoldierVersionEvidenceRules.Tests.java'
$expectedVersionExports = @(
    @{ordinal=1;name='GetFileVersionInfoA';noname=$false},
    @{ordinal=2;name='GetFileVersionInfoByHandle';noname=$false},
    @{ordinal=3;name='GetFileVersionInfoExA';noname=$false},
    @{ordinal=4;name='GetFileVersionInfoExW';noname=$false},
    @{ordinal=5;name='GetFileVersionInfoSizeA';noname=$false},
    @{ordinal=6;name='GetFileVersionInfoSizeExA';noname=$false},
    @{ordinal=7;name='GetFileVersionInfoSizeExW';noname=$false},
    @{ordinal=8;name='GetFileVersionInfoSizeW';noname=$false},
    @{ordinal=9;name='GetFileVersionInfoW';noname=$false},
    @{ordinal=10;name='VerFindFileA';noname=$false},
    @{ordinal=11;name='VerFindFileW';noname=$false},
    @{ordinal=12;name='VerInstallFileA';noname=$false},
    @{ordinal=13;name='VerInstallFileW';noname=$false},
    @{ordinal=14;name='VerLanguageNameA';noname=$false},
    @{ordinal=15;name='VerLanguageNameW';noname=$false},
    @{ordinal=16;name='VerQueryValueA';noname=$false},
    @{ordinal=17;name='VerQueryValueW';noname=$false}
)
$expectedArchiveEntries = @(
    'ff7_en.exe.local/version.dll','ff7.exe.local/version.dll',
    'ff7/workingdir/ff7_en.exe.local/version.dll',
    'ff7/workingdir/ff7.exe.local/version.dll',
    'Blind-Soldier/Bootstrap/x86/Blind-Soldier-Bootstrap-x86.exe',
    'Blind-Soldier/Bootstrap/x64/Blind-Soldier-Bootstrap-x64.exe')
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
    $proxy = Join-Path $programs 'version.dll'
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

function New-EvidenceArchive {
    param(
        [string] $Path,
        [psobject] $Fixture,
        [switch] $DivergentProxy,
        [switch] $AliasedProxy
    )
    Add-Type -AssemblyName System.IO.Compression
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew)
    try {
        $zip = [IO.Compression.ZipArchive]::new($stream,
            [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($name in $expectedArchiveEntries) {
                $source = if ($name -like '*Bootstrap/x86/*') { $Fixture.X86 }
                    elseif ($name -like '*Bootstrap/x64/*') { $Fixture.X64 }
                    else { $Fixture.Proxy }
                $bytes = [IO.File]::ReadAllBytes($source)
                if ($DivergentProxy -and $name -ceq
                        'ff7/workingdir/ff7.exe.local/version.dll') {
                    $bytes[0x100] = $bytes[0x100] -bxor 0x5A
                }
                $entry = $zip.CreateEntry($name)
                $target = $entry.Open()
                try { $target.Write($bytes, 0, $bytes.Length) }
                finally { $target.Dispose() }
            }
            if ($AliasedProxy) {
                $bytes = [IO.File]::ReadAllBytes($Fixture.Proxy)
                $entry = $zip.CreateEntry('FF7_EN.EXE.LOCAL\version.dll')
                $target = $entry.Open()
                try { $target.Write($bytes, 0, $bytes.Length) }
                finally { $target.Dispose() }
            }
        }
        finally { $zip.Dispose() }
    }
    finally { $stream.Dispose() }
}
function New-EvidenceInvoker {
    param(
        [ValidateSet('Good','MissingMarker','RegistryForbidden',
            'MissingRequired','IncompleteExports','ProxyRemoteInjection')]
        [string] $Mode = 'Good',
        [System.Collections.Generic.List[object]] $Requests
    )
    $versionExports = @($expectedVersionExports | ForEach-Object {
        [pscustomobject]@{
            ordinal=[int]$_.ordinal
            name=[string]$_.name
            noname=[bool]$_.noname
        }
    })
    return {
        param($Request)
        if ($null -ne $Requests) { $Requests.Add($Request) }
        $requiredNames = if ($Request.Kind -ceq 'bootstrap-x86') {
            @('OpenProcess','QueryFullProcessImageNameW','VirtualAllocEx',
                'WriteProcessMemory','CreateRemoteThread','LoadLibraryW',
                'CreateMutexW','MoveFileExW','SetEvent','PrivateRuntime')
        }
        elseif ($Request.Kind -ceq 'bootstrap-x64') {
            @('CreateProcessW','VirtualAllocEx','WriteProcessMemory',
                'CreateRemoteThread','LoadLibraryW','CreateMutexW',
                'MoveFileExW','ResumeThread','PrivateRuntime')
        }
        elseif ($Request.Kind -ceq 'version-proxy') {
            @('SystemVersionLoaderCluster','VersionCacheValidationCluster',
                'AppLoaderSignatureCluster','AppLoaderMarkerParser',
                'AppLoaderTimeoutStateCluster','SupportedHostNameValidation',
                'PackageRootBoundaryValidation',
                'VersionWorkerAndPortableBrokerPrimitives',
                'NoWinmmForwardingSurface','NoEmbeddedExternalRuntime')
        }
        else { @('HostIdentity') }
        $required = [ordered]@{}
        foreach ($name in $requiredNames) { $required[$name] = $true }
        $exports = if ($Request.Kind -ceq 'version-proxy') {
            @($versionExports | ForEach-Object {
                [ordered]@{
                    ordinal=[int]$_.ordinal
                    name=[string]$_.name
                    noname=[bool]$_.noname
                }
            })
        }
        else { @() }
        $marker = 'BLIND_SOLDIER_GHIDRA_EVIDENCE'
        $forbidden = @()
        if ($Mode -ceq 'MissingMarker') { $marker = 'WRONG_MARKER' }
        if ($Mode -ceq 'RegistryForbidden') {
            $forbidden = @('RegSetValueExW')
        }
        if ($Mode -ceq 'MissingRequired' -and
            $Request.Kind -ceq 'version-proxy') {
            $required['AppLoaderMarkerParser'] = $false
        }
        if ($Mode -ceq 'IncompleteExports' -and
            $Request.Kind -ceq 'version-proxy') {
            $exports = @($exports | Select-Object -First 16)
        }
        if ($Mode -ceq 'ProxyRemoteInjection' -and
            $Request.Kind -ceq 'version-proxy') {
            $forbidden = @('VirtualAllocEx')
        }
        $evidence = [ordered]@{
            schemaVersion=1;marker=$marker;kind=$Request.Kind
            program=[IO.Path]::GetFullPath($Request.ProgramPath)
            sha256=(Get-FileHash -LiteralPath $Request.ProgramPath `
                -Algorithm SHA256).Hash
            machine=$Request.ExpectedMachine;required=$required
            forbidden=$forbidden;exports=$exports
            analyzer=$Request.ScriptName;tool='Ghidra 12.1.2 fixture'
        }
        New-Item -ItemType Directory -Path `
            (Split-Path -Parent $Request.ReportPath) -Force | Out-Null
        [IO.File]::WriteAllText($Request.ReportPath,
            ($evidence | ConvertTo-Json -Depth 12),
            [Text.UTF8Encoding]::new($false))
        [pscustomobject]@{ExitCode=0;Output=@('fixture analyzed')}
    }.GetNewClosure()
}

foreach ($requiredFile in @($installerPath, $verifierPath)) {
    Assert-True (Test-Path -LiteralPath $requiredFile -PathType Leaf) `
        "Required Ghidra tool is missing: $requiredFile"
}
foreach ($requiredRuleFile in @($versionCollectorPath, $versionRulesPath,
        $versionRulesTestPath)) {
    Assert-True (Test-Path -LiteralPath $requiredRuleFile -PathType Leaf) `
        "Required Version evidence rule source is missing: $requiredRuleFile"
}

$collectorSource = [IO.File]::ReadAllText($versionCollectorPath)
Assert-True ($collectorSource -match 'getDefaultAddressSpace\(\)') `
    'Version evidence collector does not resolve the default address space.'
Assert-True ($collectorSource -match (
    '(?s)object\s+instanceof\s+Scalar.*?getUnsignedValue\(\).*?' +
    'defaultSpace\.getAddress\(value\).*?getBlock\(candidate\).*?' +
    'block\.isInitialized\(\).*?targetAddresses\.add\(candidate\)')) `
    ('Version evidence collector does not map absolute scalar operands only ' +
        'into initialized memory.')
Assert-True ($collectorSource -match 'PartitionCodeSubModel' -and
    $collectorSource -match 'resolveSyntheticFunctionFacts') `
    ('Version evidence collector does not assign decoded orphan instructions ' +
        'to recovered subroutines.')
Assert-True ($collectorSource -notmatch
    'BLIND_SOLDIER_(?:WORKER|OPERAND)_WITNESS') `
    'Temporary Version evidence witness output remains in production.'

$testRoot = Join-Path ([IO.Path]::GetTempPath()) `
    ('blind-soldier-ghidra-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $javaClasses = Join-Path $testRoot 'java-classes'
    New-Item -ItemType Directory -Path $javaClasses | Out-Null
    $javac = @(Get-Command javac.exe -CommandType Application `
        -ErrorAction Stop)[0].Source
    $java = (Get-Command java.exe -CommandType Application `
        -ErrorAction Stop)[0].Source
    $compileOutput = @(& $javac -encoding UTF-8 -d $javaClasses `
        $versionRulesPath $versionRulesTestPath 2>&1 |
        ForEach-Object { [string]$_ })
    Assert-True ($LASTEXITCODE -eq 0) `
        "Version evidence Java rules did not compile: $($compileOutput -join '; ')"
    $ruleOutput = @(& $java -cp $javaClasses `
        BlindSoldierVersionEvidenceRulesTests 2>&1 |
        ForEach-Object { [string]$_ })
    Assert-True ($LASTEXITCODE -eq 0) `
        "Version evidence Java predicates failed: $($ruleOutput -join '; ')"

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
    $goodInvoker = New-EvidenceInvoker -Mode Good
    $archivePath = Join-Path $testRoot 'bound-portable.zip'
    New-EvidenceArchive -Path $archivePath -Fixture $fixture
    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    $archiveRequests = New-Object 'System.Collections.Generic.List[object]'
    $archiveInvoker = New-EvidenceInvoker -Mode Good -Requests $archiveRequests
    $archiveOutput = Join-Path $testRoot 'archive-evidence'
    $archiveResult = & $verifierPath -GhidraRoot $fixture.GhidraRoot `
        -ArchivePath $archivePath -OutputDirectory $archiveOutput `
        -AnalysisInvoker $archiveInvoker
    Assert-True ($archiveResult.PortableArchiveSha256 -ceq $archiveHash) `
        'Archive-bound verification returned the wrong archive digest.'
    Assert-True ($archiveRequests.Count -eq 3) `
        'Archive-bound verification did not analyze exactly two brokers and one proxy.'
    Assert-True ((@($archiveRequests.ArchiveEntry | Sort-Object) -join '|') -ceq
        ((@('Blind-Soldier/Bootstrap/x64/Blind-Soldier-Bootstrap-x64.exe',
            'Blind-Soldier/Bootstrap/x86/Blind-Soldier-Bootstrap-x86.exe',
            'ff7_en.exe.local/version.dll') | Sort-Object) -join '|')) `
        'Archive-bound verification analyzed the wrong package entries.'
    foreach ($request in $archiveRequests) {
        Assert-True (-not (Test-Path -LiteralPath $request.ProgramPath)) `
            'Archive extraction was not cleaned after Ghidra analysis.'
    }
    $archiveVersion = @($archiveResult.Evidence | Where-Object {
        $_.kind -ceq 'version-proxy'
    })[0]
    Assert-True ($archiveVersion.archiveEntry -ceq
        'ff7_en.exe.local/version.dll') `
        'Version evidence is not bound to its package entry.'
    $summary = [IO.File]::ReadAllText((Join-Path $archiveOutput 'summary.json')) |
        ConvertFrom-Json
    Assert-True ($summary.portableArchiveSha256 -ceq $archiveHash) `
        'Ghidra summary is not bound to the portable archive digest.'
    Assert-True (@($summary.versionProxyEntries).Count -eq 4) `
        'Ghidra summary did not record all four packaged Version proxies.'
    Assert-True (@($summary.versionProxyEntries.sha256 | Select-Object -Unique).Count -eq 1) `
        'Ghidra summary did not prove identical Version proxy bytes.'

    $divergentArchive = Join-Path $testRoot 'divergent-portable.zip'
    New-EvidenceArchive -Path $divergentArchive -Fixture $fixture -DivergentProxy
    Assert-ThrowsLike -Name 'divergent packaged Version proxies' `
        -Pattern 'Version proxy entries.*byte-identical' -Action {
            & $verifierPath -GhidraRoot $fixture.GhidraRoot `
                -ArchivePath $divergentArchive `
                -OutputDirectory (Join-Path $testRoot 'divergent-evidence') `
                -AnalysisInvoker $goodInvoker
        }
    $aliasedArchive = Join-Path $testRoot 'aliased-portable.zip'
    New-EvidenceArchive -Path $aliasedArchive -Fixture $fixture -AliasedProxy
    Assert-ThrowsLike -Name 'case or slash aliased packaged Version proxy' `
        -Pattern 'duplicate|case-aliased' -Action {
            & $verifierPath -GhidraRoot $fixture.GhidraRoot `
                -ArchivePath $aliasedArchive `
                -OutputDirectory (Join-Path $testRoot 'aliased-evidence') `
                -AnalysisInvoker $goodInvoker
        }
    Assert-ThrowsLike -Name 'missing program' -Pattern 'program.*unavailable' `
        -Action {
            & $verifierPath -GhidraRoot $fixture.GhidraRoot `
                -X86BrokerPath $fixture.X86 `
                -X64BrokerPath (Join-Path $testRoot 'missing.exe') `
                -ProxyPath $fixture.Proxy -OutputDirectory $fixture.Output `
                -AnalysisInvoker $goodInvoker
        }

    $missingMarkerInvoker = New-EvidenceInvoker -Mode MissingMarker
    Assert-ThrowsLike -Name 'missing evidence marker' `
        -Pattern 'evidence marker' -Action {
            & $verifierPath -GhidraRoot $fixture.GhidraRoot `
                -X86BrokerPath $fixture.X86 -X64BrokerPath $fixture.X64 `
                -ProxyPath $fixture.Proxy -OutputDirectory $fixture.Output `
                -AnalysisInvoker $missingMarkerInvoker
        }

    $registryInvoker = New-EvidenceInvoker -Mode RegistryForbidden
    Assert-ThrowsLike -Name 'registry-writing import' `
        -Pattern 'forbidden native evidence' -Action {
            & $verifierPath -GhidraRoot $fixture.GhidraRoot `
                -X86BrokerPath $fixture.X86 -X64BrokerPath $fixture.X64 `
                -ProxyPath $fixture.Proxy -OutputDirectory $fixture.Output `
                -AnalysisInvoker $registryInvoker
        }

    $missingRequiredInvoker = New-EvidenceInvoker -Mode MissingRequired
    Assert-ThrowsLike -Name 'missing AppLoader marker parser evidence' `
        -Pattern 'AppLoaderMarkerParser' -Action {
            & $verifierPath -GhidraRoot $fixture.GhidraRoot `
                -X86BrokerPath $fixture.X86 -X64BrokerPath $fixture.X64 `
                -ProxyPath $fixture.Proxy -OutputDirectory $fixture.Output `
                -AnalysisInvoker $missingRequiredInvoker
        }

    $remoteInjectionInvoker = New-EvidenceInvoker -Mode ProxyRemoteInjection
    Assert-ThrowsLike -Name 'remote injection import in Version proxy' `
        -Pattern 'VirtualAllocEx' -Action {
            & $verifierPath -GhidraRoot $fixture.GhidraRoot `
                -X86BrokerPath $fixture.X86 -X64BrokerPath $fixture.X64 `
                -ProxyPath $fixture.Proxy -OutputDirectory $fixture.Output `
                -AnalysisInvoker $remoteInjectionInvoker
        }

    $incompleteExportsInvoker = New-EvidenceInvoker -Mode IncompleteExports
    Assert-ThrowsLike -Name 'incomplete Version exports' `
        -Pattern 'Version export table' -Action {
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
    $versionEvidence = @($result.Evidence | Where-Object {
        $_.kind -ceq 'version-proxy'
    })
    Assert-True ($versionEvidence.Count -eq 1) `
        'Successful fixture verification did not analyze one Version proxy.'
    Assert-True (@($versionEvidence[0].exports).Count -eq 17) `
        'Successful fixture verification did not preserve all 17 Version exports.'
    Assert-True ($versionEvidence[0].analyzer -ceq `
        'BlindSoldierVersionEvidence.java') `
        'Successful fixture verification used the wrong Version analyzer.'
    foreach ($kind in @('bootstrap-x86','bootstrap-x64')) {
        $broker = @($result.Evidence | Where-Object kind -CEQ $kind)[0]
        Assert-True ($broker.required.VirtualAllocEx -eq $true) `
            "$kind lost its expected remote-injection evidence."
    }

    Write-Host 'PASS: collector maps absolute scalar operands only into initialized memory'
    Write-Host 'PASS: Java fixtures exercise relational Version evidence rules'
    Write-Host 'PASS: Ghidra evidence is bound to the exact portable archive'
    Write-Host 'PASS: wrapper rejects divergent packaged Version proxies'
    Write-Host 'PASS: wrapper rejects aliased packaged Version proxies'
    Write-Host 'PASS: pinned acquisition rejects a wrong archive digest'
    Write-Host 'PASS: pinned acquisition rejects missing 64-bit Java 21'
    Write-Host 'PASS: wrapper rejects a missing program'
    Write-Host 'PASS: wrapper rejects a missing evidence marker'
    Write-Host 'PASS: wrapper rejects registry-writing evidence'
    Write-Host 'PASS: wrapper rejects missing AppLoader readiness evidence'
    Write-Host 'PASS: wrapper rejects remote injection in the Version proxy'
    Write-Host 'PASS: wrapper rejects an incomplete Version export table'
    Write-Host 'PASS: wrapper accepts complete machine-readable evidence'
}
finally {
    if (Test-Path -LiteralPath $testRoot -PathType Container) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
