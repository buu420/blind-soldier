# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
#
# The Black Materia handoff at the top of the Whirlwind Maze, and the illusion at Nibel
# it leads into. Neither writes a GameMoment where the player is standing - the counter
# sits at 677 through all of it - so write extraction found nothing and the two places
# the chapter genuinely stops said nothing at all.
#
#   trnad_1 (702) is where Cloud hands the Black Materia over. Barret is entity 11 and
#   Red XIII entity 13, and both of their Talks are built the same way: they test
#   Bank[1][133] bit 2 - has anyone taken it yet - and then ask IFPRTY whether that
#   character is in the party. The offer only appears on the branch where they are NOT,
#   which is the whole point of the scene; both models stand there either way, so
#   nothing about the models can be used to decide it. Accepting sets bit 0 for Barret
#   or bit 1 for Red, and bit 2 for either.
#   Tifa is entity 12, and her Talk tests the same bit 2: while it is clear she says
#   something and control returns, and once it is set the same Talk map-jumps to 705.
#   So she is a reminder before the handoff and the way on after it, and only the second
#   of those is a step.
#
#   nivgate (279) and nivl_b22 (293) are the illusion. The arrival runs itself and hands
#   control back with nothing on screen asking to be spoken to.
#   279's Tifa, entity 7, is an ordinary Talk that enters 290.
#   293 is ordered by Bank[3][21]. Sephiroth is entity 10 and his Talk runs while bit 2
#   is clear, setting it at byte 230. Tifa is entity 3, and her Talk does nothing at all
#   until bit 2 is set: with bit 2 and not bit 3 it plays the first exchange and sets
#   bit 3 at byte 187, and with bit 3 it runs the last one. So the order is Sephiroth,
#   then Tifa, then Tifa again, and before Sephiroth she is only a reminder.
#
# The maze's own wind and the meteor chain that follows are not rows: the wind is a
# hazard to be read out live rather than routed through, and the chain after the
# illusion runs to 999 by itself without asking for anything.
$blackMateriaUnclaimed = New-Condition -Bank 1 -Address 133 -Mask 4 -Value 0
$blackMateriaHandedOver = New-Condition -Bank 1 -Address 133 -Mask 4 -Value 4
$barretOutsideParty = New-PartyMemberCondition -CharacterId 1
$redOutsideParty = New-PartyMemberCondition -CharacterId 4

$illusionBeforeConfrontation = New-Condition -Bank 3 -Address 21 -Mask 4 -Value 0
$illusionAfterConfrontation = New-Condition -Bank 3 -Address 21 -Mask 4 -Value 4
$illusionFirstReplyPending = New-Condition -Bank 3 -Address 21 -Mask 8 -Value 0
$illusionFirstReplyGiven = New-Condition -Bank 3 -Address 21 -Mask 8 -Value 8

$whirlwindSummit = @{ FieldId = 702; FieldName = 'trnad_1'; Kind = 'Model'; ScriptType = 'Talk'
    MinimumGameMoment = 677; MaximumGameMoment = 677 }

# --- Handing over the Black Materia ---------------------------------------------------
Add-Definition @whirlwindSummit -EntityId 11 -Priority 0 `
    -Label 'Give the Black Materia to Barret' `
    -RequiredConditions @($blackMateriaUnclaimed, $barretOutsideParty) `
    -CompletedCondition $blackMateriaHandedOver `
    -EntityName 'ballet'

Add-Definition @whirlwindSummit -EntityId 13 -Priority 0 `
    -Label 'Give the Black Materia to Red XIII' `
    -RequiredConditions @($blackMateriaUnclaimed, $redOutsideParty) `
    -CompletedCondition $blackMateriaHandedOver `
    -EntityName 'red'

Add-Definition @whirlwindSummit -EntityId 12 -Priority 1 `
    -Label 'Talk to Tifa to go on' `
    -RequiredCondition $blackMateriaHandedOver `
    -EntityName 'tifa'

# --- The illusion at Nibelheim ----------------------------------------------------------
Add-Definition -FieldId 279 -FieldName 'nivgate' -Kind Model -EntityId 7 -Priority 0 `
    -Label 'Talk to Tifa at the gate' `
    -MinimumGameMoment 677 -MaximumGameMoment 677 `
    -EntityName 'tifa' -ScriptType 'Talk'

Add-Definition -FieldId 293 -FieldName 'nivl_b22' -Kind Model -EntityId 10 -Priority 0 `
    -Label 'Talk to Sephiroth' `
    -MinimumGameMoment 677 -MaximumGameMoment 677 `
    -RequiredCondition $illusionBeforeConfrontation `
    -CompletedCondition $illusionAfterConfrontation `
    -EntityName 'cefirth' -ScriptType 'Talk'

Add-Definition -FieldId 293 -FieldName 'nivl_b22' -Kind Model -EntityId 3 -Priority 0 `
    -Label 'Answer Tifa' `
    -MinimumGameMoment 677 -MaximumGameMoment 677 `
    -RequiredConditions @($illusionAfterConfrontation, $illusionFirstReplyPending) `
    -CompletedCondition $illusionFirstReplyGiven `
    -EntityName 'tifa' -ScriptType 'Talk'

Add-Definition -FieldId 293 -FieldName 'nivl_b22' -Kind Model -EntityId 3 -Priority 0 `
    -Label 'Answer Tifa again' `
    -MinimumGameMoment 677 -MaximumGameMoment 677 `
    -RequiredCondition $illusionFirstReplyGiven `
    -EntityName 'tifa' -ScriptType 'Talk'

Add-CuratedFields 702, 279, 293
