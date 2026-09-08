# The ordinary rooms between the opening and the first visit to Junon.
#
# Every row here is a door, a stair or a native line the player has to walk to, and each
# of them was simply missing: a transit room whose only objective was an NPC who has
# already left, a room the player is put back into with nothing offered, or a branch the
# game allows that had no route at all. None of them is a chapter label; each is the
# actual gateway exit line or LINE opcode from the installed field.

$reactorEntry = @{ Priority = 0 }
$reactorEscape = @{ MinimumGameMoment = 27; MaximumGameMoment = 32; Priority = 0 }
$sectorSevenFirst = @{ MinimumGameMoment = 63; MaximumGameMoment = 68; Priority = 0 }
$sectorSevenMorning = @{ MinimumGameMoment = 108; MaximumGameMoment = 116; Priority = 0 }
$wallMarket = @{ MinimumGameMoment = 191; MaximumGameMoment = 192; Priority = 0 }
$corneoAdmitted = @{ MinimumGameMoment = 192; MaximumGameMoment = 192; Priority = 0 }
$corneoSelection = @{ MinimumGameMoment = 197; MaximumGameMoment = 197; Priority = 0 }
$shinraFloors = @{ MinimumGameMoment = 263; MaximumGameMoment = 263; Priority = 0 }
$afterKalm = @{ MinimumGameMoment = 385; MaximumGameMoment = 386; Priority = 0 }
$mythrilMine = @{ MinimumGameMoment = 387; MaximumGameMoment = 387; Priority = 0 }

# --- Reactor 1, in and out --------------------------------------------------------

# 116:2's Main writes 6 and then puts Barret out of sight. The room's objective after
# that is the gate he went through, not the man.
Add-Definition @reactorEntry -FieldId 116 -FieldName 'md1stin' -Kind Location `
    -Label 'Follow the others out of the station' -X 3665 -Y 29312 -Z 353 `
    -MinimumGameMoment 6 -MaximumGameMoment 7 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 3661; startY = 29256; startZ = 353; endX = 3669; endY = 29368; endZ = 353 })

Add-Definition @reactorEntry -FieldId 118 -FieldName 'md1_2' -Kind Location `
    -Label 'Go on toward the reactor' -X 3034 -Y 32363 -Z 652 `
    -MinimumGameMoment 8 -MaximumGameMoment 9 `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 3037; startY = 32315; startZ = 652; endX = 3031; endY = 32412; endZ = 652 })

Add-Definition @reactorEntry -FieldId 119 -FieldName 'nrthmk' -Kind Location `
    -Label 'Go in through the reactor door' -X 0 -Y -1040 -Z 491 `
    -MinimumGameMoment 10 -MaximumGameMoment 10 `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -75; startY = -1040; startZ = 491; endX = 75; endY = -1040; endZ = 491 })

# The bomb is set and the timer is running; the way out is the door the party came in by.
Add-Definition @reactorEscape -FieldId 125 -FieldName 'nmkin_5' -Kind Location `
    -Label 'Get out of the core' -X -85 -Y -749 -Z -181 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -30; startY = -728; startZ = -181; endX = -141; endY = -771; endZ = -181 })

# 1[225] bits 3, 4 and 5 are the two security doors and Jessie being free. With all
# three set the way out is open and nobody needs talking to again.
Add-Definition @reactorEscape -FieldId 120 -FieldName 'nmkin_1' -Kind Location `
    -Label 'Go out through the security doors' -X -698 -Y 1104 -Z -411 `
    -RequiredCondition (New-Condition 1 225 0x38 0x38) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -861; startY = 1104; startZ = -412; endX = -536; endY = 1104; endZ = -411 })

# 121:5's Main tests the player's walkmesh triangle before it looks at the button, so
# the switch is only usable from triangle 8. While 1[225] bit 0 is set the lift is on
# the lower floor and its own door leads back down rather than out.
Add-Definition @reactorEscape -FieldId 121 -FieldName 'elevtr1' -Kind Location -EntityId 5 `
    -Label 'Stand on the elevator switch plate and press Confirm' -X 86 -Y 64 -Z 5 `
    -CompletionPlayerTriangles @(8) `
    -RequiredCondition (New-Condition 1 225 0x01 0x01) `
    -CompletedCondition (New-Condition 1 225 0x01 0x00) `
    -EntityName 'ele' -ScriptType 'Main'

Add-Definition @reactorEscape -FieldId 119 -FieldName 'nrthmk' -Kind Location `
    -Label 'Go back out the way you came' -X 928 -Y -3021 -Z 491 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 972; startY = -2907; startZ = 491; endX = 884; endY = -3135; endZ = 491 })

Add-Definition @reactorEscape -FieldId 118 -FieldName 'md1_2' -Kind Location `
    -Label 'Go on out of the reactor grounds' -X 3550 -Y 30534 -Z 649 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 3485; startY = 30538; startZ = 649; endX = 3616; endY = 30530; endZ = 649 })

# --- Sector 7, first visit and the morning after ----------------------------------

Add-Definition @sectorSevenFirst -FieldId 156 -FieldName 'mds7plr1' -Kind Location `
    -Label 'Take the left way into the slums' -X -459 -Y -429 -Z 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -495; startY = -192; startZ = 0; endX = -423; endY = -667; endZ = 0 })

# 3[209] bit 3 is Barret standing in the bar doorway after the eviction, and bit 4 is him
# having stepped aside. Until then he is the thing in the way, and talking to him is what
# moves him.
Add-Definition @sectorSevenFirst -FieldId 151 -FieldName 'mds7' -Kind Model -EntityId 22 `
    -Label 'Talk to Barret at the bar door' `
    -RequiredConditions @(
        (New-Condition 3 209 0x08 0x08),
        (New-Condition 3 209 0x10 0x00)) `
    -EntityName 'BARRET' -ScriptType 'Talk'

Add-Definition @sectorSevenFirst -FieldId 151 -FieldName 'mds7' -Kind Location -EntityId 17 `
    -Label 'Go in through the bar door' -X 308 -Y 371 -Z 76 `
    -RequiredCondition (New-Condition 3 209 0x10 0x10) `
    -EntityName 'border4' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 17 `
    -TriggerLine ([ordered]@{ startX = 290; startY = 379; startZ = 76; endX = 326; endY = 364; endZ = 76 })

Add-Definition @sectorSevenMorning -FieldId 154 -FieldName 'mds7pb_1' -Kind Location `
    -Label 'Leave the bar' -X 282 -Y 0 -Z 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 278; startY = -46; startZ = 0; endX = 287; endY = 45; endZ = 0 })

Add-Definition @sectorSevenMorning -FieldId 151 -FieldName 'mds7' -Kind Location `
    -Label 'Take the way back toward the station' -X 1690 -Y -420 -Z 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 1928; startY = -293; startZ = 0; endX = 1451; endY = -548; endZ = 0 })

Add-Definition @sectorSevenMorning -FieldId 156 -FieldName 'mds7plr1' -Kind Location `
    -Label 'Take the right way to the station' -X 484 -Y -339 -Z 0 `
    -EntityName 'gateway3' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 495; startY = -209; startZ = 0; endX = 472; endY = -468; endZ = 0 })

# 146:7's Move is the departure itself: it calls Cloud and the guard and maps the train.
# The guard's own Talk is only dialogue.
Add-Definition @sectorSevenMorning -FieldId 146 -FieldName 'mds7st3' -Kind Location -EntityId 7 `
    -Label 'Board the train' -X -2880 -Y 3370 -Z 101 `
    -EntityName 'border1' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 7 `
    -TriggerLine ([ordered]@{ startX = -2916; startY = 3351; startZ = 101; endX = -2845; endY = 3389; endZ = 101 })

# --- Wall Market ------------------------------------------------------------------

# 1[162] bit 7 is the clerk having named her father; after that the shop has nothing
# more to say and the errand is elsewhere. The same door is the way out again once the
# dress is collected and once Cloud has changed.
Add-Definition @wallMarket -FieldId 201 -FieldName 'mkt_s1' -Kind Location `
    -Label 'Leave the clothes shop' -X 258 -Y -72 -Z -8 `
    -RequiredCondition (New-Condition 1 162 0x80 0x80) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 259; startY = -20; startZ = -8; endX = 258; endY = -125; endZ = -8 })

Add-Definition @wallMarket -FieldId 204 -FieldName 'mktpb' -Kind Location `
    -Label 'Leave the bar for the clothes shop' -X 292 -Y -47 -Z 0 `
    -RequiredCondition (New-Condition 1 161 0x10 0x10) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 293; startY = -91; startZ = 0; endX = 291; endY = -3; endZ = 0 })

# Winning, drawing and losing the squats all leave with a usable wig, so the way out is
# offered on 1[160] bit 7 alone and never asks for another attempt.
Add-Definition @wallMarket -FieldId 197 -FieldName 'mkt_mens' -Kind Location `
    -Label 'Leave the gym' -X -1 -Y -29 -Z 0 `
    -RequiredCondition (New-Condition 1 160 0x80 0x80) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -62; startY = -28; startZ = 0; endX = 60; endY = -31; endZ = 0 })

# --- Corneo's mansion -------------------------------------------------------------

Add-Definition @corneoAdmitted -FieldId 207 -FieldName 'colne_2' -Kind Location `
    -Label 'Take the upper left door' -X -438 -Y 136 -Z 225 `
    -RequiredCondition (New-Condition 3 162 0x07 0x07) `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -426; startY = 80; startZ = 225; endX = -451; endY = 193; endZ = 225 })

# 3[162] bit 3 is set by the escort scene whichever of the three the girls are, so all
# three outcomes leave the basement and reach the central door the same way.
# 207:13's CDLINE is the door-opening line, not the crossing. Its Move does nothing at
# all while 163 bit 0 is set and 162 bit 7 is clear - that is the window in which a
# companion is still down in the basement - and once it has run it disables itself and
# unblocks the way, leaving the actual gateway eighty-four units further north. So the
# line is the objective while it can be used, and the door it opens is the objective
# after that.
Add-Definition @corneoSelection -FieldId 207 -FieldName 'colne_2' -Kind Location -EntityId 13 `
    -Label 'Go through the central door' -X 0 -Y 286 -Z 225 `
    -RequiredCondition (New-Condition 3 163 0x01 0x00) `
    -EntityName 'CDLINE' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 13 `
    -TriggerLine ([ordered]@{ startX = -56; startY = 286; startZ = 225; endX = 57; endY = 286; endZ = 225 })

Add-Definition @corneoSelection -FieldId 207 -FieldName 'colne_2' -Kind Location -EntityId 13 `
    -Label 'Go through the central door' -X 0 -Y 286 -Z 225 `
    -RequiredCondition (New-Condition 3 162 0x80 0x80) `
    -EntityName 'CDLINE' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 13 `
    -TriggerLine ([ordered]@{ startX = -56; startY = 286; startZ = 225; endX = 57; endY = 286; endZ = 225 })

# Once the line has disabled itself these are the only rows left, which is the point:
# the route has to continue across the door rather than end at the line that opened it.
Add-Definition @corneoSelection -FieldId 207 -FieldName 'colne_2' -Kind Location -Priority 1 `
    -Label 'Go on through the opened central door' -X -4 -Y 370 -Z 225 `
    -RequiredCondition (New-Condition 3 163 0x01 0x00) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -68; startY = 370; startZ = 225; endX = 60; endY = 370; endZ = 225 })

Add-Definition @corneoSelection -FieldId 207 -FieldName 'colne_2' -Kind Location -Priority 1 `
    -Label 'Go on through the opened central door' -X -4 -Y 370 -Z 225 `
    -RequiredCondition (New-Condition 3 162 0x80 0x80) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -68; startY = 370; startZ = 225; endX = 60; endY = 370; endZ = 225 })

# While a companion is still waiting in the basement the left door is the way on, and
# the central one genuinely does nothing.
Add-Definition @corneoSelection -FieldId 207 -FieldName 'colne_2' -Kind Location `
    -Label 'Take the upper left door down to the basement' -X -438 -Y 136 -Z 225 `
    -RequiredConditions @(
        (New-Condition 3 163 0x01 0x01),
        (New-Condition 3 162 0x80 0x00)) `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -426; startY = 80; startZ = 225; endX = -451; endY = 193; endZ = 225 })

# 209:20's Go 1x is the rescue itself and it sets 162 bit 7. Walking back upstairs
# without crossing it leaves the story where it was.
Add-Definition @corneoSelection -FieldId 209 -FieldName 'colne_4' -Kind Location -EntityId 20 `
    -Label 'Cross to the stairs where the shouting is' -X 1100 -Y 0 -Z 518 `
    -RequiredConditions @(
        (New-Condition 3 163 0x01 0x01),
        (New-Condition 3 162 0x80 0x00)) `
    -EntityName 'SLINEM' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 20 `
    -TriggerLine ([ordered]@{ startX = 1100; startY = 102; startZ = 518; endX = 1100; endY = -102; endZ = 518 })

# The way back up is right before the selection and right after the rescue, and not in
# between, when the thing to do is down here.
Add-Definition @corneoSelection -FieldId 209 -FieldName 'colne_4' -Kind Location `
    -Label 'Go back up out of the basement' -X 1600 -Y 0 -Z 994 `
    -RequiredCondition (New-Condition 3 163 0x01 0x00) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 1618; startY = 98; startZ = 1011; endX = 1583; endY = -98; endZ = 978 })

Add-Definition @corneoSelection -FieldId 209 -FieldName 'colne_4' -Kind Location `
    -Label 'Go back up out of the basement' -X 1600 -Y 0 -Z 994 `
    -RequiredCondition (New-Condition 3 162 0x80 0x80) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 1618; startY = 98; startZ = 1011; endX = 1583; endY = -98; endZ = 978 })

# --- Corneo's throne room -----------------------------------------------------------
#
# 210:5's Main reads the disguised Cloud's own position and waits for his Y to pass -32
# before the selection runs at all, and 207's gateway puts the party down at (-8,-488)
# on triangle 96. The room is not automatic on entry: the walk up it is the step. The
# completion triangles are every reachable triangle whose three vertices are all past
# that line on the installed walkmesh, so arriving inside one really does satisfy the
# test the script makes, rather than stopping eighty units short of it.
Add-Definition @corneoSelection -FieldId 210 -FieldName 'colne_5' -Kind Location -EntityId 5 `
    -Label 'Walk up to Corneo' -X -10 -Y 183 -Z -34 `
    -RequiredCondition (New-Condition 3 163 0x01 0x00) `
    -CompletionPlayerTriangles @(13, 31, 35, 36, 37, 38, 48, 56, 57, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 82, 100, 101, 102, 103, 104, 105, 106, 107, 108) `
    -EntityName 'AD' -ScriptType 'Main'

# Once the selection has run and the party is back together, the northern door is the
# way on.
Add-Definition @corneoSelection -FieldId 210 -FieldName 'colne_5' -Kind Location `
    -Label 'Take the northern door' -X 2 -Y 485 -Z -34 `
    -RequiredConditions @(
        (New-Condition 3 162 0x80 0x80),
        (New-Condition 3 163 0x01 0x01)) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -55; startY = 486; startZ = -34; endX = 60; endY = 484; endZ = -34 })

# 208:11's Scotch only repeats himself until 5[2] has reached three, and what raises it
# is talking to the men standing round the room. 3[163] bit 3 is the fight being over.
foreach ($lackey in @(13, 14, 15, 16, 17)) {
    Add-Definition @corneoSelection -FieldId 208 -FieldName 'colne_3' -Kind Model -EntityId $lackey `
        -Label "Talk to Corneo's men" `
        -RequiredCondition (New-Condition 3 163 0x08 0x00) `
        -EntityName 'ZAKO' -ScriptType 'Talk'
}

Add-Definition @corneoSelection -FieldId 208 -FieldName 'colne_3' -Kind Location `
    -Label 'Leave the basement room' -X -234 -Y -510 -Z 0 `
    -RequiredCondition (New-Condition 3 163 0x08 0x08) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -277; startY = -464; startZ = 0; endX = -192; endY = -556; endZ = 0 })

# --- Shinra headquarters ----------------------------------------------------------

# 3[179] bit 3 is the mayor having sent the party to read; the books are the player's to
# read or skip, and the way back is the stair they came up.
Add-Definition @shinraFloors -FieldId 243 -FieldName 'blin62_2' -Kind Location `
    -Label 'Go back down to the mayor' -X -134 -Y -781 -Z 0 `
    -RequiredCondition (New-Condition 3 179 0x08 0x08) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -164; startY = -781; startZ = 0; endX = -104; endY = -781; endZ = 0 })

Add-Definition @shinraFloors -FieldId 244 -FieldName 'blin62_3' -Kind Location `
    -Label 'Go back down to the mayor' -X -137 -Y 278 -Z 0 `
    -RequiredCondition (New-Condition 3 179 0x08 0x08) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -169; startY = 278; startZ = 0; endX = -105; endY = 278; endZ = 0 })

# 252 is the room the duct puts the party back into after the conference; its own door
# is the way on, and the earlier vent climb is finished.
Add-Definition -FieldId 252 -FieldName 'blin66_3' -Kind Location -Priority 0 `
    -Label 'Go back out to the corridor' -X 134 -Y 109 -Z -376 `
    -MinimumGameMoment 269 -MaximumGameMoment 269 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 133; startY = 154; startZ = -376; endX = 136; endY = 64; endZ = -376 })

# 263 is not wholly automatic: after the party choice it writes 284, reloads and gives
# control back, and the assistant with the keycard is through this door.
Add-Definition -FieldId 263 -FieldName 'blin68_2' -Kind Location -Priority 0 `
    -Label 'Go out to the laboratory' -X -600 -Y 12 -Z -2 `
    -MinimumGameMoment 284 -MaximumGameMoment 284 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -559; startY = -31; startZ = 0; endX = -642; endY = 55; endZ = -4 })

# --- Kalm, the marshes and the mines -----------------------------------------------

# 3[131] bit 0 is the PHS actually being in hand. Before it the handoff is the objective;
# after it the inn has nothing left and the journey is south.
Add-Definition -FieldId 331 -FieldName 'elminn_1' -Kind Location -Priority 0 `
    -Label 'Leave the inn' -X 335 -Y -553 -Z -1 `
    -MinimumGameMoment 385 -MaximumGameMoment 385 `
    -RequiredCondition (New-Condition 3 131 0x01 0x01) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 382; startY = -551; startZ = -1; endX = 289; endY = -555; endZ = -1 })

Add-Definition -FieldId 335 -FieldName 'elm' -Kind Location -Priority 0 `
    -Label 'Take the southern gate out of Kalm' -X -758 -Y -970 -Z 0 `
    -MinimumGameMoment 385 -MaximumGameMoment 385 `
    -RequiredCondition (New-Condition 3 131 0x01 0x01) `
    -EntityName 'gateway9' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -925; startY = -777; startZ = 0; endX = -592; endY = -1163; endZ = -1 })

# 348's scene finishes and hands control back; one of its four Go 1x lines is the way off
# the marsh. This is not an automatic field.
Add-Definition @afterKalm -FieldId 348 -FieldName 'sichi' -Kind Location -EntityId 7 `
    -Label 'Get off the marsh' -X 446 -Y -1095 -Z 0 `
    -EntityName 'jl1' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 7 `
    -TriggerLine ([ordered]@{ startX = 487; startY = -734; startZ = 0; endX = 405; endY = -1456; endZ = 0 })

Add-Definition @afterKalm -FieldId 350 -FieldName 'psdun_2' -Kind Location `
    -Label 'Go west through the mine' -X -840 -Y 105 -Z 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -856; startY = 173; startZ = 0; endX = -824; endY = 37; endZ = 0 })

Add-Definition @afterKalm -FieldId 351 -FieldName 'psdun_3' -Kind Location `
    -Label 'Go back down to the mine passage' -X -446 -Y -437 -Z 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -540; startY = -422; startZ = 0; endX = -352; endY = -452; endZ = 0 })

# 3[133] bit 0 is set by the Turks conversation the field runs on its own; the western
# door is what is left to walk to.
Add-Definition @mythrilMine -FieldId 349 -FieldName 'psdun_1' -Kind Location `
    -Label 'Take the western way out of the mine' -X 80 -Y 82 -Z -72 `
    -RequiredCondition (New-Condition 3 133 0x01 0x01) `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 106; startY = -7; startZ = -72; endX = 55; endY = 171; endZ = -72 })

Add-Definition @mythrilMine -FieldId 352 -FieldName 'psdun_4' -Kind Location `
    -Label 'Go back to the mine passage' -X 42 -Y -552 -Z -96 `
    -RequiredCondition (New-Condition 3 133 0x01 0x01) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -84; startY = -561; startZ = -96; endX = 168; endY = -543; endZ = -96 })

# --- The Chocobo farm ---------------------------------------------------------------
#
# Buying a lure is optional and those two rows are the only thing either of these rooms
# offered for the whole stretch. The way back out is what a player who has finished in
# there actually needs, whether they bought anything or not.

Add-Definition -FieldId 345 -FieldName 'frcyo' -Kind Location -Priority 0 `
    -Label 'Leave the stable' -X 0 -Y -1193 -Z 0 `
    -MinimumGameMoment 385 -MaximumGameMoment 565 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -125; startY = -1186; startZ = 0; endX = 124; endY = -1200; endZ = 0 })

Add-Definition -FieldId 344 -FieldName 'frmin' -Kind Location -Priority 0 `
    -Label 'Leave the farmhouse' -X 233 -Y -167 -Z 0 `
    -MinimumGameMoment 385 -MaximumGameMoment 565 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 229; startY = -120; startZ = 0; endX = 237; endY = -215; endZ = 0 })

# 343's own way out is south to the world map; the farm's other gateways lead to the
# same map from the fences round it.
Add-Definition -FieldId 343 -FieldName 'farm' -Kind Location -Priority 0 `
    -Label 'Leave the farm for the world map' -X -413 -Y -374 -Z 51 `
    -MinimumGameMoment 385 -MaximumGameMoment 565 `
    -EntityName 'gateway3' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 478; startY = -1313; startZ = 2; endX = -1305; endY = 564; endZ = 100 })

# --- Junon --------------------------------------------------------------------------

# The house asks about resting by itself on every entry and the grandmother has nothing
# to say, so the door is the objective whether the player rested or declined.
Add-Definition -FieldId 431 -FieldName 'prisila' -Kind Location -Priority 0 `
    -Label 'Leave the house' -X -101 -Y -320 -Z 0 `
    -MinimumGameMoment 388 -MaximumGameMoment 394 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -143; startY = -320; startZ = 0; endX = -60; endY = -320; endZ = 0 })

Add-Definition -FieldId 432 -FieldName 'ujun_w' -Kind Location -Priority 0 `
    -Label 'Leave the shop' -X 266 -Y 2 -Z 0 `
    -MinimumGameMoment 388 -MaximumGameMoment 394 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 225; startY = 2; startZ = 0; endX = 307; endY = 2; endZ = 0 })

Add-Definition -FieldId 433 -FieldName 'jumin' -Kind Location -Priority 0 `
    -Label 'Leave the house' -X -336 -Y -199 -Z 0 `
    -MinimumGameMoment 388 -MaximumGameMoment 394 `
    -RequiredCondition (New-Condition 1 129 0x04 0x04) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -336; startY = -245; startZ = 0; endX = -336; endY = -153; endZ = 0 })

# No Add-CuratedFields here on purpose. Nothing above replaces an extracted row: every
# row is a door or a line the extraction never found, so there is nothing to supersede.
# Curating them anyway is not free: with 156 in the curated list the three extracted rows
# that point at Wall Market from that junction at moment 176 disappeared from the
# generated catalog, while the generator still reported the same eleven supersessions.
# Whatever the mechanism, curating a field the region does not actually replace anything
# in can cost coverage, so this region asks for none.
