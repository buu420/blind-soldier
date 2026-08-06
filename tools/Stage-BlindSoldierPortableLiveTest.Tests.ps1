$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$stagerPath = Join-Path $scriptRoot 'Stage-BlindSoldierPortableLiveTest.ps1'

function New-TestPe {
    param([string] $Path, [uint16] $Machine, [byte] $Marker = 0)
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $bytes = New-Object byte[] 512
    $bytes[0] = 0x4D
    $bytes[1] = 0x5A
    $bytes[2] = $Marker
    [BitConverter]::GetBytes([int]0x80).CopyTo($bytes, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 0x80)
    [BitConverter]::GetBytes($Machine).CopyTo($bytes, 0x84)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function New-TestZip {
    param([string] $Source, [string] $Destination)
    Add-Type -AssemblyName System.IO.Compression
    $stream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew)
    try {
        $archive = New-Object IO.Compression.ZipArchive(
            $stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($file in @(Get-ChildItem -LiteralPath $Source -File -Recurse |
                    Sort-Object FullName)) {
                $relative = $file.FullName.Substring($Source.Length + 1).Replace('\','/')
                $entry = $archive.CreateEntry($relative)
                $entry.ExternalAttributes = 0
                $input = [IO.File]::OpenRead($file.FullName)
                $output = $entry.Open()
                try { $input.CopyTo($output) }
                finally { $output.Dispose(); $input.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Write-Sidecar {
    param([string] $Archive)
    $hash = (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash
    [IO.File]::WriteAllText($Archive + '.sha256',
        "$hash  $([IO.Path]::GetFileName($Archive))`n")
}

function New-UnsafeZip {
    param([string] $Destination)
    Add-Type -AssemblyName System.IO.Compression
    $stream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew)
    try {
        $archive = New-Object IO.Compression.ZipArchive(
            $stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $entry = $archive.CreateEntry('../escaped.txt')
            $writer = New-Object IO.StreamWriter($entry.Open())
            try { $writer.Write('unsafe') } finally { $writer.Dispose() }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
    Write-Sidecar -Archive $Destination
}

function New-LiveStageFixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) (
        'blind-soldier-live-stage-test-' + [Guid]::NewGuid().ToString('N'))
    $payload = Join-Path $root 'payload'
    $game = Join-Path $root 'game'
    New-Item -ItemType Directory -Path $payload, $game -Force | Out-Null

    $hostPath = Join-Path $game 'FFVII.exe'
    $stockLauncher = Join-Path $game 'FFVII_LAUNCHER.exe'
    New-TestPe -Path $hostPath -Machine 0x8664 -Marker 1
    New-TestPe -Path $stockLauncher -Machine 0x014C -Marker 2

    New-TestPe -Path (Join-Path $payload 'FFVII_LAUNCHER.exe') `
        -Machine 0x014C -Marker 3
    [IO.File]::WriteAllText((Join-Path $payload 'FFVII_LAUNCHER.exe.config'),
        '<configuration />')
    New-TestPe -Path (Join-Path $payload `
        'launcher_accessibility\native\x86\FFVII_LAUNCHER.prism.x86.dll') `
        -Machine 0x014C -Marker 4
    New-TestPe -Path (Join-Path $payload `
        'Blind-Soldier\Bootstrap\x64\Blind-Soldier-Bootstrap-x64.exe') `
        -Machine 0x8664 -Marker 5
    [IO.File]::WriteAllText((Join-Path $payload 'README-PORTABLE.txt'), 'fixture')

    $proxySource = Join-Path $payload 'proxy.dll'
    New-TestPe -Path $proxySource -Machine 0x014C -Marker 6
    foreach ($relative in @(
        'ff7_en.exe.local\winmm.dll',
        'ff7.exe.local\winmm.dll',
        'ff7\workingdir\ff7_en.exe.local\winmm.dll',
        'ff7\workingdir\ff7.exe.local\winmm.dll')) {
        $target = Join-Path $payload $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force |
            Out-Null
        Copy-Item -LiteralPath $proxySource -Destination $target
    }
    Remove-Item -LiteralPath $proxySource

    $records = @(Get-ChildItem -LiteralPath $payload -File -Recurse |
        Sort-Object { $_.FullName.Substring($payload.Length + 1).Replace('\','/') } |
        ForEach-Object {
            [ordered]@{
                path = $_.FullName.Substring($payload.Length + 1).Replace('\','/')
                length = [int64]$_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        })
    [ordered]@{schemaVersion=1;version='0.1.5';files=$records} |
        ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath (Join-Path $payload 'portable-manifest.json') `
            -Encoding utf8

    $archive = Join-Path $root 'Blind-Soldier-Portable.zip'
    New-TestZip -Source $payload -Destination $archive
    Write-Sidecar -Archive $archive

    $hostHash = (Get-FileHash -LiteralPath $hostPath -Algorithm SHA256).Hash
    $stockLauncherHash = (Get-FileHash -LiteralPath $stockLauncher `
        -Algorithm SHA256).Hash
    $priorLauncher = Join-Path $root 'prior-accessible-launcher.exe'
    New-TestPe -Path $priorLauncher -Machine 0x014C -Marker 7
    $priorLauncherHash = (Get-FileHash -LiteralPath $priorLauncher `
        -Algorithm SHA256).Hash
    $priorProxy = Join-Path $root 'prior-accessible-winmm.dll'
    New-TestPe -Path $priorProxy -Machine 0x014C -Marker 8
    $priorProxyHash = (Get-FileHash -LiteralPath $priorProxy `
        -Algorithm SHA256).Hash
    $supported = Join-Path $root 'supported-hosts.json'
    [ordered]@{
        schemaVersion = 1
        hosts = @([ordered]@{
            fileName='FFVII.exe'; machine=34404; sha256=$hostHash
        })
        stockLauncherSha256 = $stockLauncherHash
    } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $supported -Encoding utf8

    $verifier = Join-Path $root 'Verify-Fixture.ps1'
    [IO.File]::WriteAllText($verifier, @'
param([string] $ArchivePath, [string] $ExpectedVersion)
if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf)) { exit 91 }
if ($ExpectedVersion -cne '0.1.5') { exit 92 }
'@)

    [pscustomobject]@{
        Root=$root
        Payload=$payload
        Game=$game
        Archive=$archive
        Supported=$supported
        Verifier=$verifier
        Backup=(Join-Path $root 'backup')
        HostHash=$hostHash
        StockLauncherHash=$stockLauncherHash
        PackageLauncherHash=(Get-FileHash -LiteralPath `
            (Join-Path $payload 'FFVII_LAUNCHER.exe') -Algorithm SHA256).Hash
        PriorLauncher=$priorLauncher
        PriorLauncherHash=$priorLauncherHash
        PriorProxy=$priorProxy
        PriorProxyHash=$priorProxyHash
        ProxyHash=(Get-FileHash -LiteralPath `
            (Join-Path $payload 'ff7.exe.local\winmm.dll') -Algorithm SHA256).Hash
        EntryCount=@(Get-ChildItem -LiteralPath $payload -File -Recurse).Count
    }
}

function Get-RegistrySafetySnapshot {
    $paths = @(
        'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\ff7.exe',
        'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\ff7_en.exe',
        'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\FFVII.exe',
        'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\ff7.exe',
        'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\ff7_en.exe',
        'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\FFVII.exe',
        'HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x86',
        'HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64'
    )
    $records = foreach ($path in $paths) {
        if (Test-Path -LiteralPath $path) {
            $properties = Get-ItemProperty -LiteralPath $path
            $values = [ordered]@{}
            foreach ($property in @($properties.PSObject.Properties |
                    Where-Object Name -NotMatch '^PS' | Sort-Object Name)) {
                $values[$property.Name] = [string]$property.Value
            }
            [ordered]@{path=$path;exists=$true;values=$values}
        }
        else { [ordered]@{path=$path;exists=$false;values=[ordered]@{}} }
    }
    return ($records | ConvertTo-Json -Depth 6 -Compress)
}

function Invoke-FixtureStage {
    param(
        [psobject] $Fixture,
        [switch] $DryRun,
        [string] $DestinationRoot,
        [string] $BackupRoot
    )
    if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
        $DestinationRoot = $Fixture.Game
    }
    $parameters = @{
        ArchivePath=$Fixture.Archive
        DestinationRoot=$DestinationRoot
        VerifierPath=$Fixture.Verifier
        SupportedHostsPath=$Fixture.Supported
        ExpectedVersion='0.1.5'
    }
    if (-not [string]::IsNullOrWhiteSpace($BackupRoot)) {
        $parameters.BackupRoot = $BackupRoot
    }
    if ($DryRun) { $parameters.DryRun = $true }
    return & $stagerPath @parameters
}

function Get-ThrownMessage {
    param([scriptblock] $Action)
    try { & $Action | Out-Null }
    catch { return $_.Exception.Message }
    throw 'Expected the action to throw, but it completed successfully.'
}

Describe 'Blind Soldier portable live staging safety' {
    BeforeEach { $fixture = New-LiveStageFixture }
    AfterEach {
        if ($null -ne $fixture -and (Test-Path -LiteralPath $fixture.Root)) {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
        $fixture = $null
    }

    It 'refuses a drive root as a deployment target' {
        $message = Get-ThrownMessage {
            Invoke-FixtureStage -Fixture $fixture -DryRun `
            -DestinationRoot ([IO.Path]::GetPathRoot($fixture.Game)) `
            -BackupRoot $fixture.Backup
        }
        $message | Should Match 'drive root'
    }

    It 'refuses a reparse-point destination' {
        $target = Join-Path $fixture.Root 'junction-target'
        $junction = Join-Path $fixture.Root 'junction-game'
        New-Item -ItemType Directory -Path $target | Out-Null
        Copy-Item -LiteralPath (Join-Path $fixture.Game 'FFVII.exe') `
            -Destination $target
        Copy-Item -LiteralPath (Join-Path $fixture.Game 'FFVII_LAUNCHER.exe') `
            -Destination $target
        New-Item -ItemType Junction -Path $junction -Target $target | Out-Null
        $message = Get-ThrownMessage {
            Invoke-FixtureStage -Fixture $fixture -DryRun `
                -DestinationRoot $junction -BackupRoot $fixture.Backup
        }
        $message | Should Match 'reparse'
    }

    It 'refuses an unknown executable-local WinMM proxy' {
        $collision = Join-Path $fixture.Game 'ff7.exe.local\winmm.dll'
        New-TestPe -Path $collision -Machine 0x014C -Marker 99
        $message = Get-ThrownMessage {
            Invoke-FixtureStage -Fixture $fixture -DryRun `
                -BackupRoot $fixture.Backup
        }
        $message | Should Match 'unknown.+winmm\.dll'
    }

    It 'accepts a specifically allowlisted earlier accessible WinMM proxy' {
        $policy = Get-Content -LiteralPath $fixture.Supported -Raw |
            ConvertFrom-Json
        $policy | Add-Member -NotePropertyName accessibleProxySha256 `
            -NotePropertyValue @($fixture.PriorProxyHash)
        $policy | ConvertTo-Json -Depth 5 |
            Set-Content -LiteralPath $fixture.Supported -Encoding utf8
        $collision = Join-Path $fixture.Game 'ff7.exe.local\winmm.dll'
        New-Item -ItemType Directory -Path (Split-Path -Parent $collision) `
            -Force | Out-Null
        Copy-Item -LiteralPath $fixture.PriorProxy -Destination $collision

        $result = Invoke-FixtureStage -Fixture $fixture -DryRun `
            -BackupRoot $fixture.Backup
        $result.Operation | Should Be 'DryRun'
    }

    It 'refuses an unrecognized launcher beside an x64 host' {
        New-TestPe -Path (Join-Path $fixture.Game 'FFVII_LAUNCHER.exe') `
            -Machine 0x014C -Marker 88
        $message = Get-ThrownMessage {
            Invoke-FixtureStage -Fixture $fixture -DryRun `
                -BackupRoot $fixture.Backup
        }
        $message | Should Match 'launcher.+not recognized'
    }

    It 'accepts a specifically allowlisted earlier accessible launcher' {
        $policy = Get-Content -LiteralPath $fixture.Supported -Raw |
            ConvertFrom-Json
        $policy | Add-Member -NotePropertyName accessibleLauncherSha256 `
            -NotePropertyValue @($fixture.PriorLauncherHash)
        $policy | ConvertTo-Json -Depth 5 |
            Set-Content -LiteralPath $fixture.Supported -Encoding utf8
        Copy-Item -LiteralPath $fixture.PriorLauncher -Destination `
            (Join-Path $fixture.Game 'FFVII_LAUNCHER.exe') -Force

        $result = Invoke-FixtureStage -Fixture $fixture -DryRun `
            -BackupRoot $fixture.Backup
        $result.Operation | Should Be 'DryRun'
    }

    It 'refuses an unsafe archive member before invoking the verifier' {
        $unsafe = Join-Path $fixture.Root 'unsafe.zip'
        New-UnsafeZip -Destination $unsafe
        $fixture.Archive = $unsafe
        $message = Get-ThrownMessage {
            Invoke-FixtureStage -Fixture $fixture -DryRun `
                -BackupRoot $fixture.Backup
        }
        $message | Should Match 'unsafe'
    }

    It 'requires a backup ownership location for a nonempty destination' {
        $message = Get-ThrownMessage {
            Invoke-FixtureStage -Fixture $fixture -DryRun
        }
        $message | Should Match 'ownership snapshot'
    }

    It 'reports every overlay during dry-run and changes neither files nor registry' {
        $registryBefore = Get-RegistrySafetySnapshot
        $launcherBefore = (Get-FileHash -LiteralPath `
            (Join-Path $fixture.Game 'FFVII_LAUNCHER.exe') -Algorithm SHA256).Hash
        $result = Invoke-FixtureStage -Fixture $fixture -DryRun `
            -BackupRoot $fixture.Backup
        $registryAfter = Get-RegistrySafetySnapshot

        $result.Operation | Should Be 'DryRun'
        @($result.Files).Count | Should Be $fixture.EntryCount
        @($result.Files | Where-Object Action -eq 'Replace').Count |
            Should BeGreaterThan 0
        (Get-FileHash -LiteralPath (Join-Path $fixture.Game `
            'FFVII_LAUNCHER.exe') -Algorithm SHA256).Hash | Should Be $launcherBefore
        (Test-Path -LiteralPath $fixture.Backup) | Should Be $false
        $registryAfter | Should Be $registryBefore
    }

    It 'backs up replaced files and writes an ownership snapshot before overlay' {
        $result = Invoke-FixtureStage -Fixture $fixture `
            -BackupRoot $fixture.Backup
        $result.Operation | Should Be 'Applied'
        (Get-FileHash -LiteralPath (Join-Path $fixture.Game `
            'FFVII_LAUNCHER.exe') -Algorithm SHA256).Hash |
            Should Be $fixture.PackageLauncherHash
        (Get-FileHash -LiteralPath (Join-Path $fixture.Backup `
            'files\FFVII_LAUNCHER.exe') -Algorithm SHA256).Hash |
            Should Be $fixture.StockLauncherHash
        (Test-Path -LiteralPath (Join-Path $fixture.Backup `
            'ownership-snapshot.json') -PathType Leaf) | Should Be $true
        (Test-Path -LiteralPath (Join-Path $fixture.Game `
            'portable-manifest.json') -PathType Leaf) | Should Be $true
    }
}
