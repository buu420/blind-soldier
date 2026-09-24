# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
#
# Wutai and the Materia Yuffie takes on the way in. The quest writes no GameMoment at
# all, so milestone extraction found nothing here and every Wutai room said nothing. It
# is ordered by five bytes its own scripts write, read from the installed archive:
#
#   3[207] bit 5   the theft on the way in (yougan, yougan3); bit 6 is the end of it,
#                  written by yufy1/AD Script 4 with the Materia returned.
#   3[189] bit 5   uutai1/AD Script 3, Yuffie's "Hey!!" on first entering the town.
#          bit 1   utapb/AD Script 3, the scene with the Turks in Turtle's Paradise. Reno's
#                  Talk and Rude's each request it while the bit is clear; Elena's Talk
#                  does nothing until it is set.
#   3[190] bit 0   uta_im/TAKARA Talk. It returns at once until 3[189] bit 1 is set, and
#                  until then OYAJI2's Init stands her at (146,158), between the counter and
#                  the chest. It writes the chest's own 1[59] bit 1, then Yuffie steals the
#                  Materia and bit 0 is set.
#          bit 1   utmin2/YUFI Script 3. BYOBU and BYOBUB, the two sides of the folding
#                  screen, are enabled only while bit 0 is set and bit 1 is clear, and
#                  either one's Go with Confirm runs it.
#   3[189] bit 2   uutai1/AD Script 5. While 3[190] bit 1 is set and this is clear, AD2
#                  holds triangles 212 and 213, shakes the pot every 90 frames and requests
#                  YUFI Script 4: XYZI (-1175,-776) on triangle 214, TLKON on, TALKR 180 and
#                  no VISI. Her Talk is the only caller of the capture, and its own VISI 1
#                  is what first shows her - so the pot is a Talk with an entity nobody can
#                  see, and its row says so. The capture map-jumps to the bar, whose
#                  Director plays the second scene (bit 3) and map-jumps into yufy1.
#          uutai1/AD holds triangle 170, the door of Yuffie's house, until bit 2 is set.
#   3[190] bit 2   yufy1's Director plays "Follow me" while it is clear. yufy1/AD holds
#                  triangle 1, the way back to town, until 3[189] bit 0.
#          bit 3   yufy2/YUFI walks to (68,-243) on first arrival. Her Talk requests AD
#                  Script 3 while 5[0] is 0; the scene sets 5[0] and switches SWITCH on.
#          bit 4   either answer at SWITCH drops the cage (bit 5 or 6 with it). The first
#                  time, YUFI Script 9 escapes and sets 3[189] bit 0. The same SWITCH with
#                  bit 4 set raises the cage, clears bits 4 to 6 and sets bit 7.
#   5[3]           uutai2/AD holds triangle 114 before the trap and triangle 128 after it,
#                  unless 5[3] is 1. KANE, rung with Confirm from triangle 138, 139, 140 or
#                  86, releases 128 and sets 5[3]; arriving from uttmpin4 sets it too.
#   3[191] bit 0   uttmpin4/AD on arrival: Corneo takes Yuffie and Elena and runs toward
#                  gateway 0. bit 1: uttmpin3/AD fights battle 622 on arrival. bit 2:
#                  uutai2/RENO's Talk, which also sets 13[80] bit 5 - "the most obvious
#                  place".
#   13[80] bit 6   datiao_6's scene on arrival, which map-jumps into yufy1; its Director
#                  then returns the Materia by itself.
#
# Every row carries the quest's own window, 3[207] bit 5 set and bit 6 clear, so none of
# them is ever offered outside it. A step's rows are priority 0. Every town room, the
# Pagoda and Da-chao also have their one way back at priority 50, so a room the current
# step does not need is still not silent; the step's own row, where there is one, is
# then the only row. Godo's house is an optional puzzle house and gets rows only for the
# long way back to Reno. Nothing here names a lever, a reward or a room a sighted player
# has not been shown: the pot shakes, the bell hangs where it can be seen, and Corneo
# runs out of the Hidden Room in plain view.

$wutaiStep = @{ Priority = 0 }
$wutaiWayBack = @{ Priority = 50 }

$wutaiQuest = New-Condition -Bank 3 -Address 207 -Mask 0x60 -Value 0x20
$wutaiBarUnseen = New-Condition -Bank 3 -Address 189 -Mask 0x02 -Value 0x00
$wutaiBarSeen = New-Condition -Bank 3 -Address 189 -Mask 0x02 -Value 0x02
$wutaiChestUnopened = New-Condition -Bank 3 -Address 190 -Mask 0x01 -Value 0x00
$wutaiChestStolen = New-Condition -Bank 3 -Address 190 -Mask 0x01 -Value 0x01
$wutaiChestOpened = New-Condition -Bank 1 -Address 59 -Mask 0x02 -Value 0x02
$wutaiScreenUnchecked = New-Condition -Bank 3 -Address 190 -Mask 0x02 -Value 0x00
$wutaiScreenChecked = New-Condition -Bank 3 -Address 190 -Mask 0x02 -Value 0x02
$wutaiPotUnchecked = New-Condition -Bank 3 -Address 189 -Mask 0x04 -Value 0x00
$wutaiPotChecked = New-Condition -Bank 3 -Address 189 -Mask 0x04 -Value 0x04
$wutaiFollowSeen = New-Condition -Bank 3 -Address 190 -Mask 0x04 -Value 0x04
$wutaiTrapPending = New-Condition -Bank 3 -Address 189 -Mask 0x01 -Value 0x00
$wutaiTrapSprung = New-Condition -Bank 3 -Address 189 -Mask 0x01 -Value 0x01
$wutaiCageUp = New-Condition -Bank 3 -Address 190 -Mask 0x10 -Value 0x00
$wutaiCageDown = New-Condition -Bank 3 -Address 190 -Mask 0x10 -Value 0x10
$wutaiYuffieUnheard = New-Condition -Bank 5 -Address 0 -Mask 0xFF -Value 0x00
$wutaiBellUnrung = New-Condition -Bank 5 -Address 3 -Mask 0xFF -Value 0x00
$wutaiBellRung = New-Condition -Bank 5 -Address 3 -Mask 0xFF -Value 0x01
$wutaiCorneoSeen = New-Condition -Bank 3 -Address 191 -Mask 0x01 -Value 0x01
$wutaiFightPending = New-Condition -Bank 3 -Address 191 -Mask 0x02 -Value 0x00
$wutaiFightWon = New-Condition -Bank 3 -Address 191 -Mask 0x02 -Value 0x02
$wutaiRenoUnheard = New-Condition -Bank 3 -Address 191 -Mask 0x04 -Value 0x00
$wutaiRenoHeard = New-Condition -Bank 3 -Address 191 -Mask 0x04 -Value 0x04
$wutaiDaChaoAhead = New-Condition -Bank 13 -Address 80 -Mask 0x60 -Value 0x20

# The uttmpin3 pocket beside the gateway to the Hidden Room. Arriving from uttmpin2 the
# Director holds triangle 21, and these are the triangles that then cannot be reached;
# arriving from uttmpin4 they are where the party stands.
$wutaiPrayerRoomPocket = @(21, 23, 24, 25, 32, 33)

# uttmpin2's hall as its own Inits leave it, reachable from both of its doors: D1, D3 and
# KAI hold the rest shut until the party opens them.
$wutaiGodoHall = @(0..7) + @(53..58) + @(109..111) + @(114) + @(117..118)

# uttmpin1's room behind the hanging scroll: JIKU's Init holds triangle 22 on arrival
# from hideway1, and these four are all that remain.
$wutaiBehindTheScroll = @(94..97)

function Add-WutaiGateway {
    param([hashtable] $Band, [int] $FieldId, [string] $FieldName, [string] $Label,
          [string] $Gateway, [int[]] $Line, [object[]] $Conditions,
          [int[]] $RequiredPlayerTriangles = @(), [int[]] $ExcludedPlayerTriangles = @())
    $parameters = @{
        FieldId = $FieldId
        FieldName = $FieldName
        Kind = 'Location'
        Label = $Label
        X = [int][Math]::Truncate(($Line[0] + $Line[3]) / 2)
        Y = [int][Math]::Truncate(($Line[1] + $Line[4]) / 2)
        Z = [int][Math]::Truncate(($Line[2] + $Line[5]) / 2)
        EntityName = $Gateway
        ScriptType = 'Gateway'
        RequiredConditions = $Conditions
        TriggerLine = ([ordered]@{ startX = $Line[0]; startY = $Line[1]; startZ = $Line[2]; endX = $Line[3]; endY = $Line[4]; endZ = $Line[5] })
    }
    if ($RequiredPlayerTriangles.Count -gt 0) { $parameters.RequiredPlayerTriangles = $RequiredPlayerTriangles }
    if ($ExcludedPlayerTriangles.Count -gt 0) { $parameters.ExcludedPlayerTriangles = $ExcludedPlayerTriangles }
    Add-Definition @Band @parameters
}

function Add-WutaiLineExit {
    param([hashtable] $Band, [int] $FieldId, [string] $FieldName, [string] $Label,
          [int] $EntityId, [string] $EntityName, [string] $ScriptType, [int[]] $Line,
          [object[]] $Conditions)
    $parameters = @{
        FieldId = $FieldId
        FieldName = $FieldName
        Kind = 'Location'
        Label = $Label
        EntityId = $EntityId
        X = [int][Math]::Truncate(($Line[0] + $Line[3]) / 2)
        Y = [int][Math]::Truncate(($Line[1] + $Line[4]) / 2)
        Z = [int][Math]::Truncate(($Line[2] + $Line[5]) / 2)
        EntityName = $EntityName
        ScriptType = $ScriptType
        RequiredConditions = $Conditions
        RequiredEnabledLineEntityId = $EntityId
        TriggerLine = ([ordered]@{ startX = $Line[0]; startY = $Line[1]; startZ = $Line[2]; endX = $Line[3]; endY = $Line[4]; endZ = $Line[5] })
    }
    # A Go 1x fires when the leader touches the LINE, inside the player's own collision
    # radius; a Move fires on the crossing itself.
    if ($ScriptType -eq 'Go 1x') { $parameters.UsesPlayerCollisionRadius = $true }
    Add-Definition @Band @parameters
}

# --- The town -------------------------------------------------------------------
Add-WutaiGateway $wutaiStep 579 'uutai1' "Go into Turtle's Paradise" 'gateway2' `
    @(-1409, -477, 46, -1409, -378, 46) @($wutaiQuest, $wutaiBarUnseen)
Add-WutaiGateway $wutaiStep 579 'uutai1' 'Go into the Item Store' 'gateway4' `
    @(-1340, 1328, 0, -1255, 1337, 0) @($wutaiQuest, $wutaiBarSeen, $wutaiChestUnopened)
Add-WutaiGateway $wutaiStep 579 'uutai1' "Go into the Old Man's House" 'gateway0' `
    @(1142, 921, 0, 1212, 852, 0) @($wutaiQuest, $wutaiChestStolen, $wutaiScreenUnchecked)

Add-Definition @wutaiStep -FieldId 579 -FieldName 'uutai1' -Kind Model -EntityId 16 `
    -Label 'Check the shaking pot' `
    -RequiredConditions @($wutaiQuest, $wutaiScreenChecked, $wutaiPotUnchecked) `
    -CompletedCondition $wutaiPotChecked `
    -EntityName 'YUFI' -ScriptType 'Talk' -UsesHiddenTalkTarget

Add-WutaiGateway $wutaiStep 579 'uutai1' "Go back into Yuffie's House" 'gateway5' `
    @(444, 2478, 416, 460, 2559, 416) @($wutaiQuest, $wutaiPotChecked, $wutaiTrapPending)
Add-WutaiGateway $wutaiStep 579 'uutai1' 'Go to the Pagoda' 'gateway7' `
    @(-3873, 2167, 0, -3730, 2218, 0) @($wutaiQuest, $wutaiTrapSprung, $wutaiRenoUnheard)
Add-WutaiGateway $wutaiStep 579 'uutai1' 'Go to the Da-chao Statue' 'gateway6' `
    @(-1131, 3748, 1, -1027, 3574, 1) @($wutaiQuest, $wutaiDaChaoAhead)

# --- Turtle's Paradise ----------------------------------------------------------
foreach ($turk in @(@{ entity = 15; name = 'RENO'; label = 'Talk to Reno' },
                    @{ entity = 14; name = 'LUDE'; label = 'Talk to Rude' })) {
    Add-Definition @wutaiStep -FieldId 580 -FieldName 'utapb' -Kind Model -EntityId $turk.entity `
        -Label $turk.label `
        -RequiredConditions @($wutaiQuest, $wutaiBarUnseen) -CompletedCondition $wutaiBarSeen `
        -EntityName $turk.name -ScriptType 'Talk'
}
Add-WutaiLineExit $wutaiWayBack 580 'utapb' "Leave Turtle's Paradise" 9 'LINEJ' 'Go 1x' `
    @(-77, -529, 0, 75, -529, 0) @($wutaiQuest)

# --- The Item Store --------------------------------------------------------------
Add-Definition @wutaiStep -FieldId 576 -FieldName 'uta_im' -Kind Model -EntityId 7 `
    -Label 'Open the treasure chest' `
    -RequiredConditions @($wutaiQuest, $wutaiBarSeen, $wutaiChestUnopened) `
    -CompletedCondition $wutaiChestOpened `
    -EntityName 'TAKARA' -ScriptType 'Talk'
Add-WutaiLineExit $wutaiWayBack 576 'uta_im' 'Leave the Item Store' 8 'LINEJ' 'Go 1x' `
    @(-85, -171, 0, 1, -174, 0) @($wutaiQuest)

# --- The Old Man's House ---------------------------------------------------------
foreach ($side in @(@{ entity = 16; name = 'BYOBU'; label = 'Check behind the folding screen'; line = @(-82, -190, 10, -91, -37, 10) },
                    @{ entity = 17; name = 'BYOBUB'; label = 'Check behind the folding screen from the other side'; line = @(-202, 21, 10, -140, 36, 10) })) {
    $line = $side.line
    Add-Definition @wutaiStep -FieldId 578 -FieldName 'utmin2' -Kind Location -EntityId $side.entity `
        -Label $side.label `
        -X ([int][Math]::Truncate(($line[0] + $line[3]) / 2)) `
        -Y ([int][Math]::Truncate(($line[1] + $line[4]) / 2)) `
        -Z ([int][Math]::Truncate(($line[2] + $line[5]) / 2)) `
        -RequiredConditions @($wutaiQuest, $wutaiChestStolen, $wutaiScreenUnchecked) `
        -CompletedCondition $wutaiScreenChecked `
        -EntityName $side.name -ScriptType 'Go' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
        -RequiredEnabledLineEntityId $side.entity `
        -TriggerLine ([ordered]@{ startX = $line[0]; startY = $line[1]; startZ = $line[2]; endX = $line[3]; endY = $line[4]; endZ = $line[5] })
}
Add-WutaiLineExit $wutaiWayBack 578 'utmin2' "Leave the Old Man's House" 18 'LINEJ' 'Go 1x' `
    @(-50, -275, -1, 50, -275, -1) @($wutaiQuest)

# --- Yuffie's House ---------------------------------------------------------------
Add-WutaiGateway $wutaiStep 581 'yufy1' 'Go into the back room' 'gateway0' `
    @(351, -228, -33, 306, -298, -5) @($wutaiQuest, $wutaiFollowSeen, $wutaiTrapPending)
Add-WutaiLineExit $wutaiWayBack 581 'yufy1' "Leave Yuffie's House" 12 'LINEJ' 'Go 1x' `
    @(-43, -391, 0, 53, -391, 0) @($wutaiQuest, $wutaiTrapSprung)

Add-Definition @wutaiStep -FieldId 582 -FieldName 'yufy2' -Kind Model -EntityId 7 `
    -Label 'Talk to Yuffie' `
    -RequiredConditions @($wutaiQuest, $wutaiTrapPending, $wutaiCageUp, $wutaiYuffieUnheard) `
    -EntityName 'YUFI' -ScriptType 'Talk'
$wutaiSwitch = [ordered]@{ startX = 50; startY = 207; startZ = -25; endX = 178; endY = 207; endZ = -25 }
Add-Definition @wutaiStep -FieldId 582 -FieldName 'yufy2' -Kind Location -EntityId 11 `
    -Label 'Use the levers' -X 114 -Y 207 -Z -25 `
    -RequiredConditions @($wutaiQuest, $wutaiTrapPending, $wutaiCageUp) `
    -CompletedCondition $wutaiCageDown `
    -EntityName 'SWITCH' -ScriptType '[OK]' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
    -RequiredEnabledLineEntityId 11 -TriggerLine $wutaiSwitch
Add-Definition @wutaiStep -FieldId 582 -FieldName 'yufy2' -Kind Location -EntityId 11 `
    -Label 'Use the levers again to raise the cage' -X 114 -Y 207 -Z -25 `
    -RequiredConditions @($wutaiQuest, $wutaiCageDown) `
    -CompletedCondition $wutaiCageUp `
    -EntityName 'SWITCH' -ScriptType '[OK]' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
    -RequiredEnabledLineEntityId 11 -TriggerLine $wutaiSwitch
Add-WutaiGateway $wutaiWayBack 582 'yufy2' 'Go back to the front room' 'gateway0' `
    @(-198, -622, 180, -266, -546, 180) @($wutaiQuest, $wutaiTrapSprung, $wutaiCageUp)

# --- The Pagoda --------------------------------------------------------------------
Add-Definition @wutaiStep -FieldId 587 -FieldName 'uutai2' -Kind Location -EntityId 14 `
    -Label 'Ring the bell' -X -623 -Y -4862 -Z 127 `
    -RequiredConditions @($wutaiQuest, $wutaiTrapSprung, $wutaiFightPending, $wutaiBellUnrung) `
    -CompletedCondition $wutaiBellRung `
    -CompletionPlayerTriangles @(138, 139, 140, 86) `
    -EntityName 'KANE' -ScriptType 'Main' -KeepActiveOnArrival
Add-WutaiGateway $wutaiStep 587 'uutai2' 'Go through the open door' 'gateway0' `
    @(-865, -4998, -72, -778, -4998, -76) @($wutaiQuest, $wutaiTrapSprung, $wutaiFightPending, $wutaiBellRung)
Add-Definition @wutaiStep -FieldId 587 -FieldName 'uutai2' -Kind Model -EntityId 12 `
    -Label 'Talk to Reno' `
    -RequiredConditions @($wutaiQuest, $wutaiFightWon, $wutaiRenoUnheard) `
    -CompletedCondition $wutaiRenoHeard `
    -EntityName 'RENO' -ScriptType 'Talk'
Add-WutaiLineExit $wutaiWayBack 587 'uutai2' 'Go back into town' 17 'LINEJ' 'Go 1x' `
    @(656, -5369, -35, 536, -5503, -77) @($wutaiQuest)

# --- The Hidden Room and the way back to Reno --------------------------------------
Add-WutaiGateway $wutaiStep 591 'uttmpin4' 'Go after Corneo' 'gateway0' `
    @(251, 475, 207, 251, 395, 207) @($wutaiQuest, $wutaiCorneoSeen, $wutaiFightPending)
Add-WutaiGateway $wutaiWayBack 591 'uttmpin4' 'Go back out to the Pagoda' 'gateway1' `
    @(-52, -566, 8, 68, -566, 8) @($wutaiQuest)
Add-WutaiGateway $wutaiStep 590 'uttmpin3' 'Go back to the Hidden Room' 'gateway1' `
    @(-164, 426, 28, -116, 426, 28) @($wutaiQuest, $wutaiFightWon, $wutaiRenoUnheard) `
    -RequiredPlayerTriangles $wutaiPrayerRoomPocket
Add-WutaiGateway $wutaiStep 590 'uttmpin3' 'Go back out through the house' 'gateway0' `
    @(-56, -369, 0, 66, -373, 0) @($wutaiQuest, $wutaiFightWon, $wutaiRenoUnheard) `
    -ExcludedPlayerTriangles $wutaiPrayerRoomPocket
Add-WutaiGateway $wutaiStep 589 'uttmpin2' 'Go back toward the Pagoda' 'gateway0' `
    @(-4462, -639, 1123, -4291, -412, 1123) @($wutaiQuest, $wutaiFightWon, $wutaiRenoUnheard) `
    -RequiredPlayerTriangles $wutaiGodoHall
Add-WutaiGateway $wutaiStep 588 'uttmpin1' 'Go back out to the Pagoda' 'gateway0' `
    @(49, -879, 0, 189, -795, 0) @($wutaiQuest, $wutaiFightWon, $wutaiRenoUnheard) `
    -ExcludedPlayerTriangles $wutaiBehindTheScroll

# --- Da-chao --------------------------------------------------------------------------
Add-WutaiGateway $wutaiStep 592 'datiao_1' 'Climb Da-chao' 'gateway1' `
    @(-61, -263, -560, 44, -259, -580) @($wutaiQuest, $wutaiDaChaoAhead)
Add-WutaiGateway $wutaiStep 593 'datiao_2' 'Keep climbing Da-chao' 'gateway2' `
    @(-371, 96, 108, -383, 0, 94) @($wutaiQuest, $wutaiDaChaoAhead)
Add-WutaiGateway $wutaiStep 596 'datiao_5' 'Keep climbing Da-chao' 'gateway3' `
    @(-195, -380, -237, -113, -318, -231) @($wutaiQuest, $wutaiDaChaoAhead)

# --- One way back from every other room ------------------------------------------------
Add-WutaiLineExit $wutaiWayBack 575 'uta_wa' 'Leave the shop' 7 'LINEJ' 'Go 1x' `
    @(-64, -153, 0, 64, -153, 0) @($wutaiQuest)
Add-WutaiLineExit $wutaiWayBack 577 'utmin1' "Leave the Cat's House" 5 'LINEJ' 'Go 1x' `
    @(-48, -159, -7, 48, -159, -7) @($wutaiQuest)
Add-WutaiGateway $wutaiWayBack 585 'hideway3' "Go back to the Cat's House" 'gateway0' `
    @(52, -364, 0, -52, -364, 0) @($wutaiQuest)
Add-WutaiLineExit $wutaiWayBack 592 'datiao_1' 'Go back down to town' 5 'jump' 'Move' `
    @(-117, -1293, -1724, 108, -1336, -1743) @($wutaiQuest)
Add-WutaiGateway $wutaiWayBack 593 'datiao_2' 'Go back down' 'gateway0' `
    @(-69, -297, -635, 41, -299, -668) @($wutaiQuest)
Add-WutaiGateway $wutaiWayBack 594 'datiao_3' 'Go back the way you came' 'gateway0' `
    @(-558, 150, -286, -500, 18, -338) @($wutaiQuest)
Add-WutaiGateway $wutaiWayBack 595 'datiao_4' 'Go back the way you came' 'gateway0' `
    @(413, 26, -338, 320, -54, -302) @($wutaiQuest)
Add-WutaiGateway $wutaiWayBack 596 'datiao_5' 'Go back down' 'gateway0' `
    @(-127, 209, 142, -175, 87, 126) @($wutaiQuest)
Add-WutaiGateway $wutaiWayBack 597 'datiao_6' 'Go back down' 'gateway0' `
    @(-382, -283, -155, -401, -165, -41) @($wutaiQuest)
Add-WutaiGateway $wutaiWayBack 598 'datiao_7' 'Go back the way you came' 'gateway0' `
    @(-598, 483, 364, -702, 365, 307) @($wutaiQuest)
Add-WutaiGateway $wutaiWayBack 599 'datiao_8' 'Go back the way you came' 'gateway0' `
    @(-308, -1094, 0, -222, -1187, 0) @($wutaiQuest)
