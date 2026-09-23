# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
#
# The present-day crossing of Mount Nibel, between Cosmo Canyon's exit write of 523 and
# Rocket Town's backyard write of 535. Nothing in between writes a GameMoment at all,
# so extraction has nothing to offer here, and the same fields are walked twice more -
# in the flashback at 353..385 and again out of Mideel at 1178..1184 - with different
# scripts each time. These rows are bounded to the present-day window only.
#
# The route, from the installed gateway tables and the walkmesh:
#
#   mtnvl2 g0 -> mtnvl3; mtnvl3 g0 -> nvdun1, arriving at (697,398), triangle 39.
#   nvdun1's mon, entity 21, stands at (935,-978,-1683) on triangle 245 and blocks the
#   way. Its Talk starts BATTLE 595 at byte 9 and, on winning, sets Bank[1][232] bit 5
#   at byte 19 and makes itself non-solid. There is no GameMoment write anywhere in it.
#   nvdun1 g3 -> mtnvl4 at (175,774), triangle 245, and from that ledge the world exit
#   g3 is 13 triangles away.
#
# The trap is nvdun1's other gateway to the same field. g2 also reaches mtnvl4, at
# (940,549) on triangle 189, and that ledge is a nine-triangle component with no route
# to the world exit at all - the route planner finds none. Two doors into one field are
# not one door. Only g3 is offered, and a player who has already taken g2 is given
# mtnvl4's own g1 back to the pipe room rather than an objective they cannot walk to.
#
# The Counter materia mtr holds is optional: it sets Bank[15][34] bit 6 and a full
# inventory simply leaves it there. It is never a condition on going on. So are the
# mansion, the safe and Vincent, which the guide treats as exploration.
$mountNibelPresentVisit = @{ MinimumGameMoment = 523; MaximumGameMoment = 534; Priority = 0 }
$mountNibelMonsterGone = New-Condition -Bank 1 -Address 232 -Mask 32 -Value 32
$mountNibelMonsterBlocking = New-Condition -Bank 1 -Address 232 -Mask 32 -Value 0

# The two mtnvl4 ledges, as the route planner measured them on the installed walkmesh.
$mountNibelWorldLedge = @(17..37) + @(216..217) + @(222..223) + @(238) + @(245..259)
$mountNibelWrongLedge = @(186..194)

# The way up to the caves.
Add-Definition @mountNibelPresentVisit -FieldId 311 -FieldName 'mtnvl2' -Kind Location `
    -Label 'Take this path further up the mountain' -X 4201 -Y -1794 -Z 312 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=4059; startY=-1819; startZ=322; endX=4344; endY=-1770; endZ=302 })

Add-Definition @mountNibelPresentVisit -FieldId 312 -FieldName 'mtnvl3' -Kind Location `
    -Label 'Go into the pipe room' -X 858 -Y -269 -Z 2304 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=858; startY=-310; startZ=2304; endX=859; endY=-228; endZ=2305 })

# The monster in the way. It is a visible model, so the row follows it through the live
# model table and disappears when the native scripts hide it.
Add-Definition @mountNibelPresentVisit -FieldId 317 -FieldName 'nvdun1' -Kind Model -EntityId 21 `
    -Label 'Deal with the creature blocking the way' `
    -RequiredCondition $mountNibelMonsterBlocking `
    -CompletedCondition $mountNibelMonsterGone `
    -EntityName 'mon' -ScriptType 'Talk'

# With it gone, the one gateway that lands on the ledge the world exit is on.
Add-Definition @mountNibelPresentVisit -FieldId 317 -FieldName 'nvdun1' -Kind Location `
    -Label 'Take the higher opening out of the cave' -X 1171 -Y -1111 -Z -1700 `
    -RequiredCondition $mountNibelMonsterGone `
    -EntityName 'gateway3' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=1151; startY=-1192; startZ=-1700; endX=1191; endY=-1031; endZ=-1700 })

Add-Definition @mountNibelPresentVisit -FieldId 313 -FieldName 'mtnvl4' -Kind Location `
    -Label 'Leave the mountain by the northern path' -X -657 -Y 1001 -Z 37 `
    -RequiredPlayerTriangles $mountNibelWorldLedge `
    -EntityName 'gateway3' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-570; startY=1014; startZ=36; endX=-744; endY=988; endZ=39 })

# The wrong ledge, and the only thing on it that leads anywhere.
Add-Definition @mountNibelPresentVisit -FieldId 313 -FieldName 'mtnvl4' -Kind Location `
    -Label 'Go back into the cave; this ledge leads nowhere' -X 946 -Y 467 -Z 46 `
    -RequiredPlayerTriangles $mountNibelWrongLedge `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=991; startY=462; startZ=46; endX=902; endY=473; endZ=46 })

# --- The cave loop, and the third ledge nothing named ------------------------------
#
# mtnvl4 is three separate pieces of walkmesh, not one. The installed field reads 271
# triangles in three components with no path between them:
#
#   component 0, 221 triangles, holds gateway 0 only, which goes back into nvdun2
#   component 1, 41 triangles, holds gateway 2 to nvdun1 and gateway 3 off the mountain
#   component 2, 9 triangles (186..194), holds gateway 1 back to nvdun1
#
# The two rows above cover components 1 and 2. Component 0 - the largest piece of the
# field - had nothing, and it is exactly where nvdun2's gateway 0 puts the party down,
# at (984,804) on triangle 240. From there the world exit is not merely far; the route
# planner finds no path to it at all, because there is none.
#
# The way on from that ledge is back through the caves: nvdun2 g1 to nvdun3, g0 to
# nvdun4, g1 to mtnvl5, g0 to mtnvl6, g2 to nvdun1, and then the higher opening above.
# None of those rooms is required - the Counter materia in them is optional - but a
# player standing in one is not on the route and had no way to learn it. Each carries
# the step that leads back toward nvdun1, from that room's own gateway table. No field
# here disables a gateway; there is no MPJPO in any of them.
$mountNibelCaveLoop = @{ MinimumGameMoment = 523; MaximumGameMoment = 534; Priority = 1 }

Add-Definition @mountNibelCaveLoop -FieldId 313 -FieldName 'mtnvl4' -Kind Location `
    -Label 'Go back into the cave' -X 912 -Y 740 -Z -210 `
    -ExcludedPlayerTriangles ($mountNibelWorldLedge + $mountNibelWrongLedge) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=962; startY=746; startZ=-209; endX=863; endY=734; endZ=-211 })

foreach ($caveStep in @(
    @{ id=318; name='nvdun2'; gateway='gateway1'; label='Take this passage on through the cave';
       x=-142; y=1788; z=-416;
       line=@{ startX=-148; startY=1753; startZ=-416; endX=-137; endY=1824; endZ=-416 } },
    @{ id=319; name='nvdun3'; gateway='gateway0'; label='Take this passage on through the cave';
       x=-37; y=-1563; z=39;
       line=@{ startX=0; startY=-1694; startZ=41; endX=-75; endY=-1432; endZ=38 } },
    @{ id=321; name='nvdun4'; gateway='gateway1'; label='Take this passage out to the mountain path';
       x=382; y=-405; z=-345;
       line=@{ startX=322; startY=-403; startZ=-345; endX=443; endY=-407; endZ=-345 } },
    @{ id=314; name='mtnvl5'; gateway='gateway0'; label='Follow the path on to the next opening';
       x=-613; y=-563; z=-67;
       line=@{ startX=-732; startY=-513; startZ=-75; endX=-495; endY=-613; endZ=-59 } },
    @{ id=315; name='mtnvl6'; gateway='gateway2'; label='Go back into the pipe room';
       x=696; y=954; z=82;
       line=@{ startX=707; startY=1007; startZ=87; endX=686; endY=901; endZ=78 } })) {
    Add-Definition @mountNibelCaveLoop -FieldId $caveStep.id -FieldName $caveStep.name `
        -Kind Location -Label $caveStep.label -X $caveStep.x -Y $caveStep.y -Z $caveStep.z `
        -EntityName $caveStep.gateway -ScriptType 'Gateway' `
        -TriggerLine $caveStep.line
}

# nvdun1's ladders and pipe jumps are native LADER/JUMP runtimes with their own
# activation - ladd1 refuses to come back down until ladu1's upward Go has set
# Bank[1][232] bit 6 - and they are left to the field's own traversal handling rather
# than described as separate objectives here.
# The cave rooms are deliberately NOT curated. Curating a field tells
# NativeEntryDoors.ps1 that this region owns every moment in it, and these five are
# also walked in the Nibelheim flashback and again out of Mideel, where the entry-door
# pass supplies rows this region says nothing about. Adding them here would have
# silently withdrawn mtnvl5 at 363, mtnvl6 at 376 and mtnvl6 at 1182.
Add-CuratedFields 311, 312, 313, 317

