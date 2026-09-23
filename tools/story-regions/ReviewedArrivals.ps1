# Dot-sourced by Generate-FieldStoryEvents.ps1 after the reviewed regions.
#
# Rooms whose own entry scripts advance the story under a "less than" test rather than
# an equality one. NativeEntryDoors deliberately will not touch these: a test of
# Bank[2][0] < 652 is true for every moment from the start of the game, so a row taken
# from it alone would offer a door in the Sleeping Forest to a player still in Midgar
# if they could somehow stand there. The lower end has to be read off the chapter, and
# reading it off is a judgement, so each one is made here one at a time with the stage
# it follows written down beside it.
#
# The stage before each is taken from the stage ledger
# (.active-build-main-story-20260907/ledger/story-stages.md), which lists every native
# write in value order with the entity and script that performs it. Where the stage
# before is in a different place entirely - a world map crossing - the world catalog
# already covers it and nothing is added here.
#
# Two of the stages the ledger reports as unresolved are arrivals from the world map:
# mtcrl_0's 422 and, on the present-day visit, mtnvl2. Those fields carry their own
# gateway to a world map field and the world catalog's story stages name them, so no
# field row belongs to them and none is added.

# --- Shinra Building, floors 65-68 ---------------------------------------------
# blin66_1/shin Main writes 264 on entry while below it; blin66_2/dir Main then writes
# 266 the same way, and blin66_1's gateway2 is the only door between them. The catalog
# already carries that same gateway at 266 for the 267 write, so this is the step
# immediately before it and shares its geometry.
Add-Definition -FieldId 250 -FieldName 'blin66_1' -Kind Location `
    -Label 'Take this exit to continue' -X 2 -Y -734 -Z 0 `
    -MinimumGameMoment 264 -MaximumGameMoment 265 -TargetGameMoment 266 -Priority 50 `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-58; startY=-734; startZ=0; endX=62; endY=-734; endZ=0 })

# --- Nibelheim flashback, Mount Nibel -------------------------------------------
# nivgate/line_jp writes 353 walking into the town; mtnvl2/dir Main writes 357 on
# arriving at the mountain. nivl's line3 is the town's own way out to it.
Add-Definition -FieldId 282 -FieldName 'nivl' -Kind Location -EntityId 4 `
    -Label 'Take this way out of the town to continue' -X -38 -Y 3098 -Z 207 `
    -MinimumGameMoment 353 -MaximumGameMoment 356 -TargetGameMoment 357 -Priority 50 `
    -EntityName 'line3' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 4 `
    -TriggerLine ([ordered]@{ startX=21; startY=2869; startZ=205; endX=-98; endY=3328; endZ=209 })

# mtnvl6/dir Main writes 364 while below it, tested for exactly 363; mtnvl6b/dir Main
# then writes 365 the same way, and mtnvl6's gateway0 is the door between them.
Add-Definition -FieldId 315 -FieldName 'mtnvl6' -Kind Location `
    -Label 'Take this exit to continue' -X -574 -Y -592 -Z 32 `
    -MinimumGameMoment 364 -MaximumGameMoment 364 -TargetGameMoment 365 -Priority 50 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-617; startY=-584; startZ=25; endX=-532; endY=-600; endZ=39 })

# --- Rocket Town, first visit ----------------------------------------------------
# There was a row here for rktsid's back door labelled "Go up to the rocket to
# continue". It named the wrong place: below 553 that [OK] opens on rckt3, the backyard
# behind the house, where Shera is introduced and the Director writes 535. The rocket
# itself is four rooms further on, through the town and up the gantry. RocketTown.ps1
# reviews the same trigger properly and calls it what it is, so this coarser row is
# withdrawn rather than left to contradict it.

# --- The Sleeping Forest ---------------------------------------------------------
# slfrst_1/dir Main writes 638 on entry; slfrst_2/dir Main writes 652 the same way,
# but only while Bank[1][231] bit 3 is set. Without that bit the forest turns the
# party back, and offering the way in would be offering something that cannot be
# done, so the rows carry the same condition the script does.
$sleepingForestPass = New-Condition -Bank 1 -Address 231 -Mask 8 -Value 8

Add-Definition -FieldId 618 -FieldName 'slfrst_1' -Kind Location `
    -Label 'Take this way deeper into the forest' -X 79 -Y 1756 -Z 0 `
    -MinimumGameMoment 638 -MaximumGameMoment 651 -TargetGameMoment 652 -Priority 50 `
    -RequiredCondition $sleepingForestPass `
    -EntityName 'gateway3' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-25; startY=1756; startZ=0; endX=183; endY=1756; endZ=0 })

Add-Definition -FieldId 625 -FieldName 'sango1' -Kind Location `
    -Label 'Take this way back into the forest' -X 278 -Y -1065 -Z 46 `
    -MinimumGameMoment 638 -MaximumGameMoment 651 -TargetGameMoment 652 -Priority 50 `
    -RequiredCondition $sleepingForestPass `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=221; startY=-1072; startZ=47; endX=336; endY=-1058; endZ=46 })

# --- Junon, second visit ---------------------------------------------------------
# junbin5/gusSw Go writes 1017 tested for exactly 1016; junone6/dir Main writes 1020
# on entry while below it, and junone4's gateway0 is the door into it.
Add-Definition -FieldId 413 -FieldName 'junone4' -Kind Location `
    -Label 'Take this exit to continue' -X 174 -Y -15482 -Z 6931 `
    -MinimumGameMoment 1017 -MaximumGameMoment 1019 -TargetGameMoment 1020 -Priority 50 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=145; startY=-15499; startZ=6931; endX=204; endY=-15465; endZ=6931 })

# --- The Highwind ----------------------------------------------------------------
# junone7/lin0 Move writes 1022 boarding the airship; fship_1/bunki Main writes 1025
# on entry while below it. fship_4 and fship_42 are the same deck at two stages of the
# scene and share the gateway into it.
foreach ($highwindDeck in @(
    @{ Id = 74; Name = 'fship_4' },
    @{ Id = 75; Name = 'fship_42' })) {
    Add-Definition -FieldId $highwindDeck.Id -FieldName $highwindDeck.Name -Kind Location `
        -Label 'Take this exit to continue' -X 153 -Y 133 -Z 389 `
        -MinimumGameMoment 1022 -MaximumGameMoment 1024 -TargetGameMoment 1025 -Priority 50 `
        -EntityName 'gateway0' -ScriptType 'Gateway' `
        -TriggerLine ([ordered]@{ startX=149; startY=178; startZ=385; endX=157; endY=88; endZ=394 })
}

# --- Rocket Town, the Huge Materia visit -----------------------------------------
# The party arrives at 1299 - the world catalog's own Rocket Town (North Side) stage
# runs 1299 to 1307 - and rcktin5/init Main writes 1308 on entry while below it.
# rcktin1's gateway0 is the way in. rcktin2 has a line to the same field and is not a
# second way in: its Go tests Bank[5][16], which rcktin2's own Director only sets when
# the moment is already 1308 or past and the party has come back from rcktin5. Before
# that the line does nothing, so offering it would be sending a player to stand on a
# trigger that ignores them.
Add-Definition -FieldId 563 -FieldName 'rcktin1' -Kind Location `
    -Label 'Take this exit to continue' -X 12 -Y -24 -Z 16 `
    -MinimumGameMoment 1299 -MaximumGameMoment 1307 -TargetGameMoment 1308 -Priority 50 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-72; startY=-24; startZ=16; endX=96; endY=-24; endZ=16 })
