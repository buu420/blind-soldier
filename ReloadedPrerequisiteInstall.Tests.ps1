$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$modulePath = Join-Path $scriptRoot 'ReloadedPrerequisiteInstall.psm1'

function New-TestPe {
    param([Parameter(Mandatory=$true)] [string] $Path, [Parameter(Mandatory=$true)] [uint16] $Machine)
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $bytes = New-Object byte[] 512
    $bytes[0] = 0x4D
    $bytes[1] = 0x5A
    [BitConverter]::GetBytes([int]0x80).CopyTo($bytes, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 0x80)
    [BitConverter]::GetBytes($Machine).CopyTo($bytes, 0x84)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function New-PrerequisiteInstallFixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('blind-swordsman-prerequisite-install-test-' + [Guid]::NewGuid().ToString('N'))
    $bundle = Join-Path $root 'bundle'
    $reloadedSource = Join-Path $bundle 'reloaded'
    $hooksSource = Join-Path $bundle 'shared-hooks'
    $dotnetSource = Join-Path $bundle 'dotnet'
    $notices = Join-Path $bundle 'notices'
    New-Item -ItemType Directory -Path $reloadedSource, $hooksSource, $dotnetSource, $notices -Force | Out-Null

    New-TestPe -Path (Join-Path $reloadedSource 'Reloaded-II.exe') -Machine 0x014C
    [IO.File]::WriteAllText((Join-Path $reloadedSource 'version.txt'), '1.30.3')
    New-Item -ItemType Directory -Path (Join-Path $reloadedSource 'Loader\X86'), (Join-Path $reloadedSource 'Loader\X64') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $reloadedSource 'Loader\X86\Reloaded.Mod.Loader.dll'), 'loader x86')
    [IO.File]::WriteAllText((Join-Path $reloadedSource 'Loader\X64\Reloaded.Mod.Loader.dll'), 'loader x64')
    New-TestPe -Path (Join-Path $reloadedSource 'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Machine 0x014C
    New-TestPe -Path (Join-Path $reloadedSource 'Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Machine 0x8664
    New-TestPe -Path (Join-Path $reloadedSource '_asi_extract\ASILoader32.dll') -Machine 0x014C
    New-TestPe -Path (Join-Path $reloadedSource '_asi_extract\ASILoader64.dll') -Machine 0x8664

    New-Item -ItemType Directory -Path (Join-Path $hooksSource 'x86'), (Join-Path $hooksSource 'x64') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $hooksSource 'ModConfig.json'), '{"ModId":"reloaded.sharedlib.hooks","ModVersion":"1.16.3"}')
    New-TestPe -Path (Join-Path $hooksSource 'x86\Reloaded.Hooks.ReloadedII.dll') -Machine 0x014C
    New-TestPe -Path (Join-Path $hooksSource 'x64\Reloaded.Hooks.ReloadedII.dll') -Machine 0x8664

    $runtimeRecords = @()
    foreach ($architecture in @('x86','x64')) {
        $name = "windowsdesktop-runtime-9.0.8-win-$architecture.exe"
        $path = Join-Path $dotnetSource $name
        New-TestPe -Path $path -Machine $(if ($architecture -eq 'x86') { 0x014C } else { 0x8664 })
        $runtimeRecords += [ordered]@{
            architecture = $architecture
            name = $name
            sourceUrl = "https://fixture.invalid/$name"
            sourceSize = (Get-Item $path).Length
            sourceSha256 = (Get-FileHash $path -Algorithm SHA256).Hash
            sourceSha512 = (Get-FileHash $path -Algorithm SHA512).Hash
        }
    }
    foreach ($name in @(
        'THIRD-PARTY-NOTICES.md','Reloaded-II-GPL-3.0.txt','Reloaded-Shared-Hooks-LGPL-3.0.txt',
        'dotnet-LICENSE.txt','dotnet-THIRD-PARTY-NOTICES.txt')) {
        [IO.File]::WriteAllText((Join-Path $notices $name), "fixture $name")
    }
    $manifest = [ordered]@{
        schemaVersion = 1
        reloaded = [ordered]@{
            version='1.30.3'; sourceUrl='https://fixture.invalid/reloaded.zip'; sourceSize=1
            sourceSha256=('A' * 64); sourceCodeUrl='https://fixture.invalid/reloaded/source'
        }
        sharedHooks = [ordered]@{
            version='1.16.3'; sourceUrl='https://fixture.invalid/hooks.7z'; sourceSize=1
            sourceSha256=('B' * 64); sourceCodeUrl='https://fixture.invalid/hooks/source'
        }
        dotnetDesktopRuntime = [ordered]@{
            version='9.0.8'; sourceCodeUrl='https://fixture.invalid/dotnet/source'; installers=$runtimeRecords
        }
    }
    [IO.File]::WriteAllText((Join-Path $bundle 'dependency-bundle.json'), ($manifest | ConvertTo-Json -Depth 8))

    return [pscustomobject]@{
        Root = $root
        Bundle = $bundle
        ReloadedRoot = Join-Path $root 'game\Reloaded-II'
        SettingsPath = Join-Path $root 'roaming\Reloaded-Mod-Loader-II\ReloadedII.json'
    }
}

function New-RuntimeHarness {
    param([string[]] $InitiallyInstalled = @())
    $state = @{ x86 = $false; x64 = $false }
    foreach ($architecture in $InitiallyInstalled) { $state[$architecture] = $true }
    $installs = New-Object 'System.Collections.Generic.List[string]'
    $probe = {
        param($architecture, $minimumVersion)
        return [bool]$state[[string]$architecture]
    }.GetNewClosure()
    $installer = {
        param($architecture, $installerPath)
        $installs.Add([string]$architecture)
        $state[[string]$architecture] = $true
        return 0
    }.GetNewClosure()
    return [pscustomobject]@{ Probe=$probe; Installer=$installer; Installs=$installs }
}

Describe 'Reloaded prerequisite installation' {
    BeforeAll {
        Import-Module $modulePath -Force
    }

    BeforeEach {
        $fixture = New-PrerequisiteInstallFixture
    }

    AfterEach {
        if ($null -ne $fixture -and (Test-Path -LiteralPath $fixture.Root)) {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
    }

    It 'provisions a fresh x86 install and only installs the required desktop runtime' {
        $runtime = New-RuntimeHarness
        $result = Install-BlindSwordsmanReloadedPrerequisites -BundlePath $fixture.Bundle `
            -ReloadedRoot $fixture.ReloadedRoot -RequiredArchitectures @('x86') -SettingsPath $fixture.SettingsPath `
            -RuntimeProbe $runtime.Probe -RuntimeInstaller $runtime.Installer

        Test-Path -LiteralPath (Join-Path $fixture.ReloadedRoot 'Reloaded-II.exe') -PathType Leaf | Should Be $true
        Test-Path -LiteralPath (Join-Path $fixture.ReloadedRoot 'Loader\X86\Reloaded.Mod.Loader.dll') -PathType Leaf | Should Be $true
        Test-Path -LiteralPath (Join-Path $fixture.ReloadedRoot 'Mods\reloaded.sharedlib.hooks\x86\Reloaded.Hooks.ReloadedII.dll') -PathType Leaf | Should Be $true
        @($runtime.Installs) | Should Be @('x86')
        @($result.InstalledDotNetArchitectures) | Should Be @('x86')
        $settings = [IO.File]::ReadAllText($fixture.SettingsPath) | ConvertFrom-Json
        $settings.LoaderPath32 | Should Be ([IO.Path]::GetFullPath((Join-Path $fixture.ReloadedRoot 'Loader\X86\Reloaded.Mod.Loader.dll')))
        $settings.LauncherPath | Should Be ([IO.Path]::GetFullPath((Join-Path $fixture.ReloadedRoot 'Reloaded-II.exe')))
    }

    It 'supports fresh x64-only and dual-runtime installations' {
        $runtime = New-RuntimeHarness
        $result = Install-BlindSwordsmanReloadedPrerequisites -BundlePath $fixture.Bundle `
            -ReloadedRoot $fixture.ReloadedRoot -RequiredArchitectures @('x64') -SettingsPath $fixture.SettingsPath `
            -RuntimeProbe $runtime.Probe -RuntimeInstaller $runtime.Installer
        @($runtime.Installs) | Should Be @('x64')
        Test-Path -LiteralPath (Join-Path $fixture.ReloadedRoot '_asi_extract\ASILoader64.dll') -PathType Leaf | Should Be $true

        $dualFixture = New-PrerequisiteInstallFixture
        try {
            $dualRuntime = New-RuntimeHarness
            $dual = Install-BlindSwordsmanReloadedPrerequisites -BundlePath $dualFixture.Bundle `
                -ReloadedRoot $dualFixture.ReloadedRoot -RequiredArchitectures @('x86','x64') -SettingsPath $dualFixture.SettingsPath `
                -RuntimeProbe $dualRuntime.Probe -RuntimeInstaller $dualRuntime.Installer
            @($dualRuntime.Installs) | Should Be @('x86','x64')
            @($dual.InstalledDotNetArchitectures) | Should Be @('x86','x64')
        }
        finally {
            if (Test-Path -LiteralPath $dualFixture.Root) { Remove-Item -LiteralPath $dualFixture.Root -Recurse -Force }
        }
    }

    It 'is idempotent and preserves existing Reloaded preferences and user content' {
        $runtime = New-RuntimeHarness -InitiallyInstalled @('x86')
        New-Item -ItemType Directory -Path (Join-Path $fixture.ReloadedRoot 'User\Mods') -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $fixture.ReloadedRoot 'User\Mods\keep.txt'), 'keep me')
        New-Item -ItemType Directory -Path (Split-Path -Parent $fixture.SettingsPath) -Force | Out-Null
        [IO.File]::WriteAllText($fixture.SettingsPath, '{"ShowConsole":true,"LanguageFile":"custom.xaml","UnknownPreference":17}')

        Install-BlindSwordsmanReloadedPrerequisites -BundlePath $fixture.Bundle -ReloadedRoot $fixture.ReloadedRoot `
            -RequiredArchitectures @('x86') -SettingsPath $fixture.SettingsPath `
            -RuntimeProbe $runtime.Probe -RuntimeInstaller $runtime.Installer | Out-Null
        Install-BlindSwordsmanReloadedPrerequisites -BundlePath $fixture.Bundle -ReloadedRoot $fixture.ReloadedRoot `
            -RequiredArchitectures @('x86') -SettingsPath $fixture.SettingsPath `
            -RuntimeProbe $runtime.Probe -RuntimeInstaller $runtime.Installer | Out-Null

        @($runtime.Installs).Count | Should Be 0
        [IO.File]::ReadAllText((Join-Path $fixture.ReloadedRoot 'User\Mods\keep.txt')) | Should Be 'keep me'
        $settings = [IO.File]::ReadAllText($fixture.SettingsPath) | ConvertFrom-Json
        $settings.ShowConsole | Should Be $true
        $settings.LanguageFile | Should Be 'custom.xaml'
        $settings.UnknownPreference | Should Be 17
    }

    It 'refuses an unrelated Shared Hooks ModId before changing Reloaded files' {
        $runtime = New-RuntimeHarness -InitiallyInstalled @('x86')
        New-Item -ItemType Directory -Path (Join-Path $fixture.ReloadedRoot 'Mods\reloaded.sharedlib.hooks') -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $fixture.ReloadedRoot 'Mods\reloaded.sharedlib.hooks\ModConfig.json'), '{"ModId":"someone.elses.mod"}')
        [IO.File]::WriteAllText((Join-Path $fixture.ReloadedRoot 'existing.txt'), 'unchanged')

        { Install-BlindSwordsmanReloadedPrerequisites -BundlePath $fixture.Bundle -ReloadedRoot $fixture.ReloadedRoot `
                -RequiredArchitectures @('x86') -SettingsPath $fixture.SettingsPath `
                -RuntimeProbe $runtime.Probe -RuntimeInstaller $runtime.Installer } | Should Throw
        [IO.File]::ReadAllText((Join-Path $fixture.ReloadedRoot 'existing.txt')) | Should Be 'unchanged'
        Test-Path -LiteralPath (Join-Path $fixture.ReloadedRoot 'Reloaded-II.exe') | Should Be $false
    }

    It 'refuses a reparse-point Reloaded target before changing its destination' {
        $runtime = New-RuntimeHarness -InitiallyInstalled @('x86')
        $realTarget = Join-Path $fixture.Root 'real-reloaded-target'
        New-Item -ItemType Directory -Path $realTarget -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $realTarget 'keep.txt'), 'untouched')
        New-Item -ItemType Directory -Path (Split-Path -Parent $fixture.ReloadedRoot) -Force | Out-Null
        New-Item -ItemType Junction -Path $fixture.ReloadedRoot -Target $realTarget | Out-Null

        { Install-BlindSwordsmanReloadedPrerequisites -BundlePath $fixture.Bundle -ReloadedRoot $fixture.ReloadedRoot `
                -RequiredArchitectures @('x86') -SettingsPath $fixture.SettingsPath `
                -RuntimeProbe $runtime.Probe -RuntimeInstaller $runtime.Installer } | Should Throw
        [IO.File]::ReadAllText((Join-Path $realTarget 'keep.txt')) | Should Be 'untouched'
        Test-Path -LiteralPath (Join-Path $realTarget 'Reloaded-II.exe') | Should Be $false
    }

    It 'rolls back overwritten and newly created files after a forced overlay failure' {
        $runtime = New-RuntimeHarness -InitiallyInstalled @('x86')
        New-Item -ItemType Directory -Path $fixture.ReloadedRoot -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $fixture.ReloadedRoot 'Reloaded-II.exe'), 'original bytes')
        $writeState = @{ Count = 0 }
        $failingWriter = {
            param($source, $temporaryTarget)
            $writeState.Count++
            if ($writeState.Count -eq 3) { throw 'controlled overlay failure' }
            Copy-Item -LiteralPath $source -Destination $temporaryTarget
        }.GetNewClosure()

        { Install-BlindSwordsmanReloadedPrerequisites -BundlePath $fixture.Bundle -ReloadedRoot $fixture.ReloadedRoot `
                -RequiredArchitectures @('x86') -SettingsPath $fixture.SettingsPath `
                -RuntimeProbe $runtime.Probe -RuntimeInstaller $runtime.Installer -FileWriter $failingWriter } |
            Should Throw 'controlled overlay failure'
        [IO.File]::ReadAllText((Join-Path $fixture.ReloadedRoot 'Reloaded-II.exe')) | Should Be 'original bytes'
        Test-Path -LiteralPath (Join-Path $fixture.ReloadedRoot 'version.txt') | Should Be $false
        Test-Path -LiteralPath $fixture.SettingsPath | Should Be $false
    }

    It 'skips an already present desktop runtime and validates bundle digests' {
        $runtime = New-RuntimeHarness -InitiallyInstalled @('x64')
        Install-BlindSwordsmanReloadedPrerequisites -BundlePath $fixture.Bundle -ReloadedRoot $fixture.ReloadedRoot `
            -RequiredArchitectures @('x64') -SettingsPath $fixture.SettingsPath `
            -RuntimeProbe $runtime.Probe -RuntimeInstaller $runtime.Installer | Out-Null
        @($runtime.Installs).Count | Should Be 0

        $installer = Join-Path $fixture.Bundle 'dotnet\windowsdesktop-runtime-9.0.8-win-x64.exe'
        [IO.File]::WriteAllText($installer, 'tampered')
        { Assert-BlindSwordsmanPrerequisiteBundle -Path $fixture.Bundle } | Should Throw
    }

    It 'rejects a failed or unverifiable desktop runtime installation before overlaying files' {
        $missingProbe = { param($architecture, $minimumVersion) return $false }
        $failedInstaller = { param($architecture, $installerPath) return 17 }
        { Install-BlindSwordsmanReloadedPrerequisites -BundlePath $fixture.Bundle -ReloadedRoot $fixture.ReloadedRoot `
                -RequiredArchitectures @('x86') -SettingsPath $fixture.SettingsPath `
                -RuntimeProbe $missingProbe -RuntimeInstaller $failedInstaller } | Should Throw 'exit code 17'
        Test-Path -LiteralPath $fixture.ReloadedRoot | Should Be $false

        $apparentlySuccessful = { param($architecture, $installerPath) return 3010 }
        { Install-BlindSwordsmanReloadedPrerequisites -BundlePath $fixture.Bundle -ReloadedRoot $fixture.ReloadedRoot `
                -RequiredArchitectures @('x86') -SettingsPath $fixture.SettingsPath `
                -RuntimeProbe $missingProbe -RuntimeInstaller $apparentlySuccessful } | Should Throw 'not detected after installation'
        Test-Path -LiteralPath $fixture.ReloadedRoot | Should Be $false
    }
}
