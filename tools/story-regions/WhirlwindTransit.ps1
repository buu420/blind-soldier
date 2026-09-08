# The walk through the Whirlwind Maze.
#
# The maze writes no GameMoment at all between the Forgotten Capital's 677 and the
# Nibelheim flashback's 770, so extraction had nothing to find and the accepted rows only
# covered the conversations at either end. What was missing is the travel: eight rooms
# joined by gateways and by lines the field turns on and off with its own local flags in
# Bank 1[132], which is the only thing that orders this chapter.
#
# The wind rooms keep the readout they already have. Nothing here says when the wind is
# safe to cross, because nothing in the field says so either: woa_3's gusts come from the
# field's own random number and the readout reports the state it is in, not the state it
# is about to be in.

$whirlwindTransit = @{ MinimumGameMoment = 677; MaximumGameMoment = 769; Priority = 0 }

# 700:1's Main sets 1[132] bit 7 and hands control back. The black-cloaked figure below is
# not required - his line only plays a short animation and sets 1[133] bit 6 - so the way
# on is the downhill gate.
$craterArrivalDone = New-Condition 1 132 0x80 0x80

# 701:2's Main handles a party that already has Tifa by itself and sets bit 2. Without
# her, 701:4's Move is the scene that adds her, and it sets the same bit.
$tifaSceneOutstanding = New-Condition 1 132 0x04 0x00
$tifaSceneDone = New-Condition 1 132 0x04 0x04

# 705:17's discovery line is enabled while bit 4 is clear, sets it, and disables itself.
$discoveryOutstanding = New-Condition 1 132 0x10 0x00
$discoveryDone = New-Condition 1 132 0x10 0x10

Add-Definition @whirlwindTransit -FieldId 700 -FieldName 'crater_1' -Kind Location `
    -Label 'Go on down the crater rim' -X -1493 -Y -356 -Z -1629 `
    -RequiredCondition $craterArrivalDone `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -1495; startY = -423; startZ = -1612; endX = -1491; endY = -290; endZ = -1647 })

Add-Definition @whirlwindTransit -FieldId 701 -FieldName 'crater_2' -Kind Location -EntityId 4 `
    -Label 'Go over to the others' -X 321 -Y 64 -Z 0 `
    -RequiredCondition $tifaSceneOutstanding `
    -EntityName 'evline1' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 4 `
    -TriggerLine ([ordered]@{ startX = 305; startY = -98; startZ = 0; endX = 337; endY = 226; endZ = 0 })

Add-Definition @whirlwindTransit -FieldId 701 -FieldName 'crater_2' -Kind Location -EntityId 5 `
    -Label 'Cross to the far side of the rim' -X -1194 -Y 134 -Z 0 `
    -RequiredCondition $tifaSceneDone `
    -EntityName 'evline2' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 5 `
    -TriggerLine ([ordered]@{ startX = -1151; startY = -13; startZ = 0; endX = -1238; endY = 282; endZ = 0 })

Add-Definition @whirlwindTransit -FieldId 703 -FieldName 'trnad_2' -Kind Location `
    -Label 'Go on to the wind' -X -1531 -Y -1191 -Z -9 `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -1436; startY = -1365; startZ = -17; endX = -1626; endY = -1018; endZ = -2 })

# The wind rooms: the crossing is the far side, and the readout says what the wind is
# doing while the player waits for their own moment.
Add-Definition @whirlwindTransit -FieldId 709 -FieldName 'woa_1' -Kind Location `
    -Label 'Cross to the far side of the wind' -X 501 -Y 2789 -Z -604 `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 258; startY = 2698; startZ = -604; endX = 744; endY = 2880; endZ = -604 })

Add-Definition @whirlwindTransit -FieldId 704 -FieldName 'trnad_3' -Kind Location `
    -Label 'Go on to the next wind' -X 640 -Y 3225 -Z -816 `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 445; startY = 3255; startZ = -823; endX = 836; endY = 3196; endZ = -809 })

Add-Definition @whirlwindTransit -FieldId 710 -FieldName 'woa_2' -Kind Location `
    -Label 'Cross to the far side of the wind' -X 479 -Y 3065 -Z -575 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 325; startY = 3065; startZ = -575; endX = 633; endY = 3065; endZ = -575 })

Add-Definition @whirlwindTransit -FieldId 705 -FieldName 'trnad_4' -Kind Location -EntityId 17 `
    -Label 'Go over to what the party has found' -X -91 -Y -112 -Z -145 `
    -RequiredCondition $discoveryOutstanding `
    -EntityName 'discver' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 17 `
    -TriggerLine ([ordered]@{ startX = -147; startY = -113; startZ = -144; endX = -36; endY = -111; endZ = -146 })

# The party comes back to this room after the handoff, with bit 4 already set, and the
# line they used is gone. The way on is the northern gate.
Add-Definition @whirlwindTransit -FieldId 705 -FieldName 'trnad_4' -Kind Location `
    -Label 'Go on north out of the hollow' -X -202 -Y 1669 -Z 346 `
    -RequiredCondition $discoveryDone `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -256; startY = 1689; startZ = 343; endX = -149; endY = 1649; endZ = 350 })

# 711's three lines lie end to end across the same Y, and any of them is the approach the
# scene wants; the far gateway beyond them is past the trigger, not an alternative to it.
Add-Definition @whirlwindTransit -FieldId 711 -FieldName 'woa_3' -Kind Location -EntityId 12 `
    -Label 'Go on toward what is waiting ahead' -X 430 -Y 4272 -Z -713 `
    -RequiredCondition $discoveryDone `
    -EntityName 'gonivl2' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 12 `
    -TriggerLine ([ordered]@{ startX = 334; startY = 4272; startZ = -722; endX = 526; endY = 4272; endZ = -705 })
