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
Describe 'Midgar model Story generator consistency' {
    It 'regenerates the reviewed chest, placement and return targets exactly' {
        $sourceRoot = Split-Path -Parent $PSScriptRoot
        $generator = Join-Path $PSScriptRoot 'Generate-FieldStoryEvents.ps1'
        $tokens = $null
        $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile($generator, [ref]$tokens, [ref]$errors)
        @($errors).Count | Should Be 0
        foreach ($function in $ast.FindAll({ param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -in @('New-Condition','ConvertTo-OrderedShape','Add-Definition')
        }, $false)) {
            . ([scriptblock]::Create($function.Extent.Text))
        }
        $definitions = New-Object 'Collections.Generic.List[object]'
        $commands = @($ast.FindAll({ param($node)
            $node -is [Management.Automation.Language.CommandAst] -and
            $node.Extent.Text -match '^Add-Definition -FieldId (248|249) '
        }, $false))
        foreach ($command in $commands) { & ([scriptblock]::Create($command.Extent.Text)) }
        $catalog = Get-Content -LiteralPath (Join-Path $sourceRoot `
            'Ff7.Accessibility.Reloaded\Assets\navigation\field_story_events.json') -Raw | ConvertFrom-Json
        $expected = @($catalog.definitions | Where-Object { $_.fieldId -in 248,249 })
        $definitions.Count | Should Be $expected.Count
        function ConvertTo-CanonicalShape([object]$value) {
            if ($null -eq $value) { return $null }
            if ($value -is [Collections.IDictionary]) {
                $result = [ordered]@{}
                foreach ($key in @($value.Keys | Sort-Object)) { $result[$key] = ConvertTo-CanonicalShape $value[$key] }
                return $result
            }
            if ($value -is [pscustomobject]) {
                $result = [ordered]@{}
                foreach ($key in @($value.PSObject.Properties.Name | Sort-Object)) { $result[$key] = ConvertTo-CanonicalShape $value.$key }
                return $result
            }
            if ($value -is [Collections.IEnumerable] -and $value -isnot [string]) {
                return ,@($value | ForEach-Object { ConvertTo-CanonicalShape $_ })
            }
            return $value
        }
        foreach ($row in $expected) {
            $actual = @($definitions | Where-Object { $_.fieldId -eq $row.fieldId -and $_.label -ceq $row.label })
            $actual.Count | Should Be 1
            (ConvertTo-CanonicalShape $actual[0] | ConvertTo-Json -Depth 20 -Compress) |
                Should Be (ConvertTo-CanonicalShape $row | ConvertTo-Json -Depth 20 -Compress)
        }
    }
}
