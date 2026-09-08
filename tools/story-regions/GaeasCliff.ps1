# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
#
# Gaea's Cliff writes no GameMoment at all. From the Forgotten Capital's last write of
# 677 (losin2) through Icicle Inn, the Great Glacier and the whole climb, the moment
# does not move again until the Whirlwind Maze writes 770 and then 790 - there is no
# native write of any value in between, so 677 is what the game is sitting at for the
# entire chapter and milestone extraction has nothing whatever to say about it. What
# orders the chapter instead is three local bits and a door graph, all of which the
# fields state outright.
#
# Native evidence (root-native-gaiafoot.txt, root-native-holu_1.txt,
# root-native-holu_2.txt, root-native-gaia_1.txt and the installed gateway tables):
#
#   holu_2/drctr Main, arriving from holu_1 (Bank[6][14] === 687) with Bank[1][131]
#   bit 6 clear, runs Holzoff's account of the climb and sets bit 6 at byte 26.
#
#   holu_1/drctr Main, arriving from holu_2 (Bank[6][11] === 688) with bit 6 set and
#   bit 7 clear, runs the party's own scene and sets bit 7 at byte 228.
#
#   gaiafoot/drctr Main, with bits 6 and 7 both set and Bank[1][132] bit 0 clear,
#   freezes the player, opens the party menu and sets Bank[1][132] bit 0 at byte 117.
#   Only after that is the climb something the party can start.
#
#   gaiafoot/ladd1, a LINE at (-503,3119,881)-(530,2849,794), jumps to gaia_1 while
#   Bank[2][0] < 999. That is the climb itself.
#
# The climb is then a chain of doors, except for the pair of ice caves in the middle of
# it, which is a puzzle and is dealt with where the rows for it are. The rest:
#
#   gaia_1 g0 -> gaiin_1; gaiin_1/evjump2 -> gaia_2; gaia_2 g1 -> gaiin_3;
#   gaiin_3/evjp41 and evjp42 -> gaiin_4; gaiin_4 g2 and g3 -> gaiin_5;
#   gaiin_5/evjump3 -> gaia_31; gaia_31 g1 -> gaiin_7; gaiin_7 g0 -> gaiin_6;
#   gaiin_6 g1 -> gaia_32; gaia_32/tocrtr1 and tocrtr2 -> crater_1.
#
# The body-temperature counter gaia_1 keeps in Bank[6][9] is not a route: it starts at
# 36, gaia_1's atatame script walks it back up while the party is warm, and below 27
# the Director sends them back to Holzoff's. It is information a sighted player is
# given on screen and this mod does not yet speak it; that is recorded as a separate
# gap in the ledger rather than dressed up as an objective here.

# The chapter runs at a standing GameMoment of 677 - the Forgotten Capital's last
# write - and ends when the Whirlwind Maze writes 770. Holzoff has later dialogue
# guarded on Bank[2][0] < 1576, so the party does come back; these rows deliberately
# do not reach that far.
$gaeaFirstVisitStart = 677
$gaeaFirstVisitEnd = 769

$gaeaHolzoffTold = 64      # Bank[1][131] bit 6
$gaeaPartyScene = 128      # Bank[1][131] bit 7
$gaeaPartyChosen = 1       # Bank[1][132] bit 0

# Holzoff's cabin, before the climb. Three rooms and a fixed order, because each
# scene is gated on the one before it having run.
Add-Definition -FieldId 686 -FieldName 'gaiafoot' -Kind Location `
    -Label 'Go into the cabin at the foot of the cliff' -X 271 -Y -118 -Z -79 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -RequiredCondition (New-Condition -Bank 1 -Address 131 -Mask $gaeaHolzoffTold -Value 0) `
    -CompletedCondition (New-Condition -Bank 1 -Address 131 -Mask $gaeaHolzoffTold -Value $gaeaHolzoffTold) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=264; startY=-176; startZ=-78; endX=279; endY=-61; endZ=-81 })

Add-Definition -FieldId 687 -FieldName 'holu_1' -Kind Location `
    -Label 'Go up to the room above' -X 143 -Y 323 -Z -53 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -RequiredCondition (New-Condition -Bank 1 -Address 131 -Mask $gaeaHolzoffTold -Value 0) `
    -CompletedCondition (New-Condition -Bank 1 -Address 131 -Mask $gaeaHolzoffTold -Value $gaeaHolzoffTold) `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=105; startY=370; startZ=-77; endX=182; endY=277; endZ=-29 })

Add-Definition -FieldId 688 -FieldName 'holu_2' -Kind Location `
    -Label 'Go back down to the room below' -X 172 -Y -185 -Z 57 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -RequiredConditions @(
        (New-Condition -Bank 1 -Address 131 -Mask $gaeaHolzoffTold -Value $gaeaHolzoffTold),
        (New-Condition -Bank 1 -Address 131 -Mask $gaeaPartyScene -Value 0)) `
    -CompletedCondition (New-Condition -Bank 1 -Address 131 -Mask $gaeaPartyScene -Value $gaeaPartyScene) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=89; startY=-181; startZ=57; endX=255; endY=-190; endZ=57 })

Add-Definition -FieldId 687 -FieldName 'holu_1' -Kind Location `
    -Label 'Go back outside to the foot of the cliff' -X 121 -Y -193 -Z 3 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -RequiredConditions @(
        (New-Condition -Bank 1 -Address 131 -Mask ($gaeaHolzoffTold + $gaeaPartyScene) -Value ($gaeaHolzoffTold + $gaeaPartyScene)),
        (New-Condition -Bank 1 -Address 132 -Mask $gaeaPartyChosen -Value 0)) `
    -CompletedCondition (New-Condition -Bank 1 -Address 132 -Mask $gaeaPartyChosen -Value $gaeaPartyChosen) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=87; startY=-191; startZ=3; endX=155; endY=-195; endZ=3 })

# The climb. ladd1 is a scripted LINE, so it has to be switched on and the party has
# to be inside the model's own collision radius of it before the engine will take it.
Add-Definition -FieldId 686 -FieldName 'gaiafoot' -Kind Location -EntityId 5 `
    -Label 'Start climbing the cliff' -X 13 -Y 2984 -Z 837 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -RequiredCondition (New-Condition -Bank 1 -Address 132 -Mask $gaeaPartyChosen -Value $gaeaPartyChosen) `
    -EntityName 'ladd1' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 5 `
    -TriggerLine ([ordered]@{ startX=-503; startY=3119; startZ=881; endX=530; endY=2849; endZ=794 })

# The way up, one room at a time. Each of these is the only door in its room that
# leads anywhere new; the rest go back the way the party came.
Add-Definition -FieldId 689 -FieldName 'gaia_1' -Kind Location `
    -Label 'Take the cave mouth at the top of the climb' -X 457 -Y 593 -Z 5385 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=393; startY=593; startZ=5385; endX=521; endY=593; endZ=5385 })

# The two ice caves are one puzzle, and an earlier draft of this file got it wrong in
# the way that is easy to get wrong: gaiin_1 and gaiin_2 are joined by four doors each
# way, so the field graph looks like a loop that can be ignored, and the passage on to
# gaia_2 looks like something to walk straight to. Running the installed walkmesh
# through the route planner says otherwise. Neither field is one place. gaiin_1 breaks
# into three components and gaiin_2 into two, and which door a player took decides
# which of them they are standing in.
#
#   gaiin_1 arriving from gaia_1 is triangle 153, in a 56-triangle component that
#   reaches gateway0 and nothing else that matters - not evjump2, the way on.
#   gaiin_2's four returns land on triangles 106, 238, 79 and 0, and only 238's
#   59-triangle component reaches evjump2 at all.
#   gaiin_2 arriving on 163, 151 or 144 is one 146-triangle component; arriving on 198
#   is a separate 96-triangle one, and only that one reaches the boulder.
#
# So the boulder is not scenery and the loop is not optional. icerock's Talk sets
# Bank[1][131] bit 1, and the four kenzan models 11..14 each read that same bit in
# their own Init: until it is set they stand solid at (66,266,-559), (27,297,-556),
# (40,190,-556) and (1,247,-555), blocking the way the party has to come back through.
# The route the native entrances and the walkmesh agree on is:
#
#   gaia_1 -> gaiin_1 (153) -> gateway0 -> gaiin_2 (163) -> gateway3 -> gaiin_1 (0)
#   -> gateway2 -> gaiin_2 (198) -> the boulder -> gateway2 -> gaiin_1 (79)
#   -> gateway3 -> gaiin_2 (144) -> gateway1 -> gaiin_1 (238) -> evjump2 -> gaia_2.
#
# The Ribbon and the Javelin in these rooms are optional and are not prerequisites for
# any of it.
$gaeaFirstCaveEntry = @(106..145) + @(148..156) + @(240..246)
$gaeaFirstCaveMiddle = @(0..6) + @(8..84) + @(87..91) + @(93..105) + @(146..147) + @(157..180) + @(196) + @(223..224) + @(226..227) + @(235) + @(247..259) + @(261)
$gaeaFirstCaveUpper = @(7) + @(85..86) + @(92) + @(181..195) + @(197..222) + @(225) + @(228..234) + @(236..239) + @(260) + @(262)
$gaeaSecondCaveMain = @(0..10) + @(12..14) + @(16..17) + @(20..25) + @(27..38) + @(40..42) + @(45) + @(48..50) + @(53..54) + @(67) + @(69..70) + @(73) + @(77) + @(81) + @(84) + @(86) + @(89..91) + @(96..101) + @(103..146) + @(149..154) + @(156..163) + @(165..166) + @(188) + @(191..195) + @(200..201) + @(203) + @(225..241)
$gaeaSecondCaveBoulder = @(11) + @(15) + @(18..19) + @(26) + @(39) + @(43..44) + @(46..47) + @(51..52) + @(55..66) + @(68) + @(71..72) + @(74..76) + @(78..80) + @(82..83) + @(85) + @(87..88) + @(92..95) + @(102) + @(147..148) + @(155) + @(164) + @(167..187) + @(189..190) + @(196..199) + @(202) + @(204..224)
$gaeaBoulderClear = New-Condition -Bank 1 -Address 131 -Mask 2 -Value 2
$gaeaBoulderBlocking = New-Condition -Bank 1 -Address 131 -Mask 2 -Value 0

# gaiin_1, on the level the climb arrives at. Only gateway0 is reachable from here.
Add-Definition -FieldId 690 -FieldName 'gaiin_1' -Kind Location `
    -Label 'Take the doorway on this level into the next cave' -X -270 -Y 238 -Z -427 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' -RequiredPlayerTriangles $gaeaFirstCaveEntry `
    -TriggerLine ([ordered]@{ startX=-292; startY=271; startZ=-428; endX=-249; endY=206; endZ=-427 })

# gaiin_2 on the level that door lands on, while the boulder still blocks the way back
# through: the only useful door is gateway3, which puts the party on gaiin_1's middle
# level.
Add-Definition -FieldId 691 -FieldName 'gaiin_2' -Kind Location `
    -Label 'Take the doorway on this level back to the first cave' -X -558 -Y -721 -Z -542 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -EntityName 'gateway3' -ScriptType 'Gateway' `
    -RequiredCondition $gaeaBoulderBlocking -RequiredPlayerTriangles $gaeaSecondCaveMain `
    -TriggerLine ([ordered]@{ startX=-592; startY=-701; startZ=-543; endX=-524; endY=-741; endZ=-542 })

# gaiin_1's middle level, boulder still blocking: gateway2 is the door to the level the
# boulder is on.
Add-Definition -FieldId 690 -FieldName 'gaiin_1' -Kind Location `
    -Label 'Take the doorway on this level into the next cave' -X -933 -Y 795 -Z -220 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -RequiredCondition $gaeaBoulderBlocking -RequiredPlayerTriangles $gaeaFirstCaveMiddle `
    -TriggerLine ([ordered]@{ startX=-970; startY=795; startZ=-220; endX=-896; endY=796; endZ=-220 })

# The boulder itself. It is on the level gateway2 lands on and on no other, and moving
# it is what clears the four ice spikes standing in the way further on.
Add-Definition -FieldId 691 -FieldName 'gaiin_2' -Kind Model -EntityId 10 `
    -Label 'Push the ice boulder out of the way' `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -RequiredCondition $gaeaBoulderBlocking -RequiredPlayerTriangles $gaeaSecondCaveBoulder `
    -CompletedCondition $gaeaBoulderClear `
    -EntityName 'icerock' -ScriptType 'Talk'

# With the boulder moved, back the way the party came: gateway2 to gaiin_1's middle
# level, gateway3 from there to gaiin_2's main level, and gateway1 from there to the
# only level of gaiin_1 that reaches the way on.
Add-Definition -FieldId 691 -FieldName 'gaiin_2' -Kind Location `
    -Label 'Take the doorway on this level back to the first cave' -X 550 -Y -640 -Z -531 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -RequiredCondition $gaeaBoulderClear -RequiredPlayerTriangles $gaeaSecondCaveBoulder `
    -TriggerLine ([ordered]@{ startX=467; startY=-655; startZ=-538; endX=634; endY=-625; endZ=-524 })

Add-Definition -FieldId 690 -FieldName 'gaiin_1' -Kind Location `
    -Label 'Take the doorway on this level into the next cave' -X -1212 -Y -26 -Z -253 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -EntityName 'gateway3' -ScriptType 'Gateway' `
    -RequiredCondition $gaeaBoulderClear -RequiredPlayerTriangles $gaeaFirstCaveMiddle `
    -TriggerLine ([ordered]@{ startX=-1218; startY=12; startZ=-257; endX=-1206; endY=-64; endZ=-250 })

Add-Definition -FieldId 691 -FieldName 'gaiin_2' -Kind Location `
    -Label 'Take the doorway on this level back to the first cave' -X 710 -Y 525 -Z -293 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -RequiredCondition $gaeaBoulderClear -RequiredPlayerTriangles $gaeaSecondCaveMain `
    -TriggerLine ([ordered]@{ startX=663; startY=570; startZ=-321; endX=758; endY=480; endZ=-265 })

# And finally the way on, which only gaiin_1's upper level ever reaches.
Add-Definition -FieldId 690 -FieldName 'gaiin_1' -Kind Location -EntityId 4 `
    -Label 'Take the passage on through the cave' -X 697 -Y 356 -Z -108 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -EntityName 'evjump2' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 4 -RequiredPlayerTriangles $gaeaFirstCaveUpper `
    -TriggerLine ([ordered]@{ startX=633; startY=380; startZ=-109; endX=761; endY=333; endZ=-108 })

Add-Definition -FieldId 692 -FieldName 'gaia_2' -Kind Location `
    -Label 'Take the cave mouth higher up the cliff' -X 581 -Y 1306 -Z 11350 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=532; startY=1308; startZ=11351; endX=631; endY=1304; endZ=11349 })

# The icicles, and what they open.
#
# gaiin_3's own drctr, entity 3, locks triangles 161, 162 and 155 in its Init and only
# unlocks them again from Bank[1][131]: 161 and 162 - the approach to the far passage -
# come free once bits 2, 3 and 4 are all set, and 155 once bit 5 is. Those bits are the
# icicles, and each is set by its own battle line in gaiin_5: bat1 entity 3 while bit 2
# is clear, bat2 entity 4 while bit 3 is clear, bat3 entity 5 while bit 4 is clear.
# Three of them make the crossing; the fourth, bit 5, opens 155 and is optional, so no
# row asks for it.
#
# gaiin_3 and gaiin_4 each have two passages to the same next room, but they are not
# interchangeable. In gaiin_4 they are on two different walkmesh components - the
# eastern arrival at triangle 2 can only reach gateway2, and the western arrival at
# triangle 20 only gateway3 - so each is offered from its own side and not from the
# other. gaiin_5 is split the same way: the icicle ledge is one component and the way
# back out onto the cliff is another, and the only way between them is the field's own
# jump.
$gaeaThreeIciclesDown = New-Condition -Bank 1 -Address 131 -Mask 0x1C -Value 0x1C
$gaeaFirstIcicleStanding = New-Condition -Bank 1 -Address 131 -Mask 0x04 -Value 0x00
$gaeaFirstIcicleDown = New-Condition -Bank 1 -Address 131 -Mask 0x04 -Value 0x04
$gaeaSecondIcicleStanding = New-Condition -Bank 1 -Address 131 -Mask 0x08 -Value 0x00
$gaeaSecondIcicleDown = New-Condition -Bank 1 -Address 131 -Mask 0x08 -Value 0x08
$gaeaThirdIcicleStanding = New-Condition -Bank 1 -Address 131 -Mask 0x10 -Value 0x00

# The two components of gaiin_4, and the two of gaiin_5, from the installed walkmesh.
$gaeaOutsideEastern = @(0..17) + @(29..85)
$gaeaOutsideWestern = @(18..28) + @(86..146)
$gaeaIcicleLedge = @(7, 8, 11) + @(18..43) + @(45, 46)
$gaeaLowerCliffFloor = @(0..6) + @(9, 10) + @(12..17) + @(44, 47)
Add-Definition -FieldId 693 -FieldName 'gaiin_3' -Kind Location -EntityId 16 `
    -Label 'Take the passage on through the cave' -X 808 -Y 943 -Z -593 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -EntityName 'evjp41' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 16 `
    -TriggerLine ([ordered]@{ startX=830; startY=1045; startZ=-593; endX=786; endY=841; endZ=-593 })

Add-Definition -FieldId 693 -FieldName 'gaiin_3' -Kind Location -EntityId 17 `
    -Label 'Cross the fallen ice to the northern passage' -X 234 -Y 1823 -Z -593 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -RequiredCondition $gaeaThreeIciclesDown `
    -EntityName 'evjp42' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 17 `
    -TriggerLine ([ordered]@{ startX=162; startY=1826; startZ=-593; endX=306; endY=1820; endZ=-593 })

Add-Definition -FieldId 696 -FieldName 'gaiin_4' -Kind Location `
    -Label 'Follow the ledge round to the icicles' -X 1247 -Y -2607 -Z 1724 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -RequiredPlayerTriangles $gaeaOutsideEastern `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=1093; startY=-2943; startZ=1726; endX=1402; endY=-2272; endZ=1723 })

Add-Definition -FieldId 696 -FieldName 'gaiin_4' -Kind Location `
    -Label 'Follow the ledge round to the way down' -X -738 -Y -3714 -Z 1766 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -RequiredPlayerTriangles $gaeaOutsideWestern `
    -EntityName 'gateway3' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-851; startY=-3459; startZ=1764; endX=-626; endY=-3970; endZ=1768 })

# The three icicles, each offered only while its own bit is still clear and the one
# before it has fallen. Each is a Go-once line that starts the battle the icicle is.
Add-Definition -FieldId 697 -FieldName 'gaiin_5' -Kind Location -EntityId 3 `
    -Label 'Walk into the first icicle' -X 495 -Y 1184 -Z -1 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -RequiredCondition $gaeaFirstIcicleStanding `
    -CompletedCondition $gaeaFirstIcicleDown `
    -RequiredPlayerTriangles $gaeaIcicleLedge `
    -EntityName 'bat1' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 3 `
    -TriggerLine ([ordered]@{ startX=486; startY=1088; startZ=-4; endX=505; endY=1280; endZ=1 })

Add-Definition -FieldId 697 -FieldName 'gaiin_5' -Kind Location -EntityId 4 `
    -Label 'Walk into the second icicle' -X 324 -Y 1266 -Z -3 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -RequiredConditions @($gaeaFirstIcicleDown, $gaeaSecondIcicleStanding) `
    -CompletedCondition $gaeaSecondIcicleDown `
    -RequiredPlayerTriangles $gaeaIcicleLedge `
    -EntityName 'bat2' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 4 `
    -TriggerLine ([ordered]@{ startX=314; startY=1185; startZ=-8; endX=334; endY=1348; endZ=2 })

Add-Definition -FieldId 697 -FieldName 'gaiin_5' -Kind Location -EntityId 5 `
    -Label 'Walk into the third icicle' -X -193 -Y 1305 -Z -3 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -RequiredConditions @($gaeaFirstIcicleDown, $gaeaSecondIcicleDown, $gaeaThirdIcicleStanding) `
    -RequiredPlayerTriangles $gaeaIcicleLedge `
    -EntityName 'bat3' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 5 `
    -TriggerLine ([ordered]@{ startX=-193; startY=1257; startZ=-9; endX=-193; endY=1353; endZ=2 })

Add-Definition -FieldId 697 -FieldName 'gaiin_5' -Kind Location -EntityId 7 `
    -Label 'Take the passage back out onto the cliff' -X -532 -Y -27 -Z -177 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -RequiredPlayerTriangles $gaeaLowerCliffFloor `
    -EntityName 'evjump3' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 7 `
    -TriggerLine ([ordered]@{ startX=-642; startY=-29; startZ=-181; endX=-422; endY=-25; endZ=-173 })

Add-Definition -FieldId 694 -FieldName 'gaia_31' -Kind Location `
    -Label 'Take the cave mouth higher up the cliff' -X -23 -Y 1666 -Z 16215 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-57; startY=1666; startZ=16215; endX=11; endY=1666; endZ=16215 })

Add-Definition -FieldId 699 -FieldName 'gaiin_7' -Kind Location `
    -Label 'Take the passage on through the cave' -X 616 -Y -660 -Z 0 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=696; startY=-431; startZ=0; endX=536; endY=-889; endZ=0 })

Add-Definition -FieldId 698 -FieldName 'gaiin_6' -Kind Location `
    -Label 'Take the passage back out onto the cliff' -X -33 -Y -23 -Z -9 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-223; startY=-23; startZ=-9; endX=157; endY=-23; endZ=-9 })

Add-Definition -FieldId 695 -FieldName 'gaia_32' -Kind Location -EntityId 6 `
    -Label 'Go on to the top of the cliff' -X 1295 -Y -27557 -Z 8569 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -EntityName 'tocrtr1' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 6 `
    -TriggerLine ([ordered]@{ startX=1498; startY=-27552; startZ=8616; endX=1093; endY=-27563; endZ=8523 })

Add-Definition -FieldId 695 -FieldName 'gaia_32' -Kind Location -EntityId 7 `
    -Label 'Go on to the top of the cliff by the other way' -X 862 -Y -27557 -Z 8609 `
    -MinimumGameMoment $gaeaFirstVisitStart -MaximumGameMoment $gaeaFirstVisitEnd -Priority 0 `
    -EntityName 'tocrtr2' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 7 `
    -TriggerLine ([ordered]@{ startX=1093; startY=-27563; startZ=8523; endX=631; endY=-27552; endZ=8695 })

Add-CuratedFields 686, 687, 688, 689, 690, 691, 692, 693, 694, 695, 696, 697, 698, 699






