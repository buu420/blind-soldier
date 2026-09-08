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

# nvdun1's ladders and pipe jumps are native LADER/JUMP runtimes with their own
# activation - ladd1 refuses to come back down until ladu1's upward Go has set
# Bank[1][232] bit 6 - and they are left to the field's own traversal handling rather
# than described as separate objectives here.
Add-CuratedFields 311, 312, 313, 317

