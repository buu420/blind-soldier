$ErrorActionPreference = 'Stop'

$nativeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$commonPath = Join-Path $nativeRoot 'BlindSoldier.Common\common.h'
$installerSource = Join-Path $nativeRoot 'BlindSoldier.Installer\installer.cpp'
$installerProject = Join-Path $nativeRoot 'BlindSoldier.Installer\BlindSoldier.Installer.vcxproj'
$launcherSource = Join-Path $nativeRoot 'BlindSoldier.Launcher\launcher.cpp'
$launcherProject = Join-Path $nativeRoot 'BlindSoldier.Launcher\BlindSoldier.Launcher.vcxproj'
$launcherBehaviorProject = Join-Path $nativeRoot 'BlindSoldier.Launcher.Tests\BlindSoldier.Launcher.Tests.vcxproj'
$installerBehaviorProject = Join-Path $nativeRoot 'BlindSoldier.Installer.Tests\BlindSoldier.Installer.Tests.vcxproj'
$hostBehaviorProject = Join-Path $nativeRoot 'BlindSoldier.Host.Tests\BlindSoldier.Host.Tests.vcxproj'

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

Describe 'Blind Soldier preserved native installer workflow' {
    It 'contains the adapted common, installer, launcher, and projects' {
        foreach ($path in @($commonPath, $installerSource, $installerProject, $launcherSource, $launcherProject)) {
            Test-Path -LiteralPath $path -PathType Leaf | Should Be $true
        }
    }

    It 'maps both FFVII executable names to architecture-matched launchers' {
        Test-Path -LiteralPath $installerSource -PathType Leaf | Should Be $true
        $source = [IO.File]::ReadAllText($installerSource)
        $source | Should Match 'Blind Soldier Accessibility Mod Installer'
        $source | Should Match 'FFVII\.exe'
        $source | Should Match 'ff7_en\.exe'
        $source | Should Match 'Blind-Soldier-Launcher-x64\.exe'
        $source | Should Match 'Blind-Soldier-Launcher-x86\.exe'
        $source | Should Match 'SetIFEODebugger'
        $source | Should Match 'RemoveIFEODebugger'
        $source | Should Match '/uninstall'
    }

    It 'retains suspended bootstrapper injection and enables Shared Hooks first' {
        Test-Path -LiteralPath $launcherSource -PathType Leaf | Should Be $true
        Test-Path -LiteralPath $commonPath -PathType Leaf | Should Be $true
        $launcher = [IO.File]::ReadAllText($launcherSource)
        $common = [IO.File]::ReadAllText($commonPath)
        $launcher | Should Match 'DEBUG_ONLY_THIS_PROCESS\s*\|\s*CREATE_SUSPENDED'
        $launcher | Should Match 'CreateRemoteThread'
        $launcher | Should Match 'LoadLibraryW'
        $launcher | Should Match 'ResumeThread'
        $launcher | Should Match '#if(?:def)?\s+_WIN64'
        $common | Should Match 'reloaded\.sharedlib\.hooks'
        $common | Should Match 'ff7\.accessibility\.reloaded'
        $hooksPosition = $common.IndexOf('reloaded.sharedlib.hooks', [StringComparison]::Ordinal)
        $modPosition = $common.IndexOf('ff7.accessibility.reloaded', [StringComparison]::Ordinal)
        ($hooksPosition -ge 0 -and $modPosition -gt $hooksPosition) | Should Be $true
    }

    It 'fails closed when Reloaded configuration cannot be written and serializes pointer swaps' {
        $launcher = [IO.File]::ReadAllText($launcherSource)
        $common = [IO.File]::ReadAllText($commonPath)

        $launcher | Should Match 'CreateMutexW'
        $launcher | Should Match 'WAIT_ABANDONED'
        $launcher | Should Match 'MoveFileExW'
        $launcher | Should Match 'if\s*\(\s*!WriteAppConfig\('
        $launcher | Should Match 'if\s*\(\s*!swap\.Ready\(\)\s*\)'
        $common | Should Match 'WriteUtf8FileAtomic'
    }

    It 'resolves the injection entry point in the remote module instead of reusing a local address' {
        $launcher = [IO.File]::ReadAllText($launcherSource)

        $launcher | Should Match 'CreateToolhelp32Snapshot'
        $launcher | Should Match 'Module32FirstW'
        $launcher | Should Match 'GetModuleHandleExW'
        $launcher | Should Match 'remoteModuleBase'
        $launcher | Should Not Match 'reinterpret_cast<LPTHREAD_START_ROUTINE>\(loadLibrary\)'
    }

    It 'checks compatible architecture-matched .NET desktop runtimes before registration' {
        $installer = [IO.File]::ReadAllText($installerSource)

        $installer | Should Match 'Microsoft\.WindowsDesktop\.App'
        $installer | Should Match 'HasCompatibleDesktopRuntime'
        $installer | Should Match '9\.0\.8'
        $installer | Should Match 'DOTNET_ROOT_X86'
    }

    It 'owns only its exact IFEO debugger value and refuses destructive conflicts' {
        $installer = [IO.File]::ReadAllText($installerSource)

        $installer | Should Match 'BlindSoldierDebuggerOwner'
        $installer | Should Match 'RegQueryValueExW'
        $installer | Should Match 'ERROR_ALREADY_ASSIGNED'
        $installer | Should Match 'currentDebugger\s*!=\s*expectedDebugger'
        $installer | Should Match 'currentOwner\s*!=\s*expectedDebugger'
    }

    It 'keeps administrator elevation and Win32 plus x64 launcher configurations' {
        Test-Path -LiteralPath $installerProject -PathType Leaf | Should Be $true
        Test-Path -LiteralPath $launcherProject -PathType Leaf | Should Be $true
        $installer = [IO.File]::ReadAllText($installerProject)
        $launcher = [IO.File]::ReadAllText($launcherProject)
        $installer | Should Match '<UACExecutionLevel>RequireAdministrator</UACExecutionLevel>'
        $launcher | Should Match 'Release\|Win32'
        $launcher | Should Match 'Release\|x64'
        $launcher | Should Match '<RuntimeLibrary>MultiThreaded</RuntimeLibrary>'
    }

    It 'passes native failure, ownership, runtime, restore, and concurrency behavior tests' {
        $msbuild = Get-TestMsBuild
        foreach ($project in @($launcherBehaviorProject, $installerBehaviorProject)) {
            Test-Path -LiteralPath $project -PathType Leaf | Should Be $true
            & $msbuild $project /nologo /m /p:Configuration=Release /p:Platform=x64 /v:minimal
            $LASTEXITCODE | Should Be 0
            $executable = Join-Path (Split-Path -Parent $project) `
                ('bin\Release\x64\' + [IO.Path]::GetFileNameWithoutExtension($project) + '.exe')
            Test-Path -LiteralPath $executable -PathType Leaf | Should Be $true
            & $executable
            $LASTEXITCODE | Should Be 0
            $proof = Start-Process -FilePath $executable `
                -ArgumentList '--prove-check-failure' -Wait -PassThru `
                -WindowStyle Hidden
            $proof.ExitCode | Should Not Be 0
        }
    }

    It 'accepts only evidence-backed FFVII host identities' {
        $msbuild = Get-TestMsBuild
        Test-Path -LiteralPath $hostBehaviorProject -PathType Leaf | Should Be $true
        & $msbuild $hostBehaviorProject /nologo /m /p:Configuration=Release /p:Platform=x64 /v:minimal
        $LASTEXITCODE | Should Be 0
        $executable = Join-Path (Split-Path -Parent $hostBehaviorProject) `
            'bin\Release\x64\BlindSoldier.Host.Tests.exe'
        Test-Path -LiteralPath $executable -PathType Leaf | Should Be $true
        & $executable
        $LASTEXITCODE | Should Be 0
    }

    It 'builds an x64 installer and architecture-matched launchers' {
        foreach ($path in @($installerProject, $launcherProject)) {
            Test-Path -LiteralPath $path -PathType Leaf | Should Be $true
        }
        $msbuild = Get-TestMsBuild
        & $msbuild $installerProject /nologo /m /p:Configuration=Release /p:Platform=x64 /v:minimal
        $LASTEXITCODE | Should Be 0
        & $msbuild $launcherProject /nologo /m /p:Configuration=Release /p:Platform=Win32 /v:minimal
        $LASTEXITCODE | Should Be 0
        & $msbuild $launcherProject /nologo /m /p:Configuration=Release /p:Platform=x64 /v:minimal
        $LASTEXITCODE | Should Be 0

        $installer = Join-Path $nativeRoot 'BlindSoldier.Installer\bin\Release\x64\Blind-Soldier-Installer.exe'
        $launcherX86 = Join-Path $nativeRoot 'BlindSoldier.Launcher\bin\Release\Win32\Blind-Soldier-Launcher-x86.exe'
        $launcherX64 = Join-Path $nativeRoot 'BlindSoldier.Launcher\bin\Release\x64\Blind-Soldier-Launcher-x64.exe'
        foreach ($path in @($installer, $launcherX86, $launcherX64)) {
            Test-Path -LiteralPath $path -PathType Leaf | Should Be $true
        }
        (Get-TestPeMachine -Path $installer) | Should Be 0x8664
        (Get-TestPeMachine -Path $launcherX86) | Should Be 0x014C
        (Get-TestPeMachine -Path $launcherX64) | Should Be 0x8664
    }
}
