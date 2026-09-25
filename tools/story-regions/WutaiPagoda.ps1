# Dot-sourced by Generate-FieldStoryEvents.ps1 after the reviewed regions.
#
# Godo's Pagoda (586 tower5), Yuffie's five fights. All five floors are the one field:
# 15[138] is the floor the party is on (0 to 4), and 15[139] bits 0 to 4 are the five
# fights won - Gorky, Shake, Chekhov, Staniv and Godo. Before this nothing in the Story
# catalog covered the field, and its stairs are map jumps back into the same field, which
# the script catalog deliberately never publishes as exits - so a player who had won a
# floor was offered no way up, and a player on an upper floor no way down.
#
# What the installed scripts do:
#
#   jump_u, entity 14, LINE (82,547,116)-(125,426,83): its Move adds one to 15[138] and
#        map jumps into 586 at (-215,312), triangle 15 - the stairs up. It stands on
#        triangle 37, which is reached only over triangle 29.
#   jump_d, entity 15, LINE (-137,430,-18)-(-118,544,-18): its Move takes one off 15[138]
#        and map jumps to (275,277), triangle 22 - the stairs down.
#   init, entity 4, Main: on floor k it unlocks triangle 29 only while 15[139] bit k is
#        set, and locks it on the top floor. The ground floor has triangle 16 (the door
#        out to the courtyard, gateway0 to 587) open and 27 (the stairs down) locked; every
#        upper floor has them the other way round.
#   goriki 17, shake 18, tiehofu 19, sutanif 20 and godoh 21 are the five opponents, one
#        per floor. Each one's Talk asks its question and fights Yuffie alone only while
#        she is in the party and its own bit is clear (IFPRTYQ 5), and a win sets that
#        bit: 623, 624, 625 and 626 for the first four, 628 for Godo, whose win sets bit 4
#        and puts the party back on the floor below. Without Yuffie each of them turns the
#        party away, and the ground floor's own exit line sends it out.
#
# So on each floor the step is the floor's own opponent until the bit is set, then the
# stairs up. The stairs down and the courtyard door are always the way back. It is an
# optional side quest, so every row says so. There is no save point in 586 or in the
# courtyard (587); the nearest is Wutai's main street (579), already an Object. Nothing
# here claims one.
$pagodaYuffie = New-PartyMemberCondition -CharacterId 5 -Present
$pagodaGodoUnbeaten = New-Condition -Bank 15 -Address 139 -Mask 0x10 -Value 0x00
$pagodaStep = @{ Priority = 1 }
$pagodaWayOut = @{ Priority = 50 }
# Each floor's stairs row names the floor it leads to. That is what a sighted player is
# told ("you cannot go on to the second floor"), and it makes each floor's row its own
# target: the stairs' Move script changes 15[138] before it reloads the field, so the row
# being walked to is gone once it is crossed and an automatic walk cannot carry on up the
# next floor's stairs after the reload.
$pagodaFloorNames = @('first', 'second', 'third', 'fourth', 'fifth')
$pagodaStairsUp = [ordered]@{ startX = 82; startY = 547; startZ = 116; endX = 125; endY = 426; endZ = 83 }
$pagodaStairsDown = [ordered]@{ startX = -137; startY = 430; startZ = -18; endX = -118; endY = 544; endZ = -18 }
$pagodaCourtyardDoor = [ordered]@{ startX = -94; startY = -401; startZ = 20; endX = 101; endY = -399; endZ = 20 }

foreach ($pagodaFloor in @(
    @{ floor = 0; entity = 17; name = 'goriki'; label = 'Talk to Gorky (optional)' },
    @{ floor = 1; entity = 18; name = 'shake'; label = 'Talk to Shake (optional)' },
    @{ floor = 2; entity = 19; name = 'tiehofu'; label = 'Talk to Chekhov (optional)' },
    @{ floor = 3; entity = 20; name = 'sutanif'; label = 'Talk to Staniv (optional)' },
    @{ floor = 4; entity = 21; name = 'godoh'; label = 'Talk to Godo (optional)' })) {
    $onFloor = New-Condition -Bank 15 -Address 138 -Mask 0xFF -Value $pagodaFloor.floor
    $bit = 1 -shl $pagodaFloor.floor
    $unbeaten = New-Condition -Bank 15 -Address 139 -Mask $bit -Value 0
    $beaten = New-Condition -Bank 15 -Address 139 -Mask $bit -Value $bit

    Add-Definition @pagodaStep -FieldId 586 -FieldName 'tower5' -Kind Model -EntityId $pagodaFloor.entity `
        -Label $pagodaFloor.label `
        -RequiredConditions @($onFloor, $unbeaten, $pagodaYuffie) `
        -CompletedCondition $beaten `
        -EntityName $pagodaFloor.name -ScriptType 'Talk'

    if ($pagodaFloor.floor -lt 4) {
        Add-Definition @pagodaStep -FieldId 586 -FieldName 'tower5' -Kind Location -EntityId 14 `
            -Label "Climb the stairs to the $($pagodaFloorNames[$pagodaFloor.floor + 1]) floor (optional)" -X 103 -Y 486 -Z 99 `
            -RequiredConditions @($onFloor, $beaten, $pagodaGodoUnbeaten) `
            -EntityName 'jump_u' -ScriptType 'Move' -RequiredEnabledLineEntityId 14 `
            -TriggerLine $pagodaStairsUp
    }

    if ($pagodaFloor.floor -gt 0) {
        Add-Definition @pagodaStep -FieldId 586 -FieldName 'tower5' -Kind Location -EntityId 15 `
            -Label "Go back down to the $($pagodaFloorNames[$pagodaFloor.floor - 1]) floor (optional)" -X -127 -Y 487 -Z -18 `
            -RequiredConditions @($onFloor) `
            -EntityName 'jump_d' -ScriptType 'Move' -RequiredEnabledLineEntityId 15 `
            -TriggerLine $pagodaStairsDown
    }
}

$pagodaGroundFloor = New-Condition -Bank 15 -Address 138 -Mask 0xFF -Value 0
Add-Definition @pagodaWayOut -FieldId 586 -FieldName 'tower5' -Kind Location `
    -Label 'Leave the Pagoda (optional)' -X 3 -Y -400 -Z 20 `
    -RequiredConditions @($pagodaGroundFloor) `
    -EntityName 'gateway0' -ScriptType 'Gateway' `
    -TriggerLine $pagodaCourtyardDoor

Add-CuratedFields 586
