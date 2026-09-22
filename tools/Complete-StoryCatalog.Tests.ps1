. "$PSScriptRoot\Complete-StoryCatalog.ps1"

Describe 'Story catalog regeneration' {
    function New-Row($name, $minimum = 523, $maximum = 534, $target = -1) {
        [pscustomobject]@{ sourceFieldName = $name; label = $name; minimumGameMoment = $minimum;
            maximumGameMoment = $maximum; targetGameMoment = $target }
    }
    $key = { param($row) "$($row.label):$($row.minimumGameMoment):$($row.maximumGameMoment):$($row.targetGameMoment)" }

    It 'keeps offline fields in the final output rather than only counting them' {
        $result = Complete-StoryCatalog -Definitions @(New-Row 'readable') `
            -PreviousDefinitions @((New-Row 'offline'), (New-Row 'obsolete-readable')) `
            -UnreadableFields @('offline') -GetKey $key
        @($result.Definitions).Count | Should -Be 2
        $result.Definitions.sourceFieldName | Should -Contain 'offline'
        $result.Definitions.sourceFieldName | Should -Not -Contain 'obsolete-readable'
        $result.CarriedForward | Should -Be 1
    }
    It 'deduplicates carried rows without merging distinct progression windows' {
        $result = Complete-StoryCatalog -Definitions @(New-Row 'offline') `
            -PreviousDefinitions @((New-Row 'offline'), (New-Row 'offline' 370 370)) `
            -UnreadableFields @('offline') -GetKey $key
        @($result.Definitions).Count | Should -Be 2
    }
    It 'reports rows the production reader can never offer' {
        $result = Complete-StoryCatalog -Definitions @((New-Row 'bad-milestone' 78 78 72),
            (New-Row 'bad-interval' 535 534), (New-Row 'valid')) -GetKey $key
        @($result.Definitions).Count | Should -Be 1
        @($result.RejectedUnreachable).Count | Should -Be 2
        $result.Definitions[0].label | Should -Be 'valid'
    }
}
