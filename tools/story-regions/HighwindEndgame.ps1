# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
#
# The last stretch aboard the Highwind, between the Forgotten Capital and the crater.
# Add the walked corridor/deck steps between automatic scenes. Earlier flight
# rows already expire at their TargetGameMoment; they do not remain active here.
#
# What the installed scripts do, and where that leaves the player:
#
#   1566  blin70_4's event Script 1 writes 1566 and map jumps back to loslake1, whose
#         gateway0 out of the lake is already covered. From the world map the party
#         boards the Highwind and arrives inside it; fship_4 is the corridor everything
#         else opens off. Its jump line, entity 6, branches on the counter at byte 22 -
#         below 1199 it enters the cockpit and above it the deck - so at 1566 that line
#         is the way through to fship_25.
#         Nothing on the deck is a Talk the player has to find: fship_25's bunki Main
#         falls into the branch at byte 44 and runs direct Script 3, which ends at byte
#         193 by calling Cait Sith's own Talk with the player still frozen.
#         MidgarRaid.ps1 already suppresses the extracted row for that Talk and this
#         region does not put one back.
#   1580  md8_52's dir3 hands the party back to the deck and fship_25's direct Script 4
#         writes 1580 at byte 55 and returns control there. This time the deck really is
#         waiting for something: bunki Main takes the branch at byte 83 into direct
#         Script 1, which places everyone and sets Movable at byte 48 without calling
#         anybody's Talk. crew3 - entity 16 - is the pilot, and his Talk is a takeoff
#         question at every stage of the flight; the branch for this one starts at byte
#         266 and maps the world at byte 446. The scenes that follow are run by the world
#         map and by the fields it jumps to, so nothing from 1582 to 1599 is walked.
#   1612  hill2's Director writes 1612 and map jumps to fship_4, and the corridor then
#         runs itself: 74:1's Main calls 74:2's Script 4, which at byte 63 calls Cloud's
#         own Script 10 - the one that writes 1614 and map jumps to fship_2, where the
#         departure and the write of 1620 also run themselves. The player is frozen from
#         the first byte of that chain to the last. MidgarRaid.ps1 suppresses the
#         extracted row for the line in that corridor and nothing is authored here.
#
# The deck's other party members are all live at these moments and say their own lines;
# none of them is a step and none is claimed.

$highwindWeaponReport = @{ MinimumGameMoment = 1566; MaximumGameMoment = 1567; Priority = 0 }
$highwindTakeoff = @{ MinimumGameMoment = 1580; MaximumGameMoment = 1581; Priority = 0 }

# The corridor's forward line, which is the way through to the deck at both of the
# moments the deck is where the story is. At 1580 the party is already standing there
# and this is the way back if the player has walked off it.
foreach ($highwindDeckTrip in @($highwindWeaponReport, $highwindTakeoff)) {
    Add-Definition @highwindDeckTrip -FieldId 74 -FieldName 'fship_4' -Kind Location -EntityId 6 `
        -Label 'Go forward to the deck' -X 997 -Y -820 -Z -417 `
        -EntityName 'jump' -ScriptType 'Move' -UsesPlayerCollisionRadius `
        -RequiredEnabledLineEntityId 6 `
        -TriggerLine ([ordered]@{ startX = 1016; startY = -765; startZ = -401; endX = 978; endY = -876; endZ = -433 })
}

Add-Definition @highwindTakeoff -FieldId 72 -FieldName 'fship_25' -Kind Model -EntityId 16 `
    -Label 'Talk to the pilot to take off' `
    -EntityName 'crew3' -ScriptType 'Talk'

# The generic first-flight row already expires at TargetGameMoment1027. Give
# it the same lower bound as the reviewed first-flight row as well.
foreach ($highwindUnboundedRow in $definitions) {
    if ($highwindUnboundedRow.fieldId -eq 74 -and $highwindUnboundedRow.targetGameMoment -eq 1027 -and
        $highwindUnboundedRow.minimumGameMoment -lt 0 -and $highwindUnboundedRow.maximumGameMoment -lt 0) {
        $highwindUnboundedRow.minimumGameMoment = 1025
        $highwindUnboundedRow.maximumGameMoment = 1026
    }
}

# Neither field is claimed wholesale. fship_25's other extracted rows belong to the
# chapters that wrote them and none of them shares a trigger with the rows above, so
# there is nothing here for the supersede pass to do; fship_4 is already curated by
# JunonEscapeAndHighwind.ps1 and stays exactly as that region left it.
