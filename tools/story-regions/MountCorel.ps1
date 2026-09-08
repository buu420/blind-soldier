# First visit, from mtcrl_0/produce Init's SETWORD2[0]=422 until the
# North Corel arrival advances to427. IDs and geometry were checked against
# the installed flevel.lgp; later Huge Materia visits use different scripts.
$mountFirstVisit = @{ MinimumGameMoment = 422; MaximumGameMoment = 426; Priority = 0 }
$mountBridgeRaised = New-Condition -Bank 3 -Address 222 -Mask 32 -Value 0
$mountBridgeLowered = New-Condition -Bank 3 -Address 222 -Mask 32 -Value 32

Add-Definition @mountFirstVisit -FieldId 458 -FieldName 'mtcrl_0' -Kind 'Location' -Label 'Continue up Mount Corel' -X -23 -Y 1487 -Z 546 -ScriptType 'Gateway' -TriggerLine @{ startX=-62; startY=1487; startZ=545; endX=17; endY=1487; endZ=546 }
Add-Definition @mountFirstVisit -FieldId 459 -FieldName 'mtcrl_1' -Kind 'Location' -Label 'Continue toward the Corel reactor' -X -4 -Y 2586 -Z 547 -ScriptType 'Gateway' -TriggerLine @{ startX=5; startY=2193; startZ=504; endX=-12; endY=2978; endZ=589 }
Add-Definition @mountFirstVisit -FieldId 460 -FieldName 'mtcrl_2' -Kind 'Location' -Label 'Continue past the Corel reactor' -X 221 -Y -2318 -Z -600 -ScriptType 'Gateway' -TriggerLine @{ startX=157; startY=-2318; startZ=-600; endX=285; endY=-2318; endZ=-600 }
Add-Definition @mountFirstVisit -FieldId 461 -FieldName 'mtcrl_3' -Kind 'Location' -Label 'Continue onto the railway tracks' -X 1814 -Y -3 -Z 551 -ScriptType 'Gateway' -TriggerLine @{ startX=1747; startY=124; startZ=551; endX=1881; endY=-129; endZ=551 }

# The rail fork's two exits enter different elevations of mtcrl_6.
# The bridge switch opens IDLCK140 and6 and sets3[222] bit5. Going straight
# from the upper cabin to the long bridge would skip the required return.
Add-Definition @mountFirstVisit -FieldId 462 -FieldName 'mtcrl_4' -Kind 'Location' -Label 'Take the upper tracks to the bridge switch' -X 2450 -Y -8 -Z 1040 -RequiredCondition $mountBridgeRaised -ScriptType 'Gateway' -TriggerLine @{ startX=2442; startY=-39; startZ=1057; endX=2457; endY=23; endZ=1022 }
Add-Definition @mountFirstVisit -FieldId 462 -FieldName 'mtcrl_4' -Kind 'Location' -Label 'Take the lower tracks to the lowered bridge' -X 2146 -Y -52 -Z -355 -RequiredCondition $mountBridgeLowered -ScriptType 'Gateway' -TriggerLine @{ startX=2115; startY=-79; startZ=-295; endX=2177; endY=-25; endZ=-415 }

# The upper native adjacency component remains separate with the bridge
# either raised or lowered. Both ends of Cloud's scripted XYZI wrap are in
# this component; his camera wrap does not complete the switch objective.
$mountUpperTrackTriangles = @(
    95,96,122,123,124,125,137,138,139,145,147,153,157,158,185,186,187,189,
    192,193,194,195,200,201,202,203,206,208,210,211,212,213,215,216,217,
    218,219,246,247,248,249,250,251,252,253,254,255,256,257,258,259,260,
    261,262,263,264,265,266,267,268,269,270,271,272,273,274,275,276,277,
    278,279
)
$mountLowerTrackTriangles = @(
    0..94; 103..121; 126..132; 135;136;140..144;146;148..152;154..156;
    159..181;184;188;190;191;198;199;204;205;207;209;214;220;221;224..227;
    229;230;232;242..245;282;283;285..321;324..328
)

# border4 Go1x opens an ASK automatically inside the player's native
# collision radius. Only choosing "Do it" sets completion bit5; reaching
# the cabin and declining the choice must leave this objective active.
Add-Definition @mountFirstVisit -FieldId 464 -FieldName 'mtcrl_6' -Kind 'Location' -Label 'Lower the railway bridge at the cabin switch' -EntityId 9 -X 2768 -Y -2087 -Z 1070 -RequiredCondition $mountBridgeRaised -CompletedCondition $mountBridgeLowered -RequiredPlayerTriangles $mountUpperTrackTriangles -RequiredEnabledLineEntityId 9 -EntityName 'border4' -ScriptType 'Go 1x' -KeepActiveOnArrival -UsesPlayerCollisionRadius -TriggerLine @{ startX=2815; startY=-2162; startZ=1070; endX=2721; endY=-2012; endZ=1070 }
Add-Definition @mountFirstVisit -FieldId 464 -FieldName 'mtcrl_6' -Kind 'Location' -Label 'Return to the track junction for the upper route' -X -2106 -Y 181 -Z 358 -RequiredCondition $mountBridgeRaised -RequiredPlayerTriangles @(151,152,155,207) -ScriptType 'Gateway' -TriggerLine @{ startX=-2220; startY=264; startZ=358; endX=-1991; endY=97; endZ=358 }
Add-Definition @mountFirstVisit -FieldId 464 -FieldName 'mtcrl_6' -Kind 'Location' -Label 'Return to the track junction for the lowered bridge' -X -3209 -Y 1001 -Z 756 -RequiredCondition $mountBridgeLowered -RequiredPlayerTriangles $mountUpperTrackTriangles -ScriptType 'Gateway' -TriggerLine @{ startX=-3096; startY=917; startZ=756; endX=-3321; endY=1084; endZ=756 }
Add-Definition @mountFirstVisit -FieldId 464 -FieldName 'mtcrl_6' -Kind 'Location' -Label 'Cross the lowered railway bridge' -X 4802 -Y -3321 -Z 540 -RequiredCondition $mountBridgeLowered -RequiredPlayerTriangles $mountLowerTrackTriangles -ScriptType 'Gateway' -TriggerLine @{ startX=4788; startY=-3236; startZ=540; endX=4815; endY=-3406; endZ=540 }

# The native bird calls (SOUND197) run only while5[17]=1 beside the cabin.
# Expose their climb only then, without revealing the nest reward or choice.
# The native ladder handoff to mtcrl_8 and its return control their own scene.
Add-Definition @mountFirstVisit -FieldId 464 -FieldName 'mtcrl_6' -Kind 'Location' -Label 'Investigate the bird calls (optional)' -EntityId 6 -X 3475 -Y -2266 -Z 1070 -RequiredConditions @((New-Condition -Bank 5 -Address 17 -Mask 255 -Value 1),(New-Condition -Bank 3 -Address 222 -Mask 16 -Value 0)) -RequiredPlayerTriangles $mountUpperTrackTriangles -RequiredEnabledLineEntityId 6 -EntityName 'border1' -ScriptType 'Move' -KeepActiveOnArrival -UsesPlayerCollisionRadius -TriggerLine @{ startX=3313; startY=-2225; startZ=1070; endX=3637; endY=-2307; endZ=1070 }

# The cave is optional. Its three native Talk treasure models already have
# collection-bit-backed Objects entries; Story always provides the return.
Add-Definition @mountFirstVisit -FieldId 464 -FieldName 'mtcrl_6' -Kind 'Location' -Label 'Follow the side path to the cave (optional)' -X 2002 -Y -1598 -Z 84 -RequiredCondition $mountBridgeLowered -RequiredPlayerTriangles $mountLowerTrackTriangles -ScriptType 'Gateway' -TriggerLine @{ startX=1925; startY=-1532; startZ=79; endX=2079; endY=-1663; endZ=88 }
Add-Definition @mountFirstVisit -FieldId 465 -FieldName 'mtcrl_7' -Kind 'Location' -Label "Return from the miner's cave" -X 16 -Y 492 -Z -44 -ScriptType 'Gateway' -TriggerLine @{ startX=-26; startY=445; startZ=-44; endX=57; endY=538; endZ=-44 }
Add-Definition @mountFirstVisit -FieldId 467 -FieldName 'mtcrl_9' -Kind 'Location' -Label 'Continue into North Corel' -EntityId 3 -X 0 -Y -6898 -Z 1008 -RequiredEnabledLineEntityId 3 -EntityName 'border1' -ScriptType 'Move' -TriggerLine @{ startX=-92; startY=-6898; startZ=1008; endX=92; endY=-6898; endZ=1008 }

# mtcrl_5 (463) is the native falling-track/climbing minigame, and mtcrl_8
# (466) is the automatically staged nest scene. Neither is ordinary walking:
# they retain native OK/direction and ASK input, then MAPJUMP back themselves.

# Reviewed above. Mount Corel is crossed again later on unrelated business, and these
# first-visit routes are deliberately silent then, so nothing may be added here.
Add-CuratedFields 458, 459, 460, 461, 462, 463, 464, 465, 466, 467
