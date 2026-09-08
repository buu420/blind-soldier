# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
#
# Mideel, the two Huge Materia missions in either order, the return to Mideel and the
# memory sequence that gets Cloud back. Almost none of it writes a GameMoment where the
# player is standing, and the two missions can be done in either order and can both be
# lost, so the ordering here is by the flags the game itself keeps rather than by the
# story counter.
#
# What completes each mission, whatever the outcome:
#   the train  Bank[15][145] bit 5, set whether it was caught, missed or ran out of time
#   the fort   Bank[13][82] bit 5, set by a successful elder handoff or by losing
# The rewards - Bank[1][66] bits 4 and 5 - are not completion, and a party that missed
# them is not sent back for them.
#
#   1033  itown1a. line01, entity 11, is a Go-once from (-373,-514) to (192,-514) and is
#         the conversation outside the clinic that starts the visit. The dog only barks.
#         The line switches off at 1100 or once Bank[15][177] bit 1 is set.
#   1110  Both missions are open. Corel: mtcrl_2's event line, entity 17, only while
#         Bank[15][144] bit 0 is clear - once the pursuit has happened it is over.
#         Condor: fort_1's elder, entity 29, then gateway0 up to the lookout.
#   ...   zcoal_2 is the train itself: four cars, each a battle line then a jump line,
#         and each battle sets its own flag. The rows follow those flags rather than the
#         counter, because the counter does not move at all up there.
#   1116  Written when the second of the two finishes. Back at Mideel, gateway1 into the
#         clinic, then Tifa - entity 2 - whose Talk writes 1118 and hands control back.
#         The party then has to walk out of the clinic themselves: <c>out</c>, entity 12,
#         is the only line that works and every ordinary gateway there is switched off.
#   1122  The memory. zmind1 has two doors and only ever one of them open: door1, entity
#         8, on the way in, and door2, entity 9, on the way back at 1180.
#   1126  zmind2's shad2, entity 3, by the well.
#   1130  zmind3: shad1, entity 3, first - he moves aside and sets Bank[5][6] - and only
#         once he has finished moving the child, entity 7, and Bank[5][7].
#   1178  whitebg3 does not run itself: young Cloud, entity 4, has to be spoken to.
#   1188  zmind3 again, with Cloud himself, entity 1.
#   1199  fship_3's evt1, entity 3, is the line that writes 1250 - not the operations
#         crew, whose own row belongs to the earlier visits.
$trainMissionDone = New-Condition -Bank 15 -Address 145 -Mask 0x20 -Value 0x20
$trainMissionOpen = New-Condition -Bank 15 -Address 144 -Mask 0x01 -Value 0x00
$fortMissionDone = New-Condition -Bank 13 -Address 82 -Mask 0x20 -Value 0x20
$fortMissionOpen = New-Condition -Bank 13 -Address 82 -Mask 0x20 -Value 0x00
$fortJoined = New-Condition -Bank 1 -Address 168 -Mask 0x03 -Value 0x03
$fortNotJoined = New-Condition -Bank 1 -Address 168 -Mask 0x03 -Value 0x00
$fortDefenceWon = New-Condition -Bank 1 -Address 168 -Mask 0x40 -Value 0x40
$fortHandoffPending = New-Condition -Bank 1 -Address 177 -Mask 0x01 -Value 0x00
$mideelFirstVisitOpen = New-Condition -Bank 15 -Address 177 -Mask 0x02 -Value 0x00
$mideelTifaSpoken = New-Condition -Bank 5 -Address 4 -Mask 0xFF -Value 0x01
$memoryCloudMovedAside = New-Condition -Bank 5 -Address 6 -Mask 0xFF -Value 0x01
$memoryCloudStillThere = New-Condition -Bank 5 -Address 6 -Mask 0xFF -Value 0x00
$memoryChildSettled = New-Condition -Bank 5 -Address 7 -Mask 0xFF -Value 0x01

# --- The first visit ------------------------------------------------------------------
Add-Definition -FieldId 712 -FieldName 'itown1a' -Kind Location -EntityId 11 -Priority 0 `
    -Label 'Walk along the street past the clinic' -X -90 -Y -514 -Z 0 `
    -MinimumGameMoment 1033 -MaximumGameMoment 1099 `
    -RequiredCondition $mideelFirstVisitOpen `
    -EntityName 'line01' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 11 `
    -TriggerLine ([ordered]@{ startX = -373; startY = -514; startZ = 0; endX = 192; endY = -514; endZ = 0 })

# --- The Corel train ---------------------------------------------------------------------
Add-Definition -FieldId 460 -FieldName 'mtcrl_2' -Kind Location -EntityId 17 -Priority 0 `
    -Label 'Go on past the reactor guards' -X 51 -Y -483 -Z -600 `
    -MinimumGameMoment 1110 -MaximumGameMoment 1115 `
    -RequiredCondition $trainMissionOpen `
    -EntityName 'event' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 17 `
    -TriggerLine ([ordered]@{ startX = -32; startY = -493; startZ = -600; endX = 135; endY = -473; endZ = -600 })

# The four cars. Each battle is offered only while its own flag is clear, and the jump
# off that car only once it is set, so a car already fought is never offered again and
# a car not yet fought is never skipped. Both are the same priority: they are mutually
# exclusive per car by that flag, and putting the jump in a lower tier would hide it
# behind the next carriage's battle.
foreach ($car in @(
    @{ battle = 6;  jump = 7;  bank = 15; address = 144; mask = 0x40
       bx1 = 862; by1 = 253;  bz1 = 160; bx2 = 757; by2 = 237;  bz2 = 137
       jx1 = 863; jy1 = 290;  jz1 = 158; jx2 = 758; jy2 = 290;  jz2 = 136; ordinal = 'first' },
    @{ battle = 8;  jump = 9;  bank = 15; address = 144; mask = 0x80
       bx1 = 863; by1 = 508;  bz1 = 156; bx2 = 755; by2 = 496;  bz2 = 139
       jx1 = 863; jy1 = 545;  jz1 = 154; jx2 = 755; jy2 = 545;  jz2 = 138; ordinal = 'second' },
    @{ battle = 10; jump = 11; bank = 15; address = 145; mask = 0x01
       bx1 = 860; by1 = 760;  bz1 = 153; bx2 = 754; by2 = 748;  bz2 = 137
       jx1 = 860; jy1 = 797;  jz1 = 152; jx2 = 754; jy2 = 797;  jz2 = 136; ordinal = 'third' },
    @{ battle = 12; jump = 13; bank = 15; address = 145; mask = 0x02
       bx1 = 856; by1 = 1043; bz1 = 151; bx2 = 754; by2 = 1031; bz2 = 136
       jx1 = 856; jy1 = 1080; jz1 = 150; jx2 = 754; jy2 = 1063; jz2 = 135; ordinal = 'fourth' })) {

    $fought = New-Condition -Bank $car.bank -Address $car.address -Mask $car.mask -Value $car.mask
    $unfought = New-Condition -Bank $car.bank -Address $car.address -Mask $car.mask -Value 0

    Add-Definition -FieldId 729 -FieldName 'zcoal_2' -Kind Location -EntityId $car.battle -Priority 0 `
        -Label "Take on the $($car.ordinal) carriage" `
        -X ([int](($car.bx1 + $car.bx2) / 2)) -Y ([int](($car.by1 + $car.by2) / 2)) -Z $car.bz1 `
        -MinimumGameMoment 1110 -MaximumGameMoment 1115 `
        -RequiredCondition $unfought -CompletedCondition $fought `
        -EntityName "BTL" -ScriptType 'Move' -UsesPlayerCollisionRadius `
        -RequiredEnabledLineEntityId $car.battle `
        -TriggerLine ([ordered]@{ startX = $car.bx1; startY = $car.by1; startZ = $car.bz1
                                  endX = $car.bx2; endY = $car.by2; endZ = $car.bz2 })

    Add-Definition -FieldId 729 -FieldName 'zcoal_2' -Kind Location -EntityId $car.jump -Priority 0 `
        -Label "Jump on past the $($car.ordinal) carriage" `
        -X ([int](($car.jx1 + $car.jx2) / 2)) -Y ([int](($car.jy1 + $car.jy2) / 2)) -Z $car.jz1 `
        -MinimumGameMoment 1110 -MaximumGameMoment 1115 `
        -RequiredCondition $fought `
        -EntityName "jump" -ScriptType 'Move' -UsesPlayerCollisionRadius `
        -RequiredEnabledLineEntityId $car.jump `
        -TriggerLine ([ordered]@{ startX = $car.jx1; startY = $car.jy1; startZ = $car.jz1
                                  endX = $car.jx2; endY = $car.jy2; endZ = $car.jz2 })
}

# Arriving in one piece: every ordinary gateway is off and the celebration line is the
# way on to the inn.
Add-Definition -FieldId 452 -FieldName 'ncorel3' -Kind Location -EntityId 9 -Priority 0 `
    -Label 'Go over to the townspeople' -X 327 -Y -186 -Z 0 `
    -MinimumGameMoment 1110 -MaximumGameMoment 1117 `
    -EntityName 'thanks2' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 9 `
    -TriggerLine ([ordered]@{ startX = 313; startY = -159; startZ = 0; endX = 342; endY = -213; endZ = 0 })

Add-Definition -FieldId 456 -FieldName 'ncoinn' -Kind Location -EntityId 2 -Priority 0 `
    -Label 'Leave the inn' -X 191 -Y -365 -Z 0 `
    -MinimumGameMoment 1110 -MaximumGameMoment 1117 `
    -EntityName 'evline1' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 2 `
    -TriggerLine ([ordered]@{ startX = 252; startY = -370; startZ = 0; endX = 130; endY = -361; endZ = 0 })

# Missing the train is a finished mission, not a retry. From the town the way on is the
# world map and whichever mission is still open.
Add-Definition -FieldId 450 -FieldName 'ncorel1' -Kind Location -Priority 0 `
    -Label 'Leave North Corel' -X 777 -Y -531 -Z 0 `
    -MinimumGameMoment 1110 -MaximumGameMoment 1117 `
    -RequiredCondition $trainMissionDone `
    -EntityName 'gateway5' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 810; startY = -411; startZ = 0; endX = 745; endY = -652; endZ = 0 })

# --- Fort Condor ---------------------------------------------------------------------------
Add-Definition -FieldId 355 -FieldName 'fort_1' -Kind Model -EntityId 29 -Priority 0 `
    -Label 'Talk to the elder' `
    -MinimumGameMoment 1110 -MaximumGameMoment 1115 `
    -RequiredConditions @($fortNotJoined, $fortMissionOpen) `
    -EntityName 'jijii' -ScriptType 'Talk'

Add-Definition -FieldId 355 -FieldName 'fort_1' -Kind Location -Priority 0 `
    -Label 'Climb to the top of the fort' -X -127 -Y 267 -Z 1376 `
    -MinimumGameMoment 1110 -MaximumGameMoment 1115 `
    -RequiredConditions @($fortJoined, $fortMissionOpen) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -122; startY = 224; startZ = 1371; endX = -132; endY = 311; endZ = 1381 })

Add-Definition -FieldId 356 -FieldName 'fort_2' -Kind Model -EntityId 22 -Priority 0 `
    -Label 'Talk to the lookout to set up the defence' `
    -MinimumGameMoment 1110 -MaximumGameMoment 1115 `
    -RequiredConditions @($fortJoined, $fortMissionOpen) `
    -EntityName 'miharu' -ScriptType 'Talk'

# After the defence holds, the way back down is the ordinary lower gateway. The Phoenix
# out on the exterior is optional and is not on the way.
Add-Definition -FieldId 356 -FieldName 'fort_2' -Kind Location -Priority 0 `
    -Label 'Go back down to the elder' -X 175 -Y -66 -Z -115 `
    -MinimumGameMoment 1110 -MaximumGameMoment 1115 `
    -RequiredConditions @($fortDefenceWon, $fortMissionOpen) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 150; startY = -98; startZ = -109; endX = 200; endY = -34; endZ = -122 })

Add-Definition -FieldId 355 -FieldName 'fort_1' -Kind Model -EntityId 29 -Priority 0 `
    -Label 'Report to the elder' `
    -MinimumGameMoment 1110 -MaximumGameMoment 1115 `
    -RequiredConditions @($fortDefenceWon, $fortHandoffPending) `
    -CompletedCondition $fortMissionDone `
    -EntityName 'jijii' -ScriptType 'Talk'

# Losing is a finished mission too: the field drops the party into the lower chamber and
# sets the same completion flag, and the way out is the ordinary door.
Add-Definition -FieldId 354 -FieldName 'fort_3' -Kind Location -Priority 0 `
    -Label 'Leave the fort' -X 1 -Y -346 -Z 0 `
    -MinimumGameMoment 1110 -MaximumGameMoment 1117 `
    -RequiredCondition $fortMissionDone `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -70; startY = -302; startZ = 0; endX = 73; endY = -390; endZ = 0 })

Add-Definition -FieldId 353 -FieldName 'fort_4' -Kind Location -Priority 0 `
    -Label 'Leave for the world map' -X 8 -Y -676 -Z 0 `
    -MinimumGameMoment 1110 -MaximumGameMoment 1117 `
    -RequiredCondition $fortMissionDone `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -70; startY = -692; startZ = 0; endX = 86; endY = -660; endZ = 0 })

# --- Back to Mideel ----------------------------------------------------------------------------
Add-Definition -FieldId 712 -FieldName 'itown1a' -Kind Location -Priority 0 `
    -Label 'Go into the clinic' -X -243 -Y 620 -Z 123 `
    -MinimumGameMoment 1116 -MaximumGameMoment 1117 `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -243; startY = 569; startZ = 123; endX = -243; endY = 672; endZ = 123 })

Add-Definition -FieldId 720 -FieldName 'ithos' -Kind Model -EntityId 2 -Priority 0 `
    -Label 'Talk to Tifa' `
    -MinimumGameMoment 1116 -MaximumGameMoment 1117 `
    -EntityName 'tifa' -ScriptType 'Talk'

# The clinic's own way out, which is the only one that works and is needed both before
# the missions are over and again once Tifa has spoken.
Add-Definition -FieldId 720 -FieldName 'ithos' -Kind Location -EntityId 12 -Priority 1 `
    -Label 'Leave the clinic' -X 251 -Y -168 -Z 0 `
    -MinimumGameMoment 1100 -MaximumGameMoment 1121 `
    -EntityName 'out' -ScriptType 'Go' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
    -RequiredEnabledLineEntityId 12 `
    -TriggerLine ([ordered]@{ startX = 251; startY = -208; startZ = 0; endX = 251; endY = -128; endZ = 0 })

# --- The memory ----------------------------------------------------------------------------------
Add-Definition -FieldId 725 -FieldName 'zmind1' -Kind Location -EntityId 8 -Priority 0 `
    -Label 'Go through the nearer doorway' -X 74 -Y -5777 -Z -48 `
    -MinimumGameMoment 1122 -MaximumGameMoment 1123 `
    -EntityName 'door1' -ScriptType 'Go' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 8 `
    -TriggerLine ([ordered]@{ startX = -1010; startY = -5787; startZ = -48; endX = 1159; endY = -5768; endZ = -48 })

Add-Definition -FieldId 725 -FieldName 'zmind1' -Kind Location -EntityId 9 -Priority 0 `
    -Label 'Go through the far doorway' -X 82 -Y -4840 -Z -48 `
    -MinimumGameMoment 1180 -MaximumGameMoment 1180 `
    -EntityName 'door2' -ScriptType 'Go' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 9 `
    -TriggerLine ([ordered]@{ startX = -1094; startY = -4798; startZ = -48; endX = 1259; endY = -4882; endZ = -48 })

Add-Definition -FieldId 726 -FieldName 'zmind2' -Kind Model -EntityId 3 -Priority 0 `
    -Label 'Talk to the figure by the well' `
    -MinimumGameMoment 1126 -MaximumGameMoment 1127 `
    -EntityName 'shad2' -ScriptType 'Talk'

Add-Definition -FieldId 727 -FieldName 'zmind3' -Kind Model -EntityId 3 -Priority 0 `
    -Label 'Talk to the figure in the way' `
    -MinimumGameMoment 1130 -MaximumGameMoment 1131 `
    -RequiredCondition $memoryCloudStillThere `
    -EntityName 'shad1' -ScriptType 'Talk'

# The child only has anything to say once he has finished moving and the field says so.
Add-Definition -FieldId 727 -FieldName 'zmind3' -Kind Model -EntityId 7 -Priority 0 `
    -Label 'Talk to the child' `
    -MinimumGameMoment 1130 -MaximumGameMoment 1131 `
    -RequiredConditions @($memoryCloudMovedAside, $memoryChildSettled) `
    -EntityName 'mabolo' -ScriptType 'Talk'

Add-Definition -FieldId 115 -FieldName 'whitebg3' -Kind Model -EntityId 4 -Priority 0 `
    -Label 'Talk to the young Cloud' `
    -MinimumGameMoment 1178 -MaximumGameMoment 1179 -TargetGameMoment 1180 `
    -EntityName 'tcl' -ScriptType 'Talk'

Add-Definition -FieldId 727 -FieldName 'zmind3' -Kind Model -EntityId 1 -Priority 0 `
    -Label 'Talk to Cloud' `
    -MinimumGameMoment 1188 -MaximumGameMoment 1198 `
    -EntityName 'cloud' -ScriptType 'Talk'

Add-Definition -FieldId 73 -FieldName 'fship_3' -Kind Location -EntityId 3 -Priority 0 `
    -Label 'Go over to the others' -X 4 -Y -320 -Z 0 `
    -MinimumGameMoment 1199 -MaximumGameMoment 1249 `
    -EntityName 'evt1' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 3 `
    -TriggerLine ([ordered]@{ startX = -59; startY = -320; startZ = 0; endX = 67; endY = -320; endZ = 0 })

Add-CuratedFields 712, 720, 725, 726, 727, 115, 73, 460, 729, 452, 456, 450, 355, 356, 354, 353
