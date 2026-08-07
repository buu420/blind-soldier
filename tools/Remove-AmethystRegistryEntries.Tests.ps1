$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$commandPath = Join-Path $repoRoot `
    'portable-assets\Remove-Amethyst-Registry-Entries.cmd'
$cleanupPath = Join-Path $repoRoot `
    'portable-assets\Remove-AmethystRegistryEntries.ps1'

Describe 'Amethyst registry cleanup' {
    It 'removes owned current and legacy values and preserves foreign entries' {
        Test-Path -LiteralPath $commandPath -PathType Leaf | Should Be $true
        Test-Path -LiteralPath $cleanupPath -PathType Leaf | Should Be $true
        $stage = Join-Path ([IO.Path]::GetTempPath()) `
            ('blind-soldier-cleanup-test-' + [Guid]::NewGuid().ToString('N'))
        $stagedTools = Join-Path $stage 'Blind-Soldier\Tools'
        New-Item -ItemType Directory -Path $stagedTools -Force | Out-Null
        $stagedCommand = Join-Path $stage 'Remove-Amethyst-Registry-Entries.cmd'
        Copy-Item -LiteralPath $commandPath -Destination $stagedCommand
        Copy-Item -LiteralPath $cleanupPath -Destination $stagedTools

        $testId = [Guid]::NewGuid().ToString('N')
        $rootPath = "Software\BlindSoldier\CleanupTests\$testId"
        $ownedKey = "Registry::HKEY_CURRENT_USER\$rootPath\ff7_en.exe"
        $secondKey = "Registry::HKEY_CURRENT_USER\$rootPath\FFVII.exe"
        $previous = @{
            TestMode = $env:BLIND_SOLDIER_CLEANUP_TEST_MODE
            Hive = $env:BLIND_SOLDIER_CLEANUP_HIVE
            Root = $env:BLIND_SOLDIER_CLEANUP_IFEO_ROOT
            Confirm = $env:BLIND_SOLDIER_CLEANUP_CONFIRM
            NoPause = $env:BLIND_SOLDIER_CLEANUP_NO_PAUSE
        }
        try {
            New-Item -Path $ownedKey -Force | Out-Null
            New-ItemProperty -Path $ownedKey -Name Debugger `
                -PropertyType String `
                -Value '"C:\Games\FF7\Blind-Soldier-Launcher-x86.exe"' |
                Out-Null
            New-ItemProperty -Path $ownedKey `
                -Name BlindSoldierDebuggerOwner -PropertyType String `
                -Value '"C:\Games\FF7\Blind-Soldier-Launcher-x86.exe"' |
                Out-Null
            New-ItemProperty -Path $ownedKey -Name UnrelatedValue `
                -PropertyType String -Value 'preserve me' | Out-Null

            New-Item -Path $secondKey -Force | Out-Null
            New-ItemProperty -Path $secondKey -Name Debugger `
                -PropertyType String -Value '"C:\Tools\ForeignDebugger.exe"' |
                Out-Null
            New-ItemProperty -Path $secondKey `
                -Name BlindSoldierDebuggerOwner -PropertyType String `
                -Value '"C:\Games\FF7\Blind-Soldier-Launcher-x64.exe"' |
                Out-Null

            $env:BLIND_SOLDIER_CLEANUP_TEST_MODE = '1'
            $env:BLIND_SOLDIER_CLEANUP_HIVE = 'CurrentUser'
            $env:BLIND_SOLDIER_CLEANUP_IFEO_ROOT = $rootPath
            $env:BLIND_SOLDIER_CLEANUP_CONFIRM = 'Y'
            $env:BLIND_SOLDIER_CLEANUP_NO_PAUSE = '1'
            & $env:ComSpec /d /c ('"{0}"' -f $stagedCommand) | Out-Null
            $LASTEXITCODE | Should Be 0
            (Get-ItemProperty -Path $ownedKey -Name Debugger `
                -ErrorAction SilentlyContinue) | Should BeNullOrEmpty
            (Get-ItemProperty -Path $ownedKey `
                -Name BlindSoldierDebuggerOwner -ErrorAction SilentlyContinue) |
                Should BeNullOrEmpty
            Get-ItemPropertyValue -Path $ownedKey -Name UnrelatedValue |
                Should Be 'preserve me'
            Get-ItemPropertyValue -Path $secondKey -Name Debugger |
                Should Be '"C:\Tools\ForeignDebugger.exe"'
            Get-ItemPropertyValue -Path $secondKey `
                -Name BlindSoldierDebuggerOwner |
                Should Be '"C:\Games\FF7\Blind-Soldier-Launcher-x64.exe"'

            Remove-ItemProperty -Path $secondKey `
                -Name BlindSoldierDebuggerOwner
            Set-ItemProperty -Path $secondKey -Name Debugger `
                -Value '"C:\Games\FF7\BlindSoldier_Launcher.exe"'
            New-ItemProperty -Path $secondKey -Name UnrelatedValue `
                -PropertyType String -Value 'keep this too' | Out-Null
            & $env:ComSpec /d /c ('"{0}"' -f $stagedCommand) | Out-Null
            $LASTEXITCODE | Should Be 0
            (Get-ItemProperty -Path $secondKey -Name Debugger `
                -ErrorAction SilentlyContinue) | Should BeNullOrEmpty
            Get-ItemPropertyValue -Path $secondKey -Name UnrelatedValue |
                Should Be 'keep this too'
        }
        finally {
            $env:BLIND_SOLDIER_CLEANUP_TEST_MODE = $previous.TestMode
            $env:BLIND_SOLDIER_CLEANUP_HIVE = $previous.Hive
            $env:BLIND_SOLDIER_CLEANUP_IFEO_ROOT = $previous.Root
            $env:BLIND_SOLDIER_CLEANUP_CONFIRM = $previous.Confirm
            $env:BLIND_SOLDIER_CLEANUP_NO_PAUSE = $previous.NoPause
            Remove-Item -Path "Registry::HKEY_CURRENT_USER\$rootPath" `
                -Recurse -Force -ErrorAction SilentlyContinue
            if (Test-Path -LiteralPath $stage -PathType Container) {
                Remove-Item -LiteralPath $stage -Recurse -Force
            }
        }
    }
}
