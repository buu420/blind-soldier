# Travel that stays inside one field.
#
# Leaving a room is largely the gateway table's business, and the exit provider reads that
# table live wherever the player is standing, so an ordinary door needs no row here. A
# line that makes the party jump or climb is different: it moves them somewhere no table
# describes, and nothing offers it unless this catalog does.
#
# The audit that produced this file (audit-transit-coverage.js) found eleven fields whose
# lines move the party. Ladders are the ladder reader's job. What was left with nothing at
# all is Reactor 5's upper piping, where the way across is three jumps and the only rows
# were the doors at either end.

$reactorFivePiping = @{ MinimumGameMoment = 117; MaximumGameMoment = 128; Priority = 0 }

# 130:2's Move requests cloud script 3, which jumps the party from x -146 to x 41 across
# the break in the pipe. 130:3 is the same gap taken the other way, cloud script 4, and
# both stay open the whole time the party is up here - the escape uses them going out and
# the return uses them coming back.
#
# These share the priority the two doors at either end already have. A player halfway
# along the pipe cannot reach a door, and putting the jumps below the doors would offer
# them the far end of the room and nothing they could actually use.
Add-Definition @reactorFivePiping -FieldId 130 -FieldName 'smkin_3' -Kind Location -EntityId 2 `
    -Label 'Jump the gap in the piping' -X -146 -Y 1920 -Z 2212 `
    -EntityName 'jp0' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 2 `
    -TriggerLine ([ordered]@{ startX = -146; startY = 1958; startZ = 2212; endX = -146; endY = 1882; endZ = 2212 })

Add-Definition @reactorFivePiping -FieldId 130 -FieldName 'smkin_3' -Kind Location -EntityId 3 `
    -Label 'Jump back over the gap in the piping' -X 9 -Y 1908 -Z 2213 `
    -EntityName 'jp1' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 3 `
    -TriggerLine ([ordered]@{ startX = 9; startY = 1942; startZ = 2213; endX = 9; endY = 1874; endZ = 2213 })

# 130:4 is the drop onto the lower pipe, and it is the one here the player has to ask for:
# its Go tests IFKEYON before it requests cloud script 5, so standing on it is not enough.
# The row aims at the line; the press is the player's, and nothing here makes it.
Add-Definition @reactorFivePiping -FieldId 130 -FieldName 'smkin_3' -Kind Location -EntityId 4 `
    -Label 'Stand at the pipe edge and press Confirm to drop to the lower pipe' -X 192 -Y 1907 -Z 2214 `
    -EntityName 'jp2' -ScriptType 'Go' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 4 `
    -TriggerLine ([ordered]@{ startX = 157; startY = 1943; startZ = 2214; endX = 228; endY = 1872; endZ = 2214 })
