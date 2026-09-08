# Dot-sourced by Generate-FieldStoryEvents.ps1 after Add-Definition/New-Condition.
# First arrival retains GM415 (ship_2/BALLET2 Talk306). mtcrl_0/produce Init21
# advances it to422, and Init26 sets bank3[224] bit5. No Costa script writes GM.
# All choices share priority and have no next-GM filter: optional visits must
# never hide the main exit. Gateway/scene lines remain active until native change.

# del1/border1 Move54 invokes the separate, automatic del12 scene. Its Main14
# disables the line after del12/heri Script4 byte178 sets bank3[224] bit0.
Add-Definition -FieldId 441 -FieldName 'del1' -Kind 'Location' -Label 'Enter Costa del Sol' -X 1044 -Y -1127 -Z 128 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -RequiredCondition (New-Condition -Bank 3 -Address 224 -Mask 1 -Value 0) -RequiredEnabledLineEntityId 5 -EntityName 'border1' -ScriptType 'Move' -TriggerLine @{ startX = 1044; startY = -1032; startZ = 128; endX = 1044; endY = -1222; endZ = 128 }
Add-Definition -FieldId 441 -FieldName 'del1' -Kind 'Location' -Label 'Enter Costa del Sol' -X 1066 -Y -1127 -Z 139 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -RequiredCondition (New-Condition -Bank 3 -Address 224 -Mask 1 -Value 1) -ScriptType 'Gateway' -TriggerLine @{ startX = 1071; startY = -1036; startZ = 142; endX = 1062; endY = -1219; endZ = 137 }

# del2 ordinary gateway7 -> world field13/wm12. This is independent of every
# optional town conversation, inn payment, purchase, or collectible.
Add-Definition -FieldId 443 -FieldName 'del2' -Kind 'Location' -Label 'Leave Costa del Sol for Mount Corel' -X -1435 -Y -251 -Z -143 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -ScriptType 'Gateway' -TriggerLine @{ startX = -1209; startY = -396; startZ = -143; endX = -1661; endY = -106; endZ = -143 }
# del2 gateway2 -> del3 remains usable after the optional Hojo scene or inn rest.
# Only the scene's actual interaction is retired by those flags below.
Add-Definition -FieldId 443 -FieldName 'del2' -Kind 'Location' -Label 'Visit the beach (optional)' -X 1370 -Y 956 -Z -98 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -ScriptType 'Gateway' -TriggerLine @{ startX = 1329; startY = 913; startZ = -99; endX = 1412; endY = 999; endZ = -98 }
# Ordinary visible town entrances: inn6, Johnny5, villa4, bar1.
Add-Definition -FieldId 443 -FieldName 'del2' -Kind 'Location' -Label 'Visit the inn (optional)' -X 1030 -Y 2110 -Z 69 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -ScriptType 'Gateway' -TriggerLine @{ startX = 998; startY = 2110; startZ = 69; endX = 1062; endY = 2110; endZ = 69 }
Add-Definition -FieldId 443 -FieldName 'del2' -Kind 'Location' -Label "Visit Johnny's house (optional)" -X -18 -Y 1119 -Z 11 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -ScriptType 'Gateway' -TriggerLine @{ startX = 5; startY = 1062; startZ = 10; endX = -42; endY = 1177; endZ = 13 }
Add-Definition -FieldId 443 -FieldName 'del2' -Kind 'Location' -Label 'Visit the villa (optional)' -X -1455 -Y 316 -Z 165 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -ScriptType 'Gateway' -TriggerLine @{ startX = -1508; startY = 344; startZ = 165; endX = -1403; endY = 288; endZ = 165 }
Add-Definition -FieldId 443 -FieldName 'del2' -Kind 'Location' -Label 'Visit the bar (optional)' -X -88 -Y 88 -Z 8 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -ScriptType 'Gateway' -TriggerLine @{ startX = -122; startY = 68; startZ = 8; endX = -55; endY = 109; endZ = 9 }
# These are counter interactions, not automatic exits. border1/2 Go4 test
# IFKEYON544 before calling the merchant. Native Go requires proximity strictly
# inside the player's collision radius; pause there for the player's mapped OK.
# Ordinary NPC discovery cannot safely replace these rows: the item merchant has
# no speaker name, while Yuffie is called from two different LINE entities.
Add-Definition -FieldId 443 -FieldName 'del2' -Kind 'Location' -Label 'Talk at the item shop (optional)' -X 1280 -Y 1623 -Z 0 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -RequiredEnabledLineEntityId 5 -UsesPlayerCollisionRadius -KeepActiveOnArrival -EntityName 'border1' -ScriptType 'Go' -TriggerLine @{ startX = 1350; startY = 1567; startZ = 0; endX = 1210; endY = 1679; endZ = 0 }
Add-Definition -FieldId 443 -FieldName 'del2' -Kind 'Location' -Label 'Talk at the materia shop (optional)' -X 662 -Y 1849 -Z 0 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -RequiredEnabledLineEntityId 6 -UsesPlayerCollisionRadius -KeepActiveOnArrival -EntityName 'border2' -ScriptType 'Go' -TriggerLine @{ startX = 601; startY = 1855; startZ = 0; endX = 723; endY = 1843; endZ = 0 }
# del2/red Init gates visibility on party membership and inn-rest bit5.
# The ball's Talk invokes Cloud's kick and Red's reaction. Neither has a saved
# completion bit; visibility and current native position govern the target.
Add-Definition -FieldId 443 -FieldName 'del2' -Kind 'Model' -Label 'Talk to Red XIII (optional)' -EntityId 11 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -RequiredCondition (New-Condition -Bank 3 -Address 224 -Mask 32 -Value 0) -EntityName 'red' -ScriptType 'Talk'
Add-Definition -FieldId 443 -FieldName 'del2' -Kind 'Model' -Label 'Kick the ball (optional)' -EntityId 23 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -EntityName 'ball' -ScriptType 'Talk'

# delinn gateway0, Barret's actual sailor model, and the innkeeper's normal Talk.
Add-Definition -FieldId 444 -FieldName 'delinn' -Kind 'Location' -Label 'Return to the town' -X -346 -Y -975 -Z -63 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -ScriptType 'Gateway' -TriggerLine @{ startX = -290; startY = -975; startZ = -63; endX = -402; endY = -975; endZ = -63 }
Add-Definition -FieldId 444 -FieldName 'delinn' -Kind 'Model' -Label 'Talk to Barret (optional)' -EntityId 12 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -RequiredCondition (New-Condition -Bank 3 -Address 224 -Mask 32 -Value 0) -EntityName 'cballet' -ScriptType 'Talk'
Add-Definition -FieldId 444 -FieldName 'delinn' -Kind 'Model' -Label 'Talk to the innkeeper (optional)' -EntityId 13 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -EntityName 'man1' -ScriptType 'Talk'

# delpb gateway0 and its native armor merchant; opening the shop is manual.
Add-Definition -FieldId 445 -FieldName 'delpb' -Kind 'Location' -Label 'Return to the town' -X 389 -Y -5 -Z -157 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -ScriptType 'Gateway' -TriggerLine @{ startX = 354; startY = -24; startZ = -147; endX = 424; endY = 13; endZ = -167 }
Add-Definition -FieldId 445 -FieldName 'delpb' -Kind 'Model' -Label 'Browse the armor shop (optional)' -EntityId 13 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -EntityName 'weapon' -ScriptType 'Talk'

# delmin1 gateways0/1. All three basement items already have verified Objects
# rows in delmin12; entering or leaving the villa must not require buying it.
Add-Definition -FieldId 446 -FieldName 'delmin1' -Kind 'Location' -Label 'Return to the town' -X -489 -Y 269 -Z 0 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -ScriptType 'Gateway' -TriggerLine @{ startX = -489; startY = 331; startZ = 0; endX = -489; endY = 207; endZ = 0 }
Add-Definition -FieldId 446 -FieldName 'delmin1' -Kind 'Location' -Label 'Visit the basement (optional)' -X 417 -Y -416 -Z -143 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -ScriptType 'Gateway' -TriggerLine @{ startX = 373; startY = -412; startZ = -143; endX = 461; endY = -421; endZ = -143 }
Add-Definition -FieldId 447 -FieldName 'delmin12' -Kind 'Location' -Label 'Return upstairs' -X 318 -Y -45 -Z 121 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -ScriptType 'Gateway' -TriggerLine @{ startX = 293; startY = -50; startZ = 119; endX = 344; endY = -41; endZ = 124 }

# delmin2 gateway0. Johnny's initial conversation sets224 bit6 at Talk304.
# After Hojo, Tifa appears here only when outside the party; live visibility is
# authoritative. Johnny Talk147/150 still reaches his original conversation192
# when Tifa is in the party, so Hojo's bit4 is not a Johnny completion flag.
# Neither conversation nor party choice may hide the exit.
Add-Definition -FieldId 448 -FieldName 'delmin2' -Kind 'Location' -Label 'Return to the town' -X 360 -Y -275 -Z -76 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -ScriptType 'Gateway' -TriggerLine @{ startX = 342; startY = -322; startZ = -57; endX = 379; endY = -228; endZ = -95 }
Add-Definition -FieldId 448 -FieldName 'delmin2' -Kind 'Model' -Label 'Talk to Johnny (optional)' -EntityId 14 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -RequiredCondition (New-Condition -Bank 3 -Address 224 -Mask 96 -Value 0) -EntityName 'johnny' -ScriptType 'Talk'
Add-Definition -FieldId 448 -FieldName 'delmin2' -Kind 'Model' -Label 'Talk to Tifa (optional)' -EntityId 12 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -RequiredCondition (New-Condition -Bank 3 -Address 224 -Mask 48 -Value 16) -EntityName 'tifa' -ScriptType 'Talk'

# del3 gateway1. The introduction sets bit3 at tifa Script6 byte52. Hojo's Talk
# only gives an ellipsis: woman1 Talk134 calls AD/1 and starts the full scene.
# cloud Script12 byte40 sets completion bit4. Inn man1 Talk719 sets bit5.
Add-Definition -FieldId 449 -FieldName 'del3' -Kind 'Location' -Label 'Return to the town' -X -717 -Y 865 -Z 107 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -ScriptType 'Gateway' -TriggerLine @{ startX = -749; startY = 863; startZ = 109; endX = -685; endY = 867; endZ = 105 }
Add-Definition -FieldId 449 -FieldName 'del3' -Kind 'Model' -Label 'Speak to the woman beside Hojo (optional)' -EntityId 13 -MinimumGameMoment 415 -MaximumGameMoment 421 -Priority 0 -RequiredCondition (New-Condition -Bank 3 -Address 224 -Mask 56 -Value 8) -CompletedCondition (New-Condition -Bank 3 -Address 224 -Mask 16 -Value 16) -EntityName 'woman1' -ScriptType 'Talk'

# Reviewed above; the transit continuity pass must not add rows here.
Add-CuratedFields 441, 443, 444, 445, 446, 447, 448, 449
