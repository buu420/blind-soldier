$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$modulePath = Join-Path $scriptRoot 'FF7LauncherInstall.psm1'
$bundleBuilder = Join-Path $scriptRoot 'Build-AccessibleLauncherBundle.ps1'

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

function New-LauncherFixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('blind-soldier-launcher-test-' + [Guid]::NewGuid().ToString('N'))
    $gameRoot = Join-Path $root 'game'
    $reloadedRoot = Join-Path $root 'Reloaded-II'
    $bundleRoot = Join-Path $root 'bundle'
    New-Item -ItemType Directory -Path $gameRoot, $reloadedRoot, (Join-Path $bundleRoot 'native\x86') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $script:testLauncherBundle 'FFVII_LAUNCHER.exe') -Destination $bundleRoot
    Copy-Item -LiteralPath (Join-Path $script:testLauncherBundle 'FFVII_LAUNCHER.exe.config') -Destination $bundleRoot
    Copy-Item -LiteralPath (Join-Path $script:testLauncherBundle 'native\x86\FFVII_LAUNCHER.prism.x86.dll') `
        -Destination (Join-Path $bundleRoot 'native\x86\FFVII_LAUNCHER.prism.x86.dll')

    $launcherTarget = Join-Path $gameRoot 'FFVII_LAUNCHER.exe'
    New-MinimalX86Pe -Path $launcherTarget -Marker 17
    $stockBytes = [IO.File]::ReadAllBytes($launcherTarget)
    $stockHash = (Get-FileHash -LiteralPath $launcherTarget -Algorithm SHA256).Hash
    $manifest = [IO.File]::ReadAllText((Join-Path $script:testLauncherBundle 'launcher-bundle.json')) | ConvertFrom-Json
    $manifest.stockLauncherSha256 = $stockHash
    [IO.File]::WriteAllText(
        (Join-Path $bundleRoot 'launcher-bundle.json'),
        ($manifest | ConvertTo-Json -Depth 6),
        (New-Object Text.UTF8Encoding($false)))

    return [pscustomobject]@{
        Root = $root
        GameRoot = $gameRoot
        ReloadedRoot = $reloadedRoot
        BundleRoot = $bundleRoot
        LauncherTarget = $launcherTarget
        ConfigTarget = $launcherTarget + '.config'
        PrismTarget = Join-Path $gameRoot 'launcher_accessibility\native\x86\FFVII_LAUNCHER.prism.x86.dll'
        ManifestTarget = Join-Path $gameRoot 'launcher_accessibility\install-manifest.json'
        StockBytes = $stockBytes
        StockHash = $stockHash
        AccessibleHash = [string]$manifest.launcher.sha256
        ConfigHash = [string]$manifest.config.sha256
        PrismHash = [string]$manifest.prism.sha256
    }
}

function Set-LegacyLauncherInstallation {
    param([Parameter(Mandatory=$true)] [object] $Fixture)

    New-MinimalX86Pe -Path $Fixture.LauncherTarget -Marker 44
    $priorAccessibleHash = (Get-FileHash -LiteralPath $Fixture.LauncherTarget -Algorithm SHA256).Hash
    Copy-Item -LiteralPath (Join-Path $Fixture.BundleRoot 'FFVII_LAUNCHER.exe.config') `
        -Destination $Fixture.ConfigTarget
    $prismParent = Split-Path -Parent $Fixture.PrismTarget
    New-Item -ItemType Directory -Path $prismParent -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $Fixture.BundleRoot 'native\x86\FFVII_LAUNCHER.prism.x86.dll') `
        -Destination $Fixture.PrismTarget

    $legacyBackupRoot = Join-Path $Fixture.Root 'old-developer-backups'
    New-Item -ItemType Directory -Path $legacyBackupRoot -Force | Out-Null
    $stockBackupPath = Join-Path $legacyBackupRoot 'FFVII_LAUNCHER.exe'
    [IO.File]::WriteAllBytes($stockBackupPath, $Fixture.StockBytes)
    $manifestParent = Split-Path -Parent $Fixture.ManifestTarget
    New-Item -ItemType Directory -Path $manifestParent -Force | Out-Null
    $legacyManifest = [ordered]@{
        SchemaVersion = 1
        InstalledAt = '2026-07-22T21:44:56.7834316-05:00'
        GameRoot = $Fixture.GameRoot
        LauncherPath = $Fixture.LauncherTarget
        OriginalStockSha256 = $Fixture.StockHash
        OriginalStockBackupPath = $stockBackupPath
        ReplacedLauncherSha256 = $priorAccessibleHash
        InstalledLauncherSha256 = $priorAccessibleHash
        LauncherConfigPath = $Fixture.ConfigTarget
        InstalledLauncherConfigSha256 = $Fixture.ConfigHash
        PrismPath = $Fixture.PrismTarget
        PrismSha256 = $Fixture.PrismHash
        PrismArchitecture = 'x86'
    }
    [IO.File]::WriteAllText(
        $Fixture.ManifestTarget,
        ($legacyManifest | ConvertTo-Json -Depth 4),
        (New-Object Text.UTF8Encoding($false)))
    return [pscustomobject]@{
        PriorAccessibleHash = $priorAccessibleHash
        LegacyBackupRoot = $legacyBackupRoot
        StockBackupPath = $stockBackupPath
    }
}

Describe 'Accessible FFVII launcher lifecycle' {
    BeforeAll {
        Import-Module $modulePath -Force
        $script:testLauncherBundle = Join-Path ([IO.Path]::GetTempPath()) `
            ('blind-soldier-launcher-source-' + [Guid]::NewGuid().ToString('N'))
        & $bundleBuilder -OutputPath $script:testLauncherBundle -Configuration Release | Out-Null
    }

    AfterAll {
        if (Test-Path -LiteralPath $script:testLauncherBundle) {
            Remove-Item -LiteralPath $script:testLauncherBundle -Recurse -Force
        }
    }

    BeforeEach {
        $fixture = New-LauncherFixture
    }

    AfterEach {
        if (Test-Path -LiteralPath $fixture.Root -PathType Container) {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
    }

    It 'installs every launcher file and records a verified stock backup' {
        $result = Install-Ff7AccessibleLauncher -GameRoot $fixture.GameRoot `
            -ReloadedRoot $fixture.ReloadedRoot -BundlePath $fixture.BundleRoot
        try {
            (Get-FileHash -LiteralPath $fixture.LauncherTarget -Algorithm SHA256).Hash | Should Be $fixture.AccessibleHash
            (Get-FileHash -LiteralPath $fixture.ConfigTarget -Algorithm SHA256).Hash | Should Be $fixture.ConfigHash
            (Get-FileHash -LiteralPath $fixture.PrismTarget -Algorithm SHA256).Hash | Should Be $fixture.PrismHash
            $result.State.executable.changed | Should Be $true
            (Test-Path -LiteralPath $result.State.executable.backupPath -PathType Leaf) | Should Be $true
            (Get-FileHash -LiteralPath $result.State.executable.backupPath -Algorithm SHA256).Hash | Should Be $fixture.StockHash
            (Test-Path -LiteralPath $fixture.ManifestTarget -PathType Leaf) | Should Be $true
        }
        finally {
            Complete-Ff7AccessibleLauncherTransaction -Result $result
        }
    }

    It 'rejects the obsolete launcher bundle schema before changing the game' {
        $manifestPath = Join-Path $fixture.BundleRoot 'launcher-bundle.json'
        $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
        $manifest.schemaVersion = 1
        [IO.File]::WriteAllText($manifestPath,
            ($manifest | ConvertTo-Json -Depth 6),
            (New-Object Text.UTF8Encoding($false)))
        $before = [IO.File]::ReadAllBytes($fixture.LauncherTarget)

        { Install-Ff7AccessibleLauncher -GameRoot $fixture.GameRoot `
                -ReloadedRoot $fixture.ReloadedRoot `
                -BundlePath $fixture.BundleRoot } | Should Throw

        [IO.File]::ReadAllBytes($fixture.LauncherTarget) | Should Be $before
        Test-Path -LiteralPath $fixture.ManifestTarget | Should Be $false
    }

    It 'repairs idempotently while retaining the original stock backup' {
        $first = Install-Ff7AccessibleLauncher -GameRoot $fixture.GameRoot `
            -ReloadedRoot $fixture.ReloadedRoot -BundlePath $fixture.BundleRoot
        Complete-Ff7AccessibleLauncherTransaction -Result $first
        $second = Install-Ff7AccessibleLauncher -GameRoot $fixture.GameRoot `
            -ReloadedRoot $fixture.ReloadedRoot -BundlePath $fixture.BundleRoot
        try {
            $second.Changed | Should Be $false
            $second.State.executable.changed | Should Be $true
            $second.State.executable.backupPath | Should Be $first.State.executable.backupPath
            (Get-FileHash -LiteralPath $second.State.executable.backupPath -Algorithm SHA256).Hash | Should Be $fixture.StockHash
        }
        finally {
            Complete-Ff7AccessibleLauncherTransaction -Result $second
        }
    }

    It 'migrates the legacy launcher manifest into the managed Reloaded backup root' {
        $legacy = Set-LegacyLauncherInstallation -Fixture $fixture

        $validation = Install-Ff7AccessibleLauncher -GameRoot $fixture.GameRoot `
            -ReloadedRoot $fixture.ReloadedRoot -BundlePath $fixture.BundleRoot -ValidateOnly
        $validation.Changed | Should Be $true
        (Get-FileHash -LiteralPath $fixture.LauncherTarget -Algorithm SHA256).Hash | Should Be $legacy.PriorAccessibleHash
        (Test-Path -LiteralPath (Join-Path $fixture.ReloadedRoot 'AccessibilityBackups')) | Should Be $false

        $result = Install-Ff7AccessibleLauncher -GameRoot $fixture.GameRoot `
            -ReloadedRoot $fixture.ReloadedRoot -BundlePath $fixture.BundleRoot
        try {
            (Get-FileHash -LiteralPath $fixture.LauncherTarget -Algorithm SHA256).Hash | Should Be $fixture.AccessibleHash
            $managedBackupRoot = [IO.Path]::GetFullPath((Join-Path $fixture.ReloadedRoot 'AccessibilityBackups')).TrimEnd('\') + '\'
            $result.State.executable.backupPath.StartsWith($managedBackupRoot, [StringComparison]::OrdinalIgnoreCase) | Should Be $true
            (Get-FileHash -LiteralPath $result.State.executable.backupPath -Algorithm SHA256).Hash | Should Be $fixture.StockHash
            $newManifestText = [IO.File]::ReadAllText($fixture.ManifestTarget)
            ($newManifestText | ConvertFrom-Json).schemaVersion | Should Be 2
            $newManifestText | Should Not Match ([regex]::Escape($legacy.LegacyBackupRoot))
        }
        finally {
            Complete-Ff7AccessibleLauncherTransaction -Result $result
        }
    }

    It 'refuses an unknown launcher before changing any file' {
        New-MinimalX86Pe -Path $fixture.LauncherTarget -Marker 99
        $before = [IO.File]::ReadAllBytes($fixture.LauncherTarget)

        { Install-Ff7AccessibleLauncher -GameRoot $fixture.GameRoot `
                -ReloadedRoot $fixture.ReloadedRoot -BundlePath $fixture.BundleRoot } |
            Should Throw 'unknown FFVII_LAUNCHER.exe identity'

        [IO.File]::ReadAllBytes($fixture.LauncherTarget) | Should Be $before
        (Test-Path -LiteralPath (Join-Path $fixture.GameRoot 'launcher_accessibility')) | Should Be $false
        (Test-Path -LiteralPath (Join-Path $fixture.ReloadedRoot 'AccessibilityBackups')) | Should Be $false
    }

    It 'restores stock and removes newly-created launcher files on uninstall' {
        $installed = Install-Ff7AccessibleLauncher -GameRoot $fixture.GameRoot `
            -ReloadedRoot $fixture.ReloadedRoot -BundlePath $fixture.BundleRoot
        Complete-Ff7AccessibleLauncherTransaction -Result $installed

        $outcome = Restore-Ff7AccessibleLauncherFromState -GameRoot $fixture.GameRoot `
            -ReloadedRoot $fixture.ReloadedRoot -State $installed.State

        [IO.File]::ReadAllBytes($fixture.LauncherTarget) | Should Be $fixture.StockBytes
        (Test-Path -LiteralPath $fixture.ConfigTarget) | Should Be $false
        (Test-Path -LiteralPath $fixture.PrismTarget) | Should Be $false
        (Test-Path -LiteralPath $fixture.ManifestTarget) | Should Be $false
        $outcome.Preserved.Count | Should Be 0
    }

    It 'preserves a launcher changed after installation' {
        $installed = Install-Ff7AccessibleLauncher -GameRoot $fixture.GameRoot `
            -ReloadedRoot $fixture.ReloadedRoot -BundlePath $fixture.BundleRoot
        Complete-Ff7AccessibleLauncherTransaction -Result $installed
        New-MinimalX86Pe -Path $fixture.LauncherTarget -Marker 77
        $changedHash = (Get-FileHash -LiteralPath $fixture.LauncherTarget -Algorithm SHA256).Hash

        $outcome = Restore-Ff7AccessibleLauncherFromState -GameRoot $fixture.GameRoot `
            -ReloadedRoot $fixture.ReloadedRoot -State $installed.State

        (Get-FileHash -LiteralPath $fixture.LauncherTarget -Algorithm SHA256).Hash | Should Be $changedHash
        ($outcome.Preserved -join '|') | Should Match 'changed after installation'
    }

    It 'rolls back every launcher file after a later deployment failure' {
        $installed = Install-Ff7AccessibleLauncher -GameRoot $fixture.GameRoot `
            -ReloadedRoot $fixture.ReloadedRoot -BundlePath $fixture.BundleRoot

        Undo-Ff7AccessibleLauncherTransaction -Result $installed

        [IO.File]::ReadAllBytes($fixture.LauncherTarget) | Should Be $fixture.StockBytes
        (Test-Path -LiteralPath $fixture.ConfigTarget) | Should Be $false
        (Test-Path -LiteralPath $fixture.PrismTarget) | Should Be $false
        (Test-Path -LiteralPath $fixture.ManifestTarget) | Should Be $false
        (Test-Path -LiteralPath (Join-Path $fixture.ReloadedRoot 'AccessibilityBackups')) | Should Be $false
    }
}
