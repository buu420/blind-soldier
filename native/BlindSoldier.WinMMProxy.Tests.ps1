$ErrorActionPreference = 'Stop'

$nativeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $nativeRoot
$manifestPath = Join-Path $repoRoot `
    'analysis\native-bootstrap\winmm-exports-10.0.26100.8737.json'
$generatorPath = Join-Path $repoRoot 'tools\Generate-WinmmForwarders.ps1'
$proxyRoot = Join-Path $nativeRoot 'BlindSoldier.WinMMProxy'
$proxyProject = Join-Path $proxyRoot 'BlindSoldier.WinMMProxy.vcxproj'
$proxyBehaviorProject = Join-Path $nativeRoot `
    'BlindSoldier.WinMMProxy.Tests\BlindSoldier.WinMMProxy.Tests.vcxproj'
$forwardingProject = Join-Path $nativeRoot `
    'BlindSoldier.WinMMProxy.Tests\BlindSoldier.WinMMForwardingSmoke.vcxproj'
$systemWinmm = Join-Path $env:WINDIR 'SysWOW64\winmm.dll'

function Get-WinmmTestTools {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} `
        'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
        throw 'Visual Studio vswhere.exe is unavailable.'
    }
    $install = (& $vswhere -latest -products '*' `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath | Select-Object -First 1)
    $msbuild = Join-Path $install 'MSBuild\Current\Bin\MSBuild.exe'
    $dumpbin = Get-ChildItem -LiteralPath (Join-Path $install 'VC\Tools\MSVC') `
        -Recurse -Filter dumpbin.exe |
        Where-Object FullName -Match '\\Hostx64\\x64\\dumpbin\.exe$' |
        Sort-Object FullName -Descending | Select-Object -First 1 `
        -ExpandProperty FullName
    if (-not (Test-Path -LiteralPath $msbuild -PathType Leaf) -or
        -not (Test-Path -LiteralPath $dumpbin -PathType Leaf)) {
        throw 'MSVC build tools are unavailable.'
    }
    [pscustomobject]@{ MsBuild=$msbuild; Dumpbin=$dumpbin }
}

function Get-DumpbinExports {
    param([string] $Dumpbin, [string] $Path)
    $lines = @(& $Dumpbin /exports $Path)
    if ($LASTEXITCODE -ne 0) { throw "dumpbin failed for $Path" }
    $exports = New-Object 'System.Collections.Generic.List[object]'
    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if ($trimmed -match '^(\d+)\s+([0-9A-F]+)\s+\[NONAME\]$') {
            $exports.Add([pscustomobject]@{
                ordinal=[int]$matches[1]; name=$null; noname=$true
            })
        }
        elseif ($trimmed -match '^(\d+)\s+[0-9A-F]+\s+[0-9A-F]+\s+(\S+)') {
            $exports.Add([pscustomobject]@{
                ordinal=[int]$matches[1]; name=[string]$matches[2]; noname=$false
            })
        }
    }
    return @($exports | Sort-Object ordinal)
}

function Get-TestPeMachine {
    param([string] $Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    $offset = [BitConverter]::ToInt32($bytes, 0x3C)
    return [BitConverter]::ToUInt16($bytes, $offset + 4)
}

Describe 'Blind Soldier guarded x86 WinMM proxy' {
    It 'contains the locked evidence generator proxy and behavior fixtures' {
        foreach ($path in @(
            $manifestPath, $generatorPath, $proxyProject,
            (Join-Path $proxyRoot 'proxy.cpp'),
            (Join-Path $proxyRoot 'proxy_state.h'),
            (Join-Path $proxyRoot 'proxy_state.cpp'),
            (Join-Path $proxyRoot 'winmm_exports.inc'),
            (Join-Path $proxyRoot 'winmm.def'),
            $proxyBehaviorProject, $forwardingProject
        )) {
            Test-Path -LiteralPath $path -PathType Leaf | Should Be $true
        }
    }

    It 'locks every named and ordinal-only export of the observed system WinMM' {
        $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
        $manifest.schemaVersion | Should Be 1
        $manifest.fileVersion | Should Be '10.0.26100.8737'
        $manifest.sha256 | Should Be `
            '761E7285BDCA295F82E9EC88FE73D7CF23FBDCB1757F0E043DC701BB3ECD3A51'
        $entries = @($manifest.exports)
        $entries.Count | Should Be 193
        @($entries | Where-Object { -not $_.noname }).Count | Should Be 192
        @($entries | Where-Object noname).Count | Should Be 1
        ($entries | Where-Object noname).ordinal | Should Be 2

        $tools = Get-WinmmTestTools
        $actual = @(Get-DumpbinExports -Dumpbin $tools.Dumpbin -Path $systemWinmm)
        ($actual | ConvertTo-Json -Compress) | Should Be `
            ($entries | Select-Object ordinal,name,noname | ConvertTo-Json -Compress)
    }

    It 'regenerates the checked-in forwarders deterministically' {
        $root = Join-Path ([IO.Path]::GetTempPath()) `
            ('blind-soldier-winmm-generator-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $root | Out-Null
        try {
            & $generatorPath -SystemWinmmPath $systemWinmm `
                -ManifestPath (Join-Path $root 'manifest.json') `
                -IncludePath (Join-Path $root 'winmm_exports.inc') `
                -DefinitionPath (Join-Path $root 'winmm.def')
            $LASTEXITCODE | Should Be 0
            foreach ($pair in @(
                @((Join-Path $root 'manifest.json'), $manifestPath),
                @((Join-Path $root 'winmm_exports.inc'),
                    (Join-Path $proxyRoot 'winmm_exports.inc')),
                @((Join-Path $root 'winmm.def'),
                    (Join-Path $proxyRoot 'winmm.def'))
            )) {
                if ([IO.Path]::GetFileName($pair[0]) -eq 'manifest.json') {
                    $generated = [IO.File]::ReadAllText($pair[0]) |
                        ConvertFrom-Json | ConvertTo-Json -Depth 5 -Compress
                    $checkedIn = [IO.File]::ReadAllText($pair[1]) |
                        ConvertFrom-Json | ConvertTo-Json -Depth 5 -Compress
                    $generated | Should Be $checkedIn
                }
                else {
                    (Get-FileHash -LiteralPath $pair[0] -Algorithm SHA256).Hash |
                        Should Be (Get-FileHash -LiteralPath $pair[1] `
                            -Algorithm SHA256).Hash
                }
            }
        }
        finally { Remove-Item -LiteralPath $root -Recurse -Force }
    }

    It 'passes root discovery host gating and one-shot broker behavior tests' {
        $tools = Get-WinmmTestTools
        & $tools.MsBuild $proxyBehaviorProject /nologo /m /t:Rebuild `
            /p:Configuration=Release /p:Platform=Win32 /v:minimal
        $LASTEXITCODE | Should Be 0
        $test = Join-Path (Split-Path -Parent $proxyBehaviorProject) `
            'bin\Release\Win32\BlindSoldier.WinMMProxy.Tests.exe'
        & $test
        $LASTEXITCODE | Should Be 0
    }

    It 'builds x86 and exactly matches the complete WinMM export table' {
        $tools = Get-WinmmTestTools
        & $tools.MsBuild $proxyProject /nologo /m /t:Rebuild `
            /p:Configuration=Release /p:Platform=Win32 /v:minimal
        $LASTEXITCODE | Should Be 0
        $proxy = Join-Path $proxyRoot 'bin\Release\Win32\winmm.dll'
        (Get-TestPeMachine $proxy) | Should Be 0x014C
        $dependents = (& $tools.Dumpbin /dependents $proxy) -join "`n"
        $dependents | Should Not Match '(?i)VCRUNTIME|MSVCP|ucrtbase'
        $expected = @(([IO.File]::ReadAllText($manifestPath) |
            ConvertFrom-Json).exports | Select-Object ordinal,name,noname)
        $actual = @(Get-DumpbinExports -Dumpbin $tools.Dumpbin -Path $proxy)
        ($actual | ConvertTo-Json -Compress) | Should Be `
            ($expected | ConvertTo-Json -Compress)
    }

    It 'forwards representative APIs to the canonical system module without recursion' {
        $tools = Get-WinmmTestTools
        & $tools.MsBuild $forwardingProject /nologo /m /t:Rebuild `
            /p:Configuration=Release /p:Platform=Win32 /v:minimal
        $LASTEXITCODE | Should Be 0
        $smoke = Join-Path (Split-Path -Parent $forwardingProject) `
            'bin\Release\Win32\BlindSoldier.WinMMForwardingSmoke.exe'
        $proxy = Join-Path $proxyRoot 'bin\Release\Win32\winmm.dll'
        $process = Start-Process -FilePath $smoke -ArgumentList @(
            ('"' + $proxy + '"'), ('"' + $systemWinmm + '"')) `
            -PassThru -WindowStyle Hidden
        $exited = $process.WaitForExit(30000)
        if (-not $exited) { Stop-Process -Id $process.Id -Force }
        $exited | Should Be $true
        if ($exited) { $process.ExitCode | Should Be 0 }
    }
}
