[CmdletBinding()]
param(
    [string] $SevenHeavenPatchPath = 'C:\Users\buu42\Tools\7thHeaven\Resources\FF7_1.02_Eng_Patch\ff7.exe',
    [string] $ConvertedPath = 'C:\Users\buu42\ff7_accessibility_analysis\input\ff7_en.exe',
    [string] $Steam2026Path = 'C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY VII Steam Edition\FFVII.exe',
    [string] $OutputPath,
    [string] $CppOutputPath,
    [string] $CSharpOutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$sourceRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $sourceRoot 'analysis\native-bootstrap\supported-hosts.json'
}
if ([string]::IsNullOrWhiteSpace($CppOutputPath)) {
    $CppOutputPath = Join-Path $sourceRoot 'native\BlindSoldier.Common\supported_hosts.generated.h'
}
if ([string]::IsNullOrWhiteSpace($CSharpOutputPath)) {
    $CSharpOutputPath = Join-Path $sourceRoot 'Ff7.Accessibility.Reloaded\Runtime\SupportedHosts.Generated.cs'
}

function Assert-Range {
    param([byte[]] $Bytes, [long] $Offset, [long] $Length, [string] $Label)
    if ($Offset -lt 0 -or $Length -lt 0 -or $Offset -gt $Bytes.LongLength - $Length) {
        throw "$Label is outside the PE image (offset=$Offset length=$Length file=$($Bytes.LongLength))."
    }
}

function Read-UInt16 {
    param([byte[]] $Bytes, [long] $Offset, [string] $Label)
    Assert-Range $Bytes $Offset 2 $Label
    return [BitConverter]::ToUInt16($Bytes, [int]$Offset)
}

function Read-UInt32 {
    param([byte[]] $Bytes, [long] $Offset, [string] $Label)
    Assert-Range $Bytes $Offset 4 $Label
    return [BitConverter]::ToUInt32($Bytes, [int]$Offset)
}

function Read-UInt64 {
    param([byte[]] $Bytes, [long] $Offset, [string] $Label)
    Assert-Range $Bytes $Offset 8 $Label
    return [BitConverter]::ToUInt64($Bytes, [int]$Offset)
}

function Get-PeEvidence {
    param([Parameter(Mandatory = $true)][string] $Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $bytes = [IO.File]::ReadAllBytes($fullPath)
    if ((Read-UInt16 $bytes 0 'DOS signature') -ne 0x5A4D) {
        throw "Not an MZ executable: $fullPath"
    }
    $peOffset = Read-UInt32 $bytes 0x3C 'PE offset'
    if ((Read-UInt32 $bytes $peOffset 'PE signature') -ne 0x00004550) {
        throw "Not a PE executable: $fullPath"
    }

    $machine = Read-UInt16 $bytes ($peOffset + 4) 'COFF machine'
    $sectionCount = Read-UInt16 $bytes ($peOffset + 6) 'COFF section count'
    $optionalSize = Read-UInt16 $bytes ($peOffset + 20) 'COFF optional-header size'
    $optionalOffset = $peOffset + 24
    $magic = Read-UInt16 $bytes $optionalOffset 'optional-header magic'
    $imageBase = if ($magic -eq 0x10B) {
        [uint64](Read-UInt32 $bytes ($optionalOffset + 28) 'PE32 image base')
    }
    elseif ($magic -eq 0x20B) {
        Read-UInt64 $bytes ($optionalOffset + 24) 'PE32+ image base'
    }
    else {
        throw ('Unsupported optional-header magic 0x{0:X4}: {1}' -f $magic, $fullPath)
    }

    $sections = New-Object 'System.Collections.Generic.List[object]'
    $sectionOffset = $optionalOffset + $optionalSize
    for ($index = 0; $index -lt $sectionCount; $index++) {
        $offset = $sectionOffset + (40 * $index)
        Assert-Range $bytes $offset 40 "section $index"
        $nameBytes = $bytes[$offset..($offset + 7)]
        $name = [Text.Encoding]::ASCII.GetString($nameBytes).Trim([char]0)
        $sections.Add([ordered]@{
            name = $name
            rva = [uint32](Read-UInt32 $bytes ($offset + 12) "section $name RVA")
            virtualSize = [uint32](Read-UInt32 $bytes ($offset + 8) "section $name virtual size")
            rawOffset = [uint32](Read-UInt32 $bytes ($offset + 20) "section $name raw offset")
            rawSize = [uint32](Read-UInt32 $bytes ($offset + 16) "section $name raw size")
            characteristics = [uint32](Read-UInt32 $bytes ($offset + 36) "section $name characteristics")
        })
    }

    [pscustomobject]@{
        Path = $fullPath
        Bytes = $bytes
        Machine = [uint16]$machine
        ImageBase = [uint64]$imageBase
        Sections = $sections.ToArray()
        Sha256 = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToUpperInvariant()
        Length = [long]$bytes.LongLength
    }
}

function Get-CodeSignature {
    param(
        [Parameter(Mandatory = $true)] $Evidence,
        [Parameter(Mandatory = $true)][uint64] $VirtualAddress,
        [Parameter(Mandatory = $true)][string] $Mask
    )

    $rva64 = $VirtualAddress - $Evidence.ImageBase
    if ($rva64 -gt [uint32]::MaxValue) {
        throw ('Signature address 0x{0:X} is below or too far from image base 0x{1:X}.' -f $VirtualAddress, $Evidence.ImageBase)
    }
    $rva = [uint32]$rva64
    $length = [int]($Mask.Length / 2)
    $section = $Evidence.Sections | Where-Object {
        $rva -ge $_.rva -and $rva -lt ([uint64]$_.rva + $_.rawSize)
    } | Select-Object -First 1
    if ($null -eq $section) {
        throw ('No file-backed section covers signature RVA 0x{0:X8}.' -f $rva)
    }
    $fileOffset = [uint64]$section.rawOffset + ($rva - $section.rva)
    Assert-Range $Evidence.Bytes $fileOffset $length ('signature RVA 0x{0:X8}' -f $rva)
    $signatureBytes = $Evidence.Bytes[$fileOffset..($fileOffset + $length - 1)]
    [ordered]@{
        rva = $rva
        bytes = -join ($signatureBytes | ForEach-Object { $_.ToString('X2') })
        mask = $Mask
    }
}

function New-StructuralProfile {
    param([string] $Id, $Evidence)
    $signatures = @(
        Get-CodeSignature $Evidence 0x0042D833 'FF00000000FF00000000FFFFFFFFFFFF'
        Get-CodeSignature $Evidence 0x0060BACF 'FFFFFFFFFF00000000FFFFFFFFFFFFFF'
        Get-CodeSignature $Evidence 0x0063C17F 'FFFFFFFFFFFFFFFFFF00000000FFFFFF'
    )
    [ordered]@{
        id = $Id
        sampleSha256 = $Evidence.Sha256
        sampleLength = $Evidence.Length
        imageBase = $Evidence.ImageBase
        sections = @($Evidence.Sections)
        signatures = $signatures
    }
}

function Convert-HexToCppBytes {
    param([string] $Hex)
    $items = for ($index = 0; $index -lt $Hex.Length; $index += 2) {
        '0x' + $Hex.Substring($index, 2)
    }
    return $items -join ', '
}

function Write-GeneratedCpp {
    param($Manifest, [string] $Path)
    $lines = New-Object 'System.Collections.Generic.List[string]'
    $lines.Add('#pragma once')
    $lines.Add('')
    $lines.Add('#include <cstddef>')
    $lines.Add('#include <cstdint>')
    $lines.Add('')
    $lines.Add('namespace blind_soldier::generated {')
    $lines.Add('struct SectionEvidence { const char* name; uint32_t rva; uint32_t virtualSize; uint32_t rawSize; uint32_t characteristics; };')
    $lines.Add('struct SignatureEvidence { uint32_t rva; const uint8_t* bytes; const uint8_t* mask; size_t length; };')
    $lines.Add('struct StructuralProfileEvidence { const char* id; uint64_t imageBase; const SectionEvidence* sections; size_t sectionCount; const SignatureEvidence* signatures; size_t signatureCount; };')
    $lines.Add(('inline constexpr wchar_t kLegacyStockSha256[] = L"{0}";' -f $Manifest.legacyStockX86.sha256))
    $lines.Add(('inline constexpr wchar_t kSteam2026Sha256[] = L"{0}";' -f $Manifest.steam2026X64.sha256))
    foreach ($profileIndex in 0..($Manifest.sevenHeavenX86.profiles.Count - 1)) {
        $profile = $Manifest.sevenHeavenX86.profiles[$profileIndex]
        $lines.Add(('inline constexpr SectionEvidence kProfile{0}Sections[] = {{' -f $profileIndex))
        foreach ($section in $profile.sections) {
            $lines.Add(('    {{"{0}", 0x{1:X8}, 0x{2:X8}, 0x{3:X8}, 0x{4:X8}}},' -f $section.name, [uint32]$section.rva, [uint32]$section.virtualSize, [uint32]$section.rawSize, [uint32]$section.characteristics))
        }
        $lines.Add('};')
        foreach ($signatureIndex in 0..($profile.signatures.Count - 1)) {
            $signature = $profile.signatures[$signatureIndex]
            $lines.Add(('inline constexpr uint8_t kProfile{0}Signature{1}Bytes[] = {{{2}}};' -f $profileIndex, $signatureIndex, (Convert-HexToCppBytes $signature.bytes)))
            $lines.Add(('inline constexpr uint8_t kProfile{0}Signature{1}Mask[] = {{{2}}};' -f $profileIndex, $signatureIndex, (Convert-HexToCppBytes $signature.mask)))
        }
        $lines.Add(('inline constexpr SignatureEvidence kProfile{0}Signatures[] = {{' -f $profileIndex))
        foreach ($signatureIndex in 0..($profile.signatures.Count - 1)) {
            $signature = $profile.signatures[$signatureIndex]
            $length = $signature.bytes.Length / 2
            $lines.Add(('    {{0x{0:X8}, kProfile{1}Signature{2}Bytes, kProfile{1}Signature{2}Mask, {3}}},' -f [uint32]$signature.rva, $profileIndex, $signatureIndex, $length))
        }
        $lines.Add('};')
    }
    $lines.Add('inline constexpr StructuralProfileEvidence kSevenHeavenProfiles[] = {')
    foreach ($profileIndex in 0..($Manifest.sevenHeavenX86.profiles.Count - 1)) {
        $profile = $Manifest.sevenHeavenX86.profiles[$profileIndex]
        $lines.Add(('    {{"{0}", 0x{1:X}, kProfile{2}Sections, {3}, kProfile{2}Signatures, {4}}},' -f $profile.id, [uint64]$profile.imageBase, $profileIndex, $profile.sections.Count, $profile.signatures.Count))
    }
    $lines.Add('};')
    $lines.Add('}  // namespace blind_soldier::generated')
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllLines([IO.Path]::GetFullPath($Path), $lines, [Text.UTF8Encoding]::new($false))
}

function Write-GeneratedCSharp {
    param($Manifest, [string] $Path)
    $json = $Manifest | ConvertTo-Json -Depth 12 -Compress
    $escaped = $json.Replace('"', '""')
    $content = @"
namespace Ff7.Accessibility.Reloaded.Runtime;

internal static class SupportedHostsGenerated
{
    internal const string LegacyStockSha256 = "$($Manifest.legacyStockX86.sha256)";
    internal const string Steam2026Sha256 = "$($Manifest.steam2026X64.sha256)";
    internal const string ManifestJson = @"$escaped";
}
"@
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($Path), $content, [Text.UTF8Encoding]::new($false))
}

$patch = Get-PeEvidence $SevenHeavenPatchPath
$converted = Get-PeEvidence $ConvertedPath
$steam = Get-PeEvidence $Steam2026Path
if ($patch.Machine -ne 0x014C -or $converted.Machine -ne 0x014C -or $steam.Machine -ne 0x8664) {
    throw 'Host evidence contains an unexpected PE machine.'
}
if ($patch.Sha256 -ne 'C1437392C5E4178765FBD238DCC9B33D86D2B97337310131C874F302236E4B6F' -or
    $converted.Sha256 -ne '68CF1B8C1D732CC00A1DDB02CED161F7C94B06680D9E8641A11C7361417375C2' -or
    $steam.Sha256 -ne '57A23D166D69E46B9E3339F779D4A3C4FEB402A989FA7291D0D9B4A1953ABB4B') {
    throw 'A local FFVII evidence binary does not match the pinned researched digest.'
}

$manifest = [ordered]@{
    schemaVersion = 1
    legacyStockX86 = [ordered]@{
        name = 'ff7_en.exe'
        machine = 332
        sha256 = '4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225'
    }
    sevenHeavenX86 = [ordered]@{
        names = @('ff7.exe', 'ff7_en.exe')
        machine = 332
        requiredImports = @('WINMM.DLL')
        forbidEmbeddedManifest = $true
        profiles = @(
            New-StructuralProfile '7th-heaven-1.02-patch' $patch
            New-StructuralProfile '7th-heaven-converted-output' $converted
        )
    }
    steam2026X64 = [ordered]@{
        name = 'FFVII.exe'
        machine = 34404
        sha256 = '57A23D166D69E46B9E3339F779D4A3C4FEB402A989FA7291D0D9B4A1953ABB4B'
    }
}

New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath),
    (($manifest | ConvertTo-Json -Depth 12) + "`n"), [Text.UTF8Encoding]::new($false))
Write-GeneratedCpp $manifest $CppOutputPath
Write-GeneratedCSharp $manifest $CSharpOutputPath

Write-Output ([IO.Path]::GetFullPath($OutputPath))
Write-Output ([IO.Path]::GetFullPath($CppOutputPath))
Write-Output ([IO.Path]::GetFullPath($CSharpOutputPath))
