$ErrorActionPreference = 'Stop'

function Assert-ExactProperties {
    param([psobject] $Value, [string[]] $Expected, [string] $Label)
    $actual = @($Value.PSObject.Properties | ForEach-Object Name)
    if ($actual.Count -ne $Expected.Count) { throw "$Label has an unexpected property set." }
    foreach ($name in $Expected) {
        if (-not ($actual -ccontains $name)) { throw "$Label is missing property '$name'." }
    }
}

function Assert-OrdinaryDirectoryTree {
    param([Parameter(Mandatory=$true)] [string] $Root, [Parameter(Mandatory=$true)] [string] $Label)
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) { throw "$Label is missing: $Root" }
    $rootItem = Get-Item -LiteralPath $Root -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label cannot be a reparse point: $Root"
    }
    foreach ($item in @(Get-ChildItem -LiteralPath $Root -Recurse -Force)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Label cannot contain a reparse point: $($item.FullName)"
        }
    }
}

function Get-PeMachine {
    param([Parameter(Mandatory=$true)] [string] $Path)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $buffer = New-Object byte[] 4
        if ($stream.Read($buffer, 0, 2) -ne 2 -or $buffer[0] -ne 0x4D -or $buffer[1] -ne 0x5A) {
            throw "File is not a PE image: $Path"
        }
        $stream.Position = 0x3C
        if ($stream.Read($buffer, 0, 4) -ne 4) { throw "PE header is truncated: $Path" }
        $offset = [BitConverter]::ToInt32($buffer, 0)
        $stream.Position = $offset
        if ($stream.Read($buffer, 0, 4) -ne 4 -or [BitConverter]::ToUInt32($buffer, 0) -ne 0x00004550) {
            throw "PE signature is invalid: $Path"
        }
        if ($stream.Read($buffer, 0, 2) -ne 2) { throw "PE machine is truncated: $Path" }
        return [BitConverter]::ToUInt16($buffer, 0)
    }
    finally { $stream.Dispose() }
}

function Assert-PeMachine {
    param([string] $Path, [uint16] $Expected, [string] $Label)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Label is missing: $Path" }
    if ((Get-PeMachine -Path $Path) -ne $Expected) { throw "$Label has the wrong PE architecture: $Path" }
}

function Assert-DigestRecord {
    param([string] $Path, [long] $Size, [string] $Sha256, [string] $Sha512, [string] $Label)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Label is missing: $Path" }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $item.Length -ne $Size) {
        throw "$Label failed its locked size check."
    }
    if (-not (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.Equals($Sha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label failed its locked SHA-256 check."
    }
    if (-not [string]::IsNullOrWhiteSpace($Sha512) -and
        -not (Get-FileHash -LiteralPath $Path -Algorithm SHA512).Hash.Equals($Sha512, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label failed its locked SHA-512 check."
    }
}

function Assert-BlindSwordsmanPrerequisiteBundle {
    [CmdletBinding()]
    param([Parameter(Mandatory=$true)] [string] $Path)

    $root = [IO.Path]::GetFullPath($Path)
    Assert-OrdinaryDirectoryTree -Root $root -Label 'Prerequisite bundle'
    $manifestPath = Join-Path $root 'dependency-bundle.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Prerequisite bundle manifest is missing.' }
    try { $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json }
    catch { throw "Prerequisite bundle manifest is invalid JSON: $($_.Exception.Message)" }
    Assert-ExactProperties -Value $manifest -Expected @('schemaVersion','reloaded','sharedHooks','dotnetDesktopRuntime') -Label 'Prerequisite manifest'
    if ([int]$manifest.schemaVersion -ne 1) { throw 'Prerequisite manifest schemaVersion must be 1.' }
    Assert-ExactProperties -Value $manifest.reloaded -Expected @('version','sourceUrl','sourceSize','sourceSha256','sourceCodeUrl') -Label 'Reloaded manifest record'
    Assert-ExactProperties -Value $manifest.sharedHooks -Expected @('version','sourceUrl','sourceSize','sourceSha256','sourceCodeUrl') -Label 'Shared Hooks manifest record'
    Assert-ExactProperties -Value $manifest.dotnetDesktopRuntime -Expected @('version','sourceCodeUrl','installers') -Label '.NET manifest record'
    if ([string]$manifest.reloaded.version -cne '1.30.3' -or
        [string]$manifest.sharedHooks.version -cne '1.16.3' -or
        [string]$manifest.dotnetDesktopRuntime.version -cne '9.0.8') {
        throw 'Prerequisite bundle contains an unsupported component version.'
    }

    $reloaded = Join-Path $root 'reloaded'
    $hooks = Join-Path $root 'shared-hooks'
    $dotnet = Join-Path $root 'dotnet'
    $notices = Join-Path $root 'notices'
    foreach ($directory in @($reloaded,$hooks,$dotnet,$notices)) {
        Assert-OrdinaryDirectoryTree -Root $directory -Label 'Prerequisite component directory'
    }
    foreach ($required in @(
        'Reloaded-II.exe','Loader\X86\Reloaded.Mod.Loader.dll','Loader\X64\Reloaded.Mod.Loader.dll',
        'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll','Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll',
        '_asi_extract\ASILoader32.dll','_asi_extract\ASILoader64.dll'
    )) {
        if (-not (Test-Path -LiteralPath (Join-Path $reloaded $required) -PathType Leaf)) { throw "Reloaded prerequisite is missing $required." }
    }
    Assert-PeMachine -Path (Join-Path $reloaded '_asi_extract\ASILoader32.dll') -Expected 0x014C -Label 'x86 ASI loader'
    Assert-PeMachine -Path (Join-Path $reloaded '_asi_extract\ASILoader64.dll') -Expected 0x8664 -Label 'x64 ASI loader'
    Assert-PeMachine -Path (Join-Path $reloaded 'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Expected 0x014C -Label 'x86 Reloaded bootstrapper'
    Assert-PeMachine -Path (Join-Path $reloaded 'Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Expected 0x8664 -Label 'x64 Reloaded bootstrapper'

    $hooksConfigPath = Join-Path $hooks 'ModConfig.json'
    try { $hooksConfig = [IO.File]::ReadAllText($hooksConfigPath) | ConvertFrom-Json }
    catch { throw "Bundled Shared Hooks configuration is invalid: $($_.Exception.Message)" }
    if ([string]$hooksConfig.ModId -cne 'reloaded.sharedlib.hooks') { throw 'Bundled Shared Hooks has the wrong ModId.' }
    foreach ($required in @('x86\Reloaded.Hooks.ReloadedII.dll','x64\Reloaded.Hooks.ReloadedII.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $hooks $required) -PathType Leaf)) { throw "Shared Hooks prerequisite is missing $required." }
    }
    Assert-PeMachine -Path (Join-Path $hooks 'x86\Reloaded.Hooks.ReloadedII.dll') -Expected 0x014C -Label 'x86 Shared Hooks'
    Assert-PeMachine -Path (Join-Path $hooks 'x64\Reloaded.Hooks.ReloadedII.dll') -Expected 0x8664 -Label 'x64 Shared Hooks'

    $installers = @($manifest.dotnetDesktopRuntime.installers)
    if ($installers.Count -ne 2 -or (@($installers.architecture | Sort-Object) -join ',') -cne 'x64,x86') {
        throw 'Prerequisite manifest must contain exactly one x86 and one x64 .NET installer.'
    }
    foreach ($installer in $installers) {
        Assert-ExactProperties -Value $installer -Expected @('architecture','name','sourceUrl','sourceSize','sourceSha256','sourceSha512') -Label '.NET installer manifest record'
        if ([string]$installer.architecture -cnotmatch '^(x86|x64)$' -or
            [string]$installer.name -cne [IO.Path]::GetFileName([string]$installer.name)) {
            throw 'Prerequisite manifest has an invalid .NET installer identity.'
        }
        Assert-DigestRecord -Path (Join-Path $dotnet ([string]$installer.name)) -Size ([long]$installer.sourceSize) `
            -Sha256 ([string]$installer.sourceSha256) -Sha512 ([string]$installer.sourceSha512) `
            -Label ".NET $($installer.architecture) desktop runtime"
    }
    foreach ($name in @(
        'THIRD-PARTY-NOTICES.md','Reloaded-II-GPL-3.0.txt','Reloaded-Shared-Hooks-LGPL-3.0.txt',
        'dotnet-LICENSE.txt','dotnet-THIRD-PARTY-NOTICES.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $notices $name) -PathType Leaf)) { throw "Prerequisite notice is missing: $name" }
    }
    return [pscustomobject]@{ Root=$root; Manifest=$manifest; Reloaded=$reloaded; SharedHooks=$hooks; DotNet=$dotnet }
}

function Test-BlindSwordsmanDesktopRuntime {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)] [ValidateSet('x86','x64')] [string] $Architecture,
        [string] $MinimumVersion = '9.0.8'
    )
    $minimum = [Version]::Parse($MinimumVersion)
    $programFiles = if ($Architecture -ceq 'x86') {
        [Environment]::GetFolderPath('ProgramFilesX86')
    } else {
        [Environment]::GetFolderPath('ProgramFiles')
    }
    if ([string]::IsNullOrWhiteSpace($programFiles)) { return $false }
    $shared = Join-Path $programFiles 'dotnet\shared\Microsoft.WindowsDesktop.App'
    if (-not (Test-Path -LiteralPath $shared -PathType Container)) { return $false }
    foreach ($directory in @(Get-ChildItem -LiteralPath $shared -Directory -ErrorAction SilentlyContinue)) {
        $version = $null
        if ([Version]::TryParse($directory.Name, [ref]$version) -and
            $version.Major -eq $minimum.Major -and $version -ge $minimum) {
            return $true
        }
    }
    return $false
}

function Assert-NoUnsafeExistingPath {
    param([Parameter(Mandatory=$true)] [string] $Path, [switch] $LeafMustBeFile)
    $current = [IO.Path]::GetFullPath($Path)
    $first = $true
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Prerequisite destination cannot traverse a reparse point: $current"
            }
            if ($first -and $LeafMustBeFile -and $item.PSIsContainer) {
                throw "Prerequisite file destination is a directory: $current"
            }
            if (-not $first -and -not $item.PSIsContainer) {
                throw "Prerequisite destination parent is a file: $current"
            }
        }
        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -ceq $current) { break }
        $current = $parent
        $first = $false
    }
}

function Get-OverlayPlan {
    param(
        [Parameter(Mandatory=$true)] [string] $SourceRoot,
        [Parameter(Mandatory=$true)] [string] $TargetRoot,
        [string[]] $ExcludedTopDirectories = @(),
        [Parameter(Mandatory=$true)] [string] $Category
    )
    Assert-OrdinaryDirectoryTree -Root $SourceRoot -Label "$Category source"
    $sourcePrefix = [IO.Path]::GetFullPath($SourceRoot).TrimEnd('\') + '\'
    $targetPrefix = [IO.Path]::GetFullPath($TargetRoot).TrimEnd('\') + '\'
    foreach ($file in @(Get-ChildItem -LiteralPath $SourceRoot -Recurse -File -Force)) {
        $relative = $file.FullName.Substring($sourcePrefix.Length)
        $top = ($relative -split '[\\/]', 2)[0]
        if ($ExcludedTopDirectories -icontains $top) { continue }
        if ($relative -match '(^|[\\/])\.\.([\\/]|$)' -or $relative.Contains(':')) {
            throw "$Category source contains an unsafe relative path: $relative"
        }
        [pscustomobject]@{
            Source = $file.FullName
            Target = $targetPrefix + $relative
            Category = $Category
        }
    }
}

function New-ReloadedSettingsFile {
    param([string] $ReloadedRoot, [string] $ExistingPath, [string] $Destination)
    $values = [ordered]@{
        LoaderPath32=''; LoaderPath64=''; LauncherPath=''; Bootstrapper32Path=''; Bootstrapper64Path=''
        ApplicationConfigDirectory=''; ModUserConfigDirectory=''; MiscConfigDirectory=''; PluginConfigDirectory=''; ModConfigDirectory=''
        EnabledPlugins=@(); LanguageFile='en-GB.xaml'; ThemeFile='Default.xaml'; FirstLaunch=$false; ShowConsole=$false
        LogFileCompressTimeHours=6; LogFileDeleteHours=336; CrashDumpDeleteHours=24
        NuGetFeeds=@([ordered]@{ Name='Official Repository'; URL='https://packages.sewer56.moe/v3/index.json'; Description='Official Reloaded package repository.' })
        ForceModPrereleases=$false; ReloadedProcessListRefreshInterval=1000; LoaderSetupTimeout=30000
        LoaderSetupSleeptime=32; ProcessRefreshInterval=200; SkipWineLaunchWarning=$false; DisableDInput=$false
    }
    if (Test-Path -LiteralPath $ExistingPath -PathType Leaf) {
        try {
            $existing = [IO.File]::ReadAllText($ExistingPath) | ConvertFrom-Json
            foreach ($property in @($existing.PSObject.Properties)) { $values[$property.Name] = $property.Value }
        }
        catch {
            # A malformed settings file is backed up by the transaction and replaced with safe defaults.
        }
    }
    $fullRoot = [IO.Path]::GetFullPath($ReloadedRoot)
    $values['LoaderPath32'] = Join-Path $fullRoot 'Loader\X86\Reloaded.Mod.Loader.dll'
    $values['LoaderPath64'] = Join-Path $fullRoot 'Loader\X64\Reloaded.Mod.Loader.dll'
    $values['LauncherPath'] = Join-Path $fullRoot 'Reloaded-II.exe'
    $values['Bootstrapper32Path'] = Join-Path $fullRoot 'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll'
    $values['Bootstrapper64Path'] = Join-Path $fullRoot 'Loader\X64\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll'
    $values['ApplicationConfigDirectory'] = Join-Path $fullRoot 'Apps'
    $values['ModUserConfigDirectory'] = Join-Path $fullRoot 'User\Mods'
    $values['MiscConfigDirectory'] = Join-Path $fullRoot 'User\Misc'
    $values['PluginConfigDirectory'] = Join-Path $fullRoot 'Plugins'
    $values['ModConfigDirectory'] = Join-Path $fullRoot 'Mods'
    $values['FirstLaunch'] = $false
    [IO.File]::WriteAllText($Destination, (($values | ConvertTo-Json -Depth 10) + "`n"), (New-Object Text.UTF8Encoding($false)))
}

function Install-BlindSwordsmanReloadedPrerequisites {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)] [string] $BundlePath,
        [Parameter(Mandatory=$true)] [string] $ReloadedRoot,
        [Parameter(Mandatory=$true)] [ValidateSet('x86','x64')] [string[]] $RequiredArchitectures,
        [string] $SettingsPath,
        [Parameter(DontShow=$true)] [scriptblock] $RuntimeProbe,
        [Parameter(DontShow=$true)] [scriptblock] $RuntimeInstaller,
        [Parameter(DontShow=$true)] [scriptblock] $FileWriter
    )

    $bundle = Assert-BlindSwordsmanPrerequisiteBundle -Path $BundlePath
    $architectures = @('x86','x64') | Where-Object { $RequiredArchitectures -contains $_ }
    if ($architectures.Count -eq 0 -or $architectures.Count -ne @($RequiredArchitectures | Select-Object -Unique).Count) {
        throw 'At least one unique supported game architecture is required.'
    }
    $targetRoot = [IO.Path]::GetFullPath($ReloadedRoot)
    if ([string]::IsNullOrWhiteSpace($SettingsPath)) {
        $SettingsPath = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Reloaded-Mod-Loader-II\ReloadedII.json'
    }
    $resolvedSettings = [IO.Path]::GetFullPath($SettingsPath)
    Assert-NoUnsafeExistingPath -Path $targetRoot
    Assert-NoUnsafeExistingPath -Path $resolvedSettings -LeafMustBeFile

    $hooksTarget = Join-Path $targetRoot 'Mods\reloaded.sharedlib.hooks'
    if (Test-Path -LiteralPath $hooksTarget) {
        $hooksTargetItem = Get-Item -LiteralPath $hooksTarget -Force
        if (-not $hooksTargetItem.PSIsContainer -or
            ($hooksTargetItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Existing Shared Hooks target is not a safe directory.'
        }
        $existingHooksConfig = Join-Path $hooksTarget 'ModConfig.json'
        if (Test-Path -LiteralPath $existingHooksConfig -PathType Leaf) {
            try { $existingHooks = [IO.File]::ReadAllText($existingHooksConfig) | ConvertFrom-Json }
            catch { $existingHooks = $null }
            if ($null -ne $existingHooks -and [string]$existingHooks.ModId -cne 'reloaded.sharedlib.hooks') {
                throw "Refusing to replace a Shared Hooks target owned by ModId '$($existingHooks.ModId)'."
            }
        }
    }

    $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('blind-swordsman-prerequisite-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    try {
        $settingsSource = Join-Path $temporaryRoot 'ReloadedII.json'
        New-ReloadedSettingsFile -ReloadedRoot $targetRoot -ExistingPath $resolvedSettings -Destination $settingsSource
        $plan = New-Object 'System.Collections.Generic.List[object]'
        foreach ($entry in @(Get-OverlayPlan -SourceRoot $bundle.Reloaded -TargetRoot $targetRoot `
            -ExcludedTopDirectories @('Apps','Mods','User','Plugins','AccessibilityBackups') -Category 'Reloaded')) {
            $plan.Add($entry)
        }
        foreach ($entry in @(Get-OverlayPlan -SourceRoot $bundle.SharedHooks -TargetRoot $hooksTarget -Category 'Shared Hooks')) {
            $plan.Add($entry)
        }
        $plan.Add([pscustomobject]@{ Source=$settingsSource; Target=$resolvedSettings; Category='Reloaded settings' })
        $targets = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $plan) {
            if (-not $targets.Add([IO.Path]::GetFullPath([string]$entry.Target))) {
                throw "Prerequisite overlay has a duplicate destination: $($entry.Target)"
            }
            Assert-NoUnsafeExistingPath -Path ([string]$entry.Target) -LeafMustBeFile
        }

        $installedRuntimes = New-Object 'System.Collections.Generic.List[string]'
        foreach ($architecture in $architectures) {
            $present = if ($null -ne $RuntimeProbe) {
                [bool](& $RuntimeProbe $architecture ([string]$bundle.Manifest.dotnetDesktopRuntime.version))
            } else {
                Test-BlindSwordsmanDesktopRuntime -Architecture $architecture -MinimumVersion ([string]$bundle.Manifest.dotnetDesktopRuntime.version)
            }
            if ($present) { continue }
            $record = @($bundle.Manifest.dotnetDesktopRuntime.installers | Where-Object architecture -eq $architecture)[0]
            $installerPath = Join-Path $bundle.DotNet ([string]$record.name)
            $exitCode = if ($null -ne $RuntimeInstaller) {
                $installerResult = & $RuntimeInstaller $architecture $installerPath
                if ($installerResult -is [int]) { [int]$installerResult } else { [int]$installerResult.ExitCode }
            } else {
                $process = Start-Process -FilePath $installerPath -ArgumentList @('/install','/quiet','/norestart') -Wait -PassThru
                [int]$process.ExitCode
            }
            if (@(0,1641,3010) -notcontains $exitCode) { throw ".NET $architecture desktop runtime installer failed with exit code $exitCode." }
            $presentAfter = if ($null -ne $RuntimeProbe) {
                [bool](& $RuntimeProbe $architecture ([string]$bundle.Manifest.dotnetDesktopRuntime.version))
            } else {
                Test-BlindSwordsmanDesktopRuntime -Architecture $architecture -MinimumVersion ([string]$bundle.Manifest.dotnetDesktopRuntime.version)
            }
            if (-not $presentAfter) { throw ".NET $architecture desktop runtime was not detected after installation." }
            $installedRuntimes.Add($architecture)
        }

        $writePlan = New-Object 'System.Collections.Generic.List[object]'
        foreach ($entry in $plan) {
            if (Test-Path -LiteralPath $entry.Target -PathType Leaf) {
                $sourceHash = (Get-FileHash -LiteralPath $entry.Source -Algorithm SHA256).Hash
                $targetHash = (Get-FileHash -LiteralPath $entry.Target -Algorithm SHA256).Hash
                if ($sourceHash.Equals($targetHash, [StringComparison]::OrdinalIgnoreCase)) { continue }
            }
            $writePlan.Add($entry)
        }

        $rootExisted = Test-Path -LiteralPath $targetRoot -PathType Container
        $createdDirectories = New-Object 'System.Collections.Generic.List[string]'
        $applied = New-Object 'System.Collections.Generic.List[object]'
        $recoveryRoot = $null
        try {
            if (-not $rootExisted) {
                New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
                $createdDirectories.Add($targetRoot)
            }
            if ($writePlan.Count -gt 0) {
                $recoveryRoot = Join-Path $targetRoot ('AccessibilityBackups\prerequisites-' + [Guid]::NewGuid().ToString('N'))
                New-Item -ItemType Directory -Path (Join-Path $recoveryRoot 'files') -Force | Out-Null
            }
            for ($index = 0; $index -lt $writePlan.Count; $index++) {
                $entry = $writePlan[$index]
                $target = [IO.Path]::GetFullPath([string]$entry.Target)
                $parent = Split-Path -Parent $target
                if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
                    New-Item -ItemType Directory -Path $parent -Force | Out-Null
                    $createdDirectories.Add($parent)
                }
                Assert-NoUnsafeExistingPath -Path $target -LeafMustBeFile
                $hadOriginal = Test-Path -LiteralPath $target -PathType Leaf
                $backup = if ($hadOriginal) { Join-Path $recoveryRoot ('files\{0:D5}.bak' -f $index) } else { $null }
                if ($hadOriginal) { Copy-Item -LiteralPath $target -Destination $backup }
                $record = [pscustomobject]@{ Target=$target; HadOriginal=$hadOriginal; Backup=$backup; Source=[string]$entry.Source; Category=[string]$entry.Category }
                $applied.Add($record)
                $temporaryTarget = Join-Path $parent ('.blind-swordsman-prerequisite-' + [Guid]::NewGuid().ToString('N') + '.tmp')
                try {
                    if ($null -ne $FileWriter) { & $FileWriter ([string]$entry.Source) $temporaryTarget }
                    else { Copy-Item -LiteralPath ([string]$entry.Source) -Destination $temporaryTarget }
                    if (-not (Test-Path -LiteralPath $temporaryTarget -PathType Leaf)) { throw "Prerequisite writer produced no file for $target." }
                    $sourceHash = (Get-FileHash -LiteralPath ([string]$entry.Source) -Algorithm SHA256).Hash
                    $temporaryHash = (Get-FileHash -LiteralPath $temporaryTarget -Algorithm SHA256).Hash
                    if (-not $sourceHash.Equals($temporaryHash, [StringComparison]::OrdinalIgnoreCase)) { throw "Prerequisite write verification failed for $target." }
                    if ($hadOriginal) {
                        $replaceBackup = Join-Path $parent ('.blind-swordsman-replaced-' + [Guid]::NewGuid().ToString('N') + '.bak')
                        try { [IO.File]::Replace($temporaryTarget, $target, $replaceBackup, $true) }
                        finally {
                            if (Test-Path -LiteralPath $replaceBackup -PathType Leaf) { Remove-Item -LiteralPath $replaceBackup -Force }
                        }
                    }
                    else { Move-Item -LiteralPath $temporaryTarget -Destination $target }
                }
                finally {
                    if (Test-Path -LiteralPath $temporaryTarget -PathType Leaf) { Remove-Item -LiteralPath $temporaryTarget -Force }
                }
            }
            if ($writePlan.Count -gt 0) {
                $recoveryManifest = [ordered]@{
                    schemaVersion=1
                    createdUtc=[DateTime]::UtcNow.ToString('O')
                    files=@($applied | ForEach-Object {
                        [ordered]@{
                            target=$_.Target; category=$_.Category; hadOriginal=$_.HadOriginal; backup=$_.Backup
                            installedSha256=(Get-FileHash -LiteralPath $_.Target -Algorithm SHA256).Hash
                        }
                    })
                }
                [IO.File]::WriteAllText((Join-Path $recoveryRoot 'recovery-manifest.json'), ($recoveryManifest | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
            }
        }
        catch {
            $failure = $_
            for ($index = $applied.Count - 1; $index -ge 0; $index--) {
                $record = $applied[$index]
                if (Test-Path -LiteralPath $record.Target) {
                    $item = Get-Item -LiteralPath $record.Target -Force
                    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                        throw "Prerequisite rollback encountered an unsafe target after: $($failure.Exception.Message)"
                    }
                    Remove-Item -LiteralPath $record.Target -Force
                }
                if ($record.HadOriginal) {
                    if (-not (Test-Path -LiteralPath $record.Backup -PathType Leaf)) { throw "Prerequisite rollback backup is missing after: $($failure.Exception.Message)" }
                    New-Item -ItemType Directory -Path (Split-Path -Parent $record.Target) -Force | Out-Null
                    Move-Item -LiteralPath $record.Backup -Destination $record.Target
                }
            }
            if (-not [string]::IsNullOrWhiteSpace($recoveryRoot) -and (Test-Path -LiteralPath $recoveryRoot)) {
                Remove-Item -LiteralPath $recoveryRoot -Recurse -Force
            }
            foreach ($directory in @($createdDirectories | Sort-Object Length -Descending)) {
                if ((Test-Path -LiteralPath $directory -PathType Container) -and
                    @(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) {
                    Remove-Item -LiteralPath $directory -Force
                }
            }
            throw $failure
        }

        return [pscustomobject]@{
            ReloadedRoot=$targetRoot
            SettingsPath=$resolvedSettings
            SharedHooksPath=$hooksTarget
            InstalledDotNetArchitectures=$installedRuntimes.ToArray()
            RecoveryRoot=$recoveryRoot
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
    }
}

Export-ModuleMember -Function `
    Assert-BlindSwordsmanPrerequisiteBundle, `
    Test-BlindSwordsmanDesktopRuntime, `
    Install-BlindSwordsmanReloadedPrerequisites
