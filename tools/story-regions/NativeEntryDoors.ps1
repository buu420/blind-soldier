# Dot-sourced by Generate-FieldStoryEvents.ps1 after the reviewed regions.
#
# The reason story guidance runs out is structural, not regional. Milestone extraction
# can only put a row where the write is, and most of FFVII's story writes are performed
# by a Director or a model's own Main the instant the party walks into the next room.
# There is nothing in that room to point at: the step is getting there. So a player is
# told what to do in the room that has a talkable milestone and told nothing at all in
# every room between, which is exactly the "it stops" the report describes.
#
# This pass fills only that one shape, and only where the native scripts state it
# outright:
#
#   * the write is performed by an entity's own Init or Main, so it runs on entry;
#   * the only thing between entry and the write is a single equality test on the
#     GameMoment, so the value the door is the objective at is the script's own test
#     and not an inference from the order writes happen to be numbered in;
#   * the row is one hop. It is placed on a real door whose destination is that very
#     field - never a chain, never a guess about where the party currently is. A field
#     row is only ever offered while the player is standing in that field, so the row
#     says no more than it can prove: from here, this exit is the next step now.
#
# It deliberately does not fire for anything else. A room that also needs a flag set,
# or that tests a range rather than a value, is not a room a door alone gets you
# through, and the earlier attempt to reach those by graph search is what independent
# review rejected. Those remain the reviewed regions' work and are recorded as
# remaining in the ledger.
#
# Doors are taken from the engine's own gateway table and from LINE entities whose
# player-triggered scripts perform the MAPJUMP themselves. A field whose Director calls
# MPJPO has no working gateway table at all - jail1, jail3 and jail4 each do - so only
# its LINE doors are used there.

$entryDoorRows = 0
$entryDoorFields = [Collections.Generic.HashSet[string]]::new()
$entryDoorSkippedDisabled = 0

# Rooms that exist only to hold dialogue for testing. They are entered by no door in
# normal play and their writes are not story steps.
function Test-EntryDoorPlayable {
    param([string] $Name)
    # The same measured classification the generator uses, rather than a second guess
    # at it: a dialogue-test room is not part of the game and a film has no navigation.
    if (-not (Test-PlayableField $Name)) { return $false }
    if ($Name -match '^wm\d') { return $false }
    return $true
}

foreach ($destinationName in $entryWritesByField.Keys) {
    if (-not (Test-EntryDoorPlayable $destinationName)) {
        continue
    }

    foreach ($entryWrite in $entryWritesByField[$destinationName]) {
        $requiredMoment = [int]$entryWrite.RequiredGameMoment

        foreach ($sourceName in $gatewaysByField.Keys) {
            if ($sourceName -eq $destinationName -or -not (Test-EntryDoorPlayable $sourceName)) {
                continue
            }
            if (-not $fieldIds.ContainsKey($sourceName)) {
                continue
            }
            # The reviewed regions decide for themselves which of their rooms are on
            # the route at which moment, including the ones that are deliberately not.
            if ($curatedFields.Contains([int]$fieldIds[$sourceName])) {
                continue
            }

            foreach ($door in $gatewaysByField[$sourceName]) {
                if ([string]$door.ToField -ne $destinationName) {
                    continue
                }
                if ($door.ScriptType -eq 'Gateway' -and $gatewaysDisabledFields.ContainsKey($sourceName)) {
                    $entryDoorSkippedDisabled++
                    continue
                }

                $parameters = @{
                    FieldId = [int]$fieldIds[$sourceName]
                    FieldName = $sourceName
                    Kind = 'Location'
                    Label = 'Take this exit to continue'
                    X = [int](($door.X1 + $door.X2) / 2)
                    Y = [int](($door.Y1 + $door.Y2) / 2)
                    Z = [int](($door.Z1 + $door.Z2) / 2)
                    TargetGameMoment = [int]$entryWrite.TargetGameMoment
                    MinimumGameMoment = $requiredMoment
                    MaximumGameMoment = $requiredMoment
                    Priority = 50
                    EntityName = [string]$door.EntityName
                    ScriptType = [string]$door.ScriptType
                    TriggerLine = [ordered]@{
                        startX = [int]$door.X1; startY = [int]$door.Y1; startZ = [int]$door.Z1
                        endX = [int]$door.X2; endY = [int]$door.Y2; endZ = [int]$door.Z2
                    }
                }

                # A scripted LINE is only walked onto if it is switched on and the
                # player gets within the model's own collision radius of it. A static
                # gateway is the engine's own trigger and needs neither.
                if ($door.ScriptType -ne 'Gateway') {
                    $parameters.EntityId = [int]$door.EntityId
                    $parameters.RequiredEnabledLineEntityId = [int]$door.EntityId
                    $parameters.UsesPlayerCollisionRadius = $true
                }

                Add-Definition @parameters
                $entryDoorRows++
                [void]$entryDoorFields.Add($sourceName)
            }
        }
    }
}

# Rooms the story walks into by itself that no door reaches. Every one of them is a
# step the party is moved to by a cutscene or a vehicle, or a room whose only way in
# is through a field a reviewed region has taken charge of. They are named rather than
# counted so the ledger can say which is which.
$entryDoorOrphans = [Collections.Generic.List[string]]::new()
foreach ($destinationName in $entryWritesByField.Keys) {
    if (-not (Test-EntryDoorPlayable $destinationName)) { continue }
    $reached = $false
    foreach ($sourceName in $gatewaysByField.Keys) {
        foreach ($door in $gatewaysByField[$sourceName]) {
            if ([string]$door.ToField -eq $destinationName) { $reached = $true; break }
        }
        if ($reached) { break }
    }
    if (-not $reached) { $entryDoorOrphans.Add($destinationName) }
}
if ($entryDoorOrphans.Count -gt 0) {
    Write-Host ("Native entry rooms no door reaches: {0} -> {1}" -f
        $entryDoorOrphans.Count, (@($entryDoorOrphans | Sort-Object) -join ' '))
}

Write-Host ("Native entry doors: {0} row(s) across {1} field(s) from {2} entry write(s); {3} door(s) skipped as gateway-disabled" -f
    $entryDoorRows,
    $entryDoorFields.Count,
    (@($entryWritesByField.Values | ForEach-Object { $_ }).Count),
    $entryDoorSkippedDisabled)


if ($env:FF7_ENTRY_DOOR_TRACE) {
    $trace = $env:FF7_ENTRY_DOOR_TRACE
    Write-Host ("TRACE {0}: entryWrites={1} inGateways={2} fieldId={3} curated={4} disabled={5}" -f
        $trace,
        $(if ($entryWritesByField.ContainsKey($trace)) { @($entryWritesByField[$trace] | ForEach-Object { "$($_.RequiredGameMoment)->$($_.TargetGameMoment)" }) -join ',' } else { 'none' }),
        $gatewaysByField.ContainsKey($trace),
        $(if ($fieldIds.ContainsKey($trace)) { $fieldIds[$trace] } else { 'missing' }),
        $(if ($fieldIds.ContainsKey($trace)) { $curatedFields.Contains([int]$fieldIds[$trace]) } else { 'n/a' }),
        $gatewaysDisabledFields.ContainsKey($trace))
    if ($gatewaysByField.ContainsKey($trace)) {
        foreach ($door in $gatewaysByField[$trace]) { Write-Host ("      door {0} {1} -> {2}" -f $door.EntityName, $door.ScriptType, $door.ToField) }
    }
}
