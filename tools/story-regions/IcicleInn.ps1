# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
#
# The Icicle Inn is mandatory and advances no GameMoment at all. Every step of it is
# ordered by one byte of local state instead - Bank[1][130] - so write extraction finds
# nothing here, and a door-graph pass would at best point at the way out of town while
# skipping the three interactions that make leaving possible.
#
# Native order, from the installed scripts (root-native-snow.txt, -snmin1, -snmin2):
#
#   bit 0  the confrontation on the north path has happened. snow/snowbd (entity 10)
#          Move runs it while bit 0 is clear; snow/man1 Script 3 sets bit 0 at byte 221.
#   bit 5  the boy has given permission. snmin1/boy (entity 7) Talk requires bit 0 set
#          and bit 5 clear, and sets bit 5 at byte 93.
#   bit 1  the snowboard has been taken. snmin1/board (entity 11) Talk requires bit 5
#          set and bit 1 clear, sets bit 1 at byte 16, and the board model then stops
#          being drawn.
#   bit 6  the Glacier Map has been read. snmin2/dscvmap (entity 3) is a LINE, not the
#          map model - the model is display only and has no Talk script. Its Go runs
#          while bit 6 is clear and sets bit 6 at byte 59.
#
#   snow/playgam (entity 11) Move then starts the descent: it needs Bank[3][9] to be
#   zero, which is Cloud leading the party, and both bit 1 and bit 6. Byte 58 is
#   MINIGAME type 2 into field 658.
#
# The GameMoment band is only a coarse outer bound, and the local bits above do all
# the ordering. Its lower edge is 677, the last write of the Forgotten Capital that
# the party leaves to come here; its upper edge is 1007, because the town's own
# scripts - boy, mam, man1, man2, the children - all switch to their later-visit
# behaviour at Bank[2][0] >= 1008.

$icicleFirstVisitStart = 677
$icicleFirstVisitEnd = 1007

# The path up out of town. While bit 0 is clear this is where the story goes; the
# scene it starts is the game's to spring, not this row's to announce.
Add-Definition -FieldId 654 -FieldName 'snow' -Kind Location -EntityId 10 `
    -Label 'Take the path up out of the village' -X -273 -Y 3251 -Z -280 `
    -MinimumGameMoment $icicleFirstVisitStart -MaximumGameMoment $icicleFirstVisitEnd -Priority 0 `
    -RequiredCondition (New-Condition -Bank 1 -Address 130 -Mask 1 -Value 0) `
    -EntityName 'snowbd' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 10 `
    -TriggerLine ([ordered]@{ startX=-334; startY=3293; startZ=-280; endX=-212; endY=3210; endZ=-280 })

# Once that is done the snowboard is what the descent needs, and it is in the house by
# the square. snow gateway5 is its door.
Add-Definition -FieldId 654 -FieldName 'snow' -Kind Location `
    -Label 'Go into the house by the square' -X 231 -Y 152 -Z -173 `
    -MinimumGameMoment $icicleFirstVisitStart -MaximumGameMoment $icicleFirstVisitEnd -Priority 0 `
    -RequiredConditions @(
        (New-Condition -Bank 1 -Address 130 -Mask 1 -Value 1),
        (New-Condition -Bank 1 -Address 130 -Mask 2 -Value 0)) `
    -EntityName 'gateway5' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=196; startY=139; startZ=-176; endX=267; endY=165; endZ=-171 })

# The boy owns the permission. His Talk is the only thing that sets bit 5, and it is
# offered only while he is actually standing there.
Add-Definition -FieldId 655 -FieldName 'snmin1' -Kind Model -EntityId 7 `
    -Label 'Ask the boy about the snowboard' `
    -MinimumGameMoment $icicleFirstVisitStart -MaximumGameMoment $icicleFirstVisitEnd -Priority 0 `
    -RequiredConditions @(
        (New-Condition -Bank 1 -Address 130 -Mask 1 -Value 1),
        (New-Condition -Bank 1 -Address 130 -Mask 32 -Value 0)) `
    -CompletedCondition (New-Condition -Bank 1 -Address 130 -Mask 32 -Value 32) `
    -EntityName 'boy' -ScriptType 'Talk'

# Permission is not the board. Taking it is its own interaction, and the model stops
# being drawn afterwards, so the row resolves through live visibility.
Add-Definition -FieldId 655 -FieldName 'snmin1' -Kind Model -EntityId 11 `
    -Label 'Take the snowboard' `
    -MinimumGameMoment $icicleFirstVisitStart -MaximumGameMoment $icicleFirstVisitEnd -Priority 0 `
    -RequiredConditions @(
        (New-Condition -Bank 1 -Address 130 -Mask 32 -Value 32),
        (New-Condition -Bank 1 -Address 130 -Mask 2 -Value 0)) `
    -CompletedCondition (New-Condition -Bank 1 -Address 130 -Mask 2 -Value 2) `
    -EntityName 'board' -ScriptType 'Talk'

# snmin1's single gateway back to the village, so the house is never a dead end once
# the board has been picked up.
Add-Definition -FieldId 655 -FieldName 'snmin1' -Kind Location `
    -Label 'Leave the house' -X 2 -Y -41 -Z 25 `
    -MinimumGameMoment $icicleFirstVisitStart -MaximumGameMoment $icicleFirstVisitEnd -Priority 0 `
    -RequiredCondition (New-Condition -Bank 1 -Address 130 -Mask 2 -Value 2) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-72; startY=-41; startZ=25; endX=76; endY=-41; endZ=25 })

# The map is in the other house. snow gateway6 is its door; its arrow is hidden but
# the doorway itself is visible.
Add-Definition -FieldId 654 -FieldName 'snow' -Kind Location `
    -Label 'Go into the house on the east side' -X 800 -Y 507 -Z -171 `
    -MinimumGameMoment $icicleFirstVisitStart -MaximumGameMoment $icicleFirstVisitEnd -Priority 0 `
    -RequiredConditions @(
        (New-Condition -Bank 1 -Address 130 -Mask 2 -Value 2),
        (New-Condition -Bank 1 -Address 130 -Mask 64 -Value 0)) `
    -EntityName 'gateway6' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=801; startY=568; startZ=-171; endX=799; endY=446; endZ=-171 })

# The map on the wall is read by standing at the LINE and pressing OK. The visible map
# model has no Talk script of its own, so routing to it would be routing to something
# that cannot be used. The first choice only previews the map; the second keeps it,
# and both are the player's to make.
Add-Definition -FieldId 656 -FieldName 'snmin2' -Kind Location -EntityId 3 `
    -Label 'Stand at the map on the wall and press OK' -X -312 -Y 132 -Z 0 `
    -MinimumGameMoment $icicleFirstVisitStart -MaximumGameMoment $icicleFirstVisitEnd -Priority 0 `
    -RequiredCondition (New-Condition -Bank 1 -Address 130 -Mask 64 -Value 0) `
    -CompletedCondition (New-Condition -Bank 1 -Address 130 -Mask 64 -Value 64) `
    -EntityName 'dscvmap' -ScriptType 'Go' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 3 -KeepActiveOnArrival `
    -TriggerLine ([ordered]@{ startX=-317; startY=173; startZ=0; endX=-308; endY=91; endZ=0 })

Add-Definition -FieldId 656 -FieldName 'snmin2' -Kind Location `
    -Label 'Leave the house' -X -445 -Y -873 -Z 0 `
    -MinimumGameMoment $icicleFirstVisitStart -MaximumGameMoment $icicleFirstVisitEnd -Priority 0 `
    -RequiredCondition (New-Condition -Bank 1 -Address 130 -Mask 64 -Value 64) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-534; startY=-862; startZ=0; endX=-356; endY=-885; endZ=0 })

# With the board and the map, the slope itself. playgam's Move refuses unless Cloud is
# leading, so that condition is carried here rather than sending the player to a line
# that will turn them away.
Add-Definition -FieldId 654 -FieldName 'snow' -Kind Location -EntityId 11 `
    -Label 'Go to the top of the slope to start down' -X -47 -Y 3473 -Z -280 `
    -MinimumGameMoment $icicleFirstVisitStart -MaximumGameMoment $icicleFirstVisitEnd -Priority 0 `
    -RequiredConditions @(
        (New-Condition -Bank 1 -Address 130 -Mask 2 -Value 2),
        (New-Condition -Bank 1 -Address 130 -Mask 64 -Value 64),
        (New-Condition -Bank 3 -Address 9 -Mask 255 -Value 0)) `
    -EntityName 'playgam' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 11 `
    -TriggerLine ([ordered]@{ startX=-78; startY=3517; startZ=-280; endX=-16; endY=3430; endZ=-280 })

# Reviewed above. The village's other buildings are ordinary shops and are not part of
# the route, and the later visit is a different chapter with its own state.
Add-CuratedFields 654, 655, 656
