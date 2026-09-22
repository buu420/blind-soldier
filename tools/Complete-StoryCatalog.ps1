function Complete-StoryCatalog {
    param(
        [object[]] $Definitions,
        [object[]] $PreviousDefinitions,
        [string[]] $UnreadableFields,
        [scriptblock] $GetKey
    )

    $combined = [Collections.Generic.List[object]]::new()
    foreach ($row in $Definitions) { $combined.Add($row) }
    $carried = 0
    foreach ($row in $PreviousDefinitions) {
        if ($UnreadableFields -contains [string]$row.sourceFieldName) {
            $combined.Add($row)
            $carried++
        }
    }

    $seen = @{}
    $result = [Collections.Generic.List[object]]::new()
    $rejected = [Collections.Generic.List[object]]::new()
    foreach ($row in $combined) {
        # The shipping reader hides a milestone as soon as it is reached.
        # An interval starting at or after that milestone can never be offered.
        if (($row.targetGameMoment -ge 0 -and $row.minimumGameMoment -ge $row.targetGameMoment) -or
            ($row.minimumGameMoment -ge 0 -and $row.maximumGameMoment -ge 0 -and
             $row.minimumGameMoment -gt $row.maximumGameMoment)) {
            $rejected.Add($row)
            continue
        }
        $key = & $GetKey $row
        if (-not $seen.ContainsKey($key)) {
            $seen[$key] = $true
            $result.Add($row)
        }
    }
    [pscustomobject]@{
        Definitions = $result.ToArray()
        CarriedForward = $carried
        RejectedUnreachable = $rejected.ToArray()
    }
}
