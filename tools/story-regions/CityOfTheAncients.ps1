# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
#
# The first visit to the City of the Ancients, from arriving out of the Sleeping Forest
# to leaving through Coral Valley for the north. The chapter is ordered by one savemap
# bit rather than by the story counter, so write extraction had almost nothing to work
# with: the counter sits at 652 from the forest until the party sleeps, and the whole
# eastern half of the city passes without a single write where the player is standing.
#
#   652  lost1 is the crossroads the forest lets out onto. Its gateway1 leads east to
#        lost2, whose gateway1 is the door of the sleeping house, losinn. The other
#        house, losin2, does not advance the visit.
#        losinn's own upstairs crossing - line11 - offers the rest once, and beds 8, 9
#        and 10 are the confirm-and-face retries if it is declined; those lines are
#        already extracted and are left alone.
#   664  Resting sets Bank[3][132] bit 4 and the counter to 664. Everything after that
#        is gated on the bit rather than the counter, because the counter does not move
#        again until the altar.
#        Out of losinn by gateway0, back west across lost2 by its own scripted jump -
#        entity 4 - to the crossroads, then lost1's gateway2 to the blue building.
#        blue_1's gateway0 enters blue_2; blue_2's gateway2 climbs to losin1.
#   ...  losin1 is the chamber above. Before the bit, its floor triangles 59, 60 and 61
#        are closed and the only way out is gateway1 back down to blue_2 - which is why
#        that row exists at 652 and the descent does not. With the bit set, gateway0 is
#        the way down into whitein, and whitein's gateway0 reaches the altar chamber.
#   667  ancnt1's lin0 and ancnt2's lin1 are already extracted. The pillar jumps between
#        them and the confirm the scene waits on are inputs rather than doorways and are
#        read out live rather than written down here.
#   677  The battle, the return and the film run themselves and hand back in losin2 at
#        677. From there the way out is gateway0 to lost2, lost2's gateway2 north to
#        sango3, sango3's gateway0 into the Coral Valley cave, the cave's gateway1 to
#        sandun_2 and its gateway1 out to the world map at 25, for Icicle Inn.
$ancientsRested = New-Condition -Bank 3 -Address 132 -Mask 16 -Value 16

$ancientsFirstVisit = @{ MinimumGameMoment = 652; MaximumGameMoment = 663; Priority = 0 }
$ancientsAfterRest = @{ MinimumGameMoment = 664; MaximumGameMoment = 672; Priority = 0 }
$ancientsDeparture = @{ MinimumGameMoment = 677; MaximumGameMoment = 680; Priority = 0 }

# --- Arriving, and finding somewhere to sleep ----------------------------------------
Add-Definition @ancientsFirstVisit -FieldId 630 -FieldName 'lost1' -Kind Location `
    -Label 'Take the eastern path into the houses' -X -2945 -Y 2938 -Z 161 `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -3042; startY = 3029; startZ = 170; endX = -2849; endY = 2847; endZ = 152 })

Add-Definition @ancientsFirstVisit -FieldId 634 -FieldName 'lost2' -Kind Location `
    -Label 'Go in through the shell house door' -X -811 -Y 6197 -Z 922 `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -809; startY = 6277; startZ = 909; endX = -814; endY = 6118; endZ = 935 })

# --- After resting -------------------------------------------------------------------
Add-Definition @ancientsAfterRest -FieldId 636 -FieldName 'losinn' -Kind Location `
    -Label 'Go back outside' -X 171 -Y -470 -Z 64 `
    -RequiredCondition $ancientsRested `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 189; startY = -513; startZ = 64; endX = 153; endY = -427; endZ = 64 })

Add-Definition @ancientsAfterRest -FieldId 634 -FieldName 'lost2' -Kind Location -EntityId 4 `
    -Label 'Cross back west towards the crossroads' -X -1931 -Y 4155 -Z 249 `
    -RequiredCondition $ancientsRested `
    -EntityName 'jump' -ScriptType 'Go' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 4 `
    -TriggerLine ([ordered]@{ startX = -2017; startY = 4153; startZ = 258; endX = -1845; endY = 4157; endZ = 240 })

Add-Definition @ancientsAfterRest -FieldId 630 -FieldName 'lost1' -Kind Location `
    -Label 'Take the western path towards the blue building' -X -7377 -Y 4292 -Z 90 `
    -RequiredCondition $ancientsRested `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -7400; startY = 4235; startZ = 92; endX = -7355; endY = 4350; endZ = 88 })

Add-Definition @ancientsAfterRest -FieldId 640 -FieldName 'blue_1' -Kind Location `
    -Label 'Go in at the foot of the blue building' -X -13585 -Y 7941 -Z 276 `
    -RequiredCondition $ancientsRested `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -13601; startY = 7911; startZ = 276; endX = -13569; endY = 7971; endZ = 276 })

Add-Definition @ancientsAfterRest -FieldId 641 -FieldName 'blue_2' -Kind Location `
    -Label 'Climb to the chamber above' -X -76 -Y 775 -Z 29 `
    -RequiredCondition $ancientsRested `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -187; startY = 778; startZ = 33; endX = 34; endY = 772; endZ = 25 })

Add-Definition @ancientsAfterRest -FieldId 631 -FieldName 'losin1' -Kind Location `
    -Label 'Go down through the opened floor' -X 81 -Y 10 -Z -22 `
    -RequiredCondition $ancientsRested `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 74; startY = -31; startZ = -31; endX = 89; endY = 51; endZ = -14 })

# Before the rest the floor is still closed, so the chamber's only way on is back down.
Add-Definition @ancientsFirstVisit -FieldId 631 -FieldName 'losin1' -Kind Location -Priority 1 `
    -Label 'Go back down to the building below' -X 348 -Y -29 -Z -96 `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 376; startY = 8; startZ = -96; endX = 320; endY = -67; endZ = -97 })

Add-Definition @ancientsAfterRest -FieldId 645 -FieldName 'whitein' -Kind Location `
    -Label 'Follow the spiral down to the altar chamber' -X -764 -Y 1139 -Z 542 `
    -RequiredCondition $ancientsRested `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -703; startY = 1079; startZ = 526; endX = -825; endY = 1199; endZ = 558 })

# --- Leaving for the north -------------------------------------------------------------
Add-Definition @ancientsDeparture -FieldId 632 -FieldName 'losin2' -Kind Location `
    -Label 'Go back outside' -X 219 -Y 133 -Z -102 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 217; startY = 96; startZ = -102; endX = 221; endY = 171; endZ = -102 })

Add-Definition @ancientsDeparture -FieldId 634 -FieldName 'lost2' -Kind Location `
    -Label 'Take the northern path out of the city' -X 46 -Y 7377 -Z 1271 `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 91; startY = 7577; startZ = 1282; endX = 2; endY = 7177; endZ = 1260 })

Add-Definition @ancientsDeparture -FieldId 627 -FieldName 'sango3' -Kind Location `
    -Label 'Go in at the mouth of the cave' -X -693 -Y 853 -Z 1227 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -654; startY = 814; startZ = 1223; endX = -733; endY = 893; endZ = 1232 })

Add-Definition @ancientsDeparture -FieldId 628 -FieldName 'sandun_1' -Kind Location `
    -Label 'Take the way north out of the cave' -X 357 -Y 764 -Z 1205 `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 303; startY = 764; startZ = 1205; endX = 411; endY = 764; endZ = 1205 })

Add-Definition @ancientsDeparture -FieldId 629 -FieldName 'sandun_2' -Kind Location `
    -Label 'Leave the valley for the world map' -X -83 -Y 851 -Z 30 `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -137; startY = 851; startZ = 29; endX = -29; endY = 851; endZ = 32 })

Add-CuratedFields 630, 631, 632, 634, 636, 640, 641, 645, 646, 647, 627, 628, 629
