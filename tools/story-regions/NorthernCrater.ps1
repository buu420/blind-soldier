# The Northern Crater, from the Highwind's landing to the last platform before the
# bottom.
#
# The descent forks twice and the game keeps the player's answers in bank13[84]: the
# first choice leaves it at 0 for the left-hand way and sets it to 1 for the right, and
# the second, which the left-hand way runs by itself, sets 2 for the upper passage and 3
# for the lower. 751's own LINE17 and LINE18 turn the party back from the side they did
# not choose, so every row below is offered only on the branch it belongs to. None of
# this picks a branch: it says where the way on is once the player has picked one.
#
# bank15[149] bit 1 is set once the party has been split, and 751's ladder is the thing
# that starts that, so the ladder is the objective until then and the two ways down are
# the objectives afterwards.

$craterDescent = @{ MinimumGameMoment = 1620; MaximumGameMoment = 1996; Priority = 0 }

$splitPending = New-Condition -Bank 15 -Address 149 -Mask 0x02 -Value 0x00
$splitDone = New-Condition -Bank 15 -Address 149 -Mask 0x02 -Value 0x02
$leftChosen = New-Condition -Bank 13 -Address 84 -Mask 0xFF -Value 0x00
$rightChosen = New-Condition -Bank 13 -Address 84 -Mask 0xFF -Value 0x01
$leftUpperChosen = New-Condition -Bank 13 -Address 84 -Mask 0xFF -Value 0x02
$leftLowerChosen = New-Condition -Bank 13 -Address 84 -Mask 0xFF -Value 0x03

Add-Definition @craterDescent -FieldId 744 -FieldName 'las0_1' -Kind Location -EntityId 5 `
    -Label 'Climb down the ladder into the crater' -X -164 -Y -1144 -Z -999 `
    -EntityName 'ladd' -ScriptType 'Go' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 5 `
    -TriggerLine ([ordered]@{ startX = -163; startY = -1118; startZ = -1003; endX = -166; endY = -1170; endZ = -996 })

Add-Definition @craterDescent -FieldId 745 -FieldName 'las0_2' -Kind Location -EntityId 3 `
    -Label 'Take the way down off the spiral' -X -930 -Y -482 -Z -2108 `
    -EntityName 'mjump' -ScriptType '[OK]' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 3 `
    -TriggerLine ([ordered]@{ startX = -872; startY = -415; startZ = -2117; endX = -989; endY = -549; endZ = -2100 })

Add-Definition @craterDescent -FieldId 746 -FieldName 'las0_3' -Kind Location `
    -Label 'Follow the ledge round' -X -920 -Y 578 -Z 118 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -976; startY = 577; startZ = 118; endX = -865; endY = 579; endZ = 118 })

Add-Definition @craterDescent -FieldId 747 -FieldName 'las0_4' -Kind Location -EntityId 28 `
    -Label 'Climb down the stepped wall' -X -654 -Y 552 -Z 954 `
    -EntityName 'mjump' -ScriptType '[OK]' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 28 `
    -TriggerLine ([ordered]@{ startX = -714; startY = 607; startZ = 920; endX = -595; endY = 498; endZ = 988 })

Add-Definition @craterDescent -FieldId 748 -FieldName 'las0_5' -Kind Location `
    -Label 'Go through to the next cave' -X 948 -Y -1500 -Z -615 `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 914; startY = -1500; startZ = -627; endX = 982; endY = -1500; endZ = -604 })

# Field 749 is a lattice of ledges, eighteen of them and none walkable to the next, so
# every step down is a line the party has to stand on. The gateways in and out of 750
# are read from the live gateway table by the exit provider and are not repeated here;
# these are the jumps, which no native reader offers because they stay inside the field.
#
# Only the lines that descend are offered. Each jump_N goes back up the ledge its
# partner came down, and the row that used to be here named one of those.
# They all share one priority: a player standing on a ledge can only reach the line on
# that ledge, and splitting them by priority would hide the reachable one behind the
# rest of the lattice.

# down_0 starts on the ledge at z 920 and puts the party down at z 762.
Add-Definition @craterDescent -FieldId 749 -FieldName 'las0_6' -Kind Location -EntityId 13 `
    -Label 'Jump down to the next ledge' -X 3 -Y 54 -Z 920 `
    -EntityName 'down_0' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 13 `
    -TriggerLine ([ordered]@{ startX = 3; startY = 128; startZ = 920; endX = 2; endY = -20; endZ = 919 })

# fall_1 starts on the ledge at z 762 and puts the party down at z 346.
Add-Definition @craterDescent -FieldId 749 -FieldName 'las0_6' -Kind Location -EntityId 9 `
    -Label 'Drop down past the ledge below' -X 457 -Y 47 -Z 762 `
    -EntityName 'fall_1' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 9 `
    -TriggerLine ([ordered]@{ startX = 461; startY = 96; startZ = 762; endX = 452; endY = -3; endZ = 762 })

# down_1 starts on the ledge at z 554 and puts the party down at z 346.
Add-Definition @craterDescent -FieldId 749 -FieldName 'las0_6' -Kind Location -EntityId 15 `
    -Label 'Jump down to the next ledge' -X -407 -Y 51 -Z 554 `
    -EntityName 'down_1' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 15 `
    -TriggerLine ([ordered]@{ startX = -412; startY = 122; startZ = 554; endX = -402; endY = -21; endZ = 554 })

# down_2 starts on the ledge at z 554 and puts the party down at z 346.
Add-Definition @craterDescent -FieldId 749 -FieldName 'las0_6' -Kind Location -EntityId 17 `
    -Label 'Jump down to the next ledge' -X 442 -Y 64 -Z 554 `
    -EntityName 'down_2' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 17 `
    -TriggerLine ([ordered]@{ startX = 442; startY = 120; startZ = 554; endX = 442; endY = 8; endZ = 554 })

# fall_2 starts on the ledge at z 346 and puts the party down at z -693.
Add-Definition @craterDescent -FieldId 749 -FieldName 'las0_6' -Kind Location -EntityId 10 `
    -Label 'Drop down past the ledge below' -X -617 -Y 81 -Z 346 `
    -EntityName 'fall_2' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 10 `
    -TriggerLine ([ordered]@{ startX = -617; startY = 116; startZ = 346; endX = -617; endY = 45; endZ = 346 })

# down_3 starts on the ledge at z 345 and puts the party down at z 137.
Add-Definition @craterDescent -FieldId 749 -FieldName 'las0_6' -Kind Location -EntityId 19 `
    -Label 'Jump down to the next ledge' -X 608 -Y 63 -Z 345 `
    -EntityName 'down_3' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 19 `
    -TriggerLine ([ordered]@{ startX = 610; startY = 129; startZ = 345; endX = 606; endY = -3; endZ = 345 })

# fall_3 starts on the ledge at z -69 and puts the party down at z -693.
Add-Definition @craterDescent -FieldId 749 -FieldName 'las0_6' -Kind Location -EntityId 11 `
    -Label 'Drop down past the ledge below' -X -335 -Y 64 -Z -69 `
    -EntityName 'fall_3' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 11 `
    -TriggerLine ([ordered]@{ startX = -335; startY = 128; startZ = -69; endX = -335; endY = 0; endZ = -69 })

# fall_4 starts on the ledge at z -69 and puts the party down at z -485.
Add-Definition @craterDescent -FieldId 749 -FieldName 'las0_6' -Kind Location -EntityId 12 `
    -Label 'Drop down past the ledge below' -X 535 -Y 54 -Z -69 `
    -EntityName 'fall_4' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 12 `
    -TriggerLine ([ordered]@{ startX = 535; startY = 126; startZ = -69; endX = 535; endY = -18; endZ = -69 })

# down_4 starts on the ledge at z -69 and puts the party down at z -278.
Add-Definition @craterDescent -FieldId 749 -FieldName 'las0_6' -Kind Location -EntityId 21 `
    -Label 'Jump down to the next ledge' -X 224 -Y 48 -Z -69 `
    -EntityName 'down_4' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 21 `
    -TriggerLine ([ordered]@{ startX = 226; startY = 82; startZ = -69; endX = 221; endY = 14; endZ = -69 })

# down_5 starts on the ledge at z -278 and puts the party down at z -485.
Add-Definition @craterDescent -FieldId 749 -FieldName 'las0_6' -Kind Location -EntityId 23 `
    -Label 'Jump down to the next ledge' -X 382 -Y 54 -Z -278 `
    -EntityName 'down_5' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 23 `
    -TriggerLine ([ordered]@{ startX = 382; startY = 128; startZ = -278; endX = 382; endY = -20; endZ = -278 })

# down_10 starts on the ledge at z -278 and puts the party down at z -485.
Add-Definition @craterDescent -FieldId 749 -FieldName 'las0_6' -Kind Location -EntityId 33 `
    -Label 'Jump down to the next ledge' -X -7 -Y 62 -Z -278 `
    -EntityName 'down_10' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 33 `
    -TriggerLine ([ordered]@{ startX = -7; startY = 98; startZ = -278; endX = -7; endY = 26; endZ = -278 })

# down_6 starts on the ledge at z -485 and puts the party down at z -693.
Add-Definition @craterDescent -FieldId 749 -FieldName 'las0_6' -Kind Location -EntityId 25 `
    -Label 'Jump down to the next ledge' -X -42 -Y 55 -Z -485 `
    -EntityName 'down_6' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 25 `
    -TriggerLine ([ordered]@{ startX = -44; startY = 129; startZ = -485; endX = -40; endY = -19; endZ = -485 })

# down_7 starts on the ledge at z -485 and puts the party down at z -693.
Add-Definition @craterDescent -FieldId 749 -FieldName 'las0_6' -Kind Location -EntityId 27 `
    -Label 'Jump down to the next ledge' -X 543 -Y 54 -Z -485 `
    -EntityName 'down_7' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 27 `
    -TriggerLine ([ordered]@{ startX = 543; startY = 126; startZ = -485; endX = 543; endY = -18; endZ = -485 })

# down_8 starts on the ledge at z -693 and puts the party down at z -902.
Add-Definition @craterDescent -FieldId 749 -FieldName 'las0_6' -Kind Location -EntityId 29 `
    -Label 'Jump down to the next ledge' -X 522 -Y 64 -Z -693 `
    -EntityName 'down_8' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 29 `
    -TriggerLine ([ordered]@{ startX = 520; startY = 1; startZ = -693; endX = 523; endY = 126; endZ = -693 })

# down_9 starts on the ledge at z -902 and puts the party down at z -1014.
Add-Definition @craterDescent -FieldId 749 -FieldName 'las0_6' -Kind Location -EntityId 31 `
    -Label 'Jump down to the next ledge' -X 28 -Y 55 -Z -902 `
    -EntityName 'down_9' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 31 `
    -TriggerLine ([ordered]@{ startX = 34; startY = 119; startZ = -902; endX = 22; endY = -9; endZ = -902 })

# m_jump starts on the ledge at z -1041 and is the one line here that leaves for 751.
Add-Definition @craterDescent -FieldId 749 -FieldName 'las0_6' -Kind Location -EntityId 35 `
    -Label 'Jump down to the crater floor' -X -72 -Y 55 -Z -1041 `
    -EntityName 'm_jump' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 35 `
    -TriggerLine ([ordered]@{ startX = -72; startY = 127; startZ = -1041; endX = -72; endY = -17; endZ = -1041 })

# 751:5's Main waits on bank5[26], and the ladder is what sets it: climbing down is how
# the party splitting begins. Nothing here chooses a side.
Add-Definition @craterDescent -FieldId 751 -FieldName 'las0_8' -Kind Location -EntityId 15 `
    -Label 'Climb down the ladder to gather the party' -X -2662 -Y -175 -Z 4822 `
    -RequiredCondition $splitPending `
    -EntityName 'lad_2_d' -ScriptType 'Go' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 15 `
    -TriggerLine ([ordered]@{ startX = -2749; startY = -165; startZ = 4830; endX = -2575; endY = -186; endZ = 4814 })

Add-Definition @craterDescent -FieldId 751 -FieldName 'las0_8' -Kind Location `
    -Label 'Follow the right-hand way down' -X -280 -Y 914 -Z 3947 `
    -RequiredConditions @($splitDone, $rightChosen) `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -261; startY = 841; startZ = 3950; endX = -300; endY = 987; endZ = 3945 })

Add-Definition @craterDescent -FieldId 751 -FieldName 'las0_8' -Kind Location `
    -Label 'Follow the left-hand way down' -X -2133 -Y -1087 -Z 4188 `
    -RequiredConditions @($splitDone, $leftChosen) `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -2300; startY = -982; startZ = 4279; endX = -1966; endY = -1192; endZ = 4098 })

Add-Definition @craterDescent -FieldId 752 -FieldName 'las1_1' -Kind Location `
    -Label 'Go on down the right-hand way' -X -108 -Y -204 -Z 2824 `
    -RequiredCondition $rightChosen `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -106; startY = -246; startZ = 2808; endX = -111; endY = -163; endZ = 2841 })

Add-Definition @craterDescent -FieldId 753 -FieldName 'las1_2' -Kind Location `
    -Label 'Carry on down the right-hand way' -X 315 -Y 420 -Z 340 `
    -RequiredCondition $rightChosen `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 244; startY = 393; startZ = 339; endX = 386; endY = 447; endZ = 342 })

Add-Definition @craterDescent -FieldId 754 -FieldName 'las1_3' -Kind Location `
    -Label 'Keep going down the right-hand way' -X 400 -Y 2838 -Z 688 `
    -RequiredCondition $rightChosen `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 373; startY = 2787; startZ = 689; endX = 428; endY = 2889; endZ = 688 })

Add-Definition @craterDescent -FieldId 755 -FieldName 'las1_4' -Kind Location `
    -Label 'Go on to where the party meets' -X 745 -Y 1377 -Z -556 `
    -RequiredCondition $rightChosen `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 719; startY = 1390; startZ = -557; endX = 772; endY = 1364; endZ = -555 })

# 756 runs the second choice itself on arrival, so there is nothing to walk to until it
# has been made. 756:12 and 756:13 turn the party back from the way they did not pick.
Add-Definition @craterDescent -FieldId 756 -FieldName 'las2_1' -Kind Location `
    -Label 'Take the upper way on' -X 895 -Y 264 -Z -303 `
    -RequiredCondition $leftUpperChosen `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 896; startY = 294; startZ = -303; endX = 895; endY = 235; endZ = -303 })

Add-Definition @craterDescent -FieldId 756 -FieldName 'las2_1' -Kind Location `
    -Label 'Take the lower way on' -X 674 -Y -202 -Z -303 `
    -RequiredCondition $leftLowerChosen `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 642; startY = -226; startZ = -303; endX = 706; endY = -179; endZ = -303 })

Add-Definition @craterDescent -FieldId 757 -FieldName 'las2_2' -Kind Location `
    -Label 'Go on along the upper way' -X 363 -Y 2472 -Z 91 `
    -RequiredCondition $leftUpperChosen `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 302; startY = 2539; startZ = 93; endX = 424; endY = 2405; endZ = 89 })

Add-Definition @craterDescent -FieldId 758 -FieldName 'las2_3' -Kind Location `
    -Label 'Carry on along the upper way' -X 461 -Y 1807 -Z 88 `
    -RequiredCondition $leftUpperChosen `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 455; startY = 1888; startZ = 95; endX = 468; endY = 1726; endZ = 81 })

Add-Definition @craterDescent -FieldId 759 -FieldName 'las2_4' -Kind Location `
    -Label 'Go on to where the party meets' -X -1208 -Y 10 -Z 412 `
    -RequiredCondition $leftUpperChosen `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -1196; startY = -23; startZ = 412; endX = -1221; endY = 43; endZ = 412 })

Add-Definition @craterDescent -FieldId 760 -FieldName 'las3_1' -Kind Location `
    -Label 'Go on down to the platforms' -X 1077 -Y -2283 -Z -518 `
    -RequiredCondition $leftLowerChosen `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 769; startY = -2320; startZ = -518; endX = 1385; endY = -2246; endZ = -519 })

Add-Definition @craterDescent -FieldId 761 -FieldName 'las3_2' -Kind Location -EntityId 16 `
    -Label 'Cross the platforms to the far side' -X 792 -Y -1295 -Z -635 `
    -RequiredCondition $leftLowerChosen `
    -EntityName 'l11' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 16 `
    -TriggerLine ([ordered]@{ startX = 561; startY = -1321; startZ = -635; endX = 1023; endY = -1270; endZ = -635 })

Add-Definition @craterDescent -FieldId 762 -FieldName 'las3_3' -Kind Location `
    -Label 'Go on to where the party meets' -X 1611 -Y -1126 -Z -1225 `
    -RequiredCondition $leftLowerChosen `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 1321; startY = -1158; startZ = -1225; endX = 1901; endY = -1094; endZ = -1225 })

# 764 keeps two lines on the same piece of ground: the one that runs the meeting is
# enabled until it has happened, and the one that simply goes on afterwards takes over.
# Which of them is enabled is the whole difference, so each row waits for its own.
Add-Definition -FieldId 764 -FieldName 'las4_1' -Kind Location -EntityId 12 -Priority 0 `
    -Label 'Go over to the others' -X -563 -Y -141 -Z -91 `
    -MinimumGameMoment 1997 -MaximumGameMoment 1997 `
    -EntityName 'l2' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 12 `
    -TriggerLine ([ordered]@{ startX = -658; startY = -168; startZ = -84; endX = -468; endY = -114; endZ = -98 })

Add-Definition -FieldId 764 -FieldName 'las4_1' -Kind Location -EntityId 13 -Priority 0 `
    -Label 'Go back over to the others' -X -563 -Y -141 -Z -91 `
    -MinimumGameMoment 1998 -MaximumGameMoment 1998 `
    -EntityName 'l3' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 13 `
    -TriggerLine ([ordered]@{ startX = -658; startY = -168; startZ = -84; endX = -468; endY = -114; endZ = -98 })

Add-Definition -FieldId 765 -FieldName 'las4_2' -Kind Location -EntityId 18 -Priority 0 `
    -Label 'Cross to the far side' -X 514 -Y -316 -Z -373 `
    -MinimumGameMoment 1999 -MaximumGameMoment 1999 `
    -EntityName 'l6' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 18 `
    -TriggerLine ([ordered]@{ startX = 483; startY = -347; startZ = -365; endX = 546; endY = -285; endZ = -381 })

Add-Definition -FieldId 766 -FieldName 'las4_3' -Kind Location -EntityId 12 -Priority 0 `
    -Label 'Go on across to the far ledge' -X 289 -Y 436 -Z 302 `
    -MinimumGameMoment 1999 -MaximumGameMoment 1999 `
    -EntityName 'l8' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 12 `
    -TriggerLine ([ordered]@{ startX = 139; startY = 424; startZ = 303; endX = 440; endY = 448; endZ = 302 })

Add-CuratedFields 744, 745, 746, 747, 748, 749, 751, 752, 753, 754, 755, 756, 757, 758, 759, 760, 761, 762, 765, 766

# The cave beside the descent. 749's lower gateways all lead in here, and it had no rows
# at all, so stepping through one arrived somewhere the catalog said nothing about.
#
# Its walkmesh is three separate pieces and its five doors are spread across them, so a
# row for the wrong door would point at a way out across a gap the player cannot cross.
# Each row therefore carries the triangles of the piece its door stands on, and where a
# piece has two doors it is the one that comes out lower - this is a way down. The rest
# are still offered by the live gateway reader.

# gateway4 stands on the 162-triangle side of the cave and comes out
# on the ledge at z -693. The other door on this side, gateway0, goes back up to z 762 and is left to the live gateway reader.
Add-Definition @craterDescent -FieldId 750 -FieldName 'las0_7' -Kind Location `
    -Label 'Follow the passage down and out to the next ledge' -X 131 -Y 186 -Z -529 `
    -EntityName 'gateway4' -ScriptType 'Gateway' `
    -RequiredPlayerTriangles @(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 54, 55, 56, 58, 59, 60, 61, 62, 70, 71, 72, 74, 84, 107, 108, 111, 112, 113, 114, 115, 119, 120, 122, 125, 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 149, 150, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191, 192, 193, 196, 197, 198, 199, 200, 201, 202, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 214, 215, 216, 217) `
    -TriggerLine ([ordered]@{ startX = 201; startY = 195; startZ = -529; endX = 61; endY = 176; endZ = -529 })

# gateway1 stands on the 13-triangle side of the cave and comes out
# on the ledge at z 346.
Add-Definition @craterDescent -FieldId 750 -FieldName 'las0_7' -Kind Location `
    -Label 'Take the passage back out to the ledge' -X -338 -Y 119 -Z 341 `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -RequiredPlayerTriangles @(53, 57, 63, 109, 110, 116, 117, 118, 121, 163, 164, 165, 194) `
    -TriggerLine ([ordered]@{ startX = -282; startY = 127; startZ = 341; endX = -394; endY = 111; endZ = 341 })

# gateway3 stands on the 43-triangle side of the cave and comes out
# on the ledge at z -69. The other door on this side, gateway2, goes back up to z 137 and is left to the live gateway reader.
Add-Definition @craterDescent -FieldId 750 -FieldName 'las0_7' -Kind Location `
    -Label 'Follow the passage down and out to the next ledge' -X 320 -Y 38 -Z -214 `
    -EntityName 'gateway3' -ScriptType 'Gateway' `
    -RequiredPlayerTriangles @(64, 65, 66, 67, 68, 69, 73, 75, 76, 77, 78, 79, 80, 81, 82, 83, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 123, 124, 148, 195, 203) `
    -TriggerLine ([ordered]@{ startX = 366; startY = 48; startZ = -214; endX = 274; endY = 28; endZ = -214 })
