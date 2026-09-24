param(
    [string] $GameRoot = '',
    [string] $KujataDataRoot = '',
    [string] $OutputPath = '',

    # Provenance for the Kujata data the catalog was extracted from. Supply it
    # explicitly when the caller already knows the revision, or when the caller is
    # not permitted to run Git at all. Left empty, the script falls back to asking
    # the checkout itself, exactly as it always did.
    [string] $SourceCommit = '',

    # Where the coverage ledger is written. The ledger is what makes the catalog
    # reviewable: a definition count says nothing about whether a chapter can
    # actually be played through.
    [string] $LedgerPath = '',

    # Verified local copies of extracted field files that the cloud store has taken
    # offline. Only consulted for a file the filesystem itself reports as an offline
    # placeholder, so it can never mask a corrupted or edited input.
    [string] $SupplementRoot = ''
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

# The reverse of $fieldIds, so a native MAPJUMP's numeric destination can be named.
$idNames = @{}
foreach ($entry in $fieldIds.GetEnumerator()) {
    if (-not $idNames.ContainsKey($entry.Value)) {
        $idNames[$entry.Value] = $entry.Key
    }
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

# Filled while the field data is parsed below, and consumed by the transit
# continuity pass. A chapter is only playable end to end if the rooms between its
# milestones say which way is on, and neither of these can be recovered from the
# catalog after the fact.
$gatewaysByField = @{}
$momentWritesByField = @{}
$unreadableFields = [Collections.Generic.List[string]]::new()
$suppliedFields = [Collections.Generic.List[string]]::new()

# Fields a reviewed region has taken responsibility for. Those regions encode which
# moments a place is and is not on the story route for - a mountain the party crosses
# once on the way in and again on unrelated business later is the usual case - and
# that judgement cannot be recovered from geometry. The transit continuity pass fills
# only ground no region has claimed.
# Doors into a room whose own entry scripts advance the story, and the fields whose
# Director switches the gateway table off so that its doors are not doors at all.
$entryWritesByField = @{}
$gatewaysDisabledFields = @{}

# Where a conditional actually sends control when its test fails.
#
# The extracted JSON's `goto` is one byte too large for the long conditional forms.
# Root read the installed dispatch table at 009055A0: IFUB 0x14's false branch advances
# IP + 5 + raw[5], IFUBL 0x15's advances IP + 5 + uint16(raw[5..6]) and IFSWL 0x17's
# advances IP + 7 + uint16(raw[7..8]). Measuring the installed data agrees and says the
# error is confined to those forms: across 150 field files every one of 3034 JMPF, 268
# JMPFL, 1549 JMPB, 35 JMPBL, 4569 IFUB and 1377 IFSW targets lands on an opcode
# boundary, while 202 of 208 IFUBL, 26 of 31 IFSWL and all 3 IFUWL targets only do so
# after subtracting one - the handful of exceptions being positions where both the
# value and the value minus one happen to be boundaries.
$longConditionalOps = @('IFUBL', 'IFSWL', 'IFUWL', 'IFKEYL')
function Get-JumpTarget {
    param($Op)

    if ($null -eq $Op.goto) { return $null }
    $target = [int]$Op.goto
    if ([string]$Op.op -in $longConditionalOps) { $target-- }
    return $target
}

# Every jump in a script, once, so the two questions below do not each re-walk it.
function Get-ScriptJumps {
    param($Script)

    $jumps = [Collections.Generic.List[object]]::new()
    foreach ($op in $Script.ops) {
        $target = Get-JumpTarget $op
        if ($null -eq $target) { continue }
        $jumps.Add([pscustomobject]@{
            Start = [int]$op.byteIndex
            Target = $target
            IsConditional = ([string]$op.op).StartsWith('IF')
            Js = [string]$op.js
        })
    }
    return $jumps
}

# The GameMoment tests that have to hold for a byte position to be reached at all.
#
# A conditional's body is the byte range between the test and where it jumps on
# failure, so a test whose body contains the position is a condition on reaching it -
# but only if nothing else jumps into that body from outside, which would be a way in
# that skips the test. Both halves matter: without the first there is no band, and
# without the second the band can be wrong.
function Get-ExactGameMomentGuards {
    param($Script, [int] $ByteIndex)

    $jumps = Get-ScriptJumps $Script
    $exact = [Collections.Generic.List[int]]::new()
    $other = 0
    foreach ($jump in $jumps) {
        if (-not $jump.IsConditional) { continue }
        if ($ByteIndex -le $jump.Start -or $ByteIndex -ge $jump.Target) { continue }

        $bypassed = $false
        foreach ($inbound in $jumps) {
            if ($inbound.Target -le $jump.Start -or $inbound.Target -gt $ByteIndex) { continue }
            if ($inbound.Start -gt $jump.Start -and $inbound.Start -lt $ByteIndex) { continue }
            $bypassed = $true
            break
        }
        if ($bypassed) { continue }

        if ($jump.Js -match '^if \(Bank\[2\]\[0\] === (\d+)\)') {
            $exact.Add([int]$Matches[1])
        }
        else {
            $other++
        }
    }
    return [pscustomobject]@{ Exact = $exact; Other = $other }
}

# Everything that can carry control past a byte position without running it.
#
# Lexical nesting is not proof that a write happens. A script can jump clean over the
# block holding it from outside every conditional that encloses it: subin_1b's Director
# Main tests a saved flag and takes a long forward jump straight past the whole 1286
# block, which no enclosing test of that block would ever reveal. So the question asked
# for an automatic arrival is the one that decides it - which ops, sitting before the
# write, can send control past it? Every one of them has to be the same single
# GameMoment equality test, or walking into the room is not what performs the write.
function Get-WriteSkippers {
    param($Script, [int] $ByteIndex)

    $exact = [Collections.Generic.List[int]]::new()
    $other = 0
    foreach ($jump in (Get-ScriptJumps $Script)) {
        if ($jump.Start -ge $ByteIndex -or $jump.Target -le $ByteIndex) { continue }
        if ($jump.IsConditional -and $jump.Js -match '^if \(Bank\[2\]\[0\] === (\d+)\)') {
            $exact.Add([int]$Matches[1])
        }
        else {
            $other++
        }
    }
    return [pscustomobject]@{ Exact = $exact; Other = $other }
}

# A field's own call graph, keyed the way the engine keys it.
#
# A request names an entity and a script by their native numbers, and those are not
# array positions in the extracted JSON. Kujata lists Init and Main separately although
# both are native script 0, so from Talk onward every array position is one further
# along than the number a caller uses: kuro_8's cefi has its native Script 4 at array
# position 5, and its caller asks for function 4. Keying the graph by position finds
# the wrong script's callers or none at all, and either way invents an activation.
function Get-ScriptRequests {
    param($Field)

    $requests = @{}
    foreach ($entity in $Field.script.entities) {
        foreach ($script in $entity.scripts) {
            foreach ($op in $script.ops) {
                # Only the entity-addressed requests belong in an entity-keyed graph.
                # The party-member forms are PREQ, PRQSW and PRQEW - not PREQSW and
                # PREQEW, which are not opcodes and so matched nothing - and their first
                # operand is a party slot, not an entity. Slot 2 is whoever is third in
                # the party, which is not entity 2, so keying them here would invent a
                # caller for whichever entity happened to carry that number.
                if ([string]$op.op -notin @('REQ', 'REQSW', 'REQEW')) { continue }
                $key = '{0}/{1}' -f [int]$op.e, [int]$op.f
                if (-not $requests.ContainsKey($key)) {
                    $requests[$key] = [Collections.Generic.List[object]]::new()
                }
                $requests[$key].Add([pscustomobject]@{
                    EntityId = [int]$entity.entityId
                    ScriptIndex = [int]$script.index
                    ByteIndex = [int]$op.byteIndex
                })
            }
        }
    }
    return $requests
}

# The scripts a native entity/script number names. Init and Main share number 0, and
# both of them run on entering the field, so a request for 0 reaches whichever of them
# holds the byte the caller was found at.
function Get-ScriptsByNativeId {
    param($Field)

    $byId = @{}
    foreach ($entity in $Field.script.entities) {
        foreach ($script in $entity.scripts) {
            $key = '{0}/{1}' -f [int]$entity.entityId, [int]$script.index
            if (-not $byId.ContainsKey($key)) {
                $byId[$key] = [Collections.Generic.List[object]]::new()
            }
            $byId[$key].Add($script)
        }
    }
    return $byId
}

function Find-ScriptHoldingByte {
    param($Scripts, [int] $ByteIndex)

    foreach ($script in $Scripts) {
        foreach ($op in $script.ops) {
            if ([int]$op.byteIndex -eq $ByteIndex) { return $script }
        }
    }
    return $null
}

# The GameMoment the party has to be at for a write to happen just by being in the
# room, or nothing if being in the room is not enough. Every step of the way back to an
# Init or a Main has to be unskippable apart from one single equality test on the
# GameMoment, and every route back has to agree on which value that is. A
# player-triggered script - a Talk, a Contact, a line's Go - is not a way back:
# reaching one of those is the player doing something, which is a different kind of
# objective and is extracted on its own terms elsewhere.
function Resolve-EntryGameMoment {
    param($Field, $Lookup, $Requests, [int] $EntityId, $Script, [int] $ByteIndex, $Visited, [int] $Depth)

    if ($Depth -gt 6 -or $null -eq $Script) { return $null }

    $skippers = Get-WriteSkippers $Script $ByteIndex
    if ($skippers.Other -ne 0) { return $null }
    $local = @($skippers.Exact | Sort-Object -Unique)
    if ($local.Count -gt 1) { return $null }

    if ([string]$Script.scriptType -in @('Init', 'Main')) {
        return $local
    }

    $key = '{0}/{1}' -f $EntityId, [int]$Script.index
    if ($Visited.Contains($key)) { return $null }
    if (-not $Requests.ContainsKey($key)) { return $null }

    $moments = [Collections.Generic.List[int]]::new()
    foreach ($value in $local) { $moments.Add([int]$value) }
    foreach ($caller in $Requests[$key]) {
        $callerKey = '{0}/{1}' -f $caller.EntityId, $caller.ScriptIndex
        if (-not $Lookup.ContainsKey($callerKey)) { return $null }
        $callerScript = Find-ScriptHoldingByte $Lookup[$callerKey] $caller.ByteIndex
        if ($null -eq $callerScript) { return $null }

        $branch = [Collections.Generic.HashSet[string]]::new($Visited)
        [void]$branch.Add($key)
        $resolved = Resolve-EntryGameMoment $Field $Lookup $Requests $caller.EntityId $callerScript $caller.ByteIndex $branch ($Depth + 1)
        if ($null -eq $resolved) { return $null }
        foreach ($value in $resolved) { $moments.Add([int]$value) }
    }
    $combined = @($moments | Sort-Object -Unique)
    if ($combined.Count -gt 1) { return $null }
    return $combined
}

# A native trigger the extraction found and the player never has to reach, because the
# field runs it itself. Superseding it with a reviewed row is the wrong tool for that:
# there is no row to offer instead, and inventing one with conditions that can never be
# met would be a lie told in data. Each suppression names the caller that makes the
# trigger automatic, so it can be checked against the installed script.
$suppressedTriggers = [Collections.Generic.List[object]]::new()
function Add-SuppressedTrigger {
    param(
        [int] $FieldId,
        [string] $EntityName,
        [string] $ScriptType,
        [int] $MinimumGameMoment = -1,
        [int] $MaximumGameMoment = -1,
        [Parameter(Mandatory)] [string] $Reason
    )
    $suppressedTriggers.Add([pscustomobject]@{
        FieldId = $FieldId
        EntityName = $EntityName
        ScriptType = $ScriptType
        MinimumGameMoment = $MinimumGameMoment
        MaximumGameMoment = $MaximumGameMoment
        Reason = $Reason
    })
}

$curatedFields = [Collections.Generic.HashSet[int]]::new()
function Add-CuratedFields {
    param([int[]] $FieldId)
    foreach ($id in $FieldId) {
        [void]$curatedFields.Add($id)
    }
}

# The field scripts' own IFPRTY: is this character in the party right now? The engine
# keeps the three slots in Bank[3][9], [10] and [11], so this cannot be written as a mask
# over one byte, and it is a different question from whether the character's model is on
# screen.
function New-PartyMemberCondition {
    param(
        [int] $CharacterId,
        [switch] $Present
    )

    return [ordered]@{
        bank = 3
        address = 9
        mask = 0
        value = 0
        partyMemberId = $CharacterId
        requirePartyMember = [bool]$Present
    }
}

function New-Condition {
    param(
        [int] $Bank,
        [int] $Address,
        [int] $Mask,
        [int] $Value,
        [switch] $AnyBitSet,
        [int] $MinimumValue = -1,
        [int] $MaximumValue = -1,
        [int] $MinimumSetBits = -1,
        [int] $MaximumSetBits = -1
    )

    $condition = [ordered]@{ bank = $Bank; address = $Address; mask = $Mask; value = $Value }
    if ($AnyBitSet) {
        $condition.anyBitSet = $true
    }
    if ($MinimumValue -ge 0) { $condition.minimumValue = $MinimumValue }
    if ($MaximumValue -ge 0) { $condition.maximumValue = $MaximumValue }
    if ($MinimumSetBits -ge 0) { $condition.minimumSetBits = $MinimumSetBits }
    if ($MaximumSetBits -ge 0) { $condition.maximumSetBits = $MaximumSetBits }
    return $condition
}

# A plain PowerShell hashtable has no order, so the same trigger line written the same
# way twice serialises its keys in whatever order the table felt like, and every
# regeneration produces a file that differs from the last one in nothing but key order.
# Region files have been written both ways over time; normalising here means none of
# them has to be, and a regenerated catalog with no real change is byte for byte the
# file it replaced.
function ConvertTo-OrderedShape {
    param([object] $Value, [string[]] $Order)

    if ($null -eq $Value) { return $null }
    $shaped = [ordered]@{}
    foreach ($name in $Order) {
        $present = $false
        $item = $null
        if ($Value -is [System.Collections.IDictionary]) {
            if ($Value.Contains($name)) { $present = $true; $item = $Value[$name] }
        }
        elseif ($null -ne $Value.PSObject.Properties[$name]) {
            $present = $true
            $item = $Value.PSObject.Properties[$name].Value
        }
        if (-not $present) { continue }
        $shaped[$name] = if ($name -eq 'blockedLine') {
            ConvertTo-OrderedShape $item @('startX', 'startY', 'startZ', 'endX', 'endY', 'endZ')
        }
        else {
            $item
        }
    }
    return $shaped
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
        [int[]] $CompletionPlayerTriangles = @(),
        [int] $RequiredEnabledLineEntityId = -1,
        [switch] $KeepActiveOnArrival,
        [string] $ManualNavigationGuidance = '',
        [switch] $UsesPlayerCollisionRadius,
        [switch] $UsesContactRange,
        [switch] $UsesHiddenTalkTarget
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
    if ($UsesContactRange) {
        $definition.usesContactRange = $true
    }
    if ($ManualNavigationGuidance) {
        $definition.manualNavigationGuidance = $ManualNavigationGuidance
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
        $definition.triggerLine = ConvertTo-OrderedShape $TriggerLine @('startX', 'startY', 'startZ', 'endX', 'endY', 'endZ')
    }
    if ($null -ne $RouteDetour) {
        $definition.routeDetour = ConvertTo-OrderedShape $RouteDetour @('blockedLine', 'x', 'y', 'z', 'clearance')
    }
    if ($RouteDetours.Count -gt 0) {
        $definition.routeDetours = @($RouteDetours | ForEach-Object {
            ConvertTo-OrderedShape $_ @('blockedLine', 'x', 'y', 'z', 'clearance')
        })
    }
    if ($RequiredPlayerTriangles.Count -gt 0) {
        $definition.requiredPlayerTriangles = @($RequiredPlayerTriangles)
    }
    if ($ExcludedPlayerTriangles.Count -gt 0) {
        $definition.excludedPlayerTriangles = @($ExcludedPlayerTriangles)
    }
    if ($CompletionPlayerTriangles.Count -gt 0) {
        $definition.completionPlayerTriangles = @($CompletionPlayerTriangles)
    }
    if ($RequiredEnabledLineEntityId -ge 0) {
        $definition.requiredEnabledLineEntityId = $RequiredEnabledLineEntityId
    }
    if ($UsesPlayerCollisionRadius) {
        if ($Kind -ne 'Location' -or -not $TriggerLine) {
            throw 'Native player collision radius requires a Location with a trigger line.'
        }
        $definition.usesPlayerCollisionRadius = $true
    }
    if ($UsesHiddenTalkTarget) {
        # Only a Talk can be made with an entity that is not drawn, and only where the
        # native script is built on it: see story-regions/Wutai.ps1.
        if ($Kind -ne 'Model' -or $ScriptType -ne 'Talk') {
            throw 'A hidden Talk target must be a Model row with a Talk script.'
        }
        $definition.usesHiddenTalkTarget = $true
    }
    $definitions.Add($definition)
}

# An entity the player can walk up to and act on. Playable Character is a model type
# like any other as far as the field engine is concerned - a character who has not
# joined the party yet stands in the world, visible and talkable, and several required
# conversations belong to one. Cid in rcktin2 is the clearest case: his own Talk is
# what writes 538, and excluding his entity type dropped that step entirely.
#
# This only ever matters for a script the reverse-call search already tied to a
# GameMoment write, so it cannot turn ordinary party chatter into an objective. The
# resolved row still reads live visibility and position at runtime, so a character who
# is not standing there is never offered.
function Test-ActivationScript {
    param([object] $Entity, [object] $Script)
    if ($Entity.entityType -in @('Model', 'Playable Character') -and
        $Script.scriptType -in @('Talk', 'Contact')) {
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
                $reverseCalls[$key].Add([pscustomobject]@{
                    Entity = $callerEntity
                    Script = $callerScript
                    ByteIndex = [int]$operation.byteIndex
                })
            }
        }
    }

    $queue = [Collections.Generic.Queue[object]]::new()
    $queue.Enqueue([pscustomobject]@{
        Entity = $SourceEntity
        Script = $SourceScript
        Depth = 0
        Moments = @()
    })
    $seen = @{}
    $matches = [Collections.Generic.List[object]]::new()
    $matchDepth = [int]::MaxValue
    while ($queue.Count -gt 0) {
        $node = $queue.Dequeue()
        if ($node.Depth -gt $matchDepth) {
            continue
        }
        $nodeKey = "$([int]$node.Entity.entityId):$([int]$node.Script.index):$([string]$node.Script.scriptType):$(@($node.Moments | Sort-Object -Unique) -join ',')"
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
            # Whatever the caller tested before asking for this script is a condition on
            # the write as surely as if it had been written into the write's own script.
            $callerGuards = Get-ExactGameMomentGuards $caller.Script $caller.ByteIndex
            $queue.Enqueue([pscustomobject]@{
                Entity = $caller.Entity
                Script = $caller.Script
                Depth = $node.Depth + 1
                Moments = @($node.Moments + @($callerGuards.Exact))
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
    # The midpoint is where to walk, but the line itself is the contract: the native
    # Go handler wants the player within the model's own collision radius of it
    # (FUN_00637ABB), and a thin exit can be missed entirely by a fixed threshold that
    # stops outside the activation region.
    return [pscustomobject]@{
        X = [int](($line.x1 + $line.x2) / 2)
        Y = [int](($line.y1 + $line.y2) / 2)
        Z = [int](($line.z1 + $line.z2) / 2)
        TriggerLine = [ordered]@{
            startX = [int]$line.x1; startY = [int]$line.y1; startZ = [int]$line.z1
            endX = [int]$line.x2; endY = [int]$line.y2; endZ = [int]$line.z2
        }
    }
}

# Rooms with nothing in them but a black background, which are two different things
# wearing the same name.
#
# Some are dialogue-test rooms. blackbg4 alone writes the GameMoment seventy-seven
# times, and nothing outside the family can enter it at all, so every row extracted from
# one is a row the player can never reach - and worse, a row that makes a coverage
# report say a step is answered when nothing answers it.
#
# The rest are films. blackbgb is what plays when the rocket launch is aborted, and
# rcktin5, blin1, losin2 and slfrst_1 all enter it. Calling that a debug room because of
# the first five letters of its name is not a classification, it is a guess, and it
# would have hidden a real transit in the Rocket Town chapter's own flashback.
#
# The two lists below are measured rather than assumed - which fields outside the family
# have a door or a map jump into each - and Measure-CinematicRooms.ps1 in the build
# directory reprints the evidence against the pinned data. Both lists are excluded from
# extraction, but for different reasons: a dialogue-test room is not part of the game,
# and a film is part of it but has no navigation in it to offer.
$dialogueTestRooms = @(
    'blackbg1', 'blackbg2', 'blackbg3', 'blackbg4', 'blackbg5', 'blackbg6', 'blackbg7',
    'blackbg8', 'blackbge', 'blackbgh', 'blackbgi', 'blackbgk', 'startmap',
    'zz1', 'zz3', 'zz5', 'zz6', 'zz7', 'zz8'
)
$frozenFilmRooms = @('blackbg9', 'blackbgb', 'blackbgc', 'blackbgd', 'blackbgj', 'zz4')

function Test-PlayableField {
    param([string] $Name)

    # The world-map weapon seller is playable despite the legacy zz/debug grouping.
    if ($Name -eq 'zz2') { return $true }
    if ($Name -in $dialogueTestRooms -or $Name -in $frozenFilmRooms) { return $false }
    if ($chapterByField.ContainsKey($Name) -and $chapterByField[$Name] -in @('Debug', 'Missing')) { return $false }
    return $true
}

foreach ($file in Get-ChildItem -LiteralPath $fieldJsonRoot -Filter '*.json') {
    $fieldName = $file.BaseName
    if (-not $fieldIds.ContainsKey($fieldName)) {
        continue
    }
    if (-not (Test-PlayableField $fieldName)) {
        continue
    }

    # Some of the extracted field files are cloud placeholders that are offline at the
    # moment they are asked for. A verified local supplement is used when one exists;
    # otherwise the field is named as an unresolved input rather than quietly treated
    # as covered. A file that is present and readable but does not parse is a real
    # problem and still stops the run - a catch-all would hide corrupted input as if
    # it were a placeholder.
    # Most of the extracted field files are cloud placeholders. Carrying the Offline
    # attribute does not mean a file cannot be read - the provider hydrates it on
    # demand - so the read is always attempted first and only a genuine failure is
    # treated as missing input.
    #
    # A file whose bytes were read but do not parse is a real problem and still stops
    # the run: a catch-all here would hide corrupted or edited input as if it were a
    # placeholder.
    $raw = $null
    try {
        $raw = Get-Content -Raw -LiteralPath $file.FullName -ErrorAction Stop
    }
    catch {
        $isOffline = $false
        try {
            $isOffline = ((Get-Item -LiteralPath $file.FullName -ErrorAction Stop).Attributes -band
                [IO.FileAttributes]::Offline) -ne 0
        }
        catch {
            $isOffline = $false
        }
        if (-not $isOffline) {
            throw
        }

        $supplementPath = if ($SupplementRoot) { Join-Path $SupplementRoot "$fieldName.json" } else { '' }
        if ($supplementPath -and (Test-Path -LiteralPath $supplementPath)) {
            $raw = Get-Content -Raw -LiteralPath $supplementPath -ErrorAction Stop
            $suppliedFields.Add($fieldName)
        }
        else {
            $unreadableFields.Add($fieldName)
            continue
        }
    }

    $field = $raw | ConvertFrom-Json

    # Collected while the field is already parsed, so the continuity pass below costs
    # nothing extra. The gateway table is the engine's own set of doors, with the exit
    # line a navigation target needs; the GameMoment writes say which field owns each
    # step of the story.
    if ($field.triggers -and $field.triggers.gateways) {
        $gatewayIndex = 0
        foreach ($gateway in $field.triggers.gateways) {
            $destination = [int]$gateway.fieldId
            $ax = [int]$gateway.exitLineVertex1.x
            $ay = [int]$gateway.exitLineVertex1.y
            $bx = [int]$gateway.exitLineVertex2.x
            $by = [int]$gateway.exitLineVertex2.y
            if ($destination -gt 0 -and -not ($ax -eq 0 -and $ay -eq 0 -and $bx -eq 0 -and $by -eq 0)) {
                if (-not $gatewaysByField.ContainsKey($fieldName)) {
                    $gatewaysByField[$fieldName] = [Collections.Generic.List[object]]::new()
                }
                $gatewaysByField[$fieldName].Add([pscustomobject]@{
                    EntityName = "gateway$gatewayIndex"
                    ScriptType = 'Gateway'
                    EntityId = -1
                    ToField = [string]$gateway.fieldName
                    ToFieldId = $destination
                    X1 = $ax; Y1 = $ay; Z1 = [int]$gateway.exitLineVertex1.z
                    X2 = $bx; Y2 = $by; Z2 = [int]$gateway.exitLineVertex2.z
                })
            }
            $gatewayIndex++
        }
    }

    # Doors the gateway table does not hold. Plenty of interiors - shops, inns, the
    # prison houses - are left through a LINE entity whose own walk-on script performs
    # the MAPJUMP, and a graph built from gateways alone cannot get back out of them.
    #
    # Only a Line entity's player-triggered scripts count. A MAPJUMP inside a Director
    # or a model's scene is not a door: it is the end of a cutscene, a battle
    # transition or a branch, and sending a player to walk into it would be wrong.
    foreach ($lineEntity in $field.script.entities) {
        if ($lineEntity.entityType -ne 'Line') {
            continue
        }

        $lineInit = $lineEntity.scripts | Where-Object { $_.scriptType -eq 'Init' } | Select-Object -First 1
        $lineOp = $null
        if ($lineInit) {
            $lineOp = $lineInit.ops | Where-Object { $_.op -eq 'LINE' } | Select-Object -First 1
        }
        if ($null -eq $lineOp) {
            continue
        }

        foreach ($lineScript in $lineEntity.scripts) {
            if ([string]$lineScript.scriptType -notin @('Go', 'Go 1x', '[OK]', 'Move')) {
                continue
            }

            foreach ($op in $lineScript.ops) {
                if ($op.op -ne 'MAPJUMP') {
                    continue
                }
                # MAPJUMP carries the destination field in f. Its i is the arrival
                # walkmesh triangle, which is a different number entirely and names a
                # world-map field when read as an identity.
                $destinationId = [int]$op.f
                if ($destinationId -le 0 -or -not $idNames.ContainsKey($destinationId)) {
                    continue
                }
                if (-not $gatewaysByField.ContainsKey($fieldName)) {
                    $gatewaysByField[$fieldName] = [Collections.Generic.List[object]]::new()
                }
                $gatewaysByField[$fieldName].Add([pscustomobject]@{
                    EntityName = [string]$lineEntity.entityName
                    ScriptType = [string]$lineScript.scriptType
                    EntityId = [int]$lineEntity.entityId
                    ToField = $idNames[$destinationId]
                    ToFieldId = $destinationId
                    X1 = [int]$lineOp.x1; Y1 = [int]$lineOp.y1; Z1 = [int]$lineOp.z1
                    X2 = [int]$lineOp.x2; Y2 = [int]$lineOp.y2; Z2 = [int]$lineOp.z2
                })
            }
        }
    }


    # A field whose Director turns the gateway table off entirely - jail1, jail3 and
    # jail4 all do it in their Init - has no usable static doors at all, whatever the
    # table says. Doors out of it have to come from its own LINE scripts instead.
    foreach ($mpjpoEntity in $field.script.entities) {
        foreach ($mpjpoScript in $mpjpoEntity.scripts) {
            foreach ($op in $mpjpoScript.ops) {
                if ($op.op -eq 'MPJPO' -and [int]$op.s -ne 0) {
                    $gatewaysDisabledFields[$fieldName] = $true
                }
            }
        }
    }

    # Writes that happen simply because the party walked into the room. An entity's own
    # Init or Main runs on entry; when the only thing standing between entry and the
    # write is a single exact test on the GameMoment, then at that one value the door
    # into this field IS the next step, and the value is the script's own test rather
    # than a guess from write order. Anything with a further condition is not recorded:
    # the room would be entered and nothing would happen.
    $fieldRequests = Get-ScriptRequests $field
    $fieldScriptsByNativeId = Get-ScriptsByNativeId $field
    foreach ($entryEntity in $field.script.entities) {
        foreach ($entryScript in $entryEntity.scripts) {
            foreach ($op in $entryScript.ops) {
                if ($op.op -ne 'SETWORD' -or [int]$op.bd -ne 2 -or [int]$op.bs -ne 0 -or [int]$op.a -ne 0) {
                    continue
                }
                $required = Resolve-EntryGameMoment $field $fieldScriptsByNativeId $fieldRequests ([int]$entryEntity.entityId) $entryScript ([int]$op.byteIndex) ([Collections.Generic.HashSet[string]]::new()) 0
                if ($null -eq $required -or $required.Count -ne 1) {
                    continue
                }
                if ([int]$op.v -eq [int]$required[0]) {
                    continue
                }
                if (-not $entryWritesByField.ContainsKey($fieldName)) {
                    $entryWritesByField[$fieldName] = [Collections.Generic.List[object]]::new()
                }
                $entryWritesByField[$fieldName].Add([pscustomobject]@{
                    RequiredGameMoment = [int]$required[0]
                    TargetGameMoment = [int]$op.v
                    EntityName = [string]$entryEntity.entityName
                    ScriptType = [string]$entryScript.scriptType
                })
            }
        }
    }

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
                if (-not $momentWritesByField.ContainsKey($fieldName)) {
                    $momentWritesByField[$fieldName] = [Collections.Generic.List[int]]::new()
                }
                if (-not $momentWritesByField[$fieldName].Contains($targetMoment)) {
                    $momentWritesByField[$fieldName].Add($targetMoment)
                }
                if (($fieldName -eq 'nmkin_1' -and $targetMoment -eq 11) -or
                    ($fieldName -eq 'nmkin_3' -and $targetMoment -eq 14) -or
                    ($fieldName -eq 'nmkin_5' -and $targetMoment -in @(15, 27)) -or
                    ($fieldName -eq 'chrin_3b' -and $targetMoment -eq 155)) {
                    continue
                }

                # The script that performs the write says when it will perform it. If
                # exactly one equality test on the GameMoment stands between the top of
                # that script and the write, then outside that one value the action the
                # row points at achieves nothing at all, and an unbounded row would go
                # on offering it for the rest of the game. Other conditions in the way
                # are left alone: they are further requirements, not a different moment,
                # and the value is still the only one the write can happen at.
                $writeGuards = Get-ExactGameMomentGuards $sourceScript ([int]$milestone.byteIndex)
                $writeMoments = [Collections.Generic.List[int]]::new()
                foreach ($value in $writeGuards.Exact) { $writeMoments.Add([int]$value) }

                $writeMoments = @($writeMoments | Sort-Object -Unique)

                $activationNodes = @(Get-ActivationNodes $field $sourceEntity $sourceScript)
                if ($activationNodes.Count -eq 0) {
                    $unresolved.Add("${fieldName}:$($sourceEntity.entityName):$($sourceScript.scriptType):$targetMoment")
                    continue
                }

                foreach ($activation in $activationNodes) {
                    $entity = $activation.Entity
                    $script = $activation.Script

                    # The write's own script and every request between it and this
                    # activation, taken together. One value and the row is bounded to
                    # it; none, or more than one that disagree, and it stays open,
                    # because a wrong band hides a step and an open one only repeats.
                    $activationMoments = @(($writeMoments + @($activation.Moments)) | Sort-Object -Unique)
                    $milestoneMinimum = -1
                    $milestoneMaximum = -1
                    if ($activationMoments.Count -eq 1 -and [int]$activationMoments[0] -ne $targetMoment) {
                        $milestoneMinimum = [int]$activationMoments[0]
                        $milestoneMaximum = [int]$activationMoments[0]
                    }
                    $chapterName = if ($chapterByField.ContainsKey($fieldName)) {
                        $chapterByField[$fieldName]
                    } else {
                        'the story'
                    }
                    if ($entity.entityType -in @('Model', 'Playable Character')) {
                        $speaker = Get-DialogSpeaker @($script, $sourceScript) ([string]$entity.entityName)
                        $label = if ($script.scriptType -eq 'Contact') {
                            if ($speaker) { "Approach $speaker to continue" } else { "Approach the story character" }
                        } else {
                            if ($speaker) { "Talk to $speaker to continue" } else { "Talk to the story character" }
                        }
                        Add-Definition `
                            -FieldId $fieldIds[$fieldName] -FieldName $fieldName -Kind 'Model' `
                            -Label $label -EntityId $entity.entityId -TargetGameMoment $targetMoment `
                            -MinimumGameMoment $milestoneMinimum -MaximumGameMoment $milestoneMaximum `
                            -EntityName $entity.entityName -ScriptType $script.scriptType
                        $navigableMilestoneCount++
                        continue
                    }

                    $location = Get-LineLocation $entity
                    if ($null -eq $location) {
                        continue
                    }
                    # Not the chapter's name. The metadata titles are written for a
                    # reader who has finished the game - "Prison, Bloodbath",
                    # "Sephiroth goes ballistic" - so speaking one tells the player
                    # nothing about what to do now and gives away a scene they have
                    # not reached. The row already carries the place; the label only
                    # has to say that walking there is the way on.
                    #
                    # The line comes with it, along with the two conditions the engine
                    # itself applies: arrival is measured against the player's own
                    # collision radius, and a LINE the field has switched off with
                    # LINON is not a way anywhere.
                    Add-Definition `
                        -FieldId $fieldIds[$fieldName] -FieldName $fieldName -Kind 'Location' `
                        -Label 'Continue on from here' -X $location.X -Y $location.Y -Z $location.Z `
                        -TargetGameMoment $targetMoment -EntityName $entity.entityName -ScriptType $script.scriptType `
                        -MinimumGameMoment $milestoneMinimum -MaximumGameMoment $milestoneMaximum `
                        -TriggerLine $location.TriggerLine -UsesPlayerCollisionRadius `
                        -RequiredEnabledLineEntityId ([int]$entity.entityId)
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
Add-Definition -FieldId 120 -FieldName 'nmkin_1' -Kind 'Location' -Label 'Approach Barret and Avalanche' -X -704 -Y 2166 -Z -274 -TargetGameMoment 11 -RequiredCondition (New-Condition 1 225 0x18 0x18) -EntityName 'evb' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = -896; startY = 2166; startZ = -274; endX = -512; endY = 2166; endZ = -274 })
Add-Definition -FieldId 120 -FieldName 'nmkin_1' -Kind 'Location' -Label 'Press the walkway door button' -X -1699 -Y 4400 -Z -273 -TargetGameMoment 12 -MinimumGameMoment 11 -MaximumGameMoment 11 -RequiredCondition (New-Condition 1 225 0x18 0x18) -CompletedCondition (New-Condition 5 2 0x01 0x01) -EntityName 'drE' -ScriptType 'Go' -EntityId 13 -RequiredEnabledLineEntityId 13 -UsesPlayerCollisionRadius -TriggerLine ([ordered]@{ startX = -1601; startY = 4400; startZ = -273; endX = -1797; endY = 4400; endZ = -273 })
Add-Definition -FieldId 120 -FieldName 'nmkin_1' -Kind 'Location' -Label 'Go through the opened walkway door' -X -1490 -Y 4517 -Z -282 -TargetGameMoment 12 -MinimumGameMoment 11 -MaximumGameMoment 11 -RequiredCondition (New-Condition 5 2 0x01 0x01) -EntityName 'drE' -ScriptType 'Go'
Add-Definition -FieldId 121 -FieldName 'elevtr1' -Kind 'Location' -Label 'Stand on the elevator switch plate and press Confirm' -X 86 -Y 64 -Z 5 -TargetGameMoment 12 -MinimumGameMoment 11 -MaximumGameMoment 11 -EntityId 5 -CompletionPlayerTriangles @(8) -EntityName 'ele' -ScriptType 'Main'
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
Add-Definition -FieldId 121 -FieldName 'elevtr1' -Kind 'Location' -Label "Leave the elevator toward Reactor 1's security rooms" -X -174 -Y -6 -Z 5 -MinimumGameMoment 27 -MaximumGameMoment 32 -Priority 0 -RequiredCondition (New-Condition 1 225 0x01 0x00) -EntityName 'jp0' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = -174; startY = -68; startZ = 5; endX = -174; endY = 56; endZ = 5 })
Add-Definition -FieldId 120 -FieldName 'nmkin_1' -Kind 'Model' -Label 'Talk to Jessie to reopen the inner security door' -EntityId 10 -MinimumGameMoment 27 -Priority 0 -RequiredCondition (New-Condition 1 225 0x20 0x20) -CompletedCondition (New-Condition 1 225 0x10 0x10) -EntityName 'av_j' -ScriptType 'Talk'
Add-Definition -FieldId 120 -FieldName 'nmkin_1' -Kind 'Model' -Label 'Talk to Biggs to reopen the outer security door' -EntityId 9 -MinimumGameMoment 27 -Priority 1 -RequiredCondition (New-Condition 1 225 0x30 0x30) -CompletedCondition (New-Condition 1 225 0x08 0x08) -EntityName 'av_b' -ScriptType 'Talk'

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
Add-Definition -FieldId 137 -FieldName 'md8brdg' -Kind 'Location' -Label 'Run past the soldiers and reach the bridge' -EntityId 4 -X -426 -Y 835 -Z 518 -TargetGameMoment 48 -MinimumGameMoment 39 -MaximumGameMoment 47 -Priority 0 -EntityName 'ev' -ScriptType 'Move' -RequiredEnabledLineEntityId 4 -TriggerLine ([ordered]@{ startX = -614; startY = 835; startZ = 518; endX = -238; endY = 835; endZ = 518 })
Add-Definition -FieldId 139 -FieldName 'tin_1' -Kind 'Model' -Label 'Talk to Jessie and view the railway map' -EntityId 34 -TargetGameMoment 63 -MinimumGameMoment 51 -MaximumGameMoment 62 -Priority 0 -CompletedCondition (New-Condition 3 223 0x04 0x04) -EntityName 'avaw' -ScriptType 'Talk'
Add-Definition -FieldId 139 -FieldName 'tin_1' -Kind 'Location' -Label 'Go to the train window and join Barret' -EntityId 27 -X -2 -Y -215 -Z -53 -TargetGameMoment 63 -MinimumGameMoment 51 -MaximumGameMoment 62 -Priority 0 -RequiredCondition (New-Condition 3 223 0x04 0x04) -EntityName 'border2' -ScriptType 'Move' -RequiredEnabledLineEntityId 27 -TriggerLine ([ordered]@{ startX = -55; startY = -210; startZ = -53; endX = 52; endY = -220; endZ = -53 })
Add-Definition -FieldId 138 -FieldName 'cargoin' -Kind 'Location' -Label 'Return to the passenger car' -EntityId 10 -X 19 -Y -104 -Z 0 -TargetGameMoment 63 -MinimumGameMoment 51 -MaximumGameMoment 62 -Priority 0 -ScriptType 'Move' -RequiredEnabledLineEntityId 10 -TriggerLine ([ordered]@{ startX = -15; startY = -104; startZ = 0; endX = 53; endY = -104; endZ = 0 })
Add-Definition -FieldId 139 -FieldName 'tin_1' -Kind 'Location' -Label 'Go to Tifa at the railway map monitor' -EntityId 27 -X -2 -Y -215 -Z -53 -TargetGameMoment 117 -MinimumGameMoment 108 -MaximumGameMoment 116 -Priority 0 -CompletedCondition (New-Condition 5 33 0x01 0x01) -RequiredEnabledLineEntityId 27 -EntityName 'border2' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = -55; startY = -210; startZ = -53; endX = 52; endY = -220; endZ = -53 })
Add-Definition -FieldId 139 -FieldName 'tin_1' -Kind 'Location' -Label 'Escape Car 1 through the forward door' -EntityId 26 -X 1 -Y -381 -Z -53 -TargetGameMoment 117 -MinimumGameMoment 108 -MaximumGameMoment 116 -Priority 0 -RequiredCondition (New-Condition 5 33 0x01 0x01) -RequiredEnabledLineEntityId 26 -EntityName 'border1' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = -53; startY = -361; startZ = -53; endX = 54; endY = -400; endZ = -53 })
Add-Definition -FieldId 140 -FieldName 'tin_2' -Kind 'Location' -Label 'Escape Car 2 through the forward door' -EntityId 18 -X -2 -Y -412 -Z -56 -TargetGameMoment 117 -MinimumGameMoment 108 -MaximumGameMoment 116 -Priority 0 -RequiredEnabledLineEntityId 18 -EntityName 'border0' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = -60; startY = -418; startZ = -56; endX = 56; endY = -406; endZ = -56 })
Add-Definition -FieldId 142 -FieldName 'tin_4' -Kind 'Location' -Label 'Escape through the next train car' -EntityId 15 -X 6 -Y -444 -Z -56 -TargetGameMoment 117 -MinimumGameMoment 108 -MaximumGameMoment 116 -Priority 0 -RequiredEnabledLineEntityId 15 -EntityName 'border1' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 38; startY = -441; startZ = -56; endX = -26; endY = -447; endZ = -56 })
Add-Definition -FieldId 141 -FieldName 'tin_3' -Kind 'Model' -Label 'Talk to Tifa and jump from the train' -EntityId 15 -TargetGameMoment 117 -MinimumGameMoment 108 -MaximumGameMoment 116 -Priority 0 -EntityName 'tifa' -ScriptType 'Talk'
Add-Definition -FieldId 161 -FieldName 'tunnel_1' -Kind 'Location' -Label 'Continue north through the winding tunnel' -EntityId 2 -X 746 -Y 2237 -Z 5 -TargetGameMoment 117 -MinimumGameMoment 108 -MaximumGameMoment 116 -Priority 0 -EntityName 'line2' -ScriptType 'Move' -RequiredEnabledLineEntityId 2 -TriggerLine ([ordered]@{ startX = 805; startY = 2862; startZ = 9; endX = 687; endY = 1611; endZ = 0 }) -KeepActiveOnArrival
Add-Definition -FieldId 162 -FieldName 'tunnel_2' -Kind 'Location' -Label 'Enter the maintenance duct to continue to Reactor 5' -EntityId 5 -X -46 -Y 556 -Z 0 -TargetGameMoment 117 -MinimumGameMoment 108 -MaximumGameMoment 116 -Priority 0 -EntityName 'line2' -ScriptType 'Move' -RequiredEnabledLineEntityId 5 -TriggerLine ([ordered]@{ startX = -38; startY = 513; startZ = 0; endX = -54; endY = 600; endZ = 0 })
# Choosing Go down in tunnel_2 advances GameMoment to 117 before the Sector 4
# plate sequence begins. It remains 117 throughout these fields; Reactor 5 is
# the next native progression block and advances the moment to 123.
# These objectives are the exact ladder activation LINEs in sbwy4_1 and
# sbwy4_3 through sbwy4_6. sbwy4_2 is the short automatic ladder connector and
# therefore uses its native forward endpoint rather than fabricating a LINE.
Add-Definition -FieldId 164 -FieldName 'sbwy4_1' -Kind 'Location' -Label 'Follow the large duct to the far ladder' -EntityId 2 -X 0 -Y 63 -Z 0 -TargetGameMoment 123 -MinimumGameMoment 117 -MaximumGameMoment 122 -Priority 0 -EntityName 'line' -ScriptType '[OK]' -RequiredEnabledLineEntityId 2 -TriggerLine ([ordered]@{ startX = -69; startY = 65; startZ = 0; endX = 68; endY = 60; endZ = 0 })
Add-Definition -FieldId 165 -FieldName 'sbwy4_2' -Kind 'Location' -Label 'Continue across the upper ladder' -X 0 -Y 277 -Z 0 -TargetGameMoment 123 -MinimumGameMoment 117 -MaximumGameMoment 122 -Priority 0 -EntityName 'cloud' -ScriptType 'Main'
Add-Definition -FieldId 166 -FieldName 'sbwy4_3' -Kind 'Location' -Label "Cross Jessie's platform and use the next ladder" -EntityId 2 -X 6 -Y 52 -Z 0 -TargetGameMoment 123 -MinimumGameMoment 117 -MaximumGameMoment 122 -Priority 0 -EntityName 'line2' -ScriptType 'Go' -RequiredEnabledLineEntityId 2 -TriggerLine ([ordered]@{ startX = -60; startY = 52; startZ = 0; endX = 72; endY = 52; endZ = 0 })
Add-Definition -FieldId 167 -FieldName 'sbwy4_4' -Kind 'Location' -Label 'Go right across the small duct and use the ladder' -EntityId 5 -X 178 -Y 1 -Z 0 -TargetGameMoment 123 -MinimumGameMoment 117 -MaximumGameMoment 122 -Priority 0 -EntityName 'line1' -ScriptType '[OK]' -RequiredEnabledLineEntityId 5 -TriggerLine ([ordered]@{ startX = 185; startY = -35; startZ = 0; endX = 170; endY = 37; endZ = 0 })
Add-Definition -FieldId 168 -FieldName 'sbwy4_5' -Kind 'Location' -Label 'Use the ladder near Wedge' -EntityId 4 -X -2281 -Y 778 -Z 0 -TargetGameMoment 123 -MinimumGameMoment 117 -MaximumGameMoment 122 -Priority 0 -EntityName 'line3' -ScriptType 'Go' -RequiredEnabledLineEntityId 4 -TriggerLine ([ordered]@{ startX = -2323; startY = 778; startZ = 0; endX = -2239; endY = 778; endZ = 0 })
Add-Definition -FieldId 169 -FieldName 'sbwy4_6' -Kind 'Location' -Label 'Use the ladder near Biggs to reach Reactor 5' -EntityId 3 -X -357 -Y -144 -Z 14 -TargetGameMoment 123 -MinimumGameMoment 117 -MaximumGameMoment 122 -Priority 0 -EntityName 'line2' -ScriptType 'Go' -RequiredEnabledLineEntityId 3 -TriggerLine ([ordered]@{ startX = -357; startY = -176; startZ = 14; endX = -357; endY = -112; endZ = 14 })
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
Add-Definition -FieldId 132 -FieldName 'smkin_5' -Kind 'Location' -Label "Escape Reactor 5's core" -EntityId 6 -X -87 -Y -1049 -Z -184 -TargetGameMoment 128 -MinimumGameMoment 127 -MaximumGameMoment 127 -Priority 0 -EntityName 'ln0' -ScriptType 'Move' -RequiredEnabledLineEntityId 6 -TriggerLine ([ordered]@{ startX = -27; startY = -1049; startZ = -184; endX = -147; endY = -1049; endZ = -184 })
Add-Definition -FieldId 131 -FieldName 'smkin_4' -Kind 'Location' -Label 'Climb back toward the Reactor 5 elevator' -X 250 -Y 1255 -Z 862 -TargetGameMoment 128 -MinimumGameMoment 127 -MaximumGameMoment 127 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 208; startY = 1255; startZ = 854; endX = 291; endY = 1255; endZ = 869 })
Add-Definition -FieldId 130 -FieldName 'smkin_3' -Kind 'Location' -Label 'Cross the upper piping back toward the elevator' -X -328 -Y 1921 -Z 2094 -TargetGameMoment 128 -MinimumGameMoment 127 -MaximumGameMoment 127 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -341; startY = 1955; startZ = 2082; endX = -316; endY = 1887; endZ = 2107 })
Add-Definition -FieldId 129 -FieldName 'smkin_2' -Kind 'Location' -Label 'Return to the Reactor 5 elevator' -X -730 -Y 310 -Z 1571 -TargetGameMoment 128 -MinimumGameMoment 127 -MaximumGameMoment 127 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -694; startY = 273; startZ = 1571; endX = -767; endY = 347; endZ = 1571 })
Add-Definition -FieldId 121 -FieldName 'elevtr1' -Kind 'Location' -Label 'Stand on the elevator switch plate and press Confirm' -EntityId 5 -X 86 -Y 64 -Z 5 -TargetGameMoment 128 -MinimumGameMoment 127 -MaximumGameMoment 127 -Priority 0 -CompletionPlayerTriangles @(8) -RequiredCondition (New-Condition 1 225 0x01 0x01) -CompletedCondition (New-Condition 1 225 0x01 0x00) -EntityName 'ele' -ScriptType 'Main'
Add-Definition -FieldId 121 -FieldName 'elevtr1' -Kind 'Location' -Label 'Leave the elevator for the security room' -EntityId 10 -X -185 -Y -6 -Z 5 -TargetGameMoment 128 -MinimumGameMoment 127 -MaximumGameMoment 127 -Priority 0 -RequiredCondition (New-Condition 1 225 0x01 0x00) -EntityName 'jp0' -ScriptType 'Move' -RequiredEnabledLineEntityId 10 -TriggerLine ([ordered]@{ startX = -185; startY = -68; startZ = 5; endX = -185; endY = 56; endZ = 5 })
Add-Definition -FieldId 128 -FieldName 'smkin_1' -Kind 'Location' -Label 'Reach the simultaneous security controls' -EntityId 6 -X -532 -Y 3353 -Z -273 -TargetGameMoment 128 -MinimumGameMoment 127 -MaximumGameMoment 127 -Priority 0 -EntityName 'ln1' -ScriptType 'Move' -RequiredEnabledLineEntityId 6 -TriggerLine ([ordered]@{ startX = -532; startY = 3305; startZ = -273; endX = -532; endY = 3401; endZ = -273 })
Add-Definition -FieldId 128 -FieldName 'smkin_1' -Kind 'Location' -Label 'Continue to the bridge approach' -X -694 -Y 1124 -Z -433 -TargetGameMoment 140 -MinimumGameMoment 128 -MaximumGameMoment 139 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -516; startY = 1106; startZ = -433; endX = -873; endY = 1141; endZ = -433 })
Add-Definition -FieldId 126 -FieldName 'southmk1' -Kind 'Location' -Label 'Open the bridge door and continue' -EntityId 1 -X -3 -Y -2760 -Z 491 -TargetGameMoment 140 -MinimumGameMoment 128 -MaximumGameMoment 139 -Priority 0 -EntityName 'line' -ScriptType '[OK]' -RequiredEnabledLineEntityId 1 -TriggerLine ([ordered]@{ startX = 48; startY = -2771; startZ = 491; endX = -54; endY = -2750; endZ = 491 }) -KeepActiveOnArrival

# chrin_1b (183) is an automatic transition with user control locked. The
# playable escape and barrel rescue are in chrin_2 (184). Entering walkmesh
# triangle 81 advances GameMoment to 152; its center is the stable approach
# target immediately before the scripted Reno sequence.
Add-Definition -FieldId 184 -FieldName 'chrin_2' -Kind 'Location' -Label 'Continue along the upper church rafters' -X -198 -Y 1871 -Z 502 -TargetGameMoment 152 -Priority 0 -ScriptType 'Native triangle 81'

# The native script exposes barrel interaction only while temporary Bank 5,
# byte 10 equals one. Byte 12 identifies the visible guard/Aerith rescue stage.
# Each stage points to the barrel Aerith visually indicates on screen.
Add-Definition -FieldId 184 -FieldName 'chrin_2' -Kind 'Model' -Label 'Push the barrel on the left' -EntityId 10 -MinimumGameMoment 152 -MaximumGameMoment 152 -Priority 0 -RequiredCondition (New-Condition 5 10 0xFF 1) -EntityName 'bar3' -ScriptType 'Talk'
Add-Definition -FieldId 184 -FieldName 'chrin_2' -Kind 'Model' -Label 'Push the barrel in the middle' -EntityId 8 -MinimumGameMoment 152 -MaximumGameMoment 152 -Priority 0 -RequiredCondition (New-Condition 5 10 0xFF 1) -EntityName 'bar1' -ScriptType 'Talk'
Add-Definition -FieldId 184 -FieldName 'chrin_2' -Kind 'Model' -Label 'Push the barrel on the right' -EntityId 9 -MinimumGameMoment 152 -MaximumGameMoment 152 -Priority 0 -RequiredCondition (New-Condition 5 10 0xFF 1) -EntityName 'bar2' -ScriptType 'Talk'
Add-Definition -FieldId 184 -FieldName 'chrin_2' -Kind 'Model' -Label 'Push the far barrel' -EntityId 11 -MinimumGameMoment 152 -MaximumGameMoment 152 -Priority 0 -RequiredCondition (New-Condition 5 10 0xFF 1) -EntityName 'bar4' -ScriptType 'Talk'

# The live church-to-house run establishes which apparent exit gaps are
# actually mandatory scripted interactions. chrin_1b gives control back to
# the player at GameMoment 140 and puts the only forward MAPJUMP inside
# Aeris's Talk script. chrin_3b uses an [OK] line to set moment 155 before the
# roof transition. Neither transition is a native gateway, so both belong in
# Story rather than as fabricated Exit entries.
Add-Definition -FieldId 183 -FieldName 'chrin_1b' -Kind 'Model' -Label 'Talk to Aeris by the flowers' -EntityId 6 -TargetGameMoment 143 -MinimumGameMoment 140 -MaximumGameMoment 142 -Priority 0 -RequiredCondition (New-Condition 3 17 0x01 0x00) -CompletedCondition (New-Condition 3 17 0x01 0x01) -EntityName 'earith' -ScriptType 'Talk'
Add-Definition -FieldId 183 -FieldName 'chrin_1b' -Kind 'Model' -Label 'Talk to Aeris again after Reno arrives' -EntityId 6 -TargetGameMoment 143 -MinimumGameMoment 140 -MaximumGameMoment 142 -Priority 0 -RequiredCondition (New-Condition 3 17 0x01 0x01) -EntityName 'earith' -ScriptType 'Talk'
Add-Definition -FieldId 186 -FieldName 'chrin_3b' -Kind 'Location' -Label 'Cross the final roof beam to escape the church' -EntityId 1 -X 213 -Y -12 -Z 935 -TargetGameMoment 155 -MinimumGameMoment 152 -MaximumGameMoment 154 -Priority 0 -EntityName 'jump' -ScriptType '[OK]' -RequiredEnabledLineEntityId 1 -TriggerLine ([ordered]@{ startX = 213; startY = 2; startZ = 935; endX = 213; endY = -25; endZ = 935 }) -KeepActiveOnArrival

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
Add-Definition -FieldId 187 -FieldName 'eals_1' -Kind 'Location' -Label "Enter Aeris's house" -EntityId 9 -X 12 -Y 98 -Z 0 -TargetGameMoment 164 -MinimumGameMoment 158 -MaximumGameMoment 163 -Priority 0 -EntityName 'll' -ScriptType 'Move' -RequiredEnabledLineEntityId 9 -TriggerLine ([ordered]@{ startX = -22; startY = 98; startZ = 0; endX = 46; endY = 98; endZ = 0 })
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
Add-Definition -FieldId 154 -FieldName 'mds7pb_1' -Kind 'Location' -Label 'Approach the front door; Barret and Avalanche are entering' -X 264 -Y 0 -Z 0 -TargetGameMoment 72 -MinimumGameMoment 69 -MaximumGameMoment 71 -Priority 0 -CompletedCondition (New-Condition 3 212 0x01 0x01) -EntityName 'border1' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 255; startY = -48; startZ = 0; endX = 274; endY = 48; endZ = 0 })
Add-Definition -FieldId 154 -FieldName 'mds7pb_1' -Kind 'Location' -Label 'Use the pinball machine to descend to the Avalanche basement' -X -30 -Y 180 -Z 0 -MinimumGameMoment 72 -MaximumGameMoment 77 -Priority 0 -EntityName 'pinball' -ScriptType 'Go'
Add-Definition -FieldId 155 -FieldName 'mds7pb_2' -Kind 'Location' -Label 'Approach Barret and the Avalanche meeting' -X 12 -Y 168 -Z 0 -MinimumGameMoment 72 -MaximumGameMoment 77 -Priority 0 -CompletedCondition (New-Condition 3 214 0x01 0x01) -EntityName 'border2' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 56; startY = 214; startZ = 0; endX = -32; endY = 123; endZ = 0 })
Add-Definition -FieldId 155 -FieldName 'mds7pb_2' -Kind 'Location' -Label 'Head back toward the pinball elevator; Tifa wants to speak' -X 12 -Y 168 -Z 0 -TargetGameMoment 78 -MinimumGameMoment 72 -MaximumGameMoment 77 -Priority 0 -RequiredCondition (New-Condition 3 214 0x03 0x03) -CompletedCondition (New-Condition 3 214 0x04 0x04) -EntityName 'border2' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 56; startY = 214; startZ = 0; endX = -32; endY = 123; endZ = 0 })
Add-Definition -FieldId 155 -FieldName 'mds7pb_2' -Kind 'Location' -Label 'Use the pinball machine to return upstairs' -X 142 -Y -40 -Z 0 -MinimumGameMoment 78 -MaximumGameMoment 83 -Priority 0 -EntityName 'pinball' -ScriptType 'Go'
Add-Definition -FieldId 154 -FieldName 'mds7pb_1' -Kind 'Location' -Label 'Walk toward the front door; Tifa will stop Cloud about the promise' -X 50 -Y -24 -Z 0 -TargetGameMoment 84 -MinimumGameMoment 78 -MaximumGameMoment 83 -Priority 0 -RequiredCondition (New-Condition 3 213 0x01 0x01) -CompletedCondition (New-Condition 3 213 0x08 0x08) -EntityName 'border4' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 51; startY = 48; startZ = 0; endX = 49; endY = -96; endZ = 0 })
Add-Definition -FieldId 155 -FieldName 'mds7pb_2' -Kind 'Location' -Label 'Use the pinball machine to return to the bar after waking' -X 142 -Y -40 -Z 0 -MinimumGameMoment 105 -MaximumGameMoment 107 -Priority 0 -EntityName 'pinball' -ScriptType 'Go'

# Wall Market, Corneo Hall, and the sewers have a long mandatory sequence whose
# progress is mostly stored in local and persistent bitfields. Every model and
# line below is the exact native activation target from the installed scripts.
Add-Definition -FieldId 214 -FieldName 'mrkt3' -Kind 'Model' -Label 'Ask about Tifa; choose the Tifa question' -EntityId 16 -TargetGameMoment 190 -MinimumGameMoment 188 -MaximumGameMoment 189 -Priority 0 -EntityName 'fatman1' -ScriptType 'Talk'
Add-Definition -FieldId 206 -FieldName 'colne_1' -Kind 'Model' -Label 'Ask to enter Corneo Hall; learn men are refused' -EntityId 14 -TargetGameMoment 191 -MinimumGameMoment 190 -MaximumGameMoment 190 -Priority 0 -CompletedCondition (New-Condition 3 162 0x01 0x01) -EntityName 'DOORMAN' -ScriptType 'Talk'
Add-Definition -FieldId 201 -FieldName 'mkt_s1' -Kind 'Location' -Label 'Ask the clothes-shop clerk for a dress; learn the owner is at the bar' -X 140 -Y 0 -Z 0 -MinimumGameMoment 191 -MaximumGameMoment 191 -Priority 0 -CompletedCondition (New-Condition 1 162 0x80 0x80) -EntityName 'line00' -ScriptType '[OK]' -TriggerLine ([ordered]@{ startX = 71; startY = -8; startZ = 0; endX = 208; endY = 8; endZ = 0 })
Add-Definition -FieldId 204 -FieldName 'mktpb' -Kind 'Model' -Label "Ask the clothes-shop owner to make Cloud's dress" -EntityId 14 -MinimumGameMoment 191 -MaximumGameMoment 191 -Priority 0 -RequiredCondition (New-Condition 1 162 0x80 0x80) -CompletedCondition (New-Condition 1 161 0xE0 0 -AnyBitSet) -EntityName 'oldm3' -ScriptType 'Talk'
Add-Definition -FieldId 201 -FieldName 'mkt_s1' -Kind 'Location' -Label 'Collect the finished dress and learn about the gym' -X 140 -Y 0 -Z 0 -MinimumGameMoment 191 -MaximumGameMoment 191 -Priority 0 -RequiredCondition (New-Condition 1 161 0xE0 0 -AnyBitSet) -CompletedCondition (New-Condition 1 161 0x08 0x08) -EntityName 'line00' -ScriptType '[OK]' -TriggerLine ([ordered]@{ startX = 71; startY = -8; startZ = 0; endX = 208; endY = 8; endZ = 0 })
Add-Definition -FieldId 197 -FieldName 'mkt_mens' -Kind 'Model' -Label 'Talk to Big Bro and complete the squat contest for a wig' -EntityId 12 -MinimumGameMoment 191 -MaximumGameMoment 191 -Priority 0 -RequiredCondition (New-Condition 1 161 0x08 0x08) -CompletedCondition (New-Condition 1 160 0x80 0x80) -EntityName 'okama' -ScriptType 'Talk'
Add-Definition -FieldId 201 -FieldName 'mkt_s1' -Kind 'Location' -Label 'Enter the fitting room and choose to change clothes' -X -113 -Y 158 -Z 0 -TargetGameMoment 192 -MinimumGameMoment 191 -MaximumGameMoment 191 -Priority 0 -RequiredCondition (New-Condition 1 160 0x80 0x80) -CompletedCondition (New-Condition 3 162 0x02 0x02) -EntityName 'line01' -ScriptType '[OK]' -TriggerLine ([ordered]@{ startX = -174; startY = 150; startZ = 0; endX = -52; endY = 167; endZ = 0 })
Add-Definition -FieldId 206 -FieldName 'colne_1' -Kind 'Model' -Label 'Talk to the doorman while disguised and enter' -EntityId 14 -MinimumGameMoment 192 -MaximumGameMoment 192 -Priority 0 -RequiredCondition (New-Condition 3 162 0x03 0x03) -EntityName 'DOORMAN' -ScriptType 'Talk'
Add-Definition -FieldId 209 -FieldName 'colne_4' -Kind 'Model' -Label 'Talk to Tifa until the escort and selection sequence begins' -EntityId 12 -TargetGameMoment 197 -MinimumGameMoment 192 -MaximumGameMoment 192 -Priority 0 -CompletedCondition (New-Condition 3 162 0x08 0x08) -EntityName 'TIFA2' -ScriptType 'Talk'
Add-Definition -FieldId 208 -FieldName 'colne_3' -Kind 'Model' -Label "Confront Scotch and Corneo's lackeys" -EntityId 11 -MinimumGameMoment 197 -MaximumGameMoment 197 -Priority 0 -RequiredCondition (New-Condition 5 2 0xFF 0 -MinimumValue 3) -CompletedCondition (New-Condition 3 163 0x08 0x08) -EntityName 'SOTCH' -ScriptType 'Talk'
Add-Definition -FieldId 211 -FieldName 'colne_6' -Kind 'Location' -Label 'Cross the rug in the middle of the bedroom' -X 38 -Y 21 -Z -237 -TargetGameMoment 203 -MinimumGameMoment 197 -MaximumGameMoment 197 -Priority 0 -EntityName 'LINE' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 0; startY = 83; startZ = -237; endX = 76; endY = -41; endZ = -237 })
Add-Definition -FieldId 212 -FieldName 'colne_b1' -Kind 'Model' -Label 'Wake and check Aeris' -EntityId 2 -TargetGameMoment 209 -MinimumGameMoment 203 -MaximumGameMoment 203 -Priority 0 -CompletedCondition (New-Condition 5 10 0xFF 0x01) -EntityName 'ea' -ScriptType 'Talk'
Add-Definition -FieldId 212 -FieldName 'colne_b1' -Kind 'Model' -Label 'Wake and check Tifa' -EntityId 3 -TargetGameMoment 209 -MinimumGameMoment 203 -MaximumGameMoment 203 -Priority 0 -CompletedCondition (New-Condition 5 9 0xFF 0x01) -EntityName 'ti' -ScriptType 'Talk'
Add-Definition -FieldId 212 -FieldName 'colne_b1' -Kind 'Location' -Label 'Choose to climb out after defeating Aps' -X 1360 -Y -568 -Z 128 -MinimumGameMoment 209 -MaximumGameMoment 209 -Priority 0 -EntityName 'jp0' -ScriptType '[OK]' -TriggerLine ([ordered]@{ startX = 1360; startY = -513; startZ = 128; endX = 1360; endY = -622; endZ = 128 })
Add-Definition -FieldId 213 -FieldName 'colne_b3' -Kind 'Location' -Label 'Use the final sewer ladder' -X 423 -Y 310 -Z -211 -MinimumGameMoment 209 -MaximumGameMoment 209 -Priority 0 -EntityName 'ln2' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 354; startY = 314; startZ = -211; endX = 492; endY = 306; endZ = -211 })

# Train Graveyard begins after the sewer ladder has already advanced
# GameMoment to 212. The generic automatic extraction points back into the
# sewer at that exact moment, so this native forward chain replaces it.
# Bank 1 byte 164 records the two trains' live geometry. Native line20 advances
# state 0 or recovery state 2 to 3; line21 reverses 3 to 2; then line30 advances
# 3 to 7. The direct walkmesh route from line20's landing point to line30
# crosses line21, so route left of that trigger. Once both trains form the
# walkable bridge, the native path uses the ladders and upper station gateway.
Add-Definition -FieldId 144 -FieldName 'mds7st1' -Kind 'Location' -Label 'Continue into the Train Graveyard' -X 1688 -Y 676 -Z 0 -TargetGameMoment 215 -MinimumGameMoment 212 -MaximumGameMoment 214 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 145 -FieldName 'mds7st2' -Kind 'Location' -Label 'Move the first Train Graveyard train' -X 1740 -Y 3094 -Z 0 -TargetGameMoment 215 -MinimumGameMoment 212 -MaximumGameMoment 214 -Priority 0 -RequiredCondition (New-Condition 1 164 0x07 0x00) -EntityName 'line20' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 1691; startY = 3064; startZ = 0; endX = 1790; endY = 3124; endZ = 0 })
Add-Definition -FieldId 145 -FieldName 'mds7st2' -Kind 'Location' -Label 'Move the first Train Graveyard train back into place' -X 1740 -Y 3094 -Z 0 -TargetGameMoment 215 -MinimumGameMoment 212 -MaximumGameMoment 214 -Priority 0 -RequiredCondition (New-Condition 1 164 0x07 0x02) -EntityName 'line20' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 1691; startY = 3064; startZ = 0; endX = 1790; endY = 3124; endZ = 0 })
Add-Definition -FieldId 145 -FieldName 'mds7st2' -Kind 'Location' -Label 'Move the upper Train Graveyard train' -X 823 -Y 3482 -Z 0 -TargetGameMoment 215 -MinimumGameMoment 212 -MaximumGameMoment 214 -Priority 0 -RequiredCondition (New-Condition 1 164 0x07 0x03) -EntityName 'line30' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 776; startY = 3453; startZ = 0; endX = 871; endY = 3511; endZ = 0 }) -RouteDetours @(
    [ordered]@{
        blockedLine = [ordered]@{ startX = 798; startY = 2547; startZ = 0; endX = 896; endY = 2610; endZ = 0 }
        x = 720
        y = 2480
        z = 0
        clearance = 30
    },
    [ordered]@{
        blockedLine = [ordered]@{ startX = 798; startY = 2547; startZ = 0; endX = 896; endY = 2610; endZ = 0 }
        x = 720
        y = 2680
        z = 0
        clearance = 30
    })
Add-Definition -FieldId 145 -FieldName 'mds7st2' -Kind 'Location' -Label 'Leave the Train Graveyard for Sector 7 Station' -X -2153 -Y 3390 -Z 101 -TargetGameMoment 215 -MinimumGameMoment 212 -MaximumGameMoment 214 -Priority 0 -RequiredCondition (New-Condition 1 164 0x07 0x07) -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -2212; startY = 3557; startZ = 101; endX = -2095; endY = 3223; endZ = 101 })
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
Add-Definition -FieldId 191 -FieldName 'mds6_1' -Kind 'Location' -Label 'Continue until Barret and Tifa catch up' -EntityId 6 -X 368 -Y 240 -Z 166 -TargetGameMoment 248 -MinimumGameMoment 239 -MaximumGameMoment 239 -Priority 0 -EntityName 'ev' -ScriptType 'Move' -RequiredEnabledLineEntityId 6 -TriggerLine ([ordered]@{ startX = 220; startY = 343; startZ = 161; endX = 517; endY = 137; endZ = 171 })

# Once the party has joined, the native route returns through Sector 6 and
# Sector 5 to Aeris's house. On the post-collapse visit, ealin_2 entity 10's
# upstairs Move line is enabled while Bank 3 byte 65 bit 0 is clear. It sets
# that bit after the Barret and Marlene reunion. The native upper gateway then
# returns Cloud downstairs, where ealin_1/ealin_12 entity 13's Move line starts
# the rescue-planning scene and advances GameMoment to 257. The older entity 14
# line is only active on the first house visit and must not own this route.
Add-Definition -FieldId 191 -FieldName 'mds6_1' -Kind 'Location' -Label "Return through Sector 6 toward Aeris's house" -X -1068 -Y -754 -Z 0 -TargetGameMoment 257 -MinimumGameMoment 248 -MaximumGameMoment 256 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 172 -FieldName 'mds5_3' -Kind 'Location' -Label "Continue through Sector 5 toward Aeris's house" -X 122 -Y 547 -Z 0 -TargetGameMoment 257 -MinimumGameMoment 248 -MaximumGameMoment 256 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 177 -FieldName 'mds5_1' -Kind 'Location' -Label "Enter Aeris's garden" -X 630 -Y 1314 -Z 0 -TargetGameMoment 257 -MinimumGameMoment 248 -MaximumGameMoment 256 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 187 -FieldName 'eals_1' -Kind 'Location' -Label "Enter Aeris's house" -EntityId 9 -X 12 -Y 98 -Z 0 -TargetGameMoment 257 -MinimumGameMoment 248 -MaximumGameMoment 256 -Priority 0 -EntityName 'll' -ScriptType 'Move' -RequiredEnabledLineEntityId 9 -TriggerLine ([ordered]@{ startX = -22; startY = 98; startZ = 0; endX = 46; endY = 98; endZ = 0 })
Add-Definition -FieldId 188 -FieldName 'ealin_1' -Kind 'Location' -Label 'Go upstairs to Barret and Marlene' -X 204 -Y 371 -Z 288 -TargetGameMoment 257 -MinimumGameMoment 248 -MaximumGameMoment 256 -Priority 0 -RequiredCondition (New-Condition 3 65 0x01 0x00) -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 188; startY = 332; startZ = 288; endX = 220; endY = 409; endZ = 288 })
Add-Definition -FieldId 189 -FieldName 'ealin_12' -Kind 'Location' -Label 'Go upstairs to Barret and Marlene' -X 204 -Y 371 -Z 288 -TargetGameMoment 257 -MinimumGameMoment 248 -MaximumGameMoment 256 -Priority 0 -RequiredCondition (New-Condition 3 65 0x01 0x00) -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 188; startY = 332; startZ = 288; endX = 220; endY = 409; endZ = 288 })
Add-Definition -FieldId 190 -FieldName 'ealin_2' -Kind 'Location' -Label 'Approach Barret and Marlene upstairs' -EntityId 10 -X 86 -Y 30 -Z 288 -TargetGameMoment 257 -MinimumGameMoment 248 -MaximumGameMoment 256 -Priority 0 -RequiredCondition (New-Condition 3 65 0x01 0x00) -CompletedCondition (New-Condition 3 65 0x01 0x01) -EntityName 'ev' -ScriptType 'Move' -RequiredEnabledLineEntityId 10 -TriggerLine ([ordered]@{ startX = 22; startY = 60; startZ = 288; endX = 150; endY = 0; endZ = 288 }) -KeepActiveOnArrival
Add-Definition -FieldId 190 -FieldName 'ealin_2' -Kind 'Location' -Label 'Go downstairs after checking on Marlene' -X 83 -Y 442 -Z 69 -TargetGameMoment 257 -MinimumGameMoment 248 -MaximumGameMoment 256 -Priority 0 -RequiredCondition (New-Condition 3 65 0x01 0x01) -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 94; startY = 486; startZ = 54; endX = 72; endY = 398; endZ = 84 })
Add-Definition -FieldId 188 -FieldName 'ealin_1' -Kind 'Location' -Label "Leave Aeris's house to plan her rescue" -EntityId 13 -X -170 -Y -138 -Z 0 -TargetGameMoment 257 -MinimumGameMoment 248 -MaximumGameMoment 256 -Priority 0 -RequiredCondition (New-Condition 3 65 0x01 0x01) -EntityName 'checkun' -ScriptType 'Move' -RequiredEnabledLineEntityId 13 -TriggerLine ([ordered]@{ startX = -232; startY = -110; startZ = 0; endX = -109; endY = -166; endZ = 0 }) -KeepActiveOnArrival
Add-Definition -FieldId 189 -FieldName 'ealin_12' -Kind 'Location' -Label "Leave Aeris's house to plan her rescue" -EntityId 13 -X -170 -Y -138 -Z 0 -TargetGameMoment 257 -MinimumGameMoment 248 -MaximumGameMoment 256 -Priority 0 -RequiredCondition (New-Condition 3 65 0x01 0x01) -EntityName 'checkun' -ScriptType 'Move' -RequiredEnabledLineEntityId 13 -TriggerLine ([ordered]@{ startX = -232; startY = -110; startZ = 0; endX = -109; endY = -166; endZ = 0 }) -KeepActiveOnArrival

# Return to Wall Market, buy the three visible batteries, and use the rope.
# Bank 1 byte 165 bits 5-7 are set together by the native battery seller.
Add-Definition -FieldId 187 -FieldName 'eals_1' -Kind 'Location' -Label 'Leave the garden and return toward Wall Market' -X -188 -Y -149 -Z 0 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 177 -FieldName 'mds5_1' -Kind 'Location' -Label 'Backtrack through Sector 5 toward Wall Market' -X -944 -Y 4 -Z 0 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 172 -FieldName 'mds5_3' -Kind 'Location' -Label 'Enter Sector 6 on the way to Wall Market' -X -667 -Y -156 -Z 0 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 191 -FieldName 'mds6_1' -Kind 'Location' -Label 'Cross the collapsed expressway toward Wall Market' -EntityId 7 -X 1277 -Y 345 -Z 22 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -EntityName 'jp' -ScriptType 'Go 1x' -RequiredEnabledLineEntityId 7 -TriggerLine ([ordered]@{ startX = 1195; startY = 427; startZ = 22; endX = 1359; endY = 263; endZ = 22 })
Add-Definition -FieldId 193 -FieldName 'mds6_22' -Kind 'Location' -Label 'Leave the playground toward Wall Market' -X -424 -Y 1426 -Z 0 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 194 -FieldName 'mds6_3' -Kind 'Location' -Label 'Continue into Wall Market' -X 62 -Y 848 -Z 0 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 195 -FieldName 'mrkt2' -Kind 'Location' -Label 'Go north to the weapon shop for batteries' -X -135 -Y 2496 -Z 0 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 205 -FieldName 'mrkt1' -Kind 'Location' -Label 'Enter the weapon shop and buy batteries' -X 376 -Y -1266 -Z 0 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -CompletedCondition (New-Condition 1 165 0xE0 0xE0) -ScriptType 'Gateway'
Add-Definition -FieldId 196 -FieldName 'mkt_w' -Kind 'Model' -Label 'Buy three batteries for the wall climb' -EntityId 9 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -CompletedCondition (New-Condition 1 165 0xE0 0xE0) -EntityName 'oyaji02' -ScriptType 'Talk'
Add-Definition -FieldId 196 -FieldName 'mkt_w' -Kind 'Location' -Label 'Leave the weapon shop with the batteries' -X -18 -Y -102 -Z 0 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -RequiredCondition (New-Condition 1 165 0xE0 0xE0) -ScriptType 'Gateway'
Add-Definition -FieldId 205 -FieldName 'mrkt1' -Kind 'Location' -Label 'Go to the wall-climb entrance' -X 664 -Y -154 -Z 0 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -RequiredCondition (New-Condition 1 165 0xE0 0xE0) -ScriptType 'Gateway'
Add-Definition -FieldId 222 -FieldName 'mrkt4' -Kind 'Location' -Label 'Use the rope to begin climbing the wall' -EntityId 3 -X 5 -Y 630 -Z -1333 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -RequiredCondition (New-Condition 1 165 0xE0 0xE0) -EntityName 'line00' -ScriptType '[OK]' -RequiredEnabledLineEntityId 3 -TriggerLine ([ordered]@{ startX = -16; startY = 632; startZ = -1333; endX = 26; endY = 627; endZ = -1333 })

# The wall climb is a sequence of two battery sockets, the timed swinging-bar
# prompt, and the final native gateway. wcrimb_1's first propeller animation
# writes Bank 1 byte 165 bit 1 (0x02); bit 0 (0x01) only records inspecting
# the socket without installing a battery. The second socket writes bit 2
# (0x04), so the mandatory route is complete at mask 0x06. Keep the action
# targets active on arrival until those native state changes occur.
# Background segment MAPJUMPs are not exposed as separate goals.
Add-Definition -FieldId 223 -FieldName 'wcrimb_1' -Kind 'Location' -Label 'Place a battery in the first wall-climb socket' -EntityId 32 -X 304 -Y 724 -Z 1547 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -RequiredCondition (New-Condition 1 165 0xE0 0xE0) -CompletedCondition (New-Condition 1 165 0x02 0x02) -EntityName 'lined0' -ScriptType '[OK]' -RequiredEnabledLineEntityId 32 -TriggerLine ([ordered]@{ startX = 346; startY = 732; startZ = 1542; endX = 262; endY = 716; endZ = 1552 }) -KeepActiveOnArrival
Add-Definition -FieldId 223 -FieldName 'wcrimb_1' -Kind 'Location' -Label 'Place a battery in the second wall-climb socket' -EntityId 25 -X -72 -Y 1034 -Z 2280 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -RequiredCondition (New-Condition 1 165 0x02 0x02) -CompletedCondition (New-Condition 1 165 0x04 0x04) -EntityName 'line82' -ScriptType '[OK]' -RequiredEnabledLineEntityId 25 -TriggerLine ([ordered]@{ startX = -25; startY = 997; startZ = 2249; endX = -118; endY = 1071; endZ = 2311 }) -KeepActiveOnArrival
# wcrimb_1 is one half of a wall the party climbs by alternating with wcrimb_2, and
# its walkmesh has sixteen components. The arrivals on triangles0/1 and68/69
# cannot reach the swinging bar. The0/1 ledge can rejoin the final-ladder approach;
# the line02 components28,29,162..165 and68,69 first return through224 using the
# explicit Story step below. Arrivals on triangles2 and111 can reach the bar.
Add-Definition -FieldId 223 -FieldName 'wcrimb_1' -Kind 'Location' -Label 'Reach the swinging bar and press OK at the prompt' -X -369 -Y 1677 -Z 3290 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -RequiredCondition (New-Condition 1 165 0x06 0x06) -ExcludedPlayerTriangles @(0, 1, 28, 29, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 68, 69, 162, 163, 164, 165, 176, 177) -ScriptType 'Native triangle 13' -KeepActiveOnArrival
Add-Definition -FieldId 223 -FieldName 'wcrimb_1' -Kind 'Location' -Label 'Climb the final ladder after landing from the swinging bar' -EntityId 10 -X 280 -Y 2042 -Z 3240 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -RequiredCondition (New-Condition 1 165 0x06 0x06) -RequiredPlayerTriangles @(0, 1, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 176, 177) -EntityName 'line03' -ScriptType 'Move' -RequiredEnabledLineEntityId 10 -TriggerLine ([ordered]@{ startX = 231; startY = 2040; startZ = 3235; endX = 328; endY = 2043; endZ = 3244 })

# And what those two do instead, which is the same thing. line02, entity 9, is a
# LINE at (-175,1461,3463)-(-106,1464,3457) on triangle 164, the c4 floor. Its Move
# handler is IFUB Bank[5][36] === 0 then REQEW 03 21 c8 - entity 33, priority 6,
# script 8 - and cloud's script 8 is LADER up to (-111,1554,3673) triangle 68, then
# GETAI the triangle it ended on and, for mapinit's Bank[6][18]=68 or Bank[6][20]=69,
# MAPJUMP 60 e0 00 dc ff 3e 03 5b 00 80: field 224, (-36,830), triangle 91.
# Triangle 91 is in wallClimbOptionalReturnTriangles below, so the landing already
# offers the left ladder back down to the swinging bar and the round trip closes.
#
# 68,69 is c9, the ledge field 224 MAPJUMPs onto, and its only way on is cloud's
# script 4 back down to triangle 29 - the same c4 floor. One row covers both because
# the planner walks that ladder, and it carries the battery gate and the band of the
# bar it is heading back to.
Add-Definition -FieldId 223 -FieldName 'wcrimb_1' -Kind 'Location' -Label 'Take the ladder across to the other wall and back toward the swinging bar' -EntityId 9 -X -141 -Y 1463 -Z 3460 -TargetGameMoment 260 -MinimumGameMoment 257 -MaximumGameMoment 259 -Priority 0 -RequiredCondition (New-Condition 1 165 0x06 0x06) -RequiredPlayerTriangles @(28, 29, 68, 69, 162, 163, 164, 165) -EntityName 'line02' -ScriptType 'Move' -RequiredEnabledLineEntityId 9 -TriggerLine ([ordered]@{ startX = -175; startY = 1461; startZ = 3463; endX = -106; endY = 1464; endZ = 3457 })

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
Add-Definition -FieldId 234 -FieldName 'blin1' -Kind 'Location' -Label 'Take the left lobby elevator toward floor 59' -EntityId 15 -X -1558 -Y -117 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -EntityName 'ELINEL' -ScriptType '[OK]' -RequiredEnabledLineEntityId 15 -TriggerLine ([ordered]@{ startX = -1559; startY = -192; startZ = 0; endX = -1558; endY = -42; endZ = 0 })
Add-Definition -FieldId 234 -FieldName 'blin1' -Kind 'Location' -Label 'Take the right lobby elevator toward floor 59' -EntityId 16 -X -1550 -Y 138 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -EntityName 'ELINER' -ScriptType '[OK]' -RequiredEnabledLineEntityId 16 -TriggerLine ([ordered]@{ startX = -1549; startY = 58; startZ = 0; endX = -1551; endY = 217; endZ = 0 })
Add-Definition -FieldId 235 -FieldName 'blin2' -Kind 'Location' -Label 'Take the left lobby elevator toward floor 59' -EntityId 18 -X -1558 -Y -117 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -EntityName 'ELINEL' -ScriptType '[OK]' -RequiredEnabledLineEntityId 18 -TriggerLine ([ordered]@{ startX = -1559; startY = -192; startZ = 0; endX = -1558; endY = -42; endZ = 0 })
Add-Definition -FieldId 235 -FieldName 'blin2' -Kind 'Location' -Label 'Take the right lobby elevator toward floor 59' -EntityId 19 -X -1550 -Y 138 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -EntityName 'ELINER' -ScriptType '[OK]' -RequiredEnabledLineEntityId 19 -TriggerLine ([ordered]@{ startX = -1549; startY = 58; startZ = 0; endX = -1551; endY = 217; endZ = 0 })
Add-Definition -FieldId 237 -FieldName 'blin3_1' -Kind 'Location' -Label 'Take the left lobby elevator toward floor 59' -EntityId 12 -X -1558 -Y -117 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -EntityName 'ELINEL' -ScriptType '[OK]' -RequiredEnabledLineEntityId 12 -TriggerLine ([ordered]@{ startX = -1559; startY = -192; startZ = 0; endX = -1558; endY = -42; endZ = 0 })
Add-Definition -FieldId 237 -FieldName 'blin3_1' -Kind 'Location' -Label 'Take the right lobby elevator toward floor 59' -EntityId 13 -X -1550 -Y 138 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -EntityName 'ELINER' -ScriptType '[OK]' -RequiredEnabledLineEntityId 13 -TriggerLine ([ordered]@{ startX = -1549; startY = 58; startZ = 0; endX = -1551; endY = 217; endZ = 0 })
# SWITCH itself occupies isolated collision triangle 3 at (-105,136). Guide to
# the reachable face immediately in front of it so GPS does not route onto the
# non-walkable switch model.
Add-Definition -FieldId 232 -FieldName 'blinele' -Kind 'Location' -Label 'Press the elevator switch to continue toward floor 59' -EntityId 16 -X -105 -Y 110 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -EntityName 'SWITCH' -ScriptType '[OK]' -KeepActiveOnArrival
Add-Definition -FieldId 228 -FieldName 'sinbil_2' -Kind 'Location' -Label 'Continue up the emergency stairs' -X 196 -Y 1390 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 229 -FieldName 'blinst_1' -Kind 'Location' -Label 'Continue up the emergency stairs' -EntityId 6 -X 175 -Y 398 -Z 2169 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -EntityName 'LINEU' -ScriptType 'Move' -RequiredEnabledLineEntityId 6 -TriggerLine ([ordered]@{ startX = 175; startY = 482; startZ = 2169; endX = 175; endY = 314; endZ = 2169 })
Add-Definition -FieldId 230 -FieldName 'blinst_2' -Kind 'Location' -Label 'Continue up the left stairway' -EntityId 10 -X 175 -Y 403 -Z 3069 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -ScriptType 'Move' -RequiredEnabledLineEntityId 10 -TriggerLine ([ordered]@{ startX = 175; startY = 481; startZ = 3069; endX = 175; endY = 325; endZ = 3069 })
Add-Definition -FieldId 230 -FieldName 'blinst_2' -Kind 'Location' -Label 'Continue up the right stairway' -EntityId 12 -X 216 -Y 404 -Z 3038 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -ScriptType 'Move' -RequiredEnabledLineEntityId 12 -TriggerLine ([ordered]@{ startX = 216; startY = 480; startZ = 3038; endX = 216; endY = 328; endZ = 3038 })
Add-Definition -FieldId 231 -FieldName 'blinst_3' -Kind 'Location' -Label 'Leave the stairs for the 59th floor by the left exit' -EntityId 9 -X 476 -Y 208 -Z 2043 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -ScriptType 'Move' -RequiredEnabledLineEntityId 9 -TriggerLine ([ordered]@{ startX = 476; startY = 324; startZ = 2042; endX = 476; endY = 92; endZ = 2044 })
Add-Definition -FieldId 231 -FieldName 'blinst_3' -Kind 'Location' -Label 'Leave the stairs for the 59th floor by the right exit' -EntityId 10 -X 563 -Y 157 -Z 2042 -TargetGameMoment 263 -MinimumGameMoment 259 -MaximumGameMoment 262 -Priority 0 -ScriptType 'Move' -RequiredEnabledLineEntityId 10 -TriggerLine ([ordered]@{ startX = 563; startY = 111; startZ = 2043; endX = 563; endY = 203; endZ = 2041 })

# The two diagonal floor-59 ambush lines are equivalent. The battle awards
# Keycard 60 by writing Bank 1 byte 224, after which either elevator line is
# valid and the player must choose floor 60 from the native selector.
Add-Definition -FieldId 238 -FieldName 'blin59' -Kind 'Location' -Label 'Approach the left guard group and obtain Keycard 60' -EntityId 23 -X 408 -Y -688 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 260 -MaximumGameMoment 262 -Priority 0 -CompletedCondition (New-Condition 1 224 0xFF 60) -EntityName 'KLINEB' -ScriptType 'Move' -RequiredEnabledLineEntityId 23 -TriggerLine ([ordered]@{ startX = 60; startY = -466; startZ = 0; endX = 756; endY = -909; endZ = 0 })
Add-Definition -FieldId 238 -FieldName 'blin59' -Kind 'Location' -Label 'Approach the right guard group and obtain Keycard 60' -EntityId 24 -X 403 -Y -142 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 260 -MaximumGameMoment 262 -Priority 0 -CompletedCondition (New-Condition 1 224 0xFF 60) -EntityName 'KLINEA' -ScriptType 'Move' -RequiredEnabledLineEntityId 24 -TriggerLine ([ordered]@{ startX = 0; startY = -538; startZ = 0; endX = 806; endY = 255; endZ = 0 })
Add-Definition -FieldId 238 -FieldName 'blin59' -Kind 'Location' -Label 'Enter the right floor elevator with Keycard 60' -EntityId 16 -X 604 -Y -643 -Z -1 -TargetGameMoment 263 -MinimumGameMoment 260 -MaximumGameMoment 262 -Priority 0 -RequiredCondition (New-Condition 1 224 0xFF 60) -EntityName 'DLINER' -ScriptType 'Move' -RequiredEnabledLineEntityId 16 -TriggerLine ([ordered]@{ startX = 603; startY = -565; startZ = -1; endX = 606; endY = -721; endZ = -1 })
Add-Definition -FieldId 238 -FieldName 'blin59' -Kind 'Location' -Label 'Enter the left floor elevator with Keycard 60' -EntityId 17 -X 602 -Y -366 -Z 0 -TargetGameMoment 263 -MinimumGameMoment 260 -MaximumGameMoment 262 -Priority 0 -RequiredCondition (New-Condition 1 224 0xFF 60) -EntityName 'DLINEL' -ScriptType 'Move' -RequiredEnabledLineEntityId 17 -TriggerLine ([ordered]@{ startX = 610; startY = -279; startZ = 0; endX = 595; endY = -452; endZ = 0 })
Add-Definition -FieldId 233 -FieldName 'eleout' -Kind 'Location' -Label 'Use the elevator controls and choose floor 60' -EntityId 6 -X -2 -Y 36 -Z -1 -TargetGameMoment 263 -MinimumGameMoment 260 -MaximumGameMoment 262 -Priority 0 -RequiredCondition (New-Condition 1 224 0xFF 60) -EntityName 'lin0' -ScriptType '[OK]'

# Floor 60 has three equivalent security-room entrances. In the native timed
# sequence, LINE1 explains the guard timing and Bank 5 byte 13 advances as
# Tifa crosses each statue gap. At six, LINE2 lets Cloud take the party to the
# far side; Cloud's script then sets Bank 3 byte 172 bit 3 before the exit.
Add-Definition -FieldId 240 -FieldName 'blin60_2' -Kind 'Location' -Label 'Enter the security room by the left route' -X -440 -Y -920 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 240 -FieldName 'blin60_2' -Kind 'Location' -Label 'Enter the security room by the middle route' -X -85 -Y -778 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 240 -FieldName 'blin60_2' -Kind 'Location' -Label 'Enter the security room by the right route' -X 209 -Y -904 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 239 -FieldName 'blin60_1' -Kind 'Location' -Label 'Reach the floor 60 signaling point; press OK when the soldiers turn away' -EntityId 21 -X 5 -Y 66 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -CompletedCondition (New-Condition 5 13 0xFF 0 -AnyBitSet) -EntityName 'LINE1' -ScriptType 'Go 1x' -RequiredEnabledLineEntityId 21 -TriggerLine ([ordered]@{ startX = 5; startY = 258; startZ = 0; endX = 5; endY = -126; endZ = 0 })
Add-Definition -FieldId 239 -FieldName 'blin60_1' -Kind 'Location' -Label 'Continue signaling Barret and Tifa when the soldiers turn away' -X 7 -Y 204 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 5 13 0xFF 0 -AnyBitSet) -CompletedCondition (New-Condition 5 13 0xFF 6) -EntityName 'LINE1' -ScriptType 'Native signal state'
Add-Definition -FieldId 239 -FieldName 'blin60_1' -Kind 'Location' -Label 'Cross to the far side after Barret and Tifa are safely across' -EntityId 28 -X 551 -Y 51 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 5 13 0xFF 6) -CompletedCondition (New-Condition 3 172 0x08 0x08) -EntityName 'LINE2' -ScriptType 'Go 1x' -RequiredEnabledLineEntityId 28 -TriggerLine ([ordered]@{ startX = 553; startY = 291; startZ = 0; endX = 549; endY = -189; endZ = 0 })
Add-Definition -FieldId 239 -FieldName 'blin60_1' -Kind 'Location' -Label 'Continue to floor 61 after clearing security' -X 1078 -Y 189 -Z 227 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 3 172 0x08 0x08) -ScriptType 'Gateway'
Add-Definition -FieldId 241 -FieldName 'blin61' -Kind 'Location' -Label 'Continue to Mayor Domino on floor 62' -X 1166 -Y -148 -Z 207 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 1 59 0x01 0x01) -ScriptType 'Gateway'

# Once Mayor Domino's challenge is complete, Keycard 65 makes either elevator
# or the optional stair route valid.
Add-Definition -FieldId 242 -FieldName 'blin62_1' -Kind 'Location' -Label 'Use the left elevator and choose floor 65' -X -130 -Y -994 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 3 179 0x10 0x10) -ScriptType 'Move'
Add-Definition -FieldId 242 -FieldName 'blin62_1' -Kind 'Location' -Label 'Use the right elevator and choose floor 65' -X 131 -Y -994 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 3 179 0x10 0x10) -ScriptType 'Move'
Add-Definition -FieldId 242 -FieldName 'blin62_1' -Kind 'Location' -Label 'Take the stairs toward floor 65' -X 1060 -Y 212 -Z 222 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 3 179 0x10 0x10) -ScriptType 'Gateway'
Add-Definition -FieldId 233 -FieldName 'eleout' -Kind 'Location' -Label 'Use the elevator controls and choose floor 65' -X -2 -Y 36 -Z -1 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 3 179 0x10 0x10) -EntityName 'lin0' -ScriptType '[OK]' -TriggerLine ([ordered]@{ startX = -77; startY = 0; startZ = -1; endX = 73; endY = 73; endZ = -1 })
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
Add-Definition -FieldId 252 -FieldName 'blin66_3' -Kind 'Location' -Label 'Choose climb up at the bathroom vent' -EntityId 2 -X 64 -Y 146 -Z -376 -TargetGameMoment 269 -MinimumGameMoment 264 -MaximumGameMoment 268 -Priority 0 -EntityName 'vent' -ScriptType '[OK]' -RequiredEnabledLineEntityId 2 -TriggerLine ([ordered]@{ startX = 65; startY = 98; startZ = -376; endX = 62; endY = 193; endZ = -376 })
Add-Definition -FieldId 253 -FieldName 'blin66_4' -Kind 'Location' -Label 'Crawl down the duct to eavesdrop on the conference' -X 448 -Y -536 -Z -28 -TargetGameMoment 269 -MinimumGameMoment 264 -MaximumGameMoment 268 -Priority 0 -EntityName 'cl' -ScriptType 'Native LADER, advance Down'
Add-Definition -FieldId 256 -FieldName 'blin67_1' -Kind 'Location' -Label 'Ride the floor 67 elevator to floor 68' -EntityId 18 -X -435 -Y 976 -Z 0 -TargetGameMoment 284 -MinimumGameMoment 280 -MaximumGameMoment 283 -Priority 0 -EntityName 'ele0' -ScriptType 'Move' -RequiredEnabledLineEntityId 18 -TriggerLine ([ordered]@{ startX = -475; startY = 891; startZ = 0; endX = -396; endY = 1061; endZ = 0 })
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
Add-Definition -FieldId 258 -FieldName 'blin67_2' -Kind 'Location' -Label 'Step through the open cell door and investigate' -EntityId 7 -X 884 -Y 512 -Z 0 -TargetGameMoment 302 -MinimumGameMoment 296 -MaximumGameMoment 301 -Priority 0 -RequiredCondition (New-Condition 1 232 0x10 0x10) -CompletedCondition (New-Condition 1 232 0x10 0) -EntityName 'ln0' -ScriptType 'Go 1x' -RequiredEnabledLineEntityId 7 -TriggerLine ([ordered]@{ startX = 813; startY = 512; startZ = 0; endX = 954; endY = 512; endZ = 0 }) -KeepActiveOnArrival
Add-Definition -FieldId 258 -FieldName 'blin67_2' -Kind 'Model' -Label 'Inspect the dead guard outside the cell' -EntityId 6 -TargetGameMoment 302 -MinimumGameMoment 296 -MaximumGameMoment 301 -Priority 0 -RequiredConditions @((New-Condition 1 232 0x10 0), (New-Condition 5 21 0xFF 0)) -CompletedCondition (New-Condition 5 21 0xFF 1) -EntityName 'sikabane' -ScriptType 'Talk'
Add-Definition -FieldId 258 -FieldName 'blin67_2' -Kind 'Model' -Label 'Wake Tifa and talk to her' -EntityId 3 -TargetGameMoment 302 -MinimumGameMoment 296 -MaximumGameMoment 301 -Priority 0 -RequiredConditions @((New-Condition 5 21 0xFF 1), (New-Condition 5 16 0xFF 0)) -CompletedCondition (New-Condition 5 16 0xFF 1) -EntityName 'ti' -ScriptType 'Talk'
Add-Definition -FieldId 258 -FieldName 'blin67_2' -Kind 'Model' -Label 'Talk to Tifa again and leave the cell' -EntityId 3 -TargetGameMoment 302 -MinimumGameMoment 296 -MaximumGameMoment 301 -Priority 0 -RequiredConditions @((New-Condition 5 16 0xFF 1), (New-Condition 5 21 0xFF 1)) -CompletedCondition (New-Condition 5 16 0xFF 2) -EntityName 'ti' -ScriptType 'Talk'
Add-Definition -FieldId 258 -FieldName 'blin67_2' -Kind 'Model' -Label 'Talk to Barret after leaving the cell' -EntityId 4 -TargetGameMoment 302 -MinimumGameMoment 296 -MaximumGameMoment 301 -Priority 0 -RequiredCondition (New-Condition 5 16 0xFF 2) -CompletedCondition (New-Condition 5 20 0xFF 1) -EntityName 'ba' -ScriptType 'Talk'
Add-Definition -FieldId 258 -FieldName 'blin67_2' -Kind 'Model' -Label 'Talk to Red XIII after leaving the cell' -EntityId 5 -TargetGameMoment 302 -MinimumGameMoment 296 -MaximumGameMoment 301 -Priority 0 -RequiredCondition (New-Condition 5 16 0xFF 2) -CompletedCondition (New-Condition 5 20 0xFF 1) -EntityName 'red' -ScriptType 'Talk'
Add-Definition -FieldId 258 -FieldName 'blin67_2' -Kind 'Location' -Label 'Follow the blood trail out of the cell block' -EntityId 9 -X 412 -Y 690 -Z 0 -TargetGameMoment 302 -MinimumGameMoment 296 -MaximumGameMoment 301 -Priority 0 -RequiredCondition (New-Condition 5 20 0xFF 1) -CompletedCondition (New-Condition 5 20 0xFF 2) -EntityName 'ln2' -ScriptType 'Move' -RequiredEnabledLineEntityId 9 -TriggerLine ([ordered]@{ startX = 406; startY = 751; startZ = 0; endX = 418; endY = 628; endZ = 0 })
Add-Definition -FieldId 258 -FieldName 'blin67_2' -Kind 'Model' -Label 'Talk to Tifa and form the pursuit party' -EntityId 3 -TargetGameMoment 302 -MinimumGameMoment 296 -MaximumGameMoment 301 -Priority 0 -RequiredCondition (New-Condition 5 20 0xFF 2) -EntityName 'ti' -ScriptType 'Talk'
Add-Definition -FieldId 258 -FieldName 'blin67_2' -Kind 'Model' -Label 'Talk to Aeris and form the pursuit party' -EntityId 2 -TargetGameMoment 302 -MinimumGameMoment 296 -MaximumGameMoment 301 -Priority 0 -RequiredCondition (New-Condition 5 20 0xFF 2) -EntityName 'ea' -ScriptType 'Talk'

# Follow the blood trail through the Jenova chamber and up to floor 70.
Add-Definition -FieldId 257 -FieldName 'blin671b' -Kind 'Location' -Label 'Follow the blood trail through the left chamber entrance' -X -779 -Y -604 -Z 0 -TargetGameMoment 305 -MinimumGameMoment 302 -MaximumGameMoment 304 -Priority 0 -ScriptType 'Move'
Add-Definition -FieldId 257 -FieldName 'blin671b' -Kind 'Location' -Label 'Follow the blood trail through the middle chamber entrance' -X -680 -Y -253 -Z 0 -TargetGameMoment 305 -MinimumGameMoment 302 -MaximumGameMoment 304 -Priority 0 -ScriptType 'Move'
Add-Definition -FieldId 257 -FieldName 'blin671b' -Kind 'Location' -Label 'Follow the blood trail through the right chamber entrance' -X -1018 -Y -43 -Z 47 -TargetGameMoment 305 -MinimumGameMoment 302 -MaximumGameMoment 304 -Priority 0 -ScriptType 'Move'
Add-Definition -FieldId 260 -FieldName 'blin673b' -Kind 'Model' -Label 'Talk to Red XIII in the Jenova chamber' -EntityId 4 -TargetGameMoment 305 -MinimumGameMoment 302 -MaximumGameMoment 304 -Priority 0 -CompletedCondition (New-Condition 1 226 0x80 0x80) -EntityName 'red' -ScriptType 'Talk'
Add-Definition -FieldId 257 -FieldName 'blin671b' -Kind 'Location' -Label 'Use the specimen elevator after speaking with Red XIII' -X -436 -Y 976 -Z 0 -TargetGameMoment 305 -MinimumGameMoment 302 -MaximumGameMoment 304 -Priority 0 -RequiredCondition (New-Condition 1 226 0x80 0x80) -EntityName 'ele0' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = -475; startY = 891; startZ = 0; endX = -396; endY = 1061; endZ = 0 })
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
Add-Definition -FieldId 233 -FieldName 'eleout' -Kind 'Location' -Label 'Use the elevator controls' -X -2 -Y 36 -Z -1 -TargetGameMoment 314 -MinimumGameMoment 311 -MaximumGameMoment 313 -Priority 0 -EntityName 'lin0' -ScriptType '[OK]' -TriggerLine ([ordered]@{ startX = -77; startY = 0; startZ = -1; endX = 73; endY = 73; endZ = -1 })
Add-Definition -FieldId 268 -FieldName 'blin70_3' -Kind 'Location' -Label 'Return inside after defeating Rufus' -X -1220 -Y 450 -Z 182 -TargetGameMoment 323 -MinimumGameMoment 320 -MaximumGameMoment 322 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -1312; startY = 378; startZ = 182; endX = -1128; endY = 521; endZ = 182 })
Add-Definition -FieldId 266 -FieldName 'blin70_1' -Kind 'Location' -Label 'Take the upper stairs down to meet Tifa' -X -718 -Y 679 -Z -172 -TargetGameMoment 323 -MinimumGameMoment 320 -MaximumGameMoment 322 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -760; startY = 706; startZ = -191; endX = -675; endY = 651; endZ = -153 })
Add-Definition -FieldId 264 -FieldName 'blin69_1' -Kind 'Model' -Label 'Talk to Tifa and begin the motorcycle escape' -EntityId 5 -TargetGameMoment 323 -MinimumGameMoment 320 -MaximumGameMoment 322 -Priority 0 -EntityName 'ti' -ScriptType 'Talk'

# The motorcycle itself and Motor Ball are automatic. LINEO starts the escape;
# after roadend, the outskirts line opens party choice and then the final
# gateway leaves Midgar for the world map.
Add-Definition -FieldId 234 -FieldName 'blin1' -Kind 'Location' -Label 'Meet the party at the lobby exit and start the motorcycle escape' -EntityId 19 -X 1846 -Y 8 -Z 0 -TargetGameMoment 332 -MinimumGameMoment 326 -MaximumGameMoment 331 -Priority 0 -EntityName 'LINEO' -ScriptType 'Move' -RequiredEnabledLineEntityId 19 -TriggerLine ([ordered]@{ startX = 1855; startY = 126; startZ = 0; endX = 1837; endY = -110; endZ = 0 })
Add-Definition -FieldId 170 -FieldName 'mds5_5' -Kind 'Location' -Label 'Meet the party and choose the group for the journey' -EntityId 8 -X 563 -Y -2830 -Z 0 -TargetGameMoment 341 -MinimumGameMoment 335 -MaximumGameMoment 340 -Priority 0 -EntityName 'ln0' -ScriptType 'Move' -RequiredEnabledLineEntityId 8 -TriggerLine ([ordered]@{ startX = 22; startY = -2723; startZ = 0; endX = 1104; endY = -2938; endZ = 0 })
Add-Definition -FieldId 170 -FieldName 'mds5_5' -Kind 'Location' -Label 'Leave Midgar for the world map' -X 472 -Y -2877 -Z 0 -MinimumGameMoment 341 -MaximumGameMoment 341 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 47; startY = -2777; startZ = 0; endX = 897; endY = -2977; endZ = 0 })

# Preserve the reviewed late-game transition objective that predates the
# generated manual additions below. Its progression write is indirect, so the
# current Kujata milestone scan does not emit it on its own.
Add-Definition -FieldId 115 -FieldName 'whitebg3' -Kind 'Model' -Label 'Talk to That was the first time to continue' -EntityId 4 -TargetGameMoment 1180 -Priority 100 -EntityName 'tcl' -ScriptType 'Talk'

# Kalm remains at GameMoment 341 while the party enters town and gathers in
# the inn. The town arrival LINE records Bank 3, byte 128, bit 1; the inn's
# two upstairs LINE regions advance temporary byte 6 before the flashback
# director writes GameMoment 344. When the flashback ends at moment 385, the
# downstairs LINE grants the PHS and records Bank 3, byte 131, bit 0.
Add-Definition -FieldId 335 -FieldName 'elm' -Kind 'Location' -Label 'Follow the party into Kalm' -X -360 -Y -799 -Z -2 -MinimumGameMoment 341 -MaximumGameMoment 341 -Priority 0 -CompletedCondition (New-Condition 3 128 0x02 0x02) -EntityName 'first' -ScriptType '[OK]' -TriggerLine ([ordered]@{ startX = -614; startY = -589; startZ = -2; endX = -107; endY = -1009; endZ = -2 })
Add-Definition -FieldId 335 -FieldName 'elm' -Kind 'Location' -Label 'Enter the Kalm inn' -X -575 -Y -448 -Z -2 -MinimumGameMoment 341 -MaximumGameMoment 341 -Priority 0 -RequiredCondition (New-Condition 3 128 0x02 0x02) -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -582; startY = -488; startZ = -2; endX = -568; endY = -407; endZ = -2 })
Add-Definition -FieldId 331 -FieldName 'elminn_1' -Kind 'Location' -Label 'Go upstairs and meet the party' -X 70 -Y 124 -Z 186 -MinimumGameMoment 341 -MaximumGameMoment 341 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 68; startY = 173; startZ = 183; endX = 71; endY = 74; endZ = 190 })
Add-Definition -FieldId 332 -FieldName 'elminn_2' -Kind 'Location' -Label 'Join Aeris and the party upstairs' -X 253 -Y 115 -Z -6 -TargetGameMoment 344 -MinimumGameMoment 341 -MaximumGameMoment 343 -Priority 0 -RequiredCondition (New-Condition 5 6 0xFF 0) -CompletedCondition (New-Condition 5 6 0xFF 1) -EntityName 'line1' -ScriptType '[OK]' -TriggerLine ([ordered]@{ startX = 175; startY = 37; startZ = -6; endX = 331; endY = 192; endZ = -6 })
Add-Definition -FieldId 332 -FieldName 'elminn_2' -Kind 'Location' -Label "Stand with the party and begin Cloud's story" -X 170 -Y -164 -Z -6 -TargetGameMoment 344 -MinimumGameMoment 341 -MaximumGameMoment 343 -Priority 0 -RequiredCondition (New-Condition 5 6 0xFF 1) -EntityName 'line2' -ScriptType '[OK]' -TriggerLine ([ordered]@{ startX = 346; startY = -75; startZ = -6; endX = -7; endY = -253; endZ = -6 })
Add-Definition -FieldId 332 -FieldName 'elminn_2' -Kind 'Location' -Label "Go downstairs after Cloud's story" -X -44 -Y 131 -Z -180 -MinimumGameMoment 385 -MaximumGameMoment 385 -Priority 0 -CompletedCondition (New-Condition 3 131 0x01 0x01) -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -58; startY = 190; startZ = -196; endX = -29; endY = 71; endZ = -163 })
Add-Definition -FieldId 331 -FieldName 'elminn_1' -Kind 'Location' -Label 'Meet the party downstairs and receive the PHS' -X 74 -Y -228 -Z -1 -MinimumGameMoment 385 -MaximumGameMoment 385 -Priority 0 -RequiredCondition (New-Condition 3 131 0x01 0x00) -CompletedCondition (New-Condition 3 131 0x01 0x01) -EntityName 'line3' -ScriptType '[OK]' -TriggerLine ([ordered]@{ startX = 178; startY = -178; startZ = -1; endX = -30; endY = -278; endZ = -1 })

# The Nibelheim flashback holds GameMoment steady across several mandatory
# interactions. Native Bank 3 byte 18 records town arrival, sleep, and morning
# departure; byte 19 records the first inn conversation. These conditions keep
# the inn objectives in their real order even when Cloud visits the optional
# houses first.
Add-Definition -FieldId 282 -FieldName 'nivl' -Kind 'Location' -Label 'Enter the inn and meet Sephiroth' -X -170 -Y -334 -Z 0 -MinimumGameMoment 353 -MaximumGameMoment 356 -Priority 0 -RequiredCondition (New-Condition 3 18 0x01 0x01) -CompletedCondition (New-Condition 3 18 0x02 0x02) -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -211; startY = -342; startZ = 0; endX = -129; endY = -326; endZ = 0 })
Add-Definition -FieldId 273 -FieldName 'nivinn_1' -Kind 'Location' -Label 'Go upstairs to Sephiroth' -X 168 -Y -142 -Z 168 -MinimumGameMoment 353 -MaximumGameMoment 356 -Priority 0 -CompletedCondition (New-Condition 3 18 0x02 0x02) -ScriptType 'Gateway'
Add-Definition -FieldId 274 -FieldName 'nivinn_2' -Kind 'Model' -Label 'Talk to Sephiroth about the reactor mission' -EntityId 8 -MinimumGameMoment 353 -MaximumGameMoment 356 -Priority 0 -RequiredCondition (New-Condition 3 19 0x02 0) -CompletedCondition (New-Condition 3 19 0x02 0x02) -EntityName 'cef' -ScriptType 'Talk'
Add-Definition -FieldId 274 -FieldName 'nivinn_2' -Kind 'Model' -Label 'Talk to Sephiroth again and choose sleep' -EntityId 8 -MinimumGameMoment 353 -MaximumGameMoment 356 -Priority 0 -RequiredCondition (New-Condition 3 19 0x02 0x02) -CompletedCondition (New-Condition 3 18 0x02 0x02) -EntityName 'cef' -ScriptType 'Talk'
Add-Definition -FieldId 282 -FieldName 'nivl' -Kind 'Model' -Label 'Talk to Sephiroth to begin the Mt. Nibel expedition' -EntityId 8 -MinimumGameMoment 353 -MaximumGameMoment 356 -Priority 0 -RequiredCondition (New-Condition 3 18 0x02 0x02) -CompletedCondition (New-Condition 3 18 0x08 0x08) -EntityName 'cef' -ScriptType 'Talk'

# Tifa's talk advances moment 357 to 359. The following bridge, cave, and
# mountain definitions are the mandatory native gateways; intermediate fall
# and Mako-fountain scenes map-jump automatically and do not need fake targets.
$nibelReactorUpperTriangles = [int[]](28..51)
$nibelReactorLowerTriangles = [int[]](@(0..27) + @(52, 53))
Add-Definition -FieldId 312 -FieldName 'mtnvl3' -Kind 'Model' -Label 'Talk to Tifa before crossing the bridge' -EntityId 6 -TargetGameMoment 359 -MinimumGameMoment 357 -MaximumGameMoment 358 -Priority 0 -EntityName 'yti' -ScriptType 'Talk'
Add-Definition -FieldId 312 -FieldName 'mtnvl3' -Kind 'Location' -Label 'Cross the bridge toward Mt. Nibel' -EntityId 10 -X 2560 -Y -1008 -Z 985 -TargetGameMoment 361 -MinimumGameMoment 359 -MaximumGameMoment 360 -Priority 0 -EntityName 'lin0' -ScriptType 'Move' -RequiredEnabledLineEntityId 10 -TriggerLine ([ordered]@{ startX = 2543; startY = -1042; startZ = 986; endX = 2576; endY = -973; endZ = 984 })
Add-Definition -FieldId 313 -FieldName 'mtnvl4' -Kind 'Location' -Label 'Continue into the Mt. Nibel caves' -X 912 -Y 740 -Z -210 -MinimumGameMoment 361 -MaximumGameMoment 361 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 318 -FieldName 'nvdun2' -Kind 'Location' -Label 'Continue through the cave passage' -X -142 -Y 1788 -Z -416 -MinimumGameMoment 361 -MaximumGameMoment 362 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 315 -FieldName 'mtnvl6' -Kind 'Location' -Label 'Continue toward the Nibel Reactor' -X -574 -Y -592 -Z 32 -MinimumGameMoment 364 -MaximumGameMoment 364 -Priority 0 -ScriptType 'Gateway'

# nvmkin1 has two disconnected walkmesh components. The native ladu Move line
# starts party script 3, whose LADER opcode carries Cloud from the upper
# platform to lower triangle 24. Never expose a lower-level objective while
# the player is still on the upper component.
Add-Definition -FieldId 322 -FieldName 'nvmkin1' -Kind 'Location' -Label 'Climb down the ladder into the Nibel Reactor' -EntityId 12 -X -124 -Y 520 -Z 1068 -MinimumGameMoment 366 -MaximumGameMoment 366 -Priority 0 -RequiredPlayerTriangles $nibelReactorUpperTriangles -EntityName 'ladu' -ScriptType 'Move' -RequiredEnabledLineEntityId 12 -TriggerLine ([ordered]@{ startX = -82; startY = 476; startZ = 1068; endX = -166; endY = 564; endZ = 1068 }) -KeepActiveOnArrival
Add-Definition -FieldId 322 -FieldName 'nvmkin1' -Kind 'Location' -Label 'Enter the Nibel Reactor core' -X -6 -Y -912 -Z 191 -MinimumGameMoment 366 -MaximumGameMoment 366 -Priority 0 -RequiredPlayerTriangles $nibelReactorLowerTriangles -ScriptType 'Gateway'

# Inside the reactor, Sephiroth's Talk script advances 366 -> 367 and
# 368 -> 369. At 367 the native director accepts OK only on valve triangles
# 4 or 5. Keep the valve route active on arrival until that native action
# changes the moment to 368.
Add-Definition -FieldId 323 -FieldName 'nvmkin21' -Kind 'Model' -Label 'Talk to Sephiroth inside the reactor' -EntityId 8 -MinimumGameMoment 366 -MaximumGameMoment 366 -Priority 0 -EntityName 'cef' -ScriptType 'Talk'
Add-Definition -FieldId 323 -FieldName 'nvmkin21' -Kind 'Location' -Label 'Close the reactor valve' -X 128 -Y -235 -Z 186 -MinimumGameMoment 367 -MaximumGameMoment 367 -Priority 0 -EntityName 'cl' -ScriptType 'Native triangles 4 and 5' -KeepActiveOnArrival
Add-Definition -FieldId 323 -FieldName 'nvmkin21' -Kind 'Model' -Label 'Return to Sephiroth after closing the valve' -EntityId 8 -MinimumGameMoment 368 -MaximumGameMoment 368 -Priority 0 -EntityName 'cef' -ScriptType 'Talk'
Add-Definition -FieldId 323 -FieldName 'nvmkin21' -Kind 'Model' -Label 'Talk to Sephiroth and inspect the pod' -EntityId 8 -MinimumGameMoment 369 -MaximumGameMoment 369 -Priority 0 -EntityName 'cef' -ScriptType 'Talk'

# After the reactor pod scene, moment 370 returns control in Nibelheim. Cloud
# must enter Shinra Mansion, take the upper-right wing, descend the spiral
# stairs, and cross the basement corridor to Sephiroth's library. The left
# rooms are optional dead ends, so give either one an exact route back to the
# entrance hall instead of leaving Story empty. Control returns beside
# Sephiroth at moment 371; crossing the native library line advances the story
# to the overnight interlude. At moment 373 Cloud must traverse the mansion a
# second time, after which entering the far library room starts the long scene.
Add-Definition -FieldId 282 -FieldName 'nivl' -Kind 'Location' -Label 'Enter Shinra Mansion and find Sephiroth' -X -601 -Y 1358 -Z 202 -TargetGameMoment 371 -MinimumGameMoment 370 -MaximumGameMoment 370 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 297 -FieldName 'sinin1_1' -Kind 'Location' -Label 'Cross the upper hall to the right wing' -X 448 -Y 855 -Z 311 -TargetGameMoment 371 -MinimumGameMoment 370 -MaximumGameMoment 370 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 298 -FieldName 'sinin1_2' -Kind 'Location' -Label 'Return to the mansion entrance hall' -X -335 -Y 205 -Z 0 -TargetGameMoment 371 -MinimumGameMoment 370 -MaximumGameMoment 370 -Priority 0 -ScriptType 'Gateway recovery'
Add-Definition -FieldId 299 -FieldName 'sinin2_1' -Kind 'Location' -Label 'Leave the upstairs room and return to the entrance hall' -X -304 -Y 753 -Z 277 -TargetGameMoment 371 -MinimumGameMoment 370 -MaximumGameMoment 370 -Priority 0 -ScriptType 'Gateway recovery'
Add-Definition -FieldId 300 -FieldName 'sinin2_2' -Kind 'Location' -Label 'Descend through the right wing' -X 948 -Y 666 -Z 339 -TargetGameMoment 371 -MinimumGameMoment 370 -MaximumGameMoment 370 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 301 -FieldName 'sinin3' -Kind 'Location' -Label 'Continue down the spiral stairs' -X 4 -Y -125 -Z -610 -TargetGameMoment 371 -MinimumGameMoment 370 -MaximumGameMoment 370 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 302 -FieldName 'sininb1' -Kind 'Location' -Label 'Continue down to the mansion basement' -X -14 -Y -520 -Z 2 -TargetGameMoment 371 -MinimumGameMoment 370 -MaximumGameMoment 370 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 303 -FieldName 'sininb2' -Kind 'Location' -Label 'Enter the basement library corridor' -X -232 -Y -1104 -Z 0 -TargetGameMoment 371 -MinimumGameMoment 370 -MaximumGameMoment 370 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 304 -FieldName 'sininb31' -Kind 'Location' -Label 'Find Sephiroth in the mansion library' -X 17 -Y 88 -Z 0 -TargetGameMoment 371 -MinimumGameMoment 370 -MaximumGameMoment 370 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 304 -FieldName 'sininb31' -Kind 'Location' -Label 'Leave Sephiroth to his research' -EntityId 3 -X -435 -Y -98 -Z 0 -MinimumGameMoment 371 -MaximumGameMoment 371 -Priority 0 -EntityName 'ln0' -ScriptType 'Go 1x' -RequiredEnabledLineEntityId 3 -TriggerLine ([ordered]@{ startX = -463; startY = -135; startZ = 0; endX = -408; endY = -61; endZ = 0 })
Add-Definition -FieldId 299 -FieldName 'sinin2_1' -Kind 'Location' -Label 'Leave the upstairs room and return to the basement' -X -304 -Y 753 -Z 277 -MinimumGameMoment 373 -MaximumGameMoment 373 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 297 -FieldName 'sinin1_1' -Kind 'Location' -Label 'Cross the upper hall to the right wing' -X 448 -Y 855 -Z 311 -MinimumGameMoment 373 -MaximumGameMoment 373 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 300 -FieldName 'sinin2_2' -Kind 'Location' -Label 'Descend through the right wing' -X 948 -Y 666 -Z 339 -MinimumGameMoment 373 -MaximumGameMoment 373 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 301 -FieldName 'sinin3' -Kind 'Location' -Label 'Continue down the spiral stairs' -X 4 -Y -125 -Z -610 -MinimumGameMoment 373 -MaximumGameMoment 373 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 302 -FieldName 'sininb1' -Kind 'Location' -Label 'Continue down to the mansion basement' -X -14 -Y -520 -Z 2 -MinimumGameMoment 373 -MaximumGameMoment 373 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 303 -FieldName 'sininb2' -Kind 'Location' -Label 'Enter the basement library corridor' -X -232 -Y -1104 -Z 0 -MinimumGameMoment 373 -MaximumGameMoment 373 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 304 -FieldName 'sininb31' -Kind 'Location' -Label 'Enter the mansion library' -X 17 -Y 88 -Z 0 -MinimumGameMoment 374 -MaximumGameMoment 374 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 307 -FieldName 'sininb41' -Kind 'Location' -Label 'Confront Sephiroth in the far library room' -X 224 -Y 3255 -Z 0 -MinimumGameMoment 374 -MaximumGameMoment 374 -Priority 0 -ScriptType 'Gateway'

# Sephiroth restores control at moment 376 after walking out of the library.
# These gateways form the complete reverse route to the mansion entrance. The
# entrance's native Move script redirects this flashback state into the burning
# Nibelheim sequence. In the square, line1 starts Zangan's scene; Bank 3 byte
# 19 bit 7 then exposes Sephiroth at the only unblocked house. After the fire
# movie, the mtnvl6b gateway is the final controllable reactor approach.
Add-Definition -FieldId 307 -FieldName 'sininb41' -Kind 'Location' -Label 'Follow Sephiroth out of the library' -X 399 -Y 10 -Z 0 -MinimumGameMoment 376 -MaximumGameMoment 376 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 304 -FieldName 'sininb31' -Kind 'Location' -Label 'Continue out of the basement library' -X -454 -Y -88 -Z 0 -MinimumGameMoment 376 -MaximumGameMoment 376 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 303 -FieldName 'sininb2' -Kind 'Location' -Label 'Climb out of the mansion basement' -X 0 -Y -290 -Z 0 -MinimumGameMoment 376 -MaximumGameMoment 376 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 302 -FieldName 'sininb1' -Kind 'Location' -Label 'Climb the spiral stairs to the mansion' -X 12 -Y 877 -Z 226 -MinimumGameMoment 376 -MaximumGameMoment 376 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 301 -FieldName 'sinin3' -Kind 'Location' -Label 'Continue up through the right wing' -X 215 -Y -136 -Z 718 -MinimumGameMoment 376 -MaximumGameMoment 376 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 300 -FieldName 'sinin2_2' -Kind 'Location' -Label 'Return to the mansion entrance hall' -X 316 -Y 746 -Z 277 -MinimumGameMoment 376 -MaximumGameMoment 376 -Priority 0 -ScriptType 'Gateway'
Add-Definition -FieldId 297 -FieldName 'sinin1_1' -Kind 'Location' -Label 'Leave the mansion and follow Sephiroth' -X 0 -Y -18 -Z 0 -MinimumGameMoment 376 -MaximumGameMoment 376 -Priority 0 -ScriptType 'Move'
Add-Definition -FieldId 290 -FieldName 'nivl_b1' -Kind 'Location' -Label 'Approach Zangan in the burning square' -EntityId 3 -X 196 -Y 746 -Z 51 -MinimumGameMoment 376 -MaximumGameMoment 376 -Priority 0 -CompletedCondition (New-Condition 3 19 0x80 0x80) -EntityName 'line1' -ScriptType 'Move' -RequiredEnabledLineEntityId 3 -TriggerLine ([ordered]@{ startX = 116; startY = 733; startZ = 51; endX = 275; endY = 759; endZ = 51 })
Add-Definition -FieldId 290 -FieldName 'nivl_b1' -Kind 'Model' -Label 'Enter the only unblocked house and follow Sephiroth' -EntityId 7 -MinimumGameMoment 376 -MaximumGameMoment 376 -Priority 0 -RequiredCondition (New-Condition 3 19 0x80 0x80) -EntityName 'cefirth' -ScriptType 'Talk'
Add-Definition -FieldId 316 -FieldName 'mtnvl6b' -Kind 'Location' -Label 'Enter the Nibel Reactor' -X -118 -Y 163 -Z 325 -MinimumGameMoment 376 -MaximumGameMoment 376 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = -166; startY = 165; startZ = 325; endX = -70; endY = 160; endZ = 325 })

# EV0 advances 380 -> 381. The lower forward gateway then reaches nvmkin21,
# where the reactor director stages the confrontation and exposes Tifa's native
# Talk model at moment 382. Talking to her advances to moment 383; the only
# forward gateway then follows Sephiroth into Jenova's chamber.
Add-Definition -FieldId 322 -FieldName 'nvmkin1' -Kind 'Location' -Label 'Climb down the ladder and follow Tifa' -EntityId 12 -X -124 -Y 520 -Z 1068 -MinimumGameMoment 380 -MaximumGameMoment 380 -Priority 0 -RequiredPlayerTriangles $nibelReactorUpperTriangles -EntityName 'ladu' -ScriptType 'Move' -RequiredEnabledLineEntityId 12 -TriggerLine ([ordered]@{ startX = -82; startY = 476; startZ = 1068; endX = -166; endY = 564; endZ = 1068 }) -KeepActiveOnArrival
Add-Definition -FieldId 322 -FieldName 'nvmkin1' -Kind 'Location' -Label 'Enter the reactor chamber after Tifa' -EntityId 14 -X 0 -Y -257 -Z 191 -MinimumGameMoment 380 -MaximumGameMoment 380 -Priority 0 -RequiredPlayerTriangles $nibelReactorLowerTriangles -EntityName 'ev0' -ScriptType 'Move' -RequiredEnabledLineEntityId 14 -TriggerLine ([ordered]@{ startX = 38; startY = -257; startZ = 191; endX = -38; endY = -257; endZ = 191 })
Add-Definition -FieldId 322 -FieldName 'nvmkin1' -Kind 'Location' -Label 'Follow Tifa deeper into the reactor' -X -6 -Y -912 -Z 191 -MinimumGameMoment 381 -MaximumGameMoment 381 -Priority 0 -RequiredPlayerTriangles $nibelReactorLowerTriangles -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 62; startY = -937; startZ = 191; endX = -74; endY = -887; endZ = 191 })
Add-Definition -FieldId 323 -FieldName 'nvmkin21' -Kind 'Model' -Label 'Talk to Tifa beside the reactor pods' -EntityId 7 -MinimumGameMoment 382 -MaximumGameMoment 382 -Priority 0 -EntityName 'ti2' -ScriptType 'Talk'
Add-Definition -FieldId 323 -FieldName 'nvmkin21' -Kind 'Location' -Label 'Follow Sephiroth into Jenova''s chamber' -X -4 -Y -1141 -Z 709 -MinimumGameMoment 383 -MaximumGameMoment 383 -Priority 0 -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 56; startY = -1141; startZ = 709; endX = -64; endY = -1141; endZ = 709 })

# Choco/Mog is an optional visual reward and must not block Story. The
# mandatory early-Ranch objective is buying the Chocobo Lure from Choco Billy;
# his purchase script sets Bank 3, byte 64, bit 6. The same fields are reused
# for later breeding visits, so this guidance is limited to moments 385-565.
Add-Definition -FieldId 343 -FieldName 'farm' -Kind 'Location' -Label 'Visit the stable to buy a Chocobo Lure (optional)' -X 911 -Y 1881 -Z 2 -MinimumGameMoment 385 -MaximumGameMoment 565 -Priority 1 -CompletedCondition (New-Condition 3 64 0x40 0x40) -ScriptType 'Gateway' -TriggerLine ([ordered]@{ startX = 850; startY = 1862; startZ = 1; endX = 972; endY = 1899; endZ = 2 })
Add-Definition -FieldId 345 -FieldName 'frcyo' -Kind 'Model' -Label 'Talk to Choco Billy about a Chocobo Lure (optional)' -EntityId 5 -MinimumGameMoment 385 -MaximumGameMoment 565 -Priority 1 -CompletedCondition (New-Condition 3 64 0x40 0x40) -EntityName 'choco' -ScriptType 'Talk'

# Junon's first mandatory visit is driven mainly by native gateways and local
# state rather than global GameMoment writes. These definitions follow the
# ujunon1 -> cargo-ship script chain. The battle and minigames deliberately
# receive no invented movement target, but every hand-controlled route into and
# out of them remains represented by its native gateway and persistent state.
Add-Definition -FieldId 428 -FieldName 'ujunon1' -Kind 'Location' -Label 'Go to the beach' -X 165 -Y -674 -Z -155 -TargetGameMoment 388 -MinimumGameMoment 385 -MaximumGameMoment 387 -Priority 0 -ScriptType 'Gateway 3' -TriggerLine ([ordered]@{ startX = 68; startY = -748; startZ = -170; endX = 262; endY = -599; endZ = -139 })
Add-Definition -FieldId 428 -FieldName 'ujunon1' -Kind 'Location' -Label 'Enter the house and rest' -X 599 -Y 152 -Z 0 -TargetGameMoment 394 -MinimumGameMoment 388 -MaximumGameMoment 393 -Priority 0 -ScriptType 'Gateway 0' -TriggerLine ([ordered]@{ startX = 611; startY = 120; startZ = 0; endX = 586; endY = 184; endZ = 0 })
Add-Definition -FieldId 428 -FieldName 'ujunon1' -Kind 'Location' -Label 'Approach Priscilla outside the house' -EntityId 8 -X -718 -Y 1041 -Z 179 -TargetGameMoment 400 -MinimumGameMoment 394 -MaximumGameMoment 399 -Priority 0 -RequiredCondition (New-Condition 1 129 0x20 0x20) -CompletedCondition (New-Condition 1 129 0x40 0x40) -EntityName 'prister' -ScriptType 'Move' -RequiredEnabledLineEntityId 8 -TriggerLine ([ordered]@{ startX = -720; startY = 996; startZ = 179; endX = -716; endY = 1086; endZ = 179 })
Add-Definition -FieldId 428 -FieldName 'ujunon1' -Kind 'Location' -Label 'Follow Priscilla back to the beach' -X 165 -Y -674 -Z -155 -TargetGameMoment 400 -MinimumGameMoment 394 -MaximumGameMoment 399 -Priority 0 -RequiredCondition (New-Condition 1 129 0x40 0x40) -ScriptType 'Gateway 3' -TriggerLine ([ordered]@{ startX = 68; startY = -748; startZ = -170; endX = 262; endY = -599; endZ = -139 })
Add-Definition -FieldId 429 -FieldName 'ujunon2' -Kind 'Model' -Label 'Talk to Priscilla at the beach' -EntityId 12 -TargetGameMoment 400 -MinimumGameMoment 394 -MaximumGameMoment 399 -Priority 0 -EntityName 'prisl' -ScriptType 'Talk'
Add-Definition -FieldId 429 -FieldName 'ujunon2' -Kind 'Model' -Label 'Talk to Priscilla to use the dolphin whistle' -EntityId 12 -TargetGameMoment 403 -MinimumGameMoment 400 -MaximumGameMoment 405 -Priority 0 -EntityName 'prisl' -ScriptType 'Talk'
Add-Definition -FieldId 430 -FieldName 'ujunon3' -Kind 'Location' -Label 'Stand at the dolphin launch point and press OK' -X -167 -Y -82 -Z -141 -TargetGameMoment 403 -MinimumGameMoment 400 -MaximumGameMoment 405 -Priority 0 -CompletedCondition (New-Condition 5 16 0xFF 1) -EntityName 'cloud' -ScriptType 'Native triangle 149' -KeepActiveOnArrival
Add-Definition -FieldId 430 -FieldName 'ujunon3' -Kind 'Location' -Label 'Climb the pole to Upper Junon' -EntityId 5 -X -363 -Y -373 -Z 985 -TargetGameMoment 403 -MinimumGameMoment 400 -MaximumGameMoment 405 -Priority 0 -RequiredCondition (New-Condition 5 16 0xFF 1) -EntityName 'rchpole' -ScriptType 'Move' -RequiredEnabledLineEntityId 5 -TriggerLine ([ordered]@{ startX = -362; startY = -344; startZ = 985; endX = -363; endY = -401; endZ = 985 })
Add-Definition -FieldId 385 -FieldName 'junair2' -Kind 'Location' -Label 'Leave the airport for Upper Junon' -X -4318 -Y -1242 -Z 3711 -TargetGameMoment 403 -MinimumGameMoment 400 -MaximumGameMoment 402 -Priority 0 -ScriptType 'Gateway 2' -TriggerLine ([ordered]@{ startX = -4087; startY = -888; startZ = 3711; endX = -4549; endY = -1596; endZ = 3711 })
# junair's lowered lift (Bank 1:226 bit 6 clear) locks the stair set that
# connects to Gateway 0. box0/Talk runs dir script 3; only after that script
# raises the lift and exchanges the native IDLCK sets can Cloud leave for the
# barracks. Keep the interaction active until its native state changes.
Add-Definition -FieldId 384 -FieldName 'junair' -Kind 'Model' -Label 'Use the airport lift controls' -EntityId 14 -TargetGameMoment 406 -MinimumGameMoment 403 -MaximumGameMoment 405 -Priority 0 -RequiredCondition (New-Condition 1 226 0x40 0) -EntityName 'box0' -ScriptType 'Talk' -KeepActiveOnArrival
Add-Definition -FieldId 384 -FieldName 'junair' -Kind 'Location' -Label 'Continue into the Junon barracks' -X 12496 -Y 14558 -Z 5139 -TargetGameMoment 406 -MinimumGameMoment 403 -MaximumGameMoment 405 -Priority 0 -RequiredCondition (New-Condition 1 226 0x40 0x40) -ScriptType 'Gateway 0' -TriggerLine ([ordered]@{ startX = 12480; startY = 14290; startZ = 5139; endX = 12512; endY = 14825; endZ = 5139 })
Add-Definition -FieldId 386 -FieldName 'junin1' -Kind 'Location' -Label 'Enter the locker room' -X -1537 -Y -654 -Z 767 -TargetGameMoment 406 -MinimumGameMoment 403 -MaximumGameMoment 405 -Priority 0 -ScriptType 'Gateway 1' -TriggerLine ([ordered]@{ startX = -1622; startY = -608; startZ = 767; endX = -1452; endY = -700; endZ = 767 })
Add-Definition -FieldId 387 -FieldName 'junin1a' -Kind 'Location' -Label 'Open the locker and change into a Shinra uniform' -EntityId 11 -X -1580 -Y -826 -Z 605 -TargetGameMoment 406 -MinimumGameMoment 403 -MaximumGameMoment 405 -Priority 0 -EntityName 'border1' -ScriptType 'Go' -RequiredEnabledLineEntityId 11 -TriggerLine ([ordered]@{ startX = -1602; startY = -847; startZ = 605; endX = -1559; endY = -804; endZ = 605 })
# junonr4/direct sets Bank 3 byte 235 bit 2 immediately before returning
# Cloud to junin1 after the first scripted demonstration. The live parade and
# locker-room send-off scene have not happened yet. The latter scene is
# not ready yet: junin1a/direct requires bit 4, which junonr2/taityo script 8
# sets only after the Upper Junon ceremony. Preserve the native chain through
# junin1 border4, movie-only field 359, and junonr1 border3's alley. Border4 sets
# Bank 3 byte 236 bit 4 as it starts movie 37, so that bit separates the two
# manually navigated legs without guessing from a temporary field value.
Add-Definition -FieldId 386 -FieldName 'junin1' -Kind 'Location' -Label 'Continue to the Upper Junon ceremony' -EntityId 9 -X -828 -Y -769 -Z 767 -TargetGameMoment 409 -MinimumGameMoment 406 -MaximumGameMoment 408 -Priority 0 -RequiredConditions @((New-Condition 3 235 0x04 0x04), (New-Condition 3 235 0x10 0), (New-Condition 3 236 0x10 0)) -EntityName 'border4' -ScriptType 'Move' -RequiredEnabledLineEntityId 9 -TriggerLine ([ordered]@{ startX = -756; startY = -766; startZ = 767; endX = -900; endY = -771; endZ = 767 })
# direct's late-squad branch additionally requires 235 bit 1 and opens triangle
# 220. The soldiers walk through border3 to field 363, not Gateway 2 toward
# the later dock route. border3 Move starts the soldiers if needed, then MAPJUMPs.
Add-Definition -FieldId 360 -FieldName 'junonr1' -Kind 'Location' -Label 'Follow the soldiers to the welcoming ceremony' -EntityId 10 -X 563 -Y 354 -Z 0 -TargetGameMoment 409 -MinimumGameMoment 406 -MaximumGameMoment 408 -Priority 0 -RequiredConditions @((New-Condition 3 235 0x04 0x04), (New-Condition 3 235 0x02 0x02), (New-Condition 3 235 0x10 0), (New-Condition 3 236 0x10 0x10)) -EntityName 'border3' -ScriptType 'Move' -RequiredEnabledLineEntityId 10 -TriggerLine ([ordered]@{ startX = 634; startY = 347; startZ = 0; endX = 492; endY = 361; endZ = 0 })
Add-Definition -FieldId 387 -FieldName 'junin1a' -Kind 'Model' -Label 'Talk to the captain and say you are ready for the parade' -EntityId 18 -TargetGameMoment 409 -MinimumGameMoment 406 -MaximumGameMoment 408 -Priority 0 -RequiredConditions @((New-Condition 5 17 0xFF 1), (New-Condition 3 235 0x10 0)) -CompletedCondition (New-Condition 3 235 0x10 0x10) -EntityName 'taityo' -ScriptType 'Talk'
# taityo/Talk's ready choice enables border2 script 7, then hides the
# captain and unlocks triangle 33. The parade-complete bit is still clear:
# following him out through this LINE is the missing prerequisite to it.
# Do not infer readiness from a transient ASK result or a hidden model.
Add-Definition -FieldId 387 -FieldName 'junin1a' -Kind 'Location' -Label 'Follow the captain out to the parade' -EntityId 12 -X -1335 -Y -648 -Z 605 -TargetGameMoment 409 -MinimumGameMoment 406 -MaximumGameMoment 408 -Priority 0 -RequiredCondition (New-Condition 3 235 0x10 0) -EntityName 'border2' -ScriptType 'Go' -RequiredEnabledLineEntityId 12 -TriggerLine ([ordered]@{ startX = -1335; startY = -606; startZ = 605; endX = -1335; endY = -690; endZ = 605 })
# The button drill's AD4 script only unlocks triangle 33 and sets Bank 3:236
# bit 0. border2 remains disabled on this visit, so leave through the ordinary
# Gateway 0 to junin1 instead of the earlier parade-only shortcut to junonr1.
Add-Definition -FieldId 387 -FieldName 'junin1a' -Kind 'Location' -Label 'Leave the locker room for the send-off' -X -1271 -Y -652 -Z 605 -TargetGameMoment 409 -MinimumGameMoment 406 -MaximumGameMoment 408 -Priority 0 -RequiredConditions @((New-Condition 3 235 0x10 0x10), (New-Condition 3 236 0x01 0x01)) -ScriptType 'Gateway 0' -TriggerLine ([ordered]@{ startX = -1269; startY = -688; startZ = 605; endX = -1272; endY = -615; endZ = 605 })
# junin1/direct sends the soldiers toward Gateway 0 after that practice flag.
# Its first-arrival bit 1 is unrelated to whether Cloud may follow them.
Add-Definition -FieldId 386 -FieldName 'junin1' -Kind 'Location' -Label 'Continue toward the dock' -X -828 -Y -818 -Z 767 -TargetGameMoment 409 -MinimumGameMoment 406 -MaximumGameMoment 408 -Priority 0 -RequiredConditions @((New-Condition 3 235 0x10 0x10), (New-Condition 3 236 0x01 0x01)) -ScriptType 'Gateway 0' -TriggerLine ([ordered]@{ startX = -896; startY = -818; startZ = 767; endX = -760; endY = -818; endZ = 767 })
Add-Definition -FieldId 360 -FieldName 'junonr1' -Kind 'Location' -Label 'Follow the road toward the dock' -X -2237 -Y -499 -Z 0 -TargetGameMoment 409 -MinimumGameMoment 406 -MaximumGameMoment 408 -Priority 0 -RequiredConditions @((New-Condition 3 235 0x10 0x10), (New-Condition 3 236 0x01 0x01)) -ScriptType 'Gateway 2' -TriggerLine ([ordered]@{ startX = -2088; startY = -100; startZ = 0; endX = -2385; endY = -897; endZ = 0 })
Add-Definition -FieldId 361 -FieldName 'junonr2' -Kind 'Location' -Label 'Follow the road toward the dock' -X 4356 -Y -4953 -Z -3407 -TargetGameMoment 409 -MinimumGameMoment 406 -MaximumGameMoment 408 -Priority 0 -RequiredConditions @((New-Condition 3 235 0x10 0x10), (New-Condition 3 236 0x01 0x01)) -ScriptType 'Gateway 1' -TriggerLine ([ordered]@{ startX = 4587; startY = -5157; startZ = -3407; endX = 4124; endY = -4748; endZ = -3407 })
Add-Definition -FieldId 390 -FieldName 'junin3' -Kind 'Location' -Label 'Continue toward the dock' -X -1665 -Y 3796 -Z 1219 -TargetGameMoment 409 -MinimumGameMoment 406 -MaximumGameMoment 408 -Priority 0 -RequiredConditions @((New-Condition 3 235 0x10 0x10), (New-Condition 3 236 0x01 0x01)) -ScriptType 'Gateway 0' -TriggerLine ([ordered]@{ startX = -1619; startY = 3692; startZ = 1219; endX = -1711; endY = 3900; endZ = 1219 })
Add-Definition -FieldId 371 -FieldName 'junonl2' -Kind 'Location' -Label 'Continue toward the dock' -X 1910 -Y -763 -Z -6 -TargetGameMoment 409 -MinimumGameMoment 406 -MaximumGameMoment 408 -Priority 0 -RequiredConditions @((New-Condition 3 235 0x10 0x10), (New-Condition 3 236 0x01 0x01)) -ScriptType 'Gateway 0' -TriggerLine ([ordered]@{ startX = 1993; startY = 8; startZ = -6; endX = 1827; endY = -1533; endZ = -6 })
Add-Definition -FieldId 370 -FieldName 'junonl1' -Kind 'Location' -Label 'Enter the dock for the send-off' -EntityId 10 -X -7973 -Y -760 -Z 0 -TargetGameMoment 409 -MinimumGameMoment 406 -MaximumGameMoment 408 -Priority 0 -RequiredConditions @((New-Condition 3 235 0x10 0x10), (New-Condition 3 236 0x01 0x01)) -EntityName 'border1' -ScriptType 'Move' -RequiredEnabledLineEntityId 10 -TriggerLine ([ordered]@{ startX = -7973; startY = -13; startZ = 0; endX = -7973; endY = -1507; endZ = 0 })
Add-Definition -FieldId 382 -FieldName 'jundoc1a' -Kind 'Location' -Label 'Board the cargo ship' -EntityId 11 -X -235 -Y 436 -Z 11 -TargetGameMoment 415 -MinimumGameMoment 409 -MaximumGameMoment 414 -Priority 0 -EntityName 'border2' -ScriptType 'Move' -RequiredEnabledLineEntityId 11 -TriggerLine ([ordered]@{ startX = -494; startY = 425; startZ = 11; endX = 25; endY = 447; endZ = 11 })
# The foredeck guard's Init blocks triangle 102 until bank 3[184] bit 7.
# shpin_2/EARITH2 (Aeris in disguise, entity 11) Talk 4..64 counts bits
# 0..6; at >=3, dialogue 18 asks about Barret and byte 93 sets bit 7.
# Aeris/Tifa answers set alternative bits; never require a specific answer.
# Yuffie's optional conversation also counts, but is not a prerequisite.
# Crossing back into ship_1 re-runs the guard's Init and removes its lock.
$shipNotUnlocked = New-Condition 3 184 0x80 0
$shipNeedsConversations = New-Condition 3 184 0x7F 0 -MaximumSetBits 2
$shipReadyForAeris = New-Condition 3 184 0x7F 0 -MinimumSetBits 3
Add-Definition -FieldId 439 -FieldName 'shpin_2' -Kind 'Model' -Label 'Talk to Aeris' -EntityId 11 -TargetGameMoment 415 -MinimumGameMoment 409 -MaximumGameMoment 414 -Priority 0 -RequiredConditions @($shipNotUnlocked, $shipNeedsConversations, (New-Condition 3 184 0x03 0)) -EntityName 'EARITH2' -ScriptType 'Talk'
Add-Definition -FieldId 436 -FieldName 'ship_1' -Kind 'Model' -Label 'Talk to Tifa' -EntityId 11 -TargetGameMoment 415 -MinimumGameMoment 409 -MaximumGameMoment 414 -Priority 0 -RequiredConditions @($shipNotUnlocked, $shipNeedsConversations, (New-Condition 3 184 0x0C 0)) -EntityName 'TIFA2' -ScriptType 'Talk'
Add-Definition -FieldId 436 -FieldName 'ship_1' -Kind 'Model' -Label 'Talk to Red XIII' -EntityId 14 -TargetGameMoment 415 -MinimumGameMoment 409 -MaximumGameMoment 414 -Priority 0 -RequiredConditions @($shipNotUnlocked, $shipNeedsConversations, (New-Condition 3 184 0x40 0)) -EntityName 'RED2' -ScriptType 'Talk'
Add-Definition -FieldId 439 -FieldName 'shpin_2' -Kind 'Location' -Label 'Go on deck to speak with the others' -X 523 -Y -434 -Z 739 -TargetGameMoment 415 -MinimumGameMoment 409 -MaximumGameMoment 414 -Priority 1 -RequiredConditions @($shipNotUnlocked, $shipNeedsConversations) -ScriptType 'Gateway 0' -TriggerLine ([ordered]@{ startX = 568; startY = -428; startZ = 733; endX = 478; endY = -441; endZ = 745 })
Add-Definition -FieldId 436 -FieldName 'ship_1' -Kind 'Location' -Label 'Return inside to speak with Aeris' -X -409 -Y 956 -Z -24643 -TargetGameMoment 415 -MinimumGameMoment 409 -MaximumGameMoment 414 -Priority 1 -RequiredCondition $shipNotUnlocked -ScriptType 'Gateway 0' -TriggerLine ([ordered]@{ startX = -453; startY = 956; startZ = -24642; endX = -365; endY = 956; endZ = -24644 })
Add-Definition -FieldId 439 -FieldName 'shpin_2' -Kind 'Model' -Label 'Ask Aeris about Barret' -EntityId 11 -TargetGameMoment 415 -MinimumGameMoment 409 -MaximumGameMoment 414 -Priority 0 -RequiredConditions @($shipNotUnlocked, $shipReadyForAeris) -EntityName 'EARITH2' -ScriptType 'Talk'
Add-Definition -FieldId 439 -FieldName 'shpin_2' -Kind 'Location' -Label 'Go on deck to find Barret' -X 523 -Y -434 -Z 739 -TargetGameMoment 415 -MinimumGameMoment 409 -MaximumGameMoment 414 -Priority 0 -RequiredCondition (New-Condition 3 184 0x80 0x80) -ScriptType 'Gateway 0' -TriggerLine ([ordered]@{ startX = 568; startY = -428; startZ = 733; endX = 478; endY = -441; endZ = 745 })
Add-Definition -FieldId 436 -FieldName 'ship_1' -Kind 'Location' -Label 'Go to the cargo ship deck' -EntityId 21 -X 260 -Y -1479 -Z -24604 -TargetGameMoment 415 -MinimumGameMoment 409 -MaximumGameMoment 414 -Priority 0 -RequiredCondition (New-Condition 3 184 0x80 0x80) -EntityName 'LINEJ' -ScriptType 'Go 1x' -RequiredEnabledLineEntityId 21 -TriggerLine ([ordered]@{ startX = 209; startY = -1468; startZ = -24604; endX = 311; endY = -1490; endZ = -24604 })

# The alarm leaves moment 415 unchanged through the engine-room sequence.
# ship_2/LINEJ leads back to ship_1, whose gateway 0 returns to shpin_2;
# HEI Init now moves off triangle 70, opening gateway 1 to shpin_3.
# ELINE disables itself before the boss and enables LINEJ only afterwards.
# Keep those two manual triggers separate; the intervening scene is automatic.
Add-Definition -FieldId 437 -FieldName 'ship_2' -Kind 'Location' -Label 'Return to the main deck after the alarm' -EntityId 6 -X 344 -Y -388 -Z 0 -MinimumGameMoment 415 -MaximumGameMoment 415 -Priority 0 -EntityName 'LINEJ' -ScriptType 'Go 1x' -RequiredEnabledLineEntityId 6 -TriggerLine ([ordered]@{ startX = 141; startY = -82; startZ = 0; endX = 546; endY = -694; endZ = 0 })
Add-Definition -FieldId 436 -FieldName 'ship_1' -Kind 'Location' -Label 'Go below deck after the alarm' -X -409 -Y 956 -Z -24643 -MinimumGameMoment 415 -MaximumGameMoment 415 -Priority 0 -ScriptType 'Gateway 0' -TriggerLine ([ordered]@{ startX = -453; startY = 956; startZ = -24642; endX = -365; endY = 956; endZ = -24644 })
Add-Definition -FieldId 439 -FieldName 'shpin_2' -Kind 'Location' -Label 'Enter the engine room' -X 244 -Y 866 -Z 51 -MinimumGameMoment 415 -MaximumGameMoment 415 -Priority 0 -ScriptType 'Gateway 1' -TriggerLine ([ordered]@{ startX = 180; startY = 797; startZ = 51; endX = 308; endY = 935; endZ = 51 })
Add-Definition -FieldId 440 -FieldName 'shpin_3' -Kind 'Location' -Label 'Investigate the engine room' -EntityId 15 -X 1 -Y -34 -Z 1 -MinimumGameMoment 415 -MaximumGameMoment 415 -Priority 0 -EntityName 'ELINE' -ScriptType 'Go 1x' -RequiredEnabledLineEntityId 15 -TriggerLine ([ordered]@{ startX = -75; startY = -32; startZ = 1; endX = 76; endY = -35; endZ = 1 })
Add-Definition -FieldId 440 -FieldName 'shpin_3' -Kind 'Location' -Label 'Leave the engine room for docking' -EntityId 18 -X -1 -Y -669 -Z 105 -MinimumGameMoment 415 -MaximumGameMoment 415 -Priority 0 -EntityName 'LINEJ' -ScriptType 'Move' -RequiredEnabledLineEntityId 18 -TriggerLine ([ordered]@{ startX = -55; startY = -670; startZ = 106; endX = 54; endY = -668; endZ = 104 })

# These Shinra Building objectives have unambiguous native model or LINE
# targets. Ambiguous alternate guard lines and model-slot triangle puzzles are
# intentionally omitted until they can be represented without false targets.
Add-Definition -FieldId 241 -FieldName 'blin61' -Kind 'Model' -Label 'Talk to the employee at the floor 61 desk' -EntityId 12 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -CompletedCondition (New-Condition 1 59 0x01 0x01) -EntityName 'ZAKOA' -ScriptType 'Talk'
Add-Definition -FieldId 242 -FieldName 'blin62_1' -Kind 'Model' -Label 'Ask Mayor Domino for the password challenge' -EntityId 26 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -CompletedCondition (New-Condition 3 179 0x08 0x08) -EntityName 'DOMINO' -ScriptType 'Talk'
Add-Definition -FieldId 242 -FieldName 'blin62_1' -Kind 'Model' -Label "Answer Mayor Domino's password" -EntityId 26 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 0 -RequiredCondition (New-Condition 3 179 0x08 0x08) -CompletedCondition (New-Condition 3 179 0x10 0x10) -EntityName 'DOMINO' -ScriptType 'Talk'

# Floor 63 is optional until the player activates its native computer, which
# sets Bank 3 byte 177 bit 4. Once activated, keep the Story category on the
# exact three-door, coupon, and duct state machine. Priority -1 temporarily
# hides the normal stairs objective so closest-target selection cannot abandon
# the active puzzle. Coupon exchange sets Bank 3 byte 181 bit 7 and returns the
# floor to its normal Story route. Bank 3 byte 172 is only a transient duct
# entry selector: blin63_t consumes and clears bits 5, 6, and 7 on load. Never
# use those bits as persistent arrival or completion state.
Add-Definition -FieldId 245 -FieldName 'blin63_1' -Kind 'Location' -Label 'Open coupon route door 1 of 3' -X 414 -Y 972 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 1 -RequiredCondition (New-Condition 3 177 0x10 0x10) -CompletedCondition (New-Condition 3 174 0x02 0x02) -EntityName 'D2' -ScriptType '[OK]' -KeepActiveOnArrival
Add-Definition -FieldId 245 -FieldName 'blin63_1' -Kind 'Location' -Label 'Open coupon route door 2 of 3' -X -549 -Y 752 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 1 -RequiredCondition (New-Condition 3 174 0x02 0x02) -CompletedCondition (New-Condition 3 174 0x08 0x08) -EntityName 'D4' -ScriptType '[OK]' -KeepActiveOnArrival
Add-Definition -FieldId 245 -FieldName 'blin63_1' -Kind 'Model' -Label 'Collect the A Coupon' -EntityId 42 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 1 -RequiredCondition (New-Condition 3 174 0x08 0x08) -CompletedCondition (New-Condition 3 177 0x02 0x02) -EntityName 'TAKARA1' -ScriptType 'Talk'
Add-Definition -FieldId 245 -FieldName 'blin63_1' -Kind 'Location' -Label 'Enter the A Coupon room duct' -EntityId 46 -X -864 -Y 119 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 1 -RequiredCondition (New-Condition 3 177 0x02 0x02) -CompletedCondition (New-Condition 3 177 0x08 0x08) -EntityName 'DUCTLB' -ScriptType 'Move' -KeepActiveOnArrival
Add-Definition -FieldId 246 -FieldName 'blin63_t' -Kind 'Location' -Label 'Crawl to the shaft for the B Coupon room' -X 384 -Y 123 -Z 369 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority -1 -RequiredCondition (New-Condition 3 177 0x02 0x02) -CompletedCondition (New-Condition 3 177 0x08 0x08) -EntityName 'CLOUD' -ScriptType 'LADER' -KeepActiveOnArrival
Add-Definition -FieldId 245 -FieldName 'blin63_1' -Kind 'Model' -Label 'Collect the B Coupon' -EntityId 44 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 1 -RequiredCondition (New-Condition 3 177 0x02 0x02) -CompletedCondition (New-Condition 3 177 0x08 0x08) -EntityName 'TAKARA3' -ScriptType 'Talk'
Add-Definition -FieldId 245 -FieldName 'blin63_1' -Kind 'Location' -Label 'Open coupon route door 3 of 3' -X -148 -Y -356 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 1 -RequiredCondition (New-Condition 3 177 0x08 0x08) -CompletedCondition (New-Condition 3 175 0x80 0x80) -EntityName 'D16' -ScriptType '[OK]' -KeepActiveOnArrival
Add-Definition -FieldId 245 -FieldName 'blin63_1' -Kind 'Model' -Label 'Collect the C Coupon' -EntityId 43 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 1 -RequiredCondition (New-Condition 3 175 0x80 0x80) -CompletedCondition (New-Condition 3 177 0x04 0x04) -EntityName 'TAKARA2' -ScriptType 'Talk'
Add-Definition -FieldId 245 -FieldName 'blin63_1' -Kind 'Location' -Label 'Enter the B Coupon room duct' -EntityId 47 -X 340 -Y 100 -Z 0 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 1 -RequiredCondition (New-Condition 3 177 0x0E 0x0E) -CompletedCondition (New-Condition 3 181 0x80 0x80) -EntityName 'DUCTLC' -ScriptType 'Move' -KeepActiveOnArrival
Add-Definition -FieldId 246 -FieldName 'blin63_t' -Kind 'Location' -Label 'Crawl to the floor 63 computer shaft' -X 644 -Y -501 -Z 369 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority -1 -RequiredCondition (New-Condition 3 177 0x0E 0x0E) -CompletedCondition (New-Condition 3 181 0x80 0x80) -EntityName 'CLOUD' -ScriptType 'LADER' -KeepActiveOnArrival
Add-Definition -FieldId 245 -FieldName 'blin63_1' -Kind 'Model' -Label 'Exchange the Floor 63 coupons at the computer' -EntityId 15 -TargetGameMoment 264 -MinimumGameMoment 263 -MaximumGameMoment 263 -Priority 1 -RequiredCondition (New-Condition 3 177 0x0E 0x0E) -CompletedCondition (New-Condition 3 181 0x80 0x80) -EntityName 'MLINE' -ScriptType '[OK]'

Add-Definition -FieldId 250 -FieldName 'blin66_1' -Kind 'Location' -Label 'Follow Hojo out of the meeting' -X -346 -Y -1024 -Z 0 -TargetGameMoment 270 -MinimumGameMoment 269 -MaximumGameMoment 269 -Priority 0 -EntityName 'hw' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = -346; startY = -768; startZ = 0; endX = -346; endY = -1280; endZ = 0 })
Add-Definition -FieldId 250 -FieldName 'blin66_1' -Kind 'Location' -Label 'Follow Hojo toward the 67th floor' -X 548 -Y -1044 -Z 0 -TargetGameMoment 271 -MinimumGameMoment 270 -MaximumGameMoment 270 -Priority 0 -EntityName 'lookh' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 409; startY = -809; startZ = 0; endX = 687; endY = -1280; endZ = 0 })
Add-Definition -FieldId 250 -FieldName 'blin66_1' -Kind 'Location' -Label 'Take the stairs to the 67th floor' -X 1051 -Y 209 -Z 196 -TargetGameMoment 272 -MinimumGameMoment 271 -MaximumGameMoment 271 -Priority 0 -EntityName 'st0' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = 1051; startY = 237; startZ = 197; endX = 1050; endY = 181; endZ = 195 })
Add-Definition -FieldId 256 -FieldName 'blin67_1' -Kind 'Location' -Label 'Follow Hojo across the 67th floor' -X 153 -Y -1060 -Z 0 -TargetGameMoment 273 -MinimumGameMoment 272 -MaximumGameMoment 272 -Priority 0 -EntityName 'ln0' -ScriptType 'Move'
Add-Definition -FieldId 259 -FieldName 'blin67_3' -Kind 'Location' -Label 'Approach Hojo and the Jenova chamber' -X -786 -Y -530 -Z 0 -TargetGameMoment 278 -MinimumGameMoment 273 -MaximumGameMoment 273 -Priority 0 -EntityName 'ln0' -ScriptType 'Move' -TriggerLine ([ordered]@{ startX = -861; startY = -567; startZ = 0; endX = -711; endY = -493; endZ = 0 })
Add-Definition -FieldId 256 -FieldName 'blin67_1' -Kind 'Location' -Label 'Continue after Jenova; follow Hojo to the lab' -X 153 -Y -1060 -Z 0 -TargetGameMoment 280 -MinimumGameMoment 278 -MaximumGameMoment 278 -Priority 0 -EntityName 'ln0' -ScriptType 'Move'
Add-Definition -FieldId 262 -FieldName 'blin68_1' -Kind 'Model' -Label 'Talk to the lab assistant for Keycard 68' -EntityId 7 -MinimumGameMoment 284 -MaximumGameMoment 284 -Priority 0 -CompletedCondition (New-Condition 1 226 0x20 0x20) -EntityName 'ZAKOA' -ScriptType 'Talk'
Add-Definition -FieldId 233 -FieldName 'eleout' -Kind 'Location' -Label 'Enter the 66th-floor elevator and press OK' -X -2 -Y 36 -Z -1 -TargetGameMoment 290 -MinimumGameMoment 284 -MaximumGameMoment 284 -Priority 0 -EntityName 'lin0' -ScriptType '[OK]' -TriggerLine ([ordered]@{ startX = -77; startY = 0; startZ = -1; endX = 73; endY = 73; endZ = -1 })
Add-Definition -FieldId 258 -FieldName 'blin67_2' -Kind 'Location' -Label 'Use the cell door' -X 884 -Y 512 -Z 0 -TargetGameMoment 296 -MinimumGameMoment 293 -MaximumGameMoment 293 -Priority 0 -EntityName 'ln0' -ScriptType '[OK]' -TriggerLine ([ordered]@{ startX = 813; startY = 512; startZ = 0; endX = 954; endY = 512; endZ = 0 })

# Reviewed regional routes supplement the automatically extracted milestones.
# They use installed trigger geometry and native state gates, including stretches
# that advance no GameMoment and therefore cannot be found by write extraction.
$genericDefinitionCount = $definitions.Count
foreach ($region in @('CostaDelSol', 'MountCorel', 'NorthCorel', 'GoldSaucer', 'CorelPrison', 'CosmoCanyon', 'IcicleInn', 'GreatGlacier', 'GaeasCliff', 'MountNibel', 'Nibelheim', 'RocketTown', 'BoneVillage', 'GoldSaucerReturn', 'JunonEscape', 'Mideel', 'TempleOfTheAncients', 'CityOfTheAncients', 'WhirlwindMaze', 'JunonEscapeAndHighwind', 'MideelAndHugeMateria', 'UnderwaterReactor', 'RocketReturnAndAncients', 'MidgarRaid', 'HighwindEndgame', 'NorthernCrater', 'EarlyContinuity', 'OptionalRoomReturns', 'WhirlwindTransit', 'SubmarineBoarding', 'InFieldTravel', 'ReviewedArrivals', 'NativeEntryDoors', 'TransitContinuity', 'Wutai')) {
    . (Join-Path $scriptRoot "story-regions\$region.ps1")
}

. (Join-Path $scriptRoot 'story-regions\WeaponSeller.ps1')
. (Join-Path $scriptRoot 'story-regions\ReviewedStoryTransit.ps1')

# What Add-CuratedFields is for.
#
# Milestone extraction writes a row for every GameMoment write it can place, and in a
# field a region file has since reviewed, that row is the earlier, coarser answer to a
# question the region now answers properly. The Nibelheim illusion is the plain case:
# extraction produced one "Talk to Tifa to continue" for nivl_b22 with no band and no
# conditions, while the field actually makes her say nothing at all until Sephiroth has
# been spoken to. Both rows offered at once is not two ways to the same place; it is the
# wrong one always being offered.
#
# So a generic row is dropped when a reviewed field has produced a row for the same
# native trigger: the same entity, kind, script name, script type and place. Matching on
# less than that is not precise enough - most Location rows carry no entity id and no
# script name at all, so Costa del Sol's reviewed bar door would otherwise stand for every
# other gateway extraction found in the same room. Anything the regions did not cover is left exactly as it was:
# a field being reviewed in one place does not make extraction wrong everywhere else in
# it.
# A row is only replaced by a row that could be offered instead of it. The same native
# trigger often serves the story twice: Junon's airport lift is box0's Talk on the first
# visit at 403 and again during the escape at 1016, and those are two different rows for
# two different chapters. An absent band means the reader will offer that row at any
# moment, which is exactly the case that has to be replaced.
function Test-MomentWindowsOverlap($left, $right) {
    $leftMinimum = [int]$left['minimumGameMoment']
    $leftMaximum = [int]$left['maximumGameMoment']
    $rightMinimum = [int]$right['minimumGameMoment']
    $rightMaximum = [int]$right['maximumGameMoment']
    if ($leftMinimum -lt 0) { $leftMinimum = [int]::MinValue }
    if ($leftMaximum -lt 0) { $leftMaximum = [int]::MaxValue }
    if ($rightMinimum -lt 0) { $rightMinimum = [int]::MinValue }
    if ($rightMaximum -lt 0) { $rightMaximum = [int]::MaxValue }
    return $leftMinimum -le $rightMaximum -and $rightMinimum -le $leftMaximum
}

$reviewedByTrigger = @{}
for ($index = $genericDefinitionCount; $index -lt $definitions.Count; $index++) {
    $reviewed = $definitions[$index]
    if (-not $curatedFields.Contains([int]$reviewed.fieldId)) { continue }
    $key = "$($reviewed.fieldId):$($reviewed.kind):$($reviewed.entityId):$($reviewed.sourceEntityName):$($reviewed.sourceScriptType):$($reviewed.x):$($reviewed.y):$($reviewed.z)"
    if (-not $reviewedByTrigger.ContainsKey($key)) {
        $reviewedByTrigger[$key] = [Collections.Generic.List[object]]::new()
    }
    $reviewedByTrigger[$key].Add($reviewed)
}

$supersededCount = 0
for ($index = $genericDefinitionCount - 1; $index -ge 0; $index--) {
    $generic = $definitions[$index]
    $key = "$($generic.fieldId):$($generic.kind):$($generic.entityId):$($generic.sourceEntityName):$($generic.sourceScriptType):$($generic.x):$($generic.y):$($generic.z)"
    if (-not $reviewedByTrigger.ContainsKey($key)) { continue }
    $overlapping = @($reviewedByTrigger[$key] | Where-Object { Test-MomentWindowsOverlap $generic $_ })
    if ($overlapping.Count -gt 0) {
        $definitions.RemoveAt($index)
        $supersededCount++
    }
}

Write-Host "Reviewed regions superseded $supersededCount extracted row(s) in $($curatedFields.Count) curated field(s)."

$suppressedCount = 0
for ($index = $definitions.Count - 1; $index -ge 0; $index--) {
    $candidate = $definitions[$index]
    foreach ($suppression in $suppressedTriggers) {
        if ([int]$candidate.fieldId -ne $suppression.FieldId) { continue }
        if ([string]$candidate.sourceEntityName -ne $suppression.EntityName) { continue }
        if ([string]$candidate.sourceScriptType -ne $suppression.ScriptType) { continue }
        $window = [ordered]@{
            minimumGameMoment = $suppression.MinimumGameMoment
            maximumGameMoment = $suppression.MaximumGameMoment
        }
        if (-not (Test-MomentWindowsOverlap $candidate $window)) { continue }
        $definitions.RemoveAt($index)
        $suppressedCount++
        break
    }
}

Write-Host "Suppressed $suppressedCount extracted row(s) the field runs by itself."

# Two rows are the same row when they say the same thing, in the same place, at the
# same time, under the same conditions. The moment band and the conditions were missing
# from this, and they are exactly what distinguishes a door offered on the way up from
# the same door offered on the way back - Cosmo Canyon's inn is walked through twice
# under different local state, and one of the two was being thrown away here without a
# word. A dropped row is a silent hole in a chapter, so the key has to carry everything
# the reader will later branch on.
function Get-ConditionKey($condition) {
    if ($null -eq $condition) { return '' }
    return "$($condition.bank).$($condition.address).$($condition.mask).$($condition.value)." +
           "$($condition.anyBitSet).$($condition.minimumSetBits).$($condition.maximumSetBits)." +
           "$($condition.minimumValue).$($condition.maximumValue)." +
           "$($condition.partyMemberId).$($condition.requirePartyMember)"
}

# A definition is an ordered dictionary while it is being built and a parsed object
# once it has been round-tripped through JSON, and those two answer "have you got this
# key" in different ways. Asking both is the only way to read a row's conditions in
# either form - and reading them wrongly here silently throws rows away.
function Get-DefinitionField($definition, [string] $name) {
    if ($definition -is [System.Collections.IDictionary]) {
        return $(if ($definition.Contains($name)) { $definition[$name] } else { $null })
    }
    return $(if ($definition.PSObject.Properties[$name]) { $definition.$name } else { $null })
}

function Get-DefinitionKey($definition) {
    $conditions = @()
    $conditions += Get-ConditionKey (Get-DefinitionField $definition 'requiredCondition')
    foreach ($condition in @(Get-DefinitionField $definition 'requiredConditions')) {
        $conditions += Get-ConditionKey $condition
    }
    $conditions += Get-ConditionKey (Get-DefinitionField $definition 'completedCondition')
    $triangles = @(Get-DefinitionField $definition 'requiredPlayerTriangles') -join ','
    return "$($definition.fieldId):$($definition.kind):$($definition.entityId):$($definition.x):$($definition.y):$($definition.z):" +
           "$($definition.targetGameMoment):$($definition.minimumGameMoment):$($definition.maximumGameMoment):" +
           "$($definition.label):$($definition.priority):$($conditions -join '|'):$triangles"
}

$filteredDefinitions = @($definitions |
    Where-Object {
        -not ($_.fieldId -eq 144 -and
              $_.targetGameMoment -eq 212 -and
              $_.label -eq 'Continue Sewers and Train Graveyard') -and
        -not ($_.fieldId -eq 115 -and
              $_.entityId -eq 4 -and
              $_.targetGameMoment -eq 1180 -and
              $_.label -eq 'Talk to the story character')
    })

# Fields that could not be read this run keep whatever the last generated catalog
# already held for them, so an offline placeholder costs no coverage. If there is no
# previous catalog to carry from, the loss is real and is reported as such.
$previousDefinitions = @()
if ($unreadableFields.Count -gt 0 -and (Test-Path -LiteralPath $OutputPath)) {
    # A corrupt previous catalog must fail regeneration, not silently discard coverage.
    $previousDocument = Get-Content -LiteralPath $OutputPath -Raw | ConvertFrom-Json
    $previousDefinitions = @($previousDocument.definitions)
}
. (Join-Path $scriptRoot 'Complete-StoryCatalog.ps1')
$finalized = Complete-StoryCatalog -Definitions $filteredDefinitions `
    -PreviousDefinitions $previousDefinitions -UnreadableFields $unreadableFields.ToArray() `
    -GetKey { param($row) Get-DefinitionKey $row }
$deduplicated = @($finalized.Definitions)
$carriedForward = $finalized.CarriedForward

# Keep established J/L cycling order stable when the catalog is regenerated.
# Existing definitions retain their prior positions; newly reviewed objectives
# are appended in generator order. This prevents a focused addition from
# silently reordering every unrelated field's targets.
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
        Write-Warning "Could not preserve existing Story target order: $($_.Exception.Message)"
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

if ($SourceCommit) {
    $sourceCommit = $SourceCommit.Trim()
}
else {
    $sourceCommit = (git -C $KujataDataRoot rev-parse HEAD).Trim()
}
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
if (-not $LedgerPath) {
    $LedgerPath = [IO.Path]::ChangeExtension($OutputPath, '.coverage.json')
}
$ledger = [ordered]@{
    schemaVersion = 1
    evidenceLevel = 'Extraction inventory, not a playthrough or proof of route coverage. Automatic and unresolved writes remain unclassified.'
    sourceCommit = $sourceCommit
    definitions = $deduplicated.Count
    scannedGameMomentWrites = $milestoneCount
    navigableGameMomentWrites = $navigableMilestoneCount
    automaticOrUnresolved = @($unresolved.ToArray())
    unreadableFields = @($unreadableFields.ToArray())
    suppliedFields = @($suppliedFields.ToArray())
    carriedForwardUnverified = $carriedForward
    rejectedUnreachable = @($finalized.RejectedUnreachable)
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent ([IO.Path]::GetFullPath($LedgerPath))) | Out-Null
$ledger | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $LedgerPath -Encoding UTF8
if ($finalized.RejectedUnreachable.Count -gt 0) {
    Write-Warning "$($finalized.RejectedUnreachable.Count) unreachable Story rows rejected. Review rejectedUnreachable in $LedgerPath."
}

Write-Host "Generated $($deduplicated.Count) FFVII story objectives from $milestoneCount native GameMoment writes at $OutputPath"
Write-Host "Navigable writes: $navigableMilestoneCount; automatic or unresolved writes: $($unresolved.Count)"
if ($suppliedFields.Count -gt 0) {
    Write-Host ("Read {0} offline field file(s) from the verified supplement: {1}" -f
        $suppliedFields.Count, ($suppliedFields -join ', '))
}
if ($unreadableFields.Count -gt 0) {
    # Named, not swallowed. Rows carried from the previous catalog for these fields
    # were generated from data that was readable then; they are not re-verified by
    # this run and must not be reported as if they were.
    Write-Warning ("{0} field file(s) are offline with no supplement: {1}. {2} previously generated row(s) were carried forward UNVERIFIED for them." -f
        $unreadableFields.Count, ($unreadableFields -join ', '), $carriedForward)
}















