$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = $PSScriptRoot
$suiteRoot = Join-Path ([IO.Path]::GetTempPath()) ('blind-soldier-2013-lifecycle-' + [Guid]::NewGuid().ToString('N'))
$bundleRoot = Join-Path $suiteRoot 'bundle'
$fixture = $null

function Write-LifecycleFixturePe {
    param([string] $Path, [byte] $Marker)
    $bytes = New-Object byte[] 512
    $bytes[0] = 0x4D; $bytes[1] = 0x5A; $bytes[2] = $Marker
    [BitConverter]::GetBytes([int]0x80).CopyTo($bytes,0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes,0x80)
    [BitConverter]::GetBytes([uint16]0x014C).CopyTo($bytes,0x84)
    [IO.File]::WriteAllBytes($Path,$bytes)
}

function New-LifecycleFixture {
    $root = Join-Path $suiteRoot ([Guid]::NewGuid().ToString('N'))
    $game = Join-Path $root 'game folder'
    $staging = Join-Path $root 'package\files'
    New-Item -ItemType Directory -Path (Join-Path $game 'data'),$staging -Force | Out-Null
    Write-LifecycleFixturePe -Path (Join-Path $game 'FF7_Launcher.exe') -Marker 1
    Write-LifecycleFixturePe -Path (Join-Path $game 'ff7_en.exe') -Marker 2
    [IO.File]::WriteAllText((Join-Path $game 'AF3DN.P'),'existing graphics driver')
    [IO.File]::WriteAllText((Join-Path $game 'FFNx.toml'),'existing graphics settings')
    [IO.File]::WriteAllText((Join-Path $game 'save00.ff7'),'existing save')
    & (Join-Path $repoRoot 'Add-Accessible2013LauncherPayload.ps1') -DestinationRoot $staging -BundlePath $bundleRoot
    $tools = Join-Path $staging 'Blind-Soldier\Tools'
    New-Item -ItemType Directory -Path $tools -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'portable-assets\Remove-AmethystRegistryEntries.ps1') -Destination $tools
    Get-ChildItem -LiteralPath $staging -Force | Copy-Item -Destination $game -Recurse
    [pscustomobject]@{Root=$root;Game=$game;Staging=$staging;OriginalHash=(Get-FileHash -LiteralPath (Join-Path $game 'FF7_Launcher.exe')).Hash}
}

function Invoke-LifecycleHook {
    param([string] $Script,[object] $Fixture)
    # Exercise Amethyst's actual -gameFolder/-modFolder command contract. The
    # migration sees a private, nonexistent HKCU test subtree and never HKLM.
    $names=@('BLIND_SOLDIER_CLEANUP_TEST_MODE','BLIND_SOLDIER_CLEANUP_HIVE','BLIND_SOLDIER_CLEANUP_IFEO_ROOT','BLIND_SOLDIER_CLEANUP_CONFIRM')
    $before=@{}
    foreach($name in $names){$before[$name]=[Environment]::GetEnvironmentVariable($name)}
    try {
        $env:BLIND_SOLDIER_CLEANUP_TEST_MODE='1'
        $env:BLIND_SOLDIER_CLEANUP_HIVE='CurrentUser'
        $env:BLIND_SOLDIER_CLEANUP_IFEO_ROOT='Software\BlindSoldier\CleanupTests\'+[Guid]::NewGuid().ToString('N')
        $previousErrorAction=$ErrorActionPreference
        try {
            # Windows PowerShell 5.1 represents a native child's stderr as
            # ErrorRecords. Capture a deliberate refusal and its exit code;
            # don't let the test harness throw before inspecting the result.
            $ErrorActionPreference='Continue'
            $output=@(& powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $Script -gameFolder $Fixture.Game -modFolder (Split-Path -Parent $Fixture.Staging) 2>&1 | ForEach-Object ToString)
            $code=$LASTEXITCODE
        }
        finally {$ErrorActionPreference=$previousErrorAction}
        [pscustomobject]@{ExitCode=$code;Output=$output -join "`n"}
    }
    finally {foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$before[$name])}}
}

Describe '2013 launcher Amethyst lifecycle' {
    BeforeAll {
        & (Join-Path $repoRoot 'Build-Accessible2013LauncherBundle.ps1') -OutputPath $bundleRoot -ArtifactsPath (Join-Path $suiteRoot 'artifacts') | Out-Null
    }
    AfterAll {
        $resolved=[IO.Path]::GetFullPath($suiteRoot)
        $allowed=Join-Path ([IO.Path]::GetTempPath()) 'blind-soldier-2013-lifecycle-'
        if (-not $resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) {throw 'Lifecycle scratch escaped its named test directory.'}
        if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
    }
    It 'installs, updates and restores from the cached standalone uninstall script' {
        $fixture=New-LifecycleFixture
        $install=Join-Path $fixture.Staging 'Blind-Soldier\Launcher2013\Manager-Install2013Launcher.ps1'
        (Invoke-LifecycleHook -Script $install -Fixture $fixture).ExitCode | Should Be 0
        (Get-FileHash -LiteralPath (Join-Path $fixture.Game 'FF7_Launcher.exe')).Hash | Should Be (Get-FileHash -LiteralPath (Join-Path $bundleRoot 'FF7_Launcher.exe')).Hash
        (Get-FileHash -LiteralPath (Join-Path $fixture.Game 'FF7_Launcher.original.exe')).Hash | Should Be $fixture.OriginalHash
        (Invoke-LifecycleHook -Script $install -Fixture $fixture).ExitCode | Should Be 0
        $cache=Join-Path $fixture.Root 'receipt\scripts'
        New-Item -ItemType Directory -Path $cache -Force | Out-Null
        $restore=Join-Path $cache 'Manager-Restore2013Launcher.ps1'
        Copy-Item -LiteralPath (Join-Path $fixture.Staging 'Blind-Soldier\Launcher2013\Manager-Restore2013Launcher.ps1') -Destination $restore
        (Invoke-LifecycleHook -Script $restore -Fixture $fixture).ExitCode | Should Be 0
        (Get-FileHash -LiteralPath (Join-Path $fixture.Game 'FF7_Launcher.exe')).Hash | Should Be $fixture.OriginalHash
        Test-Path -LiteralPath (Join-Path $fixture.Game 'FF7_Launcher.original.exe') | Should Be $false
        [IO.File]::ReadAllText((Join-Path $fixture.Game 'save00.ff7')) | Should Be 'existing save'
        [IO.File]::ReadAllText((Join-Path $fixture.Game 'AF3DN.P')) | Should Be 'existing graphics driver'
        [IO.File]::ReadAllText((Join-Path $fixture.Game 'FFNx.toml')) | Should Be 'existing graphics settings'
        @(Get-ChildItem -LiteralPath $fixture.Game -Filter '*.local').Count | Should Be 0
    }
    It 'preserves a launcher changed after installation' {
        $fixture=New-LifecycleFixture
        $install=Join-Path $fixture.Staging 'Blind-Soldier\Launcher2013\Manager-Install2013Launcher.ps1'
        (Invoke-LifecycleHook -Script $install -Fixture $fixture).ExitCode | Should Be 0
        [IO.File]::WriteAllText((Join-Path $fixture.Game 'FF7_Launcher.exe'),'subsequent launcher change')
        $restore=Join-Path $fixture.Game 'Blind-Soldier\Launcher2013\Manager-Restore2013Launcher.ps1'
        $result=Invoke-LifecycleHook -Script $restore -Fixture $fixture
        $result.ExitCode | Should Be 0
        $result.Output | Should Match 'changed after installation'
        [IO.File]::ReadAllText((Join-Path $fixture.Game 'FF7_Launcher.exe')) | Should Be 'subsequent launcher change'
        Test-Path -LiteralPath (Join-Path $fixture.Game 'FF7_Launcher.original.exe') | Should Be $true
    }
    It 'keeps the modern game layout and completes the existing migration' {
        $fixture=New-LifecycleFixture
        [IO.File]::WriteAllText((Join-Path $fixture.Game 'FFVII.exe'),'modern game')
        $install=Join-Path $fixture.Staging 'Blind-Soldier\Launcher2013\Manager-Install2013Launcher.ps1'
        $result=Invoke-LifecycleHook -Script $install -Fixture $fixture
        $result.ExitCode | Should Be 0
        $result.Output | Should Match 'launcher was kept'
        $result.Output | Should Match 'Cleanup complete'
        (Get-FileHash -LiteralPath (Join-Path $fixture.Game 'FF7_Launcher.exe')).Hash | Should Be $fixture.OriginalHash
        Test-Path -LiteralPath (Join-Path $fixture.Game 'FF7_Launcher.original.exe') | Should Be $false
    }
    It 'updates a 7th Heaven folder without an original 2013 launcher' {
        $fixture=New-LifecycleFixture
        Remove-Item -LiteralPath (Join-Path $fixture.Game 'FF7_Launcher.exe')
        [IO.File]::WriteAllText((Join-Path $fixture.Game 'FFVII_LAUNCHER.exe'),'existing modern launcher')
        $install=Join-Path $fixture.Staging 'Blind-Soldier\Launcher2013\Manager-Install2013Launcher.ps1'
        $result=Invoke-LifecycleHook -Script $install -Fixture $fixture
        $result.ExitCode | Should Be 0
        $result.Output | Should Match 'No original 2013 launcher'
        $result.Output | Should Match 'Cleanup complete'
        [IO.File]::ReadAllText((Join-Path $fixture.Game 'FFVII_LAUNCHER.exe')) | Should Be 'existing modern launcher'
        Test-Path -LiteralPath (Join-Path $fixture.Game 'FF7_Launcher.exe') | Should Be $false
        $restore=Join-Path $fixture.Game 'Blind-Soldier\Launcher2013\Manager-Restore2013Launcher.ps1'
        (Invoke-LifecycleHook -Script $restore -Fixture $fixture).ExitCode | Should Be 0
    }
    It 'keeps an unrecognized custom launcher while installing the reader' {
        $fixture=New-LifecycleFixture
        [IO.File]::WriteAllText((Join-Path $fixture.Game 'FF7_Launcher.exe'),'custom launcher')
        $install=Join-Path $fixture.Staging 'Blind-Soldier\Launcher2013\Manager-Install2013Launcher.ps1'
        $result=Invoke-LifecycleHook -Script $install -Fixture $fixture
        $result.ExitCode | Should Be 0
        $result.Output | Should Match 'launcher was kept'
        $result.Output | Should Match 'Cleanup complete'
        [IO.File]::ReadAllText((Join-Path $fixture.Game 'FF7_Launcher.exe')) | Should Be 'custom launcher'
        Test-Path -LiteralPath (Join-Path $fixture.Game 'FF7_Launcher.original.exe') | Should Be $false
    }
    It 'rejects a damaged replacement bundle before running migration' {
        $fixture=New-LifecycleFixture
        $bundleLauncher=Join-Path $fixture.Staging 'Blind-Soldier\Launcher2013\bundle\FF7_Launcher.exe'
        [IO.File]::AppendAllText($bundleLauncher,'damaged')
        $install=Join-Path $fixture.Staging 'Blind-Soldier\Launcher2013\Manager-Install2013Launcher.ps1'
        $result=Invoke-LifecycleHook -Script $install -Fixture $fixture
        $result.ExitCode | Should Be 1
        $result.Output | Should Not Match 'Cleanup complete'
        (Get-FileHash -LiteralPath (Join-Path $fixture.Game 'FF7_Launcher.exe')).Hash | Should Be $fixture.OriginalHash
    }
}
