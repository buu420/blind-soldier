$ErrorActionPreference = 'Stop'

$nativeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $nativeRoot
$manifestPath = Join-Path $repoRoot `
    'analysis\native-bootstrap\winmm-exports-10.0.26100.8737.json'
$generatorPath = Join-Path $repoRoot 'tools\Generate-WinmmForwarders.ps1'
$proxyRoot = Join-Path $nativeRoot 'BlindSoldier.WinMMProxy'
$proxyProject = Join-Path $proxyRoot 'BlindSoldier.WinMMProxy.vcxproj'
$versionRoot = Join-Path $nativeRoot 'BlindSoldier.VersionProxy'
$versionProject = Join-Path $versionRoot 'BlindSoldier.VersionProxy.vcxproj'
$versionProxySource = Join-Path $versionRoot 'version_proxy.cpp'
$proxyStateSource = Join-Path $proxyRoot 'proxy_state.cpp'
$proxyBehaviorProject = Join-Path $nativeRoot `
    'BlindSoldier.WinMMProxy.Tests\BlindSoldier.WinMMProxy.Tests.vcxproj'
$forwardingProject = Join-Path $nativeRoot `
    'BlindSoldier.WinMMProxy.Tests\BlindSoldier.WinMMForwardingSmoke.vcxproj'
$versionForwardingProject = Join-Path $nativeRoot `
    'BlindSoldier.WinMMProxy.Tests\BlindSoldier.VersionForwardingSmoke.vcxproj'
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

Describe 'Blind Soldier guarded x86 native proxies' {
    It 'contains the locked evidence generator proxy and behavior fixtures' {
        foreach ($path in @(
            $manifestPath, $generatorPath, $proxyProject,
            (Join-Path $proxyRoot 'proxy.cpp'),
            (Join-Path $proxyRoot 'proxy_state.h'),
            (Join-Path $proxyRoot 'proxy_state.cpp'),
            (Join-Path $proxyRoot 'winmm_exports.inc'),
            (Join-Path $proxyRoot 'winmm.def'),
            $versionProject,
            (Join-Path $versionRoot 'version_proxy.cpp'),
            (Join-Path $versionRoot 'version_cache.h'),
            (Join-Path $versionRoot 'version_cache.cpp'),
            (Join-Path $versionRoot 'version_exports.inc'),
            (Join-Path $versionRoot 'version.def'),
            $proxyBehaviorProject, $forwardingProject,
            $versionForwardingProject
        )) {
            Test-Path -LiteralPath $path -PathType Leaf | Should Be $true
        }
    }

    It 'keeps Version loading and bootstrap work in the scheduled worker' {
        $versionSource = [IO.File]::ReadAllText($versionProxySource)
        $dllMain = [regex]::Match($versionSource,
            '(?s)BOOL WINAPI DllMain\b.*?#define BS_VERSION_FORWARD')
        $dllMain.Success | Should Be $true
        $dllMain.Value | Should Not Match `
            'LoadSystemVersion|StartBootstrapMonitor|LoadLibraryW|LoadLibraryExW|CreateDirectoryW|CopyFileW|MessageBoxW|DisableThreadLibraryCalls|WaitFor|Sleep'
        $dllMain.Value | Should Match 'g_proxyModule\s*=\s*instance'
        $dllMain.Value | Should Match `
            '(?s)CreateThread\([^;]*VersionInitializationWorker'

        $resolver = [regex]::Match($versionSource,
            '(?s)extern "C" FARPROC __cdecl ResolveVersionExport\b.*?\n}\n\nBOOL WINAPI DllMain')
        $resolver.Success | Should Be $true
        $resolver.Value | Should Not Match `
            'LoadSystemVersion|StartBootstrapMonitor|CreateThread|CreateDirectoryW|CopyFileW|LoadLibraryW|LoadLibraryExW'
        $resolver.Value | Should Match 'WaitForPublishedVersionExport'

        $forwardingWait = [regex]::Match($versionSource,
            '(?s)FARPROC WaitForPublishedVersionExport\b.*?\n}')
        $forwardingWait.Success | Should Be $true
        $forwardingWait.Value | Should Not Match `
            'LoadSystemVersion|StartBootstrapMonitor|CreateThread|CreateDirectoryW|CopyFileW|LoadLibraryW|LoadLibraryExW'
        $forwardingWait.Value | Should Match 'kForwardingReadyTimeoutMilliseconds'
    }

    It 'lets phase-specific readiness and broker deadlines govern' {
        $stateSource = [IO.File]::ReadAllText($proxyStateSource)
        $portableWait = [regex]::Match($stateSource,
            '(?s)void WaitForPortableBootstrap\(\).*?\n}\n\n}  // namespace blind_soldier')
        $portableWait.Success | Should Be $true
        $portableWait.Value | Should Match `
            'WaitForSingleObject\(g_workerFinished,\s*INFINITE\)'
        $portableWait.Value | Should Not Match `
            'kStockRuntimeReadinessTimeoutMilliseconds\s*\+|waitMilliseconds|151000'
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
                -AllowCompatibleSystemWinmm `
                -ManifestPath (Join-Path $root 'manifest.json') `
                -IncludePath (Join-Path $root 'winmm_exports.inc') `
                -DefinitionPath (Join-Path $root 'winmm.def')
            $LASTEXITCODE | Should Be 0
            $generatedManifest = [IO.File]::ReadAllText(
                (Join-Path $root 'manifest.json')) | ConvertFrom-Json
            $checkedInManifest = [IO.File]::ReadAllText($manifestPath) |
                ConvertFrom-Json
            $generatedManifest.fileVersion | Should Be `
                ([string](Get-Item -LiteralPath $systemWinmm).VersionInfo.ProductVersion)
            $generatedManifest.sha256 | Should Be `
                (Get-FileHash -LiteralPath $systemWinmm -Algorithm SHA256).Hash
            ($generatedManifest.exports |
                Select-Object ordinal,name,noname | ConvertTo-Json -Compress) |
                Should Be ($checkedInManifest.exports |
                    Select-Object ordinal,name,noname | ConvertTo-Json -Compress)
            foreach ($pair in @(
                @((Join-Path $root 'winmm_exports.inc'),
                    (Join-Path $proxyRoot 'winmm_exports.inc')),
                @((Join-Path $root 'winmm.def'),
                    (Join-Path $proxyRoot 'winmm.def'))
            )) {
                (Get-FileHash -LiteralPath $pair[0] -Algorithm SHA256).Hash |
                    Should Be (Get-FileHash -LiteralPath $pair[1] `
                        -Algorithm SHA256).Hash
            }
        }
        finally { Remove-Item -LiteralPath $root -Recurse -Force }
    }

    It 'requires explicit compatibility mode for a changed system image' {
        $root = Join-Path ([IO.Path]::GetTempPath()) `
            ('blind-soldier-winmm-compat-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $root | Out-Null
        try {
            $modifiedWinmm = Join-Path $root 'winmm.dll'
            Copy-Item -LiteralPath $systemWinmm -Destination $modifiedWinmm
            $stream = [IO.File]::Open($modifiedWinmm, [IO.FileMode]::Open,
                [IO.FileAccess]::Write, [IO.FileShare]::None)
            try {
                $stream.Position = $stream.Length
                $stream.WriteByte(0x42)
            }
            finally { $stream.Dispose() }

            $strictRejected = $false
            try {
                & $generatorPath -SystemWinmmPath $modifiedWinmm `
                    -ManifestPath (Join-Path $root 'strict.json') `
                    -IncludePath (Join-Path $root 'strict.inc') `
                    -DefinitionPath (Join-Path $root 'strict.def')
            }
            catch { $strictRejected = $true }
            $strictRejected | Should Be $true

            & $generatorPath -SystemWinmmPath $modifiedWinmm `
                -AllowCompatibleSystemWinmm `
                -ManifestPath (Join-Path $root 'compatible.json') `
                -IncludePath (Join-Path $root 'compatible.inc') `
                -DefinitionPath (Join-Path $root 'compatible.def')
            $LASTEXITCODE | Should Be 0
            (Get-FileHash -LiteralPath (Join-Path $root 'compatible.inc') `
                -Algorithm SHA256).Hash | Should Be `
                (Get-FileHash -LiteralPath (Join-Path $proxyRoot `
                    'winmm_exports.inc') -Algorithm SHA256).Hash
            (Get-FileHash -LiteralPath (Join-Path $root 'compatible.def') `
                -Algorithm SHA256).Hash | Should Be `
                (Get-FileHash -LiteralPath (Join-Path $proxyRoot 'winmm.def') `
                    -Algorithm SHA256).Hash
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

    It 'forwards Version APIs while starting the sibling x86 bootstrap' {
        $tools = Get-WinmmTestTools
        Test-Path -LiteralPath $versionProject -PathType Leaf | Should Be $true
        & $tools.MsBuild $versionProject /nologo /m /t:Rebuild `
            /p:Configuration=Release /p:Platform=Win32 /v:minimal
        $LASTEXITCODE | Should Be 0
        & $tools.MsBuild $versionForwardingProject /nologo /m /t:Rebuild `
            /p:Configuration=Release /p:Platform=Win32 /v:minimal
        $LASTEXITCODE | Should Be 0

        $proxy = Join-Path $versionRoot 'bin\Release\Win32\version.dll'
        (Get-TestPeMachine $proxy) | Should Be 0x014C
        $exports = (& $tools.Dumpbin /exports $proxy) -join "`n"
        foreach ($name in @(
            'GetFileVersionInfoA', 'GetFileVersionInfoByHandle',
            'GetFileVersionInfoExA', 'GetFileVersionInfoExW',
            'GetFileVersionInfoSizeA', 'GetFileVersionInfoSizeExA',
            'GetFileVersionInfoSizeExW', 'GetFileVersionInfoSizeW',
            'GetFileVersionInfoW', 'VerFindFileA', 'VerFindFileW',
            'VerInstallFileA', 'VerInstallFileW', 'VerLanguageNameA',
            'VerLanguageNameW', 'VerQueryValueA', 'VerQueryValueW')) {
            $exports | Should Match ("(?m)\s" + [regex]::Escape($name) + "\s*$")
        }
        $dependents = (& $tools.Dumpbin /dependents $proxy) -join "`n"
        $dependents | Should Not Match '(?i)VCRUNTIME|MSVCP|ucrtbase'

        $root = Join-Path ([IO.Path]::GetTempPath()) `
            ('bs-version-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $root | Out-Null
        try {
            $sourceSmoke = Join-Path (Split-Path -Parent $versionForwardingProject) `
                'bin\Release\Win32\BlindSoldier.VersionForwardingSmoke.exe'
            $smoke = Join-Path $root 'BlindSoldier.VersionForwardingSmoke.exe'
            Copy-Item -LiteralPath $sourceSmoke -Destination $smoke
            Copy-Item -LiteralPath $proxy -Destination (Join-Path $root 'version.dll')

            & $smoke '--cache-tests'
            $LASTEXITCODE | Should Be 0

            $fallbackRoot = Join-Path $root 'proxy-fallback'
            New-Item -ItemType Directory -Path $fallbackRoot | Out-Null
            $fallbackSmoke = Join-Path $fallbackRoot `
                'BlindSoldier.VersionForwardingSmoke.exe'
            Copy-Item -LiteralPath $sourceSmoke -Destination $fallbackSmoke
            $localDirectory = Join-Path $fallbackRoot `
                'BlindSoldier.VersionForwardingSmoke.exe.local'
            New-Item -ItemType Directory -Path $localDirectory | Out-Null
            Copy-Item -LiteralPath $proxy -Destination `
                (Join-Path $localDirectory 'version.dll')
            & $fallbackSmoke '--proxy-fallback'
            $LASTEXITCODE | Should Be 0

            $loadOnly = Start-Process -FilePath $smoke `
                -ArgumentList '--load-only' -WorkingDirectory $root `
                -PassThru -WindowStyle Hidden
            $loadOnlyExited = $loadOnly.WaitForExit(15000)
            if (-not $loadOnlyExited) { Stop-Process -Id $loadOnly.Id -Force }
            $loadOnlyExited | Should Be $true
            if ($loadOnlyExited) { $loadOnly.ExitCode | Should Be 0 }

            $immediate = Start-Process -FilePath $smoke `
                -WorkingDirectory $root -PassThru -WindowStyle Hidden
            $immediateExited = $immediate.WaitForExit(15000)
            if (-not $immediateExited) { Stop-Process -Id $immediate.Id -Force }
            $immediateExited | Should Be $true
            if ($immediateExited) { $immediate.ExitCode | Should Be 0 }
        }
        finally { Remove-Item -LiteralPath $root -Recurse -Force }
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
