# The rooms between boarding a submarine and standing on its bridge.
#
# The accepted underwater work covered the dock and the bridge itself and stopped there,
# but the submarine's own aft compartment is a room the party is left standing in with
# the story waiting on them: 408's Main runs the boarding battle, writes 1280 and hands
# control back without any map jump of its own. The same is true of the recovery run in
# 407, whichever way the guards go, and of the dock once the dog has moved.

$submarineBoarding = @{ MinimumGameMoment = 1280; MaximumGameMoment = 1283; Priority = 0 }
$submarineRecovery = @{ MinimumGameMoment = 1289; MaximumGameMoment = 1399; Priority = 0 }

# Neither submarine is owned yet during the recovery: 13[80] bit 3 is the grey one and
# 13[82] bit 2 the red, and these are the same two tests the dock's own rows use.
$noSubmarineYet = @(
    (New-Condition 13 80 0x08 0x00),
    (New-Condition 13 82 0x04 0x00))

# 408:4's Main enters from 427 by itself, starts the boarding battle, writes 1280 and
# returns control. There is no map jump after it, and the same door is the way through
# if the player comes back aft from the bridge before the mission starts at 1283.
Add-Definition @submarineBoarding -FieldId 408 -FieldName 'subin_2b' -Kind Location `
    -Label 'Go forward to the bridge' -X 68 -Y 531 -Z 15 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 36; startY = 531; startZ = 15; endX = 100; endY = 531; endZ = 15 })

# 407:4's Main handles the guards on arrival - captured or fought - and returns control
# either way, so the door forward is the next step after both outcomes. 407:7's line is
# flavour only: it says they cannot go back, and it is not a way anywhere.
Add-Definition @submarineRecovery -FieldId 407 -FieldName 'subin_2a' -Kind Location `
    -Label 'Go forward to the bridge' -X 68 -Y 531 -Z 15 `
    -RequiredConditions $noSubmarineYet `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 36; startY = 531; startZ = 15; endX = 100; endY = 531; endZ = 15 })

# 392:16's dog walks out of the way, turns off the boundary that was holding the door
# shut and stops being drawn. Until then he is the thing to talk to, which is why this
# sits behind him; once he is gone he is the only row that goes.
Add-Definition @submarineRecovery -FieldId 392 -FieldName 'junin4' -Kind Location -Priority 1 `
    -Label 'Go through the dock door' -X -1367 -Y 1528 -Z 605 `
    -RequiredConditions $noSubmarineYet `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -1316; startY = 1493; startZ = 605; endX = -1419; endY = 1564; endZ = 605 })
