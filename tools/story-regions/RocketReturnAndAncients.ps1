# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
#
# Two late chapters that share one shape: the story counter barely moves inside either,
# and what orders them is the field's own bits.
#
# --- The rocket, 1299 ---------------------------------------------------------------
#   557's gateway0 is the way out to the base. rcktbas1's bat1 - entity 4 - is the guard
#   encounter, and once Bank[3][133] bit 1 is set its ladd, entity 3, is the way up.
#   rcktbas2's line, entity 6, is the Rude encounter; Rude himself has nothing to say and
#   is not the target. Inside, rcktin2's line1 - entity 2 - is the cockpit door while
#   Bank[5][16] is clear, and its line3 the way forward once the door is open.
#   After the launch, at 1314: rcktin5's line1 is the way out whether or not the optional
#   Huge Materia was taken, rcktin2's ladd goes down, rcktin8's line1 goes on, rcktin6's
#   event2 is the scene that ends with Shera, and rcktin3 has the hatch either way - its
#   line1 when open and its line4 to open it again when shut.
#   Back on the Highwind at 1389 the pilot, entity 16, is what moves the story; Red does
#   not need speaking to again.
#
# --- Back to Bugenhagen and the Capital, 1389 --------------------------------------------
#   541's Bugenhagen, entity 11, writes 1391. In the storage room he has nothing more to
#   say: what opens the way out is whichever huge materia is actually standing there -
#   entity 11 for the green one and entity 12 for the submarine's - through its own
#   Contact menu. Once he has left, the first-visit transport is not offered again.
#   The Capital: lost1's gateway3 west, lost3's jump2 - entity 4 - into the lake, then
#   loslake1's gateway1 to the crystal chamber. white1's lin0, entity 11, is the way back
#   for the key; white2's ev0 - entity 14 - is the explanation and again the projection.
#   Then loslake1's mjp0 - entity 17 - into the waterfall, white2's lin0 back out, and
#   loslake1's ev1 - entity 18 - for the cannon. At 1566 its gateway0 leads out.
$rocketGuardsBeaten = New-Condition -Bank 3 -Address 133 -Mask 0x02 -Value 0x02
$rocketGuardsPending = New-Condition -Bank 3 -Address 133 -Mask 0x02 -Value 0x00
$rocketDoorShut = New-Condition -Bank 5 -Address 16 -Mask 0xFF -Value 0x00
$rocketDoorOpen = New-Condition -Bank 5 -Address 16 -Mask 0xFF -Value 0x01
# rcktin3's bank5[8] is the shutter across the middle of the passage, not the escape
# hatch at the bottom of it. 565:5:4 opens the shutter - Confirm on line4, SHUTTER's
# opening animation, walkmesh triangles 20..23 unblocked, bank5[8] set to 1 - and
# 565:4:3 shuts it again as the party walks south past line3, blocking those same four
# triangles and clearing the byte. So the byte is already zero by the time the party is
# standing at the hatch, and gating the way out on it silenced the exit exactly where a
# player needs it.
$rocketShutterShut = New-Condition -Bank 5 -Address 8 -Mask 0xFF -Value 0x00

$rocketApproach = @{ MinimumGameMoment = 1299; MaximumGameMoment = 1313; Priority = 0 }
$rocketEscape = @{ MinimumGameMoment = 1314; MaximumGameMoment = 1388; Priority = 0 }

Add-Definition @rocketApproach -FieldId 557 -FieldName 'rckt' -Kind Location `
    -Label 'Go to the foot of the rocket' -X -141 -Y 2466 -Z 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -512; startY = 2453; startZ = 0; endX = 230; endY = 2479; endZ = 0 })

Add-Definition @rocketApproach -FieldId 561 -FieldName 'rcktbas1' -Kind Location -EntityId 4 `
    -Label 'Go past the guards' -X -1702 -Y 5650 -Z 0 `
    -RequiredCondition $rocketGuardsPending `
    -CompletedCondition $rocketGuardsBeaten `
    -EntityName 'bat1' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 4 `
    -TriggerLine ([ordered]@{ startX = -1554; startY = 5585; startZ = 0; endX = -1851; endY = 5716; endZ = 0 })

Add-Definition @rocketApproach -FieldId 561 -FieldName 'rcktbas1' -Kind Location -EntityId 3 `
    -Label 'Climb the ladder to the gantry' -X -1155 -Y 4977 -Z 688 `
    -RequiredCondition $rocketGuardsBeaten `
    -EntityName 'ladd' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 3 `
    -TriggerLine ([ordered]@{ startX = -1154; startY = 5021; startZ = 687; endX = -1157; endY = 4934; endZ = 690 })

Add-Definition @rocketApproach -FieldId 562 -FieldName 'rcktbas2' -Kind Location -EntityId 6 `
    -Label 'Cross the gantry' -X -1021 -Y 4754 -Z 1940 `
    -EntityName 'line' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 6 `
    -TriggerLine ([ordered]@{ startX = -978; startY = 4754; startZ = 1939; endX = -1065; endY = 4754; endZ = 1941 })

Add-Definition @rocketApproach -FieldId 564 -FieldName 'rcktin2' -Kind Location -EntityId 2 `
    -Label 'Press Confirm at the cockpit door' -X -12 -Y 372 -Z 0 `
    -RequiredCondition $rocketDoorShut `
    -CompletedCondition $rocketDoorOpen `
    -EntityName 'line1' -ScriptType 'Go' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
    -RequiredEnabledLineEntityId 2 `
    -TriggerLine ([ordered]@{ startX = -52; startY = 372; startZ = 0; endX = 28; endY = 372; endZ = 0 })

Add-Definition @rocketApproach -FieldId 564 -FieldName 'rcktin2' -Kind Location -EntityId 4 `
    -Label 'Go on through the open door' -X -11 -Y 699 -Z 0 `
    -RequiredCondition $rocketDoorOpen `
    -EntityName 'line3' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 4 `
    -TriggerLine ([ordered]@{ startX = -43; startY = 699; startZ = 0; endX = 21; endY = 699; endZ = 0 })

# --- Getting out again ------------------------------------------------------------------
Add-Definition @rocketEscape -FieldId 567 -FieldName 'rcktin5' -Kind Location -EntityId 7 `
    -Label 'Leave the chamber' -X 5 -Y -157 -Z 0 `
    -EntityName 'line1' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 7 `
    -TriggerLine ([ordered]@{ startX = -155; startY = -157; startZ = 0; endX = 165; endY = -157; endZ = 0 })

Add-Definition @rocketEscape -FieldId 564 -FieldName 'rcktin2' -Kind Location -EntityId 5 `
    -Label 'Climb down the ladder' -X -3 -Y -80 -Z 0 `
    -EntityName 'ladd' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 5 `
    -TriggerLine ([ordered]@{ startX = -17; startY = -80; startZ = 0; endX = 11; endY = -80; endZ = 0 })

Add-Definition @rocketEscape -FieldId 570 -FieldName 'rcktin8' -Kind Location -EntityId 2 `
    -Label 'Go on through the lower passage' -X -1 -Y -45 -Z 10 `
    -EntityName 'line1' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 2 `
    -TriggerLine ([ordered]@{ startX = -63; startY = -45; startZ = 10; endX = 61; endY = -45; endZ = 10 })

Add-Definition @rocketEscape -FieldId 568 -FieldName 'rcktin6' -Kind Location -EntityId 7 `
    -Label 'Cross the damaged chamber' -X 2 -Y 53 -Z 0 `
    -EntityName 'event2' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 7 `
    -TriggerLine ([ordered]@{ startX = -100; startY = 53; startZ = 0; endX = 104; endY = 53; endZ = 0 })

Add-Definition @rocketEscape -FieldId 565 -FieldName 'rcktin3' -Kind Location -EntityId 2 `
    -Label 'Go out through the escape hatch' -X 1 -Y -272 -Z 0 `
    -EntityName 'line1' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 2 `
    -TriggerLine ([ordered]@{ startX = -69; startY = -272; startZ = 0; endX = 71; endY = -272; endZ = 0 })

Add-Definition @rocketEscape -FieldId 565 -FieldName 'rcktin3' -Kind Location -EntityId 5 `
    -Label 'Press Confirm to open the shutter again' -X 5 -Y 45 -Z 0 `
    -RequiredCondition $rocketShutterShut `
    -EntityName 'line4' -ScriptType 'Go' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
    -RequiredEnabledLineEntityId 5 `
    -TriggerLine ([ordered]@{ startX = -26; startY = 45; startZ = 0; endX = 37; endY = 45; endZ = 0 })

Add-Definition -FieldId 72 -FieldName 'fship_22' -Kind Model -EntityId 16 -Priority 0 `
    -Label 'Talk to the pilot' `
    -MinimumGameMoment 1389 -MaximumGameMoment 1390 `
    -EntityName 'crew3' -ScriptType 'Talk'

# --- Bugenhagen and the Capital -------------------------------------------------------------
$bugenhagenAboard = New-Condition -Bank 3 -Address 188 -Mask 0x08 -Value 0x00
# cosmo3's four stands are gated by four separate flags, and 566:14:4 shows what
# those flags are: it walks them in a fixed order and writes the first one that is
# still clear, so they are storage slots filled in the order the Huge Materia were
# brought in, not one flag per materia. Nothing in the field ties a stand to a
# colour or to the mission a materia came from, so neither is named here.
$firstHugeMateriaSlot = New-Condition -Bank 15 -Address 145 -Mask 0x08 -Value 0x08
$secondHugeMateriaSlot = New-Condition -Bank 13 -Address 82 -Mask 0x10 -Value 0x10
$thirdHugeMateriaSlot = New-Condition -Bank 13 -Address 82 -Mask 0x01 -Value 0x01
$fourthHugeMateriaSlot = New-Condition -Bank 13 -Address 82 -Mask 0x80 -Value 0x80
$keyNotYetFetched = New-Condition -Bank 13 -Address 82 -Mask 0x02 -Value 0x00
$keyFetched = New-Condition -Bank 13 -Address 82 -Mask 0x02 -Value 0x02

Add-Definition -FieldId 541 -FieldName 'cosmo2' -Kind Model -EntityId 11 -Priority 0 `
    -Label 'Talk to Bugenhagen' `
    -MinimumGameMoment 1389 -MaximumGameMoment 1390 `
    -EntityName 'bugen' -ScriptType 'Talk'

# The Huge Materia standing in the storage room are reached by walking into them:
# their Contact script is entity script 2 and they have no Talk script at all, so
# nothing here is pressed. Each stand is offered only while its own slot flag says a
# materia is actually on it.
Add-Definition -FieldId 542 -FieldName 'cosmo3' -Kind Model -EntityId 12 -Priority 0 `
    -Label 'Go to the first stored materia' `
    -MinimumGameMoment 1391 -MaximumGameMoment 1391 `
    -RequiredConditions @($bugenhagenAboard, $firstHugeMateriaSlot) `
    -EntityName 'HUGEB' -ScriptType 'Contact' -UsesContactRange

Add-Definition -FieldId 542 -FieldName 'cosmo3' -Kind Model -EntityId 11 -Priority 0 `
    -Label 'Go to the second stored materia' `
    -MinimumGameMoment 1391 -MaximumGameMoment 1391 `
    -RequiredConditions @($bugenhagenAboard, $secondHugeMateriaSlot) `
    -EntityName 'HUGEA' -ScriptType 'Contact' -UsesContactRange

Add-Definition -FieldId 542 -FieldName 'cosmo3' -Kind Model -EntityId 13 -Priority 0 `
    -Label 'Go to the third stored materia' `
    -MinimumGameMoment 1391 -MaximumGameMoment 1391 `
    -RequiredConditions @($bugenhagenAboard, $thirdHugeMateriaSlot) `
    -EntityName 'HUGEC' -ScriptType 'Contact' -UsesContactRange

Add-Definition -FieldId 542 -FieldName 'cosmo3' -Kind Model -EntityId 14 -Priority 0 `
    -Label 'Go to the fourth stored materia' `
    -MinimumGameMoment 1391 -MaximumGameMoment 1391 `
    -RequiredConditions @($bugenhagenAboard, $fourthHugeMateriaSlot) `
    -EntityName 'HUGED' -ScriptType 'Contact' -UsesContactRange

Add-Definition -FieldId 630 -FieldName 'lost1' -Kind Location -Priority 0 `
    -Label 'Take the western path' -X -4464 -Y 1512 -Z 149 `
    -MinimumGameMoment 1391 -MaximumGameMoment 1391 `
    -EntityName 'gateway3' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -4146; startY = 1422; startZ = 153; endX = -4782; endY = 1603; endZ = 145 })

Add-Definition -FieldId 635 -FieldName 'lost3' -Kind Location -EntityId 4 -Priority 0 `
    -Label 'Go down into the lake' -X -2789 -Y 9778 -Z 1052 `
    -MinimumGameMoment 1391 -MaximumGameMoment 1391 `
    -EntityName 'jump2' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 4 `
    -TriggerLine ([ordered]@{ startX = -2760; startY = 9852; startZ = 1052; endX = -2818; endY = 9705; endZ = 1052 })

Add-Definition -FieldId 637 -FieldName 'loslake1' -Kind Location -Priority 0 `
    -Label 'Go through to the crystal chamber' -X 565 -Y -311 -Z -268 `
    -MinimumGameMoment 1392 -MaximumGameMoment 1395 `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = 566; startY = -357; startZ = -267; endX = 565; endY = -265; endZ = -269 })

Add-Definition -FieldId 642 -FieldName 'white1' -Kind Location -EntityId 12 -Priority 0 `
    -Label 'Go on to the crystal' -X -219 -Y -482 -Z 138 `
    -MinimumGameMoment 1392 -MaximumGameMoment 1395 `
    -EntityName 'ev0' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 12 `
    -TriggerLine ([ordered]@{ startX = -267; startY = -460; startZ = 139; endX = -172; endY = -505; endZ = 138 })

Add-Definition -FieldId 642 -FieldName 'white1' -Kind Location -EntityId 11 -Priority 0 `
    -Label 'Go back for the key' -X -440 -Y -982 -Z 89 `
    -MinimumGameMoment 1396 -MaximumGameMoment 1398 `
    -RequiredCondition $keyNotYetFetched `
    -EntityName 'lin0' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 11 `
    -TriggerLine ([ordered]@{ startX = -488; startY = -961; startZ = 89; endX = -392; endY = -1003; endZ = 89 })

Add-Definition -FieldId 642 -FieldName 'white1' -Kind Model -EntityId 10 -Priority 0 `
    -Label 'Give the key to Bugenhagen' `
    -MinimumGameMoment 1396 -MaximumGameMoment 1398 `
    -RequiredCondition $keyFetched `
    -EntityName 'bugen' -ScriptType 'Talk'

Add-Definition -FieldId 637 -FieldName 'loslake1' -Kind Location -EntityId 17 -Priority 0 `
    -Label 'Go into the waterfall' -X 609 -Y -329 -Z -268 `
    -MinimumGameMoment 1399 -MaximumGameMoment 1399 `
    -EntityName 'mjp0' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 17 `
    -TriggerLine ([ordered]@{ startX = 624; startY = -391; startZ = -267; endX = 595; endY = -268; endZ = -270 })

Add-Definition -FieldId 643 -FieldName 'white2' -Kind Location -EntityId 14 -Priority 0 `
    -Label 'Go on to the projection' -X -219 -Y -482 -Z 138 `
    -MinimumGameMoment 1399 -MaximumGameMoment 1399 `
    -EntityName 'ev0' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 14 `
    -TriggerLine ([ordered]@{ startX = -267; startY = -460; startZ = 139; endX = -172; endY = -505; endZ = 138 })

Add-Definition -FieldId 643 -FieldName 'white2' -Kind Location -EntityId 13 -Priority 0 `
    -Label 'Go back outside' -X -288 -Y -642 -Z 88 `
    -MinimumGameMoment 1400 -MaximumGameMoment 1400 `
    -EntityName 'lin0' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 13 `
    -TriggerLine ([ordered]@{ startX = -332; startY = -624; startZ = 88; endX = -244; endY = -660; endZ = 88 })

Add-Definition -FieldId 637 -FieldName 'loslake1' -Kind Location -EntityId 18 -Priority 0 `
    -Label 'Go on to the lake shore' -X 796 -Y -314 -Z -260 `
    -MinimumGameMoment 1400 -MaximumGameMoment 1400 `
    -EntityName 'ev1' -ScriptType 'Move' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 18 `
    -TriggerLine ([ordered]@{ startX = 746; startY = -252; startZ = -266; endX = 847; endY = -376; endZ = -255 })

Add-Definition -FieldId 637 -FieldName 'loslake1' -Kind Location -Priority 0 `
    -Label 'Leave the lake' -X -1193 -Y -46 -Z -266 `
    -MinimumGameMoment 1566 -MaximumGameMoment 1566 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX = -1113; startY = -134; startZ = -266; endX = -1274; endY = 41; endZ = -266 })

Add-CuratedFields 557, 561, 562, 564, 565, 567, 568, 570, 72, 541, 542, 630, 635, 637, 642, 643
