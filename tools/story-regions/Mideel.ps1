# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
#
# One correction rather than a route. ithos, the Mideel clinic, has a Talk on Tifa,
# entity 2, that does two different things depending on where the story is. Below 1116
# it takes the branch at byte 38 and returns at 83 after ordinary dialogue. Only at
# 1116 - both the Corel train and Fort Condor finished - does it start at 86, set
# Bank[5][4] and go on to write 1118.
#
# Milestone extraction sees the write and offers her Talk with no band at all, so from
# the party's first arrival at 1102 the objective says to go and talk to Tifa, and
# talking to her does nothing. The write's own guard is a "less than" test rather than
# an equality, so the automatic banding cannot narrow it either. Removed and replaced
# with the interval the native script actually acts in.
for ($index = $definitions.Count - 1; $index -ge 0; $index--) {
    $definition = $definitions[$index]
    if ($definition.fieldId -eq 720 -and $definition.targetGameMoment -eq 1118 -and
        $definition.minimumGameMoment -lt 0) {
        $definitions.RemoveAt($index)
    }
}

Add-Definition -FieldId 720 -FieldName 'ithos' -Kind Model -EntityId 2 `
    -Label 'Talk to Tifa at the clinic' `
    -MinimumGameMoment 1116 -MaximumGameMoment 1117 -TargetGameMoment 1118 -Priority 0 `
    -EntityName 'tifa' -ScriptType 'Talk'
