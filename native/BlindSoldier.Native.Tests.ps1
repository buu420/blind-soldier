$ErrorActionPreference = 'Stop'

$nativeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$commonPath = Join-Path $nativeRoot 'BlindSoldier.Common\common.h'
$installerSource = Join-Path $nativeRoot 'BlindSoldier.Installer\installer.cpp'
$installerProject = Join-Path $nativeRoot 'BlindSoldier.Installer\BlindSoldier.Installer.vcxproj'
$bootstrapRoot = Join-Path $nativeRoot 'BlindSoldier.Bootstrap'
$bootstrapContract = Join-Path $bootstrapRoot 'bootstrap_contract.h'
$bootstrapSession = Join-Path $bootstrapRoot 'reloaded_session.cpp'
$bootstrapProcess = Join-Path $bootstrapRoot 'process_bootstrap.cpp'
$bootstrapMain = Join-Path $bootstrapRoot 'main.cpp'
$bootstrapProject = Join-Path $bootstrapRoot 'BlindSoldier.Bootstrap.vcxproj'
$bootstrapBehaviorProject = Join-Path $nativeRoot 'BlindSoldier.Bootstrap.Tests\BlindSoldier.Bootstrap.Tests.vcxproj'
$installerBehaviorProject = Join-Path $nativeRoot 'BlindSoldier.Installer.Tests\BlindSoldier.Installer.Tests.vcxproj'
$hostBehaviorProject = Join-Path $nativeRoot 'BlindSoldier.Host.Tests\BlindSoldier.Host.Tests.vcxproj'
$versionProxyProject = Join-Path $nativeRoot 'BlindSoldier.VersionProxy\BlindSoldier.VersionProxy.vcxproj'
$winmmProxyTests = Join-Path $nativeRoot 'BlindSoldier.WinMMProxy.Tests.ps1'

function Get-TestPeMachine {
    param([Parameter(Mandatory=$true)] [string] $Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "Not a PE image: $Path"
    }
    $offset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($offset -lt 64 -or $offset + 6 -gt $bytes.Length -or
        [BitConverter]::ToUInt32($bytes, $offset) -ne 0x00004550) {
        throw "Invalid PE header: $Path"
    }
    return [BitConverter]::ToUInt16($bytes, $offset + 4)
}

function Get-TestMsBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
        throw 'Visual Studio vswhere.exe is unavailable.'
    }
    $installation = (& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($installation)) {
        throw 'Visual Studio Build Tools with MSBuild are unavailable.'
    }
    $path = Join-Path $installation 'MSBuild\Current\Bin\MSBuild.exe'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "MSBuild is unavailable: $path"
    }
    return $path
}

Describe 'Blind Soldier native bootstrap workflow' {
    It 'contains the common host validator, broker components, and projects' {
        foreach ($path in @(
            $commonPath, $bootstrapContract, $bootstrapSession,
            $bootstrapProcess, $bootstrapMain, $bootstrapProject,
            $bootstrapBehaviorProject, $hostBehaviorProject,
            $versionProxyProject, $winmmProxyTests
        )) {
            Test-Path -LiteralPath $path -PathType Leaf | Should Be $true
        }
    }

    It 'defines the shared launch and attach contract and fail-closed exit codes' {
        $contract = [IO.File]::ReadAllText($bootstrapContract)
        $contract | Should Match 'enum class BootstrapMode\s*\{\s*Launch,\s*Attach\s*\}'
        foreach ($name in @(
            'Success', 'InvalidArguments', 'UnsupportedHost', 'MissingPayload',
            'PointerLeaseUnavailable', 'TargetUnavailable',
            'ArchitectureMismatch', 'AppConfigFailed', 'InjectionFailed',
            'ResumeFailed', 'RuntimeUnavailable', 'ReadySignalFailed'
        )) { $contract | Should Match ([regex]::Escape($name)) }
        $contract | Should Match 'TryParseBootstrapRequest'
        $contract | Should Match 'RunBootstrap'
    }

    It 'owns the Reloaded pointer only through a bounded durable lease' {
        $source = [IO.File]::ReadAllText($bootstrapSession)
        $source | Should Match 'ReloadedPointerLease'
        $source | Should Match 'waitMilliseconds'
        $source | Should Match 'WAIT_TIMEOUT'
        $source | Should Match 'blind_soldier_backup'
        $source | Should Match 'pointer changed externally'
        $source | Should Match 'MoveFileExW'
        $source | Should Match 'WriteReloadedIIPointerAt'
        $source | Should Not Match 'WaitForSingleObject\(mutex_,\s*INFINITE\)'
    }

    It 'validates the complete portable payload and ordered app configuration' {
        $session = [IO.File]::ReadAllText($bootstrapSession)
        $common = [IO.File]::ReadAllText($commonPath)
        foreach ($name in @(
            'Reloaded.Mod.Loader.Bootstrapper.dll',
            'Reloaded.Mod.Loader.dll', 'portable.txt',
            'Ff7.Accessibility.Reloaded.dll',
            'Ff7.Accessibility.Steam2026X64.dll',
            'Reloaded.Hooks.ReloadedII.dll', 'prism.dll', 'hostfxr.dll',
            '9.0.8'
        )) { $session | Should Match ([regex]::Escape($name)) }
        $session | Should Match 'IsCanonicalPathWithinRoot'
        $hooksPosition = $common.IndexOf('reloaded.sharedlib.hooks', [StringComparison]::Ordinal)
        $modPosition = $common.IndexOf('ff7.accessibility.reloaded', [StringComparison]::Ordinal)
        ($hooksPosition -ge 0 -and $modPosition -gt $hooksPosition) | Should Be $true
    }

    It 'injects by remote module-relative LoadLibrary and never launches unmodded' {
        $process = [IO.File]::ReadAllText($bootstrapProcess)
        $allBootstrap = $process + [IO.File]::ReadAllText($bootstrapMain)
        $process | Should Match 'CREATE_SUSPENDED'
        $process | Should Match 'ERROR_PARTIAL_COPY'
        $process | Should Match 'CreateRemoteThread'
        $process | Should Match 'WriteProcessMemory'
        $process | Should Match 'CreateToolhelp32Snapshot'
        $process | Should Match 'GetModuleHandleExW'
        $process | Should Match 'ResumeThread'
        $process | Should Match 'QueryFullProcessImageNameW'
        $process | Should Match 'OpenEventW'
        $process | Should Match 'SetEvent'
        $allBootstrap | Should Not Match 'LaunchGameUnmodded'
        $resumePosition = $process.IndexOf(
            'DWORD suspendCount = ResumeThread(process.hThread)',
            [StringComparison]::Ordinal)
        $injectPosition = $process.IndexOf(
            'InjectResult injected = InjectDll(process.hProcess',
            [StringComparison]::Ordinal)
        ($resumePosition -ge 0 -and $injectPosition -gt $resumePosition) |
            Should Be $true
        $allBootstrap | Should Not Match 'DEBUG_ONLY_THIS_PROCESS'
        $allBootstrap | Should Not Match 'BLIND_SOLDIER_LAUNCHER_ACTIVE'
        $allBootstrap | Should Not Match 'SetIFEODebugger'
        $allBootstrap | Should Not Match 'run an installer'
    }

    It 'waits for the resumed target module list to become ready' {
        $process = [IO.File]::ReadAllText($bootstrapProcess)
        $process | Should Match 'LPVOID WaitForRemoteModuleBase'
        $process | Should Match (
            'for\s*\(\s*;;\s*\)\s*\{[\s\S]*?' +
            'WaitForSingleObject\(process,\s*0\)')
        $process | Should Match (
            'for\s*\(\s*;;\s*\)\s*\{[\s\S]*?' +
            'CreateToolhelp32Snapshot[\s\S]*?CloseHandle\(snapshot\)' +
            '[\s\S]*?Sleep\(10\)')
        $process | Should Match 'ERROR_BAD_LENGTH'
        $process | Should Match 'ERROR_PARTIAL_COPY'
        $process | Should Match (
            'WaitForRemoteModuleBase\(\s*process,\s*processId,' +
            '[\s\S]*?5000,\s*log\)')
        $process | Should Match 'Timed out waiting for remote module'
        $process | Should Match 'Target exited while waiting for remote module'
    }

    It 'uses static runtimes and architecture-specific broker names' {
        $project = [IO.File]::ReadAllText($bootstrapProject)
        $project | Should Match 'Blind-Soldier-Bootstrap-x86'
        $project | Should Match 'Blind-Soldier-Bootstrap-x64'
        $project | Should Match '<RuntimeLibrary>MultiThreaded</RuntimeLibrary>'
        $project | Should Match '<RuntimeLibrary>MultiThreadedDebug</RuntimeLibrary>'
        $project | Should Match '<AdditionalOptions>/Brepro %\(AdditionalOptions\)</AdditionalOptions>'
        $project | Should Match 'Release\|Win32'
        $project | Should Match 'Release\|x64'
    }

    It 'passes broker behavior tests in both architectures' {
        $msbuild = Get-TestMsBuild
        foreach ($platform in @('Win32','x64')) {
            & $msbuild $bootstrapBehaviorProject /nologo /m /t:Rebuild /p:Configuration=Release /p:Platform=$platform /v:minimal
            $LASTEXITCODE | Should Be 0
            $executable = Join-Path (Split-Path -Parent $bootstrapBehaviorProject) `
                "bin\Release\$platform\BlindSoldier.Bootstrap.Tests.exe"
            Test-Path -LiteralPath $executable -PathType Leaf | Should Be $true
            & $executable
            $LASTEXITCODE | Should Be 0
            $proof = Start-Process -FilePath $executable `
                -ArgumentList '--prove-check-failure' -Wait -PassThru `
                -WindowStyle Hidden
            $proof.ExitCode | Should Not Be 0
        }
    }

    It 'retains installer ownership behavior tests while releases migrate' {
        $msbuild = Get-TestMsBuild
        Test-Path -LiteralPath $installerBehaviorProject -PathType Leaf | Should Be $true
        & $msbuild $installerBehaviorProject /nologo /m /p:Configuration=Release /p:Platform=x64 /v:minimal
        $LASTEXITCODE | Should Be 0
        $executable = Join-Path (Split-Path -Parent $installerBehaviorProject) `
            'bin\Release\x64\BlindSoldier.Installer.Tests.exe'
        & $executable
        $LASTEXITCODE | Should Be 0
    }

    It 'accepts only evidence-backed FFVII host identities' {
        $msbuild = Get-TestMsBuild
        & $msbuild $hostBehaviorProject /nologo /m /p:Configuration=Release /p:Platform=x64 /v:minimal
        $LASTEXITCODE | Should Be 0
        $executable = Join-Path (Split-Path -Parent $hostBehaviorProject) `
            'bin\Release\x64\BlindSoldier.Host.Tests.exe'
        & $executable
        $LASTEXITCODE | Should Be 0
    }

    It 'builds architecture-matched portable brokers' {
        $msbuild = Get-TestMsBuild
        foreach ($platform in @('Win32','x64')) {
            & $msbuild $bootstrapProject /nologo /m /t:Rebuild /p:Configuration=Release /p:Platform=$platform /v:minimal
            $LASTEXITCODE | Should Be 0
        }
        $x86 = Join-Path $bootstrapRoot 'bin\Release\Win32\Blind-Soldier-Bootstrap-x86.exe'
        $x64 = Join-Path $bootstrapRoot 'bin\Release\x64\Blind-Soldier-Bootstrap-x64.exe'
        (Get-TestPeMachine -Path $x86) | Should Be 0x014C
        (Get-TestPeMachine -Path $x64) | Should Be 0x8664
    }

    It 'builds the x86 Version proxy without dormant WinMM forwarding' {
        $msbuild = Get-TestMsBuild
        & $msbuild $versionProxyProject /nologo /m /t:Rebuild `
            /p:Configuration=Release /p:Platform=Win32 /v:minimal
        $LASTEXITCODE | Should Be 0
        $versionProxy = Join-Path (Split-Path -Parent $versionProxyProject) `
            'bin\Release\Win32\version.dll'
        Test-Path -LiteralPath $versionProxy -PathType Leaf | Should Be $true
        (Get-TestPeMachine -Path $versionProxy) | Should Be 0x014C
        $bytes = [IO.File]::ReadAllBytes($versionProxy)
        $ascii = [Text.Encoding]::ASCII.GetString($bytes)
        $unicode = [Text.Encoding]::Unicode.GetString($bytes)
        foreach ($marker in @(
            'GetSystemWow64DirectoryW', 'canonical system WinMM',
            'Canonical SysWOW64 WinMM'
        )) {
            $ascii.IndexOf($marker,
                [StringComparison]::OrdinalIgnoreCase) | Should Be -1
            $unicode.IndexOf($marker,
                [StringComparison]::OrdinalIgnoreCase) | Should Be -1
        }
        $vswhere = Join-Path ${env:ProgramFiles(x86)} `
            'Microsoft Visual Studio\Installer\vswhere.exe'
        $dumpbin = (& $vswhere -latest -products '*' -find `
            'VC\Tools\MSVC\**\bin\Hostx64\x86\dumpbin.exe' |
            Select-Object -First 1)
        Test-Path -LiteralPath $dumpbin -PathType Leaf | Should Be $true
        $dependents = @(& $dumpbin /nologo /dependents $versionProxy)
        $LASTEXITCODE | Should Be 0
        ($dependents -join "`n") | Should Not Match `
            '(?im)^\s*winmm\.dll\s*$'
    }

    It 'registers the Version proxy and retained native compatibility gate' {
        $suite = [IO.File]::ReadAllText($winmmProxyTests)
        $suite | Should Match 'Version APIs while starting the sibling x86 bootstrap'
        $suite | Should Match 'cache-tests'
        $suite | Should Match 'canonical system module without recursion'
        $project = [IO.File]::ReadAllText($versionProxyProject)
        $project | Should Match '<RuntimeLibrary>MultiThreaded</RuntimeLibrary>'
        $project | Should Match '<ModuleDefinitionFile>version\.def</ModuleDefinitionFile>'
        $project | Should Match '<AdditionalOptions>/Brepro %\(AdditionalOptions\)</AdditionalOptions>'
        $project | Should Match 'app_loader_readiness\.cpp'
        $project | Should Match 'version_cache\.cpp'
        $project | Should Match 'BLIND_SOLDIER_NO_WINMM_FORWARDING'
        $suite | Should Match 'BlindSoldier\.VersionForwardingSmoke\.vcxproj'
        $suite | Should Match 'BlindSoldier\.WinMMForwardingSmoke\.vcxproj'
    }
}
