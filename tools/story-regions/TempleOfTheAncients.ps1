# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
#
# The Temple of the Ancients, from arriving with the Keystone through the collapse and
# the recovery at Gongaga. Almost none of it writes a GameMoment where the player is
# standing, so write extraction found two rows in the whole chapter and the rest of the
# temple said nothing at all.
#
# What the installed scripts do, in order:
#
#   604  jtempl's gateway0 is the way in off the hillside. Inside, jtmpin1's border1 -
#        entity 6 - is a Go that wants a confirm press and runs Cloud's own Script 6,
#        which tests for exactly 604 and map jumps to 603 - the counter is the gate,
#        and the party cannot be at 604 without having brought the Keystone here, so
#        neither row invents a key-item test the field does not make.
#   609  603 and 609 run themselves and hand back in kuro_1, the maze. Its gateway2 is
#        the way on to the rolling corridor, 606. kuro_2 (605) is the ancient room off
#        the maze - heal, save, a chest - and is optional, so it is offered only as the
#        way back rather than as an objective.
#   612  606's own last line writes 612 and then waits, and the vision chain 615 to 618
#        runs itself: 618 is a party-member request on slot 2, not a Talk anyone has to
#        find. The player comes back to 606 and leaves through gateway1 into the clock.
#   618  kuro_4 is the clock room. Its numbered doorways are gateway1, the sixth, and
#        gateway2, the twelfth; which of them is standing on a bridge at any moment is
#        the clock's own business and is read out live rather than written down here.
#        Door VI leads to 610, the chase chamber.
#   ...  610's mapjump line, entity 15, is the doorway out of the chase. It is only
#        walkable once the guard has been caught: Bank[3][233] bit 3. Bit 2 is set by
#        the introduction and means nothing about the door. Below 627 the line enters
#        611; at 627 and above the same line goes to 612 instead, which is why the row
#        stops at 626 and the clock becomes the goal again.
#   ...  611's border3, entity 8, is the far end of the mural hall and starts 613.
#   621  612 offers the miniature temple - already extracted - and after Cait's choice
#        at 627 the way out is gateway0 back to 610, then 610's gateway0 back to the
#        clock, then the twelfth doorway to 616 and the door itself, entity 16.
#   630  The collapse, 600's fall, 601, 775, the dream and the cinematic all run
#        themselves and take the controlled character away and give it back; none of it
#        is walkable and none of it is claimed. Cloud protesting as a child in 601 and
#        775 is an optional Talk while the adult callbacks continue, so no row invents
#        it as a required step.
#   638  gninn hands control back as Cloud without writing 641 - the row that leaves the
#        inn is already extracted and deliberately does not wait for it. Outdoors,
#        gongaga's own scene writes 641, and line4 - entity 11 - is the [OK] that clears
#        Bank[3][132] bit 6, sets Bank[3][207] bit 4 and leaves by world map 17. With
#        that bit clear the same line goes to an ordinary house instead, so the row
#        carries the bit.
$templeGuardCaught = New-Condition -Bank 3 -Address 233 -Mask 8 -Value 8
$gongagaDeparture = New-Condition -Bank 3 -Address 132 -Mask 64 -Value 64

$templeArrival = @{ MinimumGameMoment = 604; MaximumGameMoment = 608; Priority = 0 }
$templeMaze = @{ MinimumGameMoment = 609; MaximumGameMoment = 611; Priority = 0 }
$templeAfterVision = @{ MinimumGameMoment = 613; MaximumGameMoment = 626; Priority = 0 }
$templeAfterAgreement = @{ MinimumGameMoment = 627; MaximumGameMoment = 629; Priority = 0 }

# --- Arriving with the Keystone ----------------------------------------------------
Add-Definition @templeArrival -FieldId 600 -FieldName 'jtempl' -Kind Location `
    -Label 'Go in through the temple door' -X 523 -Y 782 -Z 2662 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 479; startY = 782; startZ = 2662; endX = 567; endY = 782; endZ = 2662 })

Add-Definition @templeArrival -FieldId 602 -FieldName 'jtmpin1' -Kind Location -EntityId 6 `
    -Label 'Stand at the altar and press Confirm to set the Keystone' -X 8 -Y 10 -Z 33 `
    -EntityName 'border1' -ScriptType 'Go' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 6 `
    -TriggerLine ([ordered]@{ startX = -41; startY = 24; startZ = 33; endX = 58; endY = -3; endZ = 33 })

# --- The maze and the rolling corridor ----------------------------------------------
Add-Definition @templeMaze -FieldId 604 -FieldName 'kuro_1' -Kind Location `
    -Label 'Take the passage on towards the rolling corridor' -X 331 -Y 1010 -Z 537 `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 275; startY = 1088; startZ = 537; endX = 388; endY = 932; endZ = 537 })

# The side room is a service stop, not a step: it is offered only as its own way back.
Add-Definition @templeMaze -FieldId 605 -FieldName 'kuro_2' -Kind Location -Priority 1 `
    -Label 'Go back out to the maze' -X 520 -Y 2 -Z 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 515; startY = -74; startZ = 0; endX = 525; endY = 78; endZ = 0 })

# --- Out of the corridor and into the clock ------------------------------------------
Add-Definition @templeAfterVision -FieldId 606 -FieldName 'kuro_3' -Kind Location `
    -Label 'Leave the corridor for the clock room' -X 846 -Y 1413 -Z -296 `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 846; startY = 1335; startZ = -296; endX = 846; endY = 1491; endZ = -296 })

Add-Definition @templeAfterVision -FieldId 607 -FieldName 'kuro_4' -Kind Location `
    -Label 'Cross to doorway six' -X -1 -Y -716 -Z 0 `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -87; startY = -716; startZ = 0; endX = 85; endY = -716; endZ = 0 })

# The doorway out of the chase chamber, once the guard has been caught. Above 626 this
# same line goes back to 612 instead, so the row ends where the native test changes.
Add-Definition @templeAfterVision -FieldId 610 -FieldName 'kuro_7' -Kind Location -EntityId 15 `
    -Label 'Go through the unlocked doorway' -X -26 -Y 417 -Z 423 `
    -RequiredCondition $templeGuardCaught `
    -EntityName 'mapjump' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 15 `
    -TriggerLine ([ordered]@{ startX = 40; startY = 417; startZ = 423; endX = -92; endY = 417; endZ = 423 })

Add-Definition @templeAfterVision -FieldId 611 -FieldName 'kuro_8' -Kind Location -EntityId 8 `
    -Label 'Carry on to the far end of the hall' -X 659 -Y 19 -Z 0 `
    -EntityName 'border3' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 8 `
    -TriggerLine ([ordered]@{ startX = 678; startY = 118; startZ = 0; endX = 640; endY = -80; endZ = 0 })

# --- After the agreement -------------------------------------------------------------
Add-Definition @templeAfterAgreement -FieldId 612 -FieldName 'kuro_82' -Kind Location `
    -Label 'Leave the mural hall' -X -881 -Y -112 -Z 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -1008; startY = -34; startZ = 0; endX = -754; endY = -190; endZ = 0 })

Add-Definition @templeAfterAgreement -FieldId 610 -FieldName 'kuro_7' -Kind Location `
    -Label 'Go back through to the clock room' -X 486 -Y 820 -Z 323 `
    -RequiredCondition $templeGuardCaught `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 421; startY = 791; startZ = 323; endX = 552; endY = 850; endZ = 323 })

Add-Definition @templeAfterAgreement -FieldId 607 -FieldName 'kuro_4' -Kind Location `
    -Label 'Cross to doorway twelve' -X -1 -Y 704 -Z 0 `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -82; startY = 700; startZ = 0; endX = 81; endY = 708; endZ = 0 })

# --- Gongaga, after the collapse ------------------------------------------------------
Add-Definition -FieldId 518 -FieldName 'gongaga' -Kind Location -EntityId 11 `
    -Label 'Take the path out of the village and press Confirm' -X -663 -Y -1359 -Z 17 `
    -MinimumGameMoment 641 -MaximumGameMoment 651 -Priority 0 `
    -RequiredCondition $gongagaDeparture `
    -EntityName 'line4' -ScriptType '[OK]' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
    -RequiredEnabledLineEntityId 11 `
    -TriggerLine ([ordered]@{ startX = -798; startY = -1333; startZ = 17; endX = -529; endY = -1386; endZ = 17 })

Add-CuratedFields 600, 602, 604, 605, 606, 607, 610, 611, 612, 616, 518

# --- The last room, and two rows that never stopped ------------------------------------
#
# Added after a second pass. 616's entity 16 is a field model with its own talk radius,
# and its Talk tests for exactly 627 before it does anything; everything the chapter has
# left runs from there. Extraction had produced a row for it, but took the name from the
# first line of dialogue rather than from what is being walked to, so the catalog was
# telling the player to talk to a party member in a room where the thing to approach is
# the door. The reviewed row below replaces it: same entity, same trigger, a label that
# says what the player does, and the one moment that Talk tests for. The model stays
# visible after the scene, so a wider band would go on offering a conversation that
# returns at its own byte 4 without doing anything.
Add-Definition -FieldId 616 -FieldName 'kuro_12' -Kind Model -EntityId 16 `
    -Label 'Go up to the door at the end of the room' `
    -MinimumGameMoment 627 -MaximumGameMoment 627 -Priority 0 `
    -EntityName 'boss' -ScriptType 'Talk'

# 606's last line and 612's border2 were both extracted with no band at all, so each went
# on being offered for the rest of the game in a room the chapter comes back to. 606's
# line writes 612 and the party is only in that corridor for 609..611; 612's border2
# writes 624 and stops doing anything at all once the counter reaches 627, which is the
# branch at its byte 8.
foreach ($templeUnboundedRow in $definitions) {
    if ($templeUnboundedRow.minimumGameMoment -ge 0 -or $templeUnboundedRow.maximumGameMoment -ge 0) { continue }
    if ($templeUnboundedRow.fieldId -eq 606 -and $templeUnboundedRow.targetGameMoment -eq 612) {
        $templeUnboundedRow.minimumGameMoment = 609
        $templeUnboundedRow.maximumGameMoment = 612
    }
    if ($templeUnboundedRow.fieldId -eq 612 -and $templeUnboundedRow.targetGameMoment -eq 624) {
        $templeUnboundedRow.minimumGameMoment = 621
        $templeUnboundedRow.maximumGameMoment = 626
    }
}
