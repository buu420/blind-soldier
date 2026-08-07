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
    param(
        [string] $Destination,
        [string] $EntryName = '../escaped.txt'
    )
    Add-Type -AssemblyName System.IO.Compression
    $stream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew)
    try {
        $archive = New-Object IO.Compression.ZipArchive(
            $stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $entry = $archive.CreateEntry($EntryName)
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
        'ff7_en.exe.local\version.dll',
        'ff7.exe.local\version.dll',
        'ff7\workingdir\ff7_en.exe.local\version.dll',
        'ff7\workingdir\ff7.exe.local\version.dll')) {
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
    [ordered]@{schemaVersion=1;version='0.1.6';files=$records} |
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
    $priorProxy = Join-Path $root 'prior-accessible-version.dll'
    New-TestPe -Path $priorProxy -Machine 0x014C -Marker 8
    $priorProxyHash = (Get-FileHash -LiteralPath $priorProxy `
        -Algorithm SHA256).Hash
    $externalFiles = @(
        'AF3DN.P','AF4DN.P','COPYING.TXT','FFNx.pdb','FFNx.toml',
        'steam_api.dll','dinput.dll','AppLoader.dll','AppProxy.dll',
        'AppProxy.runtimeconfig.json','AppWrapper.dll','nethost.dll',
        'FFNx.dll','7H_GameDriver.dll','steam_api64.dll',
        '.7thWrapperProfile','AppLoader.log','AppProxy.bootstrap.log',
        'AppWrapper.bootstrap.log','FFNx.log')
    $externalDirectories = @(
        'ambient','hext','lighting','music','sfx','shaders','time',
        'vibrate','voice')
    $externalRelativePaths = @(
        foreach ($rootRelative in @('', 'ff7\workingdir')) {
            foreach ($fileName in $externalFiles) {
                if ([string]::IsNullOrEmpty($rootRelative)) { $fileName }
                else { Join-Path $rootRelative $fileName }
            }
            foreach ($directoryName in $externalDirectories) {
                $nested = Join-Path $directoryName 'nested\owned.dat'
                if ([string]::IsNullOrEmpty($rootRelative)) { $nested }
                else { Join-Path $rootRelative $nested }
            }
        })
    foreach ($relative in $externalRelativePaths) {
        $path = Join-Path $game $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force |
            Out-Null
        [IO.File]::WriteAllText($path, "external-owner:$relative")
    }
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
if ($ExpectedVersion -cne '0.1.6') { exit 92 }
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
            (Join-Path $payload 'ff7.exe.local\version.dll') -Algorithm SHA256).Hash
        EntryCount=@(Get-ChildItem -LiteralPath $payload -File -Recurse).Count
        ManifestPaths=@($records.path + 'portable-manifest.json' | Sort-Object)
        ExternalRelativePaths=@($externalRelativePaths | Sort-Object)
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
        ExpectedVersion='0.1.6'
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

function Get-FileSafetySnapshot {
    param([string] $Root, [string[]] $RelativePaths)
    return @($RelativePaths | Sort-Object | ForEach-Object {
        $path = Join-Path $Root $_
        $item = Get-Item -LiteralPath $path
        [ordered]@{
            path = $_.Replace('\','/')
            length = [int64]$item.Length
            sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        }
    }) | ConvertTo-Json -Depth 4 -Compress
}

function Update-FixtureArchive {
    param([psobject] $Fixture)
    $manifestPath = Join-Path $Fixture.Payload 'portable-manifest.json'
    Remove-Item -LiteralPath $manifestPath -Force
    $records = @(Get-ChildItem -LiteralPath $Fixture.Payload -File -Recurse |
        Sort-Object FullName | ForEach-Object {
            [ordered]@{
                path=$_.FullName.Substring($Fixture.Payload.Length + 1).Replace('\','/')
                length=[int64]$_.Length
                sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        })
    [ordered]@{schemaVersion=1;version='0.1.6';files=$records} |
        ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath $manifestPath -Encoding utf8
    Remove-Item -LiteralPath $Fixture.Archive,($Fixture.Archive + '.sha256') `
        -Force
    New-TestZip -Source $Fixture.Payload -Destination $Fixture.Archive
    Write-Sidecar -Archive $Fixture.Archive
}

function Add-FixturePayloadFile {
    param(
        [psobject] $Fixture,
        [string] $RelativePath,
        [string] $Content = 'adversarial fixture'
    )
    $path = Join-Path $Fixture.Payload $RelativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force |
        Out-Null
    [IO.File]::WriteAllText($path, $Content)
    Update-FixtureArchive -Fixture $Fixture
    return $path
}

function Get-TestShortPath {
    param([string] $Path)
    if ($null -eq ('BlindSoldierTest.NativeShortPath' -as [type])) {
        Add-Type -Namespace BlindSoldierTest -Name NativeShortPath `
            -MemberDefinition '[System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode, SetLastError=true)] public static extern uint GetShortPathName(string longPath, System.Text.StringBuilder shortPath, uint bufferLength);'
    }
    $buffer = New-Object Text.StringBuilder 32768
    $length = [BlindSoldierTest.NativeShortPath]::GetShortPathName(
        [IO.Path]::GetFullPath($Path), $buffer, [uint32]$buffer.Capacity)
    if ($length -eq 0 -or $length -ge $buffer.Capacity) { return $null }
    return $buffer.ToString()
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

    It 'refuses an unknown executable-local Version proxy' {
        $collision = Join-Path $fixture.Game 'ff7.exe.local\version.dll'
        New-TestPe -Path $collision -Machine 0x014C -Marker 99
        $message = Get-ThrownMessage {
            Invoke-FixtureStage -Fixture $fixture -DryRun `
                -BackupRoot $fixture.Backup
        }
        $message | Should Match 'unknown.+version\.dll'
    }

    It 'backs up a specifically allowlisted earlier accessible Version proxy' {
        $policy = Get-Content -LiteralPath $fixture.Supported -Raw |
            ConvertFrom-Json
        $policy | Add-Member -NotePropertyName accessibleVersionProxySha256 `
            -NotePropertyValue @($fixture.PriorProxyHash)
        $policy | ConvertTo-Json -Depth 5 |
            Set-Content -LiteralPath $fixture.Supported -Encoding utf8
        $collision = Join-Path $fixture.Game 'ff7.exe.local\version.dll'
        New-Item -ItemType Directory -Path (Split-Path -Parent $collision) `
            -Force | Out-Null
        Copy-Item -LiteralPath $fixture.PriorProxy -Destination $collision

        $result = Invoke-FixtureStage -Fixture $fixture `
            -BackupRoot $fixture.Backup
        $result.Operation | Should Be 'Applied'
        (Get-FileHash -LiteralPath $collision -Algorithm SHA256).Hash |
            Should Be $fixture.ProxyHash
        (Get-FileHash -LiteralPath (Join-Path $fixture.Backup `
            'files\ff7.exe.local\version.dll') -Algorithm SHA256).Hash |
            Should Be $fixture.PriorProxyHash
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

    It 'refuses to stage files owned by stock 7th Heaven or FFNx' {
        $externalMember = Join-Path $fixture.Payload `
            'Blind-Soldier\embedded\dinput.dll'
        New-Item -ItemType Directory -Path (Split-Path -Parent $externalMember) -Force | Out-Null
        [IO.File]::WriteAllText($externalMember,
            'package must not own this file')
        Update-FixtureArchive -Fixture $fixture
        $message = Get-ThrownMessage {
            Invoke-FixtureStage -Fixture $fixture -DryRun `
                -BackupRoot $fixture.Backup
        }
        $message | Should Match '7th Heaven|FFNx|external'
    }

    It 'refuses every recognized FFNx runtime entry point in an archive' {
        $unexpected = New-Object 'System.Collections.Generic.List[string]'
        foreach ($fileName in @('FFNx.dll','7H_GameDriver.dll','steam_api64.dll')) {
            $externalMember = Join-Path $fixture.Payload `
                ('Blind-Soldier\embedded\{0}' -f $fileName)
            New-Item -ItemType Directory -Path (Split-Path -Parent $externalMember) `
                -Force | Out-Null
            [IO.File]::WriteAllText($externalMember,
                'package must not own this file')
            Update-FixtureArchive -Fixture $fixture
            try {
                Invoke-FixtureStage -Fixture $fixture -DryRun `
                    -BackupRoot $fixture.Backup | Out-Null
                $unexpected.Add(('{0}: completed' -f $fileName))
            }
            catch {
                if ($_.Exception.Message -notmatch '7th Heaven|FFNx|external') {
                    $unexpected.Add(('{0}: {1}' -f $fileName,
                        $_.Exception.Message))
                }
            }
            Remove-Item -LiteralPath $externalMember -Force
        }
        ($unexpected -join '; ') | Should Be ''
    }

    It 'rejects the pinned FFNx tree only at its canonical deployment roots' {
        $unexpected = New-Object 'System.Collections.Generic.List[string]'
        foreach ($relative in @(
            'COPYING.TXT',
            'ambient\nested\field.wav',
            'ff7\workingdir\FFNx.pdb',
            'ff7\workingdir\ShAdErS\nested\effect.fx',
            '.7thWrapperProfile',
            'ff7\workingdir\AppLoader.log')) {
            $path = Add-FixturePayloadFile -Fixture $fixture -RelativePath $relative
            try {
                Invoke-FixtureStage -Fixture $fixture -DryRun `
                    -BackupRoot $fixture.Backup | Out-Null
                $unexpected.Add("$relative`: completed")
            }
            catch {
                if ($_.Exception.Message -notmatch '7th Heaven|FFNx|external') {
                    $unexpected.Add("$relative`: $($_.Exception.Message)")
                }
            }
            Remove-Item -LiteralPath $path -Force
        }
        ($unexpected -join '; ') | Should Be ''
    }

    It 'allows a Blind Soldier owned directory whose leaf matches an FFNx prefix' {
        Add-FixturePayloadFile -Fixture $fixture `
            -RelativePath 'Blind-Soldier\Assets\music\owned.ogg' | Out-Null
        $result = Invoke-FixtureStage -Fixture $fixture -DryRun `
            -BackupRoot $fixture.Backup
        (@($result.Files.RelativePath) -contains `
            'Blind-Soldier/Assets/music/owned.ogg') | Should Be $true
    }

    It 'rejects a BackupRoot spelled through an 8.3 alias inside DestinationRoot' {
        $inside = Join-Path $fixture.Game `
            'Backup Ownership Location With Spaces'
        New-Item -ItemType Directory -Path $inside -Force | Out-Null
        $shortInside = Get-TestShortPath -Path $inside
        if ([string]::IsNullOrWhiteSpace($shortInside) -or
            $shortInside -notmatch '~') {
            Write-Warning 'This volume does not expose an 8.3 alias; alias assertion skipped.'
            $true | Should Be $true
            return
        }
        $message = Get-ThrownMessage {
            Invoke-FixtureStage -Fixture $fixture -DryRun `
                -BackupRoot (Join-Path $shortInside 'snapshot')
        }
        $message | Should Match 'BackupRoot cannot be inside DestinationRoot'
    }

    It 'rejects an existing FFNx file reached through its Windows short-name alias' {
        $protected = Join-Path $fixture.Game 'FFNx.toml'
        $shortPath = Get-TestShortPath -Path $protected
        $shortLeaf = if ([string]::IsNullOrWhiteSpace($shortPath)) { $null }
            else { Split-Path -Leaf $shortPath }
        if ([string]::IsNullOrWhiteSpace($shortLeaf) -or
            $shortLeaf -ieq 'FFNx.toml' -or $shortLeaf -notmatch '~') {
            Write-Warning 'This volume does not expose an 8.3 alias; alias assertion skipped.'
            $true | Should Be $true
            return
        }
        $before = (Get-FileHash -LiteralPath $protected -Algorithm SHA256).Hash
        Add-FixturePayloadFile -Fixture $fixture -RelativePath $shortLeaf `
            -Content 'must never reach the external FFNx file' | Out-Null
        $message = Get-ThrownMessage {
            Invoke-FixtureStage -Fixture $fixture -DryRun `
                -BackupRoot $fixture.Backup
        }
        $message | Should Match '7th Heaven|FFNx|external|alias'
        (Get-FileHash -LiteralPath $protected -Algorithm SHA256).Hash |
            Should Be $before
    }

    It 'rejects a hard-linked target before any overlay or backup' {
        $protected = Join-Path $fixture.Game 'FFNx.toml'
        $ownedTarget = Join-Path $fixture.Game 'Blind-Soldier\owned.txt'
        Add-FixturePayloadFile -Fixture $fixture `
            -RelativePath 'Blind-Soldier\owned.txt' `
            -Content 'replacement through a hard link' | Out-Null
        New-Item -ItemType Directory -Path (Split-Path -Parent $ownedTarget) `
            -Force | Out-Null
        New-Item -ItemType HardLink -Path $ownedTarget -Target $protected | Out-Null
        $externalBefore = (Get-FileHash -LiteralPath $protected -Algorithm SHA256).Hash
        $launcherBefore = (Get-FileHash -LiteralPath (Join-Path $fixture.Game `
            'FFVII_LAUNCHER.exe') -Algorithm SHA256).Hash
        $message = Get-ThrownMessage {
            Invoke-FixtureStage -Fixture $fixture -BackupRoot $fixture.Backup
        }
        $message | Should Match 'hard link|external'
        (Get-FileHash -LiteralPath $protected -Algorithm SHA256).Hash |
            Should Be $externalBefore
        (Get-FileHash -LiteralPath (Join-Path $fixture.Game `
            'FFVII_LAUNCHER.exe') -Algorithm SHA256).Hash | Should Be $launcherBefore
        Test-Path -LiteralPath (Join-Path $fixture.Game 'portable-manifest.json') |
            Should Be $false
        Test-Path -LiteralPath $fixture.Backup | Should Be $false
    }

    It 'rolls back earlier overlays when a later copy fails' {
        $firstRelative = 'Blind-Soldier\a-first.txt'
        $laterRelative = 'Blind-Soldier\z-readonly.txt'
        Add-FixturePayloadFile -Fixture $fixture -RelativePath $firstRelative `
            -Content 'new first content' | Out-Null
        Add-FixturePayloadFile -Fixture $fixture -RelativePath $laterRelative `
            -Content 'new later content' | Out-Null
        $firstTarget = Join-Path $fixture.Game $firstRelative
        $laterTarget = Join-Path $fixture.Game $laterRelative
        New-Item -ItemType Directory -Path (Split-Path -Parent $firstTarget) `
            -Force | Out-Null
        [IO.File]::WriteAllText($firstTarget, 'original first content')
        [IO.File]::WriteAllText($laterTarget, 'original later content')
        $firstBefore = (Get-FileHash -LiteralPath $firstTarget `
            -Algorithm SHA256).Hash
        $laterBefore = (Get-FileHash -LiteralPath $laterTarget `
            -Algorithm SHA256).Hash
        $launcher = Join-Path $fixture.Game 'FFVII_LAUNCHER.exe'
        $launcherBefore = (Get-FileHash -LiteralPath $launcher `
            -Algorithm SHA256).Hash
        $laterItem = Get-Item -LiteralPath $laterTarget -Force
        $laterItem.Attributes = $laterItem.Attributes -bor `
            [IO.FileAttributes]::ReadOnly
        try {
            $message = Get-ThrownMessage {
                Invoke-FixtureStage -Fixture $fixture `
                    -BackupRoot $fixture.Backup
            }
        }
        finally {
            $laterItem = Get-Item -LiteralPath $laterTarget -Force
            $laterItem.Attributes = $laterItem.Attributes -band `
                (-bnot [IO.FileAttributes]::ReadOnly)
        }
        $message | Should Match 'access|denied|read-only'
        (Get-FileHash -LiteralPath $firstTarget -Algorithm SHA256).Hash |
            Should Be $firstBefore
        (Get-FileHash -LiteralPath $laterTarget -Algorithm SHA256).Hash |
            Should Be $laterBefore
        (Get-FileHash -LiteralPath $launcher -Algorithm SHA256).Hash |
            Should Be $launcherBefore
        Test-Path -LiteralPath (Join-Path $fixture.Game 'portable-manifest.json') |
            Should Be $false
    }

    It 'keeps post-copy external validation inside the rollback transaction' {
        $tokens = $null
        $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile(
            $stagerPath, [ref]$tokens, [ref]$errors)
        @($errors).Count | Should Be 0
        $transactions = @($ast.FindAll({
            param($node)
            $node -is [Management.Automation.Language.TryStatementAst] -and
                $node.Body.Extent.Text -match '\[IO\.File\]::Copy' -and
                $node.CatchClauses.Count -eq 1 -and
                $node.Body.Extent.Text -match `
                    'Assert-ExternalOwnershipUnchanged'
        }, $true))
        $transactions.Count | Should Be 1
        $transactions[0].CatchClauses.Count | Should Be 1
        $transactions[0].CatchClauses[0].Body.Extent.Text |
            Should Match 'BeforeExists'
        $transactions[0].CatchClauses[0].Body.Extent.Text |
            Should Match 'externalAfterRollback'
        $transactions[0].CatchClauses[0].Body.Extent.Text |
            Should Match 'Assert-TargetPathSegments'
        $transactions[0].CatchClauses[0].Body.Extent.Text |
            Should Match 'backupFilesRoot'
        $transactions[0].Body.Extent.Text |
            Should Match 'copyCanonicalTargets'
    }
    It 'default policy recognizes prior Blind Soldier Version proxies' {
        $tokens = $null
        $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile(
            $stagerPath, [ref]$tokens, [ref]$errors)
        @($errors).Count | Should Be 0
        $functionAst = @($ast.FindAll({
            param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -ceq 'Get-SupportedHostPolicy'
        }, $true))[0]
        $functionAst | Should Not BeNullOrEmpty
        $module = New-Module -ScriptBlock ([scriptblock]::Create(
            $functionAst.Extent.Text))
        try { $policy = & $module { Get-SupportedHostPolicy } }
        finally { Remove-Module $module -Force }
        $policy.PSObject.Properties.Name |
            Should Contain 'accessibleVersionProxySha256'
        @($policy.accessibleVersionProxySha256) | Should Contain `
            '64E2803E3E321581FF0A58E64543BD082FFD6272941FEDB5BB3F14DCC79B7C90'
        @($policy.accessibleVersionProxySha256) | Should Contain `
            'E46DC04803F56C880D7753003F7EED73754F6B2C07D1BCFB48BCCC4DE8AA8E82'
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

    It 'independently rejects a trailing-dot ZIP component before its custom verifier' {
        $unsafe = Join-Path $fixture.Root 'trailing-dot.zip'
        New-UnsafeZip -Destination $unsafe `
            -EntryName 'ff7/workingdir/FFNx.toml.'
        $fixture.Archive = $unsafe
        $message = Get-ThrownMessage {
            Invoke-FixtureStage -Fixture $fixture -DryRun `
                -BackupRoot $fixture.Backup
        }
        $message | Should Match 'unsafe Windows path component'
    }

    It 'independently rejects a trailing-space ZIP component before its custom verifier' {
        $unsafe = Join-Path $fixture.Root 'trailing-space.zip'
        New-UnsafeZip -Destination $unsafe `
            -EntryName 'ff7/workingdir/FFNx.toml '
        $fixture.Archive = $unsafe
        $message = Get-ThrownMessage {
            Invoke-FixtureStage -Fixture $fixture -DryRun `
                -BackupRoot $fixture.Backup
        }
        $message | Should Match 'unsafe Windows path component'
    }
    It 'requires a backup ownership location for a nonempty destination' {
        $message = Get-ThrownMessage {
            Invoke-FixtureStage -Fixture $fixture -DryRun
        }
        $message | Should Match 'ownership snapshot'
    }

    It 'reports every overlay during dry-run and changes neither files nor registry' {
        $registryBefore = Get-RegistrySafetySnapshot
        $externalBefore = Get-FileSafetySnapshot -Root $fixture.Game `
            -RelativePaths $fixture.ExternalRelativePaths
        $launcherBefore = (Get-FileHash -LiteralPath `
            (Join-Path $fixture.Game 'FFVII_LAUNCHER.exe') -Algorithm SHA256).Hash
        $result = Invoke-FixtureStage -Fixture $fixture -DryRun `
            -BackupRoot $fixture.Backup
        $registryAfter = Get-RegistrySafetySnapshot
        $externalAfter = Get-FileSafetySnapshot -Root $fixture.Game `
            -RelativePaths $fixture.ExternalRelativePaths

        $result.Operation | Should Be 'DryRun'
        @($result.Files.RelativePath | Sort-Object) | Should Be $fixture.ManifestPaths
        $actualExternalFiles = @($result.ExternalFiles |
            Where-Object Type -eq 'File' | ForEach-Object RelativePath |
            Sort-Object)
        $expectedExternalFiles = @($fixture.ExternalRelativePaths |
            ForEach-Object { $_.Replace('\','/') } | Sort-Object)
        ($actualExternalFiles -join '|') |
            Should Be ($expectedExternalFiles -join '|')
        @($result.ExternalFiles | Where-Object Type -eq 'Directory').Count |
            Should Be 36
        @($result.Files | Where-Object Action -eq 'Replace').Count |
            Should BeGreaterThan 0
        (Get-FileHash -LiteralPath (Join-Path $fixture.Game `
            'FFVII_LAUNCHER.exe') -Algorithm SHA256).Hash | Should Be $launcherBefore
        (Test-Path -LiteralPath $fixture.Backup) | Should Be $false
        $registryAfter | Should Be $registryBefore
        $externalAfter | Should Be $externalBefore
    }

    It 'backs up replaced files and writes an ownership snapshot before overlay' {
        $externalBefore = Get-FileSafetySnapshot -Root $fixture.Game `
            -RelativePaths $fixture.ExternalRelativePaths
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
        (Get-FileSafetySnapshot -Root $fixture.Game `
            -RelativePaths $fixture.ExternalRelativePaths) |
            Should Be $externalBefore
    }
}
