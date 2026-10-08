param(
    [string] $GameRoot = '',
    [string] $KujataDataRoot = '',
    [string] $MapListPath = '',
    [string] $OutputPath = ''
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $GameRoot) {
    $GameRoot = (Resolve-Path (Join-Path $scriptRoot '..\..\..')).Path
}
if (-not $KujataDataRoot) {
    $KujataDataRoot = $env:KUJATA_DATA_ROOT
}
if (-not $KujataDataRoot) {
    throw 'Provide -KujataDataRoot or set KUJATA_DATA_ROOT.'
}
if (-not $OutputPath) {
    $OutputPath = Join-Path $scriptRoot '..\Ff7.Accessibility.Reloaded\Assets\navigation\field_objects.json'
}

$fieldJsonRoot = Join-Path $KujataDataRoot 'data\field\flevel.lgp'
$mapListJsonPath = Join-Path $fieldJsonRoot 'maplist.json'
if (-not $MapListPath) {
    $MapListPath = Join-Path $GameRoot 'data\field\flevel\maplist'
}
if (-not (Test-Path -LiteralPath $fieldJsonRoot)) {
    throw "Missing Kujata field data: $fieldJsonRoot"
}
$fieldIds = @{}
if (Test-Path -LiteralPath $MapListPath) {
    $mapBytes = [IO.File]::ReadAllBytes($MapListPath)
    $fieldCount = [BitConverter]::ToUInt16($mapBytes, 0)
    for ($fieldId = 0; $fieldId -lt $fieldCount; $fieldId++) {
        $offset = 2 + $fieldId * 32
        $fieldName = [Text.Encoding]::ASCII.GetString($mapBytes, $offset, 32).Split([char]0)[0].Trim()
        if ($fieldName) {
            $fieldIds[$fieldName] = $fieldId
        }
    }
}
elseif (Test-Path -LiteralPath $mapListJsonPath) {
    $mapNames = Get-Content -Raw -LiteralPath $mapListJsonPath | ConvertFrom-Json
    for ($fieldId = 0; $fieldId -lt $mapNames.Count; $fieldId++) {
        $fieldName = [string]$mapNames[$fieldId]
        if ($fieldName) {
            $fieldIds[$fieldName] = $fieldId
        }
    }
}
else {
    throw "Missing FFVII map lists: $MapListPath and $mapListJsonPath"
}

$definitions = [Collections.Generic.List[object]]::new()

# These pickups are attached to native LINE interaction regions because their
# lockers, cabinets, pots, consoles, and hidden chests are part of the field
# background instead of live field models. The whitelist deliberately excludes
# shops, minigame prizes, NPC gifts, and automatic story rewards.
$linePickupSpecs = @(
    [pscustomobject]@{ FieldName = 'blin64'; EntityId = 29; ScriptType = 'Go'; ExpectedPickups = @('STITM:7:1'); CueKind = 'Item'; CollectedBank = 3; CollectedAddress = 172; CollectedMask = 0x01; RequiredBank = -1; RequiredAddress = -1; RequiredMask = 0; RequiredValue = 0; MinimumGameMoment = -1; MaximumGameMoment = -1 },
    [pscustomobject]@{ FieldName = 'blin64'; EntityId = 32; ScriptType = 'Go'; ExpectedPickups = @('STITM:3:1'); CueKind = 'Item'; CollectedBank = 3; CollectedAddress = 172; CollectedMask = 0x02; RequiredBank = -1; RequiredAddress = -1; RequiredMask = 0; RequiredValue = 0; MinimumGameMoment = -1; MaximumGameMoment = -1 },
    [pscustomobject]@{ FieldName = 'blin64'; EntityId = 35; ScriptType = 'Go'; ExpectedPickups = @('STITM:241:1'); CueKind = 'Item'; CollectedBank = 3; CollectedAddress = 172; CollectedMask = 0x04; RequiredBank = -1; RequiredAddress = -1; RequiredMask = 0; RequiredValue = 0; MinimumGameMoment = 1008; MaximumGameMoment = -1 },
    [pscustomobject]@{ FieldName = 'blin64'; EntityId = 39; ScriptType = '[OK]'; ExpectedPickups = @('STITM:74:1', 'STITM:75:1'); CueKind = 'Item'; CollectedBank = 3; CollectedAddress = 179; CollectedMask = 0x02; RequiredBank = 3; RequiredAddress = 179; RequiredMask = 0x01; RequiredValue = 0x01; MinimumGameMoment = 1008; MaximumGameMoment = -1 },
    [pscustomobject]@{ FieldName = 'elmin2_2'; EntityId = 4; ScriptType = '[OK]'; ExpectedPickups = @('STITM:3:1'); CueKind = 'Item'; CollectedBank = 15; CollectedAddress = 81; CollectedMask = 0x01; RequiredBank = -1; RequiredAddress = -1; RequiredMask = 0; RequiredValue = 0; MinimumGameMoment = -1; MaximumGameMoment = -1 },
    [pscustomobject]@{ FieldName = 'elmin3_2'; EntityId = 3; ScriptType = '[OK]'; ExpectedPickups = @('STITM:72:1'); CueKind = 'Item'; CollectedBank = 15; CollectedAddress = 85; CollectedMask = 0x08; RequiredBank = -1; RequiredAddress = -1; RequiredMask = 0; RequiredValue = 0; MinimumGameMoment = -1; MaximumGameMoment = -1 },
    [pscustomobject]@{ FieldName = 'elmin4_1'; EntityId = 4; ScriptType = '[OK]'; ExpectedPickups = @('STITM:3:1'); CueKind = 'Item'; CollectedBank = 15; CollectedAddress = 85; CollectedMask = 0x10; RequiredBank = -1; RequiredAddress = -1; RequiredMask = 0; RequiredValue = 0; MinimumGameMoment = -1; MaximumGameMoment = -1 },
    [pscustomobject]@{ FieldName = 'elminn_2'; EntityId = 8; ScriptType = '[OK]'; ExpectedPickups = @('STITM:6:1'); CueKind = 'Item'; CollectedBank = 15; CollectedAddress = 85; CollectedMask = 0x80; RequiredBank = -1; RequiredAddress = -1; RequiredMask = 0; RequiredValue = 0; MinimumGameMoment = -1; MaximumGameMoment = -1 },
    [pscustomobject]@{ FieldName = 'ghotin_2'; EntityId = 10; ScriptType = '[OK]'; ExpectedPickups = @('STITM:5:1'); CueKind = 'Item'; CollectedBank = 1; CollectedAddress = 51; CollectedMask = 0x20; RequiredBank = -1; RequiredAddress = -1; RequiredMask = 0; RequiredValue = 0; MinimumGameMoment = -1; MaximumGameMoment = -1 },
    [pscustomobject]@{ FieldName = 'gnmk'; EntityId = 2; ScriptType = 'Go 1x'; ExpectedPickups = @('SMTRA:78:1'); CueKind = ''; CollectedBank = 15; CollectedAddress = 80; CollectedMask = 0x80; RequiredBank = -1; RequiredAddress = -1; RequiredMask = 0; RequiredValue = 0; MinimumGameMoment = -1; MaximumGameMoment = -1 },
    [pscustomobject]@{ FieldName = 'hideway1'; EntityId = 10; ScriptType = 'Go'; ExpectedPickups = @('STITM:225:1'); CueKind = 'Chest'; CollectedBank = 1; CollectedAddress = 58; CollectedMask = 0x40; RequiredBank = -1; RequiredAddress = -1; RequiredMask = 0; RequiredValue = 0; MinimumGameMoment = -1; MaximumGameMoment = -1 },
    [pscustomobject]@{ FieldName = 'hideway2'; EntityId = 10; ScriptType = 'Go'; ExpectedPickups = @('STITM:185:1'); CueKind = 'Chest'; CollectedBank = 1; CollectedAddress = 58; CollectedMask = 0x80; RequiredBank = -1; RequiredAddress = -1; RequiredMask = 0; RequiredValue = 0; MinimumGameMoment = -1; MaximumGameMoment = -1 },
    [pscustomobject]@{ FieldName = 'hideway3'; EntityId = 10; ScriptType = 'Go'; ExpectedPickups = @('SMTRA:28:1'); CueKind = ''; CollectedBank = 1; CollectedAddress = 58; CollectedMask = 0x20; RequiredBank = -1; RequiredAddress = -1; RequiredMask = 0; RequiredValue = 0; MinimumGameMoment = -1; MaximumGameMoment = -1 },
    [pscustomobject]@{ FieldName = 'mkt_ia'; EntityId = 5; ScriptType = 'Go'; ExpectedPickups = @('STITM:159:1'); CueKind = 'Item'; CollectedBank = 1; CollectedAddress = 37; CollectedMask = 0x20; RequiredBank = -1; RequiredAddress = -1; RequiredMask = 0; RequiredValue = 0; MinimumGameMoment = 999; MaximumGameMoment = -1 },
    [pscustomobject]@{ FieldName = 'ncoin1'; EntityId = 3; ScriptType = '[OK]'; ExpectedPickups = @('STITM:3:1'); CueKind = 'Item'; CollectedBank = 15; CollectedAddress = 1; CollectedMask = 0x01; RequiredBank = -1; RequiredAddress = -1; RequiredMask = 0; RequiredValue = 0; MinimumGameMoment = -1; MaximumGameMoment = -1; UsesPlayerCollisionRadius = $true }
)

# A few visible pickup models use scripted contact/jump sequences instead of a
# Talk handler. They still expose native model position and visibility at runtime.
$directModelPickupSpecs = @(
    [pscustomobject]@{ FieldName = 'las3_3'; EntityId = 5; ScriptType = 'Script 3'; ExpectedPickup = 'SMTRA:12:1'; CollectedBank = 1; CollectedAddress = 50; CollectedMask = 0x10 },
    [pscustomobject]@{ FieldName = 'mtcrl_5'; EntityId = 5; ScriptType = 'Script 3'; ExpectedPickup = 'STITM:298:1'; CollectedBank = 15; CollectedAddress = 115; CollectedMask = 0x04; ManualNavigationGuidance = 'To reach this item, hold Right and repeatedly press OK during the fall. From here, press OK, then hold Up to climb back. Auto walk is unavailable for this item.' },
    [pscustomobject]@{ FieldName = 'mtcrl_5'; EntityId = 6; ScriptType = 'Script 3'; ExpectedPickup = 'STITM:196:1'; CollectedBank = 15; CollectedAddress = 115; CollectedMask = 0x08; ManualNavigationGuidance = 'To reach this item, hold Left and repeatedly press OK during the fall. From here, press OK, then hold Up to climb back. Auto walk is unavailable for this item.' },
    [pscustomobject]@{ FieldName = 'junmin2'; EntityId = 16; ScriptType = 'Talk'; ExpectedPickup = 'STITM:95:1'; ExpectedModelResource = 'junmin2shinra_guard.char'; ExpectedCollectedWrite = 'BITON:15:118:4'; CollectedBank = 15; CollectedAddress = 118; CollectedMask = 0x10 },
    [pscustomobject]@{ FieldName = 'junmin5'; EntityId = 9; ScriptType = 'Talk'; ExpectedPickup = 'STITM:95:1'; ExpectedModelResource = 'junmin5shinra_guard.char'; ExpectedCollectedWrite = 'BITON:15:118:6'; CollectedBank = 15; CollectedAddress = 118; CollectedMask = 0x40 },
    # Lucrecia's cave: one visible weapon model whose Talk awards both rewards, then hides it.
    # Init shows it and enables Talk only once its native conditions hold (game moment 1197,
    # Vincent in the party, bank1[51] bit4 clear), and the runtime gates on that live state.
    [pscustomobject]@{ FieldName = 'zz4'; EntityId = 12; ScriptType = 'Talk'; ExpectedPickups = @('STITM:254:1', 'STITM:93:1'); Label = 'Death Penalty and Chaos'; ExpectedModelResource = 'zz4weapon_vinsen_w.char'; ExpectedCollectedWrite = 'BITON:1:51:4'; CollectedBank = 1; CollectedAddress = 51; CollectedMask = 0x10 }
)

function Add-Definition {
    param(
        [int] $FieldId,
        [string] $FieldName,
        [int] $EntityId,
        [string] $EntityName,
        [string] $ModelResource,
        [string] $Kind,
        [int] $NativeId = -1,
        [string] $Label = '',
        [int] $Quantity = 1,
        [int] $CollectedBank = -1,
        [int] $CollectedAddress = -1,
        [int] $CollectedMask = 0,
        [int] $RequiredBank = -1,
        [int] $RequiredAddress = -1,
        [int] $RequiredMask = 0,
        [int] $RequiredValue = 0,
        [string] $TargetKind = 'Model',
        [int] $StaticX = 0,
        [int] $StaticY = 0,
        [int] $StaticZ = 0,
        [string] $CueKindOverride = '',
        [int] $MinimumGameMoment = -1,
        [int] $MaximumGameMoment = -1,
        [bool] $UsesTalkInteraction = $false,
        [string] $ManualNavigationGuidance = '',
        [bool] $UsesPlayerCollisionRadius = $false,
        [bool] $ActivatesOnOk = $false
    )

    # These Kujata object entity indices differ from the native PC archive.
    # Bind the named models to their actual script-table indices.
    # StoryCoverageAudit checks the names against each installed archive.
    $nativeObjectEntities = @{
        'las0_4:tre1' = 34; 'las0_4:tre2' = 35; 'las0_4:save' = 37
        'las0_6:box_1' = 41; 'las0_6:box_2' = 42; 'las0_6:save' = 44
        'las2_2:box_1' = 24; 'las2_2:box_2' = 25; 'las2_2:save' = 27
    }
    $nativeKey = "${FieldName}:${EntityName}"
    if ($TargetKind -eq 'Model' -and $nativeObjectEntities.ContainsKey($nativeKey)) {
        $EntityId = $nativeObjectEntities[$nativeKey]
    }

    $definitions.Add([ordered]@{
        fieldId = $FieldId
        entityId = $EntityId
        kind = $Kind
        nativeId = $NativeId
        label = $Label
        quantity = [Math]::Max(1, $Quantity)
        collectedBank = $CollectedBank
        collectedAddress = $CollectedAddress
        collectedMask = $CollectedMask
        requiredBank = $RequiredBank
        requiredAddress = $RequiredAddress
        requiredMask = $RequiredMask
        requiredValue = $RequiredValue
        sourceFieldName = $FieldName
        sourceEntityName = $EntityName
        sourceModelResource = $ModelResource
        targetKind = $TargetKind
        staticX = $StaticX
        staticY = $StaticY
        staticZ = $StaticZ
        cueKindOverride = if ($CueKindOverride) { $CueKindOverride } else { $null }
        minimumGameMoment = $MinimumGameMoment
        maximumGameMoment = $MaximumGameMoment
    })
    if ($UsesTalkInteraction) {
        $definitions[$definitions.Count - 1]['usesTalkInteraction'] = $true
    }
    if ($ManualNavigationGuidance) {
        $definitions[$definitions.Count - 1]['manualNavigationGuidance'] = $ManualNavigationGuidance
    }
    if ($UsesPlayerCollisionRadius) {
        $definitions[$definitions.Count - 1]['usesPlayerCollisionRadius'] = $true
    }
    if ($ActivatesOnOk) {
        $definitions[$definitions.Count - 1]['activatesOnOk'] = $true
    }
}

function Get-ReachableScripts {
    param(
        [object] $Field,
        [object] $RootEntity,
        [object] $RootScript
    )

    $queue = [Collections.Generic.Queue[object]]::new()
    $queue.Enqueue([pscustomobject]@{ Entity = $RootEntity; Script = $RootScript })
    $visited = [Collections.Generic.HashSet[string]]::new()
    $results = [Collections.Generic.List[object]]::new()

    while ($queue.Count -gt 0) {
        $entry = $queue.Dequeue()
        $key = "$([int]$entry.Entity.entityId):$([int]$entry.Script.index)"
        if (-not $visited.Add($key)) {
            continue
        }

        $results.Add($entry.Script)
        foreach ($request in @($entry.Script.ops | Where-Object { $_.op -in @('REQ', 'REQSW', 'REQEW') })) {
            $targetEntity = $Field.script.entities | Where-Object { $_.entityId -eq $request.e } | Select-Object -First 1
            $targetScript = $targetEntity.scripts | Where-Object { $_.index -eq $request.f } | Select-Object -First 1
            if ($null -ne $targetEntity -and $null -ne $targetScript) {
                $queue.Enqueue([pscustomobject]@{ Entity = $targetEntity; Script = $targetScript })
            }
        }

        foreach ($request in @($entry.Script.ops | Where-Object { $_.op -in @('PREQ', 'PRQSW', 'PRQEW') })) {
            foreach ($targetEntity in @($Field.script.entities | Where-Object { $_.entityType -eq 'Playable Character' })) {
                $targetScript = $targetEntity.scripts | Where-Object { $_.index -eq $request.f } | Select-Object -First 1
                if ($null -ne $targetScript) {
                    $queue.Enqueue([pscustomobject]@{ Entity = $targetEntity; Script = $targetScript })
                }
            }
        }
    }

    return $results.ToArray()
}

function Get-ReceivedLabel {
    param([object[]] $Operations)

    $labels = @()
    foreach ($operation in $Operations) {
        if ($operation.op -ne 'MESSAGE' -or -not $operation.js) {
            continue
        }

        $comment = [string]$operation.js
        $commentIndex = $comment.IndexOf('//')
        if ($commentIndex -lt 0) {
            continue
        }

        $comment = $comment.Substring($commentIndex + 2).Trim()
        if ($comment -notmatch '(?i)Received') {
            continue
        }

        $quoted = [regex]::Match($comment, '["“]([^"”]+)["”]')
        if ($quoted.Success) {
            $label = $quoted.Groups[1].Value.Trim()
            if ($comment -match '(?i)Materia') {
                $label = "$label Materia"
            }
            $labels += $label
        }
    }

    $unique = @($labels | Sort-Object -Unique)
    if ($unique.Count -eq 1) {
        return $unique[0]
    }
    return ''
}

foreach ($file in Get-ChildItem -LiteralPath $fieldJsonRoot -Filter '*.json') {
    $fieldName = $file.BaseName
    if (-not $fieldIds.ContainsKey($fieldName)) {
        continue
    }

    $field = Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json
    foreach ($entity in $field.script.entities) {
        if ($entity.entityType -ne 'Model') {
            continue
        }

        $init = $entity.scripts | Where-Object { $_.scriptType -eq 'Init' } | Select-Object -First 1
        $char = $init.ops | Where-Object { $_.op -eq 'CHAR' } | Select-Object -First 1
        if ($null -eq $char) {
            continue
        }

        $modelId = [int]$char.n
        if ($modelId -lt 0 -or $modelId -ge $field.model.modelLoaders.Count) {
            continue
        }

        $modelResource = [string]$field.model.modelLoaders[$modelId].name
        if (($fieldName -eq 'bonevil' -and $entity.entityName -eq 'box1') -or
            ($fieldName -eq 'kuro_6' -and $entity.entityName -eq 'box') -or
            ($fieldName -eq 'las3_3' -and $entity.entityName -eq 'mat') -or
            ($fieldName -eq 'anfrst_1' -and $entity.entityName -eq 'box1')) {
            continue
        }

        $directSpec = $directModelPickupSpecs |
            Where-Object { $_.FieldName -eq $fieldName -and $_.EntityId -eq $entity.entityId } |
            Select-Object -First 1

        if ($modelResource -match 'fieldbg_saveicn') {
            Add-Definition $fieldIds[$fieldName] $fieldName $entity.entityId $entity.entityName $modelResource 'SavePoint' -1 'Save Point'
            continue
        }

        if ($modelResource -notmatch 'fieldbg_' -and $null -eq $directSpec) {
            continue
        }

        if ($null -ne $directSpec) {
            $pickupScript = $entity.scripts |
                Where-Object { $_.scriptType -eq $directSpec.ScriptType } |
                Select-Object -First 1
            $pickups = @($pickupScript.ops | Where-Object { $_.op -in @('STITM', 'SMTRA') })
            $expectedPickups = @(if ($directSpec.ExpectedPickups) { $directSpec.ExpectedPickups } else { $directSpec.ExpectedPickup })
            if ($null -eq $pickupScript -or $pickups.Count -ne $expectedPickups.Count) {
                throw "Missing direct model pickup script for ${fieldName}:$($entity.entityId)"
            }

            $pickup = $pickups[0]
            $quantity = if ($pickup.op -eq 'STITM') { [Math]::Max(1, [int]$pickup.a) } else { 1 }
            $actualPickups = @($pickups | ForEach-Object {
                $pickupQuantity = if ($_.op -eq 'STITM') { [Math]::Max(1, [int]$_.a) } else { 1 }
                "$($_.op):$($_.t):$pickupQuantity"
            })
            if (($actualPickups -join ',') -ne ($expectedPickups -join ',')) {
                throw "Native direct model pickup drift for ${fieldName}:$($entity.entityId): expected $($expectedPickups -join ','), found $($actualPickups -join ',')"
            }
            if ($directSpec.ExpectedModelResource -and
                $modelResource -ne $directSpec.ExpectedModelResource) {
                throw "Native direct model resource drift for ${fieldName}:$($entity.entityId): expected $($directSpec.ExpectedModelResource), found $modelResource"
            }
            if ($directSpec.ExpectedCollectedWrite) {
                $collectedWrites = @($pickupScript.ops |
                    Where-Object { $_.op -eq 'BITON' } |
                    ForEach-Object { "BITON:$($_.bd):$($_.d):$($_.bit)" })
                if ($directSpec.ExpectedCollectedWrite -notin $collectedWrites) {
                    throw "Native direct model collection drift for ${fieldName}:$($entity.entityId): expected $($directSpec.ExpectedCollectedWrite), found $($collectedWrites -join ', ')"
                }
            }

            if ($expectedPickups.Count -gt 1) {
                # One visible object that awards several rewards is one target, named for
                # everything the game says it received, not several overlapping inventory targets.
                Add-Definition `
                    -FieldId $fieldIds[$fieldName] -FieldName $fieldName -EntityId $entity.entityId `
                    -EntityName $entity.entityName -ModelResource $modelResource `
                    -Kind 'Named' -NativeId -1 -Label $directSpec.Label -Quantity 1 `
                    -CollectedBank $directSpec.CollectedBank -CollectedAddress $directSpec.CollectedAddress `
                    -CollectedMask $directSpec.CollectedMask -UsesTalkInteraction ($directSpec.ScriptType -eq 'Talk') `
                    -ManualNavigationGuidance $directSpec.ManualNavigationGuidance
                continue
            }

            Add-Definition `
                -FieldId $fieldIds[$fieldName] -FieldName $fieldName -EntityId $entity.entityId `
                -EntityName $entity.entityName -ModelResource $modelResource `
                -Kind $(if ($pickup.op -eq 'SMTRA') { 'Materia' } else { 'Item' }) `
                -NativeId ([int]$pickup.t) -Quantity $quantity `
                -CollectedBank $directSpec.CollectedBank -CollectedAddress $directSpec.CollectedAddress `
                -CollectedMask $directSpec.CollectedMask -UsesTalkInteraction ($directSpec.ScriptType -eq 'Talk') `
                -ManualNavigationGuidance $directSpec.ManualNavigationGuidance
            continue
        }

        $talk = $entity.scripts | Where-Object { $_.scriptType -eq 'Talk' } | Select-Object -First 1
        if ($null -eq $talk) {
            continue
        }

        $scripts = @(Get-ReachableScripts -Field $field -RootEntity $entity -RootScript $talk)
        $operations = @($scripts | ForEach-Object { $_.ops })
        $pickups = @($operations | Where-Object { $_.op -in @('STITM', 'SMTRA') })
        $receivedLabel = Get-ReceivedLabel $operations
        if ($pickups.Count -eq 0 -and -not $receivedLabel) {
            continue
        }

        $uniquePickups = @($pickups | ForEach-Object {
            $pickupKind = if ($_.op -eq 'SMTRA') { 'Materia' } else { 'Item' }
            $pickupQuantity = if ($_.op -eq 'STITM') { [Math]::Max(1, [int]$_.a) } else { 1 }
            [pscustomobject]@{
                Kind = $pickupKind
                NativeId = [int]$_.t
                Quantity = $pickupQuantity
                Key = "$($_.op):$($_.t)"
            }
        } | Group-Object Key | ForEach-Object {
            $_.Group | Sort-Object Quantity -Descending | Select-Object -First 1
        })

        $kind = 'Named'
        $nativeId = -1
        $quantity = 1
        $label = $receivedLabel
        $minimumGameMoment = -1
        $maximumGameMoment = -1
        if ($uniquePickups.Count -eq 1) {
            $kind = $uniquePickups[0].Kind
            $nativeId = $uniquePickups[0].NativeId
            $quantity = $uniquePickups[0].Quantity
            $label = ''
            if ($kind -eq 'Item' -and $nativeId -eq 319 -and $receivedLabel) {
                $kind = 'Named'
                $nativeId = -1
                $label = $receivedLabel
            }
        } elseif (-not $label) {
            $label = if ($modelResource -match 'trb|trbox') { 'Treasure chest' } else { 'Item pickup' }
        }

        if ($fieldName -eq 'blin2_i' -and $entity.entityId -in @(17, 18)) {
            # These two display chests are visible but locked during the first
            # Shinra visit. Their weapon contents must not be revealed until
            # the native late-game branch can actually award them.
            $minimumGameMoment = 1008
        }
        if ($fieldName -eq 'blin63_1' -and $entity.entityId -in @(42, 43, 44)) {
            # The same three models are coupons during the first raid and are
            # repopulated with different rewards during the return to Midgar.
            # Preserve the automatically resolved late rewards here; explicit
            # early coupon definitions are added below.
            $minimumGameMoment = 1008
        }
        if ($fieldName -eq 'ealin_2' -and $entity.entityId -eq 15) {
            # One visible white package awards both items in the same Talk script.
            # Treating it as a generic pickup hides information that sighted players
            # receive from the package and its two reward messages.
            $kind = 'Named'
            $nativeId = -1
            $quantity = 1
            $label = 'White package: Potion and Phoenix Down'
        }
        if ($fieldName -eq 'blin65_1' -and $entity.entityId -in @(15, 16, 17, 18, 19)) {
            $kind = 'Named'
            $nativeId = -1
            $quantity = 1
            $label = "Midgar Parts chest $([char](65 + $entity.entityId - 15))"
        }
        if ($fieldName -eq 'blin65_1' -and $entity.entityId -eq 20) {
            $kind = 'Named'
            $nativeId = -1
            $quantity = 1
            $label = 'Keycard 66 chest'
        }

        $bitWrites = @($operations | Where-Object { $_.op -eq 'BITON' } | ForEach-Object {
            [pscustomobject]@{
                Bank = [int]$_.bd
                Address = [int]$_.d
                Mask = 1 -shl [int]$_.bit
                Key = "$($_.bd):$($_.d):$($_.bit)"
            }
        } | Group-Object Key | ForEach-Object { $_.Group[0] })

        $collectedBank = -1
        $collectedAddress = -1
        $collectedMask = 0
        if ($bitWrites.Count -eq 1 -and $bitWrites[0].Bank -in @(1, 3, 5, 11, 13, 15)) {
            $collectedBank = $bitWrites[0].Bank
            $collectedAddress = $bitWrites[0].Address
            $collectedMask = $bitWrites[0].Mask
        }

        if ($fieldName -eq 'blin63_1' -and $entity.entityId -in @(42, 43, 44)) {
            $collectedBank = 3
            $collectedAddress = 177
            $collectedMask = 1 -shl ($entity.entityId - 37)
        }
        if ($fieldName -eq 'blin65_1') {
            switch ([int]$entity.entityId) {
                15 { $collectedBank = 1; $collectedAddress = 56; $collectedMask = 0x40 }
                16 { $collectedBank = 1; $collectedAddress = 56; $collectedMask = 0x80 }
                17 { $collectedBank = 1; $collectedAddress = 57; $collectedMask = 0x01 }
                18 { $collectedBank = 1; $collectedAddress = 57; $collectedMask = 0x02 }
                19 { $collectedBank = 1; $collectedAddress = 57; $collectedMask = 0x04 }
                20 { $collectedBank = 1; $collectedAddress = 57; $collectedMask = 0x08 }
            }
        }

        $requiredBank = -1
        $requiredAddress = -1
        $requiredMask = 0
        $requiredValue = 0
        $cueKindOverride = ''
        if ($fieldName -eq 'uta_im' -and $entity.entityId -eq 7) {
            # Wutai's Item Store chest is a step of the materia-recovery quest, not an
            # ordinary treasure. Its Talk returns at once until the Turtle's Paradise
            # scene has set Bank[3][189] bit 1, and until then the shopkeeper's own
            # Init stands her between the counter and the chest. The Talk writes two
            # bits: Bank[1][59] bit 1 is the chest's own, and Bank[3][190] bit 0 is
            # Yuffie's theft that follows. She takes the Materia before the player can
            # keep it, so the chest is named for what a sighted player sees rather than
            # for the Materia inside it.
            $theftWrites = @($operations |
                Where-Object { $_.op -eq 'BITON' } |
                ForEach-Object { "BITON:$($_.bd):$($_.d):$($_.bit)" })
            foreach ($expectedWrite in @('BITON:1:59:1', 'BITON:3:190:0')) {
                if ($expectedWrite -notin $theftWrites) {
                    throw "Native Wutai chest drift for ${fieldName}:$($entity.entityId): expected $expectedWrite, found $($theftWrites -join ', ')"
                }
            }
            $kind = 'Named'
            $nativeId = -1
            $quantity = 1
            $label = 'Treasure chest'
            $collectedBank = 1
            $collectedAddress = 59
            $collectedMask = 0x02
            $requiredBank = 3
            $requiredAddress = 189
            $requiredMask = 0x02
            $requiredValue = 0x02
            $cueKindOverride = 'Chest'
        }

        Add-Definition `
            -FieldId $fieldIds[$fieldName] -FieldName $fieldName -EntityId $entity.entityId `
            -EntityName $entity.entityName -ModelResource $modelResource -Kind $kind `
            -NativeId $nativeId -Label $label -Quantity $quantity `
            -CollectedBank $collectedBank -CollectedAddress $collectedAddress `
            -CollectedMask $collectedMask -RequiredBank $requiredBank `
            -RequiredAddress $requiredAddress -RequiredMask $requiredMask `
            -RequiredValue $requiredValue -CueKindOverride $cueKindOverride `
            -MinimumGameMoment $minimumGameMoment `
            -MaximumGameMoment $maximumGameMoment -UsesTalkInteraction $true
    }

    foreach ($spec in @($linePickupSpecs | Where-Object { $_.FieldName -eq $fieldName })) {
        $entity = $field.script.entities | Where-Object { $_.entityId -eq $spec.EntityId } | Select-Object -First 1
        if ($null -eq $entity -or $entity.entityType -ne 'Line') {
            throw "Missing native line pickup entity ${fieldName}:$($spec.EntityId)"
        }

        $init = $entity.scripts | Where-Object { $_.scriptType -eq 'Init' } | Select-Object -First 1
        $line = $init.ops | Where-Object { $_.op -eq 'LINE' } | Select-Object -First 1
        $trigger = $entity.scripts | Where-Object { $_.scriptType -eq $spec.ScriptType } | Select-Object -First 1
        if ($null -eq $line -or $null -eq $trigger) {
            throw "Missing LINE coordinates or $($spec.ScriptType) script for ${fieldName}:$($spec.EntityId)"
        }

        $scripts = @(Get-ReachableScripts -Field $field -RootEntity $entity -RootScript $trigger)
        $pickups = @($scripts | ForEach-Object { $_.ops } | Where-Object { $_.op -in @('STITM', 'SMTRA') })
        $uniquePickups = @($pickups | ForEach-Object {
            $pickupQuantity = if ($_.op -eq 'STITM') { [Math]::Max(1, [int]$_.a) } else { 1 }
            [pscustomobject]@{
                Op = [string]$_.op
                Kind = if ($_.op -eq 'SMTRA') { 'Materia' } else { 'Item' }
                NativeId = [int]$_.t
                Quantity = $pickupQuantity
                Key = "$($_.op):$($_.t):$pickupQuantity"
            }
        } | Group-Object Key | ForEach-Object { $_.Group[0] } | Sort-Object Key)

        $actualPickups = @($uniquePickups | ForEach-Object { $_.Key })
        $expectedPickups = @($spec.ExpectedPickups | Sort-Object)
        if (@(Compare-Object $expectedPickups $actualPickups).Count -ne 0) {
            throw "Native pickup drift for ${fieldName}:$($spec.EntityId): expected $($expectedPickups -join ', '), found $($actualPickups -join ', ')"
        }

        $staticX = [int][Math]::Round(([int]$line.x1 + [int]$line.x2) / 2.0, [MidpointRounding]::AwayFromZero)
        $staticY = [int][Math]::Round(([int]$line.y1 + [int]$line.y2) / 2.0, [MidpointRounding]::AwayFromZero)
        $staticZ = [int][Math]::Round(([int]$line.z1 + [int]$line.z2) / 2.0, [MidpointRounding]::AwayFromZero)
        foreach ($pickup in $uniquePickups) {
            Add-Definition `
                -FieldId $fieldIds[$fieldName] -FieldName $fieldName -EntityId $entity.entityId `
                -EntityName $entity.entityName -ModelResource '' -Kind $pickup.Kind `
                -NativeId $pickup.NativeId -Quantity $pickup.Quantity `
                -CollectedBank $spec.CollectedBank -CollectedAddress $spec.CollectedAddress `
                -CollectedMask $spec.CollectedMask -RequiredBank $spec.RequiredBank `
                -RequiredAddress $spec.RequiredAddress -RequiredMask $spec.RequiredMask `
                -RequiredValue $spec.RequiredValue -TargetKind 'Line' `
                -StaticX $staticX -StaticY $staticY -StaticZ $staticZ `
                -CueKindOverride $spec.CueKind -MinimumGameMoment $spec.MinimumGameMoment `
                -MaximumGameMoment $spec.MaximumGameMoment `
                -UsesPlayerCollisionRadius ($null -ne $spec.PSObject.Properties['UsesPlayerCollisionRadius'] -and $spec.UsesPlayerCollisionRadius)
        }
    }
}

# The two fallen guards at the opening station are genuine searchable Potion pickups,
# but use character models rather than field-background object models.
Add-Definition 116 'md1stin' 9 'gu0' 'md1stinshinra_guard.char' 'Item' 0 '' 1 15 32 0x03
Add-Definition 116 'md1stin' 10 'gu1' 'md1stinshinra_guard.char' 'Item' 0 '' 1 15 32 0x03

# Reactor 1 and Reactor 5 share elevtr1. Its fixed background switch is entity
# ele's native Main interaction point, so keep it trackable under Objects on
# both the initial descent and the later escape return.
Add-Definition -FieldId 121 -FieldName 'elevtr1' -EntityId 5 -EntityName 'ele' -ModelResource '' -Kind 'Named' -Label 'Reactor elevator switch; press OK' -TargetKind 'Location' -StaticX 86 -StaticY 64 -StaticZ 5

# The Sector 5 church barrel puzzle uses four visible barrel models. Their Talk
# scripts call Aerith's rescue scripts, so the generic native NPC label resolver
# otherwise mistakes each barrel for Aerith. Keep all four visible objects
# available by their on-screen positions, including the fourth lower-right
# barrel that is not part of the successful left-middle-right rescue sequence.
Add-Definition -FieldId 184 -FieldName 'chrin_2' -EntityId 8 -EntityName 'bar1' -ModelResource 'chrin_2fieldbg_taru.char' -Kind 'Named' -Label 'Middle barrel'
Add-Definition -FieldId 184 -FieldName 'chrin_2' -EntityId 9 -EntityName 'bar2' -ModelResource 'chrin_2fieldbg_taru.char' -Kind 'Named' -Label 'Right barrel'
Add-Definition -FieldId 184 -FieldName 'chrin_2' -EntityId 10 -EntityName 'bar3' -ModelResource 'chrin_2fieldbg_taru.char' -Kind 'Named' -Label 'Left barrel'
Add-Definition -FieldId 184 -FieldName 'chrin_2' -EntityId 11 -EntityName 'bar4' -ModelResource 'chrin_2fieldbg_taru.char' -Kind 'Named' -Label 'Lower-right barrel'

# These Train Graveyard pickups deliberately use std_man1 rather than a
# fieldbg_* prop even though their Talk scripts award visible items. Preserve
# them explicitly so the model-resource filter cannot hide them. They are Talk
# pickups (TLKON, TALKR 120): each stands on its own two-triangle island of the
# walkmesh, 51 to 57 units from the floor, so only the native talk range reaches
# it and the fixed object radius did not.
Add-Definition -FieldId 144 -FieldName 'mds7st1' -EntityId 16 -EntityName 'doram' -ModelResource 'mds7st1std_man1.char' -Kind 'Item' -NativeId 1 -CollectedBank 1 -CollectedAddress 36 -CollectedMask 0x01 -UsesTalkInteraction $true
Add-Definition -FieldId 145 -FieldName 'mds7st2' -EntityId 20 -EntityName 'doram' -ModelResource 'mds7st2std_man1.char' -Kind 'Item' -NativeId 3 -CollectedBank 1 -CollectedAddress 36 -CollectedMask 0x08 -UsesTalkInteraction $true
Add-Definition -FieldId 224 -FieldName 'wcrimb_2' -EntityId 11 -EntityName 'line90' -ModelResource '' -Kind 'Named' -Label 'Optional battery socket' -TargetKind 'Line' -StaticX -260 -StaticY 972 -StaticZ 2588 -CollectedBank 1 -CollectedAddress 165 -CollectedMask 0x10 -RequiredBank 1 -RequiredAddress 165 -RequiredMask 0x80 -RequiredValue 0x80

# las3_3's Mega All floats over the middle rock of the crater's jumps: its Talk and Contact
# are empty, and it is caught only in the air. Walking onto l21 or l22 (the two take-off
# ledges) sets the jump selector 5[2] to 0x15 or 0x16 and has cloud's script 3 JUMP to that rock
# (952,-439) t186; on landing, while 1[50] bit 4 is clear, it tests for a fresh OK press once
# (IFKEYON 0x0220) and, if there is one, runs mat's script 3, which awards it (SMTRA 12) and sets the bit. So the target
# is each take-off line, and the label says what to press; the floating model is not a place
# to walk to, and nothing here presses anything. Each line's Move runs as soon as the leader is
# inside its own collision range (00637ABB), so the target is kept inside that range and the jump
# starts on arrival; only the OK press is the player's.
Add-Definition -FieldId 762 -FieldName 'las3_3' -EntityId 26 -EntityName 'l21' -ModelResource '' -Kind 'Named' -Label 'Mega All Materia, caught mid-jump: stepping onto this take-off jumps you to the middle rock; press OK repeatedly until you land there (take-off 1 of 2)' -TargetKind 'Line' -StaticX 1075 -StaticY -431 -StaticZ -1341 -CollectedBank 1 -CollectedAddress 50 -CollectedMask 0x10 -CueKindOverride 'Materia' -UsesPlayerCollisionRadius $true
Add-Definition -FieldId 762 -FieldName 'las3_3' -EntityId 27 -EntityName 'l22' -ModelResource '' -Kind 'Named' -Label 'Mega All Materia, caught mid-jump: stepping onto this take-off jumps you to the middle rock; press OK repeatedly until you land there (take-off 2 of 2)' -TargetKind 'Line' -StaticX 760 -StaticY -554 -StaticZ -1289 -CollectedBank 1 -CollectedAddress 50 -CollectedMask 0x10 -CueKindOverride 'Materia' -UsesPlayerCollisionRadius $true

# The Great Glacier's optional stops that are LINEs, not models (GreatGlacierOptionalContentTests).
# hyou10's hot spring: line80..83 run event script 3 from their Move slot once per visit, which asks
# dialog 54; "Touch it" runs Cloud's script 4, dialog 55 and bank 1 byte 199 bit 0, the flag Snow
# tests in hyou13_2 before she fights. The lines are switched off after the question, so the target
# goes with them until the next visit. hyou5_2's crossing starts: line50a (south shore) and line50b
# (north shore) run Cloud's crossing script 3 from their GoOnce slot on entering range.
Add-Definition -FieldId 680 -FieldName 'hyou10' -EntityId 20 -EntityName 'line80' -ModelResource '' -Kind 'Named' -Label 'Hot spring' -TargetKind 'Line' -StaticX -395 -StaticY 109 -StaticZ 99 -CollectedBank 1 -CollectedAddress 199 -CollectedMask 0x01 -UsesPlayerCollisionRadius $true
Add-Definition -FieldId 665 -FieldName 'hyou5_2' -EntityId 21 -EntityName 'line50a' -ModelResource '' -Kind 'Named' -Label 'Ice floes, start the crossing from the south shore' -TargetKind 'Line' -StaticX 137 -StaticY -696 -StaticZ 31 -UsesPlayerCollisionRadius $true
Add-Definition -FieldId 665 -FieldName 'hyou5_2' -EntityId 22 -EntityName 'line50b' -ModelResource '' -Kind 'Named' -Label 'Ice floes, start the crossing from the north shore' -TargetKind 'Line' -StaticX 192 -StaticY 246 -StaticZ 28 -UsesPlayerCollisionRadius $true
# Temple of the Ancients mural hall (612 kuro_82): the miniature temple the director shows after
# the Red Dragon (entity 19 "mini", jtmpobj, at (1032,18,57); Talk and Contact are a lone RET).
# What the game runs is LINE entity 7 "border2" (914,35,0)-(922,-46,0) in front of it: its Go,
# on Confirm, sets 624 and enters the model (613) from 621; at 627 it plays Cloud failing to
# shift it. The 630 branch is a cutscene. See story-regions/TempleOfTheAncients.ps1.
Add-Definition -FieldId 612 -FieldName 'kuro_82' -EntityId 7 -EntityName 'border2' -ModelResource '' -Kind 'Named' -Label 'Miniature temple; press Confirm at it' -TargetKind 'Line' -StaticX 918 -StaticY -6 -StaticZ 0 -MinimumGameMoment 621 -MaximumGameMoment 629 -UsesPlayerCollisionRadius $true

# anfrst_1's Slash-All (box1, e36) lies in the first Mutant Flytrap's mouth, between its lines
# big0lt (e25) and big0rt (e26). While 5[51] is 0 - it starts so on every entry, and only the
# beehive's script 3 sets it - either line runs the leader's script 20 or 21: control off, 1000
# HP off each member, the flytrap's bite, and a JUMP back out to t102 or t142. So the materia is
# offered either way, but walked to only once the flytrap has shut.
Add-Definition -FieldId 620 -FieldName 'anfrst_1' -EntityId 36 -EntityName 'box1' -ModelResource 'anfrst_1fieldbg_mtra7.char' -Kind 'Materia' -NativeId 14 -CollectedBank 15 -CollectedAddress 37 -CollectedMask 0x10 -RequiredBank 5 -RequiredAddress 51 -RequiredMask 0xFF -RequiredValue 0x01 -UsesTalkInteraction $true
Add-Definition -FieldId 620 -FieldName 'anfrst_1' -EntityId 36 -EntityName 'box1' -ModelResource 'anfrst_1fieldbg_mtra7.char' -Kind 'Materia' -NativeId 14 -CollectedBank 15 -CollectedAddress 37 -CollectedMask 0x10 -RequiredBank 5 -RequiredAddress 51 -RequiredMask 0xFF -RequiredValue 0x00 -UsesTalkInteraction $true -ManualNavigationGuidance 'It lies in the Mutant Flytrap''s mouth. While the flytrap is open, stepping in makes it bite the party, which costs HP and throws you back out, so auto walk is unavailable until it has shut.'

# Sector 5's town contains several visible, actionable fixtures that are not
# inventory pickups. Keep their native model or LINE identity so they follow
# visibility/LINON state instead of becoming unconditional static landmarks.
# The television is used by talking to it (TLKON, TALKR 120); it stands 77 units off
# the floor's walkmesh, so it needs the native talk range too.
Add-Definition -FieldId 174 -FieldName 'min51_1' -EntityId 7 -EntityName 'TV' -ModelResource '5min1_1midgal_avaman.char' -Kind 'Named' -Label 'Television' -UsesTalkInteraction $true
Add-Definition -FieldId 175 -FieldName 'min51_2' -EntityId 6 -EntityName 'CLINE' -ModelResource '' -Kind 'Named' -Label 'Dresser with hidden drawer' -TargetKind 'Line' -StaticX 3 -StaticY 132 -StaticZ -168
Add-Definition -FieldId 175 -FieldName 'min51_2' -EntityId 8 -EntityName 'TIRASI' -ModelResource '' -Kind 'Named' -Label "Turtle's Paradise flyer No. 1" -TargetKind 'Line' -StaticX -138 -StaticY 146 -StaticZ -169
Add-Definition -FieldId 180 -FieldName 'mds5_m' -EntityId 8 -EntityName 'LINEB' -ModelResource '' -Kind 'Named' -Label 'Freezer' -TargetKind 'Line' -StaticX 119 -StaticY 66 -StaticZ -106
Add-Definition -FieldId 190 -FieldName 'ealin_2' -EntityId 9 -EntityName 'bedsen' -ModelResource '' -Kind 'Named' -Label 'Bed' -TargetKind 'Line' -StaticX -192 -StaticY 145 -StaticZ 298

# Wall Market has several visible, actionable background fixtures whose native
# interaction is carried by LINE entities rather than field models. Keep those
# in Objects while NPC/shop-counter interactions remain in the NPC category.
# The item-shop machine becomes the Premium Heart pickup at game moment 999, so
# its early broken-machine label and late pickup definition never overlap.
Add-Definition -FieldId 198 -FieldName 'mkt_ia' -EntityId 5 -EntityName 'line00' -ModelResource '' -Kind 'Named' -Label 'Broken item shop machine' -TargetKind 'Line' -StaticX 39 -StaticY 46 -StaticZ 17 -MaximumGameMoment 998
Add-Definition -FieldId 204 -FieldName 'mktpb' -EntityId 4 -EntityName 'line00' -ModelResource '' -Kind 'Named' -Label 'Occupied bathroom door' -TargetKind 'Line' -StaticX -606 -StaticY 367 -StaticZ 0
Add-Definition -FieldId 207 -FieldName 'colne_2' -EntityId 13 -EntityName 'CDLINE' -ModelResource '' -Kind 'Named' -Label "Don Corneo's office door" -TargetKind 'Line' -StaticX 1 -StaticY 286 -StaticZ 225

# Floor 60's guard puzzle uses background statues rather than live models.
# Keep each native cover position targetable while the crossing state is active.
Add-Definition -FieldId 239 -FieldName 'blin60_1' -EntityId -1 -EntityName '' -ModelResource '' -Kind 'Named' -Label 'Starting cover statue' -TargetKind 'Location' -StaticX -551 -StaticY 248 -StaticZ 0 -RequiredBank 5 -RequiredAddress 14 -RequiredMask 0xFF -RequiredValue 0x01 -MinimumGameMoment 263 -MaximumGameMoment 263
Add-Definition -FieldId 239 -FieldName 'blin60_1' -EntityId -1 -EntityName '' -ModelResource '' -Kind 'Named' -Label 'First section, hiding statue 1 of 3' -TargetKind 'Location' -StaticX -396 -StaticY 259 -StaticZ 0 -RequiredBank 5 -RequiredAddress 14 -RequiredMask 0xFF -RequiredValue 0x01 -MinimumGameMoment 263 -MaximumGameMoment 263
Add-Definition -FieldId 239 -FieldName 'blin60_1' -EntityId -1 -EntityName '' -ModelResource '' -Kind 'Named' -Label 'First section, hiding statue 2 of 3' -TargetKind 'Location' -StaticX -260 -StaticY 251 -StaticZ 0 -RequiredBank 5 -RequiredAddress 14 -RequiredMask 0xFF -RequiredValue 0x01 -MinimumGameMoment 263 -MaximumGameMoment 263
Add-Definition -FieldId 239 -FieldName 'blin60_1' -EntityId -1 -EntityName '' -ModelResource '' -Kind 'Named' -Label 'First section, midpoint hiding statue 3 of 3' -TargetKind 'Location' -StaticX 7 -StaticY 204 -StaticZ 0 -RequiredBank 5 -RequiredAddress 14 -RequiredMask 0xFF -RequiredValue 0x01 -MinimumGameMoment 263 -MaximumGameMoment 263
Add-Definition -FieldId 239 -FieldName 'blin60_1' -EntityId -1 -EntityName '' -ModelResource '' -Kind 'Named' -Label 'Second section, hiding statue 1 of 3' -TargetKind 'Location' -StaticX 267 -StaticY 256 -StaticZ 0 -RequiredBank 5 -RequiredAddress 14 -RequiredMask 0xFF -RequiredValue 0x01 -MinimumGameMoment 263 -MaximumGameMoment 263
Add-Definition -FieldId 239 -FieldName 'blin60_1' -EntityId -1 -EntityName '' -ModelResource '' -Kind 'Named' -Label 'Second section, hiding statue 2 of 3' -TargetKind 'Location' -StaticX 407 -StaticY 256 -StaticZ 0 -RequiredBank 5 -RequiredAddress 14 -RequiredMask 0xFF -RequiredValue 0x01 -MinimumGameMoment 263 -MaximumGameMoment 263
Add-Definition -FieldId 239 -FieldName 'blin60_1' -EntityId -1 -EntityName '' -ModelResource '' -Kind 'Named' -Label 'Second section, final hiding statue 3 of 3' -TargetKind 'Location' -StaticX 547 -StaticY 252 -StaticZ 0 -RequiredBank 5 -RequiredAddress 14 -RequiredMask 0xFF -RequiredValue 0x01 -MinimumGameMoment 263 -MaximumGameMoment 263

# Shinra Headquarters guide-listed interactables. These are native model or
# LINE targets whose scripts do not look like ordinary inventory pickups to
# the generic extractor.
Add-Definition -FieldId 234 -FieldName 'blin1' -EntityId 37 -EntityName 'TIRASI' -ModelResource '' -Kind 'Named' -Label "Turtle's Paradise flyer No. 2" -TargetKind 'Line' -StaticX -1214 -StaticY 851 -StaticZ 0
Add-Definition -FieldId 234 -FieldName 'blin1' -EntityId 38 -EntityName 'TIRASIB' -ModelResource '' -Kind 'Named' -Label 'Shinra company bulletin' -TargetKind 'Line' -StaticX -1342 -StaticY 734 -StaticZ 0
Add-Definition -FieldId 236 -FieldName 'blin2_i' -EntityId 15 -EntityName 'LINEC' -ModelResource '' -Kind 'Named' -Label 'Automated shop terminal' -TargetKind 'Line' -StaticX 133 -StaticY -272 -StaticZ 0
Add-Definition -FieldId 236 -FieldName 'blin2_i' -EntityId 16 -EntityName 'TV' -ModelResource '' -Kind 'Named' -Label 'Shinra news screen' -TargetKind 'Line' -StaticX -207 -StaticY -464 -StaticZ 0
Add-Definition -FieldId 236 -FieldName 'blin2_i' -EntityId 17 -EntityName 'TAKARAA' -ModelResource 'blin2_ifieldbg_trb_mety.char' -Kind 'Named' -Label 'Locked left display chest' -MaximumGameMoment 1007
Add-Definition -FieldId 236 -FieldName 'blin2_i' -EntityId 18 -EntityName 'TAKARAB' -ModelResource 'blin2_ifieldbg_trb_mety.char' -Kind 'Named' -Label 'Locked right display chest' -MaximumGameMoment 1007

Add-Definition -FieldId 242 -FieldName 'blin62_1' -EntityId 18 -EntityName 'PLINEC' -ModelResource '' -Kind 'Named' -Label 'Urban Development Research Library sign' -TargetKind 'Line' -StaticX -318 -StaticY -855 -StaticZ 0
Add-Definition -FieldId 242 -FieldName 'blin62_1' -EntityId 20 -EntityName 'PLINED' -ModelResource '' -Kind 'Named' -Label 'Scientific Research Library sign' -TargetKind 'Line' -StaticX 299 -StaticY -855 -StaticZ 0
Add-Definition -FieldId 242 -FieldName 'blin62_1' -EntityId 22 -EntityName 'PLINEE' -ModelResource '' -Kind 'Named' -Label 'Peace Preservation and Weapon Development Research Library sign' -TargetKind 'Line' -StaticX -307 -StaticY 184 -StaticZ 0
Add-Definition -FieldId 242 -FieldName 'blin62_1' -EntityId 24 -EntityName 'PLINEF' -ModelResource '' -Kind 'Named' -Label 'Space Development Research Library sign' -TargetKind 'Line' -StaticX 301 -StaticY 184 -StaticZ 0

# Each Floor 62 library has three shelves and two readable books per shelf.
# A/B are native LINE interactions on the top shelf. C-F are native triangle-
# gated interactions whose IFSW checks use the listed walkmesh centroids.
# Keep the physical book positions distinct so the player hears the same shelf
# and side information that is visible on screen. In particular, BLINEC in the
# Space Development room is the guide's left book on the middle shelf.
Add-Definition -FieldId 243 -FieldName 'blin62_2' -EntityId 19 -EntityName 'YLINEA' -ModelResource '' -Kind 'Named' -Label 'Urban Development Research Library, top shelf, left book' -TargetKind 'Line' -StaticX -287 -StaticY -663 -StaticZ 0
Add-Definition -FieldId 243 -FieldName 'blin62_2' -EntityId 20 -EntityName 'YLINEB' -ModelResource '' -Kind 'Named' -Label 'Urban Development Research Library, top shelf, right book' -TargetKind 'Line' -StaticX -204 -StaticY -605 -StaticZ 0
Add-Definition -FieldId 243 -FieldName 'blin62_2' -EntityId 21 -EntityName 'YLINEC' -ModelResource '' -Kind 'Named' -Label 'Urban Development Research Library, middle shelf, left book' -TargetKind 'Location' -StaticX -285 -StaticY -519 -StaticZ 0
Add-Definition -FieldId 243 -FieldName 'blin62_2' -EntityId 22 -EntityName 'YLINED' -ModelResource '' -Kind 'Named' -Label 'Urban Development Research Library, middle shelf, right book' -TargetKind 'Location' -StaticX -202 -StaticY -440 -StaticZ 0
Add-Definition -FieldId 243 -FieldName 'blin62_2' -EntityId 23 -EntityName 'YLINEE' -ModelResource '' -Kind 'Named' -Label 'Urban Development Research Library, bottom shelf, left book' -TargetKind 'Location' -StaticX -288 -StaticY -338 -StaticZ 0
Add-Definition -FieldId 243 -FieldName 'blin62_2' -EntityId 24 -EntityName 'YLINEF' -ModelResource '' -Kind 'Named' -Label 'Urban Development Research Library, bottom shelf, right book' -TargetKind 'Location' -StaticX -204 -StaticY -258 -StaticZ 0
Add-Definition -FieldId 243 -FieldName 'blin62_2' -EntityId 25 -EntityName 'BLINEA' -ModelResource '' -Kind 'Named' -Label 'Scientific Research Library, top shelf, left book' -TargetKind 'Line' -StaticX 201 -StaticY -606 -StaticZ 0
Add-Definition -FieldId 243 -FieldName 'blin62_2' -EntityId 26 -EntityName 'BLINEB' -ModelResource '' -Kind 'Named' -Label 'Scientific Research Library, top shelf, right book' -TargetKind 'Line' -StaticX 292 -StaticY -669 -StaticZ 0
Add-Definition -FieldId 243 -FieldName 'blin62_2' -EntityId 27 -EntityName 'BLINEC' -ModelResource '' -Kind 'Named' -Label 'Scientific Research Library, middle shelf, left book' -TargetKind 'Location' -StaticX 208 -StaticY -444 -StaticZ 0
Add-Definition -FieldId 243 -FieldName 'blin62_2' -EntityId 28 -EntityName 'BLINED' -ModelResource '' -Kind 'Named' -Label 'Scientific Research Library, middle shelf, right book' -TargetKind 'Location' -StaticX 293 -StaticY -525 -StaticZ 0
Add-Definition -FieldId 243 -FieldName 'blin62_2' -EntityId 29 -EntityName 'BLINEE' -ModelResource '' -Kind 'Named' -Label 'Scientific Research Library, bottom shelf, left book' -TargetKind 'Location' -StaticX 209 -StaticY -258 -StaticZ 0
Add-Definition -FieldId 243 -FieldName 'blin62_2' -EntityId 30 -EntityName 'BLINEF' -ModelResource '' -Kind 'Named' -Label 'Scientific Research Library, bottom shelf, right book' -TargetKind 'Location' -StaticX 294 -StaticY -339 -StaticZ 0
Add-Definition -FieldId 244 -FieldName 'blin62_3' -EntityId 19 -EntityName 'YLINEA' -ModelResource '' -Kind 'Named' -Label 'Peace Preservation and Weapon Development Research Library, top shelf, left book' -TargetKind 'Line' -StaticX -290 -StaticY 465 -StaticZ 0
Add-Definition -FieldId 244 -FieldName 'blin62_3' -EntityId 20 -EntityName 'YLINEB' -ModelResource '' -Kind 'Named' -Label 'Peace Preservation and Weapon Development Research Library, top shelf, right book' -TargetKind 'Line' -StaticX -200 -StaticY 522 -StaticZ 0
Add-Definition -FieldId 244 -FieldName 'blin62_3' -EntityId 21 -EntityName 'YLINEC' -ModelResource '' -Kind 'Named' -Label 'Peace Preservation and Weapon Development Research Library, middle shelf, left book' -TargetKind 'Location' -StaticX -276 -StaticY 620 -StaticZ 0
Add-Definition -FieldId 244 -FieldName 'blin62_3' -EntityId 22 -EntityName 'YLINED' -ModelResource '' -Kind 'Named' -Label 'Peace Preservation and Weapon Development Research Library, middle shelf, right book' -TargetKind 'Location' -StaticX -194 -StaticY 698 -StaticZ 0
Add-Definition -FieldId 244 -FieldName 'blin62_3' -EntityId 23 -EntityName 'YLINEE' -ModelResource '' -Kind 'Named' -Label 'Peace Preservation and Weapon Development Research Library, bottom shelf, left book' -TargetKind 'Location' -StaticX -279 -StaticY 816 -StaticZ 0
Add-Definition -FieldId 244 -FieldName 'blin62_3' -EntityId 24 -EntityName 'YLINEF' -ModelResource '' -Kind 'Named' -Label 'Peace Preservation and Weapon Development Research Library, bottom shelf, right book' -TargetKind 'Location' -StaticX -197 -StaticY 895 -StaticZ 0
Add-Definition -FieldId 244 -FieldName 'blin62_3' -EntityId 25 -EntityName 'BLINEA' -ModelResource '' -Kind 'Named' -Label 'Space Development Research Library, top shelf, left book' -TargetKind 'Line' -StaticX 201 -StaticY 522 -StaticZ 0
Add-Definition -FieldId 244 -FieldName 'blin62_3' -EntityId 26 -EntityName 'BLINEB' -ModelResource '' -Kind 'Named' -Label 'Space Development Research Library, top shelf, right book' -TargetKind 'Line' -StaticX 292 -StaticY 461 -StaticZ 0
Add-Definition -FieldId 244 -FieldName 'blin62_3' -EntityId 27 -EntityName 'BLINEC' -ModelResource '' -Kind 'Named' -Label 'Space Development Research Library, middle shelf, left book' -TargetKind 'Location' -StaticX 193 -StaticY 697 -StaticZ 0
Add-Definition -FieldId 244 -FieldName 'blin62_3' -EntityId 28 -EntityName 'BLINED' -ModelResource '' -Kind 'Named' -Label 'Space Development Research Library, middle shelf, right book' -TargetKind 'Location' -StaticX 276 -StaticY 619 -StaticZ 0
Add-Definition -FieldId 244 -FieldName 'blin62_3' -EntityId 29 -EntityName 'BLINEE' -ModelResource '' -Kind 'Named' -Label 'Space Development Research Library, bottom shelf, left book' -TargetKind 'Location' -StaticX 194 -StaticY 899 -StaticZ 0
Add-Definition -FieldId 244 -FieldName 'blin62_3' -EntityId 30 -EntityName 'BLINEF' -ModelResource '' -Kind 'Named' -Label 'Space Development Research Library, bottom shelf, right book' -TargetKind 'Location' -StaticX 279 -StaticY 820 -StaticZ 0

# Floor 63's optimal three-door solution follows the native D2 -> D4 -> D16
# state bits. D3 is the first door below the top corridor and must be skipped;
# D4's action side remains reachable after D2 without crossing another locked
# boundary, then its room leads directly to A Coupon.
Add-Definition -FieldId 245 -FieldName 'blin63_1' -EntityId 15 -EntityName 'MLINE' -ModelResource '' -Kind 'Named' -Label 'Floor 63 door-control and coupon-exchange computer' -TargetKind 'Line' -StaticX 920 -StaticY -570 -StaticZ 0
Add-Definition -FieldId 245 -FieldName 'blin63_1' -EntityId -1 -EntityName 'D2' -ModelResource '' -Kind 'Named' -Label 'Coupon route door 1 of 3, top corridor' -TargetKind 'Location' -StaticX 414 -StaticY 972 -StaticZ 0 -CollectedBank 3 -CollectedAddress 174 -CollectedMask 0x02 -RequiredBank 3 -RequiredAddress 177 -RequiredMask 0x10 -RequiredValue 0x10 -MaximumGameMoment 1007
Add-Definition -FieldId 245 -FieldName 'blin63_1' -EntityId -1 -EntityName 'D4' -ModelResource '' -Kind 'Named' -Label 'Coupon route door 2 of 3, second door below the top corridor' -TargetKind 'Location' -StaticX -549 -StaticY 752 -StaticZ 0 -CollectedBank 3 -CollectedAddress 174 -CollectedMask 0x08 -RequiredBank 3 -RequiredAddress 174 -RequiredMask 0x02 -RequiredValue 0x02 -MaximumGameMoment 1007
Add-Definition -FieldId 245 -FieldName 'blin63_1' -EntityId -1 -EntityName 'D16' -ModelResource '' -Kind 'Named' -Label 'Coupon route door 3 of 3, between B and C Coupon rooms' -TargetKind 'Location' -StaticX -148 -StaticY -356 -StaticZ 0 -CollectedBank 3 -CollectedAddress 175 -CollectedMask 0x80 -RequiredBank 3 -RequiredAddress 177 -RequiredMask 0x08 -RequiredValue 0x08 -MaximumGameMoment 1007
Add-Definition -FieldId 245 -FieldName 'blin63_1' -EntityId 42 -EntityName 'TAKARA1' -ModelResource 'blin63_1fieldbg_zuta_orig.char' -Kind 'Named' -Label 'A Coupon' -CollectedBank 3 -CollectedAddress 177 -CollectedMask 0x02 -RequiredBank 3 -RequiredAddress 174 -RequiredMask 0x08 -RequiredValue 0x08 -MaximumGameMoment 1007
Add-Definition -FieldId 245 -FieldName 'blin63_1' -EntityId 43 -EntityName 'TAKARA2' -ModelResource 'blin63_1fieldbg_zuta_orig.char' -Kind 'Named' -Label 'C Coupon' -CollectedBank 3 -CollectedAddress 177 -CollectedMask 0x04 -RequiredBank 3 -RequiredAddress 175 -RequiredMask 0x80 -RequiredValue 0x80 -MaximumGameMoment 1007
Add-Definition -FieldId 245 -FieldName 'blin63_1' -EntityId 44 -EntityName 'TAKARA3' -ModelResource 'blin63_1fieldbg_zuta_orig.char' -Kind 'Named' -Label 'B Coupon' -CollectedBank 3 -CollectedAddress 177 -CollectedMask 0x08 -RequiredBank 3 -RequiredAddress 177 -RequiredMask 0x02 -RequiredValue 0x02 -MaximumGameMoment 1007
Add-Definition -FieldId 245 -FieldName 'blin63_1' -EntityId 45 -EntityName 'DUCTLA' -ModelResource '' -Kind 'Named' -Label 'Computer-room duct exit; cannot enter from this side' -TargetKind 'Line' -StaticX 771 -StaticY -607 -StaticZ 0 -CollectedBank 3 -CollectedAddress 181 -CollectedMask 0x80 -RequiredBank 3 -RequiredAddress 177 -RequiredMask 0x0E -RequiredValue 0x0E -MaximumGameMoment 1007
Add-Definition -FieldId 245 -FieldName 'blin63_1' -EntityId 46 -EntityName 'DUCTLB' -ModelResource '' -Kind 'Named' -Label 'A Coupon room duct entrance' -TargetKind 'Line' -StaticX -864 -StaticY 119 -StaticZ 0 -CollectedBank 3 -CollectedAddress 177 -CollectedMask 0x08 -RequiredBank 3 -RequiredAddress 177 -RequiredMask 0x02 -RequiredValue 0x02 -MaximumGameMoment 1007
Add-Definition -FieldId 245 -FieldName 'blin63_1' -EntityId 47 -EntityName 'DUCTLC' -ModelResource '' -Kind 'Named' -Label 'B Coupon room duct entrance' -TargetKind 'Line' -StaticX 340 -StaticY 100 -StaticZ 0 -CollectedBank 3 -CollectedAddress 181 -CollectedMask 0x80 -RequiredBank 3 -RequiredAddress 177 -RequiredMask 0x0E -RequiredValue 0x0E -MaximumGameMoment 1007

# Floor 63's crawlspace is its own field. These are the three native LADER
# endpoints used by CLOUD's duct scripts: the A and B room drops, plus the
# one-way exit back into the computer room.
Add-Definition -FieldId 246 -FieldName 'blin63_t' -EntityId 6 -EntityName 'CLOUD' -ModelResource '' -Kind 'Named' -Label 'Backtrack shaft to A Coupon room' -TargetKind 'Location' -StaticX -827 -StaticY 124 -StaticZ 369 -RequiredBank 3 -RequiredAddress 177 -RequiredMask 0x02 -RequiredValue 0x02 -MaximumGameMoment 1007
Add-Definition -FieldId 246 -FieldName 'blin63_t' -EntityId 6 -EntityName 'CLOUD' -ModelResource '' -Kind 'Named' -Label 'Shaft to B Coupon room' -TargetKind 'Location' -StaticX 384 -StaticY 123 -StaticZ 369 -CollectedBank 3 -CollectedAddress 177 -CollectedMask 0x08 -RequiredBank 3 -RequiredAddress 177 -RequiredMask 0x02 -RequiredValue 0x02 -MaximumGameMoment 1007
Add-Definition -FieldId 246 -FieldName 'blin63_t' -EntityId 6 -EntityName 'CLOUD' -ModelResource '' -Kind 'Named' -Label 'Shaft to floor 63 computer room' -TargetKind 'Location' -StaticX 644 -StaticY -501 -StaticZ 369 -CollectedBank 3 -CollectedAddress 181 -CollectedMask 0x80 -RequiredBank 3 -RequiredAddress 177 -RequiredMask 0x0E -RequiredValue 0x0E -MaximumGameMoment 1007

Add-Definition -FieldId 247 -FieldName 'blin64' -EntityId 28 -EntityName 'KLINE' -ModelResource '' -Kind 'Named' -Label 'Rest area beds' -TargetKind 'Line' -StaticX -923 -StaticY -615 -StaticZ 0
Add-Definition -FieldId 247 -FieldName 'blin64' -EntityId 30 -EntityName 'RLINAHL' -ModelResource '' -Kind 'Named' -Label 'Upper-row locked lockers, left section' -TargetKind 'Line' -StaticX 102 -StaticY 387 -StaticZ 0
Add-Definition -FieldId 247 -FieldName 'blin64' -EntityId 31 -EntityName 'RLINAHR' -ModelResource '' -Kind 'Named' -Label 'Upper-row locked lockers, right section' -TargetKind 'Line' -StaticX 361 -StaticY 387 -StaticZ 0
Add-Definition -FieldId 247 -FieldName 'blin64' -EntityId 33 -EntityName 'RLINBHL' -ModelResource '' -Kind 'Named' -Label 'Middle-row locked lockers, left section' -TargetKind 'Line' -StaticX 130 -StaticY 602 -StaticZ 0
Add-Definition -FieldId 247 -FieldName 'blin64' -EntityId 34 -EntityName 'RLINBHR' -ModelResource '' -Kind 'Named' -Label 'Middle-row locked lockers, right section' -TargetKind 'Line' -StaticX 386 -StaticY 602 -StaticZ 0
Add-Definition -FieldId 247 -FieldName 'blin64' -EntityId 35 -EntityName 'RLINC' -ModelResource '' -Kind 'Named' -Label 'Locker with a megaphone' -TargetKind 'Line' -StaticX 122 -StaticY 830 -StaticZ 0 -MaximumGameMoment 1007
Add-Definition -FieldId 247 -FieldName 'blin64' -EntityId 36 -EntityName 'RLINCHR' -ModelResource '' -Kind 'Named' -Label 'Lower-row locked lockers, right section' -TargetKind 'Line' -StaticX 319 -StaticY 830 -StaticZ 0
Add-Definition -FieldId 247 -FieldName 'blin64' -EntityId 37 -EntityName 'RLINCHL' -ModelResource '' -Kind 'Named' -Label 'Lower-row locked lockers, left section' -TargetKind 'Line' -StaticX 35 -StaticY 830 -StaticZ 0
Add-Definition -FieldId 247 -FieldName 'blin64' -EntityId 38 -EntityName 'LINEW' -ModelResource '' -Kind 'Named' -Label 'Out-of-order facility' -TargetKind 'Line' -StaticX -961 -StaticY 638 -StaticZ 0
Add-Definition -FieldId 247 -FieldName 'blin64' -EntityId 39 -EntityName 'VLINE' -ModelResource '' -Kind 'Named' -Label 'Shinra Gym vending machine' -TargetKind 'Line' -StaticX -438 -StaticY -184 -StaticZ 0 -MaximumGameMoment 1007
Add-Definition -FieldId 247 -FieldName 'blin64' -EntityId 40 -EntityName 'MACHINE' -ModelResource '' -Kind 'Named' -Label 'Exercise machine' -TargetKind 'Line' -StaticX -380 -StaticY -687 -StaticZ 0

# Tifa's piano is niv_ti2's entity 17 LINE (-237,-140,0)-(-238,-359,0), defined by
# its Init on every visit and never switched off (the field has no LINON). Its OK
# handler plays in the flashback (moment < 384), asks dialog 33 on every later
# visit, and holds the Disc 2 rewards: Elemental materia (moment >= 796, 3[9] == 2,
# Tifa leading) and Final Heaven (moment >= 796, Tifa in the party, the melody
# played). Those are the game's own decisions; the piano itself stays reachable.
# It is reached on the engine's own LINE touch, inside the leader's radius.
Add-Definition -FieldId 287 -FieldName 'niv_ti2' -EntityId 17 -EntityName 'piano' -ModelResource '' -Kind 'Named' -Label "Tifa's piano" -TargetKind 'Line' -StaticX -237 -StaticY -249 -StaticZ 0 -UsesPlayerCollisionRadius $true

# Wonder Square's machines (games_2). Each is a model-less LINE whose Init defines it
# unconditionally and never switches it off, so the NPC reader (which needs a model)
# never publishes them. Their OK handler shows the machine's own card (dialogs 3 and
# 24..28: 3D Battler, G Bike, Snow Game, Submarine Game, Fortune Telling, Mog House)
# and asks "Try it". The first-visit Story rows cover five of them for moments
# 440..444; these Objects take over afterwards. The Snow Game plays only from moment
# 790: before that its OK shows a customer's line instead (dialogs 32/33). The Submarine
# Game's own script has no gate, but customer m6 (entity 9) stands solid on its LINE and
# calls it out of order until moment 1299 (Init IFUW 2[0] >= 1299, dialog 31).
Add-Definition -FieldId 507 -FieldName 'games_2' -EntityId 10 -EntityName 'kakul1' -ModelResource '' -Kind 'Named' -Label '3D Battler' -TargetKind 'Line' -StaticX -166 -StaticY -144 -StaticZ 32 -MinimumGameMoment 445 -UsesPlayerCollisionRadius $true
Add-Definition -FieldId 507 -FieldName 'games_2' -EntityId 11 -EntityName 'kakul2' -ModelResource '' -Kind 'Named' -Label '3D Battler, other side' -TargetKind 'Line' -StaticX -110 -StaticY -201 -StaticZ 32 -MinimumGameMoment 445 -UsesPlayerCollisionRadius $true
Add-Definition -FieldId 507 -FieldName 'games_2' -EntityId 18 -EntityName 'mogu' -ModelResource '' -Kind 'Named' -Label 'Mog House' -TargetKind 'Line' -StaticX 3 -StaticY -252 -StaticZ 0 -MinimumGameMoment 445 -UsesPlayerCollisionRadius $true -ActivatesOnOk $true
Add-Definition -FieldId 507 -FieldName 'games_2' -EntityId 19 -EntityName 'bikeg' -ModelResource '' -Kind 'Named' -Label 'G Bike' -TargetKind 'Line' -StaticX 241 -StaticY 174 -StaticZ 0 -MinimumGameMoment 445 -UsesPlayerCollisionRadius $true -ActivatesOnOk $true
Add-Definition -FieldId 507 -FieldName 'games_2' -EntityId 20 -EntityName 'bikeg2' -ModelResource '' -Kind 'Named' -Label 'G Bike, second machine' -TargetKind 'Line' -StaticX 149 -StaticY 145 -StaticZ 0 -UsesPlayerCollisionRadius $true -ActivatesOnOk $true
Add-Definition -FieldId 507 -FieldName 'games_2' -EntityId 21 -EntityName 'snowb' -ModelResource '' -Kind 'Named' -Label 'Snow Game' -TargetKind 'Line' -StaticX -55 -StaticY 278 -StaticZ 0 -MinimumGameMoment 790 -UsesPlayerCollisionRadius $true -ActivatesOnOk $true
Add-Definition -FieldId 507 -FieldName 'games_2' -EntityId 22 -EntityName 'snowb2' -ModelResource '' -Kind 'Named' -Label 'Snow Game, second machine' -TargetKind 'Line' -StaticX -176 -StaticY 177 -StaticZ 0 -MinimumGameMoment 790 -UsesPlayerCollisionRadius $true -ActivatesOnOk $true
Add-Definition -FieldId 507 -FieldName 'games_2' -EntityId 23 -EntityName 'subm' -ModelResource '' -Kind 'Named' -Label 'Submarine Game' -TargetKind 'Line' -StaticX -337 -StaticY 42 -StaticZ 21 -MinimumGameMoment 1299 -UsesPlayerCollisionRadius $true -ActivatesOnOk $true
Add-Definition -FieldId 507 -FieldName 'games_2' -EntityId 24 -EntityName 'la' -ModelResource '' -Kind 'Named' -Label 'Fortune Telling' -TargetKind 'Line' -StaticX 299 -StaticY 153 -StaticZ 0 -MinimumGameMoment 445 -UsesPlayerCollisionRadius $true -ActivatesOnOk $true

# The machines marked -ActivatesOnOk run from their LINE's OK slot (00637D35), which also
# needs the leader facing the line; the 3D Battler and the Basketball Game run on Go (touch).
# Wonder Square's first floor (games_1) hands over the same way. Its four machines are
# model-less LINEs defined by every Init; nothing in games_1 switches a LINE off or tests
# the moment for them. The arm wrestler and the two Wonder Catcher sides run on OK over
# the LINE (LineOk); the basketball LINE's LineGo tests OK itself (IFKEYON 0x0220).
Add-Definition -FieldId 506 -FieldName 'games_1' -EntityId 11 -EntityName 'udel' -ModelResource '' -Kind 'Named' -Label 'Arm Wrestling machine' -TargetKind 'Line' -StaticX 183 -StaticY 1610 -StaticZ -255 -MinimumGameMoment 445 -UsesPlayerCollisionRadius $true -ActivatesOnOk $true
Add-Definition -FieldId 506 -FieldName 'games_1' -EntityId 14 -EntityName 'ufo1' -ModelResource '' -Kind 'Named' -Label 'Wonder Catcher' -TargetKind 'Line' -StaticX 286 -StaticY 1345 -StaticZ -255 -MinimumGameMoment 445 -UsesPlayerCollisionRadius $true -ActivatesOnOk $true
Add-Definition -FieldId 506 -FieldName 'games_1' -EntityId 15 -EntityName 'ufo2' -ModelResource '' -Kind 'Named' -Label 'Wonder Catcher, other side' -TargetKind 'Line' -StaticX 358 -StaticY 1418 -StaticZ -255 -MinimumGameMoment 445 -UsesPlayerCollisionRadius $true -ActivatesOnOk $true
Add-Definition -FieldId 506 -FieldName 'games_1' -EntityId 16 -EntityName 'bsl' -ModelResource '' -Kind 'Named' -Label 'Basketball Game' -TargetKind 'Line' -StaticX -229 -StaticY 1664 -StaticZ -255 -MinimumGameMoment 445 -UsesPlayerCollisionRadius $true

# The Battle Square's two prize windows (coloin1 lent1/lent2): model-less LINEs defined
# on every visit. Their OK says "You currently have battle points" (dialog 14) and,
# with points, offers the exchange (dialog 15) and the prize list for the moment.
Add-Definition -FieldId 500 -FieldName 'coloin1' -EntityId 18 -EntityName 'lent1' -ModelResource '' -Kind 'Named' -Label 'Battle points exchange counter' -TargetKind 'Line' -StaticX 273 -StaticY -3289 -StaticZ -152 -UsesPlayerCollisionRadius $true
Add-Definition -FieldId 500 -FieldName 'coloin1' -EntityId 19 -EntityName 'lent2' -ModelResource '' -Kind 'Named' -Label 'Battle points exchange counter, second window' -TargetKind 'Line' -StaticX -244 -StaticY -3283 -StaticZ -152 -UsesPlayerCollisionRadius $true

# The rocket's control panel beside the Huge Materia (rcktin4 consl, model-less LINE).
# Its OK asks "There is the control panel. Try and operate it?" (dialog 166) until the
# attempt sets bank 3 byte 134 bit 5; Cid's hints and the passcode are the game's own.
Add-Definition -FieldId 566 -FieldName 'rcktin4' -EntityId 7 -EntityName 'consl' -ModelResource '' -Kind 'Named' -Label 'Control panel' -TargetKind 'Line' -StaticX -1 -StaticY -120 -StaticZ 0 -CollectedBank 3 -CollectedAddress 134 -CollectedMask 0x20 -UsesPlayerCollisionRadius $true

# Mideel's weapon-shop back door (itown_w line02) and the spot on the street where the
# old key is stuck (itown1a oldkey, entity 17). Entity 18 shares that LINE: its GoOnce
# (s5) plays the clink sound (SOUND 0x011D) when the leader first steps on it, the audible
# cue that there is something to examine; entity 17's OK does the examining. Both LINEs
# run the leading party member's own scripts, so the NPC reader only knows them as the
# counter of a party model it never offers. The door answers on every visit: locked
# (dialogs 9/10, setting 15[178] bit 0), the key choice once held, then the owner.
# Before the door has been tried, the street spot only describes the stuck key
# (dialogs 32/34); after it, the key comes out (15[177] bit 6), and both entities' Init
# switch the LINE off from then on. Those outcomes stay the game's own.
Add-Definition -FieldId 717 -FieldName 'itown_w' -EntityId 12 -EntityName 'line02' -ModelResource '' -Kind 'Named' -Label 'Weapon shop back door' -TargetKind 'Line' -StaticX 109 -StaticY 192 -StaticZ 0 -UsesPlayerCollisionRadius $true
Add-Definition -FieldId 712 -FieldName 'itown1a' -EntityId 17 -EntityName 'oldkey' -ModelResource '' -Kind 'Named' -Label 'Something to examine' -TargetKind 'Line' -StaticX -941 -StaticY -818 -StaticZ 390 -CollectedBank 15 -CollectedAddress 177 -CollectedMask 0x40 -UsesPlayerCollisionRadius $true

# The Honey Bee Inn's room doors (onna_4). border5/border6 are occupied rooms: OK asks
# "Take a listen / Take a peek" (dialog 18), and peeking plays onna_5. border7/border8
# are free rooms: OK offers to take the room and opens its door (IDLCK 28/19 released).
# Their Init switches the free-room LINEs off once a room has been chosen.
Add-Definition -FieldId 218 -FieldName 'onna_4' -EntityId 17 -EntityName 'border5' -ModelResource '' -Kind 'Named' -Label 'Occupied room door, 1 of 2' -TargetKind 'Line' -StaticX 261 -StaticY 138 -StaticZ 26 -UsesPlayerCollisionRadius $true
Add-Definition -FieldId 218 -FieldName 'onna_4' -EntityId 18 -EntityName 'border6' -ModelResource '' -Kind 'Named' -Label 'Occupied room door, 2 of 2' -TargetKind 'Line' -StaticX 261 -StaticY -145 -StaticZ 26 -UsesPlayerCollisionRadius $true
Add-Definition -FieldId 218 -FieldName 'onna_4' -EntityId 19 -EntityName 'border7' -ModelResource '' -Kind 'Named' -Label 'Free room door, 1 of 2' -TargetKind 'Line' -StaticX -260 -StaticY 149 -StaticZ 26 -UsesPlayerCollisionRadius $true
Add-Definition -FieldId 218 -FieldName 'onna_4' -EntityId 20 -EntityName 'border8' -ModelResource '' -Kind 'Named' -Label 'Free room door, 2 of 2' -TargetKind 'Line' -StaticX -269 -StaticY -151 -StaticZ 26 -UsesPlayerCollisionRadius $true

# Shop counters whose buy menu (MENU 8) opens only from a model-less counter LINE and
# neither from the shopkeeper's own Talk nor through any NPC row's counter. In the Under
# Junon weapon store (ujun_w) the shopkeeper's Talk is only "Hmm, what?" (dialog 1); the
# counter wswelcm opens shop 18 and is attached to no shopkeeper. The logged 2026-09-26
# visit talked to him four times, found no objects and left without the shop. Costa del
# Sol's second stall (del2 border2) opens the materia shop through its owner's script 3,
# which his Talk never runs. Elsewhere the shopkeeper's Talk opens the shop (Sector 7,
# Costa's souvenir stall, Mideel) or an NPC row already carries the counter (Icicle Inn's
# welcom1, North Corel), so those are not repeated here.
Add-Definition -FieldId 432 -FieldName 'ujun_w' -EntityId 2 -EntityName 'wswelcm' -ModelResource '' -Kind 'Named' -Label 'Weapon shop counter' -TargetKind 'Line' -StaticX 138 -StaticY 343 -StaticZ 0 -UsesPlayerCollisionRadius $true
Add-Definition -FieldId 443 -FieldName 'del2' -EntityId 6 -EntityName 'border2' -ModelResource '' -Kind 'Named' -Label 'Materia shop counter' -TargetKind 'Line' -StaticX 662 -StaticY 1849 -StaticZ 0 -UsesPlayerCollisionRadius $true

# Examinable LINEs from the full-guide text audit. Each has no model, so no NPC row can
# carry it; each gives the player a native response on OK. Live LINE enable gates them,
# and the game's own conditions decide what is said.
#   niv_ti2 tansu: during the flashback (moment < 384) the drawer in Tifa's room answers
#     once (dialogs 3..5, ASK "It's true / Just kidding", sets 3[19] bit 5); control is
#     the player's. The guide: "go into her room on the left.. and check the drawer".
#   cosin2 LINES1: from moment 514, "It seems like the only one that can open it is
#     Bugenhagen" at the sealed door (the Story row's "sealed door").
#   gaiin_7 cure1..3: three sides of one spot; each restores HP/MP (opcode 0x3E, dialog 2
#     "HP/MP restored!"). The guide: "head back to heal up/save". One side is offered.
#   losin3 line1/line2 and losinn line3: the Forgotten City spots where Cloud hears the
#     Ancients (dialogs 12..26, flags 3[131] bit 7, 3[132] bits 0/1); OK only while the
#     leader faces them. losin3's two LINEs are two faces of one spot; one is offered.
#   zz1 l1: the sleeping man's cave; OK there makes him mutter (dialog 2).
Add-Definition -FieldId 287 -FieldName 'niv_ti2' -EntityId 5 -EntityName 'tansu' -ModelResource '' -Kind 'Named' -Label "Drawer in Tifa's room" -TargetKind 'Line' -StaticX -99 -StaticY 213 -StaticZ 0 -CollectedBank 3 -CollectedAddress 19 -CollectedMask 0x20 -MaximumGameMoment 383 -UsesPlayerCollisionRadius $true
Add-Definition -FieldId 531 -FieldName 'cosin2' -EntityId 16 -EntityName 'LINES1' -ModelResource '' -Kind 'Named' -Label 'Sealed door' -TargetKind 'Line' -StaticX -97 -StaticY 349 -StaticZ -1 -MinimumGameMoment 514 -UsesPlayerCollisionRadius $true
Add-Definition -FieldId 699 -FieldName 'gaiin_7' -EntityId 5 -EntityName 'cure3' -ModelResource '' -Kind 'Named' -Label 'Healing spot' -TargetKind 'Line' -StaticX 48 -StaticY 166 -StaticZ 0 -UsesPlayerCollisionRadius $true
Add-Definition -FieldId 633 -FieldName 'losin3' -EntityId 3 -EntityName 'line1' -ModelResource '' -Kind 'Named' -Label 'Something to examine' -TargetKind 'Line' -StaticX 57 -StaticY 266 -StaticZ 0 -UsesPlayerCollisionRadius $true
Add-Definition -FieldId 636 -FieldName 'losinn' -EntityId 6 -EntityName 'line3' -ModelResource '' -Kind 'Named' -Label 'Something to examine' -TargetKind 'Line' -StaticX 224 -StaticY -387 -StaticZ 67 -UsesPlayerCollisionRadius $true
Add-Definition -FieldId 78 -FieldName 'zz1' -EntityId 5 -EntityName 'l1' -ModelResource '' -Kind 'Named' -Label 'Something to examine' -TargetKind 'Line' -StaticX 41 -StaticY 499 -StaticZ -12 -UsesPlayerCollisionRadius $true

# Bone Village reuses one chest entity for the current excavation reward.
# Bank 1 address 234 contains reward slots 1 through 9 and returns to zero when inactive.
for ($rewardSlot = 1; $rewardSlot -le 9; $rewardSlot++) {
    Add-Definition `
        -FieldId 617 -FieldName 'bonevil' -EntityId 13 -EntityName 'box1' `
        -ModelResource 'bonevilfieldbg_trb_mety.char' -Kind 'Named' -Label 'Excavation treasure chest' `
        -RequiredBank 1 -RequiredAddress 234 -RequiredMask 0xFF -RequiredValue $rewardSlot
}

# The Ancient Forest reuses one box model for five native branches. Each branch
# has its own activation bit and persistent collection bit.
Add-Definition -FieldId 609 -FieldName 'kuro_6' -EntityId 7 -EntityName 'box' -ModelResource 'kuro_6fieldbg_trbox_k.char' -Kind 'Named' -Label 'Battle chest' -CollectedBank 15 -CollectedAddress 112 -CollectedMask 0x10 -RequiredBank 3 -RequiredAddress 230 -RequiredMask 0x01 -RequiredValue 0x01
Add-Definition -FieldId 609 -FieldName 'kuro_6' -EntityId 7 -EntityName 'box' -ModelResource 'kuro_6fieldbg_trbox_k.char' -Kind 'Named' -Label 'Battle chest' -CollectedBank 15 -CollectedAddress 112 -CollectedMask 0x20 -RequiredBank 3 -RequiredAddress 230 -RequiredMask 0x04 -RequiredValue 0x04
Add-Definition -FieldId 609 -FieldName 'kuro_6' -EntityId 7 -EntityName 'box' -ModelResource 'kuro_6fieldbg_trbox_k.char' -Kind 'Item' -NativeId 200 -CollectedBank 15 -CollectedAddress 112 -CollectedMask 0x40 -RequiredBank 3 -RequiredAddress 230 -RequiredMask 0x08 -RequiredValue 0x08
Add-Definition -FieldId 609 -FieldName 'kuro_6' -EntityId 7 -EntityName 'box' -ModelResource 'kuro_6fieldbg_trbox_k.char' -Kind 'Item' -NativeId 237 -CollectedBank 15 -CollectedAddress 112 -CollectedMask 0x80 -RequiredBank 3 -RequiredAddress 230 -RequiredMask 0x20 -RequiredValue 0x20
Add-Definition -FieldId 609 -FieldName 'kuro_6' -EntityId 7 -EntityName 'box' -ModelResource 'kuro_6fieldbg_trbox_k.char' -Kind 'Item' -NativeId 6 -CollectedBank 15 -CollectedAddress 113 -CollectedMask 0x01 -RequiredBank 3 -RequiredAddress 230 -RequiredMask 0x40 -RequiredValue 0x40

$deduplicated = @($definitions |
    Group-Object { "$($_.fieldId):$($_.entityId):$($_.kind):$($_.nativeId):$($_.label):$($_.targetKind):$($_.staticX):$($_.staticY):$($_.staticZ):$($_.requiredBank):$($_.requiredAddress):$($_.requiredMask):$($_.requiredValue)" } |
    ForEach-Object { $_.Group[0] })

# Keep established J/L cycling order stable when the catalog is regenerated.
# Existing definitions retain their prior positions; newly reviewed objects are
# appended in generator order so a focused addition cannot reorder every field.
function Get-DefinitionKey($definition) {
    return "$($definition.fieldId):$($definition.entityId):$($definition.kind):$($definition.nativeId):$($definition.label):$($definition.targetKind):$($definition.staticX):$($definition.staticY):$($definition.staticZ):$($definition.requiredBank):$($definition.requiredAddress):$($definition.requiredMask):$($definition.requiredValue)"
}

$rankByKey = @{}
$nextRank = 0
if (Test-Path -LiteralPath $OutputPath) {
    try {
        $existingDocument = Get-Content -LiteralPath $OutputPath -Raw | ConvertFrom-Json
        foreach ($definition in $existingDocument.definitions) {
            $key = Get-DefinitionKey $definition
            if (-not $rankByKey.ContainsKey($key)) {
                $rankByKey[$key] = $nextRank
                $nextRank++
            }
        }
    }
    catch {
        Write-Warning "Could not preserve existing object target order: $($_.Exception.Message)"
    }
}
foreach ($definition in $deduplicated) {
    $key = Get-DefinitionKey $definition
    if (-not $rankByKey.ContainsKey($key)) {
        $rankByKey[$key] = $nextRank
        $nextRank++
    }
}
$deduplicated = @($deduplicated | Sort-Object {
    $rankByKey[(Get-DefinitionKey $_)]
})

$sourceCommit = (git -C $KujataDataRoot rev-parse HEAD).Trim()
$document = [ordered]@{
    schemaVersion = 2
    source = 'dangarfeld/kujata-data field script extraction'
    sourceCommit = $sourceCommit
    definitionCount = $deduplicated.Count
    definitions = $deduplicated
}

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$document | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Host "Generated $($deduplicated.Count) FFVII field navigation objects at $OutputPath"
