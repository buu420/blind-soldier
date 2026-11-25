$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $scriptRoot 'launcher\Ff7.Launcher2013.Accessible\FF7_Launcher.csproj'
$builder = Join-Path $scriptRoot 'Build-Accessible2013LauncherBundle.ps1'

# Reads the PE and CLR headers directly. AssemblyName.ProcessorArchitecture
# reports None under PowerShell 7 (CoreCLR), and machine 0x14C alone also
# matches AnyCPU builds; only the CLR 32BITREQUIRED flag marks x86-only.
function Get-ManagedPeArchitecture {
    param([Parameter(Mandatory = $true)] [string] $Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    $pe = [BitConverter]::ToInt32($bytes, 0x3C)
    if ([BitConverter]::ToUInt32($bytes, $pe) -ne 0x00004550) {
        throw "Not a PE image: $Path"
    }
    $sectionCount = [BitConverter]::ToUInt16($bytes, $pe + 6)
    $optionalSize = [BitConverter]::ToUInt16($bytes, $pe + 20)
    $optional = $pe + 24
    $magic = [BitConverter]::ToUInt16($bytes, $optional)
    $directories = $optional + $(if ($magic -eq 0x20B) { 112 } else { 96 })
    $clrRva = [BitConverter]::ToUInt32($bytes, $directories + 14 * 8)
    $clrFlags = $null
    for ($index = 0; $index -lt $sectionCount -and $clrRva -ne 0; $index++) {
        $section = $optional + $optionalSize + 40 * $index
        $virtualAddress = [BitConverter]::ToUInt32($bytes, $section + 12)
        $virtualSize = [Math]::Max([BitConverter]::ToUInt32($bytes, $section + 8), [BitConverter]::ToUInt32($bytes, $section + 16))
        if ($clrRva -ge $virtualAddress -and $clrRva -lt $virtualAddress + $virtualSize) {
            $clrOffset = [BitConverter]::ToUInt32($bytes, $section + 20) + ($clrRva - $virtualAddress)
            $clrFlags = [BitConverter]::ToUInt32($bytes, $clrOffset + 16)
        }
    }
    [pscustomobject]@{
        Machine = [BitConverter]::ToUInt16($bytes, $pe + 4)
        Magic = $magic
        ClrFlags = $clrFlags
        X86Only = $null -ne $clrFlags -and ($clrFlags -band 0x2) -ne 0 -and ($clrFlags -band 0x20000) -eq 0
    }
}

Describe 'Accessible FINAL FANTASY VII (2013) launcher bundle' {
    It 'tells an AnyCPU assembly from an x86-only one by its headers' {
        $scratch = Join-Path ([IO.Path]::GetTempPath()) ('blind-soldier-2013-anycpu-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $scratch | Out-Null
        try {
            $anyCpu = Join-Path $scratch 'AnyCpuProbe.dll'
            Add-Type -TypeDefinition 'public static class BlindSoldierAnyCpuProbe { }' -OutputAssembly $anyCpu -OutputType Library
            $architecture = Get-ManagedPeArchitecture -Path $anyCpu
            $architecture.Machine | Should Be 0x014C
            $architecture.ClrFlags | Should Not BeNullOrEmpty
            $architecture.X86Only | Should Be $false
        }
        finally {
            Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'builds an x86 FF7_Launcher.exe bundle with an exact manifest' {
        $projectText = [IO.File]::ReadAllText($project)
        $projectText | Should Not Match '(?i)<HintPath>[A-Z]:\\'
        $projectText | Should Match '<PlatformTarget>x86</PlatformTarget>'
        $projectText | Should Match '<AssemblyName>FF7_Launcher</AssemblyName>'

        $scratch = Join-Path ([IO.Path]::GetTempPath()) ('blind-soldier-2013-launcher-bundle-' + [Guid]::NewGuid().ToString('N'))
        $output = Join-Path $scratch 'bundle'
        try {
            & $builder -OutputPath $output -Configuration Release -ArtifactsPath (Join-Path $scratch 'artifacts') | Out-Null

            $expected = @('FF7_Launcher.exe', 'FF7_Launcher.exe.config', 'launcher-2013-bundle.json')
            $actual = @(Get-ChildItem -LiteralPath $output -File -Recurse |
                ForEach-Object { $_.FullName.Substring($output.Length + 1) } | Sort-Object)
            ($actual -join '|') | Should Be (($expected | Sort-Object) -join '|')

            $manifest = [IO.File]::ReadAllText((Join-Path $output 'launcher-2013-bundle.json')) | ConvertFrom-Json
            $manifest.schemaVersion | Should Be 1
            $manifest.assemblyName | Should Be 'FF7_Launcher'
            $manifest.assemblyVersion | Should Match '^\d+\.\d+\.\d+\.\d+$'
            foreach ($record in @(
                    @{ Descriptor = $manifest.launcher; Name = 'FF7_Launcher.exe' },
                    @{ Descriptor = $manifest.config; Name = 'FF7_Launcher.exe.config' })) {
                $file = Get-Item -LiteralPath (Join-Path $output $record.Name)
                $record.Descriptor.path | Should Be $record.Name
                $record.Descriptor.length | Should Be $file.Length
                $record.Descriptor.sha256 | Should Be (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            }

            $launcher = Join-Path $output 'FF7_Launcher.exe'
            $identity = [Reflection.AssemblyName]::GetAssemblyName($launcher)
            $identity.Name | Should Be 'FF7_Launcher'
            $identity.Version.ToString() | Should Be $manifest.assemblyVersion
            # The same PE machine the builder enforces, plus the x86-only CLR flag:
            # the launcher must run as 32-bit to load Blind Soldier into the game.
            $architecture = Get-ManagedPeArchitecture -Path $launcher
            $architecture.Machine | Should Be 0x014C
            $architecture.Magic | Should Be 0x10B
            $architecture.X86Only | Should Be $true

            $configText = [IO.File]::ReadAllText((Join-Path $output 'FF7_Launcher.exe.config'))
            $configText | Should Match 'Switch\.UseLegacyAccessibilityFeatures\.3=false'
            $configText | Should Match 'sku="\.NETFramework,Version=v4\.8"'
        }
        finally {
            if (Test-Path -LiteralPath $scratch) {
                Remove-Item -LiteralPath $scratch -Recurse -Force
            }
        }
    }
}
