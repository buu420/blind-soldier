# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
#
# The present-day visit to Nibelheim, in the same 523..534 window MountNibel.ps1 uses -
# between Cosmo Canyon's exit write of 523 and Rocket Town's backyard write of 535. Nothing
# in that whole leg writes a GameMoment, so extraction offers nothing here and every row is
# curated.
#
# Which field is which, from the entity rosters rather than the names:
#
#   282 nivl    the flashback town: cefiros, hei1, hei2, papa, zangan.
#   283 nivl_2  Cloud and Tifa alone; its kakehu Main tests moments 347 and 1181.
#   284 nivl_3  the present-day town: all nine party entities, dog1, zangan, and the
#               mant/mant2 black capes its own Main shows while 384 < moment <= 677.
#   285 nivl_4  the Lucrecia scene: vincent, luk, houjou, gast.
#
# Only 284 and 285 carry the two world gateways. 284 g6 leads to world location 19,
# "Nibelheim (Town Side)", and g7 to location 43, "Nibelheim (Mt. Nibel Side)" - which is
# the side Mt. Nibel's own entrance at location 44 is on. That one gateway is the whole of
# the required present-day objective in this town.
#
# Everything else here is a way out rather than an errand. The Shinra Mansion is fourteen
# fields, four rooms deep, and a player who walks into it was being told Story: none with
# no thread back. Nothing in it is required: the only gate on the present-day Mount Nibel
# path is Bank[1][232] bit 5, and nvdun1's own mon entity writes it. The safe, the coffins
# and the materia are exploration and stay in Objects and Npcs, where they already are -
# field_objects.json already carries the four chests and two materia in sinin1_2, sinin2_1,
# sinin2_2 and sininb42.
#
# The interior coordinates below are the ones the flashback rows already use for the same
# doors, so the geometry is the geometry that was already reviewed for this mansion. Only
# the fields with no flashback row of their own - the 305, 306, 308 and 310 variants - take
# their midpoints from the gateway table directly, and those gateways are identical to the
# ones their sibling fields use.
$nibelheimPresentVisit = @{ MinimumGameMoment = 523; MaximumGameMoment = 534; Priority = 0 }

# --- The town ---------------------------------------------------------------------
# The mountain side of the world map. The town side, g6, goes back the way the party came.
Add-Definition @nibelheimPresentVisit -FieldId 284 -FieldName 'nivl_3' -Kind Location `
    -Label 'Leave Nibelheim toward Mt. Nibel' -X -39 -Y 3098 -Z 207 `
    -EntityName 'gateway7' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=21; startY=2869; startZ=205; endX=-98; endY=3328; endZ=209 })

# --- The mansion, outward ---------------------------------------------------------
# One row per room, naming the door back toward the entrance hall. Two of the rooms have
# more than one door to the same place; each takes the one the reviewed flashback rows
# already chose - 298 its gateway1, 307 its gateway2.
#
# Every row carries its gateway's own exit line and the midpoint of that line, so the route
# completes by crossing the thing the game reacts to rather than by stopping within a
# generic arrival distance of a point near it.
foreach ($room in @(
    @{ field=297; name='sinin1_1'; entity='gateway0'; label='Leave the Shinra Mansion';
       x=0; y=-18; z=0;
       tl=@{ startX=-66; startY=-16; startZ=0; endX=66; endY=-20; endZ=0 } },
    @{ field=298; name='sinin1_2'; entity='gateway1'; label='Return to the mansion entrance hall';
       x=-335; y=204; z=0;
       tl=@{ startX=-338; startY=262; startZ=0; endX=-331; endY=147; endZ=0 } },
    @{ field=299; name='sinin2_1'; entity='gateway0'; label='Return to the mansion entrance hall';
       x=-305; y=753; z=277;
       tl=@{ startX=-302; startY=822; startZ=277; endX=-307; endY=684; endZ=277 } },
    @{ field=300; name='sinin2_2'; entity='gateway0'; label='Return to the mansion entrance hall';
       x=316; y=745; z=277;
       tl=@{ startX=315; startY=826; startZ=277; endX=318; endY=665; endZ=277 } },
    @{ field=301; name='sinin3'; entity='gateway0'; label='Continue up through the right wing';
       x=215; y=-136; z=718;
       tl=@{ startX=149; startY=-162; startZ=717; endX=281; endY=-109; endZ=720 } },
    @{ field=302; name='sininb1'; entity='gateway0'; label='Climb the spiral stairs to the mansion';
       x=12; y=877; z=226;
       tl=@{ startX=-32; startY=877; startZ=226; endX=56; endY=877; endZ=226 } },
    @{ field=303; name='sininb2'; entity='gateway0'; label='Climb out of the mansion basement';
       x=0; y=-291; z=0;
       tl=@{ startX=246; startY=-269; startZ=0; endX=-246; endY=-312; endZ=0 } },
    @{ field=304; name='sininb31'; entity='gateway1'; label='Continue out of the basement library';
       x=-454; y=-89; z=0;
       tl=@{ startX=-474; startY=-127; startZ=0; endX=-433; endY=-50; endZ=0 } },
    @{ field=305; name='sininb32'; entity='gateway1'; label='Continue out of the basement library';
       x=-454; y=-89; z=0;
       tl=@{ startX=-474; startY=-127; startZ=0; endX=-433; endY=-50; endZ=0 } },
    @{ field=306; name='sininb33'; entity='gateway1'; label='Continue out of the basement library';
       x=-454; y=-89; z=0;
       tl=@{ startX=-474; startY=-127; startZ=0; endX=-433; endY=-50; endZ=0 } },
    @{ field=307; name='sininb41'; entity='gateway2'; label='Go back through the library rooms';
       x=399; y=10; z=0;
       tl=@{ startX=116; startY=38; startZ=0; endX=682; endY=-17; endZ=0 } },
    @{ field=308; name='sininb42'; entity='gateway2'; label='Go back through the library rooms';
       x=399; y=10; z=0;
       tl=@{ startX=116; startY=38; startZ=0; endX=682; endY=-17; endZ=0 } },
    @{ field=309; name='sininb51'; entity='gateway0'; label='Go back out of the inner room';
       x=54; y=804; z=0;
       tl=@{ startX=-33; startY=818; startZ=0; endX=142; endY=790; endZ=0 } },
    @{ field=310; name='sininb52'; entity='gateway0'; label='Go back out of the inner room';
       x=54; y=804; z=0;
       tl=@{ startX=-33; startY=818; startZ=0; endX=142; endY=790; endZ=0 } })) {
    Add-Definition @nibelheimPresentVisit -FieldId $room.field -FieldName $room.name `
        -Kind Location -Label $room.label -X $room.x -Y $room.y -Z $room.z `
        -EntityName $room.entity -ScriptType 'Gateway' -TriggerLine $room.tl
}

# --- The town's own buildings ------------------------------------------------------
# Each of these is a single room off the square with one door back to it, and the user
# walked through every one of them at moment 523 with Story empty. Names are each field's
# own MPNAM rather than guesses: niv_w is the item store, nvmin1_1 a house, niv_cl Cloud's
# house, niv_ti1 Tifa's house. nvmin1_2, nivinn_2 and niv_ti2 are the rooms above them and
# have no MPNAM of their own.
foreach ($room in @(
    @{ field=270; name='niv_w'; entity='gateway0'; label='Leave the item store';
       x=454; y=-373; z=0;
       tl=@{ startX=456; startY=-413; startZ=0; endX=452; endY=-333; endZ=0 } },
    @{ field=271; name='nvmin1_1'; entity='gateway0'; label='Leave this house';
       x=-426; y=241; z=0;
       tl=@{ startX=-420; startY=294; startZ=0; endX=-431; endY=189; endZ=0 } },
    @{ field=272; name='nvmin1_2'; entity='gateway0'; label='Go back downstairs';
       x=-290; y=41; z=-170;
       tl=@{ startX=-289; startY=2; startZ=-166; endX=-291; endY=81; endZ=-173 } },
    @{ field=273; name='nivinn_1'; entity='gateway0'; label='Leave the inn';
       x=-32; y=-641; z=0;
       tl=@{ startX=-112; startY=-641; startZ=0; endX=48; endY=-641; endZ=0 } },
    @{ field=274; name='nivinn_2'; entity='gateway0'; label="Go back down to the inn's entrance";
       x=167; y=-200; z=-97;
       tl=@{ startX=212; startY=-210; startZ=-109; endX=123; endY=-189; endZ=-84 } },
    @{ field=276; name='niv_cl'; entity='gateway0'; label="Leave Cloud's house";
       x=-398; y=11; z=-4;
       tl=@{ startX=-398; startY=62; startZ=-4; endX=-397; endY=-40; endZ=-4 } },
    @{ field=286; name='niv_ti1'; entity='gateway0'; label="Leave Tifa's house";
       x=-410; y=65; z=0;
       tl=@{ startX=-407; startY=201; startZ=0; endX=-413; endY=-70; endZ=0 } },
    @{ field=287; name='niv_ti2'; entity='gateway0'; label='Go back downstairs';
       x=-41; y=-399; z=-131;
       tl=@{ startX=-62; startY=-338; startZ=-152; endX=-20; endY=-459; endZ=-109 } })) {
    Add-Definition @nibelheimPresentVisit -FieldId $room.field -FieldName $room.name `
        -Kind Location -Label $room.label -X $room.x -Y $room.y -Z $room.z `
        -EntityName $room.entity -ScriptType 'Gateway' -TriggerLine $room.tl
}

# --- The optional things in the mansion --------------------------------------------
# They are not Story rows. Every one of them except the entrance-hall note is a native LINE
# handler with no model, and the user asked for side things in their own list, so they live
# in NibelheimObjectCatalog and reach the player through Objects with the live LINON gate,
# the native player radius and their own collected/required flags.
#
# Field 308's Reunion scene is not a target at all: its director's Main runs the whole
# REQEW chain automatically on entry while Bank[1][231] bit 1 is clear and sets that bit at
# byte 92. The way in is the ordinary gateway out of 305, which Exits already lists.

# The eight rooms above are the town's buildings and they are filled. Each of them records
# its own exit as field 282 rather than 284 in the gateway table, which is a separate
# question about what that table's recorded destination means in this town; the rows carry
# the native exit line either way, which is what the player walks onto.
Add-CuratedFields 270, 271, 272, 273, 274, 276, 284, 286, 287, 297, 298, 299, 300, 301, 302, 303, 304, 305, 306, 307, 308, 309, 310

# --- The flashback doors this region displaced -----------------------------------------
#
# Add-CuratedFields above takes the mansion away from NativeEntryDoors.ps1 entirely, and
# that pass had been filling twelve doors in it for the Nibelheim flashback at 370..376 -
# a different chapter from the present-day rows this region authors, and one nothing here
# replaces. Curation is by field, not by moment, so claiming the mansion for 523..534 and
# for the optional objects silently took those twelve rows out of the flashback with it.
#
# Ten of the twelve are restored exactly as that pass produced them: same doors, same
# bands, same labels, same priority. Nothing here re-decides what those ten mean.
#
# The other two are not restored, and that is the one deliberate change. Both were
# sininb31's ln0, at 370 and at 375, banded from what the destination's entry write
# requires rather than from what the line itself does: its own Go opens with a test for
# exactly 371 before it writes 372 and enters 307, so at 370 and at 375 it is an action
# that cannot happen. At 371 the reviewed row above already covers it, and at 375 the
# field's own Director writes 376 straight away. Restoring an unavailable action because
# a release shipped it would be restoring a known-wrong binding.
$mansionFlashbackDoors = @(
    @{ field = 297; name = 'sinin1_1'; entity = 'gateway4'; type = 'Gateway'; id = -1
       x = -448; y = 850;  z = 311; target = 373; moment = 372
       line = @{ startX = -472; startY = 961;   startZ = 311; endX = -424; endY = 738;  endZ = 311 } },
    @{ field = 303; name = 'sininb2'; entity = 'gateway2'; type = 'Gateway'; id = -1
       x = -232; y = -1104; z = 0; target = 374; moment = 373
       line = @{ startX = -182; startY = -1114; startZ = 0; endX = -281; endY = -1093; endZ = 0 } },
    @{ field = 304; name = 'sininb31'; entity = 'gateway0'; type = 'Gateway'; id = -1
       x = 17; y = 88; z = 0; target = 371; moment = 370
       line = @{ startX = -117; startY = 92; startZ = 0; endX = 151; endY = 84; endZ = 0 } },
    @{ field = 304; name = 'sininb31'; entity = 'gateway0'; type = 'Gateway'; id = -1
       x = 17; y = 88; z = 0; target = 376; moment = 375
       line = @{ startX = -117; startY = 92; startZ = 0; endX = 151; endY = 84; endZ = 0 } },
    @{ field = 307; name = 'sininb41'; entity = 'gateway1'; type = 'Gateway'; id = -1
       x = -154; y = 229; z = 0; target = 374; moment = 373
       line = @{ startX = 116; startY = 38; startZ = 0; endX = -425; endY = 420; endZ = 0 } },
    @{ field = 307; name = 'sininb41'; entity = 'gateway0'; type = 'Gateway'; id = -1
       x = 224; y = 3255; z = 0; target = 375; moment = 374
       line = @{ startX = -138; startY = 3297; startZ = 0; endX = 587; endY = 3213; endZ = 0 } },
    @{ field = 307; name = 'sininb41'; entity = 'gateway2'; type = 'Gateway'; id = -1
       x = 399; y = 10; z = 0; target = 374; moment = 373
       line = @{ startX = 116; startY = 38; startZ = 0; endX = 682; endY = -17; endZ = 0 } },
    @{ field = 307; name = 'sininb41'; entity = 'gateway3'; type = 'Gateway'; id = -1
       x = 724; y = 80; z = 0; target = 374; moment = 373
       line = @{ startX = 767; startY = 176; startZ = 0; endX = 682; endY = -17; endZ = 0 } },
    @{ field = 309; name = 'sininb51'; entity = 'gateway0'; type = 'Gateway'; id = -1
       x = 54; y = 804; z = 0; target = 371; moment = 370
       line = @{ startX = -33; startY = 818; startZ = 0; endX = 142; endY = 790; endZ = 0 } },
    @{ field = 309; name = 'sininb51'; entity = 'gateway0'; type = 'Gateway'; id = -1
       x = 54; y = 804; z = 0; target = 376; moment = 375
       line = @{ startX = -33; startY = 818; startZ = 0; endX = 142; endY = 790; endZ = 0 } }
)

foreach ($mansionFlashbackDoor in $mansionFlashbackDoors) {
    $mansionFlashbackParameters = @{
        FieldId = $mansionFlashbackDoor.field
        FieldName = $mansionFlashbackDoor.name
        Kind = 'Location'
        Label = 'Take this exit to continue'
        X = $mansionFlashbackDoor.x
        Y = $mansionFlashbackDoor.y
        Z = $mansionFlashbackDoor.z
        TargetGameMoment = $mansionFlashbackDoor.target
        MinimumGameMoment = $mansionFlashbackDoor.moment
        MaximumGameMoment = $mansionFlashbackDoor.moment
        Priority = 50
        EntityName = $mansionFlashbackDoor.entity
        ScriptType = $mansionFlashbackDoor.type
        TriggerLine = $mansionFlashbackDoor.line
    }
    if ($mansionFlashbackDoor.id -ge 0) {
        $mansionFlashbackParameters.EntityId = $mansionFlashbackDoor.id
        $mansionFlashbackParameters.RequiredEnabledLineEntityId = $mansionFlashbackDoor.id
        $mansionFlashbackParameters.UsesPlayerCollisionRadius = $true
    }
    Add-Definition @mansionFlashbackParameters
}
