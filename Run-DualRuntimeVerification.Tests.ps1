$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$verificationPath = Join-Path $scriptRoot 'Run-DualRuntimeVerification.ps1'

function New-GateFixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) `
        ('blind-soldier-gate-tests-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root | Out-Null
    [pscustomobject]@{
        Root = $root
        Temp = Join-Path $root 'temp'
        Logs = Join-Path $root 'logs'
    }
}

Describe 'Blind Soldier aggregate portable release gate' {
    It 'passes an explicit licensed game-data runtime only to the full Reloaded suite' {
        $fixture = New-GateFixture
        try {
            $runtime = Join-Path $fixture.Root 'runtime'
            New-Item -ItemType Directory -Path (Join-Path $runtime 'data') `
                -Force | Out-Null
            [IO.File]::WriteAllBytes((Join-Path $runtime 'ff7_en.exe'),
                [byte[]](0x4D,0x5A))
            $commands = New-Object 'System.Collections.Generic.List[object]'
            $invoker = {
                param($Command)
                $commands.Add($Command)
                [pscustomobject]@{ ExitCode=0; Output=@('ok') }
            }.GetNewClosure()
            $result = & $verificationPath -GameRuntimePath $runtime `
                -CommandInvoker $invoker -TempParent $fixture.Temp `
                -LogDirectory $fixture.Logs
            $reloaded = @($commands | Where-Object Name -ceq 'Reloaded.Tests')
            $reloaded.Count | Should Be 1
            $reloaded[0].FilePath | Should Be 'dotnet'
            $reloaded[0].Environment.FF7_ACCESSIBILITY_RUNTIME |
                Should Be ([IO.Path]::GetFullPath($runtime))
            $result.GameDataIntegrationRan | Should Be $true
        }
        finally { Remove-Item -LiteralPath $fixture.Root -Recurse -Force }
    }

    It 'runs every accessibility-critical gate in exact order and records logs' {
        $fixture = New-GateFixture
        try {
            $invocations = New-Object 'System.Collections.Generic.List[object]'
            $invoker = {
                param($Command)
                $invocations.Add($Command)
                [pscustomobject]@{
                    ExitCode = 0
                    Output = @("controlled output for $($Command.Name)")
                }
            }.GetNewClosure()
            $result = & $verificationPath -CommandInvoker $invoker `
                -TempParent $fixture.Temp -LogDirectory $fixture.Logs
            $reloadedPortable = @($invocations | Where-Object Name -CEQ `
                'Reloaded.Tests')[0]
            $reloadedCommand = [string]$reloadedPortable.Arguments[-1]
            foreach ($mode in @('--runtime-lease-only',
                    '--host-validation-only','--7h-compatibility-only')) {
                $reloadedCommand | Should Match ([regex]::Escape($mode))
            }
            $build = @($invocations | Where-Object Name -CEQ `
                'PortablePackage.Build')[0]
            $verify = @($invocations | Where-Object Name -CEQ `
                'PortablePackage.Verify')[0]
            $build.Arguments[([array]::IndexOf($build.Arguments,'-Version') + 1)] |
                Should Be '0.1.6'
            $verify.Arguments[([array]::IndexOf($verify.Arguments,
                '-ExpectedVersion') + 1)] | Should Be '0.1.6'
            @($invocations.Name) | Should Be @(
                'Shared.Tests','Reloaded.Tests','Steam2026X64.Tests',
                'Parity.Tests','AccessibleLauncher.Tests',
                'VerificationGate.Tests','AccessibleLauncherBundle.Tests',
                'NativeHost.Tests','Bootstrap.Tests x86/x64',
                'NativeProxy.Tests','PortableDotNetRuntime.Tests',
                'PortablePackage.Tests','PortablePackage.Build',
                'PortablePackage.Verify','Ghidra.NativeEvidence')
            $result.VerificationSucceeded | Should Be $true
            $result.Mode | Should Be 'Research'
            $result.Steps.Count | Should Be 15
            $result.PackageStagingCleaned | Should Be $true
            $result.PortableArchiveSha256 | Should Be $null
            foreach ($step in $result.Steps) {
                Test-Path -LiteralPath $step.LogPath -PathType Leaf |
                    Should Be $true
                [IO.File]::ReadAllText($step.LogPath) |
                    Should Match 'controlled output'
            }
            @(Get-ChildItem -LiteralPath $fixture.Temp -Force).Count |
                Should Be 0
        }
        finally { Remove-Item -LiteralPath $fixture.Root -Recurse -Force }
    }

    It 'stops on the first nonzero gate and reports its exact log path' {
        $fixture = New-GateFixture
        try {
            $names = New-Object 'System.Collections.Generic.List[string]'
            $invoker = {
                param($Command)
                $names.Add([string]$Command.Name)
                if ($Command.Name -ceq 'Bootstrap.Tests x86/x64') {
                    return [pscustomobject]@{
                        ExitCode = 19; Output = @('controlled native failure')
                    }
                }
                [pscustomobject]@{ ExitCode = 0; Output = @('ok') }
            }.GetNewClosure()
            $expectedLog = Join-Path $fixture.Logs `
                'Bootstrap.Tests_x86_x64.log'
            { & $verificationPath -CommandInvoker $invoker `
                -TempParent $fixture.Temp -LogDirectory $fixture.Logs } |
                Should Throw "Verification step 'Bootstrap.Tests x86/x64' failed with exit code 19. Log: $expectedLog"
            @($names)[-1] | Should Be 'Bootstrap.Tests x86/x64'
            @($names) -contains 'NativeProxy.Tests' | Should Be $false
            [IO.File]::ReadAllText($expectedLog) |
                Should Match 'controlled native failure'
            @(Get-ChildItem -LiteralPath $fixture.Temp -Force).Count |
                Should Be 0
        }
        finally { Remove-Item -LiteralPath $fixture.Root -Recurse -Force }
    }

    It 'closes strict Release mode before executing commands' {
        $fixture = New-GateFixture
        try {
            $count = 0
            $invoker = { param($Command) $script:count++; return @{ExitCode=0} }
            { & $verificationPath -Mode Release -CommandInvoker $invoker `
                -TempParent $fixture.Temp -LogDirectory $fixture.Logs } |
                Should Throw 'Native Steam 2026 release gate is closed'
            $count | Should Be 0
        }
        finally { Remove-Item -LiteralPath $fixture.Root -Recurse -Force }
    }

    It 'resolves its parity matrix in a fresh PowerShell invocation' {
        $priorPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $output = @(& powershell.exe -NoProfile -NonInteractive `
                -ExecutionPolicy Bypass -File $verificationPath `
                -Mode Release 2>&1)
            $exitCode = $LASTEXITCODE
        }
        finally { $ErrorActionPreference = $priorPreference }
        $exitCode | Should Not Be 0
        ($output -join "`n") | Should Match `
            'Native Steam 2026 release gate is closed'
        ($output -join "`n") | Should Not Match 'Join-Path'
    }

    It 'contains no deployment registry installer or process launch mutation' {
        $content = [IO.File]::ReadAllText($verificationPath)
        foreach ($forbidden in @(
            'Start-Process','Install-FF7ReloadedMod\.ps1',
            'Install-Ff7DualRuntimePackage','Install-Ff7NativeReloadedProfile',
            'Update-SeventhHeavenSettings','RegSetValue','Image File Execution Options')) {
            $content | Should Not Match $forbidden
        }
        foreach ($required in @(
            'AccessibleLauncher.Tests','AccessibleLauncherBundle.Tests',
            'NativeHost.Tests','Bootstrap.Tests x86/x64','NativeProxy.Tests',
            'PortableDotNetRuntime.Tests','PortablePackage.Tests',
            'PortablePackage.Verify','Ghidra.NativeEvidence')) {
            $content | Should Match ([regex]::Escape($required))
        }
    }

    It 'provisions both dotnet architectures for the tagged release gate' {
        $workflowPath = Join-Path $PSScriptRoot `
            '.github\workflows\release.yml'
        $workflow = [IO.File]::ReadAllText($workflowPath)
        $workflow | Should Match '(?m)^\s*architecture:\s*x86\s*$'
        $workflow | Should Match '(?m)^\s*8\.0\.x\s*$'
        $workflow | Should Match '(?m)^\s*9\.0\.x\s*$'
        $workflow | Should Match 'DOTNET_ROOT_X86'
        $workflow | Should Match 'DOTNET_ROOT_X64'
        $workflow | Should Match 'dotnet-x86'
        $workflow | Should Match `
            '\$x86DotNetRoot\s*=\s*\$env:DOTNET_ROOT'
        $workflow | Should Match `
            'DOTNET_ROOT_X86\s*=\s*\$x86DotNetRoot'
        $workflow | Should Match '(?m)^\s*workflow_dispatch:\s*$'
        $workflow | Should Match "(?m)^\s*if:\s*github\.ref_type == 'tag'\s*$"
        $workflow | Should Match '\$\{\{ inputs\.version \}\}'
        $workflow | Should Match `
            '(?m)^\s*default:\s*["'']0\.1\.6["'']\s*$'

        $assemblyInfoPath = Join-Path $PSScriptRoot `
            'launcher\Ff7.Launcher.Accessible\Properties\AssemblyInfo.cs'
        $assemblyInfo = [IO.File]::ReadAllText($assemblyInfoPath)
        $assemblyInfo | Should Match `
            'blind-soldier\.0\.1\.6'
    }

    It 'keeps supported-host validation independent of developer-local game files' {
        $programPath = Join-Path $PSScriptRoot `
            'Ff7.Accessibility.Reloaded.Tests\Program.cs'
        $program = [IO.File]::ReadAllText($programPath)

        $program | Should Not Match 'C:\\Users\\buu42'
        $program | Should Match 'FF7_ACCESSIBILITY_STOCK_X86_HOST'
        $program | Should Match 'FF7_ACCESSIBILITY_CONVERTED_X86_HOST'
        $program | Should Match 'FF7_ACCESSIBILITY_NATIVE_X64_HOST'
    }

    It 'pins the legacy Pester contract used by the release verification scripts' {
        $workflowPath = Join-Path $PSScriptRoot `
            '.github\workflows\release.yml'
        $workflow = [IO.File]::ReadAllText($workflowPath)
        $verification = [IO.File]::ReadAllText($verificationPath)

        $workflow | Should Match `
            'Install-PackageProvider\s+-Name\s+NuGet'
        $workflow | Should Match `
            'Install-Module\s+-Name\s+Pester\s+-RequiredVersion\s+4\.10\.1'
        $verification | Should Match `
            'Import-Module\s+Pester\s+-RequiredVersion\s+4\.10\.1\s+-Force'
    }
}
