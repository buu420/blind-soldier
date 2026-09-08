# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
#
# Two objectives in the Junon gas chamber that a walk-onto-the-line row gets wrong.
#
# junbin5's gusSw, entity 9, is a LINE at (-136,-11,0)..(-90,-11,0), and its Go begins
# with IFKEYON on input bits 544 followed by a test for exactly 1016. Standing on the
# line does nothing at all: the player has to press Confirm while they are on it. The
# extracted row for it completes on arrival, which would announce the step done while
# the gas is still running, so this one keeps its objective until the moment moves.
#
# The door, entity 10 at (143,51,5)..(236,39,22), is the same shape - fresh Confirm and
# a test for exactly 1017 - and it starts Tifa's escape rather than writing a moment of
# its own, so milestone extraction never saw it and there was no row for the mandatory
# exit at all.
#
# Neither row says how the chair or the key work out. Tifa's own Script 12 runs that
# with a state machine in Bank[5][19] and there are two valid ways through it; what a
# player is owed there is what is currently visible and what the controls do, which is
# a readout and is recorded as remaining rather than answered here.
$junonGasChamber = @{ FieldId = 402; FieldName = 'junbin5'; Kind = 'Location'; Priority = 0 }

Add-Definition @junonGasChamber -EntityId 9 `
    -Label 'Press Confirm at the valve to shut off the gas' -X -113 -Y -11 -Z 0 `
    -MinimumGameMoment 1016 -MaximumGameMoment 1016 -TargetGameMoment 1017 `
    -EntityName 'gusSw' -ScriptType 'Go' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
    -RequiredEnabledLineEntityId 9 `
    -TriggerLine ([ordered]@{ startX=-136; startY=-11; startZ=0; endX=-90; endY=-11; endZ=0 })

Add-Definition @junonGasChamber -EntityId 10 `
    -Label 'Press Confirm at the door to get out' -X 189 -Y 45 -Z 13 `
    -MinimumGameMoment 1017 -MaximumGameMoment 1017 `
    -EntityName 'door' -ScriptType 'Go' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
    -RequiredEnabledLineEntityId 10 `
    -TriggerLine ([ordered]@{ startX=143; startY=51; startZ=5; endX=236; endY=39; endZ=22 })
