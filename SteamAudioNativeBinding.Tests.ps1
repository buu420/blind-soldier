$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourcePath = Join-Path $scriptRoot 'Ff7.Accessibility.Reloaded\SteamAudioNative.cs'
$playerPath = Join-Path $scriptRoot 'Ff7.Accessibility.Reloaded\NavigationBeaconPlayer.cs'
$packageRoot = if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
    Join-Path $env:USERPROFILE '.nuget\packages'
}
else {
    $env:NUGET_PACKAGES
}
$nativePackageRoot = Join-Path $packageRoot 'steamaudio.net.natives\4.6.1\runtimes'

function Get-DumpBinPath {
    $command = Get-Command dumpbin.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
        throw 'dumpbin.exe was not found and vswhere.exe is unavailable.'
    }

    $candidate = & $vswhere -latest -products '*' -find 'VC\Tools\MSVC\**\bin\Hostx64\x64\dumpbin.exe' |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        throw 'dumpbin.exe was not found in the latest Visual Studio installation.'
    }

    return $candidate
}

function Get-NativeExports {
    param(
        [Parameter(Mandatory=$true)] [string] $DumpBinPath,
        [Parameter(Mandatory=$true)] [string] $LibraryPath
    )

    if (-not (Test-Path -LiteralPath $LibraryPath -PathType Leaf)) {
        throw "Steam Audio native library not found: $LibraryPath"
    }

    return (& $DumpBinPath /nologo /exports $LibraryPath 2>&1 | Out-String)
}

$entryPoints = [ordered]@{
    ContextCreate = @('_iplContextCreate@8', 'iplContextCreate')
    ContextRelease = @('_iplContextRelease@4', 'iplContextRelease')
    HrtfCreate = @('_iplHRTFCreate@16', 'iplHRTFCreate')
    HrtfRelease = @('_iplHRTFRelease@4', 'iplHRTFRelease')
    BinauralEffectCreate = @('_iplBinauralEffectCreate@16', 'iplBinauralEffectCreate')
    BinauralEffectRelease = @('_iplBinauralEffectRelease@4', 'iplBinauralEffectRelease')
    BinauralEffectReset = @('_iplBinauralEffectReset@4', 'iplBinauralEffectReset')
    BinauralEffectApply = @('_iplBinauralEffectApply@16', 'iplBinauralEffectApply')
    AudioBufferAllocate = @('_iplAudioBufferAllocate@16', 'iplAudioBufferAllocate')
    AudioBufferFree = @('_iplAudioBufferFree@8', 'iplAudioBufferFree')
    AudioBufferInterleave = @('_iplAudioBufferInterleave@12', 'iplAudioBufferInterleave')
}

Describe 'Steam Audio native binding contract' {
    BeforeAll {
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Shared Steam Audio binding source is missing: $sourcePath"
        }

        $source = [IO.File]::ReadAllText($sourcePath)
        $playerSource = [IO.File]::ReadAllText($playerPath)
        $dumpBinPath = Get-DumpBinPath
        $x86Exports = Get-NativeExports -DumpBinPath $dumpBinPath `
            -LibraryPath (Join-Path $nativePackageRoot 'win-x86\native\phonon.dll')
        $x64Exports = Get-NativeExports -DumpBinPath $dumpBinPath `
            -LibraryPath (Join-Path $nativePackageRoot 'win-x64\native\phonon.dll')
    }

    It 'selects the native ABI from the process pointer size' {
        $source | Should Match 'IntPtr\.Size\s*==\s*8'
        $source | Should Match 'IntPtr\.Size\s*==\s*4'
    }

    It 'declares every required decorated x86 and undecorated x64 entry point' {
        foreach ($function in $entryPoints.Keys) {
            $x86Name = [regex]::Escape($entryPoints[$function][0])
            $x64Name = [regex]::Escape($entryPoints[$function][1])
            $source | Should Match "EntryPoint\s*=\s*`"$x86Name`""
            $source | Should Match "EntryPoint\s*=\s*`"$x64Name`""
        }
    }

    It 'matches the exports in both packaged native libraries' {
        foreach ($function in $entryPoints.Keys) {
            $x86Exports | Should Match ("\b" + [regex]::Escape($entryPoints[$function][0]) + "\b")
            $x64Exports | Should Match ("\b" + [regex]::Escape($entryPoints[$function][1]) + "\b")
        }
    }

    It 'keeps HRTF failure silent instead of introducing a pan fallback' {
        $playerSource | Should Match 'Steam Audio HRTF backend is unavailable; no fake pan fallback will be used\.'
        $playerSource | Should Not Match 'PanSampleProvider'
    }
}
