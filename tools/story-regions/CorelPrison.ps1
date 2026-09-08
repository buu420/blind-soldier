# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
#
# Corel Prison had no catalog rows at all - all fourteen fields - which is where the
# player's guidance stopped. The chapter is almost entirely transit and scene entry,
# so generic GameMoment-write extraction could never find it: only four of its fields
# write the moment, and three of those four writes happen in a Director Main that
# fires on arrival rather than from anything the player can be sent to.
#
# Native route, from the installed field scripts and gateway tables. Evidence dumps
# are in .active-build-main-story-20260907/evidence/.
#
#   445  clsin2_3 drops the party into jail1
#   446  jail1/dic Main plays the arrival, then the party must find Barret
#   448  jailin2/dic Main fires on entry at 446 and starts the Corel flashback
#   451  corel1/dyn, 454 mtcrl_3/direct - the flashback, entirely automatic
#   457  jailin2/dic Main resumes; Barret has gone after Dyne
#   463  dyne/balette Script 19, which map-jumps straight to jailin4
#   466  jailin4/dic Main fires on arrival at 463, then gldelev to crcin_2
#   467  crcin_2/dic Main, then 469 crcin_2/esto Main - both automatic
#
# So the player-driven steps are 446 (reach the town, enter the house) and 457 to 462
# (cross the desert to Dyne). Everything from 463 is a scene chain, and nothing is
# offered against it.


# jail1, jail3 and jail4 each call MPJPO in their Director Init, unconditionally, so
# every static gateway in those three fields is dead. Their real crossings are the
# scripted LINE entities below, and routing to a disabled gateway's endpoint would
# send the player to a door that does nothing. jail2 does not call MPJPO, so its
# gateways are the doors there.
#
# Every scripted crossing also carries the native contract: the Go handler wants the
# player inside the model's own collision radius of the LINE (FUN_00637ABB), and a
# LINE that the field has switched off is not a way anywhere.

# jail1/jl1..jl9 Go 1x each test Bank[2][0] < 454 and refuse with Cloud saying the
# party should find Barret first. That gate is why the town, not the desert, is the
# objective here.
Add-Definition -FieldId 471 -FieldName 'jail1' -Kind Location -EntityId 15 `
    -Label 'Go into the prison town to find Barret' -X -128 -Y -164 -Z 0 `
    -MinimumGameMoment 446 -MaximumGameMoment 447 -Priority 0 `
    -EntityName 'jl4' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 15 `
    -TriggerLine ([ordered]@{ startX=-96; startY=-176; startZ=0; endX=-161; endY=-153; endZ=0 })

# jail2 gateway0 and gateway1 are the two doors of the same house. jailin2/dic Main
# runs the whole Barret scene the moment it is entered at 446, so either door is the
# objective and neither is a prerequisite for the other.
Add-Definition -FieldId 473 -FieldName 'jail2' -Kind Location `
    -Label 'Enter the house where Barret is' -X 586 -Y -52 -Z 18 `
    -MinimumGameMoment 446 -MaximumGameMoment 447 -Priority 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=584; startY=-20; startZ=18; endX=589; endY=-85; endZ=18 })

Add-Definition -FieldId 473 -FieldName 'jail2' -Kind Location `
    -Label 'Enter the house where Barret is by its other door' -X 842 -Y -133 -Z 73 `
    -MinimumGameMoment 446 -MaximumGameMoment 447 -Priority 0 `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=824; startY=-159; startZ=73; endX=860; endY=-108; endZ=73 })

# The prison town's other native doors. They are visible and reachable throughout the
# chapter, and none of them is on the route, so they never outrank the objective.
Add-Definition -FieldId 473 -FieldName 'jail2' -Kind Location `
    -Label 'Visit the prison bar (optional)' -X 576 -Y -1622 -Z 0 `
    -MinimumGameMoment 446 -MaximumGameMoment 462 -Priority 1 `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=552; startY=-1624; startZ=0; endX=600; endY=-1620; endZ=0 })

# Mr Coates's office is open from the moment the party lands, and the guards outside
# it say to pay respects. A player who goes in before finding Barret must not lose the
# thread, so the office keeps its own way back for the whole chapter.
Add-Definition -FieldId 477 -FieldName 'jailin4' -Kind Location -EntityId 11 `
    -Label 'Leave the office' -X -23 -Y -271 -Z 0 `
    -MinimumGameMoment 446 -MaximumGameMoment 462 -Priority 0 `
    -EntityName 'jl2' -ScriptType 'Go' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 11 `
    -TriggerLine ([ordered]@{ startX=-69; startY=-271; startZ=0; endX=23; endY=-271; endZ=0 })

Add-Definition -FieldId 473 -FieldName 'jail2' -Kind Location `
    -Label "Pay respects at Mr Coates's office (optional)" -X -476 -Y -972 -Z 102 `
    -MinimumGameMoment 446 -MaximumGameMoment 447 -Priority 1 `
    -EntityName 'gateway3' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-502; startY=-953; startZ=102; endX=-451; endY=-991; endZ=102 })

# Native exit LINEs back to the town, so a player who steps into one of the prison
# interiors is never left without a way on. jailin2's own two doors are its exits.
$corelPrisonReturns = @(
    @{ field=474; name='jailpb'; entity=6; line='jl2'; label='Return to the prison town'; x=-47; y=-448;
       geometry=@{ startX=-88; startY=-446; startZ=0; endX=-7; endY=-451; endZ=0 } },
    @{ field=475; name='jailin2'; entity=11; line='jl1'; label='Leave the house'; x=-277; y=-143;
       geometry=@{ startX=-287; startY=-111; startZ=0; endX=-267; endY=-175; endZ=0 } },
    @{ field=475; name='jailin2'; entity=12; line='jl2'; label='Leave the house by its other door'; x=147; y=-251;
       geometry=@{ startX=113; startY=-251; startZ=0; endX=181; endY=-251; endZ=0 } }
)
foreach ($return in $corelPrisonReturns) {
    Add-Definition -FieldId $return.field -FieldName $return.name -Kind Location -EntityId $return.entity `
        -Label $return.label -X $return.x -Y $return.y -Z 0 `
        -MinimumGameMoment 446 -MaximumGameMoment 462 -Priority 0 `
        -EntityName $return.line -ScriptType 'Go' -UsesPlayerCollisionRadius `
        -RequiredEnabledLineEntityId $return.entity -TriggerLine $return.geometry
}

# jailin1's ld runs Cloud's Script 4, which map-jumps back to jail1. The room has no
# gateway table of its own, so this LINE is the only way out of it.
Add-Definition -FieldId 472 -FieldName 'jailin1' -Kind Location -EntityId 4 `
    -Label 'Return to the prison floor' -X 52 -Y 91 -Z 15 `
    -MinimumGameMoment 446 -MaximumGameMoment 462 -Priority 0 `
    -EntityName 'ld' -ScriptType 'Go' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 4 `
    -TriggerLine ([ordered]@{ startX=31; startY=116; startZ=15; endX=74; endY=66; endZ=15 })

# jail2/jl2 is the only way back out to the open prison floor, and the desert is only
# reachable from there.
Add-Definition -FieldId 473 -FieldName 'jail2' -Kind Location -EntityId 8 `
    -Label 'Go back out to the prison floor' -X 632 -Y 3244 -Z 0 `
    -MinimumGameMoment 457 -MaximumGameMoment 462 -Priority 0 `
    -EntityName 'jl2' -ScriptType 'Go' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 8 `
    -TriggerLine ([ordered]@{ startX=575; startY=2539; startZ=0; endX=690; endY=3950; endZ=0 })

# The desert opens at 454, which the flashback's own return write satisfies.
#
# jail1 has three lines into jail3 and they are not interchangeable, which is the whole
# difficulty here: two fields joined by two doors can still be two places. jl1 and jl2
# both arrive at (271,-85), walkmesh triangle 5, and jl3 arrives at (-240,-64),
# triangle 23. Running the installed walkmesh through the route planner settles it -
# triangle 5's whole adjacency component is twenty triangles, 0..7, 9..15, 85, 86 and
# 156..158, and none of them reaches the desert crossing at all. Triangle 23's
# component is 141 triangles and reaches it in 23. So jl3 is the way out and the
# eastern pair is a wrong turn that leaves the player walking a fence they cannot pass.
# An earlier draft of this file had it exactly backwards on the strength of the two
# lines sharing a destination field.
$prisonDesertRoad = @(8) + @(16..84) + @(87..155) + @(159..160)
$prisonWrongSideOfTheFence = @(0..7) + @(9..15) + @(85..86) + @(156..158)

Add-Definition -FieldId 471 -FieldName 'jail1' -Kind Location -EntityId 14 `
    -Label 'Head out into the desert to find Dyne' -X -1640 -Y 3197 -Z 223 `
    -MinimumGameMoment 457 -MaximumGameMoment 462 -Priority 0 `
    -EntityName 'jl3' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 14 `
    -TriggerLine ([ordered]@{ startX=-1795; startY=3126; startZ=164; endX=-1485; endY=3268; endZ=283 })

# jail3's static gateway to jail4 is dead: its Director Init byte 6 is MPJPO. The
# crossing the player actually walks is entity 8's jl3 LINE, whose Go 1x maps to 479 -
# but only from the road it is actually on. Offered from the eastern side it is a walk
# to a place that cannot be reached.
Add-Definition -FieldId 478 -FieldName 'jail3' -Kind Location -EntityId 8 `
    -Label 'Cross the desert toward Dyne' -X 2751 -Y 2829 -Z -61 `
    -MinimumGameMoment 457 -MaximumGameMoment 462 -Priority 0 `
    -EntityName 'jl3' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 8 -RequiredPlayerTriangles $prisonDesertRoad `
    -TriggerLine ([ordered]@{ startX=988; startY=588; startZ=-7; endX=4515; endY=5071; endZ=-115 })

# And the way back for a player who took one of the eastern lines before this was
# corrected, or who walks in from the town by the near gate. jail3's own jl1 returns to
# jail1 at (-677,3511), triangle 262, and the planner reaches the western line from
# there in 29 triangles. Without this the eastern road is simply a dead end with an
# objective on the far side of a fence.
Add-Definition -FieldId 478 -FieldName 'jail3' -Kind Location -EntityId 6 `
    -Label 'Go back to the prison and take the western road' -X 306 -Y -194 -Z -1 `
    -MinimumGameMoment 457 -MaximumGameMoment 462 -Priority 0 `
    -EntityName 'jl1' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 6 -RequiredPlayerTriangles $prisonWrongSideOfTheFence `
    -TriggerLine ([ordered]@{ startX=178; startY=-186; startZ=-3; endX=434; endY=-202; endZ=0 })

# jail4 does the same at byte 4, so both of its gateways are dead too. Entity 5's jl2
# is the real approach to Dyne and entity 4's jl1 is the real way back.
Add-Definition -FieldId 479 -FieldName 'jail4' -Kind Location -EntityId 5 `
    -Label 'Go up to where Dyne is waiting' -X 603 -Y 1302 -Z 0 `
    -MinimumGameMoment 457 -MaximumGameMoment 462 -Priority 0 `
    -EntityName 'jl2' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 5 `
    -TriggerLine ([ordered]@{ startX=318; startY=1306; startZ=0; endX=889; endY=1298; endZ=0 })

Add-Definition -FieldId 479 -FieldName 'jail4' -Kind Location -EntityId 4 `
    -Label 'Go back down the desert path' -X -555 -Y -102 -Z 0 `
    -MinimumGameMoment 446 -MaximumGameMoment 462 -Priority 1 `
    -EntityName 'jl1' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 4 `
    -TriggerLine ([ordered]@{ startX=-403; startY=-898; startZ=0; endX=-707; endY=693; endZ=0 })

# The open desert is a wandering area with its own chocobo encounters. These are the
# native ways back onto the route, so wandering into it is never a dead end.
Add-Definition -FieldId 482 -FieldName 'desert2' -Kind Location -EntityId 6 `
    -Label 'Head back toward Dyne' -X 2261 -Y -770 -Z 1 `
    -MinimumGameMoment 457 -MaximumGameMoment 462 -Priority 0 `
    -EntityName 'jl3' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 6 `
    -TriggerLine ([ordered]@{ startX=2199; startY=-511; startZ=6; endX=2324; endY=-1030; endZ=-3 })

Add-Definition -FieldId 482 -FieldName 'desert2' -Kind Location -EntityId 5 `
    -Label 'Return to the prison' -X 3137 -Y -1268 -Z 0 `
    -MinimumGameMoment 446 -MaximumGameMoment 462 -Priority 1 `
    -EntityName 'jl2' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 5 `
    -TriggerLine ([ordered]@{ startX=3869; startY=-1269; startZ=0; endX=2405; endY=-1267; endZ=0 })

# The chocobo race out of the prison, which is not automatic.
#
# crcin_2's Director advances to 467 and calls esto Script 6, which walks Esther away
# while the player is free to move; Script 7 later brings her back and leaves her
# standing at about (888,-80). Neither of those starts a race. What starts it is
# Esther's own Talk, whose byte 453 is MINIGAME type 1 returning to field 512.
#
# Her Main only handles the result: on a loss it releases the player at byte 333 and
# sets up another attempt, and only the winning branch writes 469 at 527 and jumps to
# the world map at 532. So this row is the first race and every retry, and it stops
# the moment the moment moves past 467.
#
# The target is a Model, so it resolves through the live model table: it appears only
# while Esther is actually visible and it follows her to wherever she is standing.
# Bank 1[48] bit 6 is the optional Ramuh materia in the same room and is deliberately
# not a prerequisite.
Add-Definition -FieldId 512 -FieldName 'crcin_2' -Kind Model -EntityId 9 `
    -Label 'Talk to Esther to start the chocobo race' `
    -MinimumGameMoment 467 -MaximumGameMoment 467 -TargetGameMoment 469 -Priority 0 `
    -EntityName 'esto' -ScriptType 'Talk'

# Reviewed above, including the chocobo-race room the prison is left through. The
# prison is entered once; nothing generic belongs in it.
Add-CuratedFields 471, 472, 473, 474, 475, 476, 477, 478, 479, 480, 481, 482, 512, 513


