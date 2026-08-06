$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$builderPath = Join-Path $scriptRoot 'Build-BlindSwordsmanPrerequisiteBundle.ps1'
$reloadedLoaderFiles = @(
    'Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll',
    'Colorful.Console.dll',
    'DelayInjectHooks.json',
    'Indieteur.SAMAPI.dll',
    'Indieteur.VDFAPI.dll',
    'McMaster.NETCore.Plugins.dll',
    'Reloaded.Memory.dll',
    'Reloaded.Mod.Interfaces.dll',
    'Reloaded.Mod.Loader.deps.json',
    'Reloaded.Mod.Loader.dll',
    'Reloaded.Mod.Loader.IO.dll',
    'Reloaded.Mod.Loader.runtimeconfig.json'
)

function New-TestPe {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [uint16] $Machine
    )

    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $bytes = New-Object byte[] 512
    $bytes[0] = 0x4D
    $bytes[1] = 0x5A
    [BitConverter]::GetBytes([int]0x80).CopyTo($bytes, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 0x80)
    [BitConverter]::GetBytes($Machine).CopyTo($bytes, 0x84)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function New-TestZip {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [hashtable] $Entries
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stream = New-Object IO.FileStream($Path, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($name in $Entries.Keys) {
                $entry = $archive.CreateEntry($name)
                $entryStream = $entry.Open()
                try {
                    $value = $Entries[$name]
                    $bytes = if ($value -is [byte[]]) {
                        $value
                    }
                    else {
                        [Text.Encoding]::UTF8.GetBytes([string]$value)
                    }
                    $entryStream.Write($bytes, 0, $bytes.Length)
                }
                finally {
                    $entryStream.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function New-PrerequisiteFixture {
    param(
        [switch] $UnsafeReloadedZip,
        [int] $MaximumExtractorDestinationLength = 0
    )

    $root = Join-Path ([IO.Path]::GetTempPath()) ('blind-soldier-prereq-test-' + [Guid]::NewGuid().ToString('N'))
    $sources = Join-Path $root 'sources'
    $sevenZipContent = Join-Path $root 'sevenzip-content'
    New-Item -ItemType Directory -Path $sources, $sevenZipContent -Force | Out-Null

    $reloadedZip = Join-Path $sources 'Release.zip'
    $reloadedEntries = @{
        'Reloaded-II.exe' = 'fixture reloaded'
        'Reloaded-II.dll' = 'fixture manager implementation'
        'Themes/Default.xaml' = 'fixture manager theme'
        'Updater/Reloaded.Mod.Loader.Update.dll' = 'fixture updater'
        'Loader/Asi/UltimateAsiLoader.7z' = 'fixture nested archive'
        'LICENSE.txt' = 'fixture Reloaded license'
    }
    foreach ($architecture in @('X86', 'X64')) {
        foreach ($relative in $reloadedLoaderFiles) {
            $reloadedEntries[("Loader/{0}/{1}" -f $architecture, $relative.Replace('\', '/'))] =
                "fixture $architecture $relative"
        }
    }
    if ($UnsafeReloadedZip) {
        $reloadedEntries['../outside.txt'] = 'must not escape'
    }
    New-TestZip -Path $reloadedZip -Entries $reloadedEntries

    $sharedHooksArchive = Join-Path $sources 'Reloaded.Hooks.ReloadedII1.16.3.7z'
    [IO.File]::WriteAllText($sharedHooksArchive, 'fixture shared hooks archive')
    $x86Runtime = Join-Path $sources 'windowsdesktop-runtime-9.0.8-win-x86.exe'
    $x64Runtime = Join-Path $sources 'windowsdesktop-runtime-9.0.8-win-x64.exe'
    New-TestPe -Path $x86Runtime -Machine 0x014C
    New-TestPe -Path $x64Runtime -Machine 0x8664
    $ffnxSource = Join-Path $root 'ffnx-source'
    New-TestPe -Path (Join-Path $ffnxSource 'AF3DN.P') -Machine 0x014C
    New-TestPe -Path (Join-Path $ffnxSource 'AF4DN.P') -Machine 0x014C
    New-TestPe -Path (Join-Path $ffnxSource 'steam_api.dll') -Machine 0x014C
    New-Item -ItemType Directory -Path (Join-Path $ffnxSource 'shaders') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $ffnxSource 'FFNx.toml'), 'fixture FFNx configuration')
    [IO.File]::WriteAllText((Join-Path $ffnxSource 'COPYING.TXT'), 'fixture FFNx GPL license')
    [IO.File]::WriteAllText((Join-Path $ffnxSource 'FFNx.pdb'), 'debug symbols must not ship')
    [IO.File]::WriteAllText((Join-Path $ffnxSource 'shaders\fixture.fx'), 'fixture shader')
    $ffnxEntries = @{}
    foreach ($file in @(Get-ChildItem -LiteralPath $ffnxSource -File -Recurse)) {
        $relative = $file.FullName.Substring($ffnxSource.Length + 1).Replace('\','/')
        $ffnxEntries[$relative] = [IO.File]::ReadAllBytes($file.FullName)
    }
    $ffnxArchive = Join-Path $sources 'FFNx-Steam-v1.24.3.0.zip'
    New-TestZip -Path $ffnxArchive -Entries $ffnxEntries
    $hooksLicense = Join-Path $sources 'Reloaded-Shared-Hooks-LGPL-3.0.txt'
    $dotnetLicense = Join-Path $sources 'dotnet-LICENSE.txt'
    $dotnetNotices = Join-Path $sources 'dotnet-THIRD-PARTY-NOTICES.txt'
    [IO.File]::WriteAllText($hooksLicense, 'fixture Shared Hooks license')
    [IO.File]::WriteAllText($dotnetLicense, 'fixture dotnet license')
    [IO.File]::WriteAllText($dotnetNotices, 'fixture dotnet notices')

    $asiContent = Join-Path $sevenZipContent 'UltimateAsiLoader.7z'
    New-TestPe -Path (Join-Path $asiContent 'ASILoader32.dll') -Machine 0x014C
    New-TestPe -Path (Join-Path $asiContent 'ASILoader64.dll') -Machine 0x8664
    $hooksContent = Join-Path $sevenZipContent 'Reloaded.Hooks.ReloadedII1.16.3.7z'
    New-Item -ItemType Directory -Path (Join-Path $hooksContent 'x86'), (Join-Path $hooksContent 'x64') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $hooksContent 'ModConfig.json'), '{"ModId":"reloaded.sharedlib.hooks","ModVersion":"1.16.3"}')
    New-TestPe -Path (Join-Path $hooksContent 'x86\Reloaded.Hooks.ReloadedII.dll') -Machine 0x014C
    New-TestPe -Path (Join-Path $hooksContent 'x64\Reloaded.Hooks.ReloadedII.dll') -Machine 0x8664

    $bootstrapX86 = Join-Path $root 'bootstrap-x86.dll'
    $bootstrapX64 = Join-Path $root 'bootstrap-x64.dll'
    New-TestPe -Path $bootstrapX86 -Machine 0x014C
    New-TestPe -Path $bootstrapX64 -Machine 0x8664

    $record = {
        param($path, $url, $architecture, $name)
        $item = Get-Item -LiteralPath $path
        [ordered]@{
            architecture = $architecture
            name = $name
            url = $url
            size = $item.Length
            sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            sha512 = (Get-FileHash -LiteralPath $path -Algorithm SHA512).Hash
        }
    }
    $lock = [ordered]@{
        schemaVersion = 1
        reloaded = [ordered]@{
            version = '1.30.3'; assetName = 'Release.zip'; url = 'https://fixture.invalid/Release.zip'
            size = (Get-Item $reloadedZip).Length; sha256 = (Get-FileHash $reloadedZip -Algorithm SHA256).Hash
            sourceCodeUrl = 'https://fixture.invalid/reloaded/source'; licensePath = 'LICENSE.txt'
            licenseSize = ([Text.Encoding]::UTF8.GetByteCount('fixture Reloaded license'))
            licenseSha256 = (Get-FileHash -LiteralPath (Join-Path $sources 'Reloaded-license.tmp') -Algorithm SHA256 -ErrorAction SilentlyContinue).Hash
        }
        sharedHooks = [ordered]@{
            version = '1.16.3'; assetName = 'Reloaded.Hooks.ReloadedII1.16.3.7z'
            url = 'https://fixture.invalid/Reloaded.Hooks.ReloadedII1.16.3.7z'
            size = (Get-Item $sharedHooksArchive).Length; sha256 = (Get-FileHash $sharedHooksArchive -Algorithm SHA256).Hash
            sourceCodeUrl = 'https://fixture.invalid/hooks/source'; licenseName = 'Reloaded-Shared-Hooks-LGPL-3.0.txt'
            licenseUrl = 'https://fixture.invalid/Reloaded-Shared-Hooks-LGPL-3.0.txt'
            licenseSize = (Get-Item $hooksLicense).Length; licenseSha256 = (Get-FileHash $hooksLicense -Algorithm SHA256).Hash
        }
        ffnx = [ordered]@{
            version = '1.24.3.0'; assetName = 'FFNx-Steam-v1.24.3.0.zip'
            url = 'https://fixture.invalid/FFNx-Steam-v1.24.3.0.zip'
            size = (Get-Item $ffnxArchive).Length
            sha256 = (Get-FileHash $ffnxArchive -Algorithm SHA256).Hash
            sourceCodeUrl = 'https://fixture.invalid/ffnx/source'
            licensePath = 'COPYING.TXT'; licenseName = 'FFNx-GPL-3.0.txt'
            licenseSize = (Get-Item (Join-Path $ffnxSource 'COPYING.TXT')).Length
            licenseSha256 = (Get-FileHash (Join-Path $ffnxSource 'COPYING.TXT') -Algorithm SHA256).Hash
        }
        dotnetDesktopRuntime = [ordered]@{
            version = '9.0.8'; sourceCodeUrl = 'https://fixture.invalid/dotnet/source'
            licenseName = 'dotnet-LICENSE.txt'; licenseUrl = 'https://fixture.invalid/dotnet-LICENSE.txt'
            licenseSize = (Get-Item $dotnetLicense).Length; licenseSha256 = (Get-FileHash $dotnetLicense -Algorithm SHA256).Hash
            thirdPartyNoticesName = 'dotnet-THIRD-PARTY-NOTICES.txt'
            thirdPartyNoticesUrl = 'https://fixture.invalid/dotnet-THIRD-PARTY-NOTICES.txt'
            thirdPartyNoticesSize = (Get-Item $dotnetNotices).Length
            thirdPartyNoticesSha256 = (Get-FileHash $dotnetNotices -Algorithm SHA256).Hash
            installers = @(
                (& $record $x86Runtime 'https://fixture.invalid/windowsdesktop-runtime-9.0.8-win-x86.exe' 'x86' 'windowsdesktop-runtime-9.0.8-win-x86.exe'),
                (& $record $x64Runtime 'https://fixture.invalid/windowsdesktop-runtime-9.0.8-win-x64.exe' 'x64' 'windowsdesktop-runtime-9.0.8-win-x64.exe')
            )
            portableArchives = @(
                [ordered]@{
                    architecture='x86'; component='core'; name='dotnet-runtime-9.0.8-win-x86.zip'
                    url='https://fixture.invalid/dotnet-runtime-9.0.8-win-x86.zip'; sha512=('A' * 128)
                },
                [ordered]@{
                    architecture='x86'; component='windowsDesktop'; name='windowsdesktop-runtime-9.0.8-win-x86.zip'
                    url='https://fixture.invalid/windowsdesktop-runtime-9.0.8-win-x86.zip'; sha512=('B' * 128)
                },
                [ordered]@{
                    architecture='x64'; component='core'; name='dotnet-runtime-9.0.8-win-x64.zip'
                    url='https://fixture.invalid/dotnet-runtime-9.0.8-win-x64.zip'; sha512=('C' * 128)
                },
                [ordered]@{
                    architecture='x64'; component='windowsDesktop'; name='windowsdesktop-runtime-9.0.8-win-x64.zip'
                    url='https://fixture.invalid/windowsdesktop-runtime-9.0.8-win-x64.zip'; sha512=('D' * 128)
                }
            )
        }
    }
    $licenseBytes = [Text.Encoding]::UTF8.GetBytes('fixture Reloaded license')
    $licenseHash = [Security.Cryptography.SHA256]::Create()
    try { $lock.reloaded.licenseSha256 = ([BitConverter]::ToString($licenseHash.ComputeHash($licenseBytes))).Replace('-', '') } finally { $licenseHash.Dispose() }
    $lockPath = Join-Path $root 'dependency-lock.json'
    [IO.File]::WriteAllText($lockPath, ($lock | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
    $noticePath = Join-Path $root 'THIRD-PARTY-NOTICES.md'
    [IO.File]::WriteAllText($noticePath, 'fixture prerequisite notices')

    $artifactResolver = {
        param($url, $destination)
        $name = [IO.Path]::GetFileName(([Uri]$url).AbsolutePath)
        Copy-Item -LiteralPath (Join-Path $sources $name) -Destination $destination
    }.GetNewClosure()
    $sevenZipExtractor = {
        param($archivePath, $destination)
        if ($MaximumExtractorDestinationLength -gt 0 -and
            [IO.Path]::GetFullPath($destination).Length -gt $MaximumExtractorDestinationLength) {
            throw 'Fixture extractor rejected an overlong destination.'
        }
        $name = Split-Path -Leaf $archivePath
        foreach ($item in @(Get-ChildItem -LiteralPath (Join-Path $sevenZipContent $name) -Force)) {
            Copy-Item -LiteralPath $item.FullName -Destination $destination -Recurse
        }
    }.GetNewClosure()

    return [pscustomobject]@{
        Root = $root
        Output = Join-Path $root 'output'
        LockPath = $lockPath
        NoticePath = $noticePath
        ArtifactResolver = $artifactResolver
        SevenZipExtractor = $sevenZipExtractor
        BootstrapX86 = $bootstrapX86
        BootstrapX64 = $bootstrapX64
    }
}

function Invoke-PrerequisiteFixtureBuild {
    param([psobject] $Fixture, [string] $Output)
    & $builderPath -OutputPath $Output -LockPath $Fixture.LockPath `
        -NoticePath $Fixture.NoticePath `
        -ArtifactResolver $Fixture.ArtifactResolver `
        -SevenZipExtractor $Fixture.SevenZipExtractor `
        -BootstrapperX86Override $Fixture.BootstrapX86 `
        -BootstrapperX64Override $Fixture.BootstrapX64 | Out-Null
}

Describe 'Blind Soldier prerequisite bundle builder' {
    AfterEach {
        if ($null -ne $fixture -and (Test-Path -LiteralPath $fixture.Root)) {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force
        }
        $fixture = $null
    }

    It 'builds the exact headless Reloaded closure with Shared Hooks, FFNx, dotnet, and notices' {
        $fixture = New-PrerequisiteFixture
        & $builderPath -OutputPath $fixture.Output -LockPath $fixture.LockPath -NoticePath $fixture.NoticePath `
            -ArtifactResolver $fixture.ArtifactResolver -SevenZipExtractor $fixture.SevenZipExtractor `
            -BootstrapperX86Override $fixture.BootstrapX86 -BootstrapperX64Override $fixture.BootstrapX64 | Out-Null

        foreach ($relative in @(
            'dependency-bundle.json',
            'reloaded\_asi_extract\ASILoader32.dll',
            'reloaded\_asi_extract\ASILoader64.dll',
            'reloaded\Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll',
            'reloaded\Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll',
            'shared-hooks\ModConfig.json',
            'shared-hooks\x86\Reloaded.Hooks.ReloadedII.dll',
            'shared-hooks\x64\Reloaded.Hooks.ReloadedII.dll',
            'ffnx\AF3DN.P',
            'ffnx\AF4DN.P',
            'ffnx\FFNx.toml',
            'ffnx\steam_api.dll',
            'ffnx\shaders\fixture.fx',
            'dotnet\windowsdesktop-runtime-9.0.8-win-x86.exe',
            'dotnet\windowsdesktop-runtime-9.0.8-win-x64.exe',
            'notices\THIRD-PARTY-NOTICES.md',
            'notices\Reloaded-II-GPL-3.0.txt',
            'notices\Reloaded-Shared-Hooks-LGPL-3.0.txt',
            'notices\FFNx-GPL-3.0.txt',
            'notices\dotnet-LICENSE.txt',
            'notices\dotnet-THIRD-PARTY-NOTICES.txt'
        )) {
            Test-Path -LiteralPath (Join-Path $fixture.Output $relative) -PathType Leaf | Should Be $true
        }
        $manifest = [IO.File]::ReadAllText((Join-Path $fixture.Output 'dependency-bundle.json')) | ConvertFrom-Json
        $manifest.schemaVersion | Should Be 1
        $manifest.reloaded.version | Should Be '1.30.3'
        $manifest.sharedHooks.version | Should Be '1.16.3'
        $manifest.ffnx.version | Should Be '1.24.3.0'
        $manifest.dotnetDesktopRuntime.version | Should Be '9.0.8'

        $expectedReloadedFiles = New-Object 'System.Collections.Generic.List[string]'
        foreach ($architecture in @('X86', 'X64')) {
            foreach ($relative in $reloadedLoaderFiles) {
                $expectedReloadedFiles.Add("Loader\$architecture\$relative")
            }
        }
        $expectedReloadedFiles.Add('_asi_extract\ASILoader32.dll')
        $expectedReloadedFiles.Add('_asi_extract\ASILoader64.dll')
        $reloadedRoot = Join-Path $fixture.Output 'reloaded'
        $actualReloadedFiles = @(Get-ChildItem -LiteralPath $reloadedRoot -File -Recurse |
            ForEach-Object { $_.FullName.Substring($reloadedRoot.Length + 1) } | Sort-Object)
        $expectedSorted = @($expectedReloadedFiles.ToArray() | Sort-Object)
        ($actualReloadedFiles -join '|') | Should Be ($expectedSorted -join '|')
        Test-Path -LiteralPath (Join-Path $reloadedRoot 'Reloaded-II.exe') | Should Be $false
        Test-Path -LiteralPath (Join-Path $fixture.Output 'dotnet\dotnet-runtime-9.0.8-win-x64.zip') | Should Be $false
        Test-Path -LiteralPath (Join-Path $fixture.Output 'dotnet\windowsdesktop-runtime-9.0.8-win-x64.zip') | Should Be $false
        Test-Path -LiteralPath (Join-Path $fixture.Output 'ffnx\FFNx.pdb') | Should Be $false
    }

    It 'keeps private archive extraction staging short for a deeply nested release output' {
        $fixture = New-PrerequisiteFixture -MaximumExtractorDestinationLength 180
        $deepParent = $fixture.Root
        foreach ($index in 1..6) { $deepParent = Join-Path $deepParent ('release-segment-' + $index) }
        $deepOutput = Join-Path $deepParent 'prerequisites'

        & $builderPath -OutputPath $deepOutput -LockPath $fixture.LockPath -NoticePath $fixture.NoticePath `
            -ArtifactResolver $fixture.ArtifactResolver -SevenZipExtractor $fixture.SevenZipExtractor `
            -BootstrapperX86Override $fixture.BootstrapX86 -BootstrapperX64Override $fixture.BootstrapX64 | Out-Null

        Test-Path -LiteralPath (Join-Path $deepOutput 'dependency-bundle.json') -PathType Leaf | Should Be $true
    }

    It 'rejects a digest mismatch without publishing a partial output' {
        $fixture = New-PrerequisiteFixture
        $lock = [IO.File]::ReadAllText($fixture.LockPath) | ConvertFrom-Json
        $lock.reloaded.sha256 = 'A' * 64
        [IO.File]::WriteAllText($fixture.LockPath, ($lock | ConvertTo-Json -Depth 8))

        { Invoke-PrerequisiteFixtureBuild -Fixture $fixture -Output $fixture.Output } |
            Should Throw 'locked size or cryptographic digest check'
        Test-Path -LiteralPath $fixture.Output | Should Be $false
    }

    It 'rejects parent traversal members before extracting a zip' {
        $fixture = New-PrerequisiteFixture -UnsafeReloadedZip
        { Invoke-PrerequisiteFixtureBuild -Fixture $fixture -Output $fixture.Output } |
            Should Throw 'unsafe path member'
        Test-Path -LiteralPath (Join-Path $fixture.Root 'outside.txt') | Should Be $false
        Test-Path -LiteralPath $fixture.Output | Should Be $false
    }
}
