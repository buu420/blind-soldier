$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$modulePath = Join-Path $scriptRoot 'FF7SteamInstall.psm1'

Describe 'FFVII Steam installation support module' {
    It 'exists beside the installer' {
        (Test-Path -LiteralPath $modulePath -PathType Leaf) | Should Be $true
    }
}

Remove-Module FF7SteamInstall -Force -ErrorAction SilentlyContinue
Import-Module $modulePath -Force

function New-TestDirectory {
    $path = Join-Path ([IO.Path]::GetTempPath()) ('ff7-accessibility-test-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    return $path
}

function Write-TestTextFile {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [string] $Content
    )

    $parent = Split-Path -Parent $Path
    if ($parent) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    [IO.File]::WriteAllText($Path, $Content, [Text.Encoding]::UTF8)
}

function Import-TestFunctionFromScript {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [string] $Name
    )

    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile(
        $Path,
        [ref]$tokens,
        [ref]$errors)
    if ($errors.Count -gt 0) {
        throw "Cannot import test helper from a script with parse errors: $Path"
    }
    $functionAst = $ast.Find({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -ceq $Name
    }, $true)
    if ($null -eq $functionAst) {
        throw "Function $Name was not found in $Path"
    }
    $bodyText = $functionAst.Body.Extent.Text
    $bodyText = $bodyText.Substring(1, $bodyText.Length - 2)
    Set-Item -Path ("Function:global:{0}" -f $Name) `
        -Value ([scriptblock]::Create($bodyText))
}

function Get-TestPeMachine {
    param([Parameter(Mandatory=$true)] [string] $Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "Not a PE file: $Path"
    }

    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($peOffset -lt 0 -or $peOffset + 6 -gt $bytes.Length) {
        throw "Invalid PE header offset: $Path"
    }

    return [BitConverter]::ToUInt16($bytes, $peOffset + 4)
}

function Get-TestPeManagedNativeHeaderDirectory {
    param([Parameter(Mandatory=$true)] [string] $Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "Not a PE file: $Path"
    }

    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($peOffset -lt 0 -or $peOffset + 24 -gt $bytes.Length) {
        throw "Invalid PE header offset: $Path"
    }

    $numberOfSections = [BitConverter]::ToUInt16($bytes, $peOffset + 6)
    $optionalHeaderSize = [BitConverter]::ToUInt16($bytes, $peOffset + 20)
    $optionalHeaderOffset = $peOffset + 24
    $optionalMagic = [BitConverter]::ToUInt16($bytes, $optionalHeaderOffset)
    if ($optionalMagic -eq 0x010B) {
        $numberOfDirectoriesOffset = $optionalHeaderOffset + 92
        $directoriesOffset = $optionalHeaderOffset + 96
    }
    elseif ($optionalMagic -eq 0x020B) {
        $numberOfDirectoriesOffset = $optionalHeaderOffset + 108
        $directoriesOffset = $optionalHeaderOffset + 112
    }
    else {
        throw "Unsupported PE optional header: $Path"
    }

    if ($numberOfDirectoriesOffset + 4 -gt $bytes.Length -or
        [BitConverter]::ToUInt32($bytes, $numberOfDirectoriesOffset) -le 14) {
        throw "PE file has no CLR data directory: $Path"
    }

    $clrDirectoryOffset = $directoriesOffset + (14 * 8)
    if ($clrDirectoryOffset + 8 -gt $optionalHeaderOffset + $optionalHeaderSize) {
        throw "PE CLR data directory is outside the optional header: $Path"
    }

    $clrRva = [BitConverter]::ToUInt32($bytes, $clrDirectoryOffset)
    $clrSize = [BitConverter]::ToUInt32($bytes, $clrDirectoryOffset + 4)
    if ($clrRva -eq 0 -or $clrSize -lt 72) {
        throw "PE file has no complete CLR header: $Path"
    }

    $sectionOffset = $optionalHeaderOffset + $optionalHeaderSize
    $clrFileOffset = $null
    for ($index = 0; $index -lt $numberOfSections; $index++) {
        $currentSectionOffset = $sectionOffset + ($index * 40)
        if ($currentSectionOffset + 40 -gt $bytes.Length) {
            throw "PE section table is truncated: $Path"
        }

        $virtualSize = [BitConverter]::ToUInt32($bytes, $currentSectionOffset + 8)
        $virtualAddress = [BitConverter]::ToUInt32($bytes, $currentSectionOffset + 12)
        $rawSize = [BitConverter]::ToUInt32($bytes, $currentSectionOffset + 16)
        $rawOffset = [BitConverter]::ToUInt32($bytes, $currentSectionOffset + 20)
        $mappedSize = [Math]::Max([uint64]$virtualSize, [uint64]$rawSize)
        if ([uint64]$clrRva -ge [uint64]$virtualAddress -and
            [uint64]$clrRva -lt ([uint64]$virtualAddress + $mappedSize)) {
            $clrFileOffset = [uint64]$rawOffset + ([uint64]$clrRva - [uint64]$virtualAddress)
            break
        }
    }

    if ($null -eq $clrFileOffset -or $clrFileOffset + 72 -gt $bytes.Length) {
        throw "PE CLR header RVA cannot be mapped to file data: $Path"
    }

    [pscustomobject]@{
        VirtualAddress = [BitConverter]::ToUInt32($bytes, [int]$clrFileOffset + 64)
        Size = [BitConverter]::ToUInt32($bytes, [int]$clrFileOffset + 68)
    }
}

function Set-TestPeManagedNativeHeaderDirectory {
    param(
        [Parameter(Mandatory=$true)] [string] $Path,
        [Parameter(Mandatory=$true)] [uint32] $VirtualAddress,
        [Parameter(Mandatory=$true)] [uint32] $Size
    )

    $bytes = [IO.File]::ReadAllBytes($Path)
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($peOffset -lt 0 -or $peOffset + 24 -gt $bytes.Length -or
        [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x00004550) {
        throw "Invalid PE file: $Path"
    }
    $numberOfSections = [BitConverter]::ToUInt16($bytes, $peOffset + 6)
    $optionalHeaderSize = [BitConverter]::ToUInt16($bytes, $peOffset + 20)
    $optionalHeaderOffset = $peOffset + 24
    $optionalMagic = [BitConverter]::ToUInt16($bytes, $optionalHeaderOffset)
    $directoriesOffset = if ($optionalMagic -eq 0x010B) {
        $optionalHeaderOffset + 96
    }
    elseif ($optionalMagic -eq 0x020B) {
        $optionalHeaderOffset + 112
    }
    else {
        throw "Unsupported PE optional header: $Path"
    }
    $clrDirectoryOffset = $directoriesOffset + (14 * 8)
    $clrRva = [BitConverter]::ToUInt32($bytes, $clrDirectoryOffset)
    $sectionOffset = $optionalHeaderOffset + $optionalHeaderSize
    $clrFileOffset = $null
    for ($index = 0; $index -lt $numberOfSections; $index++) {
        $currentSectionOffset = $sectionOffset + ($index * 40)
        $sectionVirtualAddress = [BitConverter]::ToUInt32($bytes, $currentSectionOffset + 12)
        $rawSize = [BitConverter]::ToUInt32($bytes, $currentSectionOffset + 16)
        $rawOffset = [BitConverter]::ToUInt32($bytes, $currentSectionOffset + 20)
        if ([uint64]$clrRva -ge [uint64]$sectionVirtualAddress -and
            [uint64]$clrRva -lt ([uint64]$sectionVirtualAddress + [uint64]$rawSize)) {
            $clrFileOffset = [uint64]$rawOffset + ([uint64]$clrRva - [uint64]$sectionVirtualAddress)
            break
        }
    }
    if ($null -eq $clrFileOffset -or $clrFileOffset + 72 -gt $bytes.Length) {
        throw "CLR header is not backed by file data: $Path"
    }
    [BitConverter]::GetBytes($VirtualAddress).CopyTo($bytes, [int]$clrFileOffset + 64)
    [BitConverter]::GetBytes($Size).CopyTo($bytes, [int]$clrFileOffset + 68)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function Assert-TestReadyToRun {
    param([Parameter(Mandatory=$true)] [string] $Path)

    $managedNativeHeader = Get-TestPeManagedNativeHeaderDirectory -Path $Path
    if ($managedNativeHeader.VirtualAddress -eq 0 -or $managedNativeHeader.Size -eq 0) {
        throw "ManagedNativeHeaderDirectory is empty: $Path"
    }
}

function Get-TestDirectoryFingerprint {
    param([Parameter(Mandatory=$true)] [string] $Root)

    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $entries = foreach ($file in Get-ChildItem -LiteralPath $rootPath -File -Recurse | Sort-Object FullName) {
        $relativePath = $file.FullName.Substring($rootPath.Length).TrimStart('\')
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        '{0}|{1}|{2}' -f $relativePath, $file.Length, $hash
    }

    $manifestBytes = [Text.Encoding]::UTF8.GetBytes(($entries -join "`n"))
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha256.ComputeHash($manifestBytes))).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
    }
}

function New-SteamManifestFixture {
    param(
        [Parameter(Mandatory=$true)] [string] $LibraryRoot,
        [Parameter(Mandatory=$true)] [string] $AppId,
        [Parameter(Mandatory=$true)] [string] $InstallDir
    )

    $manifest = Join-Path $LibraryRoot ("steamapps\appmanifest_{0}.acf" -f $AppId)
    Write-TestTextFile -Path $manifest -Content @"
"AppState"
{
    "appid" "$AppId"
    "installdir" "$InstallDir"
}
"@
    return Join-Path $LibraryRoot ("steamapps\common\{0}" -f $InstallDir)
}

function New-Steam2026Fixture {
    param([Parameter(Mandatory=$true)] [string] $GameRoot)

    Write-TestTextFile -Path (Join-Path $GameRoot 'FFVII.exe') -Content 'native-x64'
    Write-TestTextFile -Path (Join-Path $GameRoot 'FFVII_LAUNCHER.exe') -Content 'launcher'
    Write-TestTextFile -Path (Join-Path $GameRoot 'steam_api64.dll') -Content 'steam64'
    Write-TestTextFile -Path (Join-Path $GameRoot 'ff7\resources\ff7_1.02\ff7_en') -Content 'legacy-x86'
    New-Item -ItemType Directory -Path (Join-Path $GameRoot 'ff7\workingdir\data') -Force | Out-Null
}

function New-Steam2013Fixture {
    param([Parameter(Mandatory=$true)] [string] $GameRoot)

    Write-TestTextFile -Path (Join-Path $GameRoot 'ff7_en.exe') -Content 'steam-2013'
    Write-TestTextFile -Path (Join-Path $GameRoot 'FF7_Launcher.exe') -Content 'launcher'
    New-Item -ItemType Directory -Path (Join-Path $GameRoot 'data') -Force | Out-Null
}

Describe 'Get-Ff7SteamLibraryPaths' {
    It 'returns the Steam root and escaped library paths in stable order' {
        $fixture = New-TestDirectory
        try {
            $steamRoot = Join-Path $fixture 'Steam'
            $external = Join-Path $fixture 'Network Library'
            New-Item -ItemType Directory -Path $external -Force | Out-Null
            $escapedExternal = $external.Replace('\', '\\')
            Write-TestTextFile -Path (Join-Path $steamRoot 'steamapps\libraryfolders.vdf') -Content @"
"libraryfolders"
{
    "0" { "path" "$($steamRoot.Replace('\', '\\'))" }
    "1"
    {
        "path" "$escapedExternal"
    }
}
"@

            $paths = @(Get-Ff7SteamLibraryPaths -SteamRoot $steamRoot)

            $paths.Count | Should Be 2
            $paths[0] | Should Be ([IO.Path]::GetFullPath($steamRoot))
            $paths[1] | Should Be ([IO.Path]::GetFullPath($external))
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }
}

Describe 'Resolve-Ff7Installation' {
    It 'resolves Steam app 3837340 to its bundled x86 working directory' {
        $fixture = New-TestDirectory
        try {
            $steamRoot = Join-Path $fixture 'Steam'
            $library = Join-Path $fixture 'Mapped Library'
            $escapedLibrary = $library.Replace('\', '\\')
            Write-TestTextFile -Path (Join-Path $steamRoot 'steamapps\libraryfolders.vdf') -Content @"
"libraryfolders"
{
    "0" { "path" "$escapedLibrary" }
}
"@
            $gameRoot = New-SteamManifestFixture -LibraryRoot $library -AppId '3837340' -InstallDir 'FINAL FANTASY VII Steam Edition'
            New-Steam2026Fixture -GameRoot $gameRoot

            $install = Resolve-Ff7Installation -SteamRoot $steamRoot

            $install.Version | Should Be 'Steam2026'
            $install.SteamAppId | Should Be '3837340'
            $install.GameRoot | Should Be ([IO.Path]::GetFullPath($gameRoot))
            $install.RuntimeRoot | Should Be (Join-Path ([IO.Path]::GetFullPath($gameRoot)) 'ff7\workingdir')
            $install.GameExe | Should Be (Join-Path ([IO.Path]::GetFullPath($gameRoot)) 'ff7\workingdir\ff7_en.exe')
            $install.SourceExe | Should Be (Join-Path ([IO.Path]::GetFullPath($gameRoot)) 'ff7\resources\ff7_1.02\ff7_en')
            $install.LegacyRuntime.Architecture | Should Be 'x86'
            $install.LegacyRuntime.GameExe | Should Be $install.GameExe
            $install.LegacyRuntime.RuntimeRoot | Should Be $install.RuntimeRoot
            $install.NativeRuntime.Architecture | Should Be 'x64'
            $install.NativeRuntime.GameExe | Should Be (Join-Path ([IO.Path]::GetFullPath($gameRoot)) 'FFVII.exe')
            $install.NativeRuntime.RuntimeRoot | Should Be ([IO.Path]::GetFullPath($gameRoot))
            [object]::ReferenceEquals($install.LegacyRuntime, $install.NativeRuntime) | Should Be $false
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'resolves an explicitly selected Steam 2013 root' {
        $fixture = New-TestDirectory
        try {
            $gameRoot = Join-Path $fixture 'FINAL FANTASY VII'
            New-Steam2013Fixture -GameRoot $gameRoot

            $install = Resolve-Ff7Installation -GameRoot $gameRoot

            $install.Version | Should Be 'Steam2013'
            $install.SteamAppId | Should Be '39140'
            $install.RuntimeRoot | Should Be ([IO.Path]::GetFullPath($gameRoot))
            $install.GameExe | Should Be (Join-Path ([IO.Path]::GetFullPath($gameRoot)) 'ff7_en.exe')
            $install.SourceExe | Should BeNullOrEmpty
            $install.LegacyRuntime.Architecture | Should Be 'x86'
            $install.LegacyRuntime.GameExe | Should Be $install.GameExe
            $install.NativeRuntime | Should BeNullOrEmpty
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'rejects a directory whose name matches but required 2026 markers do not' {
        $fixture = New-TestDirectory
        try {
            $gameRoot = Join-Path $fixture 'FINAL FANTASY VII Steam Edition'
            New-Item -ItemType Directory -Path $gameRoot -Force | Out-Null
            Write-TestTextFile -Path (Join-Path $gameRoot 'FFVII.exe') -Content 'native-x64'

            { Resolve-Ff7Installation -GameRoot $gameRoot } | Should Throw 'not a supported FFVII installation'
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'rejects automatic discovery when more than one supported install exists' {
        $fixture = New-TestDirectory
        try {
            $steamRoot = Join-Path $fixture 'Steam'
            $library = Join-Path $fixture 'Library'
            Write-TestTextFile -Path (Join-Path $steamRoot 'steamapps\libraryfolders.vdf') -Content @"
"libraryfolders"
{
    "0" { "path" "$($library.Replace('\', '\\'))" }
}
"@
            $oldRoot = New-SteamManifestFixture -LibraryRoot $library -AppId '39140' -InstallDir 'FINAL FANTASY VII'
            $newRoot = New-SteamManifestFixture -LibraryRoot $library -AppId '3837340' -InstallDir 'FINAL FANTASY VII Steam Edition'
            New-Steam2013Fixture -GameRoot $oldRoot
            New-Steam2026Fixture -GameRoot $newRoot

            { Resolve-Ff7Installation -SteamRoot $steamRoot } | Should Throw 'Multiple supported FFVII installations'
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }
}

$knownSteam2026Native = 'X:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\FFVII.exe'
$knownSteam2026NativeSha256 = '57A23D166D69E46B9E3339F779D4A3C4FEB402A989FA7291D0D9B4A1953ABB4B'
$knownSteam2026LegacySource = 'X:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\ff7\resources\ff7_1.02\ff7_en'

Describe 'Assert-Ff7NativeRuntimeIdentity' {
    It 'accepts only the exact supported native x64 executable identity' {
        if (-not (Test-Path -LiteralPath $knownSteam2026Native -PathType Leaf)) {
            throw "Required native Steam 2026 fixture is unavailable: $knownSteam2026Native"
        }

        $fixture = New-TestDirectory
        try {
            $supported = Assert-Ff7NativeRuntimeIdentity -Path $knownSteam2026Native
            $supported.Architecture | Should Be 'x64'
            $supported.Machine | Should Be 0x8664
            $supported.Sha256 | Should Be $knownSteam2026NativeSha256

            $altered = Join-Path $fixture 'FFVII-altered.exe'
            Copy-Item -LiteralPath $knownSteam2026Native -Destination $altered
            $stream = [IO.File]::Open($altered, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            try {
                $stream.Position = $stream.Length - 1
                $value = $stream.ReadByte()
                $stream.Position = $stream.Length - 1
                $stream.WriteByte([byte](($value + 1) % 256))
            }
            finally {
                $stream.Dispose()
            }

            { Assert-Ff7NativeRuntimeIdentity -Path $altered } |
                Should Throw 'does not match the supported Steam 2026 native SHA-256'
            { Assert-Ff7NativeRuntimeIdentity -Path $knownSteam2026LegacySource } |
                Should Throw 'PE machine 0x014C; expected native x64 machine 0x8664'
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }
}

$knownSteam2026Source = 'X:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\ff7\resources\ff7_1.02\ff7_en'
$knownSteam2026SourceSha1 = 'AC306AE92615AF75FF36BBA6347C67CA1284151D'
$knownSteam2026LargeAddressSha1 = 'D270E690A0EA2C9D57AF506D102CF1A794E2ADCD'

function New-ValidSteam2026RuntimeFixture {
    param([Parameter(Mandatory=$true)] [string] $FixtureRoot)

    if (-not (Test-Path -LiteralPath $knownSteam2026Source -PathType Leaf)) {
        throw "Required real Steam 2026 source fixture is unavailable: $knownSteam2026Source"
    }

    $gameRoot = Join-Path $FixtureRoot 'FINAL FANTASY VII Steam Edition'
    New-Steam2026Fixture -GameRoot $gameRoot
    Copy-Item -LiteralPath $knownSteam2026Source -Destination (Join-Path $gameRoot 'ff7\resources\ff7_1.02\ff7_en') -Force
    Write-TestTextFile -Path (Join-Path $gameRoot 'ff7\workingdir\data\lang-ja\kernel\window.bin') -Content 'window-data'
    return Resolve-Ff7Installation -GameRoot $gameRoot
}

Describe 'Initialize-Ff7CompatibilityRuntime' {
    It 'prepares the Steam 2026 runtime without modifying the bundled source' {
        $fixture = New-TestDirectory
        try {
            $install = New-ValidSteam2026RuntimeFixture -FixtureRoot $fixture
            $sourceHashBefore = (Get-FileHash -LiteralPath $install.SourceExe -Algorithm SHA1).Hash

            $result = Initialize-Ff7CompatibilityRuntime -Installation $install

            $sourceHashBefore | Should Be $knownSteam2026SourceSha1
            (Get-FileHash -LiteralPath $install.SourceExe -Algorithm SHA1).Hash | Should Be $knownSteam2026SourceSha1
            (Get-FileHash -LiteralPath $result.GameExe -Algorithm SHA1).Hash | Should Be $knownSteam2026LargeAddressSha1
            [IO.File]::ReadAllText((Join-Path $result.RuntimeRoot 'steam_appid.txt')).Trim() | Should Be '3837340'
            [IO.File]::ReadAllText((Join-Path $result.RuntimeRoot 'data\kernel\window.bin')) | Should Be 'window-data'
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'is idempotent when the compatibility runtime is already prepared' {
        $fixture = New-TestDirectory
        try {
            $install = New-ValidSteam2026RuntimeFixture -FixtureRoot $fixture
            Initialize-Ff7CompatibilityRuntime -Installation $install | Out-Null
            $exeHash = (Get-FileHash -LiteralPath $install.GameExe -Algorithm SHA256).Hash
            $windowHash = (Get-FileHash -LiteralPath (Join-Path $install.RuntimeRoot 'data\kernel\window.bin') -Algorithm SHA256).Hash

            Initialize-Ff7CompatibilityRuntime -Installation $install | Out-Null

            (Get-FileHash -LiteralPath $install.GameExe -Algorithm SHA256).Hash | Should Be $exeHash
            (Get-FileHash -LiteralPath (Join-Path $install.RuntimeRoot 'data\kernel\window.bin') -Algorithm SHA256).Hash | Should Be $windowHash
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'rejects an unknown bundled source hash' {
        $fixture = New-TestDirectory
        try {
            $gameRoot = Join-Path $fixture 'FINAL FANTASY VII Steam Edition'
            New-Steam2026Fixture -GameRoot $gameRoot
            Write-TestTextFile -Path (Join-Path $gameRoot 'ff7\workingdir\data\lang-ja\kernel\window.bin') -Content 'window-data'
            $install = Resolve-Ff7Installation -GameRoot $gameRoot

            { Initialize-Ff7CompatibilityRuntime -Installation $install } | Should Throw 'Unsupported Steam 2026 source executable SHA-1'
            (Test-Path -LiteralPath $install.GameExe) | Should Be $false
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'rejects an unknown existing runtime executable instead of overwriting it' {
        $fixture = New-TestDirectory
        try {
            $install = New-ValidSteam2026RuntimeFixture -FixtureRoot $fixture
            Write-TestTextFile -Path $install.GameExe -Content 'unknown-runtime'

            { Initialize-Ff7CompatibilityRuntime -Installation $install } | Should Throw 'Unsupported existing compatibility executable SHA-1'
            [IO.File]::ReadAllText($install.GameExe) | Should Be 'unknown-runtime'
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'preflights every compatibility input before creating or patching the runtime executable' {
        $fixture = New-TestDirectory
        try {
            $install = New-ValidSteam2026RuntimeFixture -FixtureRoot $fixture
            $windowSource = Join-Path $install.RuntimeRoot 'data\lang-ja\kernel\window.bin'
            Remove-Item -LiteralPath $windowSource -Force
            $sourceHash = (Get-FileHash -LiteralPath $install.SourceExe -Algorithm SHA256).Hash

            { Initialize-Ff7CompatibilityRuntime -Installation $install } |
                Should Throw 'window.bin source is missing'
            Test-Path -LiteralPath $install.GameExe | Should Be $false
            Test-Path -LiteralPath (Join-Path $install.RuntimeRoot 'data\kernel\window.bin') | Should Be $false
            Test-Path -LiteralPath (Join-Path $install.RuntimeRoot 'steam_appid.txt') | Should Be $false
            (Get-FileHash -LiteralPath $install.SourceExe -Algorithm SHA256).Hash | Should Be $sourceHash
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'rolls back compatibility files when a later write fails' {
        $fixture = New-TestDirectory
        try {
            $install = New-ValidSteam2026RuntimeFixture -FixtureRoot $fixture
            $steamAppIdPath = Join-Path $install.RuntimeRoot 'steam_appid.txt'
            Write-TestTextFile -Path $steamAppIdPath -Content 'prior-app-id'
            (Get-Item -LiteralPath $steamAppIdPath).Attributes = [IO.FileAttributes]::ReadOnly
            $priorAppId = [IO.File]::ReadAllBytes($steamAppIdPath)

            { Initialize-Ff7CompatibilityRuntime -Installation $install } | Should Throw
            Test-Path -LiteralPath $install.GameExe | Should Be $false
            Test-Path -LiteralPath (Join-Path $install.RuntimeRoot 'data\kernel\window.bin') | Should Be $false
            [Convert]::ToBase64String([IO.File]::ReadAllBytes($steamAppIdPath)) |
                Should Be ([Convert]::ToBase64String($priorAppId))
        }
        finally {
            $steamAppIdPath = Join-Path $fixture 'FINAL FANTASY VII Steam Edition\ff7\workingdir\steam_appid.txt'
            if (Test-Path -LiteralPath $steamAppIdPath -PathType Leaf) {
                (Get-Item -LiteralPath $steamAppIdPath).Attributes = [IO.FileAttributes]::Normal
            }
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'leaves a Steam 2013 installation unchanged' {
        $fixture = New-TestDirectory
        try {
            $gameRoot = Join-Path $fixture 'FINAL FANTASY VII'
            New-Steam2013Fixture -GameRoot $gameRoot
            $install = Resolve-Ff7Installation -GameRoot $gameRoot
            $hashBefore = (Get-FileHash -LiteralPath $install.GameExe -Algorithm SHA256).Hash

            $result = Initialize-Ff7CompatibilityRuntime -Installation $install

            $result.GameExe | Should Be $install.GameExe
            (Get-FileHash -LiteralPath $install.GameExe -Algorithm SHA256).Hash | Should Be $hashBefore
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }
}

function New-FfnxReleaseFixture {
    param(
        [Parameter(Mandatory=$true)]
        [AllowNull()]
        [AllowEmptyString()]
        [string] $Digest,
        [string] $Name = 'FFNx-Steam-v9.9.9.0.zip'
    )

    return [pscustomobject]@{
        tag_name = '9.9.9'
        assets = @(
            [pscustomobject]@{
                name = 'FFNx-FF7_1998-v9.9.9.0.zip'
                browser_download_url = 'https://example.invalid/FFNx-FF7_1998.zip'
                digest = 'sha256:' + ('0' * 64)
                size = 1
            },
            [pscustomobject]@{
                name = $Name
                browser_download_url = 'https://example.invalid/' + $Name
                digest = $Digest
                size = 1
            }
        )
    }
}

function New-FfnxArchiveFixture {
    param(
        [Parameter(Mandatory=$true)] [string] $FixtureRoot,
        [switch] $WithoutDriver,
        [string] $AdditionalFileRelativePath
    )

    $contents = Join-Path $FixtureRoot 'ffnx-archive-contents'
    New-Item -ItemType Directory -Path $contents -Force | Out-Null
    if (-not $WithoutDriver) {
        Write-TestTextFile -Path (Join-Path $contents 'AF3DN.P') -Content 'ffnx-driver'
    }
    Write-TestTextFile -Path (Join-Path $contents 'AF4DN.P') -Content 'ffnx-proxy'
    Write-TestTextFile -Path (Join-Path $contents 'FFNx.toml') -Content 'default-config'
    Write-TestTextFile -Path (Join-Path $contents 'sfx\config.toml') -Content 'default-sfx'
    Write-TestTextFile -Path (Join-Path $contents 'shaders\test.shader') -Content 'shader-data'
    if (-not [string]::IsNullOrWhiteSpace($AdditionalFileRelativePath)) {
        Write-TestTextFile -Path (Join-Path $contents $AdditionalFileRelativePath) -Content 'additional-data'
    }

    $archive = Join-Path $FixtureRoot 'FFNx-Steam-v9.9.9.0.zip'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($contents, $archive)
    return $archive
}

Describe 'Select-FfnxSteamAsset' {
    It 'selects the single Steam archive with a SHA-256 digest' {
        $release = New-FfnxReleaseFixture -Digest ('sha256:' + ('a' * 64))

        $asset = Select-FfnxSteamAsset -Release $release

        $asset.name | Should Be 'FFNx-Steam-v9.9.9.0.zip'
    }

    It 'rejects a Steam archive without a release digest' {
        $release = New-FfnxReleaseFixture -Digest $null

        { Select-FfnxSteamAsset -Release $release } | Should Throw 'does not provide a valid SHA-256 digest'
    }

    It 'rejects releases containing more than one Steam archive' {
        $release = New-FfnxReleaseFixture -Digest ('sha256:' + ('a' * 64))
        $release.assets += [pscustomobject]@{
            name = 'FFNx-Steam-v9.9.9.1.zip'
            browser_download_url = 'https://example.invalid/duplicate.zip'
            digest = 'sha256:' + ('b' * 64)
            size = 1
        }

        { Select-FfnxSteamAsset -Release $release } | Should Throw 'exactly one FFNx Steam archive'
    }
}

Describe 'Install-FfnxSteamRuntime' {
    It 'installs verified files while preserving existing user configuration' {
        $fixture = New-TestDirectory
        try {
            $runtime = Join-Path $fixture 'runtime'
            New-Item -ItemType Directory -Path $runtime -Force | Out-Null
            Write-TestTextFile -Path (Join-Path $runtime 'FFNx.toml') -Content 'user-config'
            Write-TestTextFile -Path (Join-Path $runtime 'sfx\config.toml') -Content 'user-sfx'
            $archive = New-FfnxArchiveFixture -FixtureRoot $fixture
            $digest = 'sha256:' + (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
            $release = New-FfnxReleaseFixture -Digest $digest

            Install-FfnxSteamRuntime -RuntimeRoot $runtime -Release $release -ArchivePath $archive | Out-Null

            [IO.File]::ReadAllText((Join-Path $runtime 'AF3DN.P')) | Should Be 'ffnx-driver'
            [IO.File]::ReadAllText((Join-Path $runtime 'AF4DN.P')) | Should Be 'ffnx-proxy'
            [IO.File]::ReadAllText((Join-Path $runtime 'shaders\test.shader')) | Should Be 'shader-data'
            [IO.File]::ReadAllText((Join-Path $runtime 'FFNx.toml')) | Should Be 'user-config'
            [IO.File]::ReadAllText((Join-Path $runtime 'sfx\config.toml')) | Should Be 'user-sfx'
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'rejects an archive whose digest does not match before extracting files' {
        $fixture = New-TestDirectory
        try {
            $runtime = Join-Path $fixture 'runtime'
            New-Item -ItemType Directory -Path $runtime -Force | Out-Null
            $archive = New-FfnxArchiveFixture -FixtureRoot $fixture
            $release = New-FfnxReleaseFixture -Digest ('sha256:' + ('f' * 64))

            { Install-FfnxSteamRuntime -RuntimeRoot $runtime -Release $release -ArchivePath $archive } | Should Throw 'FFNx archive SHA-256 mismatch'
            (Test-Path -LiteralPath (Join-Path $runtime 'AF3DN.P')) | Should Be $false
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'rejects an archive missing the FFNx Steam driver' {
        $fixture = New-TestDirectory
        try {
            $runtime = Join-Path $fixture 'runtime'
            New-Item -ItemType Directory -Path $runtime -Force | Out-Null
            $archive = New-FfnxArchiveFixture -FixtureRoot $fixture -WithoutDriver
            $digest = 'sha256:' + (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
            $release = New-FfnxReleaseFixture -Digest $digest

            { Install-FfnxSteamRuntime -RuntimeRoot $runtime -Release $release -ArchivePath $archive } | Should Throw 'does not contain AF3DN.P'
            (Test-Path -LiteralPath (Join-Path $runtime 'AF4DN.P')) | Should Be $false
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'preflights every archive target before replacing any runtime file' {
        $fixture = New-TestDirectory
        try {
            $runtime = Join-Path $fixture 'runtime'
            Write-TestTextFile -Path (Join-Path $runtime 'AF3DN.P') -Content 'existing-driver'
            $collisionPath = Join-Path $runtime 'late\collision.dll'
            New-Item -ItemType Directory -Path $collisionPath -Force | Out-Null
            Write-TestTextFile -Path (Join-Path $collisionPath 'user-file.txt') -Content 'preserve'
            $before = Get-TestDirectoryFingerprint -Root $runtime
            $archive = New-FfnxArchiveFixture -FixtureRoot $fixture `
                -AdditionalFileRelativePath 'late\collision.dll'
            $digest = 'sha256:' + (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
            $release = New-FfnxReleaseFixture -Digest $digest

            { Install-FfnxSteamRuntime -RuntimeRoot $runtime -Release $release -ArchivePath $archive } |
                Should Throw 'FFNx target is not a regular file'
            (Get-TestDirectoryFingerprint -Root $runtime) | Should Be $before
            [IO.File]::ReadAllText((Join-Path $runtime 'AF3DN.P')) | Should Be 'existing-driver'
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'restores earlier FFNx files when a later atomic replacement fails' {
        $fixture = New-TestDirectory
        try {
            $runtime = Join-Path $fixture 'runtime'
            $driverPath = Join-Path $runtime 'AF3DN.P'
            $proxyPath = Join-Path $runtime 'AF4DN.P'
            Write-TestTextFile -Path $driverPath -Content 'existing-driver'
            Write-TestTextFile -Path $proxyPath -Content 'existing-proxy'
            (Get-Item -LiteralPath $proxyPath).Attributes = [IO.FileAttributes]::ReadOnly
            $beforeDriver = [IO.File]::ReadAllBytes($driverPath)
            $beforeProxy = [IO.File]::ReadAllBytes($proxyPath)
            $archive = New-FfnxArchiveFixture -FixtureRoot $fixture
            $digest = 'sha256:' + (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
            $release = New-FfnxReleaseFixture -Digest $digest

            { Install-FfnxSteamRuntime -RuntimeRoot $runtime -Release $release -ArchivePath $archive } |
                Should Throw
            [Convert]::ToBase64String([IO.File]::ReadAllBytes($driverPath)) |
                Should Be ([Convert]::ToBase64String($beforeDriver))
            [Convert]::ToBase64String([IO.File]::ReadAllBytes($proxyPath)) |
                Should Be ([Convert]::ToBase64String($beforeProxy))
            @(Get-ChildItem -LiteralPath $runtime -Filter '.ffnx-accessibility-*' -Force -Recurse).Count |
                Should Be 0
        }
        finally {
            if (Test-Path -LiteralPath (Join-Path $fixture 'runtime\AF4DN.P') -PathType Leaf) {
                (Get-Item -LiteralPath (Join-Path $fixture 'runtime\AF4DN.P')).Attributes = [IO.FileAttributes]::Normal
            }
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }
}

function New-SeventhHeavenSettingsFixture {
    param([Parameter(Mandatory=$true)] [string] $SettingsPath)

    Write-TestTextFile -Path $SettingsPath -Content @'
<?xml version="1.0" encoding="utf-8"?>
<Settings>
  <SubscribedUrls>
    <string>iros://Url/example</string>
  </SubscribedUrls>
  <LibraryLocation>C:\7thHeaven\library</LibraryLocation>
  <FF7Exe>C:\Old FF7\ff7_en.exe</FF7Exe>
  <FF7InstalledVersion>Steam</FF7InstalledVersion>
  <CurrentProfile>Accessibility</CurrentProfile>
  <GameLaunchSettings>
    <ShowLauncherWindow>false</ShowLauncherWindow>
    <InGameConfigOption>Custom Controller.cfg</InGameConfigOption>
  </GameLaunchSettings>
</Settings>
'@
}

Describe 'Update-SeventhHeavenSettings' {
    It 'changes only the executable and installed-version values' {
        $fixture = New-TestDirectory
        try {
            $settingsPath = Join-Path $fixture 'settings.xml'
            New-SeventhHeavenSettingsFixture -SettingsPath $settingsPath
            $installation = [pscustomobject]@{
                Version = 'Steam2026'
                GameExe = 'X:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\ff7\workingdir\ff7_en.exe'
            }

            $result = Update-SeventhHeavenSettings -SettingsPath $settingsPath -Installation $installation
            [xml] $settings = [IO.File]::ReadAllText($settingsPath)

            $result.Changed | Should Be $true
            $settings.Settings.FF7Exe | Should Be $installation.GameExe
            $settings.Settings.FF7InstalledVersion | Should Be 'SteamReRelease'
            $settings.Settings.LibraryLocation | Should Be 'C:\7thHeaven\library'
            $settings.Settings.CurrentProfile | Should Be 'Accessibility'
            $settings.Settings.SubscribedUrls.string | Should Be 'iros://Url/example'
            $settings.Settings.GameLaunchSettings.ShowLauncherWindow | Should Be 'false'
            $settings.Settings.GameLaunchSettings.InGameConfigOption | Should Be 'Custom Controller.cfg'
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'creates one backup on change and is idempotent when rerun' {
        $fixture = New-TestDirectory
        try {
            $settingsPath = Join-Path $fixture 'settings.xml'
            New-SeventhHeavenSettingsFixture -SettingsPath $settingsPath
            $installation = [pscustomobject]@{
                Version = 'Steam2026'
                GameExe = 'X:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\ff7\workingdir\ff7_en.exe'
            }

            $first = Update-SeventhHeavenSettings -SettingsPath $settingsPath -Installation $installation
            $backupsAfterFirst = @(Get-ChildItem -LiteralPath $fixture -Filter 'settings.xml.accessibility-backup-*')
            $second = Update-SeventhHeavenSettings -SettingsPath $settingsPath -Installation $installation
            $backupsAfterSecond = @(Get-ChildItem -LiteralPath $fixture -Filter 'settings.xml.accessibility-backup-*')

            $first.Changed | Should Be $true
            (Test-Path -LiteralPath $first.BackupPath -PathType Leaf) | Should Be $true
            $backupsAfterFirst.Count | Should Be 1
            $second.Changed | Should Be $false
            $second.BackupPath | Should BeNullOrEmpty
            $backupsAfterSecond.Count | Should Be 1
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'rejects malformed settings before writing or backing up' {
        $fixture = New-TestDirectory
        try {
            $settingsPath = Join-Path $fixture 'settings.xml'
            Write-TestTextFile -Path $settingsPath -Content '<Settings><FF7Exe>broken'
            $installation = [pscustomobject]@{
                Version = 'Steam2026'
                GameExe = 'X:\FF7\ff7_en.exe'
            }

            { Update-SeventhHeavenSettings -SettingsPath $settingsPath -Installation $installation } | Should Throw
            [IO.File]::ReadAllText($settingsPath) | Should Be '<Settings><FF7Exe>broken'
            @(Get-ChildItem -LiteralPath $fixture -Filter 'settings.xml.accessibility-backup-*').Count | Should Be 0
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }
}

Describe 'Install-Ff7OpeningMovieAudioDescription' {
    It 'installs the bundled narration beside an FFNx override movie' {
        $fixture = New-TestDirectory
        try {
            $runtime = Join-Path $fixture 'runtime'
            $source = Join-Path $fixture 'opening_audio_description.ogg'
            $nativeMovie = Join-Path $runtime 'data\movies\opening.avi'
            Write-TestTextFile -Path $source -Content 'narration-track'
            Write-TestTextFile -Path $nativeMovie -Content 'native-opening-movie'

            $result = Install-Ff7OpeningMovieAudioDescription -RuntimeRoot $runtime -SourcePath $source
            $target = Join-Path $runtime 'override\movies\opening_va.ogg'
            $targetMovie = Join-Path $runtime 'override\movies\opening.avi'

            $result.Changed | Should Be $true
            $result.MovieChanged | Should Be $true
            $result.VoiceChanged | Should Be $true
            $result.TargetPath | Should Be $target
            $result.TargetMoviePath | Should Be $targetMovie
            [IO.File]::ReadAllText($target) | Should Be 'narration-track'
            [IO.File]::ReadAllText($targetMovie) | Should Be 'native-opening-movie'
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'is idempotent when the exact narration track is already installed' {
        $fixture = New-TestDirectory
        try {
            $runtime = Join-Path $fixture 'runtime'
            $source = Join-Path $fixture 'opening_audio_description.ogg'
            $nativeMovie = Join-Path $runtime 'data\movies\opening.avi'
            Write-TestTextFile -Path $source -Content 'narration-track'
            Write-TestTextFile -Path $nativeMovie -Content 'native-opening-movie'

            Install-Ff7OpeningMovieAudioDescription -RuntimeRoot $runtime -SourcePath $source | Out-Null
            $result = Install-Ff7OpeningMovieAudioDescription -RuntimeRoot $runtime -SourcePath $source

            $result.Changed | Should Be $false
            $result.MovieChanged | Should Be $false
            $result.VoiceChanged | Should Be $false
            [IO.File]::ReadAllText($result.TargetPath) | Should Be 'narration-track'
            [IO.File]::ReadAllText($result.TargetMoviePath) | Should Be 'native-opening-movie'
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'does not overwrite a different existing FFNx movie voice track' {
        $fixture = New-TestDirectory
        try {
            $runtime = Join-Path $fixture 'runtime'
            $source = Join-Path $fixture 'opening_audio_description.ogg'
            $target = Join-Path $runtime 'override\movies\opening_va.ogg'
            $nativeMovie = Join-Path $runtime 'data\movies\opening.avi'
            $targetMovie = Join-Path $runtime 'override\movies\opening.avi'
            Write-TestTextFile -Path $source -Content 'narration-track'
            Write-TestTextFile -Path $nativeMovie -Content 'native-opening-movie'
            Write-TestTextFile -Path $target -Content 'custom-track'

            { Install-Ff7OpeningMovieAudioDescription -RuntimeRoot $runtime -SourcePath $source } |
                Should Throw 'Refusing to overwrite a different FFNx opening movie voice track'
            [IO.File]::ReadAllText($target) | Should Be 'custom-track'
            Test-Path -LiteralPath $targetMovie | Should Be $false
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'preserves an existing FFNx override movie while installing narration' {
        $fixture = New-TestDirectory
        try {
            $runtime = Join-Path $fixture 'runtime'
            $source = Join-Path $fixture 'opening_audio_description.ogg'
            $nativeMovie = Join-Path $runtime 'data\movies\opening.avi'
            $targetMovie = Join-Path $runtime 'override\movies\opening.avi'
            Write-TestTextFile -Path $source -Content 'narration-track'
            Write-TestTextFile -Path $nativeMovie -Content 'native-opening-movie'
            Write-TestTextFile -Path $targetMovie -Content 'modded-opening-movie'

            $result = Install-Ff7OpeningMovieAudioDescription -RuntimeRoot $runtime -SourcePath $source

            $result.Changed | Should Be $true
            $result.MovieChanged | Should Be $false
            $result.VoiceChanged | Should Be $true
            [IO.File]::ReadAllText($targetMovie) | Should Be 'modded-opening-movie'
            [IO.File]::ReadAllText($result.TargetPath) | Should Be 'narration-track'
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }
}

Describe 'Disable-Ff7OpeningMovieNativeVoiceLayer' {
    It 'removes the managed FFNx voice copy without touching the movie' {
        $fixture = New-TestDirectory
        try {
            $runtime = Join-Path $fixture 'runtime'
            $source = Join-Path $fixture 'opening_audio_description.ogg'
            $target = Join-Path $runtime 'override\movies\opening_va.ogg'
            $movie = Join-Path $runtime 'override\movies\opening.avi'
            Write-TestTextFile -Path $source -Content 'narration-track'
            Write-TestTextFile -Path $target -Content 'narration-track'
            Write-TestTextFile -Path $movie -Content 'active-opening-video'

            $result = Disable-Ff7OpeningMovieNativeVoiceLayer -RuntimeRoot $runtime -SourcePath $source

            $result.Removed | Should Be $true
            Test-Path -LiteralPath $target | Should Be $false
            [IO.File]::ReadAllText($movie) | Should Be 'active-opening-video'
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'is idempotent when no native FFNx voice copy exists' {
        $fixture = New-TestDirectory
        try {
            $runtime = Join-Path $fixture 'runtime'
            $source = Join-Path $fixture 'opening_audio_description.ogg'
            Write-TestTextFile -Path $source -Content 'narration-track'
            New-Item -ItemType Directory -Path $runtime -Force | Out-Null

            $result = Disable-Ff7OpeningMovieNativeVoiceLayer -RuntimeRoot $runtime -SourcePath $source

            $result.Removed | Should Be $false
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'refuses to remove a different custom FFNx voice track' {
        $fixture = New-TestDirectory
        try {
            $runtime = Join-Path $fixture 'runtime'
            $source = Join-Path $fixture 'opening_audio_description.ogg'
            $target = Join-Path $runtime 'override\movies\opening_va.ogg'
            Write-TestTextFile -Path $source -Content 'narration-track'
            Write-TestTextFile -Path $target -Content 'custom-track'

            { Disable-Ff7OpeningMovieNativeVoiceLayer -RuntimeRoot $runtime -SourcePath $source } |
                Should Throw 'Refusing to remove a different FFNx opening movie voice track'
            [IO.File]::ReadAllText($target) | Should Be 'custom-track'
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }
}

Describe 'Install-Ff7DualRuntimePackage' {
    $buildPath = Join-Path $scriptRoot 'Build-DualRuntimePackage.ps1'

    It 'validates the complete package before changing an installed mod' {
        $fixture = New-TestDirectory
        try {
            $invalidPackage = Join-Path $fixture 'invalid-package'
            $installedMod = Join-Path $fixture 'Reloaded\Mods\ff7.accessibility.reloaded'
            Write-TestTextFile -Path (Join-Path $invalidPackage 'ModConfig.json') -Content '{}'
            Write-TestTextFile -Path (Join-Path $installedMod 'sentinel.txt') -Content 'protected-installed-mod'
            $before = Get-TestDirectoryFingerprint -Root $installedMod

            { Install-Ff7DualRuntimePackage -PackagePath $invalidPackage -ModDirectory $installedMod } |
                Should Throw 'Dual-runtime package validation failed'

            (Get-TestDirectoryFingerprint -Root $installedMod) | Should Be $before
            [IO.File]::ReadAllText((Join-Path $installedMod 'sentinel.txt')) | Should Be 'protected-installed-mod'
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'rejects false dependency text invalid configuration and unbacked ReadyToRun headers' {
        $fixture = New-TestDirectory
        try {
            $package = Join-Path $fixture 'source\ff7.accessibility.reloaded'
            & $buildPath -OutputPath $package | Out-Null

            $falseDependencyPackage = Join-Path $fixture 'false-dependency'
            Copy-Item -LiteralPath $package -Destination $falseDependencyPackage -Recurse
            $depsPath = Join-Path $falseDependencyPackage 'x86\Ff7.Accessibility.Reloaded.deps.json'
            $deps = [IO.File]::ReadAllText($depsPath) | ConvertFrom-Json
            foreach ($target in $deps.targets.PSObject.Properties) {
                $target.Value.PSObject.Properties.Remove('Ff7.Accessibility.LegacyLayout/1.0.0')
            }
            $deps.libraries.PSObject.Properties.Remove('Ff7.Accessibility.LegacyLayout/1.0.0')
            Add-Member -InputObject $deps -NotePropertyName misleadingText `
                -NotePropertyValue 'Ff7.Accessibility.LegacyLayout' -Force
            Write-TestTextFile -Path $depsPath -Content ($deps | ConvertTo-Json -Depth 100)
            { Install-Ff7DualRuntimePackage -PackagePath $falseDependencyPackage `
                -ModDirectory (Join-Path $fixture 'deps-target\ff7.accessibility.reloaded') `
                -ValidateOnly } | Should Throw 'dependency manifest omits Ff7.Accessibility.LegacyLayout'

            $invalidConfigurationPackage = Join-Path $fixture 'invalid-configuration'
            Copy-Item -LiteralPath $package -Destination $invalidConfigurationPackage -Recurse
            Write-TestTextFile -Path (Join-Path $invalidConfigurationPackage 'Configuration\config.json') `
                -Content '{ invalid-json'
            { Install-Ff7DualRuntimePackage -PackagePath $invalidConfigurationPackage `
                -ModDirectory (Join-Path $fixture 'config-target\ff7.accessibility.reloaded') `
                -ValidateOnly } | Should Throw 'invalid Configuration/config.json'

            $missingFieldCuePackage = Join-Path $fixture 'missing-field-cue'
            Copy-Item -LiteralPath $package -Destination $missingFieldCuePackage -Recurse
            Remove-Item -LiteralPath (Join-Path $missingFieldCuePackage 'Assets\navigation\ladder_061.wav') -Force
            { Install-Ff7DualRuntimePackage -PackagePath $missingFieldCuePackage `
                -ModDirectory (Join-Path $fixture 'cue-target\ff7.accessibility.reloaded') `
                -ValidateOnly } | Should Throw 'missing Assets\navigation\ladder_061.wav'

            $unbackedR2rPackage = Join-Path $fixture 'unbacked-r2r'
            Copy-Item -LiteralPath $package -Destination $unbackedR2rPackage -Recurse
            Set-TestPeManagedNativeHeaderDirectory `
                -Path (Join-Path $unbackedR2rPackage 'x86\Ff7.Accessibility.Core.dll') `
                -VirtualAddress ([Convert]::ToUInt32('FFF00000', 16)) -Size 128
            { Install-Ff7DualRuntimePackage -PackagePath $unbackedR2rPackage `
                -ModDirectory (Join-Path $fixture 'r2r-target\ff7.accessibility.reloaded') `
                -ValidateOnly } | Should Throw 'unmappable ManagedNativeHeaderDirectory'
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'refuses unrelated or reparse-point mod targets without changing their bytes' {
        $fixture = New-TestDirectory
        try {
            $package = Join-Path $fixture 'source\ff7.accessibility.reloaded'
            & $buildPath -OutputPath $package | Out-Null

            $wrongSource = Join-Path $fixture 'wrong-source'
            Copy-Item -LiteralPath $package -Destination $wrongSource -Recurse
            $wrongSourceConfigPath = Join-Path $wrongSource 'ModConfig.json'
            $wrongSourceConfig = [IO.File]::ReadAllText($wrongSourceConfigPath) | ConvertFrom-Json
            $wrongSourceConfig.ModId = 'unrelated.mod'
            Write-TestTextFile -Path $wrongSourceConfigPath -Content ($wrongSourceConfig | ConvertTo-Json -Depth 8)
            $wrongSourceTarget = Join-Path $fixture 'wrong-source-target\ff7.accessibility.reloaded'
            { Install-Ff7DualRuntimePackage -PackagePath $wrongSource -ModDirectory $wrongSourceTarget } |
                Should Throw 'unexpected ModId'
            Test-Path -LiteralPath $wrongSourceTarget | Should Be $false

            $validationTarget = Join-Path $fixture 'validation-only\ff7.accessibility.reloaded'
            $validation = Install-Ff7DualRuntimePackage -PackagePath $package `
                -ModDirectory $validationTarget -ValidateOnly
            $validation.Validated | Should Be $true
            Test-Path -LiteralPath $validationTarget | Should Be $false

            $wrongLeaf = Join-Path $fixture 'Reloaded\Mods\unrelated-user-directory'
            Write-TestTextFile -Path (Join-Path $wrongLeaf 'sentinel.txt') -Content 'preserve-wrong-leaf'
            $wrongLeafFingerprint = Get-TestDirectoryFingerprint -Root $wrongLeaf
            { Install-Ff7DualRuntimePackage -PackagePath $package -ModDirectory $wrongLeaf } | Should Throw
            (Get-TestDirectoryFingerprint -Root $wrongLeaf) | Should Be $wrongLeafFingerprint

            $unownedTarget = Join-Path $fixture 'Reloaded\Mods\ff7.accessibility.reloaded'
            Write-TestTextFile -Path (Join-Path $unownedTarget 'sentinel.txt') -Content 'preserve-unowned'
            $unownedFingerprint = Get-TestDirectoryFingerprint -Root $unownedTarget
            { Install-Ff7DualRuntimePackage -PackagePath $package -ModDirectory $unownedTarget } | Should Throw
            (Get-TestDirectoryFingerprint -Root $unownedTarget) | Should Be $unownedFingerprint

            Remove-Item -LiteralPath $unownedTarget -Recurse -Force
            $junctionTarget = Join-Path $fixture 'protected-junction-target'
            Write-TestTextFile -Path (Join-Path $junctionTarget 'sentinel.txt') -Content 'preserve-junction'
            $junctionFingerprint = Get-TestDirectoryFingerprint -Root $junctionTarget
            New-Item -ItemType Junction -Path $unownedTarget -Target $junctionTarget | Out-Null
            { Install-Ff7DualRuntimePackage -PackagePath $package -ModDirectory $unownedTarget } | Should Throw
            (Get-TestDirectoryFingerprint -Root $junctionTarget) | Should Be $junctionFingerprint
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'preserves installed configuration and is stable across consecutive runs' {
        $fixture = New-TestDirectory
        try {
            $package = Join-Path $fixture 'package-source\ff7.accessibility.reloaded'
            $installedMod = Join-Path $fixture 'Reloaded\Mods\ff7.accessibility.reloaded'
            $backupRoot = Join-Path $fixture 'Reloaded\AccessibilityBackups'
            & $buildPath -OutputPath $package | Out-Null

            $installedConfiguration = Join-Path $installedMod 'Configuration\config.json'
            New-Item -ItemType Directory -Path $installedMod -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $package 'ModConfig.json') `
                -Destination (Join-Path $installedMod 'ModConfig.json')
            $protectedConfigurationBytes = [Text.Encoding]::UTF8.GetBytes("{`r`n  `"SpeechRate`": 77,`r`n  `"UserValue`": `"keep-exactly`"`r`n}")
            New-Item -ItemType Directory -Path (Split-Path -Parent $installedConfiguration) -Force | Out-Null
            [IO.File]::WriteAllBytes($installedConfiguration, $protectedConfigurationBytes)
            Write-TestTextFile -Path (Join-Path $installedMod 'obsolete.dll') -Content 'old-package'
            $protectedConfigurationHash = (Get-FileHash -LiteralPath $installedConfiguration -Algorithm SHA256).Hash

            $first = Install-Ff7DualRuntimePackage -PackagePath $package -ModDirectory $installedMod
            $first.Changed | Should Be $true
            Test-Path -LiteralPath $first.BackupPath -PathType Container | Should Be $true
            (Split-Path -Parent $first.BackupPath) | Should Be $backupRoot
            Test-Path -LiteralPath (Join-Path $first.BackupPath 'obsolete.dll') -PathType Leaf | Should Be $true
            (Get-FileHash -LiteralPath $installedConfiguration -Algorithm SHA256).Hash |
                Should Be $protectedConfigurationHash
            [Convert]::ToBase64String([IO.File]::ReadAllBytes($installedConfiguration)) |
                Should Be ([Convert]::ToBase64String($protectedConfigurationBytes))
            Test-Path -LiteralPath (Join-Path $installedMod 'obsolete.dll') | Should Be $false
            $firstFingerprint = Get-TestDirectoryFingerprint -Root $installedMod

            $second = Install-Ff7DualRuntimePackage -PackagePath $package -ModDirectory $installedMod
            $second.Changed | Should Be $false
            $second.BackupPath | Should BeNullOrEmpty
            (Get-TestDirectoryFingerprint -Root $installedMod) | Should Be $firstFingerprint
            (Get-FileHash -LiteralPath $installedConfiguration -Algorithm SHA256).Hash |
                Should Be $protectedConfigurationHash
            @(Get-ChildItem -LiteralPath (Split-Path -Parent $installedMod) -Directory -Filter 'ff7.accessibility.reloaded.backup-*').Count |
                Should Be 0
            @(Get-ChildItem -LiteralPath $backupRoot -Directory -Filter 'ff7.accessibility.reloaded.backup-*').Count |
                Should Be 1
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }
}

Describe 'Install-Ff7LegacyReloadedProfile' {
    $templatePath = Join-Path $scriptRoot 'templates\Ff7.Legacy.Steam.AppConfig.json'

    It 'creates a fresh legacy profile and validates without mutating in preflight mode' {
        $fixture = New-TestDirectory
        try {
            $runtimeRoot = Join-Path $fixture 'game\ff7\workingdir'
            $gameExe = Join-Path $runtimeRoot 'ff7_en.exe'
            Write-TestTextFile -Path $gameExe -Content 'legacy game fixture'
            $runtime = [pscustomobject]@{ Architecture='x86'; RuntimeRoot=$runtimeRoot; GameExe=$gameExe }
            $reloadedRoot = Join-Path $fixture 'Reloaded-II'

            $validation = Install-Ff7LegacyReloadedProfile -ReloadedRoot $reloadedRoot `
                -LegacyRuntime $runtime -TemplatePath $templatePath -ValidateOnly
            $validation.Validated | Should Be $true
            Test-Path -LiteralPath (Join-Path $reloadedRoot 'Apps') | Should Be $false

            $installed = Install-Ff7LegacyReloadedProfile -ReloadedRoot $reloadedRoot `
                -LegacyRuntime $runtime -TemplatePath $templatePath
            $installed.Changed | Should Be $true
            $installed.BackupPath | Should BeNullOrEmpty
            $profile = [IO.File]::ReadAllText($installed.ProfilePath) | ConvertFrom-Json
            $profile.AppId | Should Be 'ff7_en.exe'
            $profile.AppLocation | Should Be ([IO.Path]::GetFullPath($gameExe))
            $profile.WorkingDirectory | Should Be ([IO.Path]::GetFullPath($runtimeRoot))
            @($profile.EnabledMods) | Should Be @('reloaded.sharedlib.hooks','ff7.accessibility.reloaded')
            @($profile.SortedMods) | Should Be @('reloaded.sharedlib.hooks','ff7.accessibility.reloaded')
        }
        finally { Remove-Item -LiteralPath $fixture -Recurse -Force }
    }

    It 'merges required mods after unrelated mods and is byte-idempotent' {
        $fixture = New-TestDirectory
        try {
            $runtimeRoot = Join-Path $fixture 'runtime'
            $gameExe = Join-Path $runtimeRoot 'ff7_en.exe'
            Write-TestTextFile -Path $gameExe -Content 'legacy game fixture'
            $runtime = [pscustomobject]@{ Architecture='x86'; RuntimeRoot=$runtimeRoot; GameExe=$gameExe }
            $reloadedRoot = Join-Path $fixture 'Reloaded-II'
            $profilePath = Join-Path $reloadedRoot 'Apps\Ff7.En.Steam\AppConfig.json'
            Write-TestTextFile -Path $profilePath -Content @'
{
  "AppId": "ff7_en.exe",
  "AppName": "My FFVII",
  "AppLocation": "C:\\Old\\ff7_en.exe",
  "AppArguments": "-existing",
  "AppIcon": "",
  "AutoInject": false,
  "EnabledMods": ["existing.mod", "ff7.accessibility.reloaded", "another.mod"],
  "WorkingDirectory": "C:\\Old",
  "PluginData": {"keep": true},
  "SortedMods": ["another.mod", "reloaded.sharedlib.hooks", "existing.mod"],
  "PreserveDisabledModOrder": true,
  "DontInject": false,
  "IsMsStore": false
}
'@

            $first = Install-Ff7LegacyReloadedProfile -ReloadedRoot $reloadedRoot `
                -LegacyRuntime $runtime -TemplatePath $templatePath
            $first.Changed | Should Be $true
            Test-Path -LiteralPath $first.BackupPath -PathType Leaf | Should Be $true
            $profile = [IO.File]::ReadAllText($profilePath) | ConvertFrom-Json
            @($profile.EnabledMods) | Should Be @('existing.mod','another.mod','reloaded.sharedlib.hooks','ff7.accessibility.reloaded')
            @($profile.SortedMods) | Should Be @('another.mod','existing.mod','reloaded.sharedlib.hooks','ff7.accessibility.reloaded')
            $profile.AppArguments | Should Be '-existing'
            $profile.PluginData.keep | Should Be $true
            $installedHash = (Get-FileHash -LiteralPath $profilePath -Algorithm SHA256).Hash

            $second = Install-Ff7LegacyReloadedProfile -ReloadedRoot $reloadedRoot `
                -LegacyRuntime $runtime -TemplatePath $templatePath
            $second.Changed | Should Be $false
            $second.BackupPath | Should BeNullOrEmpty
            (Get-FileHash -LiteralPath $profilePath -Algorithm SHA256).Hash | Should Be $installedHash
            @(Get-ChildItem -LiteralPath (Split-Path -Parent $profilePath) -Filter 'AppConfig.json.backup-*' -File).Count | Should Be 1
        }
        finally { Remove-Item -LiteralPath $fixture -Recurse -Force }
    }

    It 'rejects a non-x86 runtime and an existing profile for another executable' {
        $fixture = New-TestDirectory
        try {
            $runtimeRoot = Join-Path $fixture 'runtime'
            $gameExe = Join-Path $runtimeRoot 'ff7_en.exe'
            Write-TestTextFile -Path $gameExe -Content 'legacy game fixture'
            $reloadedRoot = Join-Path $fixture 'Reloaded-II'
            $wrongArchitecture = [pscustomobject]@{ Architecture='x64'; RuntimeRoot=$runtimeRoot; GameExe=$gameExe }
            { Install-Ff7LegacyReloadedProfile -ReloadedRoot $reloadedRoot `
                -LegacyRuntime $wrongArchitecture -TemplatePath $templatePath } | Should Throw

            $profilePath = Join-Path $reloadedRoot 'Apps\Ff7.En.Steam\AppConfig.json'
            Write-TestTextFile -Path $profilePath -Content '{"AppId":"another.exe","EnabledMods":[],"SortedMods":[]}'
            $runtime = [pscustomobject]@{ Architecture='x86'; RuntimeRoot=$runtimeRoot; GameExe=$gameExe }
            { Install-Ff7LegacyReloadedProfile -ReloadedRoot $reloadedRoot `
                -LegacyRuntime $runtime -TemplatePath $templatePath } | Should Throw
            ([IO.File]::ReadAllText($profilePath) | ConvertFrom-Json).AppId | Should Be 'another.exe'
        }
        finally { Remove-Item -LiteralPath $fixture -Recurse -Force }
    }
}

Describe 'Install-Ff7NativeReloadedProfile' {
    $templatePath = Join-Path $scriptRoot 'templates\Ff7.Native.Steam2026.AppConfig.json'
    $parityMatrixPath = Join-Path $scriptRoot 'analysis\dual_runtime\parity-matrix.json'

    It 'rejects a fabricated native identity before writing a profile' {
        $fixture = New-TestDirectory
        try {
            $reloadedRoot = Join-Path $fixture 'Reloaded-II'
            $legacyProfile = Join-Path $reloadedRoot 'Apps\Ff7.En.Steam\AppConfig.json'
            Write-TestTextFile -Path $legacyProfile -Content '{"AppId":"ff7_en.exe","protected":true}'
            $protectedHash = (Get-FileHash -LiteralPath $legacyProfile -Algorithm SHA256).Hash
            $fabricatedExecutable = Join-Path $fixture 'fabricated-FFVII.exe'
            Write-TestTextFile -Path $fabricatedExecutable -Content 'not-the-supported-native-executable'
            $fabricatedRuntime = [pscustomobject]@{
                Architecture = 'x64'
                RuntimeRoot = $fixture
                GameExe = $fabricatedExecutable
                Machine = 0x8664
                Sha256 = $knownSteam2026NativeSha256
            }

            { Install-Ff7NativeReloadedProfile -ReloadedRoot $reloadedRoot `
                -NativeRuntime $fabricatedRuntime -TemplatePath $templatePath } | Should Throw
            Test-Path -LiteralPath (Join-Path $reloadedRoot 'Apps\Ff7.Native.Steam2026\AppConfig.json') |
                Should Be $false
            (Get-FileHash -LiteralPath $legacyProfile -Algorithm SHA256).Hash | Should Be $protectedHash
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'adds the native profile without changing protected legacy profile bytes' {
        $fixture = New-TestDirectory
        try {
            $reloadedRoot = Join-Path $fixture 'Reloaded-II'
            $legacyProfile = Join-Path $reloadedRoot 'Apps\Ff7.En.Steam\AppConfig.json'
            $ordinaryNativeProfile = Join-Path $reloadedRoot 'Apps\Ff7.Native.Steam2026\AppConfig.json'
            $nativeProfile = Join-Path $reloadedRoot 'Apps\Ff7.Native.Steam2026.Research\AppConfig.json'
            $protectedLegacyBytes = [Text.Encoding]::UTF8.GetBytes("{`r`n  `"AppId`": `"ff7_en.exe`",`r`n  `"Protected`": true`r`n}")
            New-Item -ItemType Directory -Path (Split-Path -Parent $legacyProfile) -Force | Out-Null
            [IO.File]::WriteAllBytes($legacyProfile, $protectedLegacyBytes)
            Write-TestTextFile -Path $nativeProfile -Content '{"old":true}'
            $protectedLegacyHash = (Get-FileHash -LiteralPath $legacyProfile -Algorithm SHA256).Hash
            $nativeRuntime = [pscustomobject]@{
                Architecture = 'x64'
                RuntimeRoot = Split-Path -Parent $knownSteam2026Native
                GameExe = $knownSteam2026Native
                Machine = 0x8664
                Sha256 = $knownSteam2026NativeSha256
            }

            $first = Install-Ff7NativeReloadedProfile -ReloadedRoot $reloadedRoot `
                -NativeRuntime $nativeRuntime -TemplatePath $templatePath `
                -ParityMatrixPath $parityMatrixPath -AllowResearch
            $first.Changed | Should Be $true
            $first.IsResearchProfile | Should Be $true
            Test-Path -LiteralPath $first.BackupPath -PathType Leaf | Should Be $true
            [IO.File]::ReadAllText($first.BackupPath) | Should Match '"old"'
            (Get-FileHash -LiteralPath $legacyProfile -Algorithm SHA256).Hash | Should Be $protectedLegacyHash
            [Convert]::ToBase64String([IO.File]::ReadAllBytes($legacyProfile)) |
                Should Be ([Convert]::ToBase64String($protectedLegacyBytes))

            $nativeConfig = [IO.File]::ReadAllText($nativeProfile) | ConvertFrom-Json
            $nativeConfig.AppId | Should Be 'FFVII.exe'
            $nativeConfig.AppName | Should Be 'RESEARCH ONLY - FFVII Steam 2026 - ACCESSIBILITY INCOMPLETE'
            $nativeConfig.AppLocation | Should Be $nativeRuntime.GameExe
            $nativeConfig.WorkingDirectory | Should Be $nativeRuntime.RuntimeRoot
            $nativeConfig.AutoInject | Should Be $false
            $nativeConfig.DontInject | Should Be $false
            @($nativeConfig.EnabledMods) -contains 'reloaded.sharedlib.hooks' | Should Be $true
            @($nativeConfig.EnabledMods) -contains 'ff7.accessibility.reloaded' | Should Be $true
            @($nativeConfig.SortedMods) -contains 'reloaded.sharedlib.hooks' | Should Be $true
            @($nativeConfig.SortedMods) -contains 'ff7.accessibility.reloaded' | Should Be $true
            Test-Path -LiteralPath $ordinaryNativeProfile | Should Be $false
            $firstNativeHash = (Get-FileHash -LiteralPath $nativeProfile -Algorithm SHA256).Hash

            $second = Install-Ff7NativeReloadedProfile -ReloadedRoot $reloadedRoot `
                -NativeRuntime $nativeRuntime -TemplatePath $templatePath `
                -ParityMatrixPath $parityMatrixPath -AllowResearch
            $second.Changed | Should Be $false
            $second.IsResearchProfile | Should Be $true
            $second.BackupPath | Should BeNullOrEmpty
            (Get-FileHash -LiteralPath $nativeProfile -Algorithm SHA256).Hash | Should Be $firstNativeHash
            (Get-FileHash -LiteralPath $legacyProfile -Algorithm SHA256).Hash | Should Be $protectedLegacyHash
            @(Get-ChildItem -LiteralPath (Split-Path -Parent $nativeProfile) -File -Filter 'AppConfig.json.backup-*').Count |
                Should Be 1
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'refuses to create an ordinary native profile while the release matrix is closed' {
        $fixture = New-TestDirectory
        try {
            $reloadedRoot = Join-Path $fixture 'Reloaded-II'
            $nativeRuntime = [pscustomobject]@{
                Architecture = 'x64'
                RuntimeRoot = Split-Path -Parent $knownSteam2026Native
                GameExe = $knownSteam2026Native
                Machine = 0x8664
                Sha256 = $knownSteam2026NativeSha256
            }

            { Install-Ff7NativeReloadedProfile -ReloadedRoot $reloadedRoot `
                -NativeRuntime $nativeRuntime -TemplatePath $templatePath `
                -ParityMatrixPath $parityMatrixPath } | Should Throw
            Test-Path -LiteralPath (Join-Path $reloadedRoot 'Apps\Ff7.Native.Steam2026\AppConfig.json') |
                Should Be $false
            Test-Path -LiteralPath (Join-Path $reloadedRoot 'Apps\Ff7.Native.Steam2026.Research\AppConfig.json') |
                Should Be $false

            $researchValidation = Install-Ff7NativeReloadedProfile -ReloadedRoot $reloadedRoot `
                -NativeRuntime $nativeRuntime -TemplatePath $templatePath `
                -ParityMatrixPath $parityMatrixPath -AllowResearch -ValidateOnly
            $researchValidation.Validated | Should Be $true
            $researchValidation.IsResearchProfile | Should Be $true
            Test-Path -LiteralPath (Join-Path $reloadedRoot 'Apps') | Should Be $false
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'rejects native profile template drift before writing any profile' {
        $fixture = New-TestDirectory
        try {
            $nativeRuntime = [pscustomobject]@{
                Architecture = 'x64'
                RuntimeRoot = Split-Path -Parent $knownSteam2026Native
                GameExe = $knownSteam2026Native
                Machine = 0x8664
                Sha256 = $knownSteam2026NativeSha256
            }
            $baseTemplate = [IO.File]::ReadAllText($templatePath) | ConvertFrom-Json
            $variants = @(
                [pscustomobject]@{ Name = 'wrong AppId'; Apply = { param($p) $p.AppId = 'other.exe' } },
                [pscustomobject]@{ Name = 'DontInject'; Apply = { param($p) $p.DontInject = $true } },
                [pscustomobject]@{ Name = 'missing accessibility mod'; Apply = { param($p) $p.EnabledMods = @('reloaded.sharedlib.hooks') } },
                [pscustomobject]@{ Name = 'wrong sorted order'; Apply = { param($p) $p.SortedMods = @('ff7.accessibility.reloaded', 'reloaded.sharedlib.hooks') } },
                [pscustomobject]@{ Name = 'non-string AppLocation'; Apply = { param($p) $p.AppLocation = 42 } }
            )

            foreach ($variant in $variants) {
                $reloadedRoot = Join-Path $fixture ([Guid]::NewGuid().ToString('N'))
                $variantPath = Join-Path $reloadedRoot 'template.json'
                $profile = [IO.File]::ReadAllText($templatePath) | ConvertFrom-Json
                & $variant.Apply $profile
                Write-TestTextFile -Path $variantPath -Content ($profile | ConvertTo-Json -Depth 8)
                { Install-Ff7NativeReloadedProfile -ReloadedRoot $reloadedRoot `
                    -NativeRuntime $nativeRuntime -TemplatePath $variantPath `
                    -ParityMatrixPath $parityMatrixPath -AllowResearch } | Should Throw
                @(Get-ChildItem -LiteralPath (Join-Path $reloadedRoot 'Apps') -Filter AppConfig.json `
                    -File -Recurse -ErrorAction SilentlyContinue).Count | Should Be 0
            }
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }
}

Describe 'Assert-Ff7NativeParityReleaseGate' {
    It 'rejects the current research matrix unless an explicit research override is supplied' {
        $fixture = New-TestDirectory
        try {
            $matrixPath = Join-Path $fixture 'parity-matrix.json'
            Write-TestTextFile -Path $matrixPath -Content @'
{
  "schemaVersion": 1,
  "policy": {
    "partialRuntimeMayBeReleased": false,
    "staticEvidenceMayEnableSpeech": false
  },
  "runtimes": {
    "steam2026X64": {
      "runtimeId": "ff7-steam-2026-x64",
      "sha256": "57A23D166D69E46B9E3339F779D4A3C4FEB402A989FA7291D0D9B4A1953ABB4B",
      "releaseStatus": "research-only-fail-closed"
    }
  },
  "capabilities": [
    { "capability": "Lifecycle", "x64SpeechEnabled": false },
    { "capability": "ForegroundInput", "x64SpeechEnabled": false },
    { "capability": "Menus", "x64SpeechEnabled": false },
    { "capability": "Dialogue", "x64SpeechEnabled": false },
    { "capability": "Field", "x64SpeechEnabled": false },
    { "capability": "Navigation", "x64SpeechEnabled": false },
    { "capability": "Battle", "x64SpeechEnabled": false },
    { "capability": "Movies", "x64SpeechEnabled": false },
    { "capability": "Saves", "x64SpeechEnabled": false }
  ],
  "releaseGate": {
    "steam2026X64Ready": false,
    "blockingCapabilities": ["Menus"],
    "requiredUserLedValidation": true,
    "userLedValidationComplete": false
  }
}
'@

            { Assert-Ff7NativeParityReleaseGate -ParityMatrixPath $matrixPath } |
                Should Throw 'Native Steam 2026 release gate is closed'

            $research = Assert-Ff7NativeParityReleaseGate -ParityMatrixPath $matrixPath -AllowResearch
            $research.IsReleaseReady | Should Be $false
            $research.IsResearchOverride | Should Be $true
            @($research.BlockingCapabilities) -contains 'Menus' | Should Be $true
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'accepts only a full live-validated matrix with every capability enabled' {
        $fixture = New-TestDirectory
        try {
            $matrixPath = Join-Path $fixture 'parity-matrix.json'
            Write-TestTextFile -Path $matrixPath -Content @'
{
  "schemaVersion": 1,
  "policy": {
    "partialRuntimeMayBeReleased": false,
    "staticEvidenceMayEnableSpeech": false
  },
  "runtimes": {
    "steam2026X64": {
      "runtimeId": "ff7-steam-2026-x64",
      "sha256": "57A23D166D69E46B9E3339F779D4A3C4FEB402A989FA7291D0D9B4A1953ABB4B",
      "releaseStatus": "supported"
    }
  },
  "capabilities": [
    { "capability": "Lifecycle", "x64SpeechEnabled": true },
    { "capability": "ForegroundInput", "x64SpeechEnabled": true },
    { "capability": "Menus", "x64SpeechEnabled": true },
    { "capability": "Dialogue", "x64SpeechEnabled": true },
    { "capability": "Field", "x64SpeechEnabled": true },
    { "capability": "Navigation", "x64SpeechEnabled": true },
    { "capability": "Battle", "x64SpeechEnabled": true },
    { "capability": "Movies", "x64SpeechEnabled": true },
    { "capability": "Saves", "x64SpeechEnabled": true }
  ],
  "releaseGate": {
    "steam2026X64Ready": true,
    "blockingCapabilities": [],
    "requiredUserLedValidation": true,
    "userLedValidationComplete": true
  }
}
'@

            $release = Assert-Ff7NativeParityReleaseGate -ParityMatrixPath $matrixPath
            $release.IsReleaseReady | Should Be $true
            $release.IsResearchOverride | Should Be $false

            $blockedPath = Join-Path $fixture 'blocked.json'
            $blockedText = [IO.File]::ReadAllText($matrixPath).Replace(
                '"blockingCapabilities": []',
                '"blockingCapabilities": ["Dialogue"]')
            Write-TestTextFile -Path $blockedPath -Content $blockedText
            { Assert-Ff7NativeParityReleaseGate -ParityMatrixPath $blockedPath } | Should Throw

            $disabledPath = Join-Path $fixture 'disabled.json'
            $disabledText = [IO.File]::ReadAllText($matrixPath).Replace(
                '"capability": "Dialogue", "x64SpeechEnabled": true',
                '"capability": "Dialogue", "x64SpeechEnabled": false')
            Write-TestTextFile -Path $disabledPath -Content $disabledText
            { Assert-Ff7NativeParityReleaseGate -ParityMatrixPath $disabledPath } | Should Throw

            $readyMatrix = [IO.File]::ReadAllText($matrixPath) | ConvertFrom-Json

            $missingPath = Join-Path $fixture 'missing-capability.json'
            $readyMatrix.capabilities = @($readyMatrix.capabilities | Where-Object { $_.capability -ne 'Saves' })
            Write-TestTextFile -Path $missingPath -Content ($readyMatrix | ConvertTo-Json -Depth 8)
            { Assert-Ff7NativeParityReleaseGate -ParityMatrixPath $missingPath } | Should Throw

            $duplicatePath = Join-Path $fixture 'duplicate-capability.json'
            $readyMatrix = [IO.File]::ReadAllText($matrixPath) | ConvertFrom-Json
            $readyMatrix.capabilities = @($readyMatrix.capabilities) + @($readyMatrix.capabilities[0])
            Write-TestTextFile -Path $duplicatePath -Content ($readyMatrix | ConvertTo-Json -Depth 8)
            { Assert-Ff7NativeParityReleaseGate -ParityMatrixPath $duplicatePath } | Should Throw

            $unknownPath = Join-Path $fixture 'unknown-capability.json'
            $readyMatrix = [IO.File]::ReadAllText($matrixPath) | ConvertFrom-Json
            $readyMatrix.capabilities[0].capability = 'UnknownDomain'
            Write-TestTextFile -Path $unknownPath -Content ($readyMatrix | ConvertTo-Json -Depth 8)
            { Assert-Ff7NativeParityReleaseGate -ParityMatrixPath $unknownPath } | Should Throw

            $wrongRuntimePath = Join-Path $fixture 'wrong-runtime.json'
            $readyMatrix = [IO.File]::ReadAllText($matrixPath) | ConvertFrom-Json
            $readyMatrix.runtimes.steam2026X64.sha256 = ('0' * 64)
            Write-TestTextFile -Path $wrongRuntimePath -Content ($readyMatrix | ConvertTo-Json -Depth 8)
            { Assert-Ff7NativeParityReleaseGate -ParityMatrixPath $wrongRuntimePath } | Should Throw
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }
}

Describe 'Native launcher profile preflight' {
    $launcherPath = Join-Path $scriptRoot 'Launch-FF7Reloaded.ps1'
    $templatePath = Join-Path $scriptRoot 'templates\Ff7.Native.Steam2026.AppConfig.json'
    $parityMatrixPath = Join-Path $scriptRoot 'analysis\dual_runtime\parity-matrix.json'

    It 'rejects missing or mismatched research profiles before Start-Process' {
        $fixture = New-TestDirectory
        try {
            $reloadedRoot = Join-Path $fixture 'Reloaded-II'
            Write-TestTextFile -Path (Join-Path $reloadedRoot 'Reloaded-II.exe') -Content 'fixture'
            $nativeRuntime = [pscustomobject]@{
                Architecture = 'x64'
                RuntimeRoot = Split-Path -Parent $knownSteam2026Native
                GameExe = $knownSteam2026Native
                Machine = 0x8664
                Sha256 = $knownSteam2026NativeSha256
            }
            $installed = Install-Ff7NativeReloadedProfile -ReloadedRoot $reloadedRoot `
                -NativeRuntime $nativeRuntime -TemplatePath $templatePath `
                -ParityMatrixPath $parityMatrixPath -AllowResearch
            $profilePath = $installed.ProfilePath
            $validBytes = [IO.File]::ReadAllBytes($profilePath)

            Mock Start-Process { }

            Remove-Item -LiteralPath $profilePath -Force
            { & $launcherPath -Runtime Native -GameRoot $nativeRuntime.RuntimeRoot `
                -ReloadedRoot $reloadedRoot -ParityMatrixPath $parityMatrixPath `
                -AllowResearchNative } | Should Throw
            Assert-MockCalled Start-Process -Times 0 -Exactly

            [IO.File]::WriteAllBytes($profilePath, $validBytes)
            $profile = [IO.File]::ReadAllText($profilePath) | ConvertFrom-Json
            $profile.AppId = 'wrong.exe'
            Write-TestTextFile -Path $profilePath -Content ($profile | ConvertTo-Json -Depth 8)
            { & $launcherPath -Runtime Native -GameRoot $nativeRuntime.RuntimeRoot `
                -ReloadedRoot $reloadedRoot -ParityMatrixPath $parityMatrixPath `
                -AllowResearchNative } | Should Throw
            Assert-MockCalled Start-Process -Times 0 -Exactly

            [IO.File]::WriteAllBytes($profilePath, $validBytes)
            $profile = [IO.File]::ReadAllText($profilePath) | ConvertFrom-Json
            $profile.AppLocation = Join-Path $fixture 'other.exe'
            Write-TestTextFile -Path $profilePath -Content ($profile | ConvertTo-Json -Depth 8)
            { & $launcherPath -Runtime Native -GameRoot $nativeRuntime.RuntimeRoot `
                -ReloadedRoot $reloadedRoot -ParityMatrixPath $parityMatrixPath `
                -AllowResearchNative } | Should Throw
            Assert-MockCalled Start-Process -Times 0 -Exactly

            [IO.File]::WriteAllBytes($profilePath, $validBytes)
            $profile = [IO.File]::ReadAllText($profilePath) | ConvertFrom-Json
            $profile.DontInject = $true
            Write-TestTextFile -Path $profilePath -Content ($profile | ConvertTo-Json -Depth 8)
            { & $launcherPath -Runtime Native -GameRoot $nativeRuntime.RuntimeRoot `
                -ReloadedRoot $reloadedRoot -ParityMatrixPath $parityMatrixPath `
                -AllowResearchNative } | Should Throw
            Assert-MockCalled Start-Process -Times 0 -Exactly

            [IO.File]::WriteAllBytes($profilePath, $validBytes)
            $profile = [IO.File]::ReadAllText($profilePath) | ConvertFrom-Json
            $profile.EnabledMods = @('reloaded.sharedlib.hooks')
            Write-TestTextFile -Path $profilePath -Content ($profile | ConvertTo-Json -Depth 8)
            { & $launcherPath -Runtime Native -GameRoot $nativeRuntime.RuntimeRoot `
                -ReloadedRoot $reloadedRoot -ParityMatrixPath $parityMatrixPath `
                -AllowResearchNative } | Should Throw
            Assert-MockCalled Start-Process -Times 0 -Exactly

            [IO.File]::WriteAllBytes($profilePath, $validBytes)
            & $launcherPath -Runtime Native -GameRoot $nativeRuntime.RuntimeRoot `
                -ReloadedRoot $reloadedRoot -ParityMatrixPath $parityMatrixPath `
                -AllowResearchNative
            Assert-MockCalled Start-Process -Times 1 -Exactly
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }
}

Describe 'Installer and launcher integration' {
    $installerPath = Join-Path $scriptRoot 'Install-FF7ReloadedMod.ps1'
    $launcherPath = Join-Path $scriptRoot 'Launch-FF7Reloaded.ps1'
    $installerCmdPath = Join-Path $scriptRoot 'Install-FF7ReloadedMod.cmd'
    $launcherCmdPath = Join-Path $scriptRoot 'Launch-FF7Reloaded.cmd'

    It 'uses the shared installation module for every compatibility step' {
        $installer = [IO.File]::ReadAllText($installerPath)

        $installer | Should Match 'Import-Module'
        $installer | Should Match 'Resolve-Ff7Installation'
        $installer | Should Match 'Initialize-Ff7CompatibilityRuntime'
        $installer | Should Match 'Install-FfnxSteamRuntime'
        $installer | Should Match 'Disable-Ff7OpeningMovieNativeVoiceLayer'
        $installer | Should Not Match 'Update-SeventhHeavenSettings'
        $installer | Should Match 'Build-DualRuntimePackage\.ps1'
        $installer | Should Match 'Assert-Ff7NativeRuntimeIdentity'
        $installer | Should Match 'Install-Ff7DualRuntimePackage'
        $installer | Should Match 'Install-Ff7NativeReloadedProfile'
        $installer | Should Match 'Assert-Ff7NativeParityReleaseGate'
        $installer | Should Match 'AllowResearchNativeProfile'
        $installer | Should Not Match 'Start-Process'
        $installer | Should Not Match "Join-Path \`$scriptRoot '\.\.\\\.\.'"
        $installer | Should Not Match 'C:\\Program Files \(x86\)\\Steam\\steamapps\\common\\FINAL FANTASY VII'
    }

    It 'preflights every owned target before mutation and rolls back later failures' {
        $installer = [IO.File]::ReadAllText($installerPath)

        $buildIndex = $installer.IndexOf('& $buildPackagePath -OutputPath $stagedPackage')
        $packagePreflightIndex = $installer.IndexOf(
            'Install-Ff7DualRuntimePackage -PackagePath $stagedPackage `')
        $validateOnlyIndex = $installer.IndexOf(
            '-ModDirectory $modDirectory -ValidateOnly', $packagePreflightIndex)
        $loaderPreflightIndex = $installer.IndexOf(
            'Assert-LoaderFileTarget -Source $asiLoaderX86Source -Target $asiLoaderX86Target')
        $compatibilityIndex = $installer.IndexOf(
            '$installation = Initialize-Ff7CompatibilityRuntime -Installation $installation')
        $packageMutationIndex = $installer.IndexOf(
            '$packageResult = Install-Ff7DualRuntimePackage -PackagePath $stagedPackage')
        $voiceMutationIndex = $installer.IndexOf(
            '$openingNarrationResult = Disable-Ff7OpeningMovieNativeVoiceLayer')

        $buildIndex | Should BeGreaterThan -1
        $packagePreflightIndex | Should BeGreaterThan $buildIndex
        $validateOnlyIndex | Should BeGreaterThan $packagePreflightIndex
        $loaderPreflightIndex | Should BeGreaterThan $validateOnlyIndex
        $compatibilityIndex | Should BeGreaterThan $loaderPreflightIndex
        $packageMutationIndex | Should BeGreaterThan $compatibilityIndex
        $voiceMutationIndex | Should BeGreaterThan $packageMutationIndex

        $installer | Should Match 'ordinary native profile already exists while the release gate is closed'
        $installer | Should Match 'Restore-DualRuntimePackageForRollback'
        $installer | Should Match 'Restore-NativeProfileForRollback'
        $installer | Should Match 'Remove-NewLoaderForRollback'
        $installer | Should Match 'openingVoiceWasPresent'
    }

    It 'restores only validated package profile and loader artifacts during rollback' {
        $fixture = New-TestDirectory
        $helperNames = @(
            'Assert-OwnedModDirectoryForRollback',
            'Restore-DualRuntimePackageForRollback',
            'Restore-NativeProfileForRollback',
            'Remove-NewLoaderForRollback'
        )
        try {
            foreach ($helperName in $helperNames) {
                Import-TestFunctionFromScript -Path $installerPath -Name $helperName
            }

            $mods = Join-Path $fixture 'Reloaded\Mods'
            $modTarget = Join-Path $mods 'ff7.accessibility.reloaded'
            $modBackup = Join-Path $fixture `
                'Reloaded\AccessibilityBackups\ff7.accessibility.reloaded.backup-test'
            Write-TestTextFile -Path (Join-Path $modTarget 'ModConfig.json') `
                -Content '{"ModId":"ff7.accessibility.reloaded"}'
            Write-TestTextFile -Path (Join-Path $modTarget 'new.txt') -Content 'new'
            Write-TestTextFile -Path (Join-Path $modBackup 'ModConfig.json') `
                -Content '{"ModId":"ff7.accessibility.reloaded"}'
            Write-TestTextFile -Path (Join-Path $modBackup 'prior.txt') -Content 'prior'
            Restore-DualRuntimePackageForRollback -Result ([pscustomobject]@{
                Changed = $true
                ModDirectory = $modTarget
                BackupPath = $modBackup
            }) -ExpectedModDirectory $modTarget
            Test-Path -LiteralPath (Join-Path $modTarget 'prior.txt') -PathType Leaf | Should Be $true
            Test-Path -LiteralPath (Join-Path $modTarget 'new.txt') | Should Be $false
            Test-Path -LiteralPath $modBackup | Should Be $false

            $profilePath = Join-Path $fixture 'Reloaded\Apps\Ff7.Native.Steam2026.Research\AppConfig.json'
            $profileBackup = $profilePath + '.backup-test'
            Write-TestTextFile -Path $profilePath -Content '{"new":true}'
            Write-TestTextFile -Path $profileBackup -Content '{"prior":true}'
            Restore-NativeProfileForRollback -Result ([pscustomobject]@{
                Changed = $true
                ProfilePath = $profilePath
                BackupPath = $profileBackup
            }) -ExpectedProfilePath $profilePath
            [IO.File]::ReadAllText($profilePath) | Should Match 'prior'
            Test-Path -LiteralPath $profileBackup | Should Be $false

            $loaderSource = Join-Path $fixture 'source-loader.dll'
            $loaderTarget = Join-Path $fixture 'runtime\dsound.dll'
            Write-TestTextFile -Path $loaderSource -Content 'managed-loader'
            New-Item -ItemType Directory -Path (Split-Path -Parent $loaderTarget) -Force | Out-Null
            Copy-Item -LiteralPath $loaderSource -Destination $loaderTarget
            Remove-NewLoaderForRollback -Result ([pscustomobject]@{
                Changed = $true
                Target = $loaderTarget
            }) -Source $loaderSource
            Test-Path -LiteralPath $loaderTarget | Should Be $false

            Write-TestTextFile -Path $loaderTarget -Content 'user-replaced-loader'
            { Remove-NewLoaderForRollback -Result ([pscustomobject]@{
                    Changed = $true
                    Target = $loaderTarget
                }) -Source $loaderSource } | Should Throw 'contents changed after installation'
            [IO.File]::ReadAllText($loaderTarget) | Should Be 'user-replaced-loader'
        }
        finally {
            foreach ($helperName in $helperNames) {
                Remove-Item -Path ("Function:global:{0}" -f $helperName) `
                    -Force -ErrorAction SilentlyContinue
            }
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'requires an explicit legacy native or Seventh Heaven launch selection' {
        $launcher = [IO.File]::ReadAllText($launcherPath)

        $launcher | Should Match 'Resolve-Ff7Installation'
        $launcher | Should Match "ValidateSet\('Legacy',\s*'Native',\s*'SeventhHeaven'\)"
        $launcher | Should Match 'Assert-Ff7NativeRuntimeIdentity'
        $launcher | Should Match 'Assert-Ff7NativeParityReleaseGate'
        $launcher | Should Match 'Assert-Ff7NativeReloadedProfile'
        $launcher | Should Match 'AllowResearchNative'
        $launcher | Should Match 'Reloaded-II\.exe'
        $launcher | Should Match "'--launch'"
        $launcher | Should Match '7th Heaven\.exe'
        $launcher | Should Not Match 'FFVII?_Launcher\.exe'
        $launcher | Should Not Match 'C:\\Program Files \(x86\)\\Steam\\steamapps\\common\\FINAL FANTASY VII'
    }

    It 'keeps both command wrappers relative and forwards arguments' {
        $installerCmd = [IO.File]::ReadAllText($installerCmdPath)
        $launcherCmd = [IO.File]::ReadAllText($launcherCmdPath)

        $installerCmd | Should Match '%~dp0Install-FF7ReloadedMod\.ps1'
        $installerCmd | Should Match '%\*'
        $launcherCmd | Should Match '%~dp0Launch-FF7Reloaded\.ps1'
        $launcherCmd | Should Match '%\*'
        $launcherCmd | Should Not Match 'C:\\Program Files \(x86\)'
    }
}

Describe 'Dual-runtime Reloaded package' {
    $buildPath = Join-Path $scriptRoot 'Build-DualRuntimePackage.ps1'

    It 'refuses unrelated or reparse-point output directories without changing their bytes' {
        $fixture = New-TestDirectory
        try {
            $wrongLeaf = Join-Path $fixture 'unrelated-user-directory'
            Write-TestTextFile -Path (Join-Path $wrongLeaf 'sentinel.txt') -Content 'preserve-wrong-leaf'
            $wrongLeafFingerprint = Get-TestDirectoryFingerprint -Root $wrongLeaf
            { & $buildPath -OutputPath $wrongLeaf } | Should Throw
            (Get-TestDirectoryFingerprint -Root $wrongLeaf) | Should Be $wrongLeafFingerprint

            $unownedOutput = Join-Path $fixture 'ff7.accessibility.reloaded'
            Write-TestTextFile -Path (Join-Path $unownedOutput 'sentinel.txt') -Content 'preserve-unowned'
            $unownedFingerprint = Get-TestDirectoryFingerprint -Root $unownedOutput
            { & $buildPath -OutputPath $unownedOutput } | Should Throw
            (Get-TestDirectoryFingerprint -Root $unownedOutput) | Should Be $unownedFingerprint

            Remove-Item -LiteralPath $unownedOutput -Recurse -Force
            $junctionTarget = Join-Path $fixture 'protected-junction-target'
            Write-TestTextFile -Path (Join-Path $junctionTarget 'sentinel.txt') -Content 'preserve-junction'
            $junctionFingerprint = Get-TestDirectoryFingerprint -Root $junctionTarget
            New-Item -ItemType Junction -Path $unownedOutput -Target $junctionTarget | Out-Null
            { & $buildPath -OutputPath $unownedOutput } | Should Throw
            (Get-TestDirectoryFingerprint -Root $junctionTarget) | Should Be $junctionFingerprint
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'distinguishes platform-targeted IL from ReadyToRun despite a matching COFF machine' {
        $fixture = New-TestDirectory
        try {
            $projectPath = Join-Path $fixture 'IlOnlyFixture.csproj'
            Write-TestTextFile -Path $projectPath -Content @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>IlOnlyFixture</AssemblyName>
    <PlatformTarget>x86</PlatformTarget>
    <Prefer32Bit>false</Prefer32Bit>
  </PropertyGroup>
</Project>
'@
            Write-TestTextFile -Path (Join-Path $fixture 'Fixture.cs') -Content @'
public static class Fixture
{
    public static int Value => 7;
}
'@

            & dotnet build $projectPath -c Release --nologo | Out-Host
            $LASTEXITCODE | Should Be 0

            $assemblyPath = Join-Path $fixture 'bin\Release\net8.0\IlOnlyFixture.dll'
            (Get-TestPeMachine -Path $assemblyPath) | Should Be 0x014C
            $managedNativeHeader = Get-TestPeManagedNativeHeaderDirectory -Path $assemblyPath
            $managedNativeHeader.VirtualAddress | Should Be 0
            $managedNativeHeader.Size | Should Be 0
            { Assert-TestReadyToRun -Path $assemblyPath } |
                Should Throw 'ManagedNativeHeaderDirectory is empty'
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'stages architecture-selected managed payloads and native dependencies' {
        $fixture = New-TestDirectory
        try {
            $output = Join-Path $fixture 'ff7.accessibility.reloaded'
            & $buildPath -OutputPath $output | Out-Null

            $configPath = Join-Path $output 'ModConfig.json'
            Test-Path -LiteralPath $configPath -PathType Leaf | Should Be $true
            $config = [IO.File]::ReadAllText($configPath) | ConvertFrom-Json
            [string]::IsNullOrWhiteSpace([string]$config.ModR2RManagedDll32) | Should Be $false
            [string]::IsNullOrWhiteSpace([string]$config.ModR2RManagedDll64) | Should Be $false
            @($config.SupportedAppId) -contains 'ff7_en.exe' | Should Be $true
            @($config.SupportedAppId) -contains 'FFVII.exe' | Should Be $true

            $x86Assembly = Join-Path $output $config.ModR2RManagedDll32
            $x64Assembly = Join-Path $output $config.ModR2RManagedDll64
            Test-Path -LiteralPath $x86Assembly -PathType Leaf | Should Be $true
            Test-Path -LiteralPath $x64Assembly -PathType Leaf | Should Be $true
            (Get-TestPeMachine -Path $x86Assembly) | Should Be 0x014C
            (Get-TestPeMachine -Path $x64Assembly) | Should Be 0x8664
            { Assert-TestReadyToRun -Path $x86Assembly } | Should Not Throw
            { Assert-TestReadyToRun -Path $x64Assembly } | Should Not Throw

            foreach ($architecture in @('x86', 'x64')) {
                $expectedMachine = if ($architecture -eq 'x86') { 0x014C } else { 0x8664 }
                $managedDependencies = @(
                    'Ff7.Accessibility.Core.dll',
                    'Ff7.Accessibility.LegacyLayout.dll',
                    'Ff7.Accessibility.Runtime.Abstractions.dll'
                )
                foreach ($managedDependency in $managedDependencies) {
                    $managedDependencyPath = Join-Path $output "$architecture\$managedDependency"
                    Test-Path -LiteralPath $managedDependencyPath -PathType Leaf | Should Be $true
                    (Get-TestPeMachine -Path $managedDependencyPath) | Should Be $expectedMachine
                    { Assert-TestReadyToRun -Path $managedDependencyPath } | Should Not Throw
                }
                Test-Path -LiteralPath (Join-Path $output "$architecture\prism.dll") -PathType Leaf | Should Be $true
                Test-Path -LiteralPath (Join-Path $output "$architecture\phonon.dll") -PathType Leaf | Should Be $true
            }

            (Get-TestPeMachine -Path (Join-Path $output 'x86\prism.dll')) | Should Be 0x014C
            (Get-TestPeMachine -Path (Join-Path $output 'x86\phonon.dll')) | Should Be 0x014C
            (Get-TestPeMachine -Path (Join-Path $output 'x64\prism.dll')) | Should Be 0x8664
            (Get-TestPeMachine -Path (Join-Path $output 'x64\phonon.dll')) | Should Be 0x8664

            [IO.File]::ReadAllText((Join-Path $output 'x86\Ff7.Accessibility.Reloaded.deps.json')) |
                Should Match 'Ff7.Accessibility.Core'
            [IO.File]::ReadAllText((Join-Path $output 'x86\Ff7.Accessibility.Reloaded.deps.json')) |
                Should Match 'Ff7.Accessibility.LegacyLayout'
            [IO.File]::ReadAllText((Join-Path $output 'x64\Ff7.Accessibility.Steam2026X64.deps.json')) |
                Should Match 'Ff7.Accessibility.Core'
            [IO.File]::ReadAllText((Join-Path $output 'x64\Ff7.Accessibility.Steam2026X64.deps.json')) |
                Should Match 'Ff7.Accessibility.LegacyLayout'
            Test-Path -LiteralPath (Join-Path $output 'Assets\movies\opening_audio_description.ogg') -PathType Leaf |
                Should Be $true
            Test-Path -LiteralPath (Join-Path $output 'Assets\world\field-id-to-world-map-coords.json') -PathType Leaf |
                Should Be $true
            Test-Path -LiteralPath (Join-Path $output 'Assets\world\wm-field-menu-names.txt') -PathType Leaf |
                Should Be $true
            Test-Path -LiteralPath (Join-Path $output 'Assets\footsteps\cosmo\config.toml') -PathType Leaf |
                Should Be $true
            foreach ($fieldCueAsset in @(
                'field_zone_transition.wav',
                'object_materia_190_pitch70.wav',
                'object_chest_253_pitch70.wav',
                'object_item_357_pitch70.wav',
                'ladder_061.wav',
                'floor60_statue_134.wav'
            )) {
                Test-Path -LiteralPath (Join-Path $output "Assets\navigation\$fieldCueAsset") -PathType Leaf |
                    Should Be $true
            }
            Test-Path -LiteralPath (Join-Path $output 'Configuration\config.json') -PathType Leaf |
                Should Be $true

            $wrongModConfigPath = Join-Path $fixture 'wrong-ModConfig.json'
            $wrongModConfig = [IO.File]::ReadAllText((Join-Path $output 'ModConfig.json')) | ConvertFrom-Json
            $wrongModConfig.ModId = 'unrelated.mod'
            Write-TestTextFile -Path $wrongModConfigPath -Content ($wrongModConfig | ConvertTo-Json -Depth 8)
            $badOutput = Join-Path $fixture 'bad-source\ff7.accessibility.reloaded'
            $knownGoodOutput = $output
            $copyPublisher = {
                param($Project, $RuntimeIdentifier, $Destination)
                $architecture = if ($RuntimeIdentifier -eq 'win-x86') { 'x86' } else { 'x64' }
                Get-ChildItem -LiteralPath (Join-Path $knownGoodOutput $architecture) -Force |
                    Copy-Item -Destination $Destination -Recurse -Force
                return 0
            }.GetNewClosure()
            { & $buildPath -OutputPath $badOutput -PublishInvoker $copyPublisher `
                -ModConfigSourceOverride $wrongModConfigPath } | Should Throw 'unexpected ModId'
            Test-Path -LiteralPath $badOutput | Should Be $false
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'removes obsolete files when rebuilding into an existing output directory' {
        $fixture = New-TestDirectory
        try {
            $output = Join-Path $fixture 'ff7.accessibility.reloaded'
            & $buildPath -OutputPath $output | Out-Null

            Write-TestTextFile -Path (Join-Path $output 'obsolete-root-file.txt') -Content 'stale'
            Write-TestTextFile -Path (Join-Path $output 'x86\obsolete-runtime-file.dll') -Content 'stale'

            & $buildPath -OutputPath $output | Out-Null

            Test-Path -LiteralPath (Join-Path $output 'obsolete-root-file.txt') | Should Be $false
            Test-Path -LiteralPath (Join-Path $output 'x86\obsolete-runtime-file.dll') | Should Be $false
            Test-Path -LiteralPath (Join-Path $output 'x86\Ff7.Accessibility.Reloaded.dll') -PathType Leaf |
                Should Be $true
            Test-Path -LiteralPath (Join-Path $output 'x64\Ff7.Accessibility.Steam2026X64.dll') -PathType Leaf |
                Should Be $true
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'preserves the prior coherent package when the second publish fails' {
        $fixture = New-TestDirectory
        try {
            $output = Join-Path $fixture 'ff7.accessibility.reloaded'
            & $buildPath -OutputPath $output | Out-Null
            $priorFingerprint = Get-TestDirectoryFingerprint -Root $output
            $priorX86 = Join-Path $output 'x86'

            $controlledPublisher = {
                param($Project, $RuntimeIdentifier, $Destination)

                if ($RuntimeIdentifier -eq 'win-x86') {
                    Get-ChildItem -LiteralPath $priorX86 -Force |
                        Copy-Item -Destination $Destination -Recurse -Force
                    return 0
                }

                return 73
            }.GetNewClosure()

            { & $buildPath -OutputPath $output -PublishInvoker $controlledPublisher } |
                Should Throw 'The native Steam 2026 x64 ReadyToRun publish failed with exit code 73.'

            (Get-TestDirectoryFingerprint -Root $output) | Should Be $priorFingerprint
            Test-Path -LiteralPath (Join-Path $output 'x86\Ff7.Accessibility.Reloaded.dll') -PathType Leaf |
                Should Be $true
            Test-Path -LiteralPath (Join-Path $output 'x64\Ff7.Accessibility.Steam2026X64.dll') -PathType Leaf |
                Should Be $true
            @(Get-ChildItem -LiteralPath $fixture -Directory -Filter '.ff7.accessibility.reloaded.staging-*').Count | Should Be 0
            @(Get-ChildItem -LiteralPath $fixture -Directory -Filter '.ff7.accessibility.reloaded.backup-*').Count | Should Be 0
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'rejects managed payloads whose matching COFF machine contains IL only' {
        $fixture = New-TestDirectory
        try {
            $output = Join-Path $fixture 'ff7.accessibility.reloaded'
            $ilOnlyPublisher = {
                param($Project, $RuntimeIdentifier, $Destination)

                & dotnet publish $Project `
                    -c Release `
                    -r $RuntimeIdentifier `
                    --self-contained false `
                    --nologo `
                    -p:PublishReadyToRun=false `
                    -p:PublishSingleFile=false `
                    -o $Destination | Out-Host
                return $LASTEXITCODE
            }

            { & $buildPath -OutputPath $output -PublishInvoker $ilOnlyPublisher } |
                Should Throw 'Package validation failed: x86 managed entry assembly has an empty CLR ManagedNativeHeaderDirectory.'
            Test-Path -LiteralPath $output | Should Be $false
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'rejects opposite-architecture ReadyToRun shared managed dependencies' {
        $fixture = New-TestDirectory
        try {
            $seedPackage = Join-Path $fixture 'seed\ff7.accessibility.reloaded'
            & $buildPath -OutputPath $seedPackage | Out-Null

            $contaminationCases = @(
                [pscustomobject]@{
                    Architecture = 'x86'
                    WrongArchitecture = 'x64'
                    RuntimeIdentifier = 'win-x86'
                    Dependency = 'Ff7.Accessibility.Core.dll'
                    ExpectedMachine = '0x014C'
                    ActualMachine = '0x8664'
                },
                [pscustomobject]@{
                    Architecture = 'x64'
                    WrongArchitecture = 'x86'
                    RuntimeIdentifier = 'win-x64'
                    Dependency = 'Ff7.Accessibility.Runtime.Abstractions.dll'
                    ExpectedMachine = '0x8664'
                    ActualMachine = '0x014C'
                }
            )

            foreach ($contaminationCase in $contaminationCases) {
                $caseOutput = Join-Path $fixture (Join-Path ("case-{0}" -f $contaminationCase.Architecture) 'ff7.accessibility.reloaded')
                $controlledPublisher = {
                    param($Project, $RuntimeIdentifier, $Destination)

                    $architecture = if ($RuntimeIdentifier -eq 'win-x86') { 'x86' } else { 'x64' }
                    Get-ChildItem -LiteralPath (Join-Path $seedPackage $architecture) -Force |
                        Copy-Item -Destination $Destination -Recurse -Force

                    if ($RuntimeIdentifier -eq $contaminationCase.RuntimeIdentifier) {
                        $wrongDependency = Join-Path $seedPackage `
                            (Join-Path $contaminationCase.WrongArchitecture $contaminationCase.Dependency)
                        Copy-Item -LiteralPath $wrongDependency `
                            -Destination (Join-Path $Destination $contaminationCase.Dependency) -Force
                    }
                    return 0
                }.GetNewClosure()

                $expectedMessage = 'Package validation failed: {0} managed dependency {1} has PE machine {2}; expected {3}.' -f `
                    $contaminationCase.Architecture,
                    $contaminationCase.Dependency,
                    $contaminationCase.ActualMachine,
                    $contaminationCase.ExpectedMachine
                { & $buildPath -OutputPath $caseOutput -PublishInvoker $controlledPublisher } |
                    Should Throw $expectedMessage
                Test-Path -LiteralPath $caseOutput | Should Be $false
            }
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'rejects forged PE signatures unbacked ReadyToRun directories and misleading dependency text' {
        $fixture = New-TestDirectory
        try {
            $seedPackage = Join-Path $fixture 'seed\ff7.accessibility.reloaded'
            & $buildPath -OutputPath $seedPackage | Out-Null
            $cases = @(
                [pscustomobject]@{
                    Name = 'signature'
                    Expected = 'has no PE signature'
                    Mutate = {
                        param($destination)
                        $path = Join-Path $destination 'prism.dll'
                        $bytes = [IO.File]::ReadAllBytes($path)
                        $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
                        $bytes[$peOffset] = 0
                        [IO.File]::WriteAllBytes($path, $bytes)
                    }
                },
                [pscustomobject]@{
                    Name = 'r2r-range'
                    Expected = 'unmappable ManagedNativeHeaderDirectory'
                    Mutate = {
                        param($destination)
                        Set-TestPeManagedNativeHeaderDirectory `
                            -Path (Join-Path $destination 'Ff7.Accessibility.Core.dll') `
                            -VirtualAddress ([Convert]::ToUInt32('FFF00000', 16)) -Size 128
                    }
                },
                [pscustomobject]@{
                    Name = 'deps-text'
                    Expected = 'dependency manifest omits Ff7.Accessibility.LegacyLayout'
                    Mutate = {
                        param($destination)
                        $depsPath = Join-Path $destination 'Ff7.Accessibility.Reloaded.deps.json'
                        $deps = [IO.File]::ReadAllText($depsPath) | ConvertFrom-Json
                        foreach ($target in $deps.targets.PSObject.Properties) {
                            $target.Value.PSObject.Properties.Remove('Ff7.Accessibility.LegacyLayout/1.0.0')
                        }
                        $deps.libraries.PSObject.Properties.Remove('Ff7.Accessibility.LegacyLayout/1.0.0')
                        Add-Member -InputObject $deps -NotePropertyName misleadingText `
                            -NotePropertyValue 'Ff7.Accessibility.LegacyLayout' -Force
                        Write-TestTextFile -Path $depsPath -Content ($deps | ConvertTo-Json -Depth 100)
                    }
                }
            )

            foreach ($case in $cases) {
                $caseOutput = Join-Path $fixture (Join-Path $case.Name 'ff7.accessibility.reloaded')
                $currentCase = $case
                $controlledPublisher = {
                    param($Project, $RuntimeIdentifier, $Destination)
                    $architecture = if ($RuntimeIdentifier -eq 'win-x86') { 'x86' } else { 'x64' }
                    Get-ChildItem -LiteralPath (Join-Path $seedPackage $architecture) -Force |
                        Copy-Item -Destination $Destination -Recurse -Force
                    if ($RuntimeIdentifier -eq 'win-x86') {
                        $mutator = $currentCase.Mutate
                        & $mutator $Destination
                    }
                    return 0
                }.GetNewClosure()

                { & $buildPath -OutputPath $caseOutput -PublishInvoker $controlledPublisher } |
                    Should Throw $case.Expected
                Test-Path -LiteralPath $caseOutput | Should Be $false
            }
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }

    It 'requests ReadyToRun compiler warnings from normal publishes' {
        [IO.File]::ReadAllText($buildPath) | Should Match '-p:PublishReadyToRunShowWarnings=true'
    }

    It 'rejects an x86 package missing the shared ReadyToRun legacy layout dependency' {
        $fixture = New-TestDirectory
        try {
            $seedPackage = Join-Path $fixture 'seed\ff7.accessibility.reloaded'
            $output = Join-Path $fixture 'incomplete\ff7.accessibility.reloaded'
            & $buildPath -OutputPath $seedPackage | Out-Null
            $controlledPublisher = {
                param($Project, $RuntimeIdentifier, $Destination)

                $architecture = if ($RuntimeIdentifier -eq 'win-x86') { 'x86' } else { 'x64' }
                Get-ChildItem -LiteralPath (Join-Path $seedPackage $architecture) -Force |
                    Copy-Item -Destination $Destination -Recurse -Force
                if ($architecture -eq 'x86') {
                    Remove-Item -LiteralPath (Join-Path $Destination 'Ff7.Accessibility.LegacyLayout.dll') -Force
                }
                return 0
            }.GetNewClosure()

            { & $buildPath -OutputPath $output -PublishInvoker $controlledPublisher } |
                Should Throw 'Package validation failed: missing x86 managed dependency Ff7.Accessibility.LegacyLayout.dll'
            Test-Path -LiteralPath $output | Should Be $false
        }
        finally {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }
}
