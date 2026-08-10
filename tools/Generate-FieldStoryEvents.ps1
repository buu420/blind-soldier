param(
    [string] $GameRoot = '',
    [string] $KujataDataRoot = '',
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
    $OutputPath = Join-Path $scriptRoot '..\Ff7.Accessibility.Reloaded\Assets\navigation\field_story_events.json'
}

$fieldJsonRoot = Join-Path $KujataDataRoot 'data\field\flevel.lgp'
$chaptersPath = Join-Path $KujataDataRoot 'metadata\chapters.json'
$mapListPath = Join-Path $GameRoot 'data\field\flevel\maplist'
$mapListJsonPath = Join-Path $fieldJsonRoot 'maplist.json'
if (-not (Test-Path -LiteralPath $fieldJsonRoot)) {
    throw "Missing Kujata field data: $fieldJsonRoot"
}
if (-not (Test-Path -LiteralPath $chaptersPath)) {
    throw "Missing Kujata chapter metadata: $chaptersPath"
}
$fieldIds = @{}
if (Test-Path -LiteralPath $mapListPath) {
    $mapBytes = [IO.File]::ReadAllBytes($mapListPath)
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
    throw "Missing FFVII map list: $mapListPath and $mapListJsonPath"
}

$chapterByField = @{}
$chapters = Get-Content -Raw -LiteralPath $chaptersPath | ConvertFrom-Json
foreach ($chapter in $chapters) {
    foreach ($fieldName in $chapter.fieldNames) {
        if (-not $chapterByField.ContainsKey([string]$fieldName)) {
            $chapterByField[[string]$fieldName] = [string]$chapter.name
        }
    }
}

$definitions = [Collections.Generic.List[object]]::new()
$milestoneCount = 0
$navigableMilestoneCount = 0
$unresolved = [Collections.Generic.List[string]]::new()

function New-Condition {
    param(
        [int] $Bank,
        [int] $Address,
        [int] $Mask,
        [int] $Value,
        [switch] $AnyBitSet
    )

    $condition = [ordered]@{ bank = $Bank; address = $Address; mask = $Mask; value = $Value }
    if ($AnyBitSet) {
        $condition.anyBitSet = $true
    }
    return $condition
}

function Add-Definition {
    param(
        [int] $FieldId,
        [string] $FieldName,
        [string] $Kind,
        [string] $Label,
        [int] $EntityId = -1,
        [int] $X = 0,
        [int] $Y = 0,
        [int] $Z = 0,
        [int] $TargetGameMoment = -1,
        [int] $MinimumGameMoment = -1,
        [int] $MaximumGameMoment = -1,
        [int] $Priority = 100,
        [object] $RequiredCondition = $null,
        [object[]] $RequiredConditions = @(),
        [object] $CompletedCondition = $null,
        [string] $EntityName = '',
        [string] $ScriptType = '',
        [object] $TriggerLine = $null,
        [object] $RouteDetour = $null,
        [object[]] $RouteDetours = @(),
        [int[]] $RequiredPlayerTriangles = @(),
        [int[]] $ExcludedPlayerTriangles = @(),
        [switch] $KeepActiveOnArrival
    )

    $definition = [ordered]@{
        fieldId = $FieldId
        kind = $Kind
        label = $Label
        entityId = $EntityId
        x = $X
        y = $Y
        z = $Z
        targetGameMoment = $TargetGameMoment
        minimumGameMoment = $MinimumGameMoment
        maximumGameMoment = $MaximumGameMoment
        priority = $Priority
        sourceFieldName = $FieldName
        sourceEntityName = $EntityName
        sourceScriptType = $ScriptType
        completesOnArrival = -not $KeepActiveOnArrival.IsPresent
    }
    if ($null -ne $RequiredCondition) {
        $definition.requiredCondition = $RequiredCondition
    }
    if ($RequiredConditions.Count -gt 0) {
        $definition.requiredConditions = @($RequiredConditions)
    }
    if ($null -ne $CompletedCondition) {
        $definition.completedCondition = $CompletedCondition
    }
    if ($null -ne $TriggerLine) {
        $definition.triggerLine = $TriggerLine
    }
    if ($null -ne $RouteDetour) {
        $definition.routeDetour = $RouteDetour
    }
    if ($RouteDetours.Count -gt 0) {
        $definition.routeDetours = @($RouteDetours)
    }
    if ($RequiredPlayerTriangles.Count -gt 0) {
        $definition.requiredPlayerTriangles = @($RequiredPlayerTriangles)
    }
    if ($ExcludedPlayerTriangles.Count -gt 0) {
        $definition.excludedPlayerTriangles = @($ExcludedPlayerTriangles)
    }
    $definitions.Add($definition)
}

function Test-ActivationScript {
    param([object] $Entity, [object] $Script)
    if ($Entity.entityType -eq 'Model' -and $Script.scriptType -in @('Talk', 'Contact')) {
        return $true
    }
    return $Entity.entityType -eq 'Line' -and
        ([string]$Script.scriptType -match '^(Move|Go|\[OK\])')
}

function Get-ActivationNodes {
    param([object] $Field, [object] $SourceEntity, [object] $SourceScript)

    $reverseCalls = @{}
    foreach ($callerEntity in $Field.script.entities) {
        foreach ($callerScript in $callerEntity.scripts) {
            foreach ($operation in $callerScript.ops | Where-Object { $_.op -in @('REQ', 'REQSW', 'REQEW') }) {
                $key = "$([int]$operation.e):$([int]$operation.f)"
                if (-not $reverseCalls.ContainsKey($key)) {
                    $reverseCalls[$key] = [Collections.Generic.List[object]]::new()
                }
                $reverseCalls[$key].Add([pscustomobject]@{ Entity = $callerEntity; Script = $callerScript })
            }
        }
    }

    $queue = [Collections.Generic.Queue[object]]::new()
    $queue.Enqueue([pscustomobject]@{ Entity = $SourceEntity; Script = $SourceScript; Depth = 0 })
    $seen = @{}
    $matches = [Collections.Generic.List[object]]::new()
    $matchDepth = [int]::MaxValue
    while ($queue.Count -gt 0) {
        $node = $queue.Dequeue()
        if ($node.Depth -gt $matchDepth) {
            continue
        }
        $nodeKey = "$([int]$node.Entity.entityId):$([int]$node.Script.index):$([string]$node.Script.scriptType)"
        if ($seen.ContainsKey($nodeKey)) {
            continue
        }
        $seen[$nodeKey] = $true

        if (Test-ActivationScript $node.Entity $node.Script) {
            $matchDepth = $node.Depth
            $matches.Add($node)
            continue
        }

        $targetKey = "$([int]$node.Entity.entityId):$([int]$node.Script.index)"
        if (-not $reverseCalls.ContainsKey($targetKey)) {
            continue
        }
        foreach ($caller in $reverseCalls[$targetKey]) {
            $queue.Enqueue([pscustomobject]@{
                Entity = $caller.Entity
                Script = $caller.Script
                Depth = $node.Depth + 1
            })
        }
    }
    return @($matches)
}

function Get-DialogSpeaker {
    param([object[]] $Scripts, [string] $EntityName)
    foreach ($script in $Scripts) {
        foreach ($operation in $script.ops | Where-Object { $_.op -in @('MESSAGE', 'ASK') -and $_.js }) {
            $match = [regex]::Match([string]$operation.js, '//\s*(?:\{(?<braced>[^}]+)\}|(?<plain>[^<\r\n]+))<br/>')
            if (-not $match.Success) {
                continue
            }
            $isBracedSpeaker = $match.Groups['braced'].Success
            $speaker = if ($isBracedSpeaker) {
                $match.Groups['braced'].Value
            } else {
                $match.Groups['plain'].Value
            }
            $speaker = $speaker.Trim(' ', '"', "'", [char]0x201c, [char]0x201d)
            if ($isBracedSpeaker -and
                $speaker -notmatch '^(Cloud|Barret|Tifa|Aeris|Aerith|Red XIII|Nanaki|Yuffie|Cait Sith|Vincent|Cid|Sephiroth)$') {
                continue
            }
            if (-not $isBracedSpeaker -and
                ($speaker.Length -gt 24 -or $speaker -notmatch "^[A-Za-z][A-Za-z0-9 '\-]+$")) {
                continue
            }
            if ($speaker -and $speaker -notmatch '^(CHOICE|Cloud)$') {
                return $speaker
            }
        }
    }

    $aliases = @{
        av_b = 'Biggs'; av_j = 'Jessie'; ba = 'Barret'; ti = 'Tifa';
        earith = 'Aeris'; aerith = 'Aeris'; cid = 'Cid'; yufi = 'Yuffie';
        vincent = 'Vincent'; red = 'Red XIII'; nanaki = 'Nanaki'; ketc = 'Cait Sith'
    }
    if ($aliases.ContainsKey($EntityName)) {
        return $aliases[$EntityName]
    }
    return ''
}

function Get-LineLocation {
    param([object] $Entity)
    $init = $Entity.scripts | Where-Object { $_.scriptType -eq 'Init' } | Select-Object -First 1
    $line = $init.ops | Where-Object { $_.op -eq 'LINE' } | Select-Object -First 1
    if ($null -eq $line) {
        return $null
    }
    return [pscustomobject]@{
        X = [int](($line.x1 + $line.x2) / 2)
        Y = [int](($line.y1 + $line.y2) / 2)
        Z = [int](($line.z1 + $line.z2) / 2)
    }
}

foreach ($file in Get-ChildItem -LiteralPath $fieldJsonRoot -Filter '*.json') {
    $fieldName = $file.BaseName
    if (-not $fieldIds.ContainsKey($fieldName)) {
        continue
    }

    $field = Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json
    foreach ($sourceEntity in $field.script.entities) {
        foreach ($sourceScript in $sourceEntity.scripts) {
            $milestones = @($sourceScript.ops | Where-Object {
                $_.op -eq 'SETWORD' -and
                [int]$_.bd -eq 2 -and
                [int]$_.bs -eq 0 -and
                [int]$_.a -eq 0
            })
            foreach ($milestone in $milestones) {
                $milestoneCount++
                $targetMoment = [int]$milestone.v
                if (($fieldName -eq 'nmkin_1' -and $targetMoment -eq 11) -or
                    ($fieldName -eq 'nmkin_3' -and $targetMoment -eq 14) -or
                    ($fieldName -eq 'nmkin_5' -and $targetMoment -in @(15, 27)) -or
                    ($fieldName -eq 'chrin_3b' -and $targetMoment -eq 155)) {
                    continue
                }

                $activationNodes = @(Get-ActivationNodes $field $sourceEntity $sourceScript)
                if ($activationNodes.Count -eq 0) {
                    $unresolved.Add("${fieldName}:$($sourceEntity.entityName):$($sourceScript.scriptType):$targetMoment")
                    continue
                }

                foreach ($activation in $activationNodes) {
                    $entity = $activation.Entity
                    $script = $activation.Script
                    $chapterName = if ($chapterByField.ContainsKey($fieldName)) {
                        $chapterByField[$fieldName]
                    } else {
                        'the story'
                    }
                    if ($entity.entityType -eq 'Model') {
                        $speaker = Get-DialogSpeaker @($script, $sourceScript) ([string]$entity.entityName)
                        $label = if ($script.scriptType -eq 'Contact') {
                            if ($speaker) { "Approach $speaker to continue" } else { "Approach the story character" }
                        } else {
                            if ($speaker) { "Talk to $speaker to continue" } else { "Talk to the story character" }
                        }
                        Add-Definition `
                            -FieldId $fieldIds[$fieldName] -FieldName $fieldName -Kind 'Model' `
                            -Label $label -EntityId $entity.entityId -TargetGameMoment $targetMoment `
                            -EntityName $entity.entityName -ScriptType $script.scriptType
                        $navigableMilestoneCount++
                        continue
                    }

                    $location = Get-LineLocation $entity
                    if ($null -eq $location) {
                        continue
                    }
                    Add-Definition `
                        -FieldId $fieldIds[$fieldName] -FieldName $fieldName -Kind 'Location' `
                        -Label "Continue $chapterName" -X $location.X -Y $location.Y -Z $location.Z `
                        -TargetGameMoment $targetMoment -EntityName $entity.entityName -ScriptType $script.scriptType
                    $navigableMilestoneCount++
                }
            }
        }
    }
}

# The opening station's first two controllable objectives are driven by
# Director/Main polling of Cloud's native walkmesh triangle rather than by a
# Talk/Contact/LINE activation script, so the generic reverse-call extraction
# above intentionally cannot discover them. The target actors below are the
# exact visible models standing at those native triggers: Barret in md1stin
# while GameMoment 1 advances to 6, and the Avalanche model placed on triangle
# 62 in md1_1 while GameMoment 6 advances to 7.
Add-Definition -FieldId 116 -FieldName 'md1stin' -Kind 'Model' -Label 'Follow Barret' -EntityId 2 -TargetGameMoment 6 -MinimumGameMoment 1 -MaximumGameMoment 5 -Priority 0 -EntityName 'ba' -ScriptType 'Main'
Add-Definition -FieldId 117 -FieldName 'md1_1' -Kind 'Model' -Label 'Approach Avalanche' -EntityId 4 -TargetGameMoment 7 -MinimumGameMoment 6 -MaximumGameMoment 6 -Priority 0 -EntityName 'av_l' -ScriptType 'Main'

# Reactor 1 has local door and rescue flags that must be completed before the
# next global story moment can fire. These objectives come directly from the
# nmkin_1, elevtr1, nmkin_3, and nmkin_5 field scripts.
Add-Definition -FieldId 120 -FieldName 'nmkin_1' -Kind 'Model' -Label 'Talk to Biggs to unlock the first security door' -EntityId 9 -MaximumGameMoment 26 -Priority 0 -CompletedCondition (New-Condition 1 225 0x08 0x08) -EntityName 'av_b' -ScriptType 'Talk'
Add-Definition -FieldId 120 -FieldName 'nmkin_1' -Kind 'Model' -Label 'Talk to Jessie to unlock the second security door' -EntityId 10 -MaximumGameMoment 26 -Priority 1 -RequiredCondition (New-Condition 1 225 0x08 0x08) -CompletedCondition (New-Condition 1 225 0x10 0x10) -EntityName 'av_j' -ScriptType 'Talk'
Add-Definition -FieldId 120 -FieldName 'nmkin_1' -Kind 'Location' -Label 'Approach Barret and Avalanche' -X -704 -Y 2166 -Z -274 -TargetGameMoment 11 -RequiredCondition (New-Condition 1 225 0x18 0x18) -EntityName 'evb' -ScriptType 'Move'
Add-Definition -FieldId 120 -FieldName 'nmkin_1' -Kind 'Location' -Label 'Press the walkway door button' -X -1699 -Y 4400 -Z -273 -TargetGameMoment 12 -MinimumGameMoment 11 -MaximumGameMoment 11 -RequiredCondition (New-Condition 1 225 0x18 0x18) -CompletedCondition (New-Condition 5 2 0x01 0x01) -EntityName 'drE' -ScriptType 'Go'
Add-Definition -FieldId 120 -FieldName 'nmkin_1' -Kind 'Location' -Label 'Go through the opened walkway door' -X -1490 -Y 4517 -Z -282 -TargetGameMoment 12 -MinimumGameMoment 11 -MaximumGameMoment 11 -RequiredCondition (New-Condition 5 2 0x01 0x01) -EntityName 'drE' -ScriptType 'Go'
Add-Definition -FieldId 121 -FieldName 'elevtr1' -Kind 'Location' -Label 'Press the elevator switch' -X 86 -Y 64 -Z 5 -TargetGameMoment 12 -MinimumGameMoment 11 -MaximumGameMoment 11 -EntityName 'ele' -ScriptType 'Main'
Add-Definition -FieldId 121 -FieldName 'elevtr1' -Kind 'Location' -Label "Leave the elevator toward Reactor 1's main staircase" -X -174 -Y -6 -Z 5 -MinimumGameMoment 12 -MaximumGameMoment 13 -EntityName 'jp0' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = -174; startY = -68; startZ = 5; endX = -174; endY = 56; endZ = 5 })
Add-Definition -FieldId 122 -FieldName 'nmkin_2' -Kind 'Location' -Label "Descend Reactor 1's main staircase toward Jessie and the upper piping" -X -701 -Y -249 -Z 0 -MinimumGameMoment 12 -MaximumGameMoment 13 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -730; startY = -215; startZ = 0; endX = -672; endY = -284; endZ = 0 })
Add-Definition -FieldId 123 -FieldName 'nmkin_3' -Kind 'Model' -Label 'Talk to Jessie for ladder instructions' -EntityId 4 -TargetGameMoment 14 -MinimumGameMoment 12 -MaximumGameMoment 13 -EntityName 'av_j' -ScriptType 'Talk'
Add-Definition -FieldId 123 -FieldName 'nmkin_3' -Kind 'Location' -Label 'Cross the Reactor 1 upper piping and descend toward the save point' -X 298 -Y 1265 -Z 855 -MinimumGameMoment 14 -MaximumGameMoment 26 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 260; startY = 1264; startZ = 852; endX = 337; endY = 1266; endZ = 858 })
Add-Definition -FieldId 124 -FieldName 'nmkin_4' -Kind 'Location' -Label "Continue past the save point to Reactor 1's core" -X -111 -Y -195 -Z -180 -MinimumGameMoment 14 -MaximumGameMoment 26 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -52; startY = -148; startZ = -180; endX = -171; endY = -242; endZ = -181 })
Add-Definition -FieldId 125 -FieldName 'nmkin_5' -Kind 'Location' -Label 'Plant the bomb at the reactor core' -X -67 -Y -1632 -Z -184 -MinimumGameMoment 14 -MaximumGameMoment 26 -EntityName 'dir' -ScriptType 'Main'
Add-Definition -FieldId 124 -FieldName 'nmkin_4' -Kind 'Location' -Label 'Climb back toward the Reactor 1 exit' -X 250 -Y 1195 -Z 861 -MinimumGameMoment 27 -MaximumGameMoment 32 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 206; startY = 1195; startZ = 856; endX = 294; endY = 1195; endZ = 866 })
Add-Definition -FieldId 123 -FieldName 'nmkin_3' -Kind 'Model' -Label 'Help Jessie free her leg' -EntityId 4 -MinimumGameMoment 27 -MaximumGameMoment 32 -CompletedCondition (New-Condition 1 225 0x20 0x20) -EntityName 'av_j' -ScriptType 'Talk'
Add-Definition -FieldId 123 -FieldName 'nmkin_3' -Kind 'Location' -Label 'Return to the Reactor 1 main staircase' -X -371 -Y 1921 -Z 2053 -MinimumGameMoment 27 -MaximumGameMoment 32 -RequiredCondition (New-Condition 1 225 0x20 0x20) -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -371; startY = 1959; startZ = 2053; endX = -371; endY = 1883; endZ = 2053 })
Add-Definition -FieldId 122 -FieldName 'nmkin_2' -Kind 'Location' -Label 'Return up Reactor 1''s main staircase to the elevator' -X -774 -Y 309 -Z 1571 -MinimumGameMoment 27 -MaximumGameMoment 32 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -833; startY = 346; startZ = 1571; endX = -716; endY = 273; endZ = 1571 })
Add-Definition -FieldId 121 -FieldName 'elevtr1' -Kind 'Location' -Label "Leave the elevator toward Reactor 1's security rooms" -X -174 -Y -6 -Z 5 -MinimumGameMoment 27 -MaximumGameMoment 32 -EntityName 'jp0' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = -174; startY = -68; startZ = 5; endX = -174; endY = 56; endZ = 5 })
Add-Definition -FieldId 120 -FieldName 'nmkin_1' -Kind 'Model' -Label 'Talk to Jessie to reopen the inner security door' -EntityId 10 -MinimumGameMoment 27 -MaximumGameMoment 32 -Priority 0 -RequiredCondition (New-Condition 1 225 0x20 0x20) -CompletedCondition (New-Condition 1 225 0x10 0x10) -EntityName 'av_j' -ScriptType 'Talk'
Add-Definition -FieldId 120 -FieldName 'nmkin_1' -Kind 'Model' -Label 'Talk to Biggs to reopen the outer security door' -EntityId 9 -MinimumGameMoment 27 -MaximumGameMoment 32 -Priority 1 -RequiredCondition (New-Condition 1 225 0x30 0x30) -CompletedCondition (New-Condition 1 225 0x08 0x08) -EntityName 'av_b' -ScriptType 'Talk'

# The Sector 8 escape, first train ride, and Sector 7 station sequence includes
# long stretches where the global moment is unchanged. These are the exact
# native gateway, LINE, and model targets from md8_1 through mds7st3. The
# passenger-car railway-map interaction is ordered by Bank 3 address 223 bit 2,
# which its Talk script sets before the mandatory window-side event becomes
# available. cargoin is intentionally limited to the post-arrival backtrack so
# Story remains silent during its automatic first-train choreography.
Add-Definition -FieldId 133 -FieldName 'md8_1' -Kind 'Location' -Label 'Continue through Sector 8 toward the flower girl' -X 426 -Y -5104 -Z 376 -TargetGameMoment 39 -MinimumGameMoment 36 -MaximumGameMoment 38 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 134 -FieldName 'md8_2' -Kind 'Location' -Label 'Continue through Sector 8' -X -3917 -Y 20527 -Z 258 -TargetGameMoment 48 -MinimumGameMoment 39 -MaximumGameMoment 47 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 135 -FieldName 'md8_3' -Kind 'Location' -Label 'Continue toward the Sector 8 bridge' -X -4037 -Y 17274 -Z 397 -TargetGameMoment 48 -MinimumGameMoment 39 -MaximumGameMoment 47 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 137 -FieldName 'md8brdg' -Kind 'Location' -Label 'Run past the soldiers and reach the bridge' -EntityId 4 -X -426 -Y 835 -Z 518 -TargetGameMoment 48 -MinimumGameMoment 39 -MaximumGameMoment 47 -Priority 0 -EntityName 'ev' -ScriptType 'Move'
Add-Definition -FieldId 139 -FieldName 'tin_1' -Kind 'Model' -Label 'Talk to Jessie and view the railway map' -EntityId 34 -TargetGameMoment 63 -MinimumGameMoment 51 -MaximumGameMoment 62 -Priority 0 -CompletedCondition (New-Condition 3 223 0x04 0x04) -EntityName 'avaw' -ScriptType 'Talk'
Add-Definition -FieldId 139 -FieldName 'tin_1' -Kind 'Location' -Label 'Go to the train window and join Barret' -EntityId 27 -X -2 -Y -215 -Z -53 -TargetGameMoment 63 -MinimumGameMoment 51 -MaximumGameMoment 62 -Priority 0 -RequiredCondition (New-Condition 3 223 0x04 0x04) -EntityName 'border2' -ScriptType 'Move'
Add-Definition -FieldId 138 -FieldName 'cargoin' -Kind 'Location' -Label 'Return to the passenger car' -EntityId 10 -X 19 -Y -104 -Z 0 -TargetGameMoment 63 -MinimumGameMoment 51 -MaximumGameMoment 62 -Priority 0 -ScriptType 'Move'
Add-Definition -FieldId 139 -FieldName 'tin_1' -Kind 'Location' -Label 'Go to Tifa at the railway map monitor' -EntityId 27 -X -2 -Y -215 -Z -53 -TargetGameMoment 117 -MinimumGameMoment 108 -MaximumGameMoment 116 -Priority 0 -CompletedCondition (New-Condition 5 33 0x01 0x01) -EntityName 'border2' -ScriptType 'Move'
Add-Definition -FieldId 139 -FieldName 'tin_1' -Kind 'Location' -Label 'Escape Car 1 through the forward door' -EntityId 26 -X 1 -Y -381 -Z -53 -TargetGameMoment 117 -MinimumGameMoment 108 -MaximumGameMoment 116 -Priority 0 -RequiredCondition (New-Condition 5 33 0x01 0x01) -EntityName 'border1' -ScriptType 'Move'
Add-Definition -FieldId 140 -FieldName 'tin_2' -Kind 'Location' -Label 'Escape Car 2 through the forward door' -EntityId 18 -X -2 -Y -412 -Z -56 -TargetGameMoment 117 -MinimumGameMoment 108 -MaximumGameMoment 116 -Priority 0 -EntityName 'border0' -ScriptType 'Move'
Add-Definition -FieldId 142 -FieldName 'tin_4' -Kind 'Location' -Label 'Escape through the next train car' -EntityId 15 -X 6 -Y -444 -Z -56 -TargetGameMoment 117 -MinimumGameMoment 108 -MaximumGameMoment 116 -Priority 0 -EntityName 'line0' -ScriptType 'Move'
Add-Definition -FieldId 141 -FieldName 'tin_3' -Kind 'Model' -Label 'Talk to Tifa and jump from the train' -EntityId 15 -TargetGameMoment 117 -MinimumGameMoment 108 -MaximumGameMoment 116 -Priority 0 -EntityName 'tifa' -ScriptType 'Talk'
Add-Definition -FieldId 161 -FieldName 'tunnel_1' -Kind 'Location' -Label 'Continue north through the winding tunnel' -EntityId 2 -X 746 -Y 2237 -Z 5 -TargetGameMoment 117 -MinimumGameMoment 108 -MaximumGameMoment 116 -Priority 0 -EntityName 'line2' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 805; startY = 2862; startZ = 9; endX = 687; endY = 1611; endZ = 0 }) -KeepActiveOnArrival
Add-Definition -FieldId 162 -FieldName 'tunnel_2' -Kind 'Location' -Label 'Enter the maintenance duct to continue to Reactor 5' -EntityId 5 -X -46 -Y 556 -Z 0 -TargetGameMoment 117 -MinimumGameMoment 108 -MaximumGameMoment 116 -Priority 0 -EntityName 'line2' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = -38; startY = 513; startZ = 0; endX = -54; endY = 600; endZ = 0 })
# Choosing Go down in tunnel_2 advances GameMoment to 117 before the Sector 4
# plate sequence begins. It remains 117 throughout these fields; Reactor 5 is
# the next native progression block and advances the moment to 123.
# These objectives are the exact ladder activation LINEs in sbwy4_1 and
# sbwy4_3 through sbwy4_6. sbwy4_2 is the short automatic ladder connector and
# therefore uses its native forward endpoint rather than fabricating a LINE.
Add-Definition -FieldId 164 -FieldName 'sbwy4_1' -Kind 'Location' -Label 'Follow the large duct to the far ladder' -EntityId 2 -X 0 -Y 63 -Z 0 -TargetGameMoment 123 -MinimumGameMoment 117 -MaximumGameMoment 122 -Priority 0 -EntityName 'line' -ScriptType '[OK]' -TriggerLine ([ordered]@{ startX = -69; startY = 65; startZ = 0; endX = 68; endY = 60; endZ = 0 })
Add-Definition -FieldId 165 -FieldName 'sbwy4_2' -Kind 'Location' -Label 'Continue across the upper ladder' -X 0 -Y 277 -Z 0 -TargetGameMoment 123 -MinimumGameMoment 117 -MaximumGameMoment 122 -Priority 0 -EntityName 'cloud' -ScriptType 'Main'
Add-Definition -FieldId 166 -FieldName 'sbwy4_3' -Kind 'Location' -Label "Cross Jessie's platform and use the next ladder" -EntityId 2 -X 6 -Y 52 -Z 0 -TargetGameMoment 123 -MinimumGameMoment 117 -MaximumGameMoment 122 -Priority 0 -EntityName 'line2' -ScriptType 'Go' -TriggerLine ([ordered]@{ startX = -60; startY = 52; startZ = 0; endX = 72; endY = 52; endZ = 0 })
Add-Definition -FieldId 167 -FieldName 'sbwy4_4' -Kind 'Location' -Label 'Go right across the small duct and use the ladder' -EntityId 5 -X 178 -Y 1 -Z 0 -TargetGameMoment 123 -MinimumGameMoment 117 -MaximumGameMoment 122 -Priority 0 -EntityName 'line1' -ScriptType '[OK]' -TriggerLine ([ordered]@{ startX = 185; startY = -35; startZ = 0; endX = 170; endY = 37; endZ = 0 })
Add-Definition -FieldId 168 -FieldName 'sbwy4_5' -Kind 'Location' -Label 'Use the ladder near Wedge' -EntityId 4 -X -2281 -Y 778 -Z 0 -TargetGameMoment 123 -MinimumGameMoment 117 -MaximumGameMoment 122 -Priority 0 -EntityName 'line3' -ScriptType 'Go' -TriggerLine ([ordered]@{ startX = -2323; startY = 778; startZ = 0; endX = -2239; endY = 778; endZ = 0 })
Add-Definition -FieldId 169 -FieldName 'sbwy4_6' -Kind 'Location' -Label 'Use the ladder near Biggs to reach Reactor 5' -EntityId 3 -X -357 -Y -144 -Z 14 -TargetGameMoment 123 -MinimumGameMoment 117 -MaximumGameMoment 122 -Priority 0 -EntityName 'line2' -ScriptType 'Go' -TriggerLine ([ordered]@{ startX = -357; startY = -176; startZ = 14; endX = -357; endY = -112; endZ = 14 })
# The second Reactor 5 descent keeps GameMoment 120 through smkin_2, smkin_3,
# and smkin_4. These are the exact forward gateways. Entering smkin_5 runs the
# native reactor-memory sequence and advances the moment to 123. Its director
# then waits for Cloud to enter walkmesh triangle 2, moves him to the native
# bomb-placement point, and advances the moment to 127.
Add-Definition -FieldId 129 -FieldName 'smkin_2' -Kind 'Location' -Label 'Descend through Reactor 5 to the upper piping and ladder room' -X -650 -Y -248 -Z 0 -TargetGameMoment 123 -MinimumGameMoment 117 -MaximumGameMoment 122 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -606; startY = -284; startZ = 0; endX = -694; endY = -212; endZ = 0 })
Add-Definition -FieldId 130 -FieldName 'smkin_3' -Kind 'Location' -Label 'Cross the upper piping and descend toward the save point' -X 296 -Y 1346 -Z 832 -TargetGameMoment 123 -MinimumGameMoment 117 -MaximumGameMoment 122 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 258; startY = 1356; startZ = 812; endX = 333; endY = 1336; endZ = 851 })
Add-Definition -FieldId 131 -FieldName 'smkin_4' -Kind 'Location' -Label "Continue past the save point to Reactor 5's core" -X -113 -Y -133 -Z -180 -TargetGameMoment 123 -MinimumGameMoment 117 -MaximumGameMoment 122 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -56; startY = -89; startZ = -180; endX = -170; endY = -177; endZ = -180 })
Add-Definition -FieldId 132 -FieldName 'smkin_5' -Kind 'Location' -Label "Plant the bomb at Reactor 5's core" -EntityId 2 -X -67 -Y -1632 -Z -184 -TargetGameMoment 127 -MinimumGameMoment 123 -MaximumGameMoment 126 -Priority 0 -EntityName 'cl' -ScriptType 'Script 10'

# Reactor 5's escape keeps GameMoment 127 while Cloud backtracks through the
# same fields, returns in the elevator, and performs the simultaneous button
# press. GameMoment then remains 128 through the bridge and Air Buster fight,
# finally advancing to 140 before the church map jump. These coordinates and
# trigger lines are the native return gateways and activation lines, not
# reversed descent targets. elevtr1 Bank 1 address 225 bit 0 identifies which
# side of the elevator is currently accessible.
Add-Definition -FieldId 132 -FieldName 'smkin_5' -Kind 'Location' -Label "Escape Reactor 5's core" -EntityId 6 -X -86 -Y -746 -Z -181 -TargetGameMoment 128 -MinimumGameMoment 127 -MaximumGameMoment 127 -Priority 0 -EntityName 'ln0' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = -30; startY = -727; startZ = -181; endX = -142; endY = -766; endZ = -181 })
Add-Definition -FieldId 131 -FieldName 'smkin_4' -Kind 'Location' -Label 'Climb back toward the Reactor 5 elevator' -X 250 -Y 1255 -Z 862 -TargetGameMoment 128 -MinimumGameMoment 127 -MaximumGameMoment 127 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 208; startY = 1255; startZ = 854; endX = 291; endY = 1255; endZ = 869 })
Add-Definition -FieldId 130 -FieldName 'smkin_3' -Kind 'Location' -Label 'Cross the upper piping back toward the elevator' -X -328 -Y 1921 -Z 2094 -TargetGameMoment 128 -MinimumGameMoment 127 -MaximumGameMoment 127 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -341; startY = 1955; startZ = 2082; endX = -316; endY = 1887; endZ = 2107 })
Add-Definition -FieldId 129 -FieldName 'smkin_2' -Kind 'Location' -Label 'Return to the Reactor 5 elevator' -X -730 -Y 310 -Z 1571 -TargetGameMoment 128 -MinimumGameMoment 127 -MaximumGameMoment 127 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -694; startY = 273; startZ = 1571; endX = -767; endY = 347; endZ = 1571 })
Add-Definition -FieldId 121 -FieldName 'elevtr1' -Kind 'Location' -Label 'Press the elevator switch to return upstairs' -EntityId 5 -X 86 -Y 64 -Z 5 -TargetGameMoment 128 -MinimumGameMoment 127 -MaximumGameMoment 127 -Priority 0 -RequiredCondition (New-Condition 1 225 0x01 0x01) -CompletedCondition (New-Condition 1 225 0x01 0x00) -EntityName 'ele' -ScriptType 'Main'
Add-Definition -FieldId 121 -FieldName 'elevtr1' -Kind 'Location' -Label 'Leave the elevator for the security room' -EntityId 10 -X -185 -Y -6 -Z 5 -TargetGameMoment 128 -MinimumGameMoment 127 -MaximumGameMoment 127 -Priority 0 -RequiredCondition (New-Condition 1 225 0x01 0x00) -EntityName 'jp0' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = -185; startY = -68; startZ = 5; endX = -185; endY = 56; endZ = 5 })
Add-Definition -FieldId 128 -FieldName 'smkin_1' -Kind 'Location' -Label 'Reach the simultaneous security controls' -EntityId 6 -X -532 -Y 3353 -Z -273 -TargetGameMoment 128 -MinimumGameMoment 127 -MaximumGameMoment 127 -Priority 0 -EntityName 'ln1' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = -532; startY = 3305; startZ = -273; endX = -532; endY = 3401; endZ = -273 })
Add-Definition -FieldId 128 -FieldName 'smkin_1' -Kind 'Location' -Label 'Continue to the bridge approach' -X -694 -Y 1124 -Z -433 -TargetGameMoment 140 -MinimumGameMoment 128 -MaximumGameMoment 139 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -516; startY = 1106; startZ = -433; endX = -873; endY = 1141; endZ = -433 })
Add-Definition -FieldId 126 -FieldName 'southmk1' -Kind 'Location' -Label 'Open the bridge door and continue' -EntityId 1 -X -3 -Y -2760 -Z 491 -TargetGameMoment 140 -MinimumGameMoment 128 -MaximumGameMoment 139 -Priority 0 -EntityName 'line' -ScriptType '[OK]' -TriggerLine ([ordered]@{ startX = 48; startY = -2771; startZ = 491; endX = -54; endY = -2750; endZ = 491 }) -KeepActiveOnArrival

# chrin_1b (183) is an automatic transition with user control locked. The
# playable escape and barrel rescue are in chrin_2 (184). Entering walkmesh
# triangle 81 advances GameMoment to 152; its center is the stable approach
# target immediately before the scripted Reno sequence.
Add-Definition -FieldId 184 -FieldName 'chrin_2' -Kind 'Location' -Label 'Continue along the upper church rafters' -X -198 -Y 1871 -Z 502 -TargetGameMoment 152 -Priority 0 -ScriptType 'Native triangle 81'

# The native script exposes barrel interaction only while temporary Bank 5,
# byte 10 equals one. Byte 12 identifies the visible guard/Aerith rescue stage.
# Each stage points to the barrel Aerith visually indicates on screen.
Add-Definition -FieldId 184 -FieldName 'chrin_2' -Kind 'Model' -Label 'Push the left barrel to help Aerith' -EntityId 10 -MinimumGameMoment 152 -MaximumGameMoment 152 -Priority 0 -RequiredConditions @((New-Condition 5 10 0xFF 1), (New-Condition 5 12 0xFF 1)) -EntityName 'bar3' -ScriptType 'Talk'
Add-Definition -FieldId 184 -FieldName 'chrin_2' -Kind 'Model' -Label 'Push the middle barrel to help Aerith' -EntityId 8 -MinimumGameMoment 152 -MaximumGameMoment 152 -Priority 0 -RequiredConditions @((New-Condition 5 10 0xFF 1), (New-Condition 5 12 0xFF 2)) -EntityName 'bar1' -ScriptType 'Talk'
Add-Definition -FieldId 184 -FieldName 'chrin_2' -Kind 'Model' -Label 'Push the right barrel to help Aerith' -EntityId 9 -MinimumGameMoment 152 -MaximumGameMoment 152 -Priority 0 -RequiredConditions @((New-Condition 5 10 0xFF 1), (New-Condition 5 12 0xFF 3)) -EntityName 'bar2' -ScriptType 'Talk'

# The live church-to-house run establishes which apparent exit gaps are
# actually mandatory scripted interactions. chrin_1b gives control back to
# the player at GameMoment 140 and puts the only forward MAPJUMP inside
# Aeris's Talk script. chrin_3b uses an [OK] line to set moment 155 before the
# roof transition. Neither transition is a native gateway, so both belong in
# Story rather than as fabricated Exit entries.
Add-Definition -FieldId 183 -FieldName 'chrin_1b' -Kind 'Model' -Label 'Talk to Aeris by the flowers' -EntityId 6 -TargetGameMoment 143 -MinimumGameMoment 140 -MaximumGameMoment 142 -Priority 0 -RequiredCondition (New-Condition 3 17 0x01 0x00) -CompletedCondition (New-Condition 3 17 0x01 0x01) -EntityName 'earith' -ScriptType 'Talk'
Add-Definition -FieldId 183 -FieldName 'chrin_1b' -Kind 'Model' -Label 'Talk to Aeris again after Reno arrives' -EntityId 6 -TargetGameMoment 143 -MinimumGameMoment 140 -MaximumGameMoment 142 -Priority 0 -RequiredCondition (New-Condition 3 17 0x01 0x01) -EntityName 'earith' -ScriptType 'Talk'
Add-Definition -FieldId 186 -FieldName 'chrin_3b' -Kind 'Location' -Label 'Cross the final roof beam to escape the church' -EntityId 1 -X 213 -Y -12 -Z 935 -TargetGameMoment 155 -MinimumGameMoment 152 -MaximumGameMoment 154 -Priority 0 -EntityName 'jump' -ScriptType '[OK]' -TriggerLine ([ordered]@{ startX = 213; startY = 2; startZ = 935; endX = 213; endY = -25; endZ = 935 }) -KeepActiveOnArrival

# The church roof scene advances GameMoment to 158. It remains 158 while the
# player crosses mds5_4, chooses the forward gateways in mds5_2, mds5_3, and
# mds5_1, then enters the garden and house. The house interior advances to
# moment 164. These exact native triangle, gateway, and Move-line targets
# provide one continuous Story route. The church and church-interior entries
# are recovery paths for a player who takes the wrong outskirts gateway.
Add-Definition -FieldId 171 -FieldName 'mds5_4' -Kind 'Location' -Label 'Follow the rooftops toward the Sector 5 slums' -X -1068 -Y -597 -Z 221 -TargetGameMoment 164 -MinimumGameMoment 158 -MaximumGameMoment 163 -Priority 0 -ScriptType 'Native triangle 0'
Add-Definition -FieldId 173 -FieldName 'mds5_2' -Kind 'Location' -Label "Continue through the outskirts toward Aeris's house" -X -1289 -Y -180 -Z 0 -TargetGameMoment 164 -MinimumGameMoment 158 -MaximumGameMoment 163 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -1474; startY = 79; startZ = 0; endX = -1103; endY = -438; endZ = 0 })
Add-Definition -FieldId 172 -FieldName 'mds5_3' -Kind 'Location' -Label "Continue through Sector 5 toward Aeris's house" -X 122 -Y 547 -Z 0 -TargetGameMoment 164 -MinimumGameMoment 158 -MaximumGameMoment 163 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 221; startY = 548; startZ = 0; endX = 23; endY = 546; endZ = 0 })
Add-Definition -FieldId 177 -FieldName 'mds5_1' -Kind 'Location' -Label "Go to Aeris's garden" -X 631 -Y 1314 -Z 0 -TargetGameMoment 164 -MinimumGameMoment 158 -MaximumGameMoment 163 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 590; startY = 1585; startZ = 0; endX = 671; endY = 1043; endZ = 0 })
Add-Definition -FieldId 187 -FieldName 'eals_1' -Kind 'Location' -Label "Enter Aeris's house" -EntityId 9 -X 12 -Y 98 -Z 0 -TargetGameMoment 164 -MinimumGameMoment 158 -MaximumGameMoment 163 -Priority 0 -EntityName 'll' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = -22; startY = 98; startZ = 0; endX = 46; endY = 98; endZ = 0 })
Add-Definition -FieldId 181 -FieldName 'church' -Kind 'Location' -Label 'Return to the Sector 5 slum outskirts' -X -732 -Y -149 -Z -1 -TargetGameMoment 164 -MinimumGameMoment 158 -MaximumGameMoment 163 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -906; startY = -33; startZ = -1; endX = -558; endY = -264; endZ = -1 })
Add-Definition -FieldId 182 -FieldName 'chrin_1a' -Kind 'Location' -Label 'Leave the church and return toward Sector 5' -X -1 -Y -467 -Z 0 -TargetGameMoment 164 -MinimumGameMoment 158 -MaximumGameMoment 163 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -80; startY = -467; startZ = 0; endX = 79; endY = -467; endZ = 0 })

# The first house visit has three separate native states. ealin_1 returns
# control at moment 164 after the Elmyra scene, and its upper gateway begins
# the automatic bedtime sequence that writes moment 167. ealin_2 promotes
# that to moment 170 after Cloud's flashback. escsen both checks the run input
# after x=-126 and catches Cloud unconditionally when he crosses its short
# creaky-floor Move line from (-1,288) to (21,259). Story therefore retains a
# mandatory wall-side corner only while the direct route would cross that
# native hazard. Once outside, eals_1 writes 173 and Aeris rejoins in mds5_3
# at moment 176.
Add-Definition -FieldId 188 -FieldName 'ealin_1' -Kind 'Location' -Label 'Go upstairs to rest' -X 204 -Y 371 -Z 288 -TargetGameMoment 167 -MinimumGameMoment 164 -MaximumGameMoment 166 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 188; startY = 332; startZ = 288; endX = 220; endY = 409; endZ = 288 })
Add-Definition -FieldId 190 -FieldName 'ealin_2' -Kind 'Location' -Label "Walk by the stair ledge, avoid Aeris's door, then go downstairs; do not run" -X 83 -Y 442 -Z 69 -TargetGameMoment 173 -MinimumGameMoment 170 -MaximumGameMoment 172 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 94; startY = 486; startZ = 54; endX = 72; endY = 398; endZ = 84 }) -RouteDetours @(
    [ordered]@{
        blockedLine = [ordered]@{ startX = -1; startY = 288; startZ = 288; endX = 21; endY = 259; endZ = 288 }
        x = 20
        y = 340
        z = 288
        clearance = 0
    },
    [ordered]@{
        blockedLine = [ordered]@{ startX = 58; startY = 125; startZ = 288; endX = 159; endY = 131; endZ = 288 }
        x = 196
        y = 286
        z = 288
        clearance = 40
    })
Add-Definition -FieldId 188 -FieldName 'ealin_1' -Kind 'Location' -Label "Leave Aeris's house for Sector 6" -X -187 -Y -170 -Z 0 -TargetGameMoment 173 -MinimumGameMoment 170 -MaximumGameMoment 172 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -243; startY = -143; startZ = 0; endX = -130; endY = -196; endZ = 0 })
Add-Definition -FieldId 187 -FieldName 'eals_1' -Kind 'Location' -Label "Leave Aeris's garden and backtrack toward Sector 6" -X -189 -Y -149 -Z 0 -TargetGameMoment 176 -MinimumGameMoment 173 -MaximumGameMoment 175 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -228; startY = -1; startZ = 0; endX = -149; endY = -297; endZ = 0 })
Add-Definition -FieldId 177 -FieldName 'mds5_1' -Kind 'Location' -Label 'Backtrack through Sector 5 toward Sector 6' -X -944 -Y 5 -Z 0 -TargetGameMoment 176 -MinimumGameMoment 173 -MaximumGameMoment 175 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -987; startY = 94; startZ = 0; endX = -900; endY = -85; endZ = 0 })
Add-Definition -FieldId 172 -FieldName 'mds5_3' -Kind 'Location' -Label 'Enter Sector 6 with Aeris' -X -667 -Y -156 -Z 0 -TargetGameMoment 179 -MinimumGameMoment 176 -MaximumGameMoment 178 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -590; startY = -267; startZ = 0; endX = -744; endY = -45; endZ = 0 })

# Sector 6 advances through three native map states. The collapsed expressway
# uses jp's scripted Go 1x line rather than a trigger-section gateway. Entering
# the playground starts a scene at moment 179. Aeris then waits on the slide in
# temporary field state 1 until the player talks to her; that Talk advances the
# field state to 2 and resumes the scene, which writes moment 185. The Wall
# Market entrance scene then writes 188.
Add-Definition -FieldId 191 -FieldName 'mds6_1' -Kind 'Location' -Label 'Continue through the Sector 6 collapsed expressway to the playground' -X 1277 -Y 345 -Z 22 -TargetGameMoment 179 -MinimumGameMoment 176 -MaximumGameMoment 178 -Priority 0 -EntityName 'jp' -ScriptType 'Go 1x' -TriggerLine ([ordered]@{ startX = 1195; startY = 427; startZ = 22; endX = 1359; endY = 263; endZ = 22 })
Add-Definition -FieldId 192 -FieldName 'mds6_2' -Kind 'Model' -Label 'Talk to Aeris on the playground slide' -EntityId 2 -TargetGameMoment 185 -MinimumGameMoment 179 -MaximumGameMoment 184 -Priority 0 -RequiredCondition (New-Condition 5 13 0xFF 1) -CompletedCondition (New-Condition 5 13 0xFF 2) -EntityName 'earith' -ScriptType 'Talk'
Add-Definition -FieldId 192 -FieldName 'mds6_2' -Kind 'Location' -Label 'Leave the playground toward Wall Market' -X -424 -Y 1426 -Z 0 -TargetGameMoment 188 -MinimumGameMoment 185 -MaximumGameMoment 187 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -717; startY = 1888; startZ = 0; endX = -131; endY = 964; endZ = 0 })
Add-Definition -FieldId 194 -FieldName 'mds6_3' -Kind 'Location' -Label 'Continue into Wall Market' -X 63 -Y 848 -Z 0 -TargetGameMoment 188 -MinimumGameMoment 185 -MaximumGameMoment 187 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -225; startY = 849; startZ = 0; endX = 350; endY = 846; endZ = 0 })

# At moment 188, Aeris explicitly says they must find Tifa. The native fatman1
# conversation in mrkt3 supplies the location and writes moment 190. From there
# these exact gateways keep the Story route continuous through the hub screens
# to the already-catalogued Corneo Hall doorman, which writes moment 191.
Add-Definition -FieldId 195 -FieldName 'mrkt2' -Kind 'Location' -Label 'Go to the Honey Bee Inn and ask about Tifa' -X 484 -Y -659 -Z 0 -TargetGameMoment 190 -MinimumGameMoment 188 -MaximumGameMoment 189 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 498; startY = -407; startZ = 0; endX = 470; endY = -911; endZ = 0 })
Add-Definition -FieldId 214 -FieldName 'mrkt3' -Kind 'Location' -Label 'Return to Wall Market and head toward Corneo Hall' -X -472 -Y -341 -Z 0 -TargetGameMoment 191 -MinimumGameMoment 190 -MaximumGameMoment 190 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -616; startY = -30; startZ = 0; endX = -329; endY = -652; endZ = 0 })
Add-Definition -FieldId 195 -FieldName 'mrkt2' -Kind 'Location' -Label 'Continue north toward Corneo Hall' -X -135 -Y 2496 -Z 0 -TargetGameMoment 191 -MinimumGameMoment 190 -MaximumGameMoment 190 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -251; startY = 2500; startZ = 0; endX = -19; endY = 2492; endZ = 0 }) -RouteDetours @(
    [ordered]@{
        blockedLine = [ordered]@{ startX = 100; startY = 1700; startZ = 0; endX = 400; endY = 1700; endZ = 0 }
        x = -150
        y = 2000
        z = 0
        clearance = 120
    })
Add-Definition -FieldId 205 -FieldName 'mrkt1' -Kind 'Location' -Label 'Enter Corneo Hall' -X 4 -Y -9 -Z 0 -TargetGameMoment 191 -MinimumGameMoment 190 -MaximumGameMoment 190 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -63; startY = -9; startZ = 0; endX = 70; endY = -9; endZ = 0 })

# Corneo's doorman advances the story to moment 191, but the disguise quest
# itself advances through persistent flags without changing GameMoment. Mirror
# each mandatory interior objective on both Wall Market hub screens so Story
# always exposes the next reachable gateway and retires it as soon as the
# destination script changes the corresponding native flag.
Add-Definition -FieldId 195 -FieldName 'mrkt2' -Kind 'Location' -Label 'Enter the boutique and ask the clothes-shop clerk for help' -X -436 -Y 2014 -Z 0 -TargetGameMoment 192 -MinimumGameMoment 191 -MaximumGameMoment 191 -Priority 0 -CompletedCondition (New-Condition 1 162 0x80 0x80) -ScriptType 'Gateway'

Add-Definition -FieldId 195 -FieldName 'mrkt2' -Kind 'Location' -Label 'Continue north to find the clothes-shop owner at the bar' -X -135 -Y 2496 -Z 0 -TargetGameMoment 192 -MinimumGameMoment 191 -MaximumGameMoment 191 -Priority 0 -RequiredCondition (New-Condition 1 162 0x80 0x80) -CompletedCondition (New-Condition 1 161 0xE0 0 -AnyBitSet) -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -251; startY = 2500; startZ = 0; endX = -19; endY = 2492; endZ = 0 }) -RouteDetours @(
    [ordered]@{
        blockedLine = [ordered]@{ startX = 100; startY = 1700; startZ = 0; endX = 400; endY = 1700; endZ = 0 }
        x = -150
        y = 2000
        z = 0
        clearance = 120
    })
Add-Definition -FieldId 205 -FieldName 'mrkt1' -Kind 'Location' -Label 'Enter the bar and speak with the clothes-shop owner' -X -666 -Y -1653 -Z 0 -TargetGameMoment 192 -MinimumGameMoment 191 -MaximumGameMoment 191 -Priority 0 -RequiredCondition (New-Condition 1 162 0x80 0x80) -CompletedCondition (New-Condition 1 161 0xE0 0 -AnyBitSet) -ScriptType 'Gateway'

Add-Definition -FieldId 205 -FieldName 'mrkt1' -Kind 'Location' -Label 'Return to lower Wall Market for the finished dress' -X -134 -Y -3083 -Z 0 -TargetGameMoment 192 -MinimumGameMoment 191 -MaximumGameMoment 191 -Priority 0 -RequiredCondition (New-Condition 1 161 0xE0 0 -AnyBitSet) -CompletedCondition (New-Condition 1 161 0x08 0x08) -ScriptType 'Gateway'
Add-Definition -FieldId 195 -FieldName 'mrkt2' -Kind 'Location' -Label 'Return to the boutique and collect the finished dress' -X -436 -Y 2014 -Z 0 -TargetGameMoment 192 -MinimumGameMoment 191 -MaximumGameMoment 191 -Priority 0 -RequiredCondition (New-Condition 1 161 0xE0 0 -AnyBitSet) -CompletedCondition (New-Condition 1 161 0x08 0x08) -ScriptType 'Gateway'

Add-Definition -FieldId 195 -FieldName 'mrkt2' -Kind 'Location' -Label "Continue north to the Men's Hall for a wig" -X -135 -Y 2496 -Z 0 -TargetGameMoment 192 -MinimumGameMoment 191 -MaximumGameMoment 191 -Priority 0 -RequiredCondition (New-Condition 1 161 0x08 0x08) -CompletedCondition (New-Condition 1 160 0x80 0x80) -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -251; startY = 2500; startZ = 0; endX = -19; endY = 2492; endZ = 0 }) -RouteDetours @(
    [ordered]@{
        blockedLine = [ordered]@{ startX = 100; startY = 1700; startZ = 0; endX = 400; endY = 1700; endZ = 0 }
        x = -150
        y = 2000
        z = 0
        clearance = 120
    })
Add-Definition -FieldId 205 -FieldName 'mrkt1' -Kind 'Location' -Label "Enter the Men's Hall and complete the squat contest" -X 214 -Y -2394 -Z 0 -TargetGameMoment 192 -MinimumGameMoment 191 -MaximumGameMoment 191 -Priority 0 -RequiredCondition (New-Condition 1 161 0x08 0x08) -CompletedCondition (New-Condition 1 160 0x80 0x80) -ScriptType 'Gateway'

Add-Definition -FieldId 205 -FieldName 'mrkt1' -Kind 'Location' -Label 'Return to lower Wall Market and change clothes at the boutique' -X -134 -Y -3083 -Z 0 -TargetGameMoment 192 -MinimumGameMoment 191 -MaximumGameMoment 191 -Priority 0 -RequiredCondition (New-Condition 1 160 0x80 0x80) -CompletedCondition (New-Condition 3 162 0x02 0x02) -ScriptType 'Gateway'
Add-Definition -FieldId 195 -FieldName 'mrkt2' -Kind 'Location' -Label 'Return to the boutique fitting room and change clothes' -X -436 -Y 2014 -Z 0 -TargetGameMoment 192 -MinimumGameMoment 191 -MaximumGameMoment 191 -Priority 0 -RequiredCondition (New-Condition 1 160 0x80 0x80) -CompletedCondition (New-Condition 3 162 0x02 0x02) -ScriptType 'Gateway'

Add-Definition -FieldId 195 -FieldName 'mrkt2' -Kind 'Location' -Label 'Continue north to Corneo Hall while disguised' -X -135 -Y 2496 -Z 0 -MinimumGameMoment 192 -MaximumGameMoment 192 -Priority 0 -RequiredCondition (New-Condition 3 162 0x03 0x03) -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -251; startY = 2500; startZ = 0; endX = -19; endY = 2492; endZ = 0 }) -RouteDetours @(
    [ordered]@{
        blockedLine = [ordered]@{ startX = 100; startY = 1700; startZ = 0; endX = 400; endY = 1700; endZ = 0 }
        x = -150
        y = 2000
        z = 0
        clearance = 120
    })
Add-Definition -FieldId 205 -FieldName 'mrkt1' -Kind 'Location' -Label 'Enter Corneo Hall while disguised' -X 4 -Y -9 -Z 0 -MinimumGameMoment 192 -MaximumGameMoment 192 -Priority 0 -RequiredCondition (New-Condition 3 162 0x03 0x03) -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -63; startY = -9; startZ = 0; endX = 70; endY = -9; endZ = 0 })

Add-Definition -FieldId 144 -FieldName 'mds7st1' -Kind 'Location' -Label 'Continue toward the Sector 7 station' -X 1688 -Y 676 -Z 0 -TargetGameMoment 69 -MinimumGameMoment 63 -MaximumGameMoment 68 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 145 -FieldName 'mds7st2' -Kind 'Location' -Label 'Return to Sector 7 station by the upper route' -X -2154 -Y 3390 -Z 101 -TargetGameMoment 69 -MinimumGameMoment 63 -MaximumGameMoment 68 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 145 -FieldName 'mds7st2' -Kind 'Location' -Label 'Return to Sector 7 station by the lower route' -X -1897 -Y 2644 -Z 0 -TargetGameMoment 69 -MinimumGameMoment 63 -MaximumGameMoment 68 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 146 -FieldName 'mds7st3' -Kind 'Location' -Label 'Follow Avalanche toward the Sector 7 slums' -X -3796 -Y 1694 -Z 0 -TargetGameMoment 69 -MinimumGameMoment 63 -MaximumGameMoment 68 -Priority 0 -ScriptType 'Gateway'

# Seventh Heaven uses local state rather than a global GameMoment write for
# several mandatory transitions. These targets are the native trigger lines
# and completion flags from mds7pb_1 and mds7pb_2.
Add-Definition -FieldId 154 -FieldName 'mds7pb_1' -Kind 'Location' -Label 'Approach the front door; Barret and Avalanche are entering' -X 264 -Y 0 -Z 0 -TargetGameMoment 72 -MinimumGameMoment 69 -MaximumGameMoment 71 -Priority 0 -CompletedCondition (New-Condition 3 212 0x01 0x01) -EntityName 'border1' -ScriptType 'Move'
Add-Definition -FieldId 154 -FieldName 'mds7pb_1' -Kind 'Location' -Label 'Use the pinball machine to descend to the Avalanche basement' -X -30 -Y 180 -Z 0 -MinimumGameMoment 72 -MaximumGameMoment 77 -Priority 0 -EntityName 'pinball' -ScriptType 'Go'
Add-Definition -FieldId 155 -FieldName 'mds7pb_2' -Kind 'Location' -Label 'Approach Barret and the Avalanche meeting' -X 12 -Y 168 -Z 0 -MinimumGameMoment 72 -MaximumGameMoment 77 -Priority 0 -CompletedCondition (New-Condition 3 214 0x01 0x01) -EntityName 'border2' -ScriptType 'Move'
Add-Definition -FieldId 155 -FieldName 'mds7pb_2' -Kind 'Location' -Label 'Head back toward the pinball elevator; Tifa wants to speak' -X 12 -Y 168 -Z 0 -TargetGameMoment 78 -MinimumGameMoment 72 -MaximumGameMoment 77 -Priority 0 -RequiredCondition (New-Condition 3 214 0x03 0x03) -CompletedCondition (New-Condition 3 214 0x04 0x04) -EntityName 'border2' -ScriptType 'Move'
Add-Definition -FieldId 155 -FieldName 'mds7pb_2' -Kind 'Location' -Label 'Use the pinball machine to return upstairs' -X 142 -Y -40 -Z 0 -MinimumGameMoment 78 -MaximumGameMoment 83 -Priority 0 -EntityName 'pinball' -ScriptType 'Go'
Add-Definition -FieldId 154 -FieldName 'mds7pb_1' -Kind 'Location' -Label 'Walk toward the front door; Tifa will stop Cloud about the promise' -X 50 -Y -24 -Z 0 -TargetGameMoment 84 -MinimumGameMoment 78 -MaximumGameMoment 83 -Priority 0 -RequiredCondition (New-Condition 3 213 0x01 0x01) -CompletedCondition (New-Condition 3 213 0x08 0x08) -EntityName 'border4' -ScriptType 'Move'
Add-Definition -FieldId 155 -FieldName 'mds7pb_2' -Kind 'Location' -Label 'Use the pinball machine to return to the bar after waking' -X 142 -Y -40 -Z 0 -MinimumGameMoment 105 -MaximumGameMoment 107 -Priority 0 -EntityName 'pinball' -ScriptType 'Go'

# Wall Market, Corneo Hall, and the sewers have a long mandatory sequence whose
# progress is mostly stored in local and persistent bitfields. Every model and
# line below is the exact native activation target from the installed scripts.
Add-Definition -FieldId 214 -FieldName 'mrkt3' -Kind 'Model' -Label 'Ask about Tifa; choose the Tifa question' -EntityId 16 -TargetGameMoment 190 -MinimumGameMoment 188 -MaximumGameMoment 189 -Priority 0 -EntityName 'fatman1' -ScriptType 'Talk'
Add-Definition -FieldId 206 -FieldName 'colne_1' -Kind 'Model' -Label 'Ask to enter Corneo Hall; learn men are refused' -EntityId 14 -TargetGameMoment 191 -MinimumGameMoment 190 -MaximumGameMoment 190 -Priority 0 -CompletedCondition (New-Condition 3 162 0x01 0x01) -EntityName 'DOORMAN' -ScriptType 'Talk'
Add-Definition -FieldId 201 -FieldName 'mkt_s1' -Kind 'Location' -Label 'Ask the clothes-shop clerk for a dress; learn the owner is at the bar' -X 140 -Y 0 -Z 0 -MinimumGameMoment 191 -MaximumGameMoment 191 -Priority 0 -CompletedCondition (New-Condition 1 162 0x80 0x80) -EntityName 'line00' -ScriptType '[OK]'
Add-Definition -FieldId 204 -FieldName 'mktpb' -Kind 'Model' -Label "Ask the clothes-shop owner to make Cloud's dress" -EntityId 14 -MinimumGameMoment 191 -MaximumGameMoment 191 -Priority 0 -RequiredCondition (New-Condition 1 162 0x80 0x80) -CompletedCondition (New-Condition 1 161 0xE0 0 -AnyBitSet) -EntityName 'oldm3' -ScriptType 'Talk'
Add-Definition -FieldId 201 -FieldName 'mkt_s1' -Kind 'Location' -Label 'Collect the finished dress and learn about the gym' -X 140 -Y 0 -Z 0 -MinimumGameMoment 191 -MaximumGameMoment 191 -Priority 0 -RequiredCondition (New-Condition 1 161 0xE0 0 -AnyBitSet) -CompletedCondition (New-Condition 1 161 0x08 0x08) -EntityName 'line00' -ScriptType '[OK]'
Add-Definition -FieldId 197 -FieldName 'mkt_mens' -Kind 'Model' -Label 'Talk to Big Bro and complete the squat contest for a wig' -EntityId 12 -MinimumGameMoment 191 -MaximumGameMoment 191 -Priority 0 -RequiredCondition (New-Condition 1 161 0x08 0x08) -CompletedCondition (New-Condition 1 160 0x80 0x80) -EntityName 'okama' -ScriptType 'Talk'
Add-Definition -FieldId 201 -FieldName 'mkt_s1' -Kind 'Location' -Label 'Enter the fitting room and choose to change clothes' -X -113 -Y 158 -Z 0 -TargetGameMoment 192 -MinimumGameMoment 191 -MaximumGameMoment 191 -Priority 0 -RequiredCondition (New-Condition 1 160 0x80 0x80) -CompletedCondition (New-Condition 3 162 0x02 0x02) -EntityName 'line01' -ScriptType '[OK]'
Add-Definition -FieldId 206 -FieldName 'colne_1' -Kind 'Model' -Label 'Talk to the doorman while disguised and enter' -EntityId 14 -MinimumGameMoment 192 -MaximumGameMoment 192 -Priority 0 -RequiredCondition (New-Condition 3 162 0x03 0x03) -EntityName 'DOORMAN' -ScriptType 'Talk'
Add-Definition -FieldId 209 -FieldName 'colne_4' -Kind 'Model' -Label 'Talk to Tifa until the escort and selection sequence begins' -EntityId 12 -TargetGameMoment 197 -MinimumGameMoment 192 -MaximumGameMoment 192 -Priority 0 -CompletedCondition (New-Condition 3 162 0x08 0x08) -EntityName 'TIFA2' -ScriptType 'Talk'
Add-Definition -FieldId 208 -FieldName 'colne_3' -Kind 'Model' -Label "Confront Scotch and Corneo's lackeys" -EntityId 11 -MinimumGameMoment 197 -MaximumGameMoment 197 -Priority 0 -RequiredCondition (New-Condition 3 162 0x40 0x40) -CompletedCondition (New-Condition 3 163 0x08 0x08) -EntityName 'SOTCH' -ScriptType 'Talk'
Add-Definition -FieldId 211 -FieldName 'colne_6' -Kind 'Location' -Label 'Cross the bedroom rug and trigger the trap' -X 38 -Y 21 -Z -237 -TargetGameMoment 203 -MinimumGameMoment 197 -MaximumGameMoment 197 -Priority 0 -EntityName 'LINE' -ScriptType 'Move'
Add-Definition -FieldId 212 -FieldName 'colne_b1' -Kind 'Model' -Label 'Wake and check Aeris' -EntityId 2 -TargetGameMoment 209 -MinimumGameMoment 203 -MaximumGameMoment 203 -Priority 0 -CompletedCondition (New-Condition 5 10 0xFF 0x01) -EntityName 'ea' -ScriptType 'Talk'
Add-Definition -FieldId 212 -FieldName 'colne_b1' -Kind 'Model' -Label 'Wake and check Tifa' -EntityId 3 -TargetGameMoment 209 -MinimumGameMoment 203 -MaximumGameMoment 203 -Priority 0 -CompletedCondition (New-Condition 5 9 0xFF 0x01) -EntityName 'ti' -ScriptType 'Talk'
Add-Definition -FieldId 212 -FieldName 'colne_b1' -Kind 'Location' -Label 'Choose to climb out after defeating Aps' -X 1360 -Y -568 -Z 128 -MinimumGameMoment 209 -MaximumGameMoment 209 -Priority 0 -EntityName 'jp0' -ScriptType '[OK]'
Add-Definition -FieldId 213 -FieldName 'colne_b3' -Kind 'Location' -Label 'Use the final sewer ladder' -X 423 -Y 310 -Z -211 -MinimumGameMoment 209 -MaximumGameMoment 209 -Priority 0 -EntityName 'ln2' -ScriptType 'Move'

# Train Graveyard begins after the sewer ladder has already advanced
# GameMoment to 212. The generic automatic extraction points back into the
# sewer at that exact moment, so this native forward chain replaces it.
# Bank 1 byte 164 records the two trains' live geometry. Native line20 advances
# the normal state from 0 to 3, then line30 advances 3 to 7. The station exit
# remains disconnected until both trains form the walkable bridge.
Add-Definition -FieldId 144 -FieldName 'mds7st1' -Kind 'Location' -Label 'Continue into the Train Graveyard' -X 1688 -Y 676 -Z 0 -TargetGameMoment 215 -MinimumGameMoment 212 -MaximumGameMoment 214 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 145 -FieldName 'mds7st2' -Kind 'Location' -Label 'Move the first Train Graveyard train' -X 1740 -Y 3094 -Z 0 -TargetGameMoment 215 -MinimumGameMoment 212 -MaximumGameMoment 214 -Priority 0 -RequiredCondition (New-Condition 1 164 0x07 0x00) -EntityName 'line20' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 1691; startY = 3064; startZ = 0; endX = 1790; endY = 3124; endZ = 0 })
Add-Definition -FieldId 145 -FieldName 'mds7st2' -Kind 'Location' -Label 'Move the upper Train Graveyard train' -X 823 -Y 3482 -Z 0 -TargetGameMoment 215 -MinimumGameMoment 212 -MaximumGameMoment 214 -Priority 0 -RequiredCondition (New-Condition 1 164 0x07 0x03) -EntityName 'line30' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 776; startY = 3453; startZ = 0; endX = 871; endY = 3511; endZ = 0 })
Add-Definition -FieldId 145 -FieldName 'mds7st2' -Kind 'Location' -Label 'Leave the Train Graveyard for Sector 7 Station' -X -1897 -Y 2644 -Z 0 -TargetGameMoment 215 -MinimumGameMoment 212 -MaximumGameMoment 214 -Priority 0 -RequiredCondition (New-Condition 1 164 0x07 0x07) -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -1993; startY = 2926; startZ = 0; endX = -1801; endY = 2363; endZ = 0 })
Add-Definition -FieldId 146 -FieldName 'mds7st3' -Kind 'Location' -Label 'Continue from the Train Graveyard to the Sector 7 pillar' -X -3796 -Y 1694 -Z 0 -TargetGameMoment 215 -MinimumGameMoment 212 -MaximumGameMoment 214 -Priority 0 -ScriptType 'Gateway'

# The playable pillar climb begins after the station scene at moment 221.
# pillar_2 has no forward gateway: talking to Barret at the top performs the
# native map jump. In pillar_3, Bank 5 byte 15 marks Reno defeated, Tifa's
# first Talk sets bytes 18 and 16, the sw line runs the plate-release scene
# and finally sets byte 19, and Barret's next Talk sets byte 23 before his
# escape script sets byte 7. Walking onto native triangle 18 or 19 then plays
# the wire-escape movie and advances GameMoment to 236.
Add-Definition -FieldId 156 -FieldName 'mds7plr1' -Kind 'Location' -Label 'Enter the Sector 7 pillar and begin climbing' -X 354 -Y 1253 -Z 139 -TargetGameMoment 236 -MinimumGameMoment 221 -MaximumGameMoment 235 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 319; startY = 1265; startZ = 130; endX = 388; endY = 1240; endZ = 148 })
Add-Definition -FieldId 158 -FieldName 'pillar_1' -Kind 'Location' -Label 'Climb to the upper section of the Sector 7 pillar' -X 112 -Y -1435 -Z 1816 -TargetGameMoment 236 -MinimumGameMoment 221 -MaximumGameMoment 235 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 112; startY = -1483; startZ = 1813; endX = 112; endY = -1387; endZ = 1818 })
Add-Definition -FieldId 159 -FieldName 'pillar_2' -Kind 'Model' -Label 'Talk to Barret at the top of the pillar' -EntityId 4 -TargetGameMoment 236 -MinimumGameMoment 221 -MaximumGameMoment 235 -Priority 0 -EntityName 'bal' -ScriptType 'Talk'
Add-Definition -FieldId 160 -FieldName 'pillar_3' -Kind 'Model' -Label 'Talk to Tifa about stopping the plate release' -EntityId 5 -TargetGameMoment 236 -MinimumGameMoment 221 -MaximumGameMoment 235 -Priority 0 -RequiredCondition (New-Condition 5 15 0xFF 1) -CompletedCondition (New-Condition 5 18 0xFF 1) -EntityName 'ti' -ScriptType 'Talk'
Add-Definition -FieldId 160 -FieldName 'pillar_3' -Kind 'Location' -Label 'Examine the plate release control panel' -X 295 -Y -147 -Z 6198 -TargetGameMoment 236 -MinimumGameMoment 221 -MaximumGameMoment 235 -Priority 0 -RequiredConditions @((New-Condition 5 18 0xFF 1), (New-Condition 5 16 0xFF 1)) -CompletedCondition (New-Condition 5 19 0xFF 1) -EntityName 'sw' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 213; startY = -265; startZ = 6198; endX = 377; endY = -30; endZ = 6198 })
Add-Definition -FieldId 160 -FieldName 'pillar_3' -Kind 'Model' -Label 'Talk to Barret and find an escape route' -EntityId 4 -TargetGameMoment 236 -MinimumGameMoment 221 -MaximumGameMoment 235 -Priority 0 -RequiredCondition (New-Condition 5 19 0xFF 1) -CompletedCondition (New-Condition 5 23 0xFF 1) -EntityName 'ba' -ScriptType 'Talk'
Add-Definition -FieldId 160 -FieldName 'pillar_3' -Kind 'Location' -Label 'Reach the wire to escape the collapsing pillar' -X 822 -Y 16 -Z 6208 -TargetGameMoment 236 -MinimumGameMoment 221 -MaximumGameMoment 235 -Priority 0 -RequiredConditions @((New-Condition 5 23 0xFF 1), (New-Condition 5 7 0xFF 1)) -EntityName 'dir' -ScriptType 'Native triangles 18 and 19'

# After Sector 7 collapses, Cloud initially has control alone at moment 239.
# Crossing mds6_1 entity 6's native LINE freezes movement, brings Barret and
# Tifa to Cloud, joins both party members, and advances GameMoment to 248.
# Do not expose the distant Sector 6 exit before this catch-up beat completes.
Add-Definition -FieldId 191 -FieldName 'mds6_1' -Kind 'Location' -Label 'Continue until Barret and Tifa catch up' -EntityId 6 -X 368 -Y 240 -Z 166 -TargetGameMoment 248 -MinimumGameMoment 239 -MaximumGameMoment 239 -Priority 0 -EntityName 'ev' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 220; startY = 343; startZ = 161; endX = 517; endY = 137; endZ = 171 })

# Once the party has joined, the native route returns through Sector 6 and
# Sector 5 to Aeris's house. The upstairs line, rather than Barret's no-op Talk
# script, starts the scene that advances the story to moment 257.
Add-Definition -FieldId 191 -FieldName 'mds6_1' -Kind 'Location' -Label "Return through Sector 6 toward Aeris's house" -X -1068 -Y -754 -Z 0 -TargetGameMoment 257 -MinimumGameMoment 248 -MaximumGameMoment 256 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 172 -FieldName 'mds5_3' -Kind 'Location' -Label "Continue through Sector 5 toward Aeris's house" -X 122 -Y 547 -Z 0 -TargetGameMoment 257 -MinimumGameMoment 248 -MaximumGameMoment 256 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 177 -FieldName 'mds5_1' -Kind 'Location' -Label "Enter Aeris's garden" -X 630 -Y 1314 -Z 0 -TargetGameMoment 257 -MinimumGameMoment 248 -MaximumGameMoment 256 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 187 -FieldName 'eals_1' -Kind 'Location' -Label "Enter Aeris's house" -EntityId 9 -X 12 -Y 98 -Z 0 -TargetGameMoment 257 -MinimumGameMoment 248 -MaximumGameMoment 256 -Priority 0 -EntityName 'll' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = -22; startY = 98; startZ = 0; endX = 46; endY = 98; endZ = 0 })
Add-Definition -FieldId 188 -FieldName 'ealin_1' -Kind 'Location' -Label 'Go upstairs to continue with Barret' -EntityId 14 -X 187 -Y 381 -Z 288 -TargetGameMoment 257 -MinimumGameMoment 248 -MaximumGameMoment 256 -Priority 0 -EntityName 'ev' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 206; startY = 425; startZ = 288; endX = 167; endY = 337; endZ = 288 })
Add-Definition -FieldId 189 -FieldName 'ealin_12' -Kind 'Location' -Label 'Go upstairs to continue with Barret' -EntityId 14 -X 187 -Y 381 -Z 288 -TargetGameMoment 257 -MinimumGameMoment 248 -MaximumGameMoment 256 -Priority 0 -EntityName 'ev' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 206; startY = 425; startZ = 288; endX = 167; endY = 337; endZ = 288 })

# Return to Wall Market, buy the three visible batteries, and use the rope.
# Bank 1 byte 165 bits 5-7 are set together by the native battery seller.
Add-Definition -FieldId 187 -FieldName 'eals_1' -Kind 'Location' -Label 'Leave the garden and return toward Wall Market' -X -188 -Y -149 -Z 0 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 177 -FieldName 'mds5_1' -Kind 'Location' -Label 'Backtrack through Sector 5 toward Wall Market' -X -944 -Y 4 -Z 0 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 172 -FieldName 'mds5_3' -Kind 'Location' -Label 'Enter Sector 6 on the way to Wall Market' -X -667 -Y -156 -Z 0 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 191 -FieldName 'mds6_1' -Kind 'Location' -Label 'Cross the collapsed expressway toward Wall Market' -EntityId 7 -X 1277 -Y 345 -Z 22 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -EntityName 'jp' -ScriptType 'Go 1x' -TriggerLine ([ordered]@{ startX = 1195; startY = 427; startZ = 22; endX = 1359; endY = 263; endZ = 22 })
Add-Definition -FieldId 193 -FieldName 'mds6_22' -Kind 'Location' -Label 'Leave the playground toward Wall Market' -X -424 -Y 1426 -Z 0 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 194 -FieldName 'mds6_3' -Kind 'Location' -Label 'Continue into Wall Market' -X 62 -Y 848 -Z 0 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 195 -FieldName 'mrkt2' -Kind 'Location' -Label 'Go north to the weapon shop for batteries' -X -135 -Y 2496 -Z 0 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 205 -FieldName 'mrkt1' -Kind 'Location' -Label 'Enter the weapon shop and buy batteries' -X 376 -Y -1266 -Z 0 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -CompletedCondition (New-Condition 1 165 0xE0 0xE0) -ScriptType 'Gateway'
Add-Definition -FieldId 196 -FieldName 'mkt_w' -Kind 'Model' -Label 'Buy three batteries for the wall climb' -EntityId 9 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -CompletedCondition (New-Condition 1 165 0xE0 0xE0) -EntityName 'oyaji02' -ScriptType 'Talk'
Add-Definition -FieldId 196 -FieldName 'mkt_w' -Kind 'Location' -Label 'Leave the weapon shop with the batteries' -X -18 -Y -102 -Z 0 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -RequiredCondition (New-Condition 1 165 0xE0 0xE0) -ScriptType 'Gateway'
Add-Definition -FieldId 205 -FieldName 'mrkt1' -Kind 'Location' -Label 'Go to the wall-climb entrance' -X 664 -Y -154 -Z 0 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -RequiredCondition (New-Condition 1 165 0xE0 0xE0) -ScriptType 'Gateway'
Add-Definition -FieldId 222 -FieldName 'mrkt4' -Kind 'Location' -Label 'Use the rope to begin climbing the wall' -EntityId 3 -X 5 -Y 630 -Z -1333 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -RequiredCondition (New-Condition 1 165 0xE0 0xE0) -EntityName 'line00' -ScriptType '[OK]' -TriggerLine ([ordered]@{ startX = -16; startY = 632; startZ = -1333; endX = 26; endY = 627; endZ = -1333 })

# The wall climb is a sequence of two battery sockets, the timed swinging-bar
# prompt, and the final native gateway. wcrimb_1's first propeller animation
# writes Bank 1 byte 165 bit 1 (0x02); bit 0 (0x01) only records inspecting
# the socket without installing a battery. The second socket writes bit 2
# (0x04), so the mandatory route is complete at mask 0x06. Keep the action
# targets active on arrival until those native state changes occur.
# Background segment MAPJUMPs are not exposed as separate goals.
Add-Definition -FieldId 223 -FieldName 'wcrimb_1' -Kind 'Location' -Label 'Place a battery in the first wall-climb socket' -EntityId 32 -X 304 -Y 724 -Z 1547 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -RequiredCondition (New-Condition 1 165 0xE0 0xE0) -CompletedCondition (New-Condition 1 165 0x02 0x02) -EntityName 'lined0' -ScriptType '[OK]' -TriggerLine ([ordered]@{ startX = 346; startY = 732; startZ = 1542; endX = 262; endY = 716; endZ = 1552 }) -KeepActiveOnArrival
Add-Definition -FieldId 223 -FieldName 'wcrimb_1' -Kind 'Location' -Label 'Place a battery in the second wall-climb socket' -EntityId 25 -X -72 -Y 1034 -Z 2280 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -RequiredCondition (New-Condition 1 165 0x02 0x02) -CompletedCondition (New-Condition 1 165 0x04 0x04) -EntityName 'line82' -ScriptType '[OK]' -TriggerLine ([ordered]@{ startX = -25; startY = 997; startZ = 2249; endX = -118; endY = 1071; endZ = 2311 }) -KeepActiveOnArrival
Add-Definition -FieldId 223 -FieldName 'wcrimb_1' -Kind 'Location' -Label 'Reach the swinging bar and press OK at the prompt' -X -369 -Y 1677 -Z 3290 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -RequiredCondition (New-Condition 1 165 0x06 0x06) -ExcludedPlayerTriangles @(36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 176, 177) -ScriptType 'Native triangle 13' -KeepActiveOnArrival
Add-Definition -FieldId 223 -FieldName 'wcrimb_1' -Kind 'Location' -Label 'Climb the final ladder after landing from the swinging bar' -EntityId 10 -X 280 -Y 2042 -Z 3240 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -RequiredCondition (New-Condition 1 165 0x06 0x06) -RequiredPlayerTriangles @(36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 176, 177) -EntityName 'line03' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 231; startY = 2040; startZ = 3235; endX = 328; endY = 2043; endZ = 3244 })
# The optional Ether socket is a one-way detour from wcrimb_2's upper route.
# The native return is the left LADER at entity 4, which briefly MAPJUMPs to
# wcrimb_1 and lands directly on the connected swinging-bar approach. Do not
# point this disconnected component at the final Shinra gateway.
$wallClimbOptionalReturnTriangles = @(
    28, 48, 49, 75, 76, 77, 78, 79, 80, 81, 82, 83,
    84, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96
)
Add-Definition -FieldId 224 -FieldName 'wcrimb_2' -Kind 'Location' -Label 'Descend the left ladder to return to the swinging bar' -EntityId 4 -X -263 -Y 888 -Z 2541 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -RequiredCondition (New-Condition 1 165 0x06 0x06) -RequiredPlayerTriangles $wallClimbOptionalReturnTriangles -EntityName 'Line 4' -ScriptType 'Native left LADER return'
Add-Definition -FieldId 224 -FieldName 'wcrimb_2' -Kind 'Location' -Label 'Finish the wall climb and enter the Shinra exterior' -X -2 -Y 1542 -Z 3981 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -RequiredCondition (New-Condition 1 165 0x06 0x06) -ExcludedPlayerTriangles $wallClimbOptionalReturnTriangles -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -48; startY = 1537; startZ = 3979; endX = 44; endY = 1547; endZ = 3982 })
Add-Definition -FieldId 225 -FieldName 'md0' -Kind 'Location' -Label 'Enter the Shinra Building' -X -3569 -Y -10770 -Z 485 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -RequiredCondition (New-Condition 1 165 0x06 0x06) -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -3669; startY = -10734; startZ = 485; endX = -3469; endY = -10806; endZ = 485 })

# Shinra entry preserves the same visible choice as the game: the front
# elevator or the long emergency stairs. Both converge on the 59th floor.
Add-Definition -FieldId 227 -FieldName 'sinbil_1' -Kind 'Location' -Label 'Take the front entrance into Shinra Headquarters' -X 7 -Y -1983 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 227 -FieldName 'sinbil_1' -Kind 'Location' -Label 'Take the emergency stairs into Shinra Headquarters' -X -1416 -Y -2165 -Z -73 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 234 -FieldName 'blin1' -Kind 'Location' -Label 'Take the left lobby elevator toward floor 59' -EntityId 15 -X -1558 -Y -117 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -EntityName 'ELINEL' -ScriptType '[OK]'
Add-Definition -FieldId 234 -FieldName 'blin1' -Kind 'Location' -Label 'Take the right lobby elevator toward floor 59' -EntityId 16 -X -1550 -Y 138 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -EntityName 'ELINER' -ScriptType '[OK]'
Add-Definition -FieldId 235 -FieldName 'blin2' -Kind 'Location' -Label 'Take the left lobby elevator toward floor 59' -EntityId 18 -X -1558 -Y -117 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -EntityName 'ELINEL' -ScriptType '[OK]'
Add-Definition -FieldId 235 -FieldName 'blin2' -Kind 'Location' -Label 'Take the right lobby elevator toward floor 59' -EntityId 19 -X -1550 -Y 138 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -EntityName 'ELINER' -ScriptType '[OK]'
Add-Definition -FieldId 237 -FieldName 'blin3_1' -Kind 'Location' -Label 'Take the left lobby elevator toward floor 59' -EntityId 12 -X -1558 -Y -117 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -EntityName 'ELINEL' -ScriptType '[OK]'
Add-Definition -FieldId 237 -FieldName 'blin3_1' -Kind 'Location' -Label 'Take the right lobby elevator toward floor 59' -EntityId 13 -X -1550 -Y 138 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -EntityName 'ELINER' -ScriptType '[OK]'
# SWITCH itself occupies isolated collision triangle 3 at (-105,136). Guide to
# the reachable face immediately in front of it so GPS does not route onto the
# non-walkable switch model.
Add-Definition -FieldId 232 -FieldName 'blinele' -Kind 'Location' -Label 'Press the elevator switch to continue toward floor 59' -EntityId 16 -X -105 -Y 110 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -EntityName 'SWITCH' -ScriptType '[OK]' -KeepActiveOnArrival
Add-Definition -FieldId 228 -FieldName 'sinbil_2' -Kind 'Location' -Label 'Continue up the emergency stairs' -X 196 -Y 1390 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 229 -FieldName 'blinst_1' -Kind 'Location' -Label 'Continue up the emergency stairs' -EntityId 6 -X 175 -Y 398 -Z 2169 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -EntityName 'LINEU' -ScriptType 'Move'
Add-Definition -FieldId 230 -FieldName 'blinst_2' -Kind 'Location' -Label 'Continue up the left stairway' -EntityId 10 -X 175 -Y 403 -Z 3069 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -ScriptType 'Move'
Add-Definition -FieldId 230 -FieldName 'blinst_2' -Kind 'Location' -Label 'Continue up the right stairway' -EntityId 12 -X 216 -Y 404 -Z 3038 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -ScriptType 'Move'
Add-Definition -FieldId 231 -FieldName 'blinst_3' -Kind 'Location' -Label 'Leave the stairs for the 59th floor by the left exit' -EntityId 9 -X 476 -Y 208 -Z 2043 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -ScriptType 'Move'
Add-Definition -FieldId 231 -FieldName 'blinst_3' -Kind 'Location' -Label 'Leave the stairs for the 59th floor by the right exit' -EntityId 10 -X 563 -Y 157 -Z 2042 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -ScriptType 'Move'

# The two diagonal floor-59 ambush lines are equivalent. The battle awards
# Keycard 60 by writing Bank 1 byte 224, after which either elevator line is
# valid and the player must choose floor 60 from the native selector.
Add-Definition -FieldId 238 -FieldName 'blin59' -Kind 'Location' -Label 'Approach the left guard group and obtain Keycard 60' -EntityId 23 -X 408 -Y -688 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 260 -MaximumGameMoment 262 -Priority 0 -CompletedCondition (New-Condition 1 224 0xFF 60) -EntityName 'KLINEB' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 60; startY = -466; startZ = 0; endX = 756; endY = -909; endZ = 0 })
Add-Definition -FieldId 238 -FieldName 'blin59' -Kind 'Location' -Label 'Approach the right guard group and obtain Keycard 60' -EntityId 24 -X 403 -Y -142 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 260 -MaximumGameMoment 262 -Priority 0 -CompletedCondition (New-Condition 1 224 0xFF 60) -EntityName 'KLINEA' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 0; startY = -538; startZ = 0; endX = 806; endY = 255; endZ = 0 })
Add-Definition -FieldId 238 -FieldName 'blin59' -Kind 'Location' -Label 'Enter the right floor elevator with Keycard 60' -EntityId 16 -X 604 -Y -643 -Z -1 -TargetGameMoment 263 -MinimumGameMoment 260 -MaximumGameMoment 262 -Priority 0 -RequiredCondition (New-Condition 1 224 0xFF 60) -EntityName 'DLINER' -ScriptType 'Move'
Add-Definition -FieldId 238 -FieldName 'blin59' -Kind 'Location' -Label 'Enter the left floor elevator with Keycard 60' -EntityId 17 -X 602 -Y -366 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 260 -MaximumGameMoment 262 -Priority 0 -RequiredCondition (New-Condition 1 224 0xFF 60) -EntityName 'DLINEL' -ScriptType 'Move'
Add-Definition -FieldId 233 -FieldName 'eleout' -Kind 'Location' -Label 'Use the elevator controls and choose floor 60' -EntityId 6 -X -2 -Y 36 -Z -1 -TargetGameMoment 263 -MinimumGameMoment 260 -MaximumGameMoment 262 -Priority 0 -RequiredCondition (New-Condition 1 224 0xFF 60) -EntityName 'lin0' -ScriptType '[OK]'

# Floor 60 has three equivalent security-room entrances. In the native timed
# sequence, LINE1 explains the guard timing and Bank 5 byte 13 advances as
# Tifa crosses each statue gap. At six, LINE2 lets Cloud take the party to the
# far side; Cloud's script then sets Bank 3 byte 172 bit 3 before the exit.
Add-Definition -FieldId 240 -FieldName 'blin60_2' -Kind 'Location' -Label 'Enter the security room by the left route' -X -440 -Y -920 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 240 -FieldName 'blin60_2' -Kind 'Location' -Label 'Enter the security room by the middle route' -X -85 -Y -778 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 240 -FieldName 'blin60_2' -Kind 'Location' -Label 'Enter the security room by the right route' -X 209 -Y -904 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 239 -FieldName 'blin60_1' -Kind 'Location' -Label 'Reach the floor 60 signaling point; press OK when the soldiers turn away' -EntityId 21 -X 5 -Y 66 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -CompletedCondition (New-Condition 5 13 0xFF 0 -AnyBitSet) -EntityName 'LINE1' -ScriptType 'Go 1x' -TriggerLine ([ordered]@{ startX = 5; startY = 258; startZ = 0; endX = 5; endY = -126; endZ = 0 })
Add-Definition -FieldId 239 -FieldName 'blin60_1' -Kind 'Location' -Label 'Continue signaling Barret and Tifa when the soldiers turn away' -X 7 -Y 204 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 5 13 0xFF 0 -AnyBitSet) -CompletedCondition (New-Condition 5 13 0xFF 6) -EntityName 'LINE1' -ScriptType 'Native signal state'
Add-Definition -FieldId 239 -FieldName 'blin60_1' -Kind 'Location' -Label 'Cross to the far side after Barret and Tifa are safely across' -EntityId 28 -X 551 -Y 51 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 5 13 0xFF 6) -CompletedCondition (New-Condition 3 172 0x08 0x08) -EntityName 'LINE2' -ScriptType 'Go 1x' -TriggerLine ([ordered]@{ startX = 553; startY = 291; startZ = 0; endX = 549; endY = -189; endZ = 0 })
Add-Definition -FieldId 239 -FieldName 'blin60_1' -Kind 'Location' -Label 'Continue to floor 61 after clearing security' -X 1078 -Y 189 -Z 227 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 3 172 0x08 0x08) -ScriptType 'Gateway'
Add-Definition -FieldId 241 -FieldName 'blin61' -Kind 'Location' -Label 'Continue to Mayor Domino on floor 62' -X 1166 -Y -148 -Z 207 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 1 59 0x01 0x01) -ScriptType 'Gateway'

# Once Mayor Domino's challenge is complete, Keycard 65 makes either elevator
# or the optional stair route valid.
Add-Definition -FieldId 242 -FieldName 'blin62_1' -Kind 'Location' -Label 'Use the left elevator and choose floor 65' -X -130 -Y -994 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 3 179 0x10 0x10) -ScriptType 'Move'
Add-Definition -FieldId 242 -FieldName 'blin62_1' -Kind 'Location' -Label 'Use the right elevator and choose floor 65' -X 131 -Y -994 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 3 179 0x10 0x10) -ScriptType 'Move'
Add-Definition -FieldId 242 -FieldName 'blin62_1' -Kind 'Location' -Label 'Take the stairs toward floor 65' -X 1060 -Y 212 -Z 222 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 3 179 0x10 0x10) -ScriptType 'Gateway'
Add-Definition -FieldId 233 -FieldName 'eleout' -Kind 'Location' -Label 'Use the elevator controls and choose floor 65' -X -2 -Y 36 -Z -1 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 3 179 0x10 0x10) -EntityName 'lin0' -ScriptType '[OK]'
Add-Definition -FieldId 245 -FieldName 'blin63_1' -Kind 'Location' -Label 'Continue up the stairs toward floor 65' -X 1182 -Y -150 -Z 230 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 3 179 0x10 0x10) -ScriptType 'Gateway'
Add-Definition -FieldId 247 -FieldName 'blin64' -Kind 'Location' -Label 'Continue up to floor 65' -X 1121 -Y 208 -Z 224 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 3 179 0x10 0x10) -ScriptType 'Gateway'

# Floor 65's native chest gates make this a strict state-backed sequence when
# the guide's counterclockwise placement order is followed. PARTD is the only
# chest initially open. Slots D, C, A, and B then unlock PARTB, PARTC, PARTA,
# and PARTE respectively; slot E also unlocks every remaining chest as the
# game's recovery path. Bank 1 byte 68 identifies which physical chest's part
# Cloud is carrying, while Bank 3 byte 180 records completed model sections.
Add-Definition -FieldId 248 -FieldName 'blin65_1' -Kind 'Model' -Label 'Open the lower chest in the upper-left room for the first Midgar part' -EntityId 18 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -CompletedCondition (New-Condition 1 57 0x02 0x02) -EntityName 'PARTD' -ScriptType 'Talk'
Add-Definition -FieldId 248 -FieldName 'blin65_1' -Kind 'Location' -Label 'Carry the Midgar part into the model room' -X 1 -Y -448 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 1 68 0xF8 0 -AnyBitSet) -ScriptType 'Gateway'

Add-Definition -FieldId 249 -FieldName 'blin65_2' -Kind 'Location' -Label 'Place the first Midgar part in the bottom-right model slot' -X 342 -Y -194 -Z -3 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 1 68 0x40 0x40) -CompletedCondition (New-Condition 3 180 0x40 0x40) -ScriptType 'Native triangle 70' -KeepActiveOnArrival
Add-Definition -FieldId 249 -FieldName 'blin65_2' -Kind 'Location' -Label 'Place the second Midgar part in the next counterclockwise model slot' -X 294 -Y 170 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 1 68 0x10 0x10) -CompletedCondition (New-Condition 3 180 0x20 0x20) -ScriptType 'Native triangle 33' -KeepActiveOnArrival
Add-Definition -FieldId 249 -FieldName 'blin65_2' -Kind 'Location' -Label 'Place the third Midgar part in the next counterclockwise model slot' -X -342 -Y 89 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 1 68 0x20 0x20) -CompletedCondition (New-Condition 3 180 0x08 0x08) -ScriptType 'Native triangle 75' -KeepActiveOnArrival
Add-Definition -FieldId 249 -FieldName 'blin65_2' -Kind 'Location' -Label 'Place the fourth Midgar part in the next counterclockwise model slot' -X -149 -Y 283 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 1 68 0x08 0x08) -CompletedCondition (New-Condition 3 180 0x10 0x10) -ScriptType 'Native triangle 68' -KeepActiveOnArrival
Add-Definition -FieldId 249 -FieldName 'blin65_2' -Kind 'Location' -Label 'Place the final Midgar part in the last model slot' -X 128 -Y -316 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredConditions @((New-Condition 1 57 0x04 0x04), (New-Condition 3 180 0x78 0x78)) -CompletedCondition (New-Condition 3 180 0x80 0x80) -ScriptType 'Native triangle 26' -KeepActiveOnArrival

Add-Definition -FieldId 249 -FieldName 'blin65_2' -Kind 'Location' -Label 'Return to the outer floor for the next unlocked chest' -X 1 -Y -455 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredConditions @((New-Condition 1 68 0xF8 0), (New-Condition 3 180 0xF8 0 -AnyBitSet)) -CompletedCondition (New-Condition 1 57 0x04 0x04) -ScriptType 'Gateway'
Add-Definition -FieldId 249 -FieldName 'blin65_2' -Kind 'Location' -Label 'Return to the outer floor and collect Keycard 66' -X 1 -Y -455 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredConditions @((New-Condition 1 68 0xF8 0), (New-Condition 3 180 0xF8 0xF8)) -ScriptType 'Gateway'

Add-Definition -FieldId 248 -FieldName 'blin65_1' -Kind 'Model' -Label 'Open the lower chest in the lower-left room for the second Midgar part' -EntityId 16 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 3 180 0xC0 0 -AnyBitSet) -CompletedCondition (New-Condition 1 56 0x80 0x80) -EntityName 'PARTB' -ScriptType 'Talk'
Add-Definition -FieldId 248 -FieldName 'blin65_1' -Kind 'Model' -Label 'Open the remaining chest in the upper-left room for the third Midgar part' -EntityId 17 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 3 180 0xA0 0 -AnyBitSet) -CompletedCondition (New-Condition 1 57 0x01 0x01) -EntityName 'PARTC' -ScriptType 'Talk'
Add-Definition -FieldId 248 -FieldName 'blin65_1' -Kind 'Model' -Label 'Open the remaining chest in the lower-left room for the fourth Midgar part' -EntityId 15 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 3 180 0x88 0 -AnyBitSet) -CompletedCondition (New-Condition 1 56 0x40 0x40) -EntityName 'PARTA' -ScriptType 'Talk'
Add-Definition -FieldId 248 -FieldName 'blin65_1' -Kind 'Model' -Label 'Open the chest in the upper-right room for the final Midgar part' -EntityId 19 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 3 180 0x90 0 -AnyBitSet) -CompletedCondition (New-Condition 1 57 0x04 0x04) -EntityName 'PARTE' -ScriptType 'Talk'
Add-Definition -FieldId 248 -FieldName 'blin65_1' -Kind 'Model' -Label 'Collect Keycard 66 from the completed Midgar model' -EntityId 20 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 3 180 0xF8 0xF8) -CompletedCondition (New-Condition 1 224 0xFF 66) -EntityName 'TAKARA' -ScriptType 'Talk'
Add-Definition -FieldId 248 -FieldName 'blin65_1' -Kind 'Location' -Label 'Continue to floor 66 with Keycard 66' -X 1196 -Y -168 -Z 230 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 1 224 0xFF 66) -ScriptType 'Gateway'

# Floor 66 writes moment 264 as the party arrives. The restroom vent, Hojo
# trail, Jenova chamber, and laboratory gateways are native activators.
Add-Definition -FieldId 250 -FieldName 'blin66_1' -Kind 'Location' -Label 'Enter the restroom to reach the conference vent' -X -982 -Y 584 -Z 0 -TargetGameMoment 269 -MinimumGameMoment 264 -MaximumGameMoment 268 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 252 -FieldName 'blin66_3' -Kind 'Location' -Label 'Choose climb up at the bathroom vent' -EntityId 2 -X 64 -Y 146 -Z -376 -TargetGameMoment 269 -MinimumGameMoment 264 -MaximumGameMoment 268 -Priority 0 -EntityName 'vent' -ScriptType '[OK]' -TriggerLine ([ordered]@{ startX = 65; startY = 98; startZ = -376; endX = 62; endY = 193; endZ = -376 })
Add-Definition -FieldId 253 -FieldName 'blin66_4' -Kind 'Location' -Label 'Crawl down the duct to eavesdrop on the conference' -X 448 -Y -536 -Z -28 -TargetGameMoment 269 -MinimumGameMoment 264 -MaximumGameMoment 268 -Priority 0 -EntityName 'cl' -ScriptType 'Native LADER, advance Down'
Add-Definition -FieldId 256 -FieldName 'blin67_1' -Kind 'Location' -Label 'Ride the floor 67 elevator to floor 68' -EntityId 18 -X -435 -Y 976 -Z 0 -TargetGameMoment 284 -MinimumGameMoment 280 -MaximumGameMoment 283 -Priority 0 -EntityName 'ele0' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = -475; startY = 891; startZ = 0; endX = -396; endY = 1061; endZ = 0 })
Add-Definition -FieldId 256 -FieldName 'blin67_1' -Kind 'Location' -Label 'Enter Hojo laboratory on floor 68' -X 1185 -Y -142 -Z 224 -TargetGameMoment 284 -MinimumGameMoment 280 -MaximumGameMoment 283 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 262 -FieldName 'blin68_1' -Kind 'Location' -Label 'Enter the principal laboratory chamber and rescue Aeris' -X -604 -Y 3 -Z 0 -TargetGameMoment 284 -MinimumGameMoment 280 -MaximumGameMoment 283 -Priority 0 -ScriptType 'Gateway'

# After the laboratory boss, collect Keycard 68 and backtrack by stairs or
# elevator. The floor-elevator OK line initiates the capture sequence.
Add-Definition -FieldId 262 -FieldName 'blin68_1' -Kind 'Location' -Label 'Leave floor 68 by the elevator' -X -412 -Y 1030 -Z 0 -TargetGameMoment 290 -MinimumGameMoment 284 -MaximumGameMoment 289 -Priority 0 -RequiredCondition (New-Condition 1 226 0x20 0x20) -ScriptType 'Move'
Add-Definition -FieldId 262 -FieldName 'blin68_1' -Kind 'Location' -Label 'Leave floor 68 by the stairs' -X 1060 -Y -103 -Z -104 -TargetGameMoment 290 -MinimumGameMoment 284 -MaximumGameMoment 289 -Priority 0 -RequiredCondition (New-Condition 1 226 0x20 0x20) -ScriptType 'Move'
Add-Definition -FieldId 256 -FieldName 'blin67_1' -Kind 'Location' -Label 'Continue down toward the floor elevators' -X 854 -Y 121 -Z -162 -TargetGameMoment 290 -MinimumGameMoment 284 -MaximumGameMoment 289 -Priority 0 -RequiredCondition (New-Condition 1 226 0x20 0x20) -ScriptType 'Gateway'
Add-Definition -FieldId 250 -FieldName 'blin66_1' -Kind 'Location' -Label 'Enter the left floor elevator' -X -130 -Y -984 -Z 0 -TargetGameMoment 290 -MinimumGameMoment 284 -MaximumGameMoment 289 -Priority 0 -RequiredCondition (New-Condition 1 226 0x20 0x20) -ScriptType 'Move'
Add-Definition -FieldId 250 -FieldName 'blin66_1' -Kind 'Location' -Label 'Enter the right floor elevator' -X 131 -Y -984 -Z 0 -TargetGameMoment 290 -MinimumGameMoment 284 -MaximumGameMoment 289 -Priority 0 -RequiredCondition (New-Condition 1 226 0x20 0x20) -ScriptType 'Move'

# Prison progression is local-state driven. Sleeping leaves the cell door open;
# its native line clears bank 1 address 232 bit 0x10. The dead guard must then
# set temporary bank 5 address 21 before Tifa's wake-up branch can run.
Add-Definition -FieldId 258 -FieldName 'blin67_2' -Kind 'Location' -Label 'Step through the open cell door and investigate' -EntityId 7 -X 884 -Y 512 -Z 0 -TargetGameMoment 302 -MinimumGameMoment 296 -MaximumGameMoment 301 -Priority 0 -RequiredCondition (New-Condition 1 232 0x10 0x10) -CompletedCondition (New-Condition 1 232 0x10 0) -EntityName 'ln0' -ScriptType 'Go 1x' -TriggerLine ([ordered]@{ startX = 813; startY = 512; startZ = 0; endX = 954; endY = 512; endZ = 0 }) -KeepActiveOnArrival
Add-Definition -FieldId 258 -FieldName 'blin67_2' -Kind 'Model' -Label 'Inspect the dead guard outside the cell' -EntityId 6 -TargetGameMoment 302 -MinimumGameMoment 296 -MaximumGameMoment 301 -Priority 0 -RequiredConditions @((New-Condition 1 232 0x10 0), (New-Condition 5 21 0xFF 0)) -CompletedCondition (New-Condition 5 21 0xFF 1) -EntityName 'sikabane' -ScriptType 'Talk'
Add-Definition -FieldId 258 -FieldName 'blin67_2' -Kind 'Model' -Label 'Wake Tifa and talk to her' -EntityId 3 -TargetGameMoment 302 -MinimumGameMoment 296 -MaximumGameMoment 301 -Priority 0 -RequiredConditions @((New-Condition 5 21 0xFF 1), (New-Condition 5 16 0xFF 0)) -CompletedCondition (New-Condition 5 16 0xFF 1) -EntityName 'ti' -ScriptType 'Talk'
Add-Definition -FieldId 258 -FieldName 'blin67_2' -Kind 'Model' -Label 'Talk to Tifa again and leave the cell' -EntityId 3 -TargetGameMoment 302 -MinimumGameMoment 296 -MaximumGameMoment 301 -Priority 0 -RequiredConditions @((New-Condition 5 16 0xFF 1), (New-Condition 5 21 0xFF 1)) -CompletedCondition (New-Condition 5 16 0xFF 2) -EntityName 'ti' -ScriptType 'Talk'
Add-Definition -FieldId 258 -FieldName 'blin67_2' -Kind 'Model' -Label 'Talk to Barret after leaving the cell' -EntityId 4 -TargetGameMoment 302 -MinimumGameMoment 296 -MaximumGameMoment 301 -Priority 0 -RequiredCondition (New-Condition 5 16 0xFF 2) -CompletedCondition (New-Condition 5 20 0xFF 1) -EntityName 'ba' -ScriptType 'Talk'
Add-Definition -FieldId 258 -FieldName 'blin67_2' -Kind 'Model' -Label 'Talk to Red XIII after leaving the cell' -EntityId 5 -TargetGameMoment 302 -MinimumGameMoment 296 -MaximumGameMoment 301 -Priority 0 -RequiredCondition (New-Condition 5 16 0xFF 2) -CompletedCondition (New-Condition 5 20 0xFF 1) -EntityName 'red' -ScriptType 'Talk'
Add-Definition -FieldId 258 -FieldName 'blin67_2' -Kind 'Location' -Label 'Follow the blood trail out of the cell block' -EntityId 7 -X 412 -Y 690 -Z 0 -TargetGameMoment 302 -MinimumGameMoment 296 -MaximumGameMoment 301 -Priority 0 -RequiredCondition (New-Condition 5 20 0xFF 1) -CompletedCondition (New-Condition 5 20 0xFF 2) -EntityName 'ln2' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 406; startY = 751; startZ = 0; endX = 418; endY = 628; endZ = 0 })
Add-Definition -FieldId 258 -FieldName 'blin67_2' -Kind 'Model' -Label 'Talk to Tifa and form the pursuit party' -EntityId 3 -TargetGameMoment 302 -MinimumGameMoment 296 -MaximumGameMoment 301 -Priority 0 -RequiredCondition (New-Condition 5 20 0xFF 2) -EntityName 'ti' -ScriptType 'Talk'
Add-Definition -FieldId 258 -FieldName 'blin67_2' -Kind 'Model' -Label 'Talk to Aeris and form the pursuit party' -EntityId 2 -TargetGameMoment 302 -MinimumGameMoment 296 -MaximumGameMoment 301 -Priority 0 -RequiredCondition (New-Condition 5 20 0xFF 2) -EntityName 'ea' -ScriptType 'Talk'

# Follow the blood trail through the Jenova chamber and up to floor 70.
Add-Definition -FieldId 257 -FieldName 'blin671b' -Kind 'Location' -Label 'Follow the blood trail through the left chamber entrance' -X -779 -Y -604 -Z 0 -TargetGameMoment 305 -MinimumGameMoment 302 -MaximumGameMoment 304 -Priority 0 -ScriptType 'Move'
Add-Definition -FieldId 257 -FieldName 'blin671b' -Kind 'Location' -Label 'Follow the blood trail through the middle chamber entrance' -X -680 -Y -253 -Z 0 -TargetGameMoment 305 -MinimumGameMoment 302 -MaximumGameMoment 304 -Priority 0 -ScriptType 'Move'
Add-Definition -FieldId 257 -FieldName 'blin671b' -Kind 'Location' -Label 'Follow the blood trail through the right chamber entrance' -X -1018 -Y -43 -Z 47 -TargetGameMoment 305 -MinimumGameMoment 302 -MaximumGameMoment 304 -Priority 0 -ScriptType 'Move'
Add-Definition -FieldId 260 -FieldName 'blin673b' -Kind 'Model' -Label 'Talk to Red XIII in the Jenova chamber' -EntityId 4 -TargetGameMoment 305 -MinimumGameMoment 302 -MaximumGameMoment 304 -Priority 0 -CompletedCondition (New-Condition 1 226 0x80 0x80) -EntityName 'red' -ScriptType 'Talk'
Add-Definition -FieldId 257 -FieldName 'blin671b' -Kind 'Location' -Label 'Use the specimen elevator after speaking with Red XIII' -X -436 -Y 976 -Z 0 -TargetGameMoment 305 -MinimumGameMoment 302 -MaximumGameMoment 304 -Priority 0 -RequiredCondition (New-Condition 1 226 0x80 0x80) -EntityName 'ele0' -ScriptType 'Move'
Add-Definition -FieldId 262 -FieldName 'blin68_1' -Kind 'Location' -Label 'Continue to floor 69' -X 1076 -Y 184 -Z 222 -TargetGameMoment 305 -MinimumGameMoment 302 -MaximumGameMoment 304 -Priority 0 -RequiredCondition (New-Condition 1 226 0x80 0x80) -ScriptType 'Gateway'
Add-Definition -FieldId 264 -FieldName 'blin69_1' -Kind 'Location' -Label 'Enter floor 70 by the left route' -X -354 -Y 770 -Z 318 -TargetGameMoment 305 -MinimumGameMoment 302 -MaximumGameMoment 304 -Priority 0 -RequiredCondition (New-Condition 1 226 0x80 0x80) -ScriptType 'Gateway'
Add-Definition -FieldId 264 -FieldName 'blin69_1' -Kind 'Location' -Label 'Enter floor 70 by the right route' -X -374 -Y -765 -Z 306 -TargetGameMoment 305 -MinimumGameMoment 302 -MaximumGameMoment 304 -Priority 0 -RequiredCondition (New-Condition 1 226 0x80 0x80) -ScriptType 'Gateway'

# At moment 302 the floor-70 director transfers to the President scene only
# after Cloud crosses x > 281. Field 268 is reachable before that scene, so it
# also receives a state-correct recovery route through its only gateway.
Add-Definition -FieldId 266 -FieldName 'blin70_1' -Kind 'Location' -Label "Enter President Shinra's office and investigate" -X 300 -Y 855 -Z 4 -TargetGameMoment 305 -MinimumGameMoment 302 -MaximumGameMoment 304 -Priority 0 -ScriptType 'Native director x threshold' -TriggerLine ([ordered]@{ startX = 282; startY = 769; startZ = 4; endX = 282; endY = 937; endZ = 4 })
Add-Definition -FieldId 268 -FieldName 'blin70_3' -Kind 'Location' -Label "Return inside and enter President Shinra's office" -X -1220 -Y 450 -Z 182 -TargetGameMoment 305 -MinimumGameMoment 302 -MaximumGameMoment 304 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -1312; startY = 378; startZ = 182; endX = -1128; endY = 521; endZ = 182 })

# Automatic President, elevator-boss, and Rufus scenes remain silent. These
# targets appear only when control is returned between those sequences.
Add-Definition -FieldId 266 -FieldName 'blin70_1' -Kind 'Location' -Label 'Continue to the roof to confront Rufus' -X 240 -Y 1178 -Z 4 -TargetGameMoment 308 -MinimumGameMoment 305 -MaximumGameMoment 307 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 210; startY = 1184; startZ = 4; endX = 270; endY = 1172; endZ = 4 })
Add-Definition -FieldId 264 -FieldName 'blin69_1' -Kind 'Location' -Label 'Enter the left elevator for the escape battles' -X -132 -Y -995 -Z 0 -TargetGameMoment 314 -MinimumGameMoment 311 -MaximumGameMoment 313 -Priority 0 -ScriptType 'Move'
Add-Definition -FieldId 264 -FieldName 'blin69_1' -Kind 'Location' -Label 'Enter the right elevator for the escape battles' -X 129 -Y -995 -Z 0 -TargetGameMoment 314 -MinimumGameMoment 311 -MaximumGameMoment 313 -Priority 0 -ScriptType 'Move'
Add-Definition -FieldId 233 -FieldName 'eleout' -Kind 'Location' -Label 'Use the elevator controls to begin the escape battles' -X -2 -Y 36 -Z -1 -TargetGameMoment 314 -MinimumGameMoment 311 -MaximumGameMoment 313 -Priority 0 -EntityName 'lin0' -ScriptType '[OK]'
Add-Definition -FieldId 268 -FieldName 'blin70_3' -Kind 'Location' -Label 'Return inside after defeating Rufus' -X -1220 -Y 450 -Z 182 -TargetGameMoment 323 -MinimumGameMoment 320 -MaximumGameMoment 322 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -1312; startY = 378; startZ = 182; endX = -1128; endY = 521; endZ = 182 })
Add-Definition -FieldId 266 -FieldName 'blin70_1' -Kind 'Location' -Label 'Take the upper stairs down to meet Tifa' -X -718 -Y 679 -Z -172 -TargetGameMoment 323 -MinimumGameMoment 320 -MaximumGameMoment 322 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -760; startY = 706; startZ = -191; endX = -675; endY = 651; endZ = -153 })
Add-Definition -FieldId 264 -FieldName 'blin69_1' -Kind 'Model' -Label 'Talk to Tifa and begin the motorcycle escape' -EntityId 5 -TargetGameMoment 323 -MinimumGameMoment 320 -MaximumGameMoment 322 -Priority 0 -EntityName 'ti' -ScriptType 'Talk'

# The motorcycle itself and Motor Ball are automatic. LINEO starts the escape;
# after roadend, the outskirts line opens party choice and then the final
# gateway leaves Midgar for the world map.
Add-Definition -FieldId 234 -FieldName 'blin1' -Kind 'Location' -Label 'Meet the party at the lobby exit and start the motorcycle escape' -EntityId 19 -X 1846 -Y 8 -Z 0 -TargetGameMoment 332 -MinimumGameMoment 326 -MaximumGameMoment 331 -Priority 0 -EntityName 'LINEO' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 1855; startY = 126; startZ = 0; endX = 1837; endY = -110; endZ = 0 })
Add-Definition -FieldId 170 -FieldName 'mds5_5' -Kind 'Location' -Label 'Meet the party and choose the group for the journey' -EntityId 8 -X 563 -Y -2830 -Z 0 -TargetGameMoment 341 -MinimumGameMoment 335 -MaximumGameMoment 340 -Priority 0 -EntityName 'ln0' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 22; startY = -2723; startZ = 0; endX = 1104; endY = -2938; endZ = 0 })
Add-Definition -FieldId 170 -FieldName 'mds5_5' -Kind 'Location' -Label 'Leave Midgar for the world map' -X 472 -Y -2877 -Z 0 -MinimumGameMoment 341 -MaximumGameMoment 341 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 47; startY = -2777; startZ = 0; endX = 897; endY = -2977; endZ = 0 })

# These Shinra Building objectives have unambiguous native model or LINE
# targets. Ambiguous alternate guard lines and model-slot triangle puzzles are
# intentionally omitted until they can be represented without false targets.
Add-Definition -FieldId 241 -FieldName 'blin61' -Kind 'Model' -Label 'Talk to the employee and choose silence to receive Keycard 62' -EntityId 12 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -CompletedCondition (New-Condition 1 59 0x01 0x01) -EntityName 'ZAKOA' -ScriptType 'Talk'
Add-Definition -FieldId 242 -FieldName 'blin62_1' -Kind 'Model' -Label 'Ask Mayor Domino for the password challenge' -EntityId 26 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -CompletedCondition (New-Condition 3 179 0x08 0x08) -EntityName 'DOMINO' -ScriptType 'Talk'
Add-Definition -FieldId 242 -FieldName 'blin62_1' -Kind 'Model' -Label "Answer Mayor Domino's password" -EntityId 26 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 3 179 0x08 0x08) -CompletedCondition (New-Condition 3 179 0x10 0x10) -EntityName 'DOMINO' -ScriptType 'Talk'
Add-Definition -FieldId 250 -FieldName 'blin66_1' -Kind 'Location' -Label 'Follow Hojo out of the meeting' -X -346 -Y -1024 -Z 0 -TargetGameMoment 270 -MinimumGameMoment 269 -MaximumGameMoment 269 -Priority 0 -EntityName 'hw' -ScriptType 'Move'
Add-Definition -FieldId 250 -FieldName 'blin66_1' -Kind 'Location' -Label 'Follow Hojo toward the 67th floor' -X 548 -Y -1044 -Z 0 -TargetGameMoment 271 -MinimumGameMoment 270 -MaximumGameMoment 270 -Priority 0 -EntityName 'lookh' -ScriptType 'Move'
Add-Definition -FieldId 250 -FieldName 'blin66_1' -Kind 'Location' -Label 'Take the stairs to the 67th floor' -X 1051 -Y 209 -Z 196 -TargetGameMoment 272 -MinimumGameMoment 271 -MaximumGameMoment 271 -Priority 0 -EntityName 'st0' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 1051; startY = 237; startZ = 197; endX = 1050; endY = 181; endZ = 195 })
Add-Definition -FieldId 256 -FieldName 'blin67_1' -Kind 'Location' -Label 'Follow Hojo across the 67th floor' -X 153 -Y -1060 -Z 0 -TargetGameMoment 273 -MinimumGameMoment 272 -MaximumGameMoment 272 -Priority 0 -EntityName 'ln0' -ScriptType 'Move'
Add-Definition -FieldId 259 -FieldName 'blin67_3' -Kind 'Location' -Label 'Approach Hojo and the Jenova chamber' -X -786 -Y -530 -Z 0 -TargetGameMoment 278 -MinimumGameMoment 273 -MaximumGameMoment 273 -Priority 0 -EntityName 'ln0' -ScriptType 'Move'
Add-Definition -FieldId 256 -FieldName 'blin67_1' -Kind 'Location' -Label 'Continue after Jenova; follow Hojo to the lab' -X 153 -Y -1060 -Z 0 -TargetGameMoment 280 -MinimumGameMoment 278 -MaximumGameMoment 278 -Priority 0 -EntityName 'ln0' -ScriptType 'Move'
Add-Definition -FieldId 262 -FieldName 'blin68_1' -Kind 'Model' -Label 'Talk to the lab assistant for Keycard 68' -EntityId 7 -MinimumGameMoment 284 -MaximumGameMoment 284 -Priority 0 -CompletedCondition (New-Condition 1 226 0x20 0x20) -EntityName 'ZAKOA' -ScriptType 'Talk'
Add-Definition -FieldId 233 -FieldName 'eleout' -Kind 'Location' -Label 'Enter the 66th-floor elevator and press OK' -X -2 -Y 36 -Z -1 -TargetGameMoment 290 -MinimumGameMoment 284 -MaximumGameMoment 284 -Priority 0 -EntityName 'lin0' -ScriptType '[OK]'
Add-Definition -FieldId 258 -FieldName 'blin67_2' -Kind 'Location' -Label 'Use the cell door and consider each party member until sleep is offered' -X 884 -Y 512 -Z 0 -TargetGameMoment 296 -MinimumGameMoment 293 -MaximumGameMoment 293 -Priority 0 -EntityName 'ln0' -ScriptType '[OK]'

$deduplicated = @($definitions |
    Where-Object {
        -not ($_.fieldId -eq 144 -and
              $_.targetGameMoment -eq 212 -and
              $_.label -eq 'Continue Sewers and Train Graveyard')
    } |
    Group-Object { "$($_.fieldId):$($_.kind):$($_.entityId):$($_.x):$($_.y):$($_.z):$($_.targetGameMoment):$($_.label):$($_.priority)" } |
    ForEach-Object { $_.Group[0] } |
    Sort-Object fieldId, priority, targetGameMoment, entityId, label)

$sourceCommit = (git -C $KujataDataRoot rev-parse HEAD).Trim()
$document = [ordered]@{
    schemaVersion = 1
    source = 'dangarfeld/kujata-data native field script progression extraction'
    sourceCommit = $sourceCommit
    scannedGameMomentWrites = $milestoneCount
    navigableGameMomentWrites = $navigableMilestoneCount
    unresolvedAutomaticOrIndirectWrites = $unresolved.Count
    definitionCount = $deduplicated.Count
    definitions = $deduplicated
}

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$document | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Host "Generated $($deduplicated.Count) FFVII story objectives from $milestoneCount native GameMoment writes at $OutputPath"
Write-Host "Navigable writes: $navigableMilestoneCount; automatic or unresolved writes: $($unresolved.Count)"
