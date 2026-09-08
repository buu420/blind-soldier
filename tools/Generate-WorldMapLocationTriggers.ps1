[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EventDirectory,

    [string]$CoordinatePath = (Join-Path $PSScriptRoot '..\external\kujata\field-id-to-world-map-coords.json'),

    [string]$MenuNamePath = (Join-Path $PSScriptRoot '..\external\kujata\wm-field-menu-names.txt'),

    [string]$OutputPath = (Join-Path $PSScriptRoot '..\Ff7.Accessibility.Reloaded\Assets\world\world-map-location-triggers.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-UInt16 {
    param(
        [byte[]]$Bytes,
        [int]$Offset
    )

    if ($Offset -lt 0 -or $Offset -gt $Bytes.Length - 2) {
        throw "A 16-bit read at 0x$('{0:X}' -f $Offset) is outside the event file."
    }

    return [BitConverter]::ToUInt16($Bytes, $Offset)
}

function Get-InstructionSizeWords {
    param([int]$Opcode)

    if (($Opcode -gt 0x100 -and $Opcode -lt 0x200) -or $Opcode -in 0x200, 0x201) {
        return 2
    }

    return 1
}

function Read-WorldMapEventTriggers {
    param(
        [string]$Path,
        [int]$WorldMapType
    )

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 0x400) {
        throw "World-map event file is shorter than its 0x400-byte call table: $Path"
    }

    $entries = @()
    # The call table is 0x400 bytes: 256 pairs of uint16 function id/address.
    for ($index = 0; $index -lt 256; $index++) {
        $functionId = Read-UInt16 $bytes ($index * 4)
        $address = Read-UInt16 $bytes ($index * 4 + 2)
        if ($functionId -eq 0xFFFF -or $address -eq 0xFFFF) {
            continue
        }

        $entries += [pscustomobject][ordered]@{
            FunctionId = $functionId
            Offset = 0x400 + $address * 2
        }
    }

    $functionOffsets = @($entries.Offset | Sort-Object -Unique)
    $results = @()
    foreach ($entry in $entries) {
        # Native terrain-script handlers use 10xxxxxx xxxxxxxx function ids.
        if (($entry.FunctionId -band 0xC000) -ne 0x8000) {
            continue
        }

        $nextOffset = @($functionOffsets | Where-Object { $_ -gt $entry.Offset } | Select-Object -First 1)
        $functionEnd = if ($nextOffset.Count -eq 0) { $bytes.Length } else { [int]$nextOffset[0] }
        $history = [Collections.Generic.List[object]]::new()
        for ($offset = [int]$entry.Offset; $offset -le $functionEnd - 2;) {
            $opcode = Read-UInt16 $bytes $offset
            $sizeWords = Get-InstructionSizeWords $opcode
            $argument = if ($sizeWords -eq 2) { Read-UInt16 $bytes ($offset + 2) } else { $null }

            if ($opcode -eq 0x318) {
                if ($history.Count -lt 2 -or
                    $history[$history.Count - 2].Opcode -ne 0x110 -or
                    $history[$history.Count - 1].Opcode -ne 0x110) {
                    throw (
                        "Terrain handler 0x$('{0:X4}' -f $entry.FunctionId) in $Path calls EnterFieldScene " +
                        "without two literal PUSHI operands. Refusing to infer its destination."
                    )
                }

                $meshIndex = ($entry.FunctionId -band 0x3FF0) -shr 4
                $results += [pscustomobject][ordered]@{
                    WorldMapType = $WorldMapType
                    MeshX = $meshIndex % 36
                    MeshY = [int][Math]::Floor($meshIndex / 36)
                    TerrainScriptId = ($entry.FunctionId -band 0x000F) + 3
                    NativeDestinationLocationId = [int]$history[$history.Count - 2].Argument
                    EntryPointId = [int]$history[$history.Count - 1].Argument
                    FunctionId = ('0x{0:X4}' -f $entry.FunctionId)
                }
            }

            $history.Add([pscustomobject]@{ Opcode = $opcode; Argument = $argument })
            if ($history.Count -gt 2) {
                $history.RemoveAt(0)
            }
            $offset += $sizeWords * 2
        }
    }

    return $results
}

function Read-MenuNames {
    param([string]$Path)

    $names = @{}
    foreach ($line in [IO.File]::ReadLines($Path)) {
        if ($line -match '^\s*0x(?<id>[0-9A-Fa-f]+)\s+wm\d+\s+(?<name>.+?)\s*$') {
            $names[[Convert]::ToInt32($Matches.id, 16)] = $Matches.name.Trim().TrimEnd('*').Trim()
        }
    }

    return $names
}

function Get-LocationBaseName {
    param([string]$Name)

    return ($Name -replace '\s*\([^)]*Side\)\s*$', '').Trim()
}

function Get-NormalizedLocationName {
    param([string]$Name)

    # The extracted FFVII world-map menu-name table carries this typo. Keep
    # native labels authoritative while correcting the one known transcription
    # error before it reaches generated player-facing data.
    return $Name.Replace('Ancient Forset', 'Ancient Forest')
}

$coordinateFullPath = (Resolve-Path -LiteralPath $CoordinatePath).Path
$menuNameFullPath = (Resolve-Path -LiteralPath $MenuNamePath).Path
$eventFullPath = (Resolve-Path -LiteralPath $EventDirectory).Path
$coordinates = Get-Content -LiteralPath $coordinateFullPath -Raw | ConvertFrom-Json -AsHashtable
$names = Read-MenuNames $menuNameFullPath
foreach ($locationId in @($names.Keys)) {
    $names[$locationId] = Get-NormalizedLocationName $names[$locationId]
}

$eventFiles = @(
    [pscustomobject]@{ WorldMapType = 0; Name = 'wm0.ev' },
    [pscustomobject]@{ WorldMapType = 2; Name = 'wm2.ev' },
    [pscustomobject]@{ WorldMapType = 3; Name = 'wm3.ev' }
)
$rawTriggers = @()
$sourceFiles = [ordered]@{}
foreach ($eventFile in $eventFiles) {
    $path = Join-Path $eventFullPath $eventFile.Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing extracted world-map event file: $path"
    }

    $sourceFiles[$eventFile.Name] = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
    $rawTriggers += Read-WorldMapEventTriggers -Path $path -WorldMapType $eventFile.WorldMapType
}

$groupedTriggers = @($rawTriggers |
    Group-Object WorldMapType, MeshX, MeshY, TerrainScriptId |
    ForEach-Object {
        $sample = $_.Group[0]
        [pscustomobject][ordered]@{
            WorldMapType = $sample.WorldMapType
            MeshX = $sample.MeshX
            MeshY = $sample.MeshY
            TerrainScriptId = $sample.TerrainScriptId
            NativeDestinations = @($_.Group |
                Group-Object NativeDestinationLocationId |
                ForEach-Object {
                    [pscustomobject][ordered]@{
                        LocationId = [int]$_.Group[0].NativeDestinationLocationId
                        EntryPointIds = @($_.Group.EntryPointId | Sort-Object -Unique)
                    }
                } |
                Sort-Object LocationId)
            FunctionIds = @($_.Group.FunctionId | Sort-Object -Unique)
        }
    } |
    Sort-Object WorldMapType, MeshY, MeshX, TerrainScriptId)

$locations = @()
$unresolved = @()
foreach ($coordinateEntry in $coordinates.GetEnumerator() | Sort-Object { [int]$_.Key }) {
    $locationId = [int]$coordinateEntry.Key
    if (-not $names.ContainsKey($locationId)) {
        $unresolved += [pscustomobject][ordered]@{
            LocationId = $locationId
            Label = "Location $locationId"
            Reason = 'The native menu-name table has no label for this coordinate entry.'
        }
        continue
    }

    $matched = @($groupedTriggers | Where-Object {
        $_.NativeDestinations.LocationId -contains $locationId
    })

    if ($matched.Count -eq 0) {
        # Rocket Town exposes north and south as separate menu entries, while
        # the native terrain handler enters location 20 through entry points 0
        # and 1. Resolve this generically from the shared base name, exact mesh,
        # and multiple native entry points; never use geographic proximity.
        $baseName = Get-LocationBaseName $names[$locationId]
        $matched = @($groupedTriggers | Where-Object {
            $_.WorldMapType -eq 0 -and
            $_.MeshX -eq [int]$coordinateEntry.Value.meshX -and
            $_.MeshY -eq [int]$coordinateEntry.Value.meshY -and
            @($_.NativeDestinations.EntryPointIds | Select-Object -Unique).Count -gt 1 -and
            @($_.NativeDestinations | Where-Object {
                $names.ContainsKey([int]$_.LocationId) -and
                (Get-LocationBaseName $names[[int]$_.LocationId]) -eq $baseName
            }).Count -gt 0
        })
    }

    if ($matched.Count -eq 0) {
        $unresolved += [pscustomobject][ordered]@{
            LocationId = $locationId
            Label = $names[$locationId]
            Reason = 'No native terrain-script EnterFieldScene trigger resolves this catalog location.'
        }
        continue
    }

    if ($matched.Count -gt 1) {
        throw "Location $locationId ($($names[$locationId])) resolves to more than one native terrain trigger."
    }

    $trigger = $matched[0]
    $locations += [pscustomobject][ordered]@{
        LocationId = $locationId
        Label = $names[$locationId]
        WorldMapType = $trigger.WorldMapType
        MeshX = $trigger.MeshX
        MeshY = $trigger.MeshY
        TerrainScriptId = $trigger.TerrainScriptId
        NativeDestinations = $trigger.NativeDestinations
        FunctionIds = $trigger.FunctionIds
    }
}

$document = [pscustomobject][ordered]@{
    SchemaVersion = 1
    Source = 'FFVII world_us.lgp wm*.ev terrain-script call table and EnterFieldScene opcodes'
    SourceFilesSha256 = $sourceFiles
    Locations = @($locations | Sort-Object LocationId)
    UnresolvedLocations = @($unresolved | Sort-Object LocationId)
}

$outputFullPath = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $outputFullPath
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$json = $document | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText($outputFullPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Output (
    "Wrote $($locations.Count) native world-map location mappings and " +
    "$($unresolved.Count) unresolved entries to $outputFullPath"
)
