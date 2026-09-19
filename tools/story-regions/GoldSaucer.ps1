# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
# First-visit Gold Saucer only: paid entrance (GameMoment 439) through the Battle
# Square accusation that drops the party into Corel Prison (GameMoment 445).
# Installed gldgate/coloss/coloin1/clsin2_1/games scripts, walkmesh triangles and
# gateway geometry were read from the native flevel. Evidence:
# .active-build-gold-saucer-20260906/native/field-*.txt.
#
# Native first-visit spine, all verified from SETWORD $GameMoment writes:
#   436 gldst/dic Main      arrival at the Ropeway Station (already covered)
#   439 gldgate/dic Main    entrance scene on the Terminal Floor
#   440 gldgate/dic Script3 companion choice, forced by chekun on a tube platform
#   442 games/dic Main      Cait Sith joins, automatic on entering Wonder Square
#   445 clsin2_3/dic Main   accusation cutscene, then MAPJUMP to 471 jail1

# The generic extractor produced two Gold Saucer rows with no GameMoment gate at
# all, so they advertised much later chapters during the first visit. Gate them
# to the stages their own native scripts require. ghotin_4/dic Main writes 589
# and clsin2_2/dio Talk writes 580; neither is reachable before those stages.
foreach ($definition in $definitions) {
    if ($definition.fieldId -eq 493 -and $definition.minimumGameMoment -lt 0 -and
        $definition.maximumGameMoment -lt 0) {
        $definition.minimumGameMoment = 586
        $definition.maximumGameMoment = 589
    }
    if ($definition.fieldId -eq 503 -and $definition.minimumGameMoment -lt 0 -and
        $definition.maximumGameMoment -lt 0) {
        $definition.minimumGameMoment = 580
        $definition.maximumGameMoment = 583
    }
}

# The generic extractor traced gldgate's 440 write back to ev/'Go 1x' and placed
# a four-area row on the entrance line at (432,-494) - the line the player has
# just crossed coming in from the Ropeway Station, so it points backwards. The
# real 440 write is dic Script 3, the companion choice that chekun forces when
# the player steps onto any Square platform, and the platforms below replace it.
for ($index = $definitions.Count - 1; $index -ge 0; $index--) {
    $definition = $definitions[$index]
    if ($definition.fieldId -eq 497 -and $definition.kind -eq 'Location' -and
        $definition.sourceEntityName -eq 'ev' -and
        $definition.label -like 'Continue Gold Saucer*') {
        $definitions.RemoveAt($index)
    }
}

# gldgate/chekun Main polls the party leader every frame:
#   Bank[6][7] = pos.triangleId; if (Bank[6][7] >= 24) { if ($GameMoment < 440)
#   run dic Script 3 (companion choice); then cloud Script 4 }
# cloud Script 4 selects the destination purely from that triangle id. The seven
# ramp pads are walkmesh triangles 24..37 in pairs; the z=18 member of each pair
# is the step reached first from the plaza floor, so it is the approach point.
# The Terminal Floor has exactly one native gateway (back to the Ropeway Station),
# so without these rows the seven Squares are unreachable by a blind player.
#
# Completion is the triangle set itself, never a radius. cloud Script 4 fires only
# once the party leader's own triangle is inside the pair, so a proximity arrival
# released auto walk while the player was still on the plaza outside the ramp.
# Triangles 38..51 are the tube interiors reached mid-jump and on return, so they
# are deliberately excluded even though the native `>= 36` test would accept them.
$goldSaucerPlatforms = @(
    @{ pads = @(24, 25); label = 'Ghost Square';   x = -133; y = 627; z = 18; field = 491 },
    @{ pads = @(26, 27); label = 'Battle Square';  x =  427; y = 479; z = 18; field = 499 },
    @{ pads = @(28, 29); label = 'Wonder Square';  x =  587; y = 259; z = 18; field = 505 },
    @{ pads = @(30, 31); label = 'Chocobo Square'; x =  639; y = -22; z = 18; field = 509 },
    @{ pads = @(32, 33); label = 'Event Square';   x = -637; y =  19; z = 18; field = 484 },
    @{ pads = @(34, 35); label = 'Speed Square';   x = -559; y = 304; z = 18; field = 486 },
    @{ pads = @(36, 37); label = 'Round Square';   x = -390; y = 507; z = 18; field = 488 }
)

# Wonder Square holds Cait Sith at 439..441; Battle Square holds the accusation
# at 442..444. Every other platform stays selectable at priority 1 across the
# whole first visit so an optional Square is never a prerequisite.
#
# Get-DefinitionKey deliberately ignores the GameMoment window, so two rows that
# differ only by stage collapse into the earlier one. Emit each platform once,
# and split only the two Squares whose label and priority actually change.
$goldSaucerObjectiveStages = @{ 'Wonder Square' = 439; 'Battle Square' = 442 }
foreach ($platform in $goldSaucerPlatforms) {
    if (-not $goldSaucerObjectiveStages.ContainsKey($platform.label)) {
        Add-Definition -FieldId 497 -FieldName 'gldgate' -Kind Location `
            -Label "Take the $($platform.label) platform (optional)" `
            -X $platform.x -Y $platform.y -Z $platform.z `
            -MinimumGameMoment 439 -MaximumGameMoment 444 -Priority 1 `
            -CompletionPlayerTriangles $platform.pads `
            -EntityName 'chekun' -ScriptType 'Main'
        continue
    }

    $objectiveStart = $goldSaucerObjectiveStages[$platform.label]
    Add-Definition -FieldId 497 -FieldName 'gldgate' -Kind Location `
        -Label "Take the $($platform.label) platform" `
        -X $platform.x -Y $platform.y -Z $platform.z `
        -MinimumGameMoment $objectiveStart -MaximumGameMoment ($objectiveStart + 2) -Priority 0 `
        -CompletionPlayerTriangles $platform.pads `
        -EntityName 'chekun' -ScriptType 'Main'
    $optionalStart = if ($objectiveStart -eq 439) { 442 } else { 439 }
    Add-Definition -FieldId 497 -FieldName 'gldgate' -Kind Location `
        -Label "Take the $($platform.label) platform (optional)" `
        -X $platform.x -Y $platform.y -Z $platform.z `
        -MinimumGameMoment $optionalStart -MaximumGameMoment ($optionalStart + 2) -Priority 1 `
        -CompletionPlayerTriangles $platform.pads `
        -EntityName 'chekun' -ScriptType 'Main'
}

# gldgate/al is an [OK] line, not a gateway, so the information counter never
# appears in the ordinary Exits list. Its [OK] script returns immediately while the
# GameMoment is still 439 (bytes 0..8) and again at 598, and only reaches the
# MAPJUMP to 498 gldinfo otherwise. Offering it before the companion choice would
# advertise a button press that natively does nothing, so it starts at 440.
Add-Definition -FieldId 497 -FieldName 'gldgate' -Kind Location -EntityId 14 `
    -Label 'Use the Gold Saucer information counter (optional)' `
    -X 127 -Y 552 -Z 0 -MinimumGameMoment 440 -MaximumGameMoment 444 -Priority 1 `
    -EntityName 'al' -ScriptType '[OK]' `
    -RequiredEnabledLineEntityId 14 -TriggerLine ([ordered]@{ startX = 222; startY = 533; startZ = 0; endX = 32; endY = 572; endZ = 0 })

# Nothing inside the Battle Square is a navigable objective at 442. coloss/dic Main
# byte 34 gates the whole discovery on $GameMoment === 442 and runs it the instant
# the field loads, ending at byte 365 with MAPJUMP 501; coloin2 then hands off to
# clsin2_1, whose own 442 gate ends in MAPJUMP 504, which writes 445 and jumps to
# 471 jail1. So the last thing the player actually steers is the Battle Square
# platform on the Terminal Floor, and the rest is a cutscene chain. Offering an
# Arena Lobby route at 442 would advertise a walk the player never gets to make.
#
# The Battle Square interior is closed for the whole first visit, so no row here
# may promise it. coloss entity 9 man2 is the renovation guard, standing at the
# foot of the stairs at (7,-1927,-1175) on triangle 14:
#   Init b16  IFSW  if ($GameMoment >= 442) else goto 32
#             >=442 -> TLKON off, SOLID NonSolid, VISI NotVisible   (guard gone)
#             < 442 -> b32 SLIDR setCollisionRadius(60)             (guard solid)
#   Talk b0   IFSW  if ($GameMoment < 442) -> dialogue 0,
#             "I'm sorry, we're currently renovating. Please come again."
# Entity 8 man is the 442-only scene actor and is invisible and non-solid at every
# other moment, so before 442 the only body on that walkway is the solid guard.
# The stairs, the Arena Lobby, the arena and Dio's Museum are therefore all
# unreachable at 440..441; the absence of the 442 cutscene does not open them, and
# the museum reference image root reviewed is later-chapter material. The guide
# separately notes the arena competition needs the Buggy, which the party does not
# have on this visit.
#
# The gateways themselves stay in the ordinary native Exits list untouched; what is
# removed is only this region's claim that they are first-visit objectives.

# Cait Sith joins in Wonder Square (games), not on the Terminal Floor. Its jp3
# LINE contact script delegates to cloud Script 4 with destination selector 3,
# which MAPJUMPs directly to coloss (499). Offer that visible tube at moment 442.
Add-Definition -FieldId 505 -FieldName 'games' -Kind Location -EntityId 18 `
    -Label 'Take the Battle Square exit' -X 11 -Y -834 -Z 0 `
    -MinimumGameMoment 442 -MaximumGameMoment 444 -Priority 0 `
    -EntityName 'jp3' -ScriptType 'Contact' -RequiredEnabledLineEntityId 18 `
    -TriggerLine ([ordered]@{ startX=30; startY=-852; startZ=0; endX=-8; endY=-815; endZ=0 })

# --- Available attractions -------------------------------------------------
# Every machine below is an [OK] or walk-over LINE, not a gateway and not a model,
# so none of them appear in the ordinary Exits, Objects or NPCs lists. Their
# instructions, price prompt and result windows are ordinary native dialogue that
# the existing message path already speaks; what was missing was any way to find
# them. Coordinates are the midpoints of the installed LINE triggers.
#
# Availability was taken from the installed scripts, not from a guide:
#  - games_2/snowb and snowb2 test `$GameMoment < 790` and answer with a bystander
#    line instead of the Snow Game, so the snow game is deliberately absent here.
#  - games_2/subm is NOT playable here, and its own [OK] script does not say so.
#    The machine script has no GameMoment gate, but entity 9 m6 stands on the
#    LINE at (-333,26,21) on triangle 60 and only turns itself off at
#    $GameMoment >= 1299 (Init b16 IFSW 1620000013050407); below that it stays
#    solid and visible and its Talk answers "It seems to be out of order."
#    Checking the machine script alone was the mistake that produced an earlier
#    false claim that the arcade submarine was first-visit playable.
#    Entity 8 m5 near the snow game is explicitly NonSolid, so that one really is
#    gated only by snowb's own $GameMoment < 790 test.
$goldSaucerAttractions = @(
    @{ field=506; name='games_1'; entity=11; label='Play the Arm Wrestling machine (optional)';
       x=183; y=1610; z=-255; script='[OK]'; entityName='udel';
       line=[ordered]@{ startX=178; startY=1645; startZ=-255; endX=189; endY=1575; endZ=-255 } },
    @{ field=506; name='games_1'; entity=16; label='Play the Basketball Game (optional)';
       x=-229; y=1664; z=-255; script='Go'; entityName='bsl';
       line=[ordered]@{ startX=-188; startY=1618; startZ=-255; endX=-271; endY=1711; endZ=-255 } },
    @{ field=507; name='games_2'; entity=19; label='Play G Bike (optional)';
       x=241; y=174; z=0; script='[OK]'; entityName='bikeg';
       line=[ordered]@{ startX=272; startY=195; startZ=0; endX=210; endY=154; endZ=0 } },
    @{ field=507; name='games_2'; entity=18; label='Enter the Mog House (optional)';
       x=3; y=-252; z=0; script='[OK]'; entityName='mogu';
       line=[ordered]@{ startX=12; startY=-233; startZ=0; endX=-6; endY=-271; endZ=0 } },
    @{ field=507; name='games_2'; entity=24; label='Use the Fortune Telling machine (optional)';
       x=299; y=153; z=0; script='[OK]'; entityName='la';
       line=[ordered]@{ startX=291; startY=171; startZ=0; endX=307; endY=135; endZ=0 } }
)
foreach ($attraction in $goldSaucerAttractions) {
    Add-Definition -FieldId $attraction.field -FieldName $attraction.name -Kind Location `
        -EntityId $attraction.entity -Label $attraction.label `
        -X $attraction.x -Y $attraction.y -Z $attraction.z `
        -MinimumGameMoment 440 -MaximumGameMoment 444 -Priority 1 `
        -EntityName $attraction.entityName -ScriptType $attraction.script `
        -RequiredEnabledLineEntityId $attraction.entity -TriggerLine $attraction.line
}

# --- The Shooting Coaster's own registration -------------------------------
#
# Not an attraction trigger like the rows above: jetin1 has no line that starts the ride.
# Entity 10 che is the barrier that turns the player back - its Go 1x calls man1's script
# 3, "This way, Sir, to register." - so it is deliberately not a target. The ride is
# reached by talking to the guide and then to the attendant, and the attendant's own Talk
# opens the way and starts it.
#
# 3[72] bit 4 is "the guide has explained it", which her Talk sets itself at byte 10.
# Until it is set the attendant only answers "Is this your first time?" at byte 82, so the
# guide has to come first. 3[72] bit 5 is the ride in progress.
$shootingCoasterGuideUnasked = New-Condition -Bank 3 -Address 72 -Mask 0x10 -Value 0x00
$shootingCoasterGuideAsked = New-Condition -Bank 3 -Address 72 -Mask 0x10 -Value 0x10
$shootingCoasterNotRiding = New-Condition -Bank 3 -Address 72 -Mask 0x20 -Value 0x00

Add-Definition -FieldId 487 -FieldName 'jetin1' -Kind Model -EntityId 12 -Priority 1 `
    -Label 'Ask the Shooting Coaster guide about the ride (optional)' `
    -MinimumGameMoment 440 -MaximumGameMoment 444 `
    -RequiredConditions @($shootingCoasterGuideUnasked, $shootingCoasterNotRiding) `
    -EntityName 'gairl' -ScriptType 'Talk'

# The attendant takes the ten GP and starts the ride. Whether to pay is his question and
# the player's answer; this only says where he is.
Add-Definition -FieldId 487 -FieldName 'jetin1' -Kind Model -EntityId 11 -Priority 1 `
    -Label 'Talk to the Shooting Coaster attendant to register (optional)' `
    -MinimumGameMoment 440 -MaximumGameMoment 444 `
    -RequiredConditions @($shootingCoasterGuideAsked, $shootingCoasterNotRiding) `
    -EntityName 'man1' -ScriptType 'Talk'

# --- Arcade machines the first audit missed --------------------------------
# Both of these were found by following the LINE's request into the script it
# runs, which the earlier pass stopped short of doing.
#
# Wonder Catcher: games_1 ufo1(14) and ufo2(15) each [OK] byte 4 request
# cloud(1) scripts 8 and 9. Both scripts open with dialogue 10,
# '"Wonder Catcher" 1 game 100 gil', then the ordinary Try it / Not interested
# ASK. Both sides of the cabinet are usable.
#
# 3D Battler: games_2 kakul1(10) and kakul2(11) script 4 "Go" show dialogue 3,
# '"3D Battler" 1 game 200 gil ... The first one with 10 points wins', and byte
# 101 requests dic(0) script 3, the shared game loop. The literal entity name is
# not Battler, which is why a name search missed it. Note the native entry also
# requires the player to be facing the machine: byte 0 IFKEYON plus byte 4 PGTDR
# with byte 8 testing direction 96.
$goldSaucerArcadeMachines = @(
    @{ field=506; name='games_1'; entity=14; label='Play the Wonder Catcher (optional)';
       x=286; y=1345; z=-255; script='[OK]'; entityName='ufo1';
       line=[ordered]@{ startX=300; startY=1371; startZ=-255; endX=272; endY=1319; endZ=-255 } },
    @{ field=506; name='games_1'; entity=15; label='Play the Wonder Catcher from its other side (optional)';
       x=358; y=1418; z=-255; script='[OK]'; entityName='ufo2';
       line=[ordered]@{ startX=384; startY=1423; startZ=-255; endX=333; endY=1414; endZ=-255 } },
    @{ field=507; name='games_2'; entity=10; label='Play 3D Battler (optional)';
       x=-166; y=-144; z=32; script='Go'; entityName='kakul1';
       line=[ordered]@{ startX=-152; startY=-121; startZ=32; endX=-181; endY=-168; endZ=32 } },
    @{ field=507; name='games_2'; entity=11; label='Play 3D Battler from its other side (optional)';
       x=-110; y=-201; z=32; script='Go'; entityName='kakul2';
       line=[ordered]@{ startX=-99; startY=-174; startZ=32; endX=-121; endY=-228; endZ=32 } }
)
foreach ($machine in $goldSaucerArcadeMachines) {
    Add-Definition -FieldId $machine.field -FieldName $machine.name -Kind Location `
        -EntityId $machine.entity -Label $machine.label `
        -X $machine.x -Y $machine.y -Z $machine.z `
        -MinimumGameMoment 440 -MaximumGameMoment 444 -Priority 1 `
        -EntityName $machine.entityName -ScriptType $machine.script `
        -RequiredEnabledLineEntityId $machine.entity -TriggerLine $machine.line
}

# games_1 s1(8) is the GP prize counter, placed at (182,1543,-255) on triangle
# 112. Its Talk opens with "We got new prizes. But what's in them is a secret."
# and then "You have {MEM1} GP." The prize contents stay secret until the native
# text reveals them, so this row only makes the counter reachable.
Add-Definition -FieldId 506 -FieldName 'games_1' -Kind Model -EntityId 8 `
    -Label 'Talk to the Wonder Square prize counter (optional)' `
    -MinimumGameMoment 440 -MaximumGameMoment 444 -Priority 1 `
    -EntityName 's1' -ScriptType 'Talk'

# Reviewed above, including the arcade rooms and the second-visit content that must
# not be advertised on a first visit.
Add-CuratedFields 497, 498, 499, 500, 501, 502, 503, 504, 505, 506, 507, 508
