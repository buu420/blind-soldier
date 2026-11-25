$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$modulePath = Join-Path $scriptRoot 'FF7Launcher2013Install.psm1'
$bundleBuilder = Join-Path $scriptRoot 'Build-Accessible2013LauncherBundle.ps1'

function New-MinimalX86Pe {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [byte] $Marker = 0
    )
    $bytes = New-Object byte[] 512
    $bytes[0] = 0x4D
    $bytes[1] = 0x5A
    [BitConverter]::GetBytes([int]0x80).CopyTo($bytes, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 0x80)
    [BitConverter]::GetBytes([uint16]0x014C).CopyTo($bytes, 0x84)
    $bytes[0x100] = $Marker
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function Get-Sha256 {
    param([string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Write-BundleManifest {
    param([string] $BundleRoot, [string] $AssemblyVersion)
    $records = foreach ($name in 'FF7_Launcher.exe', 'FF7_Launcher.exe.config') {
        $item = Get-Item -LiteralPath (Join-Path $BundleRoot $name)
        [ordered]@{ path = $name; length = [int64]$item.Length; sha256 = (Get-Sha256 $item.FullName) }
    }
    $manifest = [ordered]@{
        schemaVersion = 1
        assemblyName = 'FF7_Launcher'
        assemblyVersion = $AssemblyVersion
        launcher = $records[0]
        config = $records[1]
    }
    [IO.File]::WriteAllText((Join-Path $BundleRoot 'launcher-2013-bundle.json'),
        ($manifest | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding($false)))
}

function Copy-Bundle {
    param([string] $Destination)
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($name in 'FF7_Launcher.exe', 'FF7_Launcher.exe.config', 'launcher-2013-bundle.json') {
        Copy-Item -LiteralPath (Join-Path $script:bundle $name) -Destination $Destination
    }
    return $Destination
}

function New-GameFixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('blind-soldier-2013-launcher-test-' + [Guid]::NewGuid().ToString('N'))
    $game = Join-Path $root 'steamapps\common\FINAL FANTASY VII'
    New-Item -ItemType Directory -Path (Join-Path $game 'data') -Force | Out-Null
    New-MinimalX86Pe -Path (Join-Path $game 'ff7_en.exe') -Marker 3
    [IO.File]::WriteAllBytes((Join-Path $game 'AF3DN.P'), (New-Object byte[] 2048))
    $launcher = Join-Path $game 'FF7_Launcher.exe'
    New-MinimalX86Pe -Path $launcher -Marker 17
    return [pscustomobject]@{
        Root = $root
        Game = $game
        Launcher = $launcher
        Config = $launcher + '.config'
        Backup = Join-Path $game 'FF7_Launcher.original.exe'
        ConfigBackup = $launcher + '.config.original'
        Manifest = Join-Path $game 'Blind-Soldier\Launcher2013\install-manifest.json'
        OriginalBytes = [IO.File]::ReadAllBytes($launcher)
        OriginalHash = Get-Sha256 $launcher
        Bundle = Copy-Bundle -Destination (Join-Path $root 'bundle')
    }
}

Describe 'Accessible FINAL FANTASY VII (2013) launcher lifecycle' {
    BeforeAll {
        Import-Module $modulePath -Force
        $script:bundleScratch = Join-Path ([IO.Path]::GetTempPath()) ('blind-soldier-2013-launcher-source-' + [Guid]::NewGuid().ToString('N'))
        $script:bundle = Join-Path $script:bundleScratch 'bundle'
        & $bundleBuilder -OutputPath $script:bundle -Configuration Release -ArtifactsPath (Join-Path $script:bundleScratch 'artifacts') | Out-Null
        $script:bundleManifest = [IO.File]::ReadAllText((Join-Path $script:bundle 'launcher-2013-bundle.json')) | ConvertFrom-Json
    }

    AfterAll {
        if (Test-Path -LiteralPath $script:bundleScratch) {
            Remove-Item -LiteralPath $script:bundleScratch -Recurse -Force
        }
    }

    BeforeEach {
        $fixture = New-GameFixture
    }

    AfterEach {
        if (Test-Path -LiteralPath $fixture.Root) {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
    }

    It 'validates and rejects bundles in a fresh redirected Windows PowerShell process' {
        function Invoke-FreshBundleCheck {
            $moduleLiteral = $modulePath.Replace("'", "''")
            $bundleLiteral = $fixture.Bundle.Replace("'", "''")
            $code = "`$ErrorActionPreference='Stop'; Import-Module '$moduleLiteral' -Force; Test-Ff7Accessible2013LauncherBundle -BundlePath '$bundleLiteral' | Out-Null"
            $start = New-Object Diagnostics.ProcessStartInfo
            $start.FileName = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
            $start.Arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand ' + [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($code))
            $start.UseShellExecute = $false
            $start.CreateNoWindow = $true
            $start.RedirectStandardOutput = $true
            $start.RedirectStandardError = $true
            $process = New-Object Diagnostics.Process
            $process.StartInfo = $start
            try {
                [void]$process.Start()
                $output = $process.StandardOutput.ReadToEndAsync()
                $errors = $process.StandardError.ReadToEndAsync()
                if (-not $process.WaitForExit(30000)) {
                    $process.Kill()
                    throw 'The fresh bundle check did not finish.'
                }
                return [pscustomobject]@{ ExitCode=$process.ExitCode; Output=$output.GetAwaiter().GetResult(); Error=$errors.GetAwaiter().GetResult() }
            }
            finally { $process.Dispose() }
        }
        $valid = Invoke-FreshBundleCheck
        if ($valid.ExitCode -ne 0) { throw ('Fresh bundle validation failed: ' + $valid.Error) }
        [IO.File]::AppendAllText((Join-Path $fixture.Bundle 'FF7_Launcher.exe.config'), ' ')
        $invalid = Invoke-FreshBundleCheck
        $invalid.ExitCode | Should Not Be 0
        $invalid.Error | Should Match 'does not match its manifest'
        (Get-Sha256 $fixture.Launcher) | Should Be $fixture.OriginalHash
    }

    It 'rejects a tampered bundle before touching the game' {
        [IO.File]::AppendAllText((Join-Path $fixture.Bundle 'FF7_Launcher.exe.config'), ' ')
        { Install-Ff7Accessible2013Launcher -GameRoot $fixture.Game -BundlePath $fixture.Bundle } |
            Should Throw 'does not match its manifest'
        (Get-Sha256 $fixture.Launcher) | Should Be $fixture.OriginalHash
        (Test-Path -LiteralPath $fixture.Backup) | Should Be $false
    }

    It 'rejects folders that are not the 2013 game' {
        Remove-Item -LiteralPath (Join-Path $fixture.Game 'data') -Recurse -Force
        { Install-Ff7Accessible2013Launcher -GameRoot $fixture.Game -BundlePath $fixture.Bundle } |
            Should Throw 'not a FINAL FANTASY VII (2013) game folder'
        New-Item -ItemType Directory -Path (Join-Path $fixture.Game 'data') | Out-Null
        New-MinimalX86Pe -Path (Join-Path $fixture.Game 'FFVII.exe') -Marker 9
        { Install-Ff7Accessible2013Launcher -GameRoot $fixture.Game -BundlePath $fixture.Bundle } |
            Should Throw 'not a FINAL FANTASY VII (2013) game folder'
        (Get-Sha256 $fixture.Launcher) | Should Be $fixture.OriginalHash
    }

    It 'installs the accessible launcher and keeps a verified original' {
        $result = Install-Ff7Accessible2013Launcher -GameRoot $fixture.Game -BundlePath $fixture.Bundle
        $result.Action | Should Be 'Installed'
        $result.Changed | Should Be $true
        (Get-Sha256 $fixture.Launcher) | Should Be $script:bundleManifest.launcher.sha256
        (Get-Sha256 $fixture.Config) | Should Be $script:bundleManifest.config.sha256
        (Get-Sha256 $fixture.Backup) | Should Be $fixture.OriginalHash
        $manifest = [IO.File]::ReadAllText($fixture.Manifest) | ConvertFrom-Json
        $manifest.launcher.originalSha256 | Should Be $fixture.OriginalHash
        $manifest.launcher.installedSha256 | Should Be $script:bundleManifest.launcher.sha256
        $manifest.configuration.backupPath | Should Be $null
        (Test-Path -LiteralPath $fixture.ConfigBackup) | Should Be $false
    }

    It 'repairs idempotently' {
        Install-Ff7Accessible2013Launcher -GameRoot $fixture.Game -BundlePath $fixture.Bundle | Out-Null
        $manifestBefore = [IO.File]::ReadAllText($fixture.Manifest)
        $result = Install-Ff7Accessible2013Launcher -GameRoot $fixture.Game -BundlePath $fixture.Bundle
        $result.Action | Should Be 'Repaired'
        $result.Changed | Should Be $false
        [IO.File]::ReadAllText($fixture.Manifest) | Should Be $manifestBefore
        (Get-Sha256 $fixture.Backup) | Should Be $fixture.OriginalHash
    }

    It 'validates without changing anything' {
        $result = Install-Ff7Accessible2013Launcher -GameRoot $fixture.Game -BundlePath $fixture.Bundle -ValidateOnly
        $result.ValidatedOnly | Should Be $true
        $result.Changed | Should Be $true
        (Get-Sha256 $fixture.Launcher) | Should Be $fixture.OriginalHash
        (Test-Path -LiteralPath $fixture.Backup) | Should Be $false
        (Test-Path -LiteralPath $fixture.Manifest) | Should Be $false
    }

    It 'refuses an accessible launcher it has no ownership record for' {
        Copy-Item -LiteralPath (Join-Path $fixture.Bundle 'FF7_Launcher.exe') -Destination $fixture.Launcher -Force
        [IO.File]::AppendAllText($fixture.Launcher, 'unknown build')
        { Install-Ff7Accessible2013Launcher -GameRoot $fixture.Game -BundlePath $fixture.Bundle } |
            Should Throw 'unrecognized FF7_Launcher.exe'
        (Test-Path -LiteralPath $fixture.Backup) | Should Be $false
    }

    It 'reinstalls after Steam restores the original launcher' {
        Install-Ff7Accessible2013Launcher -GameRoot $fixture.Game -BundlePath $fixture.Bundle | Out-Null
        [IO.File]::WriteAllBytes($fixture.Launcher, $fixture.OriginalBytes)
        $result = Install-Ff7Accessible2013Launcher -GameRoot $fixture.Game -BundlePath $fixture.Bundle
        $result.Action | Should Be 'Reinstalled'
        (Get-Sha256 $fixture.Launcher) | Should Be $script:bundleManifest.launcher.sha256
        (Get-Sha256 $fixture.Backup) | Should Be $fixture.OriginalHash
    }

    It 'never overwrites a different original backup' {
        New-MinimalX86Pe -Path $fixture.Backup -Marker 99
        $backupHash = Get-Sha256 $fixture.Backup
        { Install-Ff7Accessible2013Launcher -GameRoot $fixture.Game -BundlePath $fixture.Bundle } |
            Should Throw 'FF7_Launcher.original.exe'
        (Get-Sha256 $fixture.Backup) | Should Be $backupHash
        (Get-Sha256 $fixture.Launcher) | Should Be $fixture.OriginalHash
    }

    It 'uninstalls by restoring the original and removing owned files' {
        Install-Ff7Accessible2013Launcher -GameRoot $fixture.Game -BundlePath $fixture.Bundle | Out-Null
        $result = Uninstall-Ff7Accessible2013Launcher -GameRoot $fixture.Game
        @($result.Preserved).Count | Should Be 0
        (Get-Sha256 $fixture.Launcher) | Should Be $fixture.OriginalHash
        (Test-Path -LiteralPath $fixture.Backup) | Should Be $false
        (Test-Path -LiteralPath $fixture.Config) | Should Be $false
        (Test-Path -LiteralPath $fixture.Manifest) | Should Be $false
        (Test-Path -LiteralPath (Split-Path -Parent $fixture.Manifest)) | Should Be $false
    }

    It 'preserves a launcher that changed after installation' {
        Install-Ff7Accessible2013Launcher -GameRoot $fixture.Game -BundlePath $fixture.Bundle | Out-Null
        New-MinimalX86Pe -Path $fixture.Launcher -Marker 55
        $changedHash = Get-Sha256 $fixture.Launcher
        $result = Uninstall-Ff7Accessible2013Launcher -GameRoot $fixture.Game
        (Get-Sha256 $fixture.Launcher) | Should Be $changedHash
        (Get-Sha256 $fixture.Backup) | Should Be $fixture.OriginalHash
        (@($result.Preserved) -join ' ') | Should Match 'changed after installation'
    }

    It 'backs up and restores an existing launcher configuration' {
        [IO.File]::WriteAllText($fixture.Config, '<configuration><!-- player --></configuration>')
        $configHash = Get-Sha256 $fixture.Config
        Install-Ff7Accessible2013Launcher -GameRoot $fixture.Game -BundlePath $fixture.Bundle | Out-Null
        (Get-Sha256 $fixture.Config) | Should Be $script:bundleManifest.config.sha256
        (Get-Sha256 $fixture.ConfigBackup) | Should Be $configHash
        Uninstall-Ff7Accessible2013Launcher -GameRoot $fixture.Game | Out-Null
        (Get-Sha256 $fixture.Config) | Should Be $configHash
        (Test-Path -LiteralPath $fixture.ConfigBackup) | Should Be $false
    }

    It 'updates a prior accessible build without losing the original' {
        $prior = Copy-Bundle -Destination (Join-Path $fixture.Root 'prior-bundle')
        [IO.File]::AppendAllText((Join-Path $prior 'FF7_Launcher.exe'), 'prior build')
        Write-BundleManifest -BundleRoot $prior -AssemblyVersion $script:bundleManifest.assemblyVersion
        Install-Ff7Accessible2013Launcher -GameRoot $fixture.Game -BundlePath $prior | Out-Null
        $result = Install-Ff7Accessible2013Launcher -GameRoot $fixture.Game -BundlePath $fixture.Bundle
        $result.Action | Should Be 'Updated'
        (Get-Sha256 $fixture.Launcher) | Should Be $script:bundleManifest.launcher.sha256
        (Get-Sha256 $fixture.Backup) | Should Be $fixture.OriginalHash
        ([IO.File]::ReadAllText($fixture.Manifest) | ConvertFrom-Json).launcher.installedSha256 | Should Be $script:bundleManifest.launcher.sha256
    }

    It 'rejects an ownership record from another game folder' {
        Install-Ff7Accessible2013Launcher -GameRoot $fixture.Game -BundlePath $fixture.Bundle | Out-Null
        $manifest = [IO.File]::ReadAllText($fixture.Manifest) | ConvertFrom-Json
        $manifest.gameRoot = 'C:\Elsewhere\FINAL FANTASY VII'
        [IO.File]::WriteAllText($fixture.Manifest, ($manifest | ConvertTo-Json -Depth 6))
        { Uninstall-Ff7Accessible2013Launcher -GameRoot $fixture.Game } | Should Throw 'another game folder'
        (Get-Sha256 $fixture.Launcher) | Should Be $script:bundleManifest.launcher.sha256
    }
}
