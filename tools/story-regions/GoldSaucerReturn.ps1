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


# --- The way in, and the way back out -------------------------------------------------
#
# Added after a second pass over the installed scripts. The rows above cover the two
# rooms the Keystone chapter talks in, and nothing at all covered getting to them. The
# Gold Saucer's seven Squares are not doors: gldgate's chekun, entity 12, polls the party
# leader's walkmesh triangle every frame and runs cloud's Script 4, which map jumps on
# the triangle id alone - 26 and 27 are the Battle Square, 36 and 37 the Round Square.
# There is exactly one gateway on the Terminal Floor and it goes back to the Ropeway
# Station, so without a row on the pads there is no way for a player who cannot see the
# tubes to reach any Square at all. GoldSaucer.ps1 established the pad triangles and the
# approach points for the first visit; these are the same pads at the later moments.
#
#   566  Ropeway Station, then the Terminal Floor, then the Battle Square pad. coloss is
#        the Square itself and its gateway0 is the Arena Lobby door; the lobby's gateway2
#        is Dio's Museum, which the rows above already carry.
#   580  dio's Talk writes 580 and map jumps to 502, the Arena, at the same landing
#        coloin1's gateway1 uses. Every branch of dio's Script 4 hands the Keystone over
#        and writes 583 at byte 237, and that script is run by clsin2_2's own Director
#        the moment the party walks back into the Museum - so the step is the walk back,
#        and the arena battles change only which extra prizes come with it. They are not
#        claimed here.
#   583  Museum, Arena Lobby, Battle Square, Terminal Floor, Ropeway Station. The first
#        two of those are already covered above; the last three were not.
#   592  ghotin_2's Director writes 592 and map jumps to the Terminal Floor, where
#        gldgate's own Main hands control back. The evening ends at the Round Square:
#        bigwheel's staff, entity 10, is the only thing there that leads anywhere, and
#        its Talk map jumps into the Ferris wheel, whose Director writes 595. The other
#        Squares are open and are offered as optional, because they are what the evening
#        is; none of them is a prerequisite and none of them writes anything.
$goldTicketMissing = New-Condition -Bank 3 -Address 72 -Mask 129 -Value 0
$goldTicketHeld = New-Condition -Bank 3 -Address 72 -Mask 129 -Value 0 -AnyBitSet

$goldKeystoneApproach = @{ MinimumGameMoment = 566; MaximumGameMoment = 582; Priority = 0 }
$goldKeystoneReturn = @{ MinimumGameMoment = 583; MaximumGameMoment = 585; Priority = 0 }
$goldDateEvening = @{ MinimumGameMoment = 592; MaximumGameMoment = 594; Priority = 0 }

# North Corel and the ropeway, the same two lines the first visit uses.
Add-Definition @goldKeystoneApproach -FieldId 450 -FieldName 'ncorel' -Kind Location -EntityId 4 `
    -Label 'Go to the Gold Saucer ropeway station' -X -665 -Y -681 -Z 0 `
    -EntityName 'evline2' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 4 `
    -TriggerLine ([ordered]@{ startX=-673; startY=-631; startZ=0; endX=-657; endY=-731; endZ=0 })

Add-Definition @goldKeystoneApproach -FieldId 457 -FieldName 'ropest' -Kind Location -EntityId 6 `
    -Label 'Board the ropeway to Gold Saucer' -X -176 -Y -13 -Z 128 `
    -EntityName 'evline3' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 6 `
    -TriggerLine ([ordered]@{ startX=-176; startY=35; startZ=128; endX=-176; endY=-62; endZ=128 })

# The gate still wants a ticket, and the clerk still sells one. Both conditions are the
# ones NorthCorel.ps1 read out of gldst's own scripts: bank3[72] bit 0 is the day ticket
# and bit 7 the lifetime one, and leaving by ropeway clears only the first.
Add-Definition @goldKeystoneApproach -FieldId 496 -FieldName 'gldst' -Kind Model -EntityId 15 `
    -Label 'Talk to the Gold Saucer ticket clerk' `
    -RequiredCondition $goldTicketMissing -CompletedCondition $goldTicketHeld `
    -EntityName 's1' -ScriptType 'Talk'

Add-Definition @goldKeystoneApproach -FieldId 496 -FieldName 'gldst' -Kind Location `
    -Label 'Enter Gold Saucer' -X -74 -Y -1358 -Z 0 `
    -RequiredCondition $goldTicketHeld `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-60; startY=-1273; startZ=0; endX=-89; endY=-1444; endZ=0 })

Add-Definition @goldKeystoneApproach -FieldId 497 -FieldName 'gldgate' -Kind Location `
    -Label 'Take the Battle Square platform' -X 427 -Y 479 -Z 18 `
    -CompletionPlayerTriangles @(26, 27) `
    -EntityName 'chekun' -ScriptType 'Main'

Add-Definition @goldKeystoneApproach -FieldId 499 -FieldName 'coloss' -Kind Location `
    -Label 'Go in to the Arena Lobby' -X 1 -Y -337 -Z -374 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-65; startY=-337; startZ=-374; endX=67; endY=-338; endZ=-374 })

# After the challenge has been accepted the party is standing in the Arena, and the way
# on is back through the lobby into the Museum.
Add-Definition -FieldId 502 -FieldName 'clsin2_1' -Kind Location `
    -Label 'Go back to the Arena Lobby' -X 1 -Y -1007 -Z -2 `
    -MinimumGameMoment 580 -MaximumGameMoment 582 -Priority 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-476; startY=-959; startZ=-2; endX=478; endY=-1056; endZ=-2 })

Add-Definition -FieldId 500 -FieldName 'coloin1' -Kind Location `
    -Label 'Go back through to the showroom' -X 1198 -Y -2814 -Z 40 `
    -MinimumGameMoment 580 -MaximumGameMoment 582 -Priority 0 `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=1252; startY=-2753; startZ=79; endX=1145; endY=-2876; endZ=2 })

# Leaving. coloss has two gateways back to the Terminal Floor and either will do.
foreach ($goldSquareExit in @(
    @{ gateway=7; x=-334; y=-2183; z=-1148; line=@{ startX=-329; startY=-2215; startZ=-1148; endX=-339; endY=-2152; endZ=-1148 } },
    @{ gateway=8; x=359;  y=-2179; z=-1137; line=@{ startX=369;  startY=-2132; startZ=-1137; endX=349;  endY=-2227; endZ=-1137 } })) {
    Add-Definition @goldKeystoneReturn -FieldId 499 -FieldName 'coloss' -Kind Location `
        -Label 'Go back to the Terminal Floor' -X $goldSquareExit.x -Y $goldSquareExit.y -Z $goldSquareExit.z `
        -EntityName "gateway$($goldSquareExit.gateway)" -ScriptType 'Gateway' `
        -TriggerLine $goldSquareExit.line
}

Add-Definition @goldKeystoneReturn -FieldId 497 -FieldName 'gldgate' -Kind Location `
    -Label 'Take the way back to the Ropeway Station' -X 632 -Y -691 -Z 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=845; startY=-606; startZ=0; endX=420; endY=-776; endZ=0 })

# Story points to the required Round Square platform. All seven platforms remain
# available independently in Exits through GoldSaucerPlatformExitCatalog.
Add-Definition @goldDateEvening -FieldId 497 -FieldName 'gldgate' -Kind Location `
    -Label 'Take the Round Square platform' -X -390 -Y 507 -Z 18 `
    -CompletionPlayerTriangles @(36, 37) `
    -EntityName 'chekun' -ScriptType 'Main'

# The attendant is not a step and is deliberately not offered. bigwheel's own dic Main
# takes the branch at byte 295 for 592 and calls staff Script 3 at byte 367, and that
# script hands over the tickets itself and ends at byte 73 with a map jump into the
# Ferris wheel. Arriving at the Square is the whole of it; the staff Talk at that moment
# is the ordinary paid ride.

# These companion Talks already expire at TargetGameMoment440. Add a lower
# bound so they are offered only during the first-visit companion selection.
foreach ($goldCompanionRow in $definitions) {
    if ($goldCompanionRow.fieldId -eq 497 -and $goldCompanionRow.targetGameMoment -eq 440 -and
        $goldCompanionRow.minimumGameMoment -lt 0 -and $goldCompanionRow.maximumGameMoment -lt 0) {
        $goldCompanionRow.minimumGameMoment = 439
        $goldCompanionRow.maximumGameMoment = 441
    }
}
