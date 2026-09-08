# Midgar raid: the way back down into the city, the tunnels and the cannon.
#
# The underground stretch reuses three screens across six floors, so a row cannot be
# told apart by its field and its flags alone: field 733 is entered three times and
# field 734 three times, and at each visit a different native line is the way on. The
# floors are disjoint on the installed walkmesh, so each row names the triangles its own
# floor is made of, taken from the mesh by walking outward from the native arrival
# triangle with 734's triangle 35 blocked as it is throughout this route.
#
# Line coordinates are the installed LINE opcodes and gateway exit vertices.

$midgarRaid = @{ MinimumGameMoment = 1601; MaximumGameMoment = 1601; Priority = 0 }
$midgarTunnels = @{ MinimumGameMoment = 1602; MaximumGameMoment = 1602; Priority = 0 }
$midgarCannon = @{ MinimumGameMoment = 1603; MaximumGameMoment = 1603; Priority = 0 }

# 734:3's walkway run only happens while this bit is clear and sets it afterwards, so it
# separates the floor the party leaves from the floor they land on.
$bridgeStanding = New-Condition -Bank 15 -Address 131 -Mask 0x08 -Value 0x00
$bridgeCollapsed = New-Condition -Bank 15 -Address 131 -Mask 0x08 -Value 0x08

# 778:18's encounter sets this bit whether the player fights or declines, so the way on
# is offered after either outcome and never before it.
$turksNotMet = New-Condition -Bank 15 -Address 128 -Mask 0x04 -Value 0x00
$turksSettled = New-Condition -Bank 15 -Address 128 -Mask 0x04 -Value 0x04

# The tunnel's section counter. 735 sets it to 4, every upward line decrements it and
# every downward line increments it, and the screens read it to decide where their own
# lines lead. Section three is where 737's upper-left line opens on the bridge.
$tunnelSectionTwo = New-Condition -Bank 15 -Address 129 -Mask 0xFF -Value 0x02
$tunnelSectionThree = New-Condition -Bank 15 -Address 129 -Mask 0xFF -Value 0x03
$tunnelSectionFour = New-Condition -Bank 15 -Address 129 -Mask 0xFF -Value 0x04

$proudClodStanding = New-Condition -Bank 15 -Address 128 -Mask 0x08 -Value 0x00
$proudClodBeaten = New-Condition -Bank 15 -Address 128 -Mask 0x08 -Value 0x08

$firstCatwalkFloor = @(27, 31, 32, 35, 36, 38, 39, 40, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 77, 81, 102, 104, 105, 139, 151, 153, 158, 160, 164, 165, 167, 168, 169, 170, 171, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190)
$upperCatwalkFloor = @(4, 5, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 106, 129, 130, 162)
$subwayCatwalkFloor = @(41, 42, 60, 61, 76, 78, 100, 101, 111, 116, 117, 121, 122, 123, 133, 134, 135, 136, 137, 141, 166)
$walkwayFloor = @(21, 22, 23, 40, 58, 59, 81, 82, 105, 111, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131, 132, 227)
$collapsedFloor = @(50, 68, 146, 147, 150, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 164, 165, 182)
$pipeFloor = @(24, 25, 26, 27, 31, 33, 34, 36, 37, 38, 39, 42, 43, 44, 55, 56, 57, 62, 63, 64, 65, 66, 67, 70, 71, 73, 74, 76, 77, 80, 83, 84, 87, 89, 91, 92, 93, 94, 95, 97, 99, 103, 106, 112, 113, 163, 177, 178, 180, 181, 184, 185, 186, 187, 188, 189, 190, 191, 192, 193, 194, 195, 196, 197, 198, 199, 200, 207, 208, 216, 219, 220, 221, 228)

Add-Definition -FieldId 731 -FieldName 'md8_5' -Kind Location -Priority 0 `
    -Label 'Go on down the street' -X 492 -Y -164 -Z 0 `
    -MinimumGameMoment 1600 -MaximumGameMoment 1600 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 1143; startY = 676; startZ = 0; endX = -158; endY = -1004; endZ = 0 })

Add-Definition -FieldId 732 -FieldName 'md8_6' -Kind Location -EntityId 14 -Priority 0 `
    -Label 'Climb down the open manhole' -X -4 -Y 419 -Z 0 `
    -MinimumGameMoment 1600 -MaximumGameMoment 1600 `
    -EntityName 'lad_up' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 14 `
    -TriggerLine ([ordered]@{ startX = -46; startY = 419; startZ = 0; endX = 38; endY = 419; endZ = 0 })

Add-Definition @midgarRaid -FieldId 733 -FieldName 'md8_b1' -Kind Location -EntityId 5 `
    -Label 'Take the second ladder down' -X -2528 -Y -622 -Z -514 `
    -RequiredPlayerTriangles $firstCatwalkFloor `
    -EntityName 'lad_2_d' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 5 `
    -TriggerLine ([ordered]@{ startX = -2531; startY = -665; startZ = -514; endX = -2525; endY = -578; endZ = -514 })

Add-Definition @midgarRaid -FieldId 734 -FieldName 'md8_b2' -Kind Location -EntityId 3 `
    -Label 'Go out along the walkway' -X -1685 -Y -384 -Z -1660 `
    -RequiredCondition $bridgeStanding `
    -RequiredPlayerTriangles $walkwayFloor `
    -EntityName 'brdg_j' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 3 `
    -TriggerLine ([ordered]@{ startX = -1709; startY = -248; startZ = -1660; endX = -1661; endY = -520; endZ = -1660 })

Add-Definition @midgarRaid -FieldId 734 -FieldName 'md8_b2' -Kind Location -EntityId 8 `
    -Label 'Climb the ladder on the left back up' -X -3661 -Y -996 -Z -986 `
    -RequiredCondition $bridgeCollapsed `
    -RequiredPlayerTriangles $collapsedFloor `
    -EntityName 'lad_0_u' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 8 `
    -TriggerLine ([ordered]@{ startX = -3676; startY = -1026; startZ = -986; endX = -3646; endY = -966; endZ = -986 })

Add-Definition @midgarRaid -FieldId 733 -FieldName 'md8_b1' -Kind Location -EntityId 11 `
    -Label 'Go through the duct' -X -2641 -Y 529 -Z -326 `
    -RequiredCondition $bridgeCollapsed `
    -RequiredPlayerTriangles $upperCatwalkFloor `
    -EntityName 'duct_1' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 11 `
    -TriggerLine ([ordered]@{ startX = -2693; startY = 563; startZ = -326; endX = -2589; endY = 495; endZ = -326 })

Add-Definition @midgarRaid -FieldId 734 -FieldName 'md8_b2' -Kind Location -Priority 0 `
    -Label 'Take the way up out of the pipes' -X -186 -Y 886 -Z -459 `
    -RequiredCondition $bridgeCollapsed `
    -RequiredPlayerTriangles $pipeFloor `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -248; startY = 886; startZ = -456; endX = -124; endY = 886; endZ = -462 })

Add-Definition @midgarRaid -FieldId 733 -FieldName 'md8_b1' -Kind Location -EntityId 10 `
    -Label 'Drop down to the subway' -X -748 -Y 1900 -Z -179 `
    -RequiredCondition $bridgeCollapsed `
    -RequiredPlayerTriangles $subwayCatwalkFloor `
    -EntityName 'jump' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 10 `
    -TriggerLine ([ordered]@{ startX = -796; startY = 1948; startZ = -179; endX = -700; endY = 1852; endZ = -179 })

Add-Definition -FieldId 778 -FieldName 'tunnel_6' -Kind Location -EntityId 18 -Priority 0 `
    -Label 'Cross the tunnel floor' -X 176 -Y 54 -Z 0 `
    -MinimumGameMoment 1601 -MaximumGameMoment 1601 `
    -RequiredConditions @($turksNotMet, $tunnelSectionFour) `
    -EntityName 'tarks' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 18 `
    -TriggerLine ([ordered]@{ startX = -114; startY = 54; startZ = 0; endX = 467; endY = 54; endZ = 0 })

Add-Definition @midgarTunnels -FieldId 778 -FieldName 'tunnel_6' -Kind Location -EntityId 19 `
    -Label 'Climb on up the tunnel' -X 788 -Y 2576 -Z 4 `
    -RequiredCondition $turksSettled `
    -EntityName 'jump_u' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 19 `
    -TriggerLine ([ordered]@{ startX = 890; startY = 3542; startZ = 9; endX = 687; endY = 1611; endZ = 0 })

Add-Definition @midgarTunnels -FieldId 737 -FieldName 'tunnel_5' -Kind Location -EntityId 10 `
    -Label 'Take the passage on the upper left' -X -632 -Y 1834 -Z 0 `
    -RequiredCondition $tunnelSectionThree `
    -EntityName 'jump_ul' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 10 `
    -TriggerLine ([ordered]@{ startX = -722; startY = 1767; startZ = 0; endX = -543; endY = 1902; endZ = 0 })

# 736 is only reached by taking the optional branch, and getting back to the section the
# story continues from is a climb from above and a drop from below.
Add-Definition @midgarTunnels -FieldId 736 -FieldName 'tunnel_4' -Kind Location -EntityId 16 `
    -Label 'Go back down toward the main tunnel' -X 209 -Y -617 -Z 0 `
    -RequiredCondition $tunnelSectionTwo `
    -EntityName 'jump_d' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 16 `
    -TriggerLine ([ordered]@{ startX = -83; startY = -665; startZ = 0; endX = 502; endY = -570; endZ = 0 })

Add-Definition @midgarTunnels -FieldId 736 -FieldName 'tunnel_4' -Kind Location -EntityId 15 `
    -Label 'Climb back up toward the main tunnel' -X 788 -Y 2576 -Z 4 `
    -RequiredCondition $tunnelSectionFour `
    -EntityName 'jump_u' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 15 `
    -TriggerLine ([ordered]@{ startX = 890; startY = 3542; startZ = 9; endX = 687; endY = 1611; endZ = 0 })

Add-Definition @midgarTunnels -FieldId 738 -FieldName 'md8brdg2' -Kind Location -EntityId 13 `
    -Label 'Go along the bridge' -X -458 -Y -816 -Z 512 `
    -RequiredCondition $proudClodStanding `
    -EntityName 'line_rb' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 13 `
    -TriggerLine ([ordered]@{ startX = -601; startY = -816; startZ = 512; endX = -315; endY = -816; endZ = 512 })

Add-Definition @midgarCannon -FieldId 738 -FieldName 'md8brdg2' -Kind Location `
    -Label 'Take the stairs on up' -X -411 -Y 2021 -Z 512 `
    -RequiredCondition $proudClodBeaten `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -591; startY = 2093; startZ = 512; endX = -231; endY = 1949; endZ = 512 })

Add-Definition @midgarCannon -FieldId 739 -FieldName 'md8_32' -Kind Location `
    -Label 'Go on up toward the cannon' -X -4131 -Y 18067 -Z 1977 `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -4160; startY = 18072; startZ = 1973; endX = -4103; endY = 18063; endZ = 1982 })

Add-Definition @midgarCannon -FieldId 740 -FieldName 'canon_1' -Kind Location -EntityId 3 `
    -Label 'Go on to the cannon deck' -X -4 -Y -1279 -Z 12 `
    -EntityName 'jump' -ScriptType 'Move' `
    -RequiredEnabledLineEntityId 3 `
    -TriggerLine ([ordered]@{ startX = -84; startY = -1281; startZ = 12; endX = 76; endY = -1277; endZ = 12 })

# The extraction offered this Talk at every moment, including the automatic camera
# cutaway earlier in the story where the player is not controlling anyone.
Add-Definition @midgarCannon -FieldId 741 -FieldName 'canon_2' -Kind Model -EntityId 13 `
    -Label 'Talk to Hojo to continue' `
    -EntityName 'hojyo' -ScriptType 'Talk'

# 74:1's Main calls entity 2's script 4 by itself the moment the party arrives at 1612,
# so the line the extraction found there is not something a player has to walk to.
Add-SuppressedTrigger -FieldId 74 -EntityName 'evt2' -ScriptType 'Move' `
    -MinimumGameMoment 1612 -MaximumGameMoment 1612 `
    -Reason '74:1 Main calls 74:2 script 4 automatically at 1612.'

# 72:4's script 3 invokes Cait's Talk itself at 1566, so asking the player to start that
# conversation is asking for something the game has already done.
Add-SuppressedTrigger -FieldId 72 -EntityName 'ket' -ScriptType 'Talk' `
    -MinimumGameMoment 1566 -MaximumGameMoment 1566 `
    -Reason '72:4 script 3 calls Cait 11 Talk automatically at 1566.'

Add-CuratedFields 731, 732, 733, 734, 736, 737, 738, 739, 740, 741, 778
