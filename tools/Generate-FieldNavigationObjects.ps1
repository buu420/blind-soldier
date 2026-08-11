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
if (-not $MapListPath) {
    $MapListPath = Join-Path $GameRoot 'data\field\flevel\maplist'
}
if (-not (Test-Path -LiteralPath $fieldJsonRoot)) {
    throw "Missing Kujata field data: $fieldJsonRoot"
}
if (-not (Test-Path -LiteralPath $MapListPath)) {
    throw "Missing FFVII map list: $MapListPath"
}

$mapBytes = [IO.File]::ReadAllBytes($MapListPath)
$fieldCount = [BitConverter]::ToUInt16($mapBytes, 0)
$fieldIds = @{}
for ($fieldId = 0; $fieldId -lt $fieldCount; $fieldId++) {
    $offset = 2 + $fieldId * 32
    $fieldName = [Text.Encoding]::ASCII.GetString($mapBytes, $offset, 32).Split([char]0)[0].Trim()
    if ($fieldName) {
        $fieldIds[$fieldName] = $fieldId
    }
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
    [pscustomobject]@{ FieldName = 'ncoin1'; EntityId = 3; ScriptType = '[OK]'; ExpectedPickups = @('STITM:3:1'); CueKind = 'Item'; CollectedBank = 15; CollectedAddress = 1; CollectedMask = 0x01; RequiredBank = -1; RequiredAddress = -1; RequiredMask = 0; RequiredValue = 0; MinimumGameMoment = -1; MaximumGameMoment = -1 }
)

# A few visible pickup models use scripted contact/jump sequences instead of a
# Talk handler. They still expose native model position and visibility at runtime.
$directModelPickupSpecs = @(
    [pscustomobject]@{ FieldName = 'las3_3'; EntityId = 5; ScriptType = 'Script 3'; ExpectedPickup = 'SMTRA:12:1'; CollectedBank = 1; CollectedAddress = 50; CollectedMask = 0x10 },
    [pscustomobject]@{ FieldName = 'mtcrl_5'; EntityId = 5; ScriptType = 'Script 3'; ExpectedPickup = 'STITM:298:1'; CollectedBank = 15; CollectedAddress = 115; CollectedMask = 0x04 },
    [pscustomobject]@{ FieldName = 'mtcrl_5'; EntityId = 6; ScriptType = 'Script 3'; ExpectedPickup = 'STITM:196:1'; CollectedBank = 15; CollectedAddress = 115; CollectedMask = 0x08 }
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
        [int] $MaximumGameMoment = -1
    )

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
            ($fieldName -eq 'kuro_6' -and $entity.entityName -eq 'box')) {
            continue
        }

        if ($modelResource -match 'fieldbg_saveicn') {
            Add-Definition $fieldIds[$fieldName] $fieldName $entity.entityId $entity.entityName $modelResource 'SavePoint' -1 'Save Point'
            continue
        }

        if ($modelResource -notmatch 'fieldbg_') {
            continue
        }

        $directSpec = $directModelPickupSpecs |
            Where-Object { $_.FieldName -eq $fieldName -and $_.EntityId -eq $entity.entityId } |
            Select-Object -First 1
        if ($null -ne $directSpec) {
            $pickupScript = $entity.scripts |
                Where-Object { $_.scriptType -eq $directSpec.ScriptType } |
                Select-Object -First 1
            $pickups = @($pickupScript.ops | Where-Object { $_.op -in @('STITM', 'SMTRA') })
            if ($null -eq $pickupScript -or $pickups.Count -ne 1) {
                throw "Missing direct model pickup script for ${fieldName}:$($entity.entityId)"
            }

            $pickup = $pickups[0]
            $quantity = if ($pickup.op -eq 'STITM') { [Math]::Max(1, [int]$pickup.a) } else { 1 }
            $actualPickup = "$($pickup.op):$($pickup.t):$quantity"
            if ($actualPickup -ne $directSpec.ExpectedPickup) {
                throw "Native direct model pickup drift for ${fieldName}:$($entity.entityId): expected $($directSpec.ExpectedPickup), found $actualPickup"
            }

            Add-Definition `
                -FieldId $fieldIds[$fieldName] -FieldName $fieldName -EntityId $entity.entityId `
                -EntityName $entity.entityName -ModelResource $modelResource `
                -Kind $(if ($pickup.op -eq 'SMTRA') { 'Materia' } else { 'Item' }) `
                -NativeId ([int]$pickup.t) -Quantity $quantity `
                -CollectedBank $directSpec.CollectedBank -CollectedAddress $directSpec.CollectedAddress `
                -CollectedMask $directSpec.CollectedMask
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

        Add-Definition `
            -FieldId $fieldIds[$fieldName] -FieldName $fieldName -EntityId $entity.entityId `
            -EntityName $entity.entityName -ModelResource $modelResource -Kind $kind `
            -NativeId $nativeId -Label $label -Quantity $quantity `
            -CollectedBank $collectedBank -CollectedAddress $collectedAddress `
            -CollectedMask $collectedMask -MinimumGameMoment $minimumGameMoment `
            -MaximumGameMoment $maximumGameMoment
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
                -MaximumGameMoment $spec.MaximumGameMoment
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
# them explicitly so the model-resource filter cannot hide them.
Add-Definition -FieldId 144 -FieldName 'mds7st1' -EntityId 16 -EntityName 'doram' -ModelResource 'mds7st1std_man1.char' -Kind 'Item' -NativeId 1 -CollectedBank 1 -CollectedAddress 36 -CollectedMask 0x01
Add-Definition -FieldId 145 -FieldName 'mds7st2' -EntityId 20 -EntityName 'doram' -ModelResource 'mds7st2std_man1.char' -Kind 'Item' -NativeId 3 -CollectedBank 1 -CollectedAddress 36 -CollectedMask 0x08
Add-Definition -FieldId 224 -FieldName 'wcrimb_2' -EntityId 11 -EntityName 'line90' -ModelResource '' -Kind 'Named' -Label 'Optional battery socket' -TargetKind 'Line' -StaticX -260 -StaticY 972 -StaticZ 2588 -CollectedBank 1 -CollectedAddress 165 -CollectedMask 0x10 -RequiredBank 1 -RequiredAddress 165 -RequiredMask 0x80 -RequiredValue 0x80

# Sector 5's town contains several visible, actionable fixtures that are not
# inventory pickups. Keep their native model or LINE identity so they follow
# visibility/LINON state instead of becoming unconditional static landmarks.
Add-Definition -FieldId 174 -FieldName 'min51_1' -EntityId 7 -EntityName 'TV' -ModelResource '5min1_1midgal_avaman.char' -Kind 'Named' -Label 'Television'
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

# Floor 63's optimal three-door solution follows the native D2 -> D4 -> D12
# state bits. D3 is the first door below the top corridor and must be skipped;
# D4's action side remains reachable after D2 without crossing another locked
# boundary, then its room leads directly to A Coupon.
Add-Definition -FieldId 245 -FieldName 'blin63_1' -EntityId 15 -EntityName 'MLINE' -ModelResource '' -Kind 'Named' -Label 'Floor 63 door-control and coupon-exchange computer' -TargetKind 'Line' -StaticX 920 -StaticY -570 -StaticZ 0
Add-Definition -FieldId 245 -FieldName 'blin63_1' -EntityId -1 -EntityName 'D2' -ModelResource '' -Kind 'Named' -Label 'Coupon route door 1 of 3, top corridor' -TargetKind 'Location' -StaticX 414 -StaticY 972 -StaticZ 0 -CollectedBank 3 -CollectedAddress 174 -CollectedMask 0x02 -RequiredBank 3 -RequiredAddress 177 -RequiredMask 0x10 -RequiredValue 0x10 -MaximumGameMoment 1007
Add-Definition -FieldId 245 -FieldName 'blin63_1' -EntityId -1 -EntityName 'D4' -ModelResource '' -Kind 'Named' -Label 'Coupon route door 2 of 3, second door below the top corridor' -TargetKind 'Location' -StaticX -549 -StaticY 752 -StaticZ 0 -CollectedBank 3 -CollectedAddress 174 -CollectedMask 0x08 -RequiredBank 3 -RequiredAddress 174 -RequiredMask 0x02 -RequiredValue 0x02 -MaximumGameMoment 1007
Add-Definition -FieldId 245 -FieldName 'blin63_1' -EntityId -1 -EntityName 'D12' -ModelResource '' -Kind 'Named' -Label 'Coupon route door 3 of 3, between B and C Coupon rooms' -TargetKind 'Location' -StaticX 10 -StaticY -42 -StaticZ 0 -CollectedBank 3 -CollectedAddress 175 -CollectedMask 0x08 -RequiredBank 3 -RequiredAddress 177 -RequiredMask 0x08 -RequiredValue 0x08 -MaximumGameMoment 1007
Add-Definition -FieldId 245 -FieldName 'blin63_1' -EntityId 42 -EntityName 'TAKARA1' -ModelResource 'blin63_1fieldbg_zuta_orig.char' -Kind 'Named' -Label 'A Coupon' -CollectedBank 3 -CollectedAddress 177 -CollectedMask 0x02 -RequiredBank 3 -RequiredAddress 174 -RequiredMask 0x08 -RequiredValue 0x08 -MaximumGameMoment 1007
Add-Definition -FieldId 245 -FieldName 'blin63_1' -EntityId 43 -EntityName 'TAKARA2' -ModelResource 'blin63_1fieldbg_zuta_orig.char' -Kind 'Named' -Label 'C Coupon' -CollectedBank 3 -CollectedAddress 177 -CollectedMask 0x04 -RequiredBank 3 -RequiredAddress 175 -RequiredMask 0x08 -RequiredValue 0x08 -MaximumGameMoment 1007
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
    ForEach-Object { $_.Group[0] } |
    Sort-Object fieldId, entityId, kind, nativeId, label)

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
