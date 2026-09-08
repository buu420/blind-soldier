# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
#
# The underwater reactor and the submarine, from Cloud taking the Highwind back to Junon
# to the cargo plane home. Two of Junon's lifts are walked twice in opposite directions
# and the field decides which way by a bit of its own, so the rows carry that bit rather
# than assuming a direction.
#
#   1250  428's guard, entity 18, takes the ten-gil fare. Up in the lift, junin7's
#         border1 - entity 7 - is the confirm that raises it, and junele2's border1 -
#         entity 4 - is the way out to the streets while Bank[3][235] bit 0 is clear.
#   1250  junin3's border3, entity 6, is the marching soldiers, and writes 1253; after
#         that its border2, entity 5, goes on to the elevator.
#   1256  Coming back down: junin6's mapjump, entity 5, then the same two lifts the other
#         way - junele2's border2 and junin7's border1 - both now with the bit set.
#   ...   semkin_1's lift is a switch and a way on, not one thing. The switch, entity 8,
#         toggles Bank[15][132] bit 0, and entity 7's line only goes on with that bit
#         set. Once it is down the switch is not asked for again.
#   ...   semkin_5's Reno is standing there with an empty Talk. What actually starts the
#         encounter is the line, entity 12, while Bank[15][133] bit 1 is clear.
#   1283  subin_1b's three seat lines each open the submarine's own menu. Once the
#         mission is over - Bank[13][80] bit 0 - they are not the way on any more.
#   1299  The way home is junair's glin, entity 15, the cargo plane, while Bank[1][232]
#         bit 2 is clear. The old escape line air0 belongs to an earlier chapter and is
#         not offered here. After the plane has gone, gateway0 leads back into town.
#   ...   A lost submarine still has to be replaced before the Key. junin4's dog, entity
#         16, is what moves aside, and only while neither submarine is owned - Bank[13]
#         [80] bit 3 for the gray one and Bank[13][82] bit 2 for the red. The guard
#         beside him only says to speak to the dog. Then junsbd1's border5, entity 9,
#         boards it.
$grayySubmarineOwned = New-Condition -Bank 13 -Address 80 -Mask 0x08 -Value 0x08
$noGraySubmarine = New-Condition -Bank 13 -Address 80 -Mask 0x08 -Value 0x00
$noRedSubmarine = New-Condition -Bank 13 -Address 82 -Mask 0x04 -Value 0x00
$submarineMissionOver = New-Condition -Bank 13 -Address 80 -Mask 0x01 -Value 0x01
$submarineMissionOpen = New-Condition -Bank 13 -Address 80 -Mask 0x01 -Value 0x00
$junonLiftGoingUp = New-Condition -Bank 3 -Address 235 -Mask 0x01 -Value 0x00
$junonLiftGoingDown = New-Condition -Bank 3 -Address 235 -Mask 0x01 -Value 0x01
$reactorSwitchUp = New-Condition -Bank 15 -Address 132 -Mask 0x01 -Value 0x00
$reactorSwitchDown = New-Condition -Bank 15 -Address 132 -Mask 0x01 -Value 0x01
$renoEncounterPending = New-Condition -Bank 15 -Address 133 -Mask 0x02 -Value 0x00
$cargoPlaneWaiting = New-Condition -Bank 1 -Address 232 -Mask 0x04 -Value 0x00
$cargoPlaneGone = New-Condition -Bank 1 -Address 232 -Mask 0x04 -Value 0x04

$reactorApproach = @{ MinimumGameMoment = 1250; MaximumGameMoment = 1252; Priority = 0 }
$reactorDescent = @{ MinimumGameMoment = 1256; MaximumGameMoment = 1282; Priority = 0 }

# --- Down through Junon --------------------------------------------------------------
Add-Definition @reactorApproach -FieldId 428 -FieldName 'junonl2' -Kind Model -EntityId 18 `
    -Label 'Talk to the guard at the lift' `
    -EntityName 'mihari' -ScriptType 'Talk'

Add-Definition @reactorApproach -FieldId 395 -FieldName 'junin7' -Kind Location -EntityId 7 `
    -Label 'Press Confirm to take the lift up' -X -69 -Y -31 -Z 0 `
    -RequiredCondition $junonLiftGoingUp `
    -EntityName 'border1' -ScriptType 'Go' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
    -RequiredEnabledLineEntityId 7 `
    -TriggerLine ([ordered]@{ startX = -99; startY = -8; startZ = 0; endX = -40; endY = -55; endZ = 0 })

Add-Definition @reactorApproach -FieldId 391 -FieldName 'junele2' -Kind Location -EntityId 4 `
    -Label 'Go out into the streets' -X -529 -Y 18 -Z 4 `
    -RequiredCondition $junonLiftGoingUp `
    -EntityName 'border1' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 4 `
    -TriggerLine ([ordered]@{ startX = -517; startY = -58; startZ = 4; endX = -541; endY = 94; endZ = 4 })

Add-Definition @reactorApproach -FieldId 390 -FieldName 'junin3' -Kind Location -EntityId 6 `
    -Label 'Follow the marching soldiers' -X -1269 -Y 2649 -Z 1043 `
    -EntityName 'border3' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 6 `
    -TriggerLine ([ordered]@{ startX = -1479; startY = 2640; startZ = 1042; endX = -1059; endY = 2659; endZ = 1045 })

Add-Definition -FieldId 390 -FieldName 'junin3' -Kind Location -EntityId 5 -Priority 0 `
    -Label 'Take their elevator down' -X -1656 -Y 1105 -Z 862 `
    -MinimumGameMoment 1253 -MaximumGameMoment 1255 `
    -EntityName 'border2' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 5 `
    -TriggerLine ([ordered]@{ startX = -1642; startY = 1145; startZ = 862; endX = -1671; endY = 1066; endZ = 862 })

# --- Back up and down the other way -----------------------------------------------------
Add-Definition @reactorDescent -FieldId 392 -FieldName 'junin4' -Kind Location `
    -Label 'Take the stairs on' -X 191 -Y 1021 -Z 605 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 196; startY = 988; startZ = 605; endX = 187; endY = 1054; endZ = 605 })

Add-Definition @reactorDescent -FieldId 394 -FieldName 'junin6' -Kind Location -EntityId 5 `
    -Label 'Go to the downward lift' -X 2100 -Y -853 -Z 184 `
    -EntityName 'mapjump' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 5 `
    -TriggerLine ([ordered]@{ startX = 2053; startY = -848; startZ = 184; endX = 2148; endY = -858; endZ = 184 })

Add-Definition @reactorDescent -FieldId 391 -FieldName 'junele2' -Kind Location -EntityId 5 `
    -Label 'Take the lift down' -X -59 -Y -38 -Z 4 `
    -RequiredCondition $junonLiftGoingDown `
    -EntityName 'border2' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 5 `
    -TriggerLine ([ordered]@{ startX = -98; startY = -22; startZ = 4; endX = -21; endY = -54; endZ = 4 })

Add-Definition @reactorDescent -FieldId 395 -FieldName 'junin7' -Kind Location -EntityId 6 `
    -Label 'Go on to the underwater entrance' -X -588 -Y 16 -Z 0 `
    -RequiredCondition $junonLiftGoingDown `
    -EntityName 'border1' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 6 `
    -TriggerLine ([ordered]@{ startX = -588; startY = -66; startZ = 0; endX = -588; endY = 98; endZ = 0 })

# --- The reactor -------------------------------------------------------------------------
Add-Definition @reactorDescent -FieldId 420 -FieldName 'semkin_1' -Kind Location -EntityId 8 `
    -Label 'Press Confirm at the lift switch' -X -197 -Y 20 -Z -1 `
    -RequiredCondition $reactorSwitchUp `
    -CompletedCondition $reactorSwitchDown `
    -EntityName 'switch' -ScriptType '[OK]' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
    -RequiredEnabledLineEntityId 8 `
    -TriggerLine ([ordered]@{ startX = -184; startY = 42; startZ = -1; endX = -210; endY = -1; endZ = -1 })

Add-Definition @reactorDescent -FieldId 420 -FieldName 'semkin_1' -Kind Location -EntityId 7 `
    -Label 'Take the lift on down' -X 307 -Y 0 -Z -2 `
    -RequiredCondition $reactorSwitchDown `
    -EntityName 'jump' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 7 `
    -TriggerLine ([ordered]@{ startX = 307; startY = 84; startZ = -2; endX = 307; endY = -84; endZ = -2 })

Add-Definition @reactorDescent -FieldId 425 -FieldName 'semkin_5' -Kind Location -EntityId 12 `
    -Label 'Go on past the crane' -X -1401 -Y -1153 -Z 7 `
    -RequiredCondition $renoEncounterPending `
    -EntityName 'line' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 12 `
    -TriggerLine ([ordered]@{ startX = -1410; startY = -1026; startZ = 7; endX = -1393; endY = -1280; endZ = 7 })

# --- The submarine --------------------------------------------------------------------------
Add-Definition -FieldId 406 -FieldName 'subin_1b' -Kind Location -EntityId 16 -Priority 0 `
    -Label 'Take a seat and press Confirm' -X -99 -Y 194 -Z 0 `
    -MinimumGameMoment 1283 -MaximumGameMoment 1298 `
    -RequiredCondition $submarineMissionOpen `
    -EntityName 'border3' -ScriptType 'Go' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
    -RequiredEnabledLineEntityId 16 `
    -TriggerLine ([ordered]@{ startX = -132; startY = 178; startZ = 0; endX = -66; endY = 211; endZ = 0 })

# --- Home by cargo plane -----------------------------------------------------------------------
Add-Definition -FieldId 384 -FieldName 'junair' -Kind Location -EntityId 15 -Priority 0 `
    -Label 'Approach the cargo plane' -X 13070 -Y 13770 -Z 5139 `
    -MinimumGameMoment 1299 -MaximumGameMoment 1299 `
    -RequiredCondition $cargoPlaneWaiting `
    -CompletedCondition $cargoPlaneGone `
    -EntityName 'glin' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 15 `
    -TriggerLine ([ordered]@{ startX = 12938; startY = 14948; startZ = 5139; endX = 13202; endY = 12593; endZ = 5139 })

Add-Definition -FieldId 384 -FieldName 'junair' -Kind Location -Priority 0 `
    -Label 'Go back towards the town' -X 12496 -Y 14557 -Z 5139 `
    -MinimumGameMoment 1299 -MaximumGameMoment 1299 `
    -RequiredCondition $cargoPlaneGone `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 12480; startY = 14290; startZ = 5139; endX = 12512; endY = 14825; endZ = 5139 })

# --- Replacing a lost submarine -------------------------------------------------------------------
Add-Definition -FieldId 392 -FieldName 'junin4' -Kind Model -EntityId 16 -Priority 0 `
    -Label 'Talk to the dog blocking the dock' `
    -MinimumGameMoment 1289 -MaximumGameMoment 1399 `
    -RequiredConditions @($noGraySubmarine, $noRedSubmarine) `
    -EntityName 'dog' -ScriptType 'Talk'

Add-Definition -FieldId 404 -FieldName 'junsbd1' -Kind Location -EntityId 9 -Priority 0 `
    -Label 'Board the submarine' -X 178 -Y 185 -Z 199 `
    -MinimumGameMoment 1289 -MaximumGameMoment 1399 `
    -RequiredConditions @($noGraySubmarine, $noRedSubmarine) `
    -EntityName 'border5' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 9 `
    -TriggerLine ([ordered]@{ startX = 191; startY = 135; startZ = 199; endX = 165; endY = 235; endZ = 199 })

Add-CuratedFields 428, 395, 391, 390, 392, 394, 420, 425, 406, 384, 404
