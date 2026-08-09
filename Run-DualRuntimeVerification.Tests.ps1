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

function New-FakePesterModule {
    param(
        [Parameter(Mandatory=$true)] [string] $ModuleRoot,
        [Parameter(Mandatory=$true)] [version] $Version,
        [string] $ImportAfterLoad
    )
    $versionRoot = Join-Path $ModuleRoot (Join-Path 'Pester' $Version.ToString())
    New-Item -ItemType Directory -Path $versionRoot -Force | Out-Null
    $manifestPath = Join-Path $versionRoot 'Pester.psd1'
    $modulePath = Join-Path $versionRoot 'Pester.psm1'
    $manifest = "@{`n" +
        "RootModule = 'Pester.psm1'`n" +
        "ModuleVersion = '$Version'`n" +
        "GUID = '1ad6d69f-7bdb-4ab9-8cde-045ac62e167b'`n" +
        "FunctionsToExport = @('Invoke-Pester')`n" +
        "}`n"
    $module = ''
    if (-not [string]::IsNullOrWhiteSpace($ImportAfterLoad)) {
        $escapedImport = $ImportAfterLoad.Replace("'", "''")
        $module += "Import-Module -Name '$escapedImport' -Force -Global -ErrorAction Stop`n"
    }
    $module += @'
function Invoke-Pester {
    param([string] $Script, [string] $TestName, [switch] $EnableExit)
    if ($EnableExit) { exit 0 }
}
Export-ModuleMember -Function Invoke-Pester
'@
    [IO.File]::WriteAllText($manifestPath, $manifest,
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($modulePath, $module,
        [Text.UTF8Encoding]::new($false))
    return $manifestPath
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
            $expectedPackageVersion = [string]((Get-Content `
                (Join-Path $scriptRoot 'Ff7.Accessibility.Reloaded\ModConfig.json') `
                -Raw | ConvertFrom-Json).ModVersion)
            $build.Arguments[([array]::IndexOf($build.Arguments,'-Version') + 1)] |
                Should Be $expectedPackageVersion
            $verify.Arguments[([array]::IndexOf($verify.Arguments,
                '-ExpectedVersion') + 1)] | Should Be $expectedPackageVersion
            $ghidra = @($invocations | Where-Object {
                $_.Name -ceq 'Ghidra.NativeEvidence'
            })[0]
            $builtArchive = [string]$build.Arguments[([array]::IndexOf(
                $build.Arguments, '-OutputPath') + 1)]
            [string]$ghidra.Arguments[([array]::IndexOf($ghidra.Arguments,
                '-ArchivePath') + 1)] | Should Be $builtArchive
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
        $expectedPackageVersion = [string]((Get-Content `
            (Join-Path $PSScriptRoot `
                'Ff7.Accessibility.Reloaded\ModConfig.json') `
            -Raw | ConvertFrom-Json).ModVersion)
        $escapedPackageVersion = [regex]::Escape($expectedPackageVersion)
        $workflow | Should Match (
            '(?m)^\s*default:\s*["'']' + $escapedPackageVersion +
            '["'']\s*$')

        $assemblyInfoPath = Join-Path $PSScriptRoot `
            'launcher\Ff7.Launcher.Accessible\Properties\AssemblyInfo.cs'
        $assemblyInfo = [IO.File]::ReadAllText($assemblyInfoPath)
        $assemblyInfo | Should Match `
            ([regex]::Escape("blind-soldier.$expectedPackageVersion"))
    }

    It 'passes the selected release version through the inner dual-runtime packager' {
        $portableBuilder = [IO.File]::ReadAllText(
            (Join-Path $PSScriptRoot 'Build-BlindSoldierPortablePackage.ps1'))
        $dualRuntimeBuilder = [IO.File]::ReadAllText(
            (Join-Path $PSScriptRoot 'Build-DualRuntimePackage.ps1'))

        $portableBuilder | Should Match '-ExpectedModVersion\s+\$Version'
        $dualRuntimeBuilder | Should Match '\[string\]\s+\$ExpectedModVersion'
        $dualRuntimeBuilder | Should Not Match "ModVersion\s+-cne\s+'0\.2\.0'"
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
    It 'fails at the first gate when its executable is missing even after a zero exit code' {
        $fixture = New-GateFixture
        $priorPath = $env:PATH
        try {
            $emptyPath = Join-Path $fixture.Root 'empty-path'
            New-Item -ItemType Directory -Path $emptyPath | Out-Null
            $env:PATH = $emptyPath
            $global:LASTEXITCODE = 0
            $message = $null
            try {
                & $verificationPath -TempParent $fixture.Temp `
                    -LogDirectory $fixture.Logs
                throw 'ASSERTION FAILED: missing executable gate completed.'
            }
            catch {
                if ($_.Exception.Message -like 'ASSERTION FAILED:*') { throw }
                $message = $_.Exception.Message
            }
            $message | Should Match "Verification executable 'dotnet' is unavailable"
            [IO.File]::ReadAllText((Join-Path $fixture.Logs `
                'Shared.Tests.log')) | Should Match 'dotnet'
        }
        finally {
            $env:PATH = $priorPath
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
    }

    It 'fails the portable Reloaded loop when its inner dotnet command is missing' {
        $fixture = New-GateFixture
        $priorPath = $env:PATH
        try {
            $systemPowerShell = (Get-Command powershell.exe `
                -CommandType Application -ErrorAction Stop).Source
            $commands = New-Object 'System.Collections.Generic.List[object]'
            $invoker = {
                param($Command)
                $commands.Add($Command)
                [pscustomobject]@{ ExitCode=0; Output=@('ok') }
            }.GetNewClosure()
            & $verificationPath -CommandInvoker $invoker `
                -TempParent $fixture.Temp -LogDirectory $fixture.Logs |
                Out-Null
            $command = @($commands | Where-Object Name -CEQ `
                'Reloaded.Tests')[0]
            $emptyPath = Join-Path $fixture.Root 'empty-path'
            New-Item -ItemType Directory -Path $emptyPath | Out-Null
            $env:PATH = $emptyPath
            $priorPreference = $ErrorActionPreference
            try {
                $ErrorActionPreference = 'Continue'
                $output = @(& $systemPowerShell @($command.Arguments) 2>&1 |
                    ForEach-Object { [string]$_ })
                $exitCode = $LASTEXITCODE
            }
            finally {
                $ErrorActionPreference = $priorPreference
            }
            $exitCode | Should Not Be 0
            ($output -join "`n") | Should Match 'dotnet.*unavailable'
        }
        finally {
            $env:PATH = $priorPath
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
    }

    It 'does not fall back to Pester 3 when pinned Pester 4 is unavailable' {
        $fixture = New-GateFixture
        $priorModulePath = $env:PSModulePath
        try {
            $systemPowerShell = (Get-Command powershell.exe `
                -CommandType Application -ErrorAction Stop).Source
            $commands = New-Object 'System.Collections.Generic.List[object]'
            $invoker = {
                param($Command)
                $commands.Add($Command)
                [pscustomobject]@{ ExitCode=0; Output=@('ok') }
            }.GetNewClosure()
            & $verificationPath -CommandInvoker $invoker `
                -TempParent $fixture.Temp -LogDirectory $fixture.Logs |
                Out-Null
            $command = @($commands | Where-Object Name -CEQ `
                'NativeProxy.Tests')[0]
            $moduleRoot = Join-Path $fixture.Root 'modules'
            New-FakePesterModule -ModuleRoot $moduleRoot `
                -Version ([version]'3.4.0') | Out-Null
            $env:PSModulePath = $moduleRoot
            $priorPreference = $ErrorActionPreference
            try {
                $ErrorActionPreference = 'Continue'
                $output = @(& $systemPowerShell @($command.Arguments) 2>&1 |
                    ForEach-Object { [string]$_ })
                $exitCode = $LASTEXITCODE
            }
            finally {
                $ErrorActionPreference = $priorPreference
            }
            $exitCode | Should Not Be 0
            ($output -join "`n") | Should Match 'Pester 4\.10\.1'
        }
        finally {
            $env:PSModulePath = $priorModulePath
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
    }

    It 'rejects a Pester 4 import that leaves another Pester version loaded' {
        $fixture = New-GateFixture
        $priorModulePath = $env:PSModulePath
        try {
            $systemPowerShell = (Get-Command powershell.exe `
                -CommandType Application -ErrorAction Stop).Source
            $commands = New-Object 'System.Collections.Generic.List[object]'
            $invoker = {
                param($Command)
                $commands.Add($Command)
                [pscustomobject]@{ ExitCode=0; Output=@('ok') }
            }.GetNewClosure()
            & $verificationPath -CommandInvoker $invoker `
                -TempParent $fixture.Temp -LogDirectory $fixture.Logs |
                Out-Null
            $command = @($commands | Where-Object Name -CEQ `
                'NativeProxy.Tests')[0]
            $moduleRoot = Join-Path $fixture.Root 'modules'
            $pester3 = New-FakePesterModule -ModuleRoot $moduleRoot `
                -Version ([version]'3.4.0')
            New-FakePesterModule -ModuleRoot $moduleRoot `
                -Version ([version]'4.10.1') -ImportAfterLoad $pester3 |
                Out-Null
            $env:PSModulePath = $moduleRoot
            $priorPreference = $ErrorActionPreference
            try {
                $ErrorActionPreference = 'Continue'
                $output = @(& $systemPowerShell @($command.Arguments) 2>&1 |
                    ForEach-Object { [string]$_ })
                $exitCode = $LASTEXITCODE
            }
            finally {
                $ErrorActionPreference = $priorPreference
            }
            $exitCode | Should Not Be 0
            ($output -join "`n") | Should Match `
                'exactly Pester 4\.10\.1'
        }
        finally {
            $env:PSModulePath = $priorModulePath
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
    }

    It 'captures native exit codes without a local LASTEXITCODE shadow' {
        $source = [IO.File]::ReadAllText($verificationPath)
        $source | Should Not Match `
            '(?m)^\s*\$LASTEXITCODE\s*=\s*\$null\s*$'
        $source | Should Match `
            '(?m)^\s*\$global:LASTEXITCODE\s*=\s*\$null\s*$'
        $source | Should Match `
            '\$exitCode\s*=\s*\$global:LASTEXITCODE'
    }

}
