# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
#
# Tifa and Barret's escape from Junon, and the first flight of the Highwind. The story
# counter does move through this chapter, but almost never in the room the player is
# standing in - most of it is written by an arriving scene - so extraction placed the
# rows somewhere else and the escape itself said nothing.
#
# The controlled character changes twice and back, which is why several rows carry the
# leader: Bank[3][9] is 2 while Tifa is being played and 1 while Barret is.
#
#    999  junbin3, Tifa's cell. Barret is entity 5 and his Talk is the only thing in the
#         room that does anything; gateway0 at (-1078,-871) is there but the field keeps
#         it shut until that conversation has happened, so the door is deliberately not
#         offered as a way on.
#   1010  junbin4, the press room, played as Barret. lin0 - entity 16 at (266,-632) - is
#         the locked gas-room door; walking onto it starts Barret's own Script 9, which
#         is a struggle with its own repeated inputs rather than a way through.
#   1014  Cait Sith, entity 5, joins here. Before 1014 the same Talk is only a remark.
#   1015  With Cait aboard the way out is either of the two gateways at y 893, then
#         junin2's lin2 - entity 14 at (1267,-568) - out to the open air.
#   1015  junone2. line13, entity 13 at (17452,-2980), is the elevator scene; once it has
#         written 1016 the onward line is line12, entity 12 at (17698,-2980), to junair.
#   1016  junair is on two levels. Arriving from junone2 sets Bank[1][226] bit 6, and
#         while that bit is set idlkd keeps the upper component shut: the only thing to
#         do down there is box0, entity 14, whose Talk raises the lift. With the bit
#         cleared the upper exit air0 - entity 16 at (15391,12189) - opens, and it leads
#         to junair2 whose Barret automatically continues into the gas chamber.
#   1016  junbin5, back as Tifa. The valve and the door are already reviewed in
#         JunonEscape.ps1 and are not repeated here.
#   1017  junone5, the wall. Two ladders, ladl0u entity 8 and lad2 entity 10, are the
#         way down from the native arrival.
#   1020  junone6 and junone4 lead along the cannon to junone7, whose lin0 - entity 12 at
#         (65,516) - starts the confrontation. Either of junone4's two forward gateways
#         reaches it; the third leads back the way the party came.
#   1025  The Highwind. fship_1's gateway0 is the way inside; fship_4's evt1 - entity 8 -
#         is the scene with the others, and after it jump - entity 6 - reaches the
#         cockpit, which writes 1029 on arrival.
#   1029  In the cockpit Red XIII, entity 10, is what moves the story on; the pilot does
#         not, until 1031. Then crew3, entity 17, sets Bank[3][22] bit 3, and only then
#         does the operations crew have anything to say.
#   1033  With the party formed, the pilot offers the first flight.
$tifaIsLeader = New-Condition -Bank 3 -Address 9 -Mask 255 -Value 2
$barretIsLeader = New-Condition -Bank 3 -Address 9 -Mask 255 -Value 1
$cidIsLeader = New-Condition -Bank 3 -Address 9 -Mask 255 -Value 8
$junonLiftDown = New-Condition -Bank 1 -Address 226 -Mask 0x40 -Value 0x40
$junonLiftRaised = New-Condition -Bank 1 -Address 226 -Mask 0x40 -Value 0x00
$highwindPilotSpoken = New-Condition -Bank 3 -Address 22 -Mask 0x08 -Value 0x08
$highwindPilotUnspoken = New-Condition -Bank 3 -Address 22 -Mask 0x08 -Value 0x00

# --- The cell, and the press room ------------------------------------------------------
Add-Definition -FieldId 400 -FieldName 'junbin3' -Kind Model -EntityId 5 -Priority 0 `
    -Label 'Talk to Barret' `
    -MinimumGameMoment 999 -MaximumGameMoment 1009 `
    -RequiredCondition $tifaIsLeader `
    -EntityName 'ba' -ScriptType 'Talk'

Add-Definition -FieldId 401 -FieldName 'junbin4' -Kind Location -EntityId 16 -Priority 0 `
    -Label 'Try the locked door' -X 304 -Y -638 -Z 21 `
    -MinimumGameMoment 1010 -MaximumGameMoment 1013 `
    -RequiredCondition $barretIsLeader `
    -EntityName 'lin0' -ScriptType 'Go' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
    -RequiredEnabledLineEntityId 16 `
    -TriggerLine ([ordered]@{ startX=266; startY=-632; startZ=21; endX=342; endY=-644; endZ=21 })

Add-Definition -FieldId 401 -FieldName 'junbin4' -Kind Model -EntityId 5 -Priority 0 `
    -Label 'Talk to Cait Sith' `
    -MinimumGameMoment 1014 -MaximumGameMoment 1014 `
    -RequiredCondition $barretIsLeader `
    -EntityName 'ketcy' -ScriptType 'Talk'

Add-Definition -FieldId 401 -FieldName 'junbin4' -Kind Location -Priority 0 `
    -Label 'Leave the press room' -X -42 -Y 893 -Z 0 `
    -MinimumGameMoment 1015 -MaximumGameMoment 1015 `
    -RequiredCondition $barretIsLeader `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-86; startY=893; startZ=0; endX=2; endY=893; endZ=0 })

Add-Definition -FieldId 389 -FieldName 'junin2' -Kind Location -EntityId 14 -Priority 0 `
    -Label 'Take the passage out to the airfield road' -X 1440 -Y -568 -Z -97 `
    -MinimumGameMoment 1015 -MaximumGameMoment 1015 `
    -RequiredCondition $barretIsLeader `
    -EntityName 'lin2' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 14 `
    -TriggerLine ([ordered]@{ startX=1267; startY=-568; startZ=-97; endX=1613; endY=-568; endZ=-97 })

# --- The road, the lift and the airfield --------------------------------------------------
Add-Definition -FieldId 411 -FieldName 'junone2' -Kind Location -EntityId 13 -Priority 0 `
    -Label 'Go on towards the elevator' -X 17424 -Y -2694 -Z -1521 `
    -MinimumGameMoment 1015 -MaximumGameMoment 1015 `
    -RequiredCondition $barretIsLeader `
    -EntityName 'lin1' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 13 `
    -TriggerLine ([ordered]@{ startX=17452; startY=-2980; startZ=-1522; endX=17396; endY=-2408; endZ=-1521 })

Add-Definition -FieldId 411 -FieldName 'junone2' -Kind Location -EntityId 12 -Priority 0 `
    -Label 'Carry on to the airfield' -X 17733 -Y -2858 -Z -1522 `
    -MinimumGameMoment 1016 -MaximumGameMoment 1016 `
    -RequiredCondition $barretIsLeader `
    -EntityName 'lin0' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 12 `
    -TriggerLine ([ordered]@{ startX=17698; startY=-2980; startZ=-1522; endX=17768; endY=-2737; endZ=-1522 })

Add-Definition -FieldId 384 -FieldName 'junair' -Kind Model -EntityId 14 -Priority 0 `
    -Label 'Talk at the lift controls to raise it' `
    -MinimumGameMoment 1016 -MaximumGameMoment 1016 `
    -RequiredCondition $junonLiftDown `
    -CompletedCondition $junonLiftRaised `
    -EntityName 'box0' -ScriptType 'Talk'

Add-Definition -FieldId 384 -FieldName 'junair' -Kind Location -EntityId 16 -Priority 0 `
    -Label 'Cross the airfield to the plane' -X 16512 -Y 11833 -Z 5119 `
    -MinimumGameMoment 1016 -MaximumGameMoment 1016 `
    -RequiredCondition $junonLiftRaised `
    -EntityName 'air0' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 16 `
    -TriggerLine ([ordered]@{ startX=15391; startY=12189; startZ=5119; endX=17633; endY=11478; endZ=5119 })

# --- Down the wall and along the cannon ----------------------------------------------------
Add-Definition -FieldId 414 -FieldName 'junone5' -Kind Location -EntityId 8 -Priority 0 `
    -Label 'Climb down the wall' -X 420 -Y 2930 -Z 6898 `
    -MinimumGameMoment 1017 -MaximumGameMoment 1019 `
    -RequiredCondition $tifaIsLeader `
    -EntityName 'ladl0u' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 8 `
    -TriggerLine ([ordered]@{ startX=392; startY=2930; startZ=6898; endX=448; endY=2930; endZ=6898 })

Add-Definition -FieldId 414 -FieldName 'junone5' -Kind Location -EntityId 10 -Priority 1 `
    -Label 'Take the lower ladder on down' -X 37 -Y 2030 -Z 5633 `
    -MinimumGameMoment 1017 -MaximumGameMoment 1019 `
    -RequiredCondition $tifaIsLeader `
    -EntityName 'lad2' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 10 `
    -TriggerLine ([ordered]@{ startX=-131; startY=2017; startZ=5633; endX=205; endY=2043; endZ=5633 })

Add-Definition -FieldId 415 -FieldName 'junone6' -Kind Location -Priority 0 `
    -Label 'Cross the cannon foundation' -X 3 -Y -15314 -Z 6932 `
    -MinimumGameMoment 1020 -MaximumGameMoment 1021 `
    -RequiredCondition $tifaIsLeader `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=137; startY=-15314; startZ=6931; endX=-131; endY=-15314; endZ=6933 })

Add-Definition -FieldId 413 -FieldName 'junone4' -Kind Location -Priority 0 `
    -Label 'Follow the cannon forward' -X 107 -Y -20726 -Z 6279 `
    -MinimumGameMoment 1020 -MaximumGameMoment 1021 `
    -RequiredCondition $tifaIsLeader `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=212; startY=-20726; startZ=6260; endX=2; endY=-20726; endZ=6298 })

Add-Definition -FieldId 416 -FieldName 'junone7' -Kind Location -EntityId 12 -Priority 0 `
    -Label 'Go on to meet Scarlet' -X -55 -Y 516 -Z 42 `
    -MinimumGameMoment 1020 -MaximumGameMoment 1021 `
    -RequiredCondition $tifaIsLeader `
    -EntityName 'lin0' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 12 `
    -TriggerLine ([ordered]@{ startX=65; startY=516; startZ=42; endX=-176; endY=516; endZ=43 })

# --- Aboard the Highwind ---------------------------------------------------------------------
Add-Definition -FieldId 66 -FieldName 'fship_1' -Kind Location -Priority 0 `
    -Label 'Go inside the airship' -X 4 -Y -693 -Z -1032 `
    -MinimumGameMoment 1025 -MaximumGameMoment 1026 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-28; startY=-707; startZ=-1032; endX=36; endY=-679; endZ=-1032 })

Add-Definition -FieldId 74 -FieldName 'fship_4' -Kind Location -EntityId 8 -Priority 0 `
    -Label 'Go through to the others' -X -369 -Y -820 -Z -442 `
    -MinimumGameMoment 1025 -MaximumGameMoment 1026 `
    -EntityName 'evt1' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 8 `
    -TriggerLine ([ordered]@{ startX=-369; startY=-880; startZ=-442; endX=-369; endY=-760; endZ=-442 })

Add-Definition -FieldId 74 -FieldName 'fship_4' -Kind Location -EntityId 6 -Priority 0 `
    -Label 'Go forward to the cockpit' -X 997 -Y -820 -Z -417 `
    -MinimumGameMoment 1027 -MaximumGameMoment 1028 `
    -EntityName 'jump' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 6 `
    -TriggerLine ([ordered]@{ startX=1016; startY=-765; startZ=-401; endX=978; endY=-876; endZ=-433 })

Add-Definition -FieldId 70 -FieldName 'fship_23' -Kind Model -EntityId 10 -Priority 0 `
    -Label 'Talk to Red XIII' `
    -MinimumGameMoment 1029 -MaximumGameMoment 1030 `
    -EntityName 'red' -ScriptType 'Talk'

Add-Definition -FieldId 70 -FieldName 'fship_23' -Kind Model -EntityId 17 -Priority 0 `
    -Label 'Talk to the pilot' `
    -MinimumGameMoment 1031 -MaximumGameMoment 1032 `
    -RequiredCondition $highwindPilotUnspoken `
    -CompletedCondition $highwindPilotSpoken `
    -EntityName 'crew3' -ScriptType 'Talk'

Add-Definition -FieldId 70 -FieldName 'fship_23' -Kind Location -EntityId 6 -Priority 1 `
    -Label 'Go back out of the cockpit' -X -478 -Y -3835 -Z -809 `
    -MinimumGameMoment 1031 -MaximumGameMoment 1032 `
    -RequiredCondition $highwindPilotSpoken `
    -EntityName 'jump' -ScriptType '[OK]' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
    -RequiredEnabledLineEntityId 6 `
    -TriggerLine ([ordered]@{ startX=-405; startY=-4063; startZ=-809; endX=-552; endY=-3608; endZ=-809 })

Add-Definition -FieldId 74 -FieldName 'fship_4' -Kind Location -Priority 0 `
    -Label 'Go through to the operations room' -X -12 -Y 224 -Z -440 `
    -MinimumGameMoment 1031 -MaximumGameMoment 1032 `
    -RequiredCondition $highwindPilotSpoken `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-130; startY=236; startZ=-441; endX=105; endY=213; endZ=-439 })

Add-Definition -FieldId 73 -FieldName 'fship_3' -Kind Model -EntityId 12 -Priority 0 `
    -Label 'Talk to the crew to choose the party' `
    -MinimumGameMoment 1031 -MaximumGameMoment 1032 `
    -RequiredCondition $highwindPilotSpoken `
    -EntityName 'crew' -ScriptType 'Talk'

Add-Definition -FieldId 73 -FieldName 'fship_3' -Kind Location -Priority 0 `
    -Label 'Go back to the corridor' -X -2 -Y -450 -Z 0 `
    -MinimumGameMoment 1033 -MaximumGameMoment 1034 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-84; startY=-450; startZ=0; endX=80; endY=-450; endZ=0 })

Add-Definition -FieldId 70 -FieldName 'fship_23' -Kind Model -EntityId 17 -Priority 0 `
    -Label 'Talk to the pilot to take off' `
    -MinimumGameMoment 1033 -MaximumGameMoment 1034 `
    -EntityName 'crew3' -ScriptType 'Talk'

# --- Cid takes the Highwind: the party formation, and the first takeoff ----------------
#
# 70's director 4 script 6 calls 5's script 1, makes entity 14 the controlled actor and
# returns Movable at byte 5, so by 1108 the appointment scene has ended and Cid is standing
# at (6,-2588,-809) on triangle 99 with control and nothing said to him.
#
# The leader gate is 3[9], which is what the scripts test: 73's crew 12 Talk branches on it
# at byte 4 for Tifa and at byte 83 for Cid, and the two branches want different moments.

# The pilot only congratulates Cid until the party has been chosen - 70's crew3 Talk falls
# through byte 247 while the moment is below 1110 - so the step here is out of the cockpit.
# The gateway carrying this geometry is disabled; the way out is entity 6's own [OK], which
# is a press made on the line rather than a crossing of it.
Add-Definition -FieldId 70 -FieldName 'fship_23' -Kind Location -EntityId 6 -Priority 0 `
    -Label 'Go back out of the cockpit' -X -478 -Y -3835 -Z -809 `
    -MinimumGameMoment 1108 -MaximumGameMoment 1109 `
    -RequiredCondition $cidIsLeader `
    -EntityName 'jump' -ScriptType '[OK]' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
    -RequiredEnabledLineEntityId 6 `
    -TriggerLine ([ordered]@{ startX=-405; startY=-4063; startZ=-809; endX=-552; endY=-3608; endZ=-809 })

Add-Definition -FieldId 74 -FieldName 'fship_4' -Kind Location -Priority 0 `
    -Label 'Go through to the operations room' -X -12 -Y 224 -Z -440 `
    -MinimumGameMoment 1108 -MaximumGameMoment 1109 `
    -RequiredCondition $cidIsLeader `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-130; startY=236; startZ=-441; endX=105; endY=213; endZ=-439 })

# 73's crew 12 Talk from byte 83: leader 8 and a moment below 1110, writes 1110 at byte 113
# and calls MENU7 at 118 - the party formation menu - then hands control back at byte 183.
# The choice inside that menu is the player's; nothing here makes it. 73's bunki1 Main
# restores control at byte 26 for any moment below 1195, so the room is theirs to walk.
Add-Definition -FieldId 73 -FieldName 'fship_3' -Kind Model -EntityId 12 -Priority 0 `
    -Label 'Talk to the crew to choose the party' `
    -MinimumGameMoment 1108 -MaximumGameMoment 1109 `
    -RequiredCondition $cidIsLeader `
    -EntityName 'crew' -ScriptType 'Talk'

# From 1110 the way back to the pilot is the objective, and it stays the objective: the
# takeoff maps to the world map and the moment does not move again until the missions move
# it, so the same three rooms are walked for the 1116 choice as for the first one.
Add-Definition -FieldId 73 -FieldName 'fship_3' -Kind Location -Priority 0 `
    -Label 'Go back to the corridor' -X -2 -Y -450 -Z 0 `
    -MinimumGameMoment 1110 -MaximumGameMoment 1116 `
    -RequiredCondition $cidIsLeader `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-84; startY=-450; startZ=0; endX=80; endY=-450; endZ=0 })

# 74's entity 6 Move maps 70 while the moment is below 1199, which is the whole of this
# stretch. Past that the same line belongs to the Cloud recovery and goes to 72 instead,
# which is why this row stops where it does.
Add-Definition -FieldId 74 -FieldName 'fship_4' -Kind Location -EntityId 6 -Priority 0 `
    -Label 'Go forward to the cockpit' -X 997 -Y -820 -Z -417 `
    -MinimumGameMoment 1110 -MaximumGameMoment 1116 `
    -RequiredCondition $cidIsLeader `
    -EntityName 'jump' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 6 `
    -TriggerLine ([ordered]@{ startX=1016; startY=-765; startZ=-401; endX=978; endY=-876; endZ=-433 })

# With the party chosen the pilot stops congratulating and starts asking. 70's crew3 Talk
# sends any moment of 1110 or more past byte 247 into the takeoff branch, which reads the
# player's answer out of bank 5[0] and maps the world at byte 316; 1116 has its own
# equivalent branch at byte 4 and maps the world at byte 59. The row is the pilot, not the
# answer.
Add-Definition -FieldId 70 -FieldName 'fship_23' -Kind Model -EntityId 17 -Priority 0 `
    -Label 'Talk to the pilot to take off' `
    -MinimumGameMoment 1110 -MaximumGameMoment 1116 `
    -RequiredCondition $cidIsLeader `
    -EntityName 'crew3' -ScriptType 'Talk'

Add-CuratedFields 400, 401, 389, 411, 384, 414, 415, 413, 416, 66, 74, 70, 73
