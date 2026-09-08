# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
# Installed ncorel/ropest/gldst scripts and gateway geometry were checked against
# the native flevel. Evidence: .artifacts/costa-gold-saucer-20260906/north-corel/.

# The town milestone had no continuation; the station milestone skipped its
# preceding flashback. Replace those two generic rows only.
for ($index = $definitions.Count - 1; $index -ge 0; $index--) {
    $definition = $definitions[$index]
    if (($definition.fieldId -eq 450 -and $definition.targetGameMoment -eq 427 -and
         $definition.sourceEntityName -eq 'evline1') -or
        ($definition.fieldId -eq 457 -and $definition.targetGameMoment -eq 433 -and
         $definition.sourceEntityName -eq 'evline3')) {
        $definitions.RemoveAt($index)
    }
}

# gldst/ev Init creates the broken-tram line only at GameMoment583. Preserve
# that later chapter without advertising it to a first-time visitor at436.
foreach ($definition in $definitions) {
    if ($definition.fieldId -eq 496 -and $definition.targetGameMoment -eq 586 -and
        $definition.sourceEntityName -eq 'ev') {
        $definition.minimumGameMoment = 583
        $definition.maximumGameMoment = 583
    }
    # gldgate's automatic companion fallback is enabled after its arrival scene
    # writes439. Companion selection itself is outside this region's new rows.
    if ($definition.fieldId -eq 497 -and $definition.targetGameMoment -eq 440 -and
        $definition.sourceEntityName -eq 'ev') {
        $definition.minimumGameMoment = 439
        $definition.maximumGameMoment = 439
    }
}

# mtcrl_0 sets422 on the first mountain entry. ncorel/evline1 Move requests the
# confrontation; drctr Script4 writes427 even if Barret is not in the party.
Add-Definition -FieldId 450 -FieldName 'ncorel' -Kind Location -EntityId 3 `
    -Label 'Approach the people in North Corel' -X 767 -Y 1012 -Z 0 `
    -MinimumGameMoment 422 -MaximumGameMoment 426 -TargetGameMoment 427 -Priority 0 `
    -EntityName 'evline1' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 3 -TriggerLine ([ordered]@{ startX=713; startY=997; startZ=0; endX=822; endY=1028; endZ=0 })

# ncorel/evline2 Move byte4 MAPJUMP457; the native line lies at the southwest
# town exit. Returning from any of the first-visit interiors retains this route.
Add-Definition -FieldId 450 -FieldName 'ncorel' -Kind Location -EntityId 4 `
    -Label 'Go to the Gold Saucer ropeway station' -X -665 -Y -681 -Z 0 `
    -MinimumGameMoment 427 -MaximumGameMoment 438 -Priority 0 `
    -EntityName 'evline2' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 4 -TriggerLine ([ordered]@{ startX=-673; startY=-631; startZ=0; endX=-657; endY=-731; endZ=0 })

# Native gateway1..4 are visible optional entrances. They share the ropeway's
# priority after the confrontation, so visiting them never becomes a prerequisite.
# Keep the elevated inn/left-house doorway Z values from the actual gateway table.
$northCorelVisits = @(
    @{ gateway=1; label='Visit the inn (optional)'; x=-326; y=597; z=426; line=@{ startX=-351; startY=651; startZ=426; endX=-302; endY=544; endZ=426 } },
    @{ gateway=2; label='Visit the left house (optional)'; x=-417; y=-86; z=213; line=@{ startX=-426; startY=-109; startZ=213; endX=-409; endY=-63; endZ=213 } },
    @{ gateway=3; label='Visit the tent (optional)'; x=338; y=-42; z=0; line=@{ startX=326; startY=-67; startZ=0; endX=351; endY=-17; endZ=0 } },
    @{ gateway=4; label='Visit the right house (optional)'; x=592; y=-175; z=0; line=@{ startX=576; startY=-155; startZ=0; endX=609; endY=-195; endZ=0 } }
)
foreach ($visit in $northCorelVisits) {
    Add-Definition -FieldId 450 -FieldName 'ncorel' -Kind Location -Label $visit.label `
        -X $visit.x -Y $visit.y -Z $visit.z -MinimumGameMoment 427 -MaximumGameMoment 438 -Priority 0 `
        -EntityName "gateway$($visit.gateway)" -ScriptType 'Gateway' -TriggerLine $visit.line
}

# ropest/evline2 Move tests bank1[128].bit0 before the first flashback. The
# flashback visits corel2, corelin and corel3, then returns at GameMoment430.
Add-Definition -FieldId 457 -FieldName 'ropest' -Kind Location -EntityId 4 `
    -Label 'Approach the group at the ropeway station' -X 1013 -Y -727 -Z 116 `
    -MinimumGameMoment 427 -MaximumGameMoment 429 -TargetGameMoment 430 -Priority 0 `
    -RequiredCondition (New-Condition -Bank 1 -Address 128 -Mask 1 -Value 0) `
    -EntityName 'evline2' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 4 -TriggerLine ([ordered]@{ startX=1012; startY=-675; startZ=113; endX=1015; endY=-780; endZ=120 })

# ropest/drctr Main byte159 sets the return flag before Barret's boarding ASK.
# A declined ASK keeps430; evline3 Move handles both <433 and >=433 branches.
# Do not expire this objective at433: the player can return by ropeway at436.
Add-Definition -FieldId 457 -FieldName 'ropest' -Kind Location -EntityId 6 `
    -Label 'Board the ropeway to Gold Saucer' -X -176 -Y -13 -Z 128 `
    -MinimumGameMoment 430 -MaximumGameMoment 438 -Priority 0 `
    -RequiredCondition (New-Condition -Bank 1 -Address 128 -Mask 1 -Value 1) `
    -EntityName 'evline3' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 6 -TriggerLine ([ordered]@{ startX=-176; startY=35; startZ=128; endX=-176; endY=-62; endZ=128 })

# These are native exit LINEs, not shop, inn, or optional Ether prerequisites.
$northCorelInteriorReturns = @(
    @{ field=453; name='ncoin1'; x=-26; y=-217; line=@{ startX=-75; startY=-217; startZ=0; endX=23; endY=-217; endZ=0 } },
    @{ field=454; name='ncoin2'; x=6; y=-316; line=@{ startX=-53; startY=-316; startZ=0; endX=66; endY=-316; endZ=0 } },
    @{ field=455; name='ncoin3'; x=324; y=-10; line=@{ startX=325; startY=-93; startZ=0; endX=324; endY=72; endZ=0 } },
    @{ field=456; name='ncoinn'; x=191; y=-365; line=@{ startX=252; startY=-370; startZ=0; endX=130; endY=-361; endZ=0 } }
)
foreach ($return in $northCorelInteriorReturns) {
    Add-Definition -FieldId $return.field -FieldName $return.name -Kind Location -EntityId 2 `
        -Label 'Return to North Corel' -X $return.x -Y $return.y -Z 0 `
        -MinimumGameMoment 427 -MaximumGameMoment 438 -Priority 0 `
        -EntityName 'evline1' -ScriptType 'Move' -RequiredEnabledLineEntityId 2 -TriggerLine $return.line
}

# gldst/s1 Talk offers the native 3000-gil day ticket, 30000-gil lifetime ticket,
# and decline choices. Only successful payment sets bank3[72].bit0 or bit7 and
# unlocks IDLCK triangle6. Leave all purchase/menu choices to the player.
Add-Definition -FieldId 496 -FieldName 'gldst' -Kind Model -EntityId 15 `
    -Label 'Talk to the Gold Saucer ticket clerk' -MinimumGameMoment 436 -MaximumGameMoment 438 -Priority 0 `
    -RequiredCondition (New-Condition -Bank 3 -Address 72 -Mask 129 -Value 0) `
    -CompletedCondition (New-Condition -Bank 3 -Address 72 -Mask 129 -Value 0 -AnyBitSet) `
    -EntityName 's1' -ScriptType 'Talk'

# Gateway0 enters gldgate497. Leaving by ropeway clears the day-ticket bit,
# while the lifetime bit persists, so returning before entering is covered too.
Add-Definition -FieldId 496 -FieldName 'gldst' -Kind Location `
    -Label 'Enter Gold Saucer' -X -74 -Y -1358 -Z 0 `
    -MinimumGameMoment 436 -MaximumGameMoment 438 -Priority 0 `
    -RequiredCondition (New-Condition -Bank 3 -Address 72 -Mask 129 -Value 0 -AnyBitSet) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-60; startY=-1273; startZ=0; endX=-89; endY=-1444; endZ=0 })

# Reviewed above, including the later broken-tram visit these fields must stay quiet
# about on a first pass.
Add-CuratedFields 450, 453, 454, 455, 456, 457, 496, 497
