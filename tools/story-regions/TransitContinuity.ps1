# Dot-sourced by Generate-FieldStoryEvents.ps1 last. It adds nothing to the catalog.
#
# This was a shipping pass in the first draft and should not have been. It walks the
# engine's door graph backwards from each field that writes a GameMoment and proposes,
# for every other room in the chapter, the door that shortens the walk. That is a
# useful way to find rooms nobody has looked at yet. It is not a route, and independent
# review was right to reject it as one:
#
#  - Activation was inferred from the numerically previous global GameMoment write.
#    FFVII's GameMoment is not a sequential task list. The previous numeric value can
#    belong to a different visit, a different continent or a debug room, so the band a
#    room was given was frequently not the band it is actually walked in. Nothing in
#    the static data fixes this; only reviewing what the chapter's own scripts gate on
#    does.
#  - A door was assumed to be a door. jail1, jail3 and jail4 each call MPJPO in their
#    Director Init and disable every static gateway they have, and this pass would have
#    routed a player to all three.
#  - Bands for one door across two visits were merged into one interval, so a doorway
#    useful on a first and a third visit was offered throughout the second.
#  - A row with no band was read as covering every moment forever, and any overlap with
#    an existing row suppressed a whole new range. Both hide real gaps.
#  - Labels were chapter titles from the metadata - "Prison, Bloodbath", "Sephiroth
#    goes ballistic" - which tell a player nothing about what to do now and give away
#    story they have not reached.
#
# So it now writes a candidate report for curation instead of definitions. Rooms that
# appear in it have no reviewed route yet; that is the point of the file.

if (-not $LedgerPath) {
    return
}

$transitSkippedChapters = @('Remaining / To Be Categorized', 'Debug', 'Missing')

function Get-TransitFirstHops {
    param(
        [string] $Destination,
        [string[]] $Allowed,
        [int] $MaximumDoors
    )

    $distance = @{ $Destination = 0 }
    $frontier = [Collections.Generic.Queue[string]]::new()
    $frontier.Enqueue($Destination)
    while ($frontier.Count -gt 0) {
        $current = $frontier.Dequeue()
        if ($distance[$current] -ge $MaximumDoors) {
            continue
        }
        foreach ($name in $Allowed) {
            if ($distance.ContainsKey($name) -or -not $gatewaysByField.ContainsKey($name)) {
                continue
            }
            foreach ($door in $gatewaysByField[$name]) {
                if ($door.ToField -eq $current) {
                    $distance[$name] = $distance[$current] + 1
                    $frontier.Enqueue($name)
                    break
                }
            }
        }
    }

    $hops = @{}
    foreach ($name in $Allowed) {
        if ($name -eq $Destination -or -not $distance.ContainsKey($name) -or -not $gatewaysByField.ContainsKey($name)) {
            continue
        }
        $best = [Collections.Generic.List[object]]::new()
        foreach ($door in $gatewaysByField[$name]) {
            if ($distance.ContainsKey($door.ToField) -and $distance[$door.ToField] -eq $distance[$name] - 1) {
                $best.Add($door)
            }
        }
        if ($best.Count -gt 0) {
            $hops[$name] = $best
        }
    }
    return $hops
}

# Which fields already have a reviewed or extracted row, so the report is a list of
# what is left rather than a list of everything.
$transitAnswered = @{}
foreach ($definition in $definitions) {
    $transitAnswered[[string]$definition.sourceFieldName] = $true
}

# A field a region has claimed counts as reviewed even when it carries no row for a
# given moment: the region looked at it and decided it is not a step there, and the
# report should not keep proposing it as unreviewed ground.
foreach ($curatedId in $curatedFields) {
    if ($idNames.ContainsKey($curatedId)) {
        $transitAnswered[[string]$idNames[$curatedId]] = $true
    }
}

$transitCandidates = [Collections.Generic.List[object]]::new()
foreach ($chapter in $chapters) {
    $chapterName = [string]$chapter.name
    if ($transitSkippedChapters -contains $chapterName) {
        continue
    }

    $chapterFields = @($chapter.fieldNames | ForEach-Object { [string]$_ } | Where-Object { $fieldIds.ContainsKey($_) })
    if ($chapterFields.Count -lt 2) {
        continue
    }

    $milestoneFields = [Collections.Generic.List[object]]::new()
    foreach ($fieldName in $chapterFields) {
        if (-not $momentWritesByField.ContainsKey($fieldName)) {
            continue
        }
        foreach ($value in $momentWritesByField[$fieldName]) {
            $milestoneFields.Add([pscustomobject]@{ Value = $value; Field = $fieldName })
        }
    }

    foreach ($milestone in @($milestoneFields | Sort-Object Value)) {
        $hops = Get-TransitFirstHops -Destination $milestone.Field -Allowed $chapterFields -MaximumDoors 8
        foreach ($fieldName in $chapterFields) {
            if (-not $hops.ContainsKey($fieldName) -or $transitAnswered.ContainsKey($fieldName)) {
                continue
            }
            foreach ($door in $hops[$fieldName]) {
                $transitCandidates.Add([ordered]@{
                    chapter = $chapterName
                    field = $fieldName
                    fieldId = $fieldIds[$fieldName]
                    door = $door.EntityName
                    doorScript = $door.ScriptType
                    doorEntityId = $door.EntityId
                    leadsTo = $door.ToField
                    towardWrite = $milestone.Value
                    towardField = $milestone.Field
                    note = 'candidate only: activation, native line state and destination entry all still need review'
                })
            }
        }
    }
}

$transitReportPath = Join-Path (Split-Path -Parent $LedgerPath) 'transit-candidates.json'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $transitReportPath) | Out-Null
[ordered]@{
    generated = 'candidate doors for rooms with no reviewed or extracted Story row'
    warning = 'These are not routes. Nothing here is in the shipped catalog.'
    candidateCount = $transitCandidates.Count
    fields = @($transitCandidates | ForEach-Object { $_.field } | Sort-Object -Unique).Count
    candidates = $transitCandidates
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $transitReportPath -Encoding UTF8

Write-Host ("Transit candidates (audit only, not shipped): {0} doors across {1} unreviewed rooms -> {2}" -f
    $transitCandidates.Count,
    (@($transitCandidates | ForEach-Object { $_.field } | Sort-Object -Unique)).Count,
    $transitReportPath)
