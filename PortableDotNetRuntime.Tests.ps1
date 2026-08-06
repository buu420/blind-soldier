$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$modulePath = Join-Path $scriptRoot 'PortableDotNetRuntime.psm1'
$realLockPath = Join-Path $scriptRoot 'installer-dependencies\dependency-lock.json'
$probeProject = Join-Path $scriptRoot `
    'native\BlindSoldier.DotNetHostProbe\BlindSoldier.DotNetHostProbe.vcxproj'

function Get-PortableTestMsBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} `
        'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
        throw 'Visual Studio vswhere.exe is unavailable.'
    }
    $installation = (& $vswhere -latest -products '*' `
        -requires Microsoft.Component.MSBuild -property installationPath |
        Select-Object -First 1)
    $msbuild = Join-Path $installation 'MSBuild\Current\Bin\MSBuild.exe'
    if (-not (Test-Path -LiteralPath $msbuild -PathType Leaf)) {
        throw "MSBuild is unavailable: $msbuild"
    }
    return $msbuild
}

function Get-VerifiedNethostNativeRoot {
    param([ValidateSet('x86','x64')] [string] $Architecture)

    $records = @{
        x86 = @{
            Name = 'microsoft.netcore.app.host.win-x86.9.0.8.nupkg'
            Url = 'https://api.nuget.org/v3-flatcontainer/microsoft.netcore.app.host.win-x86/9.0.8/microsoft.netcore.app.host.win-x86.9.0.8.nupkg'
            Sha512 = '8802A15E656AC2D075A14D3D41A160635AC77A0B7E19CCE98E5A662A26D90868DEC142CC7AF56715A35350682734A7E7173884CBD3AEF64846C918EE9AB0DBB2'
        }
        x64 = @{
            Name = 'microsoft.netcore.app.host.win-x64.9.0.8.nupkg'
            Url = 'https://api.nuget.org/v3-flatcontainer/microsoft.netcore.app.host.win-x64/9.0.8/microsoft.netcore.app.host.win-x64.9.0.8.nupkg'
            Sha512 = 'B21209708B15466972BA204DBBF389D1C977612DA0F0B014C0DFABE362629FE1D9624644E44B1FDD997A28DAC11B825D8590F7DA3359F4913F9B4B81D0F6CB45'
        }
    }
    $record = $records[$Architecture]
    $cacheRoot = Join-Path $env:LOCALAPPDATA `
        'BlindSwordsman\BuildCache\nethost-9.0.8'
    New-Item -ItemType Directory -Path $cacheRoot -Force | Out-Null
    $packagePath = Join-Path $cacheRoot $record.Name
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        $download = Join-Path $cacheRoot `
            ('.download-' + [Guid]::NewGuid().ToString('N'))
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $record.Url -OutFile $download
            Move-Item -LiteralPath $download -Destination $packagePath
        }
        finally {
            if (Test-Path -LiteralPath $download -PathType Leaf) {
                Remove-Item -LiteralPath $download -Force
            }
        }
    }
    $digest = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA512).Hash
    if ($digest -cne $record.Sha512) {
        throw "Pinned nethost package failed SHA-512: $packagePath"
    }

    $nativeRoot = Join-Path $cacheRoot "native-$Architecture"
    $required = @('nethost.h','nethost.lib','nethost.dll')
    if (Test-Path -LiteralPath $nativeRoot -PathType Container) {
        foreach ($name in $required) {
            if (-not (Test-Path -LiteralPath (Join-Path $nativeRoot $name) `
                    -PathType Leaf)) {
                throw "Pinned nethost cache is incomplete: $nativeRoot"
            }
        }
        return $nativeRoot
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $staging = Join-Path $cacheRoot `
        ('.native-' + $Architecture + '-' + [Guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Path $staging | Out-Null
        $archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
        try {
            foreach ($name in $required) {
                $member = "runtimes/win-$Architecture/native/$name"
                $entry = @($archive.Entries | Where-Object FullName -CEQ $member)
                if ($entry.Count -ne 1) {
                    throw "Pinned nethost package omits $member."
                }
                $input = $entry[0].Open()
                try {
                    $target = Join-Path $staging $name
                    $output = [IO.File]::Open($target, [IO.FileMode]::CreateNew,
                        [IO.FileAccess]::Write, [IO.FileShare]::None)
                    try { $input.CopyTo($output) }
                    finally { $output.Dispose() }
                }
                finally { $input.Dispose() }
            }
        }
        finally { $archive.Dispose() }
        Move-Item -LiteralPath $staging -Destination $nativeRoot
    }
    finally {
        if (Test-Path -LiteralPath $staging) {
            Remove-Item -LiteralPath $staging -Recurse -Force
        }
    }
    return $nativeRoot
}

function New-RuntimeZip {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [string[]] $Entries,
        [string[]] $ExtraEntries = @(),
        [switch] $AddCaseDuplicate,
        [switch] $AddReparseEntry
    )
    Add-Type -AssemblyName System.IO.Compression
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Create,
        [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $archive = [IO.Compression.ZipArchive]::new(
            $stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($name in @($Entries) + @($ExtraEntries)) {
                $entry = $archive.CreateEntry($name)
                $target = $entry.Open()
                try {
                    $bytes = [Text.Encoding]::UTF8.GetBytes("fixture $name")
                    $target.Write($bytes, 0, $bytes.Length)
                }
                finally { $target.Dispose() }
            }
            if ($AddCaseDuplicate) {
                $entry = $archive.CreateEntry('DOTNET.EXE')
                $target = $entry.Open()
                try { $target.WriteByte(1) } finally { $target.Dispose() }
            }
            if ($AddReparseEntry) {
                $entry = $archive.CreateEntry('unsafe-link')
                $attributes = [uint32]::Parse('A1FF0000',
                    [Globalization.NumberStyles]::HexNumber)
                $entry.ExternalAttributes = [BitConverter]::ToInt32(
                    [BitConverter]::GetBytes($attributes), 0)
                $target = $entry.Open()
                try { $target.WriteByte(1) } finally { $target.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
}

function New-RuntimeFixture {
    param(
        [string[]] $ExtraEntries = @(),
        [switch] $OmitHostFxr,
        [switch] $AddCaseDuplicate,
        [switch] $AddReparseEntry
    )
    $root = Join-Path ([IO.Path]::GetTempPath()) `
        ('blind-soldier-dotnet-test-' + [Guid]::NewGuid().ToString('N'))
    $cache = Join-Path $root 'cache'
    New-Item -ItemType Directory -Path $cache -Force | Out-Null
    $coreName = 'dotnet-runtime-9.0.8-win-x64.zip'
    $desktopName = 'windowsdesktop-runtime-9.0.8-win-x64.zip'
    $coreArchive = Join-Path $cache $coreName
    $desktopArchive = Join-Path $cache $desktopName
    $coreEntries = @(
        'dotnet.exe',
        'host/fxr/9.0.8/hostfxr.dll',
        'shared/Microsoft.NETCore.App/9.0.8/coreclr.dll',
        'LICENSE.txt',
        'ThirdPartyNotices.txt'
    )
    if ($OmitHostFxr) {
        $coreEntries = @($coreEntries | Where-Object {
            $_ -cne 'host/fxr/9.0.8/hostfxr.dll'
        })
    }
    New-RuntimeZip -Path $coreArchive -Entries $coreEntries `
        -ExtraEntries $ExtraEntries -AddCaseDuplicate:$AddCaseDuplicate `
        -AddReparseEntry:$AddReparseEntry
    New-RuntimeZip -Path $desktopArchive -Entries @(
        'shared/Microsoft.WindowsDesktop.App/9.0.8/PresentationFramework.dll'
    )
    $coreDigest = (Get-FileHash -LiteralPath $coreArchive -Algorithm SHA512).Hash
    $desktopDigest = (Get-FileHash -LiteralPath $desktopArchive -Algorithm SHA512).Hash
    $lock = [ordered]@{
        schemaVersion = 1
        dotnetDesktopRuntime = [ordered]@{
            version = '9.0.8'
            portableArchives = @(
                [ordered]@{
                    architecture = 'x86'
                    component = 'core'
                    name = 'dotnet-runtime-9.0.8-win-x86.zip'
                    url = 'https://fixture.invalid/x86-core.zip'
                    sha512 = 'A' * 128
                },
                [ordered]@{
                    architecture = 'x86'
                    component = 'windowsDesktop'
                    name = 'windowsdesktop-runtime-9.0.8-win-x86.zip'
                    url = 'https://fixture.invalid/x86-desktop.zip'
                    sha512 = 'B' * 128
                },
                [ordered]@{
                    architecture = 'x64'
                    component = 'core'
                    name = $coreName
                    url = 'https://fixture.invalid/x64-core.zip'
                    sha512 = $coreDigest
                },
                [ordered]@{
                    architecture = 'x64'
                    component = 'windowsDesktop'
                    name = $desktopName
                    url = 'https://fixture.invalid/x64-desktop.zip'
                    sha512 = $desktopDigest
                }
            )
        }
    }
    $lockPath = Join-Path $root 'lock.json'
    [IO.File]::WriteAllText($lockPath,
        ($lock | ConvertTo-Json -Depth 6),
        [Text.UTF8Encoding]::new($false))
    [pscustomobject]@{
        Root = $root
        Cache = $cache
        Archive = $coreArchive
        LockPath = $lockPath
        Destination = Join-Path $root 'runtime'
    }
}

Describe 'Blind Soldier portable .NET runtime closure' {
    It 'pins the official x86 and x64 9.0.8 portable archives' {
        $lock = [IO.File]::ReadAllText($realLockPath) | ConvertFrom-Json
        $archives = @($lock.dotnetDesktopRuntime.portableArchives)
        $archives.Count | Should Be 4
        $x86Core = $archives | Where-Object {
            $_.architecture -ceq 'x86' -and $_.component -ceq 'core'
        }
        $x86Desktop = $archives | Where-Object {
            $_.architecture -ceq 'x86' -and $_.component -ceq 'windowsDesktop'
        }
        $x64Core = $archives | Where-Object {
            $_.architecture -ceq 'x64' -and $_.component -ceq 'core'
        }
        $x64Desktop = $archives | Where-Object {
            $_.architecture -ceq 'x64' -and $_.component -ceq 'windowsDesktop'
        }
        $x86Core.name | Should Be 'dotnet-runtime-9.0.8-win-x86.zip'
        $x86Core.url | Should Be 'https://builds.dotnet.microsoft.com/dotnet/Runtime/9.0.8/dotnet-runtime-9.0.8-win-x86.zip'
        $x86Core.sha512 | Should Be 'B198317B9B9ACB1B92052881C4CE22E9A0CDC3D6659218CD98EB2AF3FF8F18D09CCEC1931AE9F055AC58A405E4C88EAC06B88DC0407E9E25F91BD54D9309460B'
        $x86Desktop.sha512 | Should Be '09A6D9A8AA4BA944C59D8A57703CF1C42CCC86263B7FB07D1D21848E67254623A079CC5599EB5C7E03BA04FACCC3A0E9452706151AF6B7C0A2E75F725BEFA2DC'
        $x64Core.name | Should Be 'dotnet-runtime-9.0.8-win-x64.zip'
        $x64Core.url | Should Be 'https://builds.dotnet.microsoft.com/dotnet/Runtime/9.0.8/dotnet-runtime-9.0.8-win-x64.zip'
        $x64Core.sha512 | Should Be '664509EF8DA97D5965278A5AA18002C31A0BDF0CBA1315913BB5FD61870E4052AD5F8180F361D1AA7BB3BDA92DFC30222B4A5751E17788E950303E265D11BA8C'
        $x64Desktop.sha512 | Should Be 'FFE3055F50F5E57ABA41AD7790044E32D9D73F526A0A0310664E8D936BBBB60CB84C90E4FF0EC12CB726BFC157DF105769A768306B4191FC1D6CC22173F20771'
    }

    It 'extracts a verified closure atomically from the caller cache' {
        Test-Path -LiteralPath $modulePath -PathType Leaf | Should Be $true
        Import-Module $modulePath -Force
        $fixture = New-RuntimeFixture
        try {
            Expand-VerifiedPortableDotNetRuntime -Architecture x64 `
                -Destination $fixture.Destination -CachePath $fixture.Cache `
                -LockPath $fixture.LockPath
            foreach ($relative in @(
                'dotnet.exe', 'host\fxr\9.0.8\hostfxr.dll',
                'shared\Microsoft.NETCore.App\9.0.8\coreclr.dll',
                'shared\Microsoft.WindowsDesktop.App\9.0.8\PresentationFramework.dll',
                'LICENSE.txt', 'ThirdPartyNotices.txt'
            )) {
                Test-Path -LiteralPath (Join-Path $fixture.Destination $relative) `
                    -PathType Leaf | Should Be $true
            }
        }
        finally { Remove-Item -LiteralPath $fixture.Root -Recurse -Force }
    }

    It 'rejects a digest mismatch before opening the archive' {
        Import-Module $modulePath -Force
        $fixture = New-RuntimeFixture
        try {
            $lock = [IO.File]::ReadAllText($fixture.LockPath) | ConvertFrom-Json
            ($lock.dotnetDesktopRuntime.portableArchives |
                Where-Object {
                    $_.architecture -ceq 'x64' -and $_.component -ceq 'core'
                }).sha512 = 'C' * 128
            [IO.File]::WriteAllText($fixture.LockPath,
                ($lock | ConvertTo-Json -Depth 6))
            { Expand-VerifiedPortableDotNetRuntime -Architecture x64 `
                -Destination $fixture.Destination -CachePath $fixture.Cache `
                -LockPath $fixture.LockPath } | Should Throw
            Test-Path -LiteralPath $fixture.Destination | Should Be $false
        }
        finally { Remove-Item -LiteralPath $fixture.Root -Recurse -Force }
    }

    It 'rejects rooted parent-traversal duplicate and reparse entries' {
        Import-Module $modulePath -Force
        $cases = @(
            @{ ExtraEntries=@('../escape.txt') },
            @{ ExtraEntries=@('C:/escape.txt') },
            @{ AddCaseDuplicate=$true },
            @{ AddReparseEntry=$true }
        )
        foreach ($case in $cases) {
            $fixture = New-RuntimeFixture @case
            try {
                { Expand-VerifiedPortableDotNetRuntime -Architecture x64 `
                    -Destination $fixture.Destination -CachePath $fixture.Cache `
                    -LockPath $fixture.LockPath } | Should Throw
                Test-Path -LiteralPath $fixture.Destination | Should Be $false
            }
            finally { Remove-Item -LiteralPath $fixture.Root -Recurse -Force }
        }
    }

    It 'rejects an incomplete runtime without publishing it' {
        Import-Module $modulePath -Force
        $fixture = New-RuntimeFixture -OmitHostFxr
        try {
            { Expand-VerifiedPortableDotNetRuntime -Architecture x64 `
                -Destination $fixture.Destination -CachePath $fixture.Cache `
                -LockPath $fixture.LockPath } | Should Throw
            Test-Path -LiteralPath $fixture.Destination | Should Be $false
        }
        finally { Remove-Item -LiteralPath $fixture.Root -Recurse -Force }
    }

    It 'proves pinned nethost discovers only the private architecture runtime' {
        Test-Path -LiteralPath $probeProject -PathType Leaf | Should Be $true
        Import-Module $modulePath -Force
        $msbuild = Get-PortableTestMsBuild
        $cache = Join-Path $env:LOCALAPPDATA `
            'BlindSwordsman\BuildCache\portable-dotnet-9.0.8'
        $testRoot = Join-Path ([IO.Path]::GetTempPath()) `
            ('blind-soldier-nethost-proof-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $testRoot | Out-Null
        try {
            foreach ($architecture in @('x86','x64')) {
                $runtime = Join-Path $testRoot "runtime-$architecture"
                Expand-VerifiedPortableDotNetRuntime `
                    -Architecture $architecture -Destination $runtime `
                    -CachePath $cache -LockPath $realLockPath | Out-Null
                $nativeRoot = Get-VerifiedNethostNativeRoot `
                    -Architecture $architecture
                $platform = if ($architecture -ceq 'x86') { 'Win32' } else { 'x64' }
                & $msbuild $probeProject /nologo /m /t:Rebuild `
                    /p:Configuration=Release /p:Platform=$platform `
                    "/p:NethostNativeRoot=$nativeRoot" /v:minimal
                $LASTEXITCODE | Should Be 0
                $probe = Join-Path (Split-Path -Parent $probeProject) `
                    "bin\Release\$platform\BlindSoldier.DotNetHostProbe.exe"
                $expected = Join-Path $runtime 'host\fxr\9.0.8\hostfxr.dll'
                & $probe $runtime $expected
                $LASTEXITCODE | Should Be 0
            }
        }
        finally {
            $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
            $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
            if ($resolvedTestRoot.StartsWith($tempPrefix,
                    [StringComparison]::OrdinalIgnoreCase) -and
                (Split-Path -Leaf $resolvedTestRoot).StartsWith(
                    'blind-soldier-nethost-proof-',
                    [StringComparison]::Ordinal)) {
                Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
            }
        }
    }
}
