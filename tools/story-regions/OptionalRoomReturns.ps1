# The way out of the rooms the story never asks anyone to enter.
#
# Sector 5's shops and houses, Wall Market's shops and inn, and the Honey Bee Inn's own
# rooms advance no GameMoment at all - the ledger's own reading of their scripts - so
# none of them is a step of the story and none of them gets a story objective for going
# in. But a player who does go in has to be able to come out, and every one of these
# rooms has exactly one door back. Without a row the room is silent, which for a blind
# player is indistinguishable from being stuck.
#
# Nothing here asks for a purchase, an inn stay or any other optional outcome. The rows
# are the doors and only the doors, and each is the field's own gateway exit line.

$sectorFiveRooms = @{ MinimumGameMoment = 152; MaximumGameMoment = 260; Priority = 0 }
$wallMarketRooms = @{ MinimumGameMoment = 188; MaximumGameMoment = 260; Priority = 0 }
$honeyBeeRooms = @{ MinimumGameMoment = 188; MaximumGameMoment = 191; Priority = 0 }

# --- Sector 5 slums ------------------------------------------------------------------

Add-Definition @sectorFiveRooms -FieldId 174 -FieldName 'min51_1' -Kind Location `
    -Label 'Leave the house' -X 184 -Y -199 -Z -267 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 179; startY = -147; startZ = -267; endX = 189; endY = -251; endZ = -267 })

Add-Definition @sectorFiveRooms -FieldId 175 -FieldName 'min51_2' -Kind Location `
    -Label 'Go back downstairs' -X 15 -Y 317 -Z -204 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 18; startY = 280; startZ = -207; endX = 12; endY = 355; endZ = -202 })

Add-Definition @sectorFiveRooms -FieldId 176 -FieldName 'mds5_dk' -Kind Location `
    -Label 'Leave the bar' -X 9 -Y -438 -Z -133 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -16; startY = -436; startZ = -135; endX = 34; endY = -440; endZ = -131 })

Add-Definition @sectorFiveRooms -FieldId 178 -FieldName 'mds5_w' -Kind Location `
    -Label 'Leave the weapon shop' -X 163 -Y 23 -Z -215 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 167; startY = -28; startZ = -215; endX = 160; endY = 74; endZ = -215 })

Add-Definition @sectorFiveRooms -FieldId 179 -FieldName 'mds5_i' -Kind Location `
    -Label 'Leave the item shop' -X 199 -Y -11 -Z -153 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 198; startY = 32; startZ = -153; endX = 200; endY = -54; endZ = -153 })

Add-Definition @sectorFiveRooms -FieldId 180 -FieldName 'mds5_m' -Kind Location `
    -Label 'Leave the materia shop' -X 12 -Y -163 -Z -115 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -27; startY = -159; startZ = -115; endX = 51; endY = -168; endZ = -115 })

# --- Wall Market --------------------------------------------------------------------

Add-Definition @wallMarketRooms -FieldId 198 -FieldName 'mkt_ia' -Kind Location `
    -Label 'Leave the shop' -X -77 -Y -133 -Z 17 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -101; startY = -116; startZ = 17; endX = -54; endY = -150; endZ = 17 })

Add-Definition @wallMarketRooms -FieldId 199 -FieldName 'mktinn' -Kind Location `
    -Label 'Leave the inn' -X 25 -Y 0 -Z 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 22; startY = 60; startZ = 0; endX = 28; endY = -60; endZ = 0 })

Add-Definition @wallMarketRooms -FieldId 200 -FieldName 'mkt_m' -Kind Location `
    -Label 'Leave the materia shop' -X 271 -Y -104 -Z 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 271; startY = -146; startZ = 0; endX = 272; endY = -63; endZ = 0 })

Add-Definition @wallMarketRooms -FieldId 202 -FieldName 'mkt_s2' -Kind Location `
    -Label 'Leave the shop' -X 190 -Y -156 -Z 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 160; startY = -179; startZ = 0; endX = 221; endY = -133; endZ = 1 })

Add-Definition @wallMarketRooms -FieldId 203 -FieldName 'mkt_s3' -Kind Location `
    -Label 'Leave the shop' -X 0 -Y -276 -Z 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -55; startY = -273; startZ = 0; endX = 54; endY = -279; endZ = 0 })

# --- The Honey Bee Inn ---------------------------------------------------------------
#
# The window runs to 191, not 190. 214's kobun1 Talk admits the party at 188 or above
# while 1[65] bit 4 is set and 3[218] bit 1 is clear, and at byte 181 it calls Cloud's
# script 9, which map-jumps to 218 without touching the moment. Neither 218 nor 216 resets
# it either, so the disguise preparation at 191 is spent inside these rooms and their exits
# have to still be offered.
#
# 219 onna_5 and 220 onna_52 have no gateway table, but they are not the same case and the
# comment that used to be here treated them as one.
#
# * 219's vignette directors call the map-back scripts themselves. Nothing to walk to.
# * 220 has a manual exit and it was missed. Entity 6 border1's Init creates a LINE at
#   (402,305,25)-(393,160,25) and its Move maps 218, picking the arrival by 3[220] bit 0.
#   The scenes hand control back inside the room first - Cloud's script 14 and script 23
#   both set the flag and Movable, and Mukki's script 15 starts Cloud's script 22, which
#   places him on the bed and then jumps him to (-14,-281) on triangle 36 before returning
#   control at byte 67 - so the line is a walk the player makes, after all of that.

Add-Definition @honeyBeeRooms -FieldId 216 -FieldName 'onna_2' -Kind Location `
    -Label 'Go back to the lobby' -X 336 -Y 191 -Z 25 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 340; startY = 258; startZ = 25; endX = 332; endY = 125; endZ = 25 })

# 220:6 is the way out of the scene room. Both branches of 3[220] bit 0 map to 218, so
# this is offered whichever one the visit took, and it carries the enablement contract
# like every other line row - a line the field has switched off is not a way anywhere.
Add-Definition @honeyBeeRooms -FieldId 220 -FieldName 'onna_52' -Kind Location -EntityId 6 `
    -Label 'Leave the room for the Honey Bee lobby' -X 398 -Y 233 -Z 25 `
    -EntityName 'border1' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 6 `
    -TriggerLine ([ordered]@{ startX = 402; startY = 305; startZ = 25; endX = 393; endY = 160; endZ = 25 })

Add-Definition @honeyBeeRooms -FieldId 218 -FieldName 'onna_4' -Kind Location `
    -Label 'Leave the Honey Bee Inn' -X -2 -Y -414 -Z 26 `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -56; startY = -414; startZ = 26; endX = 52; endY = -414; endZ = 26 })
