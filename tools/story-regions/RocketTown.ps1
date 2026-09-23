# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
#
# Rocket Town, first visit, from the 523 Cosmo Canyon leaves behind to the 566 the sea
# writes after the Tiny Bronco comes down.
#
# The chapter's shape is not the one it looks like from the outside. There is a rocket
# with a cabin at the top of it, and the party does climb up there - but the scene that
# actually moves the story on happens back down in the house, and the rocket interiors
# past that point are a frozen flashback the game plays by itself. Guiding a player up
# into them as though they were rooms to walk would be sending them somewhere the story
# has already taken them.
#
# Which town this is matters, and it is easy to get wrong. Rocket Town has two street
# maps: rckt (557) and rckt2 (551). They share seven gateway lines, including the one
# into the captain's house, so they look interchangeable from the gateway table alone.
# They are not. Every side room's own exit script settles it:
#
#   rkt_i/line1 Go:  IFSW Bank[2][0] < 1308 -> MAPJUMP 557, else MAPJUMP 551
#
# and rkt_w, rktinn1, rktmin1 and rktmin2 all carry the same test. Below 1308 the town
# is 557; 551 is the street of the Huge Materia visit. rckt is also the only one of the
# two with Rufus, Heidegger and Shera loaded, and the only one with a gateway to the
# rocket base at all - so the chapter below is not even walkable from 551.
#
# The route, from the installed scripts, gateway tables and walkmesh:
#
#   523  rckt g2 into the captain's house, 558. Its back door, entity 5, wants a fresh confirm
#        press and below 553 it enters the backyard 552, whose Director writes 535 and
#        plays Shera's introduction.
#   535  552 g0 back to 558; 558's line3, entity 8, out to the town 557; 557 g0 to the
#        rocket base 561; 561's ladd, entity 3, is a confirm press that runs the
#        leader's own climb to 562; 562 g0 into the cabin, 564.
#   538  564's Cid, entity 7, writes 538 at byte 72 and sets Bank[3][130] bit 2 before
#        his question is even asked at 143. Two of the answers leave without the
#        explanation of the rocket; only the one that gives it sets bit 6 at 294. The
#        scene waiting in the house tests for exactly 538 and bit 6, so a row that
#        stopped at the write would strand a player who picked another answer. Cid stays
#        the objective until bit 6.
#   ...  Then back down - the cabin's own door, the ladder, the base, the town - and
#        across either of the house's line1 or line2. That crossing freezes control and
#        runs the whole flashback: 568, 564, 567, 103's abort film, and back to 558,
#        which writes 550 on arrival. None of it is walkable and none of it is offered.
#   550  558's line3 out to 557, where Cid's own Main writes 553 by itself and Shera
#        sends the party back to the house, which writes 557.
#   557  558's back door again - the same confirm press, but above 553 it now enters 774
#        rather than the backyard. 774's lin0, entity 14, is a walk-on that starts the
#        fight, and after it the film, the sky and the sea run by themselves. The sea
#        writes 566.
#
# The rocket base has a two-triangle ledge at the top of its ladder, 107 and 131, that
# is not part of the ground at all. Arriving there from the cabin the field has already
# started a climb down and is waiting for the player to hold Down or turn back; a row
# pointing at the town from up there would be asking for a walk across nothing, so the
# ground rows exclude it and the descent is left to the ladder itself.
#
# Nothing here requires the shop, the inn or the two houses, and nothing requires a
# particular answer to Cid beyond the one the story waits on.

$rocketArrival = @{ MinimumGameMoment = 523; MaximumGameMoment = 534; Priority = 0 }
$rocketToTheCabin = @{ MinimumGameMoment = 535; MaximumGameMoment = 549; Priority = 0 }
$rocketSheraScene = @{ MinimumGameMoment = 538; MaximumGameMoment = 549; Priority = 0 }
$rocketAfterFlashback = @{ MinimumGameMoment = 550; MaximumGameMoment = 552; Priority = 0 }
$rocketTinyBronco = @{ MinimumGameMoment = 553; MaximumGameMoment = 565; Priority = 0 }

$rocketExplained = New-Condition -Bank 3 -Address 130 -Mask 64 -Value 64
$rocketNotExplained = New-Condition -Bank 3 -Address 130 -Mask 64 -Value 0

# The ledge at the top of the base ladder. Two triangles, joined to nothing.
$rocketLadderLedge = @(107, 131)

# --- Arriving in the town ---------------------------------------------------------
# The same line 551 carries as its gateway 1, on the street the game actually loads.
Add-Definition @rocketArrival -FieldId 557 -FieldName 'rckt' -Kind Location `
    -Label "Go into the captain's house" -X 321 -Y 1361 -Z 0 `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=292; startY=1373; startZ=0; endX=351; endY=1349; endZ=0 })

Add-Definition @rocketArrival -FieldId 558 -FieldName 'rktsid' -Kind Location -EntityId 5 `
    -Label 'Press Confirm at the back door into the backyard' -X 18 -Y 898 -Z 0 -TargetGameMoment 535 `
    -EntityName 'jump' -ScriptType '[OK]' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
    -RequiredEnabledLineEntityId 5 `
    -TriggerLine ([ordered]@{ startX=-23; startY=898; startZ=0; endX=60; endY=898; endZ=0 })

# --- Out to the rocket and up to the cabin ----------------------------------------
# The same chain serves the first climb and the walk back after an answer that left
# without the explanation, so it stands until Bank[3][130] bit 6 is set.
Add-Definition @rocketToTheCabin -FieldId 552 -FieldName 'rckt3' -Kind Location `
    -Label "Go back into the captain's house" -X 148 -Y 305 -Z -158 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-322; startY=692; startZ=-160; endX=619; endY=-82; endZ=-157 })

Add-Definition @rocketToTheCabin -FieldId 558 -FieldName 'rktsid' -Kind Location -EntityId 8 `
    -Label 'Go out into the town' -X -199 -Y -12 -Z 0 `
    -RequiredCondition $rocketNotExplained `
    -EntityName 'line3' -ScriptType 'Go' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 8 `
    -TriggerLine ([ordered]@{ startX=-239; startY=-9; startZ=0; endX=-160; endY=-15; endZ=0 })

Add-Definition @rocketToTheCabin -FieldId 557 -FieldName 'rckt' -Kind Location `
    -Label 'Go to the foot of the rocket' -X -141 -Y 2466 -Z 0 `
    -RequiredCondition $rocketNotExplained `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-512; startY=2453; startZ=0; endX=230; endY=2479; endZ=0 })

Add-Definition @rocketToTheCabin -FieldId 561 -FieldName 'rcktbas1' -Kind Location -EntityId 3 `
    -Label 'Press Confirm at the ladder up the gantry' -X -1155 -Y 4977 -Z 688 `
    -RequiredCondition $rocketNotExplained -ExcludedPlayerTriangles $rocketLadderLedge `
    -EntityName 'ladd' -ScriptType 'Go' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
    -RequiredEnabledLineEntityId 3 `
    -TriggerLine ([ordered]@{ startX=-1154; startY=5021; startZ=687; endX=-1157; endY=4934; endZ=690 })

Add-Definition @rocketToTheCabin -FieldId 562 -FieldName 'rcktbas2' -Kind Location `
    -Label 'Go into the cabin' -X -776 -Y 4306 -Z 1939 `
    -RequiredCondition $rocketNotExplained `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-750; startY=4302; startZ=1939; endX=-803; endY=4311; endZ=1939 })

# Cid himself. His Talk writes the moment at byte 72, before his question is even asked
# at 143, and two of the three answers end the conversation without the part the house
# scene is waiting for. So the write is not what finishes this: Bank[3][130] bit 6 is.
# The row deliberately carries no target moment, because a row that named 538 would
# retire itself the instant the write happened and leave a player who picked one of the
# other answers standing in the cabin with nothing to do.
Add-Definition -FieldId 564 -FieldName 'rcktin2' -Kind Model -EntityId 7 `
    -Label 'Talk to the captain in the cabin' `
    -MinimumGameMoment 535 -MaximumGameMoment 549 -Priority 0 `
    -RequiredCondition $rocketNotExplained -CompletedCondition $rocketExplained `
    -EntityName 'cid' -ScriptType 'Talk'

# --- Back down for the scene in the house ------------------------------------------
Add-Definition @rocketSheraScene -FieldId 564 -FieldName 'rcktin2' -Kind Location `
    -Label 'Leave the cabin' -X -119 -Y 2 -Z 0 `
    -RequiredCondition $rocketExplained `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-105; startY=-30; startZ=0; endX=-133; endY=34; endZ=0 })

Add-Definition @rocketSheraScene -FieldId 562 -FieldName 'rcktbas2' -Kind Location -EntityId 5 `
    -Label 'Press Confirm at the ladder back down' -X -1094 -Y 4960 -Z 1939 `
    -RequiredCondition $rocketExplained `
    -EntityName 'ladd' -ScriptType 'Go' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
    -RequiredEnabledLineEntityId 5 `
    -TriggerLine ([ordered]@{ startX=-1093; startY=4984; startZ=1939; endX=-1095; endY=4936; endZ=1939 })

# The four lines out of the rocket base all lead to the same place in the town. None of
# them is offered from the ledge at the top of the ladder, which reaches none of them.
foreach ($baseExit in @(
    @{ entity=7; name='line1'; x=-789; y=2367; line=@{ startX=-380; startY=2629; startZ=0; endX=-1199; endY=2106; endZ=0 } },
    @{ entity=9; name='line3'; x=128;  y=3787; line=@{ startX=-274; startY=4081; startZ=0; endX=530;  endY=3493; endZ=0 } })) {
    Add-Definition @rocketSheraScene -FieldId 561 -FieldName 'rcktbas1' -Kind Location -EntityId $baseExit.entity `
        -Label 'Go back out to the town' -X $baseExit.x -Y $baseExit.y -Z 0 `
        -RequiredCondition $rocketExplained -ExcludedPlayerTriangles $rocketLadderLedge `
        -EntityName $baseExit.name -ScriptType 'Go' -UsesPlayerCollisionRadius `
        -RequiredEnabledLineEntityId $baseExit.entity `
        -TriggerLine $baseExit.line
}

Add-Definition @rocketSheraScene -FieldId 557 -FieldName 'rckt' -Kind Location `
    -Label "Go back into the captain's house" -X 321 -Y 1361 -Z 0 `
    -RequiredCondition $rocketExplained `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=292; startY=1373; startZ=0; endX=351; endY=1349; endZ=0 })

# The crossing itself. Both lines do the same thing and either will do; each tests for
# exactly 538 and bit 6 and then takes control away for the rest of it.
foreach ($crossing in @(
    @{ entity=6; name='line1'; x=-283; y=260; line=@{ startX=-385; startY=258; startZ=0; endX=-181; endY=262; endZ=0 } },
    @{ entity=7; name='line2'; x=0;    y=197; line=@{ startX=-58;  startY=218; startZ=0; endX=58;   endY=177; endZ=0 } })) {
    Add-Definition @rocketSheraScene -FieldId 558 -FieldName 'rktsid' -Kind Location -EntityId $crossing.entity `
        -Label 'Cross the room; Shera is waiting' -X $crossing.x -Y $crossing.y -Z 0 -TargetGameMoment 550 `
        -RequiredCondition $rocketExplained `
        -EntityName $crossing.name -ScriptType 'Move' -UsesPlayerCollisionRadius `
        -RequiredEnabledLineEntityId $crossing.entity `
        -TriggerLine $crossing.line
}

# --- After the flashback ------------------------------------------------------------
# 558's own Director writes 550 on the way back in. Leaving to the town is what starts
# the scene outside, which writes 553 and 557 by itself.
Add-Definition @rocketAfterFlashback -FieldId 558 -FieldName 'rktsid' -Kind Location -EntityId 8 `
    -Label 'Go out into the town' -X -199 -Y -12 -Z 0 -TargetGameMoment 553 `
    -EntityName 'line3' -ScriptType 'Go' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 8 `
    -TriggerLine ([ordered]@{ startX=-239; startY=-9; startZ=0; endX=-160; endY=-15; endZ=0 })

# --- The Tiny Bronco -----------------------------------------------------------------
# The same gate as at the start of the chapter, and the same confirm press, but above
# 553 it opens onto somewhere else entirely.
Add-Definition @rocketTinyBronco -FieldId 558 -FieldName 'rktsid' -Kind Location -EntityId 5 `
    -Label 'Press Confirm at the back door into the backyard' -X 18 -Y 898 -Z 0 `
    -EntityName 'jump' -ScriptType '[OK]' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
    -RequiredEnabledLineEntityId 5 `
    -TriggerLine ([ordered]@{ startX=-23; startY=898; startZ=0; endX=60; endY=898; endZ=0 })

Add-Definition @rocketTinyBronco -FieldId 774 -FieldName 'rckt32' -Kind Location -EntityId 14 `
    -Label 'Go over to where Palmer is' -X -380 -Y 163 -Z -158 -TargetGameMoment 566 `
    -EntityName 'lin0' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 14 `
    -TriggerLine ([ordered]@{ startX=-170; startY=147; startZ=-158; endX=-591; endY=180; endZ=-158 })

Add-Definition @rocketTinyBronco -FieldId 774 -FieldName 'rckt32' -Kind Location -Priority 1 `
    -Label "Go back into the captain's house (optional)" -X 148 -Y 305 -Z -158 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-322; startY=692; startZ=-160; endX=619; endY=-82; endZ=-157 })

# --- The rooms off the street -------------------------------------------------------
# None of these is on the route, and none of them holds a step. What they held before
# was nothing at all: a player who went looking for the captain's house in the item shop
# was told the Story category was empty, and the 2026-09-22 capture has exactly that for
# eleven minutes across rkt_i, rkt_w, rktmin1 and rktmin2.
#
# So each room carries the one thing that is true in it - the way back out to the street
# the chapter is happening on - at a priority below every real step, so it can never be
# offered in place of one. The trigger is the room's own line1 Go, whose MAPJUMP picks
# 557 below Bank[2][0] 1308 and 551 above it; this band is entirely below 1308.
$rocketSideRoom = @{ MinimumGameMoment = 523; MaximumGameMoment = 565; Priority = 1 }

foreach ($sideRoom in @(
    @{ id=553; name='rkt_w';    entity=7; label='Go back out to the town'; x=544;  y=248;  z=0;
       line=@{ startX=541;  startY=219; startZ=0; endX=547;  endY=278; endZ=0 } },
    @{ id=554; name='rkt_i';    entity=7; label='Go back out to the town'; x=2;    y=0;    z=0;
       line=@{ startX=-40;  startY=1;   startZ=0; endX=44;   endY=-1;  endZ=0 } },
    @{ id=555; name='rktinn1';  entity=6; label='Go back out to the town'; x=-277; y=-3;   z=0;
       line=@{ startX=-335; startY=-3;  startZ=0; endX=-219; endY=-3;  endZ=0 } },
    @{ id=559; name='rktmin1';  entity=4; label='Go back out to the town'; x=-31;  y=-41;  z=0;
       line=@{ startX=-65;  startY=-41; startZ=0; endX=3;    endY=-41; endZ=0 } },
    @{ id=560; name='rktmin2';  entity=5; label='Go back out to the town'; x=-530; y=96;   z=0;
       line=@{ startX=-530; startY=57;  startZ=0; endX=-531; endY=136; endZ=0 } })) {
    Add-Definition @rocketSideRoom -FieldId $sideRoom.id -FieldName $sideRoom.name `
        -Kind Location -EntityId $sideRoom.entity -Label $sideRoom.label `
        -X $sideRoom.x -Y $sideRoom.y -Z $sideRoom.z `
        -EntityName 'line1' -ScriptType 'Go' -UsesPlayerCollisionRadius `
        -RequiredEnabledLineEntityId $sideRoom.entity `
        -TriggerLine $sideRoom.line
}

# The inn's bedroom is one further in and leaves by an ordinary gateway rather than a
# line, so it names the room below it rather than the street.
Add-Definition @rocketSideRoom -FieldId 556 -FieldName 'rktinn2' -Kind Location `
    -Label 'Go back down to the inn' -X -307 -Y 659 -Z -87 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-287; startY=619; startZ=-110; endX=-327; endY=700; endZ=-64 })

# The rocket interiors past the cabin - 565, 566, 567, 568, 569 and the abort film in
# 103 - belong to the flashback the house crossing runs and are deliberately left alone;
# they are walked by the story, not by the player.
#
# 551 stays curated and stays empty. It is a real street, but not this one: nothing puts
# the party on it below Bank[2][0] 1308, and the Huge Materia visit is its own chapter.
# The side rooms are deliberately not curated: NativeEntryDoors.ps1 still owns their
# other chapters, and the rows above win on priority wherever both are offered.
Add-CuratedFields 551, 552, 557, 558, 561, 562, 564, 774
