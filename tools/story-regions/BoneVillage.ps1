# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
#
# Bone Village and the Sleeping Forest are required, and the catalog had nothing at all
# for either. The chapter's own write of 652 happens in slfrst_2's Director on entry,
# so extraction put its row somewhere else entirely and the four fields the player
# actually stands in - bonevil, bonevil2, slfrst_1, slfrst_2 - said nothing.
#
# What orders it is one key item and one dig result, both in bank 1:
#
#   bonevil's foreman, entity 4 - a visible model whose script name is blank - has a
#   Talk gated on Bank[2][0] >= 641. Choosing the excavation sets Bank[1][235] and map
#   jumps to bonevil2; there is no GameMoment write anywhere in it. The dig's outcome
#   comes back in Bank[1][234].
#   box1, entity 13, is the chest the dig turns up. Its Talk, with Bank[1][234] equal
#   to 1 and Bank[1][231] bit 3 still clear, sets that bit at byte 95 and the key item
#   bit Bank[1][67] bit 3 at 99. Finishing the dig is not the same as collecting it,
#   and until the bit is set the forest turns the party back.
#   slfrst_2's lin0, entity 5, maps on to sango1 only while Bank[2][0] >= 652; below
#   that it maps straight back into slfrst_2 and the party walks in circles.
#
# The digging itself is a placement activity in bonevil2 with its own controls, and
# describing it means saying where the visible workers are standing and which way they
# are facing after a blast - not reading the hidden target's position out of the script
# data. That is a readout rather than an objective and is recorded as remaining.
$boneVillageDig = @{ MinimumGameMoment = 641; MaximumGameMoment = 651; Priority = 0 }
$boneHarpHeld = New-Condition -Bank 1 -Address 231 -Mask 8 -Value 8
$boneHarpMissing = New-Condition -Bank 1 -Address 231 -Mask 8 -Value 0

Add-Definition @boneVillageDig -FieldId 617 -FieldName 'bonevil' -Kind Model -EntityId 4 `
    -Label 'Talk to the excavation foreman' `
    -RequiredCondition $boneHarpMissing `
    -CompletedCondition $boneHarpHeld `
    -EntityName 'foreman' -ScriptType 'Talk'

Add-Definition @boneVillageDig -FieldId 617 -FieldName 'bonevil' -Kind Model -EntityId 13 `
    -Label 'Collect what the dig turned up' `
    -RequiredConditions @(
        (New-Condition -Bank 1 -Address 234 -Mask 255 -Value 1),
        $boneHarpMissing) `
    -CompletedCondition $boneHarpHeld `
    -EntityName 'box1' -ScriptType 'Talk'

Add-Definition @boneVillageDig -FieldId 617 -FieldName 'bonevil' -Kind Location `
    -Label 'Leave the village for the forest to the north' -X -463 -Y 1206 -Z 332 `
    -RequiredCondition $boneHarpHeld `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-444; startY=1081; startZ=332; endX=-482; endY=1332; endZ=332 })

# Once the forest has let the party through, its far side is the only way on. Below
# 652 this same line maps back into slfrst_2 itself, which is the forest turning them
# around, so the row starts where the native test does.
Add-Definition -FieldId 619 -FieldName 'slfrst_2' -Kind Location -EntityId 5 `
    -Label 'Follow the path on out of the forest' -X -34 -Y 1837 -Z 0 `
    -MinimumGameMoment 652 -MaximumGameMoment 660 -Priority 0 `
    -RequiredCondition $boneHarpHeld `
    -EntityName 'lin0' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 5 `
    -TriggerLine ([ordered]@{ startX=-866; startY=1837; startZ=0; endX=798; endY=1837; endZ=0 })

# bonevil2 (772) holds the excavation activity itself and is deliberately not claimed.
Add-CuratedFields 617, 618, 619
