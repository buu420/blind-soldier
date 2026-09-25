# Dot-sourced by Generate-FieldStoryEvents.ps1 after generic milestone extraction.
#
# Cosmo Canyon and the Cave of the Gi, complete, from the arrival at 469 that the Corel
# Prison chapter's own write leaves behind to the departure that writes 523.
#
# Almost none of it is visible to milestone extraction. Six GameMoment writes exist
# across the whole chapter - bugin1c 493, cos_btm2/RED 502, gidun_3 and seto1 514,
# cos_btm/LINEO 523 - and one more, bugin1a/AD Script 7 writing 1391, belongs to the
# Huge Materia visit hundreds of moments later. Everything between them is ordered by
# local flags, by which walkmesh component the party is standing on, and by doors.
#
# The route, from the installed scripts, gateway tables and walkmesh:
#
#   469  cos_btm/WATCH 15 Talk sets Bank[3][161] bit 2 at byte 486.
#        cos_btm/RED 9 Talk sets bit 3 at 182, remembers the arriving party in
#        Bank[3][169] and reduces it to Cloud and two empty slots at 232.
#   ...  The town is two places. Arriving from the world map is triangle 58, in a
#        288-triangle component that does not reach the terrace the observatory path
#        starts from; that terrace is a separate 56-triangle component reached only
#        through the inn - cos_btm g1 to cosin1 at triangle 36, then cosin1 g1 back out
#        to cos_btm at triangle 340. From there g3 leads to cosin2.
#        cosin2/LINEL 13 is not a doorway: its Go tests for a fresh confirm press and
#        then runs the leader's own Script 3, which climbs to cos_top. cos_top g0 is
#        Bugenhagen's door.
#   ...  bugin2's first AD Script 3 sets Bank[3][170] bit 0 at 102 and Bank[3][161]
#        bit 4 at 106, and leaves the party as Cloud alone. Bit 4 is what makes the
#        companions offer the party menu at all; five of them do, in four rooms, and
#        the player picks. What says the party is whole again is the party itself -
#        Bank[3][10] and Bank[3][11], the two seats beside the leader, stop reading
#        255 - and not Bank[3][170] bit 1, which bugin2's own Director only sets on
#        the way back in.
#   ...  bugin2 g1 to bugin1a, whose BUGEN 11 Talk starts the demonstration with bit 6
#        still clear and sets it at 470. bugin1c then writes 493.
#   493  The campfire in cos_btm2: three conversations in any order, then Red XIII,
#        whose Talk writes 502.
#   502  cosin2/BUGEN 15 opens the sealed door - collision off triangle 41, bit 2 set -
#        and g4 behind it leads into the stairwell.
#   ...  cosin5 is seven storeys joined by seven pairs of ladders that no LINE triggers:
#        four unnamed entities poll the leader's own triangle and ask for a climb when
#        the confirm key is down. Its bottom LINEJ 14 enters the Cave of the Gi.
#   ...  gidun_1 has four openings in its walls, at triangles 48, 95, 171 and 210. Each
#        is a polled trigger that asks a question; one of them opens the way on and the
#        others are what is living in the cave. Which is which is not said here.
#        Opening the way clears the collision on triangle 16 and sets Bank[3][182]
#        bit 4; LINEJB 24 then leads to gidun_2, g0 from there to gidun_4, g0 from
#        there to gidun_3, and its LINEE 15 is the crossing that writes 514.
#   514  seto1 returns the party to the campfire by itself.
#   ...  cos_btm/LINEO 16 is the way out of the canyon and writes 523.
#
# The observatory's own exhibits, the shops and inn rooms visited on the way, the
# treasure in gidun_1 and the webs and spiders in gidun_2 and gidun_4 are not steps and
# are not offered as any. The three spider lines in gidun_4 start a battle on being
# walked onto, which is a reason not to send anyone to them.

$cosmoFirstVisit = @{ MinimumGameMoment = 469; MaximumGameMoment = 492; Priority = 0 }
$cosmoCampfireWindow = @{ MinimumGameMoment = 493; MaximumGameMoment = 501; Priority = 0 }
$cosmoSealedDoorWindow = @{ MinimumGameMoment = 502; MaximumGameMoment = 513; Priority = 0 }
$cosmoDepartureWindow = @{ MinimumGameMoment = 514; MaximumGameMoment = 522; Priority = 0 }

# The two halves of the canyon floor, measured on the installed walkmesh from the world
# arrival at triangle 58 and from the inn's upper door at triangle 340.
$cosmoLowerTown = @(16..19) + @(22..48) + @(50..64) + @(66..82) + @(84..166) + @(170..183) + @(186..195) + @(202..224) + @(229..289) + @(291..308) + @(317..332)
$cosmoUpperTown = @(0..1) + @(8..9) + @(11) + @(20..21) + @(49) + @(65) + @(83) + @(184..185) + @(196..201) + @(290) + @(309..310) + @(313..314) + @(333..365)

$cosmoGuardSpoken = New-Condition -Bank 3 -Address 161 -Mask 4 -Value 4
$cosmoGuardUnspoken = New-Condition -Bank 3 -Address 161 -Mask 4 -Value 0
$cosmoRedSpoken = New-Condition -Bank 3 -Address 161 -Mask 8 -Value 8
$cosmoRedUnspoken = New-Condition -Bank 3 -Address 161 -Mask 8 -Value 0
$cosmoArrivalDone = New-Condition -Bank 3 -Address 161 -Mask 12 -Value 12
$cosmoObservatorySeen = New-Condition -Bank 3 -Address 170 -Mask 1 -Value 1
$cosmoObservatoryUnseen = New-Condition -Bank 3 -Address 170 -Mask 1 -Value 0
$cosmoPartyConfirmed = New-Condition -Bank 3 -Address 170 -Mask 2 -Value 2
$cosmoMenusOpen = New-Condition -Bank 3 -Address 161 -Mask 16 -Value 16
$cosmoDemonstrationDone = New-Condition -Bank 3 -Address 170 -Mask 64 -Value 64
$cosmoDemonstrationPending = New-Condition -Bank 3 -Address 170 -Mask 64 -Value 0
$cosmoDoorSealed = New-Condition -Bank 3 -Address 170 -Mask 4 -Value 0
$cosmoDoorOpen = New-Condition -Bank 3 -Address 170 -Mask 4 -Value 4

# The party. Bank 3[9] is the leader and 3[10] and 3[11] are the two others, so one
# filled slot is not a whole party. A slot nobody is in reads 255, and no character id
# has eight bits set, so counting them is how "somebody is here" is said with the
# conditions this catalog has.
$cosmoPartyWhole1 = New-Condition -Bank 3 -Address 10 -Mask 255 -Value 0 -MaximumSetBits 7
$cosmoPartyWhole2 = New-Condition -Bank 3 -Address 11 -Mask 255 -Value 0 -MaximumSetBits 7
$cosmoPartySlotEmpty1 = New-Condition -Bank 3 -Address 10 -Mask 255 -Value 255
$cosmoPartySlotEmpty2 = New-Condition -Bank 3 -Address 11 -Mask 255 -Value 255

# --- Arriving in the canyon ------------------------------------------------------
Add-Definition @cosmoFirstVisit -FieldId 525 -FieldName 'cos_btm' -Kind Model -EntityId 15 `
    -Label 'Talk to the guard at the way into the canyon' `
    -RequiredCondition $cosmoGuardUnspoken -CompletedCondition $cosmoGuardSpoken `
    -EntityName 'WATCH' -ScriptType 'Talk'

Add-Definition @cosmoFirstVisit -FieldId 525 -FieldName 'cos_btm' -Kind Model -EntityId 9 `
    -Label 'Talk to Red XIII' `
    -RequiredConditions @($cosmoGuardSpoken, $cosmoRedUnspoken) `
    -CompletedCondition $cosmoRedSpoken `
    -EntityName 'RED' -ScriptType 'Talk'

# --- Up to the observatory, the first time ---------------------------------------
# The canyon floor is two places and the inn is the stair between them, so the climb is
# four doors rather than one: the inn's lower door, the inn's upper door, the terrace
# path, and Bugenhagen's own. The same four serve the climb back after the party has
# been chosen, under different state, which is why each is written twice.
$cosmoAscent = @(
    @{ field=525; name='cos_btm'; label='Go into the inn to reach the upper path';
       x=-1148; y=-907; z=-2141; entity='gateway1'; triangles=$cosmoLowerTown;
       line=@{ startX=-1201; startY=-893; startZ=-2141; endX=-1095; endY=-922; endZ=-2141 } },
    @{ field=529; name='cosin1'; label='Take the upper door out of the inn';
       x=-301; y=96; z=0; entity='gateway1'; triangles=@();
       line=@{ startX=-323; startY=137; startZ=0; endX=-280; endY=56; endZ=0 } },
    @{ field=525; name='cos_btm'; label='Take the upper path toward the observatory';
       x=-494; y=-504; z=-1468; entity='gateway3'; triangles=$cosmoUpperTown;
       line=@{ startX=-536; startY=-468; startZ=-1468; endX=-452; endY=-541; endZ=-1468 } },
    @{ field=540; name='cos_top'; label="Go in to Bugenhagen's house";
       x=-289; y=-282; z=-544; entity='gateway0'; triangles=@();
       line=@{ startX=-327; startY=-240; startZ=-544; endX=-251; endY=-325; endZ=-544 } }
)
foreach ($step in $cosmoAscent) {
    foreach ($pass in @(
        @{ label = $step.label; conditions = @($cosmoArrivalDone, $cosmoObservatoryUnseen) },
        @{ label = ($step.label -replace '^Go ', 'Go back ' -replace '^Take ', 'Take ');
           conditions = @($cosmoObservatorySeen, $cosmoDemonstrationPending, $cosmoPartyWhole1, $cosmoPartyWhole2) })) {
        $parameters = @{
            FieldId = $step.field
            FieldName = $step.name
            Kind = 'Location'
            Label = $pass.label
            X = $step.x
            Y = $step.y
            Z = $step.z
            EntityName = $step.entity
            ScriptType = 'Gateway'
            RequiredConditions = $pass.conditions
            TriggerLine = $step.line
        }
        if ($step.triangles.Count -gt 0) { $parameters.RequiredPlayerTriangles = $step.triangles }
        Add-Definition @cosmoFirstVisit @parameters
    }
}

# The climb between the terrace and the observatory. LINEL is not a doorway: its Go
# waits for a fresh confirm press and then runs the leader's own climb script, so the
# objective has to stay put until the player presses it rather than reporting itself
# done on arrival.
foreach ($pass in @(
    @{ conditions = @($cosmoArrivalDone, $cosmoObservatoryUnseen) },
    @{ conditions = @($cosmoObservatorySeen, $cosmoDemonstrationPending, $cosmoPartyWhole1, $cosmoPartyWhole2) })) {
    Add-Definition @cosmoFirstVisit -FieldId 531 -FieldName 'cosin2' -Kind Location -EntityId 13 `
        -Label 'Press Confirm at the ladder up to the observatory' -X -210 -Y 198 -Z -12 `
        -RequiredConditions $pass.conditions `
        -EntityName 'LINEL' -ScriptType 'Go' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
        -RequiredEnabledLineEntityId 13 `
        -TriggerLine ([ordered]@{ startX=-224; startY=164; startZ=-16; endX=-196; endY=233; endZ=-9 })
}

# --- The observatory, the first time ---------------------------------------------
# Until the scene in bugin2 has played, the room has exactly one step in it, and the
# catalog did not have it: a player standing in Bugenhagen's house with Red XIII in
# front of them was told Story: none.
#
# bugin2/RED 5 Init is the gate, and it is a complete one. It returns at once unless
# Bank[3][161] bit 3 is set - Red XIII has to have been spoken to down in the canyon
# first - and returns again once Bank[3][170] bit 0 is set. Only between those two does
# it reach TLKON, VISI and the two IDLCKs at 38 and 42 that hold triangles 23 and 18
# while he is standing there. His Talk is three bytes: REQ AD 10 Script 3, and it is
# that script's only caller. AD 3 plays the whole introduction and sets Bank[3][170]
# bit 0 at byte 102, so the flag that hides him again is the flag the scene writes.
Add-Definition @cosmoFirstVisit -FieldId 544 -FieldName 'bugin2' -Kind Model -EntityId 5 `
    -Label 'Talk to Red XIII' `
    -RequiredConditions @($cosmoRedSpoken, $cosmoObservatoryUnseen) `
    -CompletedCondition $cosmoObservatorySeen `
    -EntityName 'RED' -ScriptType 'Talk'

# --- Back down again to choose who comes -----------------------------------------
# The first observatory scene ends with the party standing inside bugin2 as Cloud
# alone. The companions are in four rooms scattered through the canyon, so the way to
# them is back out of the house, down the ladder to the upper town, and on from there.
$cosmoChoosing = @($cosmoObservatorySeen, $cosmoDemonstrationPending)

Add-Definition @cosmoFirstVisit -FieldId 544 -FieldName 'bugin2' -Kind Location `
    -Label 'Go back out to find the others' -X -418 -Y -370 -Z -625 `
    -RequiredConditions ($cosmoChoosing + @($cosmoPartySlotEmpty1)) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-359; startY=-409; startZ=-625; endX=-477; endY=-332; endZ=-625 })

Add-Definition @cosmoFirstVisit -FieldId 544 -FieldName 'bugin2' -Kind Location `
    -Label 'Go back out to find the others' -X -418 -Y -370 -Z -625 `
    -RequiredConditions ($cosmoChoosing + @($cosmoPartySlotEmpty2)) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-359; startY=-409; startZ=-625; endX=-477; endY=-332; endZ=-625 })

foreach ($empty in @($cosmoPartySlotEmpty1, $cosmoPartySlotEmpty2)) {
    Add-Definition @cosmoFirstVisit -FieldId 540 -FieldName 'cos_top' -Kind Location -EntityId 6 `
        -Label 'Press Confirm at the ladder back down' -X -77 -Y -492 -Z -542 `
        -RequiredConditions ($cosmoChoosing + @($empty)) `
        -EntityName 'LINEL' -ScriptType 'Go' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
        -RequiredEnabledLineEntityId 6 `
        -TriggerLine ([ordered]@{ startX=9; startY=-469; startZ=-544; endX=-164; endY=-516; endZ=-540 })

    # From the upper town, the two nearest companions are behind cosin2's own doors and
    # the rest are down in the canyon. All three are offered; the player chooses.
    foreach ($door in @(
        @{ entity='gateway2'; label='Go through to the kitchen'; x=383; y=255; z=-1;
           line=@{ startX=386; startY=201; startZ=-1; endX=381; endY=309; endZ=-1 } },
        @{ entity='gateway1'; label='Go through to the house next door'; x=393; y=30; z=-1;
           line=@{ startX=399; startY=-25; startZ=-1; endX=387; endY=85; endZ=-1 } },
        @{ entity='gateway0'; label='Go back down into the canyon'; x=96; y=-356; z=-1;
           line=@{ startX=36; startY=-377; startZ=-1; endX=156; endY=-336; endZ=-1 } })) {
        Add-Definition @cosmoFirstVisit -FieldId 531 -FieldName 'cosin2' -Kind Location `
            -Label $door.label -X $door.x -Y $door.y -Z $door.z `
            -RequiredConditions ($cosmoChoosing + @($empty)) `
            -EntityName $door.entity -ScriptType 'Gateway' `
            -TriggerLine $door.line
    }

    # cosin2's own way down lands on the upper half of the canyon floor, and the inn is
    # the only way from there to the lower half where the shop and the inn's own room
    # are. Its upper entrance is a different gateway from its lower one.
    Add-Definition @cosmoFirstVisit -FieldId 525 -FieldName 'cos_btm' -Kind Location `
        -Label 'Go into the inn to reach the lower canyon' -X -1055 -Y -423 -Z -1805 `
        -RequiredConditions ($cosmoChoosing + @($empty)) -RequiredPlayerTriangles $cosmoUpperTown `
        -EntityName 'gateway2' -ScriptType 'Gateway' `
        -TriggerLine ([ordered]@{ startX=-1033; startY=-356; startZ=-1805; endX=-1076; endY=-489; endZ=-1805 })

    Add-Definition @cosmoFirstVisit -FieldId 529 -FieldName 'cosin1' -Kind Location `
        -Label 'Take the lower door out of the inn' -X -110 -Y -369 -Z -192 `
        -RequiredConditions ($cosmoChoosing + @($empty)) `
        -EntityName 'gateway0' -ScriptType 'Gateway' `
        -TriggerLine ([ordered]@{ startX=-174; startY=-337; startZ=-192; endX=-45; endY=-400; endZ=-192 })
}

# --- Choosing who comes ----------------------------------------------------------
# Five companions in four rooms, all opening the same native party menu. None of them
# is the required one; the player picks, and Cait Sith in the kitchen is simply the
# nearest to the observatory. The menu can be cancelled and it can be used twice, so
# each is offered while either of the two slots beside Cloud is still empty.
foreach ($companion in @(
    @{ field=536; name='cosmin3'; entity=3;  who='Cait Sith in the kitchen' },
    @{ field=530; name='cosin1_1'; entity=2; who='Barret upstairs at the inn' },
    @{ field=532; name='cosin3'; entity=5;   who='Aeris in the shop' },
    @{ field=532; name='cosin3'; entity=6;   who='Tifa in the shop' },
    @{ field=535; name='cosmin2'; entity=3;  who='Yuffie in the house' })) {
    foreach ($empty in @($cosmoPartySlotEmpty1, $cosmoPartySlotEmpty2)) {
        Add-Definition @cosmoFirstVisit -FieldId $companion.field -FieldName $companion.name `
            -Kind Model -EntityId $companion.entity `
            -Label ("Talk to {0} to fill the party again" -f $companion.who) `
            -RequiredConditions @($cosmoMenusOpen, $empty) `
            -EntityName 'companion' -ScriptType 'Talk'
    }
}

# And the way back out of each of those rooms once the party is whole, before bugin2
# has had the chance to set its own bit. A room the player only visited to use a menu
# still has to have a way out of it.
foreach ($interior in @(
    @{ field=536; name='cosmin3'; label='Leave the kitchen'; x=-259; y=221; z=7;
       line=@{ startX=-281; startY=191; startZ=7; endX=-238; endY=251; endZ=7 } },
    @{ field=535; name='cosmin2'; label='Leave the house'; x=-55; y=393; z=3;
       line=@{ startX=-110; startY=400; startZ=3; endX=0; endY=387; endZ=3 } },
    @{ field=530; name='cosin1_1'; label='Go back downstairs'; x=314; y=-220; z=0;
       line=@{ startX=272; startY=-269; startZ=0; endX=356; endY=-172; endZ=0 } },
    @{ field=532; name='cosin3'; label='Leave the shop'; x=588; y=-91; z=-253;
       line=@{ startX=574; startY=-181; startZ=-253; endX=602; endY=-1; endZ=-253 } })) {
    Add-Definition @cosmoFirstVisit -FieldId $interior.field -FieldName $interior.name -Kind Location `
        -Label $interior.label -X $interior.x -Y $interior.y -Z $interior.z `
        -RequiredConditions @($cosmoObservatorySeen, $cosmoDemonstrationPending, $cosmoPartyWhole1, $cosmoPartyWhole2) `
        -EntityName 'gateway0' -ScriptType 'Gateway' `
        -TriggerLine $interior.line
}

# --- The demonstration -----------------------------------------------------------
Add-Definition @cosmoFirstVisit -FieldId 544 -FieldName 'bugin2' -Kind Location `
    -Label 'Go through to the room at the top' -X 225 -Y -189 -Z -624 `
    -RequiredConditions @($cosmoObservatorySeen, $cosmoPartyConfirmed, $cosmoDemonstrationPending) `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=255; startY=-158; startZ=-624; endX=196; endY=-220; endZ=-624 })

Add-Definition @cosmoFirstVisit -FieldId 541 -FieldName 'bugin1a' -Kind Model -EntityId 11 `
    -Label 'Talk to Bugenhagen in the observatory' `
    -RequiredConditions @(
        (New-Condition -Bank 3 -Address 170 -Mask 3 -Value 3),
        $cosmoDemonstrationPending) `
    -CompletedCondition $cosmoDemonstrationDone `
    -EntityName 'BUGEN' -ScriptType 'Talk'

# --- Back down to the fire -------------------------------------------------------
# The way down is the way up in reverse, except at the bottom: cosin2's own door lands
# on the upper half of the canyon floor and the fire is on the lower half, so the inn
# has to be crossed again - in through its upper entrance, out through its lower one.
$cosmoCampfirePending = New-Condition -Bank 3 -Address 171 -Mask 8 -Value 0

Add-Definition @cosmoCampfireWindow -FieldId 541 -FieldName 'bugin1a' -Kind Location `
    -Label 'Go back down from the observatory' -X -378 -Y 30 -Z -36 `
    -RequiredCondition $cosmoCampfirePending `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-365; startY=-1; startZ=-36; endX=-392; endY=62; endZ=-36 })

Add-Definition @cosmoCampfireWindow -FieldId 544 -FieldName 'bugin2' -Kind Location `
    -Label "Leave Bugenhagen's house" -X -418 -Y -370 -Z -625 `
    -RequiredCondition $cosmoCampfirePending `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-359; startY=-409; startZ=-625; endX=-477; endY=-332; endZ=-625 })

Add-Definition @cosmoCampfireWindow -FieldId 540 -FieldName 'cos_top' -Kind Location -EntityId 6 `
    -Label 'Press Confirm at the ladder back down' -X -77 -Y -492 -Z -542 `
    -RequiredCondition $cosmoCampfirePending `
    -EntityName 'LINEL' -ScriptType 'Go' -UsesPlayerCollisionRadius -KeepActiveOnArrival `
    -RequiredEnabledLineEntityId 6 `
    -TriggerLine ([ordered]@{ startX=9; startY=-469; startZ=-544; endX=-164; endY=-516; endZ=-540 })

Add-Definition @cosmoCampfireWindow -FieldId 531 -FieldName 'cosin2' -Kind Location `
    -Label 'Go back down into the canyon' -X 96 -Y -356 -Z -1 `
    -RequiredCondition $cosmoCampfirePending `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=36; startY=-377; startZ=-1; endX=156; endY=-336; endZ=-1 })

Add-Definition @cosmoCampfireWindow -FieldId 525 -FieldName 'cos_btm' -Kind Location `
    -Label 'Go into the inn to reach the lower canyon' -X -1055 -Y -423 -Z -1805 `
    -RequiredCondition $cosmoCampfirePending -RequiredPlayerTriangles $cosmoUpperTown `
    -EntityName 'gateway2' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-1033; startY=-356; startZ=-1805; endX=-1076; endY=-489; endZ=-1805 })

Add-Definition @cosmoCampfireWindow -FieldId 529 -FieldName 'cosin1' -Kind Location `
    -Label 'Take the lower door out of the inn' -X -110 -Y -369 -Z -192 `
    -RequiredCondition $cosmoCampfirePending `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-174; startY=-337; startZ=-192; endX=-45; endY=-400; endZ=-192 })

Add-Definition @cosmoCampfireWindow -FieldId 525 -FieldName 'cos_btm' -Kind Location -EntityId 27 `
    -Label 'Go over to the fire' -X -602 -Y -1707 -Z -2545 `
    -RequiredCondition $cosmoCampfirePending -RequiredPlayerTriangles $cosmoLowerTown `
    -EntityName 'LINEFR' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 27 `
    -TriggerLine ([ordered]@{ startX=-696; startY=-1574; startZ=-2553; endX=-508; endY=-1841; endZ=-2537 })


# --- The fire ---------------------------------------------------------------------
# The three conversations can be had in any order, so all three are offered together
# and each drops out as its own bit is set. Bank[3][161] bits 5 and 6 are set by
# BUGEN Script 5 during the demonstration, which is what puts the party round the fire.
$cosmoCampfire = @(
    @{ entity=7; name='BALLET'; bit=1;  label='Talk to Barret at the campfire' },
    @{ entity=8; name='TIFA';   bit=2;  label='Talk to Tifa at the campfire' },
    @{ entity=6; name='EARITH'; bit=4;  label='Talk to Aeris at the campfire' }
)
foreach ($conversation in $cosmoCampfire) {
    Add-Definition @cosmoCampfireWindow -FieldId 526 -FieldName 'cos_btm2' -Kind Model -EntityId $conversation.entity `
        -Label $conversation.label `
        -RequiredConditions @(
            (New-Condition -Bank 3 -Address 161 -Mask 96 -Value 96),
            $cosmoCampfirePending,
            (New-Condition -Bank 3 -Address 171 -Mask $conversation.bit -Value 0)) `
        -CompletedCondition (New-Condition -Bank 3 -Address 171 -Mask $conversation.bit -Value $conversation.bit) `
        -EntityName $conversation.name -ScriptType 'Talk'
}

# Red XIII last. His own Talk counts the three bits before it will go anywhere, so
# offering him earlier would send the player to a conversation that does not advance.
Add-Definition @cosmoCampfireWindow -FieldId 526 -FieldName 'cos_btm2' -Kind Model -EntityId 9 `
    -Label 'Talk to Red XIII at the campfire' -TargetGameMoment 502 `
    -RequiredConditions @(
        (New-Condition -Bank 3 -Address 161 -Mask 96 -Value 96),
        (New-Condition -Bank 3 -Address 171 -Mask 7 -Value 7)) `
    -EntityName 'RED' -ScriptType 'Talk'

# --- The sealed door --------------------------------------------------------------
foreach ($exit in @(
    @{ entity=16; label='Leave the fire'; x=-699; y=-1683; z=-2564;
       line=@{ startX=-703; startY=-1579; startZ=-2566; endX=-695; endY=-1787; endZ=-2563 } },
    @{ entity=17; label='Leave the fire by the other path'; x=-599; y=-1826; z=-2563;
       line=@{ startX=-695; startY=-1787; startZ=-2563; endX=-504; endY=-1866; endZ=-2563 } })) {
    Add-Definition @cosmoSealedDoorWindow -FieldId 526 -FieldName 'cos_btm2' -Kind Location -EntityId $exit.entity `
        -Label $exit.label -X $exit.x -Y $exit.y -Z $exit.z `
        -RequiredCondition $cosmoDoorSealed `
        -EntityName 'LINEFR' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
        -RequiredEnabledLineEntityId $exit.entity `
        -TriggerLine $exit.line
}

# Back up through the inn to the terrace, the same two-component climb as before.
Add-Definition @cosmoSealedDoorWindow -FieldId 525 -FieldName 'cos_btm' -Kind Location `
    -Label 'Go into the inn to reach the upper path' -X -1148 -Y -907 -Z -2141 `
    -RequiredCondition $cosmoDoorSealed -RequiredPlayerTriangles $cosmoLowerTown `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-1201; startY=-893; startZ=-2141; endX=-1095; endY=-922; endZ=-2141 })

Add-Definition @cosmoSealedDoorWindow -FieldId 529 -FieldName 'cosin1' -Kind Location `
    -Label 'Take the upper door out of the inn' -X -301 -Y 96 -Z 0 `
    -RequiredCondition $cosmoDoorSealed `
    -EntityName 'gateway1' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-323; startY=137; startZ=0; endX=-280; endY=56; endZ=0 })

Add-Definition @cosmoSealedDoorWindow -FieldId 525 -FieldName 'cos_btm' -Kind Location `
    -Label 'Take the upper path to the sealed door' -X -494 -Y -504 -Z -1468 `
    -RequiredCondition $cosmoDoorSealed -RequiredPlayerTriangles $cosmoUpperTown `
    -EntityName 'gateway3' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-536; startY=-468; startZ=-1468; endX=-452; endY=-541; endZ=-1468 })

# The sealed door. Bugenhagen is the only thing that opens it: his Talk turns the
# collision off triangle 41 at byte 131 and sets bit 2 at 227. Walking into the room is
# not enough, and its own LINES1 says as much if the player tries.
Add-Definition @cosmoSealedDoorWindow -FieldId 531 -FieldName 'cosin2' -Kind Model -EntityId 15 `
    -Label 'Talk to Bugenhagen at the sealed door' `
    -RequiredCondition $cosmoDoorSealed -CompletedCondition $cosmoDoorOpen `
    -EntityName 'BUGEN' -ScriptType 'Talk'

# --- Down the stairwell ------------------------------------------------------------
Add-Definition @cosmoSealedDoorWindow -FieldId 531 -FieldName 'cosin2' -Kind Location `
    -Label 'Go through the door that has opened' -X -6 -Y 633 -Z -1 `
    -RequiredCondition $cosmoDoorOpen `
    -EntityName 'gateway4' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-64; startY=707; startZ=-1; endX=51; endY=560; endZ=-1 })

# Bugenhagen waits at the top of the stairwell. When the leader steps on triangle 54, AD8's
# Main (game moment below 514, 3[170] bit 5 clear) has him appear (BUGEN Script 3), which
# sets 3[170] bit 5 and locks triangle 51, the way down. Init shows him, solid, only while
# that bit is set. His Talk is a bare RET; walking into him runs his Contact, which says
# "Good. Then we shall proceed.", sends him on down, clears bit 5 and unlocks triangle 51.
# So while he waits he is the room's step, walked into, and the way down comes after.
$cosmoBugenhagenWaiting = New-Condition -Bank 3 -Address 170 -Mask 32 -Value 32
$cosmoBugenhagenGoneOn = New-Condition -Bank 3 -Address 170 -Mask 32 -Value 0
Add-Definition @cosmoSealedDoorWindow -FieldId 534 -FieldName 'cosin5' -Kind Model -EntityId 5 `
    -Label 'Walk into Bugenhagen to go on down together' `
    -RequiredCondition $cosmoBugenhagenWaiting `
    -EntityName 'BUGEN' -ScriptType 'Contact' -UsesContactRange

# cosin5's own descent is seven pairs of ladders that no LINE triggers, so the route to
# this exit is handled by the traversal catalog's polled-ladder reading rather than by
# a row for each rung. It is offered while Bugenhagen is not waiting at the top.
Add-Definition @cosmoSealedDoorWindow -FieldId 534 -FieldName 'cosin5' -Kind Location -EntityId 14 `
    -Label 'Go on into the cave at the bottom' -X -23 -Y 968 -Z -2337 `
    -RequiredCondition $cosmoBugenhagenGoneOn `
    -EntityName 'LINEJ' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 14 `
    -TriggerLine ([ordered]@{ startX=-68; startY=978; startZ=-2337; endX=22; endY=959; endZ=-2337 })

# --- The Cave of the Gi -------------------------------------------------------------
# Four openings in the walls of the first chamber. Each one is a triangle the room
# polls: standing on it puts a question to the player, and answering yes does whatever
# that particular opening does. One of them clears the collision on triangle 16 and
# opens the way on; the rest are the cave's own inhabitants. Nothing here says which,
# because a sighted player standing in that room cannot tell either - the openings look
# the same until they are looked into.
$cosmoCaveOpen = New-Condition -Bank 3 -Address 182 -Mask 16 -Value 16
$cosmoCaveSealed = New-Condition -Bank 3 -Address 182 -Mask 16 -Value 0

foreach ($opening in @(
    @{ triangle=48;  bit=32;  x=-251; y=136; z=-95 },
    @{ triangle=95;  bit=128; x=814;  y=402; z=-110 },
    @{ triangle=171; bit=16;  x=-235; y=729; z=-87 },
    @{ triangle=210; bit=64;  x=173;  y=297; z=-178 })) {
    Add-Definition @cosmoSealedDoorWindow -FieldId 546 -FieldName 'gidun_1' -Kind Location `
        -Label 'Look into one of the openings in the cave wall' `
        -X $opening.x -Y $opening.y -Z $opening.z `
        -RequiredConditions @(
            $cosmoCaveSealed,
            (New-Condition -Bank 3 -Address 182 -Mask $opening.bit -Value 0)) `
        -CompletedCondition (New-Condition -Bank 3 -Address 182 -Mask $opening.bit -Value $opening.bit) `
        -CompletionPlayerTriangles @($opening.triangle) `
        -EntityName ("SWITCH{0}" -f $opening.triangle) -ScriptType 'Main' -KeepActiveOnArrival
}

Add-Definition @cosmoSealedDoorWindow -FieldId 546 -FieldName 'gidun_1' -Kind Location -EntityId 24 `
    -Label 'Take the passage that has opened' -X 236 -Y 1310 -Z 6 `
    -RequiredCondition $cosmoCaveOpen `
    -EntityName 'LINEJB' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 24 `
    -TriggerLine ([ordered]@{ startX=203; startY=1316; startZ=5; endX=269; endY=1304; endZ=7 })

Add-Definition @cosmoSealedDoorWindow -FieldId 547 -FieldName 'gidun_2' -Kind Location `
    -Label 'Take the passage on through the cave' -X -424 -Y 1896 -Z 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=-502; startY=1896; startZ=0; endX=-346; endY=1896; endZ=0 })

Add-Definition @cosmoSealedDoorWindow -FieldId 548 -FieldName 'gidun_4' -Kind Location `
    -Label 'Take the passage on through the cave' -X 118 -Y 2218 -Z 0 `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine ([ordered]@{ startX=52; startY=2218; startZ=0; endX=184; endY=2218; endZ=0 })

Add-Definition @cosmoSealedDoorWindow -FieldId 549 -FieldName 'gidun_3' -Kind Location -EntityId 15 `
    -Label 'Cross to the far side of the chamber' -X -5 -Y -338 -Z -129 -TargetGameMoment 514 `
    -EntityName 'LINEE' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 15 `
    -TriggerLine ([ordered]@{ startX=-119; startY=-338; startZ=-129; endX=109; endY=-338; endZ=-129 })

# The ways back, so no room in the cave is a dead end for a player who turns round.
foreach ($return in @(
    @{ field=546; name='gidun_1'; label='Go back up to the canyon'; x=-1301; y=577; z=-93; entity='gateway0';
       line=@{ startX=-1298; startY=542; startZ=-93; endX=-1304; endY=613; endZ=-92 }; sealed=$true },
    @{ field=549; name='gidun_3'; label='Go back the way you came'; x=-56; y=-1107; z=-128; entity='gateway0';
       line=@{ startX=-176; startY=-1107; startZ=-128; endX=64; endY=-1107; endZ=-128 }; sealed=$false })) {
    $parameters = @{
        FieldId = $return.field
        FieldName = $return.name
        Kind = 'Location'
        Label = $return.label
        X = $return.x
        Y = $return.y
        Z = $return.z
        MinimumGameMoment = 502
        MaximumGameMoment = 513
        Priority = 1
        EntityName = $return.entity
        ScriptType = 'Gateway'
        TriggerLine = $return.line
    }
    if ($return.sealed) { $parameters.RequiredCondition = $cosmoCaveSealed }
    Add-Definition @parameters
}

# --- Leaving the canyon -------------------------------------------------------------
# seto1 returns the party to the fire by itself, so the last thing left is the way out
# of the canyon, which LINEO writes 523 on.
# A different label from the one at 502, and not only for the player's sake: rows are
# deduplicated on what they say and where they point, so two rows differing in nothing
# but their moment band would collapse into one and the later band would vanish.
Add-Definition @cosmoDepartureWindow -FieldId 526 -FieldName 'cos_btm2' -Kind Location -EntityId 16 `
    -Label 'Leave the fire and head out of the canyon' -X -699 -Y -1683 -Z -2564 `
    -EntityName 'LINEFR1' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 16 `
    -TriggerLine ([ordered]@{ startX=-703; startY=-1579; startZ=-2566; endX=-695; endY=-1787; endZ=-2563 })

Add-Definition @cosmoDepartureWindow -FieldId 525 -FieldName 'cos_btm' -Kind Location -EntityId 16 `
    -Label 'Leave Cosmo Canyon' -X -1473 -Y -2181 -Z -2618 -TargetGameMoment 523 `
    -RequiredPlayerTriangles $cosmoLowerTown `
    -EntityName 'LINEO' -ScriptType 'Go 1x' -UsesPlayerCollisionRadius `
    -RequiredEnabledLineEntityId 16 `
    -TriggerLine ([ordered]@{ startX=-1404; startY=-2137; startZ=-2618; endX=-1542; endY=-2225; endZ=-2618 })

# --- Corrections to what extraction produced ----------------------------------------
# Red XIII's campfire Talk does write 502, so extraction finds it - but it finds only
# the write, not the count of three bits his own script makes first. An unconditional
# row for him would send the player to a conversation that turns into ordinary dialogue
# and advances nothing. The reviewed row above replaces it.
for ($index = $definitions.Count - 1; $index -ge 0; $index--) {
    $definition = $definitions[$index]
    if ($definition.fieldId -eq 526 -and $definition.targetGameMoment -eq 502 -and
        $definition.sourceEntityName -eq 'RED' -and $definition.minimumGameMoment -lt 0) {
        $definitions.RemoveAt($index)
    }
}

# bugin1a/AD Script 7 writes 1391 on the Huge Materia return, not on this visit. Left
# unbounded it repeats Bugenhagen's conversation as a first-visit objective, which is
# both wrong and a step out of order. 1389 is the write that precedes it, in fship_25.
foreach ($definition in $definitions) {
    if ($definition.fieldId -eq 541 -and $definition.targetGameMoment -eq 1391) {
        $definition.minimumGameMoment = 1389
        $definition.maximumGameMoment = 1390
    }
}

Add-CuratedFields 525, 526, 529, 530, 531, 532, 534, 535, 536, 540, 541, 544, 546, 547, 548, 549, 550


