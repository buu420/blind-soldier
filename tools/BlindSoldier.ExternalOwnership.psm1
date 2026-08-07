Set-StrictMode -Version Latest

function Assert-OwnershipPolicyName {
    param(
        [Parameter(Mandatory=$true)]
        [AllowEmptyString()]
        [string] $Value,
        [Parameter(Mandatory=$true)]
        [string] $Label,
        [switch] $AllowEmpty
    )
    if ([string]::IsNullOrEmpty($Value)) {
        if ($AllowEmpty) { return }
        throw "$Label cannot be empty."
    }
    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "$Label cannot contain only whitespace."
    }
    $normalized = $Value.Replace('\','/')
    if ($normalized.StartsWith('/') -or $normalized.EndsWith('/') -or
        $normalized.Contains('//') -or $normalized.Contains(':')) {
        throw "$Label is not a normalized relative path: $Value"
    }
    foreach ($component in $normalized.Split('/')) {
        if ([string]::IsNullOrWhiteSpace($component) -or
            $component -ceq '.' -or $component -ceq '..' -or
            $component.EndsWith(' ') -or $component.EndsWith('.')) {
            throw "$Label contains an unsafe path component: $Value"
        }
    }
    return $normalized
}

function Assert-UniqueOwnershipNames {
    param(
        [Parameter(Mandatory=$true)]
        [AllowEmptyString()]
        [string[]] $Values,
        [Parameter(Mandatory=$true)] [string] $Label
    )
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' `
        ([StringComparer]::OrdinalIgnoreCase)
    foreach ($value in $Values) {
        if (-not $seen.Add([string]$value)) {
            throw "$Label contains a case-insensitive duplicate: $value"
        }
    }
}

function Get-RequiredPolicyArray {
    param([object] $RawPolicy, [string] $Name)
    $property = $RawPolicy.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        throw "External ownership policy is missing $Name."
    }
    return @($property.Value)
}

function Import-BlindSoldierExternalOwnershipPolicy {
    [CmdletBinding()]
    param(
        [string] $Path = (Join-Path $PSScriptRoot `
            'BlindSoldier.ExternalOwnership.json')
    )
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "External ownership policy is missing: $fullPath"
    }
    $raw = [IO.File]::ReadAllText($fullPath) | ConvertFrom-Json
    if ([int]$raw.schemaVersion -ne 1) {
        throw 'External ownership policy schemaVersion must be 1.'
    }
    if ([string]::IsNullOrWhiteSpace([string]$raw.policyVersion) -or
        [string]::IsNullOrWhiteSpace([string]$raw.sourceAsset) -or
        [string]$raw.sourceSha256 -notmatch '^[0-9A-Fa-f]{64}$') {
        throw 'External ownership policy release identity is invalid.'
    }

    $roots = New-Object 'System.Collections.Generic.List[string]'
    foreach ($entry in @(Get-RequiredPolicyArray -RawPolicy $raw `
            -Name 'deploymentRoots')) {
        $text = [string]$entry
        if ([string]::IsNullOrEmpty($text)) {
            $roots.Add('')
        }
        else {
            $roots.Add((Assert-OwnershipPolicyName -Value $text `
                -Label 'deploymentRoots entry'))
        }
    }
    $files = New-Object 'System.Collections.Generic.List[string]'
    foreach ($entry in @(Get-RequiredPolicyArray -RawPolicy $raw `
            -Name 'ownedFiles')) {
        $normalized = Assert-OwnershipPolicyName -Value ([string]$entry) `
            -Label 'ownedFiles entry'
        if ($normalized.Contains('/')) {
            throw "ownedFiles entries must be file names: $entry"
        }
        $files.Add($normalized)
    }
    $directories = New-Object 'System.Collections.Generic.List[string]'
    foreach ($entry in @(Get-RequiredPolicyArray -RawPolicy $raw `
            -Name 'ownedDirectoryPrefixes')) {
        $normalized = Assert-OwnershipPolicyName -Value ([string]$entry) `
            -Label 'ownedDirectoryPrefixes entry'
        $directories.Add($normalized)
    }
    $globalNames = New-Object 'System.Collections.Generic.List[string]'
    foreach ($entry in @(Get-RequiredPolicyArray -RawPolicy $raw `
            -Name 'globalFileNames')) {
        $normalized = Assert-OwnershipPolicyName -Value ([string]$entry) `
            -Label 'globalFileNames entry'
        if ($normalized.Contains('/')) {
            throw "globalFileNames entries must be file names: $entry"
        }
        $globalNames.Add($normalized)
    }
    if ($roots.Count -eq 0 -or $files.Count -eq 0 -or
        $directories.Count -eq 0 -or $globalNames.Count -eq 0) {
        throw 'External ownership policy arrays cannot be empty.'
    }
    Assert-UniqueOwnershipNames -Values $roots.ToArray() `
        -Label 'deploymentRoots'
    Assert-UniqueOwnershipNames -Values $files.ToArray() `
        -Label 'ownedFiles'
    Assert-UniqueOwnershipNames -Values $directories.ToArray() `
        -Label 'ownedDirectoryPrefixes'
    Assert-UniqueOwnershipNames -Values $globalNames.ToArray() `
        -Label 'globalFileNames'

    return [pscustomobject][ordered]@{
        SchemaVersion = 1
        PolicyVersion = [string]$raw.policyVersion
        SourceAsset = [string]$raw.sourceAsset
        SourceSha256 = ([string]$raw.sourceSha256).ToUpperInvariant()
        DeploymentRoots = $roots.ToArray()
        OwnedFiles = $files.ToArray()
        OwnedDirectoryPrefixes = $directories.ToArray()
        GlobalFileNames = $globalNames.ToArray()
    }
}

function Join-OwnershipRelativePath {
    param([string] $Root, [string] $Child)
    if ([string]::IsNullOrEmpty($Root)) { return $Child }
    return ($Root.TrimEnd('/') + '/' + $Child.TrimStart('/'))
}

function Test-BlindSoldierExternalOwnedPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)] [object] $Policy,
        [Parameter(Mandatory=$true)] [string] $RelativePath
    )
    $normalized = $RelativePath.Replace('\','/').Trim('/')
    if ([string]::IsNullOrWhiteSpace($normalized)) { return $false }
    $parts = $normalized.Split('/')
    $leaf = $parts[$parts.Length - 1]
    if (@($Policy.GlobalFileNames | Where-Object {
            [string]$_ -ieq $leaf }).Count -gt 0) {
        return $true
    }
    foreach ($root in @($Policy.DeploymentRoots)) {
        $rootText = [string]$root
        $local = $null
        if ([string]::IsNullOrEmpty($rootText)) {
            $local = $normalized
        }
        elseif ($normalized.StartsWith($rootText + '/',
                [StringComparison]::OrdinalIgnoreCase)) {
            $local = $normalized.Substring($rootText.Length + 1)
        }
        else { continue }
        if (@($Policy.OwnedFiles | Where-Object {
                [string]$_ -ieq $local }).Count -gt 0) {
            return $true
        }
        foreach ($prefix in @($Policy.OwnedDirectoryPrefixes)) {
            $prefixText = [string]$prefix
            if ($local.Equals($prefixText,
                    [StringComparison]::OrdinalIgnoreCase) -or
                $local.StartsWith($prefixText + '/',
                    [StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
    }
    return $false
}

function Get-BlindSoldierExternalOwnedFilePaths {
    [CmdletBinding()]
    param([Parameter(Mandatory=$true)] [object] $Policy)
    $paths = New-Object 'System.Collections.Generic.HashSet[string]' `
        ([StringComparer]::OrdinalIgnoreCase)
    foreach ($root in @($Policy.DeploymentRoots)) {
        foreach ($file in @($Policy.OwnedFiles)) {
            [void]$paths.Add((Join-OwnershipRelativePath `
                -Root ([string]$root) -Child ([string]$file)))
        }
        foreach ($file in @($Policy.GlobalFileNames)) {
            [void]$paths.Add((Join-OwnershipRelativePath `
                -Root ([string]$root) -Child ([string]$file)))
        }
    }
    return @($paths | Sort-Object)
}

function Get-BlindSoldierExternalOwnedDirectoryPaths {
    [CmdletBinding()]
    param([Parameter(Mandatory=$true)] [object] $Policy)
    $paths = New-Object 'System.Collections.Generic.HashSet[string]' `
        ([StringComparer]::OrdinalIgnoreCase)
    foreach ($root in @($Policy.DeploymentRoots)) {
        foreach ($directory in @($Policy.OwnedDirectoryPrefixes)) {
            [void]$paths.Add((Join-OwnershipRelativePath `
                -Root ([string]$root) -Child ([string]$directory)))
        }
    }
    return @($paths | Sort-Object)
}

Export-ModuleMember -Function @(
    'Import-BlindSoldierExternalOwnershipPolicy',
    'Test-BlindSoldierExternalOwnedPath',
    'Get-BlindSoldierExternalOwnedFilePaths',
    'Get-BlindSoldierExternalOwnedDirectoryPaths'
)