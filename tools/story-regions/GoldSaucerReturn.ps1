# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
#
# The return to the Gold Saucer for the Keystone, and the next morning after it.
#
# The showroom is clsin2_2, field 503, reached from the battle hall coloin1/500 by its
# gateway2. The Keystone on display is entity 4, a model with no Talk at all: what the
# player actually uses is l1, entity 6, a LINE at (1763,-2781,0)..(1783,-2911,0) whose
# [OK] is active only between 566 and 582. With Bank[3][67] bit 7 still clear it runs
# Dio's Script 3, which sets that bit and offers the challenge. Declining is allowed -
# Dio himself, entity 5, is live and his Talk offers it again - and only accepting
# writes 580.
#
# Dio's Script 4 branches on how the battles went and hands out different prizes, but
# every branch converges on writing 583 at byte 237 and releasing the player, so the
# reward bits and the battle counter cannot gate anything. At 583 the way out of the
# showroom is its own gateway0 back to the hall, and the hall's gateway0 out to the
# square - not back into the arena.
#
# Then the cable station scene writes 586, the hotel conversation and date selection
# write 589, and 592, 595 and 598 follow. All four partners are reachable; nothing here
# names one, and nothing here says anything about affection or about how the evening
# ends.
$goldKeystoneVisit = @{ MinimumGameMoment = 566; MaximumGameMoment = 579; Priority = 0 }
$goldChallengeOffered = New-Condition -Bank 3 -Address 67 -Mask 128 -Value 128
$goldChallengeNotYetSeen = New-Condition -Bank 3 -Address 67 -Mask 128 -Value 0

Add-Definition @goldKeystoneVisit -FieldId 500 -FieldName 'coloin1' -Kind Location `
    -Label 'Go through to the showroom' -X 1198 -Y -2814 -Z 40 `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=1252; startY=-2753; startZ=79; endX=1145; endY=-2876; endZ=2 })

Add-Definition @goldKeystoneVisit -FieldId 503 -FieldName 'clsin2_2' -Kind Location -EntityId 6 `
    -Label 'Look at the display case' -X 1773 -Y -2846 -Z 0 `
    -TargetGameMoment 580 -RequiredCondition $goldChallengeNotYetSeen `
    -CompletedCondition $goldChallengeOffered `
    -EntityName 'l1' -ScriptType '[OK]' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
    -RequiredEnabledLineEntityId 6 `
    -TriggerLine ([ordered]@{ startX=1763; startY=-2781; startZ=0; endX=1783; endY=-2911; endZ=0 })

# Having looked once and said no, the owner is still standing there and will ask again.
Add-Definition @goldKeystoneVisit -FieldId 503 -FieldName 'clsin2_2' -Kind Model -EntityId 5 `
    -Label 'Talk to the showroom owner' `
    -TargetGameMoment 580 -RequiredCondition $goldChallengeOffered `
    -EntityName 'dio' -ScriptType 'Talk'

# Afterwards, whatever the battles produced. 583 is written by every branch.
Add-Definition -FieldId 503 -FieldName 'clsin2_2' -Kind Location `
    -Label 'Leave the showroom' -X 1241 -Y -2816 -Z -87 `
    -MinimumGameMoment 583 -MaximumGameMoment 585 -Priority 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=1243; startY=-2774; startZ=-86; endX=1239; endY=-2858; endZ=-89 })

Add-Definition -FieldId 500 -FieldName 'coloin1' -Kind Location `
    -Label 'Leave the battle hall' -X 0 -Y -3491 -Z -152 `
    -MinimumGameMoment 583 -MaximumGameMoment 585 -Priority 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-94; startY=-3491; startZ=-152; endX=94; endY=-3491; endZ=-152 })

# The extracted row for ghotin_4's ll line takes its band from a GameMoment test on
# the request chain that reaches the write, and lands on 586..589. The line itself is
# switched on by LINON at exactly 601 and nowhere else, so that band offers a door that
# is not there. Removed in favour of the reviewed row below.
for ($index = $definitions.Count - 1; $index -ge 0; $index--) {
    $definition = $definitions[$index]
    if ($definition.fieldId -eq 493 -and $definition.targetGameMoment -eq 604 -and
        $definition.priority -ge 100) {
        $definitions.RemoveAt($index)
    }
}

for ($index = $definitions.Count - 1; $index -ge 0; $index--) {
    $definition = $definitions[$index]
    if ($definition.fieldId -eq 503 -and $definition.targetGameMoment -eq 580 -and
        $definition.priority -ge 100) {
        $definitions.RemoveAt($index)
    }
}

# The next morning. ghotin_2's l, entity 7, is switched on at exactly 601 and its Go
# reaches the room the rest of the party is in. ghotin_4's Director then calls MPJPO at
# byte 30, so that room's ordinary gateways are all off and its ll, entity 10, is the
# only door: its Go writes 604 and leaves for the hotel. The room forms a default party
# by itself and the companions standing there can be talked to, so there is no extra
# party condition to satisfy first and none is invented here.
$goldNextMorning = @{ MinimumGameMoment = 601; MaximumGameMoment = 603; Priority = 0 }

Add-Definition @goldNextMorning -FieldId 494 -FieldName 'ghotin_2' -Kind Location -EntityId 7 `
    -Label 'Go through to the rest of the party' -X -405 -Y 0 -Z 0 `
    -EntityName 'l' -ScriptType 'Go' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 7 `
    -TriggerLine ([ordered]@{ startX=-405; startY=-72; startZ=0; endX=-405; endY=72; endZ=0 })

Add-Definition @goldNextMorning -FieldId 493 -FieldName 'ghotin_4' -Kind Location -EntityId 10 `
    -Label 'Leave the room and go on' -X -563 -Y -63 -Z 0 -TargetGameMoment 604 `
    -EntityName 'll' -ScriptType 'Go' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 10 `
    -TriggerLine ([ordered]@{ startX=-563; startY=71; startZ=0; endX=-563; endY=-198; endZ=0 })

# The chase, between the hub's write of 598 and the write of 601 that puts the party in
# the hotel room the next morning. crcin_1's Director Init calls MPJPO at 598 while
# Bank[3][69] bit 0 is clear, so every static gateway in the room is off and the two
# corridor lines are the only things that respond: l1 (entity 5) and l2 (entity 6) each
# call MPJPO the other way, set that bit and switch themselves off. Only then do jp1
# and jp2, which test the same bit, carry the party on to chorace2 - which writes 601
# under exactly that pair of conditions. Both lines and both exits are offered; the
# player takes whichever they reach.
$goldChase = @{ MinimumGameMoment = 598; MaximumGameMoment = 600; Priority = 0 }
$goldChaseCornered = New-Condition -Bank 3 -Address 69 -Mask 1 -Value 1
$goldChaseRunning = New-Condition -Bank 3 -Address 69 -Mask 1 -Value 0

foreach ($corridor in @(
    @{ entity=5; name='l1'; x=-168; y=353; line=@{ startX=-101; startY=137; startZ=0; endX=-235; endY=569; endZ=0 } },
    @{ entity=6; name='l2'; x=214;  y=244; line=@{ startX=159;  startY=96;  startZ=0; endX=269;  endY=393; endZ=0 } })) {
    Add-Definition @goldChase -FieldId 511 -FieldName 'crcin_1' -Kind Location -EntityId $corridor.entity `
        -Label 'Cut off the way out of the corridor' -X $corridor.x -Y $corridor.y -Z 0 `
        -RequiredCondition $goldChaseRunning -CompletedCondition $goldChaseCornered `
        -EntityName $corridor.name -ScriptType 'Go' -UsesPlayerCollisionRadius `
        -RequiredEnabledLineEntityId $corridor.entity `
        -TriggerLine $corridor.line
}

foreach ($exit in @(
    @{ entity=17; name='jp1'; x=-512; y=-422; line=@{ startX=-623; startY=-194; startZ=0; endX=-402; endY=-650; endZ=0 } },
    @{ entity=18; name='jp2'; x=-109; y=-649; line=@{ startX=-402; startY=-650; startZ=0; endX=184;  endY=-649; endZ=0 } })) {
    Add-Definition @goldChase -FieldId 511 -FieldName 'crcin_1' -Kind Location -EntityId $exit.entity `
        -Label 'Follow on out of the corridor' -X $exit.x -Y $exit.y -Z 0 `
        -TargetGameMoment 601 `
        -RequiredCondition $goldChaseCornered `
        -EntityName $exit.name -ScriptType 'Go' -UsesPlayerCollisionRadius `
        -RequiredEnabledLineEntityId $exit.entity `
        -TriggerLine $exit.line
}

# The arena itself, the party room's optional conversations and the date evening's own
# squares are not claimed here. Neither is anything about which companion the evening
# is spent with: all four remain reachable and nothing in these rows prefers one.
Add-CuratedFields 493, 494, 500, 503, 511

