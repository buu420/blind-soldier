# Reviewed walked links between main-story events. These rooms do not write a new
# story counter, so milestone extraction cannot discover their required next door.
# Geometry is from the installed field archive; direction and chapter bands were
# traced through incoming/outgoing scripts and the Absolute Steve walkthrough.
# No shortest-path or nearest-milestone fallback chooses objectives here.
# Corresponding checkpoints assert target state, native destination and routes from
# native arrival positions. These checks do not substitute for a live playthrough.

# 619 lin0 stops looping at 652 and maps625. sango1 gateway1 leads626; no automatic counter write.
Add-Definition -FieldId 625 -FieldName 'sango1' -Kind Location -Priority 0 `
    -Label 'Continue through Coral Valley' -MinimumGameMoment 652 -MaximumGameMoment 663 `
    -X -455 -Y 2949 -Z -27 `
    -TriggerLine ([ordered]@{ startX = -515; startY = 2938; startZ = -27; endX = -395; endY = 2960; endZ = -27 }) `
    -EntityName 'gateway1' -ScriptType 'Gateway'

# sango2 gateway0 leads world57; the next world entrance is lost1. Ordinary playable corridor.
Add-Definition -FieldId 626 -FieldName 'sango2' -Kind Location -Priority 0 `
    -Label 'Leave the valley toward the Forgotten Capital' -MinimumGameMoment 652 -MaximumGameMoment 663 `
    -X -753 -Y -9219 -Z 3668 `
    -TriggerLine ([ordered]@{ startX = -698; startY = -8963; startZ = 3554; endX = -808; endY = -9475; endZ = 3782 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# First visit: resting in losinn writes664. These side rooms do not advance the counter; return to lost1/lost2 and the sleeping house.
Add-Definition -FieldId 632 -FieldName 'losin2' -Kind Location -Priority 0 `
    -Label 'Return outside to the eastern houses' -MinimumGameMoment 652 -MaximumGameMoment 663 `
    -X 219 -Y 133 -Z -102 `
    -TriggerLine ([ordered]@{ startX = 217; startY = 96; startZ = -102; endX = 221; endY = 171; endZ = -102 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# First visit: resting in losinn writes664. These side rooms do not advance the counter; return to lost1/lost2 and the sleeping house.
Add-Definition -FieldId 633 -FieldName 'losin3' -Kind Location -Priority 0 `
    -Label 'Return outside to the western path' -MinimumGameMoment 652 -MaximumGameMoment 663 `
    -X 84 -Y -347 -Z 0 `
    -TriggerLine ([ordered]@{ startX = 43; startY = -352; startZ = 0; endX = 126; endY = -343; endZ = 0 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# First visit: resting in losinn writes664. These side rooms do not advance the counter; return to lost1/lost2 and the sleeping house.
Add-Definition -FieldId 635 -FieldName 'lost3' -Kind Location -Priority 0 `
    -Label 'Return to the crossroads' -MinimumGameMoment 652 -MaximumGameMoment 663 -EntityId 3 `
    -X -5063 -Y 6178 -Z 359 `
    -RequiredEnabledLineEntityId 3 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = -5136; startY = 6169; startZ = 359; endX = -4990; endY = 6187; endZ = 359 }) `
    -EntityName 'jump1' -ScriptType 'Go'

# First visit: resting in losinn writes664. These side rooms do not advance the counter; return to lost1/lost2 and the sleeping house.
Add-Definition -FieldId 640 -FieldName 'blue_1' -Kind Location -Priority 0 `
    -Label 'Return to the crossroads' -MinimumGameMoment 652 -MaximumGameMoment 663 -EntityId 4 `
    -X -9589 -Y 5709 -Z 276 `
    -RequiredEnabledLineEntityId 4 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = -9604; startY = 5681; startZ = 276; endX = -9574; endY = 5738; endZ = 276 }) `
    -EntityName 'lin0' -ScriptType 'Move'

# First visit: resting in losinn writes664. These side rooms do not advance the counter; return to lost1/lost2 and the sleeping house.
Add-Definition -FieldId 641 -FieldName 'blue_2' -Kind Location -Priority 0 `
    -Label 'Leave the blue building' -MinimumGameMoment 652 -MaximumGameMoment 663 `
    -X 260 -Y -1652 -Z 33 `
    -TriggerLine ([ordered]@{ startX = 215; startY = -1648; startZ = 33; endX = 306; endY = -1656; endZ = 33 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# After resting, return from optional rooms to lost1 and the blue building; bank3[132] bit4 is set by the rest.
Add-Definition -FieldId 632 -FieldName 'losin2' -Kind Location -Priority 0 `
    -Label 'Return outside to the eastern houses' -MinimumGameMoment 664 -MaximumGameMoment 672 `
    -X 219 -Y 133 -Z -102 `
    -TriggerLine ([ordered]@{ startX = 217; startY = 96; startZ = -102; endX = 221; endY = 171; endZ = -102 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# After resting, return from optional rooms to lost1 and the blue building; bank3[132] bit4 is set by the rest.
Add-Definition -FieldId 633 -FieldName 'losin3' -Kind Location -Priority 0 `
    -Label 'Return outside to the western path' -MinimumGameMoment 664 -MaximumGameMoment 672 `
    -X 84 -Y -347 -Z 0 `
    -TriggerLine ([ordered]@{ startX = 43; startY = -352; startZ = 0; endX = 126; endY = -343; endZ = 0 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# After resting, return from optional rooms to lost1 and the blue building; bank3[132] bit4 is set by the rest.
Add-Definition -FieldId 635 -FieldName 'lost3' -Kind Location -Priority 0 `
    -Label 'Return to the crossroads' -MinimumGameMoment 664 -MaximumGameMoment 672 -EntityId 3 `
    -X -5063 -Y 6178 -Z 359 `
    -RequiredEnabledLineEntityId 3 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = -5136; startY = 6169; startZ = 359; endX = -4990; endY = 6187; endZ = 359 }) `
    -EntityName 'jump1' -ScriptType 'Go'

# Bugenhagen waits in white1; at1396..1398 with the Key (13[82] bit1) the route returns to him.
Add-Definition -FieldId 630 -FieldName 'lost1' -Kind Location -Priority 0 `
    -Label 'Take the western path to the lake' -MinimumGameMoment 1392 -MaximumGameMoment 1395 `
    -X -4464 -Y 1512 -Z 149 `
    -TriggerLine ([ordered]@{ startX = -4146; startY = 1422; startZ = 153; endX = -4782; endY = 1603; endZ = 145 }) `
    -EntityName 'gateway3' -ScriptType 'Gateway'

# lost3 jump2 maps637; the key branch proceeds to white1.
Add-Definition -FieldId 635 -FieldName 'lost3' -Kind Location -Priority 0 `
    -Label 'Go down into the lake' -MinimumGameMoment 1392 -MaximumGameMoment 1395 -EntityId 4 `
    -X -2789 -Y 9778 -Z 1052 `
    -RequiredEnabledLineEntityId 4 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = -2760; startY = 9852; startZ = 1052; endX = -2818; endY = 9705; endZ = 1052 }) `
    -EntityName 'jump2' -ScriptType 'Go'

# Bugenhagen waits in white1; at1396..1398 with the Key (13[82] bit1) the route returns to him.
Add-Definition -FieldId 630 -FieldName 'lost1' -Kind Location -Priority 0 `
    -Label 'Take the western path to the lake' -MinimumGameMoment 1396 -MaximumGameMoment 1398 `
    -X -4464 -Y 1512 -Z 149 `
    -TriggerLine ([ordered]@{ startX = -4146; startY = 1422; startZ = 153; endX = -4782; endY = 1603; endZ = 145 }) `
    -RequiredConditions @((New-Condition 13 82 2 2)) `
    -EntityName 'gateway3' -ScriptType 'Gateway'

# lost3 jump2 maps637; the key branch proceeds to white1.
Add-Definition -FieldId 635 -FieldName 'lost3' -Kind Location -Priority 0 `
    -Label 'Go down into the lake' -MinimumGameMoment 1396 -MaximumGameMoment 1398 -EntityId 4 `
    -X -2789 -Y 9778 -Z 1052 `
    -RequiredEnabledLineEntityId 4 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = -2760; startY = 9852; startZ = 1052; endX = -2818; endY = 9705; endZ = 1052 }) `
    -RequiredConditions @((New-Condition 13 82 2 2)) `
    -EntityName 'jump2' -ScriptType 'Go'

# white1 Bugenhagen Talk accepts Key13[82] bit1 and starts1397..1399 automatic scene chain.
Add-Definition -FieldId 637 -FieldName 'loslake1' -Kind Location -Priority 0 `
    -Label 'Return to Bugenhagen with the key' -MinimumGameMoment 1396 -MaximumGameMoment 1398 `
    -X 565 -Y -311 -Z -268 `
    -TriggerLine ([ordered]@{ startX = 566; startY = -357; startZ = -267; endX = 565; endY = -265; endZ = -269 }) `
    -RequiredConditions @((New-Condition 13 82 2 2)) `
    -EntityName 'gateway1' -ScriptType 'Gateway'

# white1 line11 sends the player back for the Key while13[82] bit1 is clear; no automatic transition in these return rooms.
Add-Definition -FieldId 637 -FieldName 'loslake1' -Kind Location -Priority 0 `
    -Label 'Leave the lake to look for the key' -MinimumGameMoment 1396 -MaximumGameMoment 1398 `
    -X -1193 -Y -46 -Z -266 `
    -TriggerLine ([ordered]@{ startX = -1113; startY = -134; startZ = -266; endX = -1274; endY = 41; endZ = -266 }) `
    -RequiredConditions @((New-Condition 13 82 2 0)) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# white1 line11 sends the player back for the Key while13[82] bit1 is clear; no automatic transition in these return rooms.
Add-Definition -FieldId 635 -FieldName 'lost3' -Kind Location -Priority 0 `
    -Label 'Return to the crossroads' -MinimumGameMoment 1396 -MaximumGameMoment 1398 -EntityId 3 `
    -X -5063 -Y 6178 -Z 359 `
    -RequiredEnabledLineEntityId 3 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = -5136; startY = 6169; startZ = 359; endX = -4990; endY = 6187; endZ = 359 }) `
    -RequiredConditions @((New-Condition 13 82 2 0)) `
    -EntityName 'jump1' -ScriptType 'Go'

# white1 line11 sends the player back for the Key while13[82] bit1 is clear; no automatic transition in these return rooms.
Add-Definition -FieldId 630 -FieldName 'lost1' -Kind Location -Priority 0 `
    -Label 'Leave the capital to look for the key' -MinimumGameMoment 1396 -MaximumGameMoment 1398 `
    -X -774 -Y 708 -Z 94 `
    -TriggerLine ([ordered]@{ startX = -815; startY = 628; startZ = 95; endX = -733; endY = 789; endZ = 94 }) `
    -RequiredConditions @((New-Condition 13 82 2 0)) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# blin70_4 writes1566 and maps637; after existing lake exit the walked path is635 jump1 then630 gateway0 toworld58.
Add-Definition -FieldId 635 -FieldName 'lost3' -Kind Location -Priority 0 `
    -Label 'Return to the crossroads' -MinimumGameMoment 1566 -MaximumGameMoment 1567 -EntityId 3 `
    -X -5063 -Y 6178 -Z 359 `
    -RequiredEnabledLineEntityId 3 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = -5136; startY = 6169; startZ = 359; endX = -4990; endY = 6187; endZ = 359 }) `
    -EntityName 'jump1' -ScriptType 'Go'

# blin70_4 writes1566 and maps637; after existing lake exit the walked path is635 jump1 then630 gateway0 toworld58.
Add-Definition -FieldId 630 -FieldName 'lost1' -Kind Location -Priority 0 `
    -Label 'Leave the capital for the Highwind' -MinimumGameMoment 1566 -MaximumGameMoment 1567 `
    -X -774 -Y 708 -Z 94 `
    -TriggerLine ([ordered]@{ startX = -815; startY = 628; startZ = 95; endX = -733; endY = 789; endZ = 94 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# fship_3 gateway0 leads74 after party selection;74 jump takes field70 below1199, whose pilot can take off.
Add-Definition -FieldId 74 -FieldName 'fship_4' -Kind Location -Priority 0 `
    -Label 'Go forward to the cockpit' -MinimumGameMoment 1033 -MaximumGameMoment 1034 -EntityId 6 `
    -X 997 -Y -820 -Z -417 `
    -RequiredEnabledLineEntityId 6 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = 1016; startY = -765; startZ = -401; endX = 978; endY = -876; endZ = -433 }) `
    -EntityName 'jump' -ScriptType 'Move'

# 74 jump uses field72 from1199 onward. These free-flight bands exclude the automatic end-of-disc departure at1612.
Add-Definition -FieldId 74 -FieldName 'fship_4' -Kind Location -Priority 0 `
    -Label 'Go forward to the deck' -MinimumGameMoment 1250 -MaximumGameMoment 1387 -EntityId 6 `
    -X 997 -Y -820 -Z -417 `
    -RequiredEnabledLineEntityId 6 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = 1016; startY = -765; startZ = -401; endX = 978; endY = -876; endZ = -433 }) `
    -EntityName 'jump' -ScriptType 'Move'

# Operations room after1199 scene completes at1250; gateway0 leads74 and no further mandatory party scene runs in this band.
Add-Definition -FieldId 73 -FieldName 'fship_3' -Kind Location -Priority 0 `
    -Label 'Return to the corridor' -MinimumGameMoment 1250 -MaximumGameMoment 1387 `
    -X -2 -Y -450 -Z 0 `
    -TriggerLine ([ordered]@{ startX = -84; startY = -450; startZ = 0; endX = 80; endY = -450; endZ = 0 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# 74 jump uses field72 from1199 onward. These free-flight bands exclude the automatic end-of-disc departure at1612.
Add-Definition -FieldId 74 -FieldName 'fship_4' -Kind Location -Priority 0 `
    -Label 'Go forward to the deck' -MinimumGameMoment 1389 -MaximumGameMoment 1565 -EntityId 6 `
    -X 997 -Y -820 -Z -417 `
    -RequiredEnabledLineEntityId 6 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = 1016; startY = -765; startZ = -401; endX = 978; endY = -876; endZ = -433 }) `
    -EntityName 'jump' -ScriptType 'Move'

# Operations room after1199 scene completes at1250; gateway0 leads74 and no further mandatory party scene runs in this band.
Add-Definition -FieldId 73 -FieldName 'fship_3' -Kind Location -Priority 0 `
    -Label 'Return to the corridor' -MinimumGameMoment 1389 -MaximumGameMoment 1565 `
    -X -2 -Y -450 -Z 0 `
    -TriggerLine ([ordered]@{ startX = -84; startY = -450; startZ = 0; endX = 80; endY = -450; endZ = 0 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# 74 jump uses field72 from1199 onward. These free-flight bands exclude the automatic end-of-disc departure at1612.
Add-Definition -FieldId 74 -FieldName 'fship_4' -Kind Location -Priority 0 `
    -Label 'Go forward to the deck' -MinimumGameMoment 1582 -MaximumGameMoment 1611 -EntityId 6 `
    -X 997 -Y -820 -Z -417 `
    -RequiredEnabledLineEntityId 6 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = 1016; startY = -765; startZ = -401; endX = 978; endY = -876; endZ = -433 }) `
    -EntityName 'jump' -ScriptType 'Move'

# Operations room after1199 scene completes at1250; gateway0 leads74 and no further mandatory party scene runs in this band.
Add-Definition -FieldId 73 -FieldName 'fship_3' -Kind Location -Priority 0 `
    -Label 'Return to the corridor' -MinimumGameMoment 1582 -MaximumGameMoment 1611 `
    -X -2 -Y -450 -Z 0 `
    -TriggerLine ([ordered]@{ startX = -84; startY = -450; startZ = 0; endX = 80; endY = -450; endZ = 0 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# 72 crew3 Talk has explicit mission-counter dialogue branches and MAPJUMP45 at446 when takeoff is confirmed; it is not an automatic scene.
Add-Definition -FieldId 72 -FieldName 'fship_25' -Kind Model -Priority 0 `
    -Label 'Talk to the pilot to take off' -MinimumGameMoment 1250 -MaximumGameMoment 1387 -EntityId 16 `
    -EntityName 'crew3' -ScriptType 'Talk'

# 72 crew3 Talk has explicit mission-counter dialogue branches and MAPJUMP45 at446 when takeoff is confirmed; it is not an automatic scene.
Add-Definition -FieldId 72 -FieldName 'fship_25' -Kind Model -Priority 0 `
    -Label 'Talk to the pilot to take off' -MinimumGameMoment 1391 -MaximumGameMoment 1565 -EntityId 16 `
    -EntityName 'crew3' -ScriptType 'Talk'

# 72 crew3 Talk has explicit mission-counter dialogue branches and MAPJUMP45 at446 when takeoff is confirmed; it is not an automatic scene.
Add-Definition -FieldId 72 -FieldName 'fship_25' -Kind Model -Priority 0 `
    -Label 'Talk to the pilot to take off' -MinimumGameMoment 1582 -MaximumGameMoment 1611 -EntityId 16 `
    -EntityName 'crew3' -ScriptType 'Talk'

# Junon reactor approach:428 guard ->395 ->391 ->386 ->360 ->361 ->390. Marching soldiers in390 write1253; existing elevator target then leads388.
Add-Definition -FieldId 386 -FieldName 'junin1' -Kind Location -Priority 0 `
    -Label 'Leave the lift hall for the main street' -MinimumGameMoment 1250 -MaximumGameMoment 1255 `
    -X -828 -Y -818 -Z 767 `
    -TriggerLine ([ordered]@{ startX = -896; startY = -818; startZ = 767; endX = -760; endY = -818; endZ = 767 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# Junon reactor approach:428 guard ->395 ->391 ->386 ->360 ->361 ->390. Marching soldiers in390 write1253; existing elevator target then leads388.
Add-Definition -FieldId 387 -FieldName 'junin1a' -Kind Location -Priority 0 `
    -Label 'Leave the locker room for the lift hall' -MinimumGameMoment 1250 -MaximumGameMoment 1255 `
    -X -1270 -Y -651 -Z 605 `
    -TriggerLine ([ordered]@{ startX = -1269; startY = -688; startZ = 605; endX = -1272; endY = -615; endZ = 605 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# Junon reactor approach:428 guard ->395 ->391 ->386 ->360 ->361 ->390. Marching soldiers in390 write1253; existing elevator target then leads388.
Add-Definition -FieldId 360 -FieldName 'junonr1' -Kind Location -Priority 0 `
    -Label 'Follow the main street toward the reactor passage' -MinimumGameMoment 1250 -MaximumGameMoment 1255 `
    -X -2345 -Y -1191 -Z -3 `
    -TriggerLine ([ordered]@{ startX = -2306; startY = -1486; startZ = -7; endX = -2385; endY = -897; endZ = 0 }) `
    -EntityName 'gateway1' -ScriptType 'Gateway'

# Junon reactor approach:428 guard ->395 ->391 ->386 ->360 ->361 ->390. Marching soldiers in390 write1253; existing elevator target then leads388.
Add-Definition -FieldId 361 -FieldName 'junonr2' -Kind Location -Priority 0 `
    -Label 'Enter the passage with the marching soldiers' -MinimumGameMoment 1250 -MaximumGameMoment 1255 `
    -X 4355 -Y -4952 -Z -3407 `
    -TriggerLine ([ordered]@{ startX = 4587; startY = -5157; startZ = -3407; endX = 4124; endY = -4748; endZ = -3407 }) `
    -EntityName 'gateway1' -ScriptType 'Gateway'

# Junon reactor approach:428 guard ->395 ->391 ->386 ->360 ->361 ->390. Marching soldiers in390 write1253; existing elevator target then leads388.
Add-Definition -FieldId 371 -FieldName 'junonl2' -Kind Location -Priority 0 `
    -Label 'Return to the reactor passage' -MinimumGameMoment 1250 -MaximumGameMoment 1255 `
    -X 4923 -Y -457 -Z -6 `
    -TriggerLine ([ordered]@{ startX = 4935; startY = -132; startZ = -6; endX = 4912; endY = -782; endZ = -6 }) `
    -EntityName 'gateway1' -ScriptType 'Gateway'

# 388 Cloud Script3 runs the guard battle/ride and returns control. border1 maps392 when lastfield390;392 arrival writes1256.
Add-Definition -FieldId 388 -FieldName 'junele1' -Kind Location -Priority 0 `
    -Label 'Leave the elevator at the submarine dock' -MinimumGameMoment 1253 -MaximumGameMoment 1255 -EntityId 6 `
    -X -683 -Y -498 -Z 605 `
    -RequiredEnabledLineEntityId 6 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = -678; startY = -528; startZ = 605; endX = -689; endY = -468; endZ = 605 }) `
    -EntityName 'border1' -ScriptType 'Move'

# Reactor chain:392->393->394->391/395->417->418->419->420 switch->421->422->423->424 crane line->425 Carry Armor->426->427. Counter stays1256 until408 boarding writes1280.
Add-Definition -FieldId 393 -FieldName 'junin5' -Kind Location -Priority 0 `
    -Label 'Continue down the stairs' -MinimumGameMoment 1256 -MaximumGameMoment 1279 `
    -X 253 -Y -492 -Z 432 `
    -TriggerLine ([ordered]@{ startX = 215; startY = -488; startZ = 433; endX = 291; endY = -496; endZ = 432 }) `
    -EntityName 'gateway1' -ScriptType 'Gateway'

# Reactor chain:392->393->394->391/395->417->418->419->420 switch->421->422->423->424 crane line->425 Carry Armor->426->427. Counter stays1256 until408 boarding writes1280.
Add-Definition -FieldId 417 -FieldName 'spgate' -Kind Location -Priority 0 `
    -Label 'Enter the underwater tunnel' -MinimumGameMoment 1256 -MaximumGameMoment 1279 `
    -X 0 -Y 2356 -Z 22 `
    -TriggerLine ([ordered]@{ startX = -74; startY = 2358; startZ = 22; endX = 73; endY = 2355; endZ = 22 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# Reactor chain:392->393->394->391/395->417->418->419->420 switch->421->422->423->424 crane line->425 Carry Armor->426->427. Counter stays1256 until408 boarding writes1280.
Add-Definition -FieldId 418 -FieldName 'spipe_1' -Kind Location -Priority 0 `
    -Label 'Follow the tunnel toward the reactor' -MinimumGameMoment 1256 -MaximumGameMoment 1279 `
    -X 969 -Y 1199 -Z -742 `
    -TriggerLine ([ordered]@{ startX = 1079; startY = 1560; startZ = -742; endX = 860; endY = 838; endZ = -742 }) `
    -EntityName 'gateway1' -ScriptType 'Gateway'

# Reactor chain:392->393->394->391/395->417->418->419->420 switch->421->422->423->424 crane line->425 Carry Armor->426->427. Counter stays1256 until408 boarding writes1280.
Add-Definition -FieldId 419 -FieldName 'spipe_2' -Kind Location -Priority 0 `
    -Label 'Enter the reactor lift' -MinimumGameMoment 1256 -MaximumGameMoment 1279 `
    -X 0 -Y 402 -Z -742 `
    -TriggerLine ([ordered]@{ startX = -104; startY = 403; startZ = -742; endX = 104; endY = 402; endZ = -742 }) `
    -EntityName 'gateway1' -ScriptType 'Gateway'

# Reactor chain:392->393->394->391/395->417->418->419->420 switch->421->422->423->424 crane line->425 Carry Armor->426->427. Counter stays1256 until408 boarding writes1280.
Add-Definition -FieldId 421 -FieldName 'semkin_2' -Kind Location -Priority 0 `
    -Label 'Continue into the reactor' -MinimumGameMoment 1256 -MaximumGameMoment 1279 `
    -X -2373 -Y -1889 -Z 384 `
    -TriggerLine ([ordered]@{ startX = -2310; startY = -1980; startZ = 384; endX = -2437; endY = -1799; endZ = 384 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# Reactor chain:392->393->394->391/395->417->418->419->420 switch->421->422->423->424 crane line->425 Carry Armor->426->427. Counter stays1256 until408 boarding writes1280.
Add-Definition -FieldId 422 -FieldName 'semkin_8' -Kind Location -Priority 0 `
    -Label 'Follow the reactor passage' -MinimumGameMoment 1256 -MaximumGameMoment 1279 `
    -X 192 -Y 1170 -Z 0 `
    -TriggerLine ([ordered]@{ startX = 203; startY = 1239; startZ = 0; endX = 182; endY = 1101; endZ = 0 }) `
    -EntityName 'gateway1' -ScriptType 'Gateway'

# Reactor chain:392->393->394->391/395->417->418->419->420 switch->421->422->423->424 crane line->425 Carry Armor->426->427. Counter stays1256 until408 boarding writes1280.
Add-Definition -FieldId 423 -FieldName 'semkin_3' -Kind Location -Priority 0 `
    -Label 'Continue toward the crane hall' -MinimumGameMoment 1256 -MaximumGameMoment 1279 `
    -X -1483 -Y 4527 -Z -265 `
    -TriggerLine ([ordered]@{ startX = -1563; startY = 4527; startZ = -273; endX = -1404; endY = 4527; endZ = -257 }) `
    -EntityName 'gateway1' -ScriptType 'Gateway'

# Reactor chain:392->393->394->391/395->417->418->419->420 switch->421->422->423->424 crane line->425 Carry Armor->426->427. Counter stays1256 until408 boarding writes1280.
Add-Definition -FieldId 426 -FieldName 'semkin_6' -Kind Location -Priority 0 `
    -Label 'Continue past the crane to the submarine dock' -MinimumGameMoment 1256 -MaximumGameMoment 1279 `
    -X 2186 -Y -1405 -Z 7 `
    -TriggerLine ([ordered]@{ startX = 2044; startY = -1045; startZ = 7; endX = 2329; endY = -1766; endZ = 8 }) `
    -EntityName 'gateway1' -ScriptType 'Gateway'

# semkin_4 jump14 MAPJUMP425 leads the Carry Armor trigger; when the battle is complete the native scene hands back in426.
Add-Definition -FieldId 424 -FieldName 'semkin_4' -Kind Location -Priority 0 `
    -Label 'Enter the crane hall' -MinimumGameMoment 1256 -MaximumGameMoment 1279 -EntityId 14 `
    -X 1017 -Y -240 -Z 24 `
    -RequiredEnabledLineEntityId 14 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = 1017; startY = -190; startZ = 24; endX = 1017; endY = -290; endZ = 24 }) `
    -EntityName 'jump' -ScriptType 'Move'

# semkin_7 vs_ssol Move fights the dock guards, sets15[133] bit5 and maps408;408 boarding scene writes1280.
Add-Definition -FieldId 427 -FieldName 'semkin_7' -Kind Location -Priority 0 `
    -Label 'Approach the guards at the submarine' -MinimumGameMoment 1256 -MaximumGameMoment 1279 -EntityId 4 `
    -X 2701 -Y 278 -Z 233 `
    -RequiredEnabledLineEntityId 4 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = 2705; startY = 319; startZ = 233; endX = 2697; endY = 237; endZ = 233 }) `
    -EntityName 'vs_ssol' -ScriptType 'Move'

# Corel pursuit: world14->458->459->460, or North Corel450->467->464->462->461->460. The native guard event in460 starts the pursuit only while15[144] bit0 is clear.
Add-Definition -FieldId 450 -FieldName 'ncorel' -Kind Location -Priority 0 `
    -Label 'Take the mountain path to the reactor' -MinimumGameMoment 1110 -MaximumGameMoment 1115 `
    -X 902 -Y 1898 -Z 0 `
    -TriggerLine ([ordered]@{ startX = 609; startY = 1897; startZ = 0; endX = 1196; endY = 1900; endZ = 0 }) `
    -RequiredConditions @((New-Condition 15 144 1 0)) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# Corel pursuit: world14->458->459->460, or North Corel450->467->464->462->461->460. The native guard event in460 starts the pursuit only while15[144] bit0 is clear.
Add-Definition -FieldId 467 -FieldName 'mtcrl_9' -Kind Location -Priority 0 `
    -Label 'Cross the bridge toward the mountain' -MinimumGameMoment 1110 -MaximumGameMoment 1115 `
    -X -1 -Y 7088 -Z 1125 `
    -TriggerLine ([ordered]@{ startX = -91; startY = 7088; startZ = 1125; endX = 89; endY = 7088; endZ = 1125 }) `
    -RequiredConditions @((New-Condition 15 144 1 0)) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# Corel pursuit: world14->458->459->460, or North Corel450->467->464->462->461->460. The native guard event in460 starts the pursuit only while15[144] bit0 is clear.
# mtcrl_6 has a gateway to 462 on each of its two tracks, which MountCorel.ps1 lists as
# $mountUpperTrackTriangles and $mountLowerTrackTriangles, and the way back is the one on
# the party's own track. Nothing takes the lower track up to the upper one after the first
# visit: border5's and border6's Mains switch their LINEs off from 427 (16200000AB010405,
# D100), and border5's event was the only way up (and was not the player's: Cid's jump,
# handed back to Cloud). Arriving from 467 (triangle 74), 465 (115) or 462's lower gateway
# (151), that is gateway1; AD's Main locks 140 and 6 while 3[222] bit5 is clear (1430DE050A0C,
# 6D8C0001, 6D060001), and the disc-one bridge switch, the way on to North Corel, sets it.
# From 462's upper gateway (138) it is gateway0.
Add-Definition -FieldId 464 -FieldName 'mtcrl_6' -Kind Location -Priority 0 `
    -Label 'Follow the tracks back toward the reactor' -MinimumGameMoment 1110 -MaximumGameMoment 1115 `
    -X -3208 -Y 1000 -Z 756 `
    -TriggerLine ([ordered]@{ startX = -3096; startY = 917; startZ = 756; endX = -3321; endY = 1084; endZ = 756 }) `
    -RequiredConditions @((New-Condition 15 144 1 0)) -RequiredPlayerTriangles $mountUpperTrackTriangles `
    -EntityName 'gateway0' -ScriptType 'Gateway'

Add-Definition -FieldId 464 -FieldName 'mtcrl_6' -Kind Location -Priority 0 `
    -Label 'Follow the tracks back toward the reactor' -MinimumGameMoment 1110 -MaximumGameMoment 1115 `
    -X -2106 -Y 181 -Z 358 `
    -TriggerLine ([ordered]@{ startX = -2220; startY = 264; startZ = 358; endX = -1991; endY = 97; endZ = 358 }) `
    -RequiredConditions @((New-Condition 15 144 1 0)) -RequiredPlayerTriangles $mountLowerTrackTriangles `
    -EntityName 'gateway1' -ScriptType 'Gateway'

# Corel pursuit: world14->458->459->460, or North Corel450->467->464->462->461->460. The native guard event in460 starts the pursuit only while15[144] bit0 is clear.
Add-Definition -FieldId 462 -FieldName 'mtcrl_4' -Kind Location -Priority 0 `
    -Label 'Continue toward the reactor' -MinimumGameMoment 1110 -MaximumGameMoment 1115 `
    -X -2412 -Y -9 -Z 1284 `
    -TriggerLine ([ordered]@{ startX = -2412; startY = 20; startZ = 1271; endX = -2412; endY = -38; endZ = 1298 }) `
    -RequiredConditions @((New-Condition 15 144 1 0)) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# Corel pursuit: world14->458->459->460, or North Corel450->467->464->462->461->460. The native guard event in460 starts the pursuit only while15[144] bit0 is clear.
Add-Definition -FieldId 461 -FieldName 'mtcrl_3' -Kind Location -Priority 0 `
    -Label 'Go on to the reactor' -MinimumGameMoment 1110 -MaximumGameMoment 1115 `
    -X -1333 -Y -1 -Z 551 `
    -TriggerLine ([ordered]@{ startX = -1209; startY = -129; startZ = 551; endX = -1458; endY = 127; endZ = 551 }) `
    -RequiredConditions @((New-Condition 15 144 1 0)) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# Corel pursuit: world14->458->459->460, or North Corel450->467->464->462->461->460. The native guard event in460 starts the pursuit only while15[144] bit0 is clear.
Add-Definition -FieldId 458 -FieldName 'mtcrl_0' -Kind Location -Priority 0 `
    -Label 'Take the path up Mount Corel' -MinimumGameMoment 1110 -MaximumGameMoment 1115 `
    -X -22 -Y 1487 -Z 545 `
    -TriggerLine ([ordered]@{ startX = -62; startY = 1487; startZ = 545; endX = 17; endY = 1487; endZ = 546 }) `
    -RequiredConditions @((New-Condition 15 144 1 0)) `
    -EntityName 'gateway1' -ScriptType 'Gateway'

# Corel pursuit: world14->458->459->460, or North Corel450->467->464->462->461->460. The native guard event in460 starts the pursuit only while15[144] bit0 is clear.
Add-Definition -FieldId 459 -FieldName 'mtcrl_1' -Kind Location -Priority 0 `
    -Label 'Continue to the reactor' -MinimumGameMoment 1110 -MaximumGameMoment 1115 `
    -X -3 -Y 2585 -Z 546 `
    -TriggerLine ([ordered]@{ startX = 5; startY = 2193; startZ = 504; endX = -12; endY = 2978; endZ = 589 }) `
    -RequiredConditions @((New-Condition 15 144 1 0)) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# condor1 gateway0 enters354; the visible doorkeeper is offered first until he steps aside. Fort completion is13[82] bit5.
Add-Definition -FieldId 353 -FieldName 'condor1' -Kind Location -Priority 1 `
    -Label 'Enter Fort Condor' -MinimumGameMoment 1110 -MaximumGameMoment 1115 `
    -X 2 -Y 383 -Z 0 `
    -TriggerLine ([ordered]@{ startX = -84; startY = 855; startZ = 0; endX = 88; endY = -89; endZ = 0 }) `
    -RequiredConditions @((New-Condition 13 82 32 0)) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# condor2 line00 Go tests fresh Confirm (31200209) before requesting the native climb; jump Script3 maps355.
Add-Definition -FieldId 354 -FieldName 'condor2' -Kind Location -Priority 0 `
    -Label 'Press Confirm at the ladder to enter the fort' -MinimumGameMoment 1110 -MaximumGameMoment 1115 -EntityId 4 `
    -X -9 -Y 242 -Z 0 `
    -RequiredEnabledLineEntityId 4 -UsesPlayerCollisionRadius `
    -KeepActiveOnArrival `
    -TriggerLine ([ordered]@{ startX = -14; startY = 281; startZ = 0; endX = -4; endY = 204; endZ = 0 }) `
    -RequiredConditions @((New-Condition 13 82 32 0)) `
    -EntityName 'line00' -ScriptType 'Go'

# convil_1 line01 Go tests fresh Confirm (31200210) before requesting ladder2 Script4 to map354. Mission completion13[82] bit5.
Add-Definition -FieldId 355 -FieldName 'convil_1' -Kind Location -Priority 0 `
    -Label 'Press Confirm at the ladder to leave the fort' -MinimumGameMoment 1110 -MaximumGameMoment 1117 -EntityId 4 `
    -X -362 -Y 79 -Z -174 `
    -RequiredEnabledLineEntityId 4 -UsesPlayerCollisionRadius `
    -KeepActiveOnArrival `
    -TriggerLine ([ordered]@{ startX = -336; startY = 145; startZ = -174; endX = -388; endY = 14; endZ = -174 }) `
    -RequiredConditions @((New-Condition 13 82 32 32)) `
    -EntityName 'line01' -ScriptType 'Go'

# condor1 ojisan is visible until the native admission conversation moves him aside and sets VISI0; native Talk handles the first-visit choice even during the Huge Materia chapter.
Add-Definition -FieldId 353 -FieldName 'condor1' -Kind Model -Priority 0 `
    -Label 'Talk to the man at the fort entrance' -MinimumGameMoment 1110 -MaximumGameMoment 1115 -EntityId 10 `
    -RequiredConditions @((New-Condition 13 82 32 0)) `
    -EntityName 'ojisan' -ScriptType 'Talk'

# Mideel shops and houses have native out Go lines returning712, where the clinic approach/return is selected by the current story state.
Add-Definition -FieldId 717 -FieldName 'itown_w' -Kind Location -Priority 0 `
    -Label 'Leave the weapon shop' -MinimumGameMoment 1033 -MaximumGameMoment 1117 -EntityId 13 `
    -X 36 -Y -304 -Z 0 `
    -RequiredEnabledLineEntityId 13 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = -48; startY = -304; startZ = 0; endX = 120; endY = -304; endZ = 0 }) `
    -EntityName 'out' -ScriptType 'Go'

# Mideel shops and houses have native out Go lines returning712, where the clinic approach/return is selected by the current story state.
Add-Definition -FieldId 718 -FieldName 'itown_i' -Kind Location -Priority 0 `
    -Label 'Leave the item shop' -MinimumGameMoment 1033 -MaximumGameMoment 1117 -EntityId 12 `
    -X -76 -Y -279 -Z 0 `
    -RequiredEnabledLineEntityId 12 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = -122; startY = -279; startZ = 0; endX = -30; endY = -279; endZ = 0 }) `
    -EntityName 'out' -ScriptType 'Go'

# Mideel shops and houses have native out Go lines returning712, where the clinic approach/return is selected by the current story state.
Add-Definition -FieldId 719 -FieldName 'itown_m' -Kind Location -Priority 0 `
    -Label 'Leave the materia shop' -MinimumGameMoment 1033 -MaximumGameMoment 1117 -EntityId 12 `
    -X 23 -Y -298 -Z 0 `
    -RequiredEnabledLineEntityId 12 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = -60; startY = -300; startZ = 0; endX = 107; endY = -297; endZ = 0 }) `
    -EntityName 'out' -ScriptType 'Go'

# Mideel shops and houses have native out Go lines returning712, where the clinic approach/return is selected by the current story state.
Add-Definition -FieldId 721 -FieldName 'itmin1' -Kind Location -Priority 0 `
    -Label 'Leave the house' -MinimumGameMoment 1033 -MaximumGameMoment 1117 -EntityId 12 `
    -X -139 -Y -247 -Z 0 `
    -RequiredEnabledLineEntityId 12 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = -83; startY = -247; startZ = 0; endX = -196; endY = -248; endZ = 0 }) `
    -EntityName 'out' -ScriptType 'Go'

# Mideel shops and houses have native out Go lines returning712, where the clinic approach/return is selected by the current story state.
Add-Definition -FieldId 722 -FieldName 'itmin2' -Kind Location -Priority 0 `
    -Label 'Leave the house' -MinimumGameMoment 1033 -MaximumGameMoment 1117 -EntityId 10 `
    -X -24 -Y -254 -Z 0 `
    -RequiredEnabledLineEntityId 10 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = 26; startY = -251; startZ = 0; endX = -74; endY = -257; endZ = 0 }) `
    -EntityName 'out' -ScriptType 'Go'

# Temple optional treasure/service rooms do not advance the counter. Their native return doors rejoin604 or607; the current clock objective remainsVI before627 andXII afterward.
Add-Definition -FieldId 605 -FieldName 'kuro_2' -Kind Location -Priority 0 `
    -Label 'Return to the temple maze' -MinimumGameMoment 613 -MaximumGameMoment 629 `
    -X 520 -Y 2 -Z 0 `
    -TriggerLine ([ordered]@{ startX = 515; startY = -74; startZ = 0; endX = 525; endY = 78; endZ = 0 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# Temple optional treasure/service rooms do not advance the counter. Their native return doors rejoin604 or607; the current clock objective remainsVI before627 andXII afterward.
Add-Definition -FieldId 608 -FieldName 'kuro_5' -Kind Location -Priority 0 `
    -Label 'Return to the temple maze' -MinimumGameMoment 613 -MaximumGameMoment 629 `
    -X -6 -Y -667 -Z 0 `
    -TriggerLine ([ordered]@{ startX = -124; startY = -698; startZ = 0; endX = 112; endY = -637; endZ = 0 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# Temple optional treasure/service rooms do not advance the counter. Their native return doors rejoin604 or607; the current clock objective remainsVI before627 andXII afterward.
Add-Definition -FieldId 614 -FieldName 'kuro_10' -Kind Location -Priority 0 `
    -Label 'Return to the clock room' -MinimumGameMoment 613 -MaximumGameMoment 629 -EntityId 4 `
    -X -2 -Y 514 -Z 0 `
    -RequiredEnabledLineEntityId 4 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = -102; startY = 514; startZ = 0; endX = 98; endY = 514; endZ = 0 }) `
    -EntityName 'border2' -ScriptType 'Move'

# Temple optional treasure/service rooms do not advance the counter. Their native return doors rejoin604 or607; the current clock objective remainsVI before627 andXII afterward.
Add-Definition -FieldId 615 -FieldName 'kuro_11' -Kind Location -Priority 0 `
    -Label 'Return to the clock room' -MinimumGameMoment 613 -MaximumGameMoment 629 -EntityId 4 `
    -X -1 -Y 508 -Z 0 `
    -RequiredEnabledLineEntityId 4 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = -100; startY = 508; startZ = 0; endX = 98; endY = 508; endZ = 0 }) `
    -EntityName 'border2' -ScriptType 'Move'

# Temple optional treasure/service rooms do not advance the counter. Their native return doors rejoin604 or607; the current clock objective remainsVI before627 andXII afterward.
Add-Definition -FieldId 609 -FieldName 'kuro_6' -Kind Location -Priority 0 `
    -Label 'Leave the treasure room' -MinimumGameMoment 613 -MaximumGameMoment 629 `
    -X 28 -Y -493 -Z 0 `
    -TriggerLine ([ordered]@{ startX = 108; startY = -439; startZ = 0; endX = -52; endY = -548; endZ = 0 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# Clock doorway V arrives on isolated maze triangle205. Its native connected component only returns through mapjump18; it cannot reach the rolling-corridor door.
Add-Definition -FieldId 604 -FieldName 'kuro_1' -Kind Location -Priority 0 `
    -Label 'Go through to the clock room' -MinimumGameMoment 613 -MaximumGameMoment 629 -EntityId 18 `
    -X 678 -Y 1743 -Z 1100 `
    -RequiredEnabledLineEntityId 18 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = 759; startY = 1741; startZ = 1100; endX = 597; endY = 1746; endZ = 1100 }) `
    -RequiredPlayerTriangles @(200, 201, 202, 203, 204, 205, 206, 306, 326, 327, 342, 343, 344) `
    -EntityName 'mapjump' -ScriptType 'Move'

# The main maze component reaches606 gateway2, then606 gateway1 returns607. The clock doorway V balcony is a separate component and uses mapjump18 instead.
Add-Definition -FieldId 604 -FieldName 'kuro_1' -Kind Location -Priority 0 `
    -Label 'Return through the rolling corridor toward the clock' -MinimumGameMoment 613 -MaximumGameMoment 629 `
    -X 331 -Y 1010 -Z 537 `
    -TriggerLine ([ordered]@{ startX = 275; startY = 1088; startZ = 537; endX = 388; endY = 932; endZ = 537 }) `
    -ExcludedPlayerTriangles @(200, 201, 202, 203, 204, 205, 206, 306, 326, 327, 342, 343, 344) `
    -EntityName 'gateway2' -ScriptType 'Gateway'

# After the mural agreement at627, optional maze/service detours still return through the rolling corridor to clock doorwayXII.
Add-Definition -FieldId 606 -FieldName 'kuro_3' -Kind Location -Priority 0 `
    -Label 'Return to the clock room' -MinimumGameMoment 627 -MaximumGameMoment 629 `
    -X 846 -Y 1413 -Z -296 `
    -TriggerLine ([ordered]@{ startX = 846; startY = 1335; startZ = -296; endX = 846; endY = 1491; endZ = -296 }) `
    -EntityName 'gateway1' -ScriptType 'Gateway'

# cos_btm lower component reaches the upper terrace only through cosin1. Native529 g0 arrives328; g1 arrives340 on the separate terrace.
Add-Definition -FieldId 525 -FieldName 'cos_btm' -Kind Location -Priority 0 `
    -Label 'Enter the inn to reach Bugenhagen' -MinimumGameMoment 1389 -MaximumGameMoment 1390 `
    -X -1148 -Y -907 -Z -2141 `
    -TriggerLine ([ordered]@{ startX = -1201; startY = -893; startZ = -2141; endX = -1095; endY = -922; endZ = -2141 }) `
    -RequiredPlayerTriangles @(16, 17, 18, 19, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 165, 166, 170, 171, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181, 182, 183, 186, 187, 188, 189, 190, 191, 192, 193, 194, 195, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 214, 215, 216, 217, 218, 219, 220, 221, 222, 223, 224, 229, 230, 231, 232, 233, 234, 235, 236, 237, 238, 239, 240, 241, 242, 243, 244, 245, 246, 247, 248, 249, 250, 251, 252, 253, 254, 255, 256, 257, 258, 259, 260, 261, 262, 263, 264, 265, 266, 267, 268, 269, 270, 271, 272, 273, 274, 275, 276, 277, 278, 279, 280, 281, 282, 283, 284, 285, 286, 287, 288, 289, 291, 292, 293, 294, 295, 296, 297, 298, 299, 300, 301, 302, 303, 304, 305, 306, 307, 308, 317, 318, 319, 320, 321, 322, 323, 324, 325, 326, 327, 328, 329, 330, 331, 332) `
    -EntityName 'gateway1' -ScriptType 'Gateway'

# cos_btm upper component gateway3 maps531 attriangle77; first-visit guard/Red flags are irrelevant to this late visit.
Add-Definition -FieldId 525 -FieldName 'cos_btm' -Kind Location -Priority 0 `
    -Label 'Take the upper path to Bugenhagen' -MinimumGameMoment 1389 -MaximumGameMoment 1390 `
    -X -494 -Y -504 -Z -1468 `
    -TriggerLine ([ordered]@{ startX = -536; startY = -468; startZ = -1468; endX = -452; endY = -541; endZ = -1468 }) `
    -RequiredPlayerTriangles @(0, 1, 8, 9, 11, 20, 21, 49, 65, 83, 184, 185, 196, 197, 198, 199, 200, 201, 290, 309, 310, 313, 314, 333, 334, 335, 336, 337, 338, 339, 340, 341, 342, 343, 344, 345, 346, 347, 348, 349, 350, 351, 352, 353, 354, 355, 356, 357, 358, 359, 360, 361, 362, 363, 364, 365) `
    -EntityName 'gateway3' -ScriptType 'Gateway'

# cosin1 gateway1 maps525 attriangle340, the observatory-side terrace.
Add-Definition -FieldId 529 -FieldName 'cosin1' -Kind Location -Priority 0 `
    -Label 'Leave the inn by the upper door' -MinimumGameMoment 1389 -MaximumGameMoment 1390 `
    -X -301 -Y 96 -Z 0 `
    -TriggerLine ([ordered]@{ startX = -323; startY = 137; startZ = 0; endX = -280; endY = 56; endZ = 0 }) `
    -EntityName 'gateway1' -ScriptType 'Gateway'

# Optional inn room returns529; its upper door continues the approach.
Add-Definition -FieldId 530 -FieldName 'cosin1_1' -Kind Location -Priority 0 `
    -Label 'Return downstairs to the inn' -MinimumGameMoment 1389 -MaximumGameMoment 1390 `
    -X 314 -Y -220 -Z 0 `
    -TriggerLine ([ordered]@{ startX = 272; startY = -269; startZ = 0; endX = 356; endY = -172; endZ = 0 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# cosin2 LINEL Go requires a fresh Confirm and calls the native climb to540. It must remain active on approach.
Add-Definition -FieldId 531 -FieldName 'cosin2' -Kind Location -Priority 0 `
    -Label 'Press Confirm at the ladder to Bugenhagen' -MinimumGameMoment 1389 -MaximumGameMoment 1390 -EntityId 13 `
    -X -210 -Y 198 -Z -12 `
    -RequiredEnabledLineEntityId 13 -UsesPlayerCollisionRadius `
    -KeepActiveOnArrival `
    -TriggerLine ([ordered]@{ startX = -224; startY = 164; startZ = -16; endX = -196; endY = 233; endZ = -9 }) `
    -EntityName 'LINEL' -ScriptType 'Go'

# cos_top gateway0 enters544 attriangle1; the observation platform is not this entrance.
Add-Definition -FieldId 540 -FieldName 'cos_top' -Kind Location -Priority 0 `
    -Label 'Enter Bugenhagen''s house' -MinimumGameMoment 1389 -MaximumGameMoment 1390 `
    -X -289 -Y -282 -Z -544 `
    -TriggerLine ([ordered]@{ startX = -327; startY = -240; startZ = -544; endX = -251; endY = -325; endZ = -544 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# bugin2 gateway1 leads541; Bugenhagen Talk advances1391 and his native scene handles the subsequent Highwind handoff.
Add-Definition -FieldId 544 -FieldName 'bugin2' -Kind Location -Priority 0 `
    -Label 'Go through to Bugenhagen''s room' -MinimumGameMoment 1389 -MaximumGameMoment 1390 `
    -X 225 -Y -189 -Z -624 `
    -TriggerLine ([ordered]@{ startX = 255; startY = -158; startZ = -624; endX = 196; endY = -220; endZ = -624 }) `
    -EntityName 'gateway1' -ScriptType 'Gateway'

# ncorel2 is the damaged town variant after failing to stop the train. Mission completion15[145] bit5 still allows continuing to the remaining mission or Mideel.
Add-Definition -FieldId 451 -FieldName 'ncorel2' -Kind Location -Priority 0 `
    -Label 'Leave North Corel after the train mission' -MinimumGameMoment 1110 -MaximumGameMoment 1117 `
    -X 777 -Y -531 -Z 0 `
    -TriggerLine ([ordered]@{ startX = 810; startY = -411; startZ = 0; endX = 745; endY = -652; endZ = 0 }) `
    -RequiredConditions @((New-Condition 15 145 32 32)) `
    -EntityName 'gateway4' -ScriptType 'Gateway'

# After the burial, native losin2 hands back at677. Optional city detours must return through634 gateway2 toward Coral Valley cave and Icicle Inn.
Add-Definition -FieldId 630 -FieldName 'lost1' -Kind Location -Priority 0 `
    -Label 'Return to the eastern path out of the city' -MinimumGameMoment 677 -MaximumGameMoment 680 `
    -X -2945 -Y 2938 -Z 161 `
    -TriggerLine ([ordered]@{ startX = -3042; startY = 3029; startZ = 170; endX = -2849; endY = 2847; endZ = 152 }) `
    -EntityName 'gateway1' -ScriptType 'Gateway'

# After the burial, native losin2 hands back at677. Optional city detours must return through634 gateway2 toward Coral Valley cave and Icicle Inn. From either house633 arrival, use jump1 to630 then its eastern gateway1. The direct635 gateway0 has no route from these two components.
Add-Definition -FieldId 635 -FieldName 'lost3' -Kind Location -Priority 0 `
    -Label 'Return to the eastern houses' -MinimumGameMoment 677 -MaximumGameMoment 680 -EntityId 3 `
    -X -5063 -Y 6178 -Z 359 `
    -RequiredEnabledLineEntityId 3 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = -5136; startY = 6169; startZ = 359; endX = -4990; endY = 6187; endZ = 359 }) `
    -EntityName 'jump1' -ScriptType 'Go'

# After the burial, native losin2 hands back at677. Optional city detours must return through634 gateway2 toward Coral Valley cave and Icicle Inn.
Add-Definition -FieldId 636 -FieldName 'losinn' -Kind Location -Priority 0 `
    -Label 'Leave the sleeping house' -MinimumGameMoment 677 -MaximumGameMoment 680 `
    -X 171 -Y -470 -Z 64 `
    -TriggerLine ([ordered]@{ startX = 189; startY = -513; startZ = 64; endX = 153; endY = -427; endZ = 64 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# After the burial, native losin2 hands back at677. Optional city detours must return through634 gateway2 toward Coral Valley cave and Icicle Inn.
Add-Definition -FieldId 631 -FieldName 'losin1' -Kind Location -Priority 0 `
    -Label 'Go back down from the upper chamber' -MinimumGameMoment 677 -MaximumGameMoment 680 `
    -X 348 -Y -29 -Z -96 `
    -TriggerLine ([ordered]@{ startX = 376; startY = 8; startZ = -96; endX = 320; endY = -67; endZ = -97 }) `
    -EntityName 'gateway1' -ScriptType 'Gateway'

# After the burial, native losin2 hands back at677. Optional city detours must return through634 gateway2 toward Coral Valley cave and Icicle Inn.
Add-Definition -FieldId 641 -FieldName 'blue_2' -Kind Location -Priority 0 `
    -Label 'Leave the blue building' -MinimumGameMoment 677 -MaximumGameMoment 680 `
    -X 260 -Y -1652 -Z 33 `
    -TriggerLine ([ordered]@{ startX = 215; startY = -1648; startZ = 33; endX = 306; endY = -1656; endZ = 33 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'

# After the burial, native losin2 hands back at677. Optional city detours must return through634 gateway2 toward Coral Valley cave and Icicle Inn.
Add-Definition -FieldId 640 -FieldName 'blue_1' -Kind Location -Priority 0 `
    -Label 'Return to the crossroads' -MinimumGameMoment 677 -MaximumGameMoment 680 -EntityId 4 `
    -X -9589 -Y 5709 -Z 276 `
    -RequiredEnabledLineEntityId 4 -UsesPlayerCollisionRadius `
    -TriggerLine ([ordered]@{ startX = -9604; startY = 5681; startZ = 276; endX = -9574; endY = 5738; endZ = 276 }) `
    -EntityName 'lin0' -ScriptType 'Move'

# After the burial, native losin2 hands back at677. Optional city detours must return through634 gateway2 toward Coral Valley cave and Icicle Inn.
Add-Definition -FieldId 633 -FieldName 'losin3' -Kind Location -Priority 0 `
    -Label 'Leave the western house' -MinimumGameMoment 677 -MaximumGameMoment 680 `
    -X 84 -Y -347 -Z 0 `
    -TriggerLine ([ordered]@{ startX = 43; startY = -352; startZ = 0; endX = 126; endY = -343; endZ = 0 }) `
    -EntityName 'gateway0' -ScriptType 'Gateway'
