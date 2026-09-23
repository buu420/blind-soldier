# zz2 (79) is the optional, playable weapon seller's house near Gongaga.
# Native m:Talk offers the Keystone questions before moment 609. The introduction
# sets 11[132] bit 0. There is no flag for asking the individual questions, so keep
# the real exit available beside the conversation rather than invent completion.
Add-Definition -FieldId 79 -FieldName 'zz2' -Kind Model -EntityId 4 `
    -Label 'Ask the weapon seller about the Keystone' `
    -MinimumGameMoment 566 -MaximumGameMoment 608 -Priority 0 `
    -RequiredCondition (New-Condition 11 132 0x01 0x01) `
    -EntityName 'm' -ScriptType 'Talk'

Add-Definition -FieldId 79 -FieldName 'zz2' -Kind Location `
    -Label "Leave the weapon seller's house" -X -69 -Y -175 -Z 0 `
    -MinimumGameMoment 566 -Priority 0 -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-121; startY=-175; startZ=0; endX=-17; endY=-175; endZ=0 })
