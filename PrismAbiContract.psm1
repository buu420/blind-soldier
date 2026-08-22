<#
.SYNOPSIS
Guards the launcher's PrismConfig declaration against drifting from the one the
Prism DLL expects.

.DESCRIPTION
prism_config_init returns PrismConfig *by value* and prism_init reads it back, so
the managed declaration is an ABI contract rather than a convenience. Prism 0.18
grew the struct from one byte to eight fields; 0.4.1 shipped that DLL to the
launcher while leaving the one-byte declaration in place, and on x86 the native
side wrote thirty-two bytes into a one-byte stack slot. Every 0.4.1 user's
launcher access-violated on startup, with any screen reader or none, before it
had chosen a backend.

Nothing caught it. The bundle builder verified both PE machine types and the
managed assembly identity; neither has anything to say about struct layout.

Prism cannot catch it either, and it is worth being precise about why. Prism does
version its config: prism_init compares the version byte and rejects a config
newer than the library, while older ones are accepted with the missing fields
treated as absent. That mechanism protects prism_init, which merely *reads* a
caller-owned struct. It can do nothing for prism_config_init, which *returns* the
struct by value: the library writes its own sizeof into whatever slot the caller
provided, having no way to know the caller declared something smaller. By the
time prism_init could have rejected anything, the stack is already gone. So a
correct managed declaration is the only defence, which is what this module exists
to enforce.

This module recomputes the marshalled x86 layout of both declarations and refuses
to let them differ. The size is computed rather than measured because
Marshal.SizeOf reports the *host* process's layout: under x64 PowerShell the
three pointers measure eight bytes each and the struct comes out at 48, not the
32 the x86 launcher actually gets.
#>

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# The marshalled x86 size of PRISM_CONFIG_VERSION 3, which is what Prism 0.18
# defines and what the shipped FFVII_LAUNCHER.prism.x86.dll returns. Measured from
# a .NET Framework x86 probe against that DLL: 32 bytes, version byte 3.
#
# Pinning this is not belt-and-braces. Comparing the launcher against the mod only
# proves the two managed copies agree; if a future Prism changes the struct and
# both copies are brought to the same wrong shape, they still agree and the
# launcher still dies. prism_init rejects a config whose version byte does not
# match PRISM_CONFIG_VERSION - the constant moved from 2 to 3 between 0.16 and
# 0.18 - so the layout is a versioned contract and changing it should be a
# deliberate act. Whoever moves the DLL forward updates these two numbers.
$script:PrismConfigVersion = 3
$script:PrismConfigX86Size = 32

# Marshalled size and alignment on x86. Pointers are four bytes here, which is the
# whole point - this must describe the launcher's target, not PowerShell's.
$script:X86Types = @{
    'byte'    = @{ Size = 1; Align = 1 }
    'sbyte'   = @{ Size = 1; Align = 1 }
    'short'   = @{ Size = 2; Align = 2 }
    'ushort'  = @{ Size = 2; Align = 2 }
    'int'     = @{ Size = 4; Align = 4 }
    'uint'    = @{ Size = 4; Align = 4 }
    'long'    = @{ Size = 8; Align = 8 }
    'ulong'   = @{ Size = 8; Align = 8 }
    'float'   = @{ Size = 4; Align = 4 }
    'double'  = @{ Size = 8; Align = 8 }
    'nint'    = @{ Size = 4; Align = 4 }
    'nuint'   = @{ Size = 4; Align = 4 }
    'IntPtr'  = @{ Size = 4; Align = 4 }
    'UIntPtr' = @{ Size = 4; Align = 4 }
}

function Remove-CSharpComment {
    param([Parameter(Mandatory = $true)] [AllowEmptyString()] [string] $Text)

    $withoutBlocks = [regex]::Replace($Text, '/\*.*?\*/', ' ', 'Singleline')
    return [regex]::Replace($withoutBlocks, '//[^\r\n]*', ' ')
}

function Get-PrismConfigBody {
    param([Parameter(Mandatory = $true)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "PrismConfig source is missing: $Path"
    }

    $text = Remove-CSharpComment -Text ([IO.File]::ReadAllText($Path))
    $match = [regex]::Match($text, 'struct\s+PrismConfig\b')
    if (-not $match.Success) {
        throw "No PrismConfig declaration found in $Path."
    }

    $open = $text.IndexOf('{', $match.Index)
    if ($open -lt 0) {
        throw "PrismConfig declaration in $Path has no body."
    }

    $depth = 0
    for ($i = $open; $i -lt $text.Length; $i++) {
        switch ($text[$i]) {
            '{' { $depth++ }
            '}' {
                $depth--
                if ($depth -eq 0) {
                    return $text.Substring($open + 1, $i - $open - 1)
                }
            }
        }
    }

    throw "PrismConfig declaration in $Path is not closed."
}

function Get-PrismConfigLayout {
    <#
    .SYNOPSIS
    Parses a PrismConfig declaration and returns its marshalled x86 layout.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)] [string] $Path)

    $body = Get-PrismConfigBody -Path $Path

    $fields = @()
    $offset = 0
    $maxAlign = 1

    foreach ($statement in ($body -split ';')) {
        if ([string]::IsNullOrWhiteSpace($statement)) { continue }

        # The launcher must declare the trailing flag as a plain byte, but the mod
        # declares it as a bool carrying [MarshalAs(UnmanagedType.I1)]: .NET
        # Framework throws MarshalDirectiveException on a struct-return delegate
        # whose struct carries that attribute, and .NET 8 does not. Both marshal to
        # one byte, so the two spellings are the same contract.
        $isI1 = $statement -match 'UnmanagedType\s*\.\s*I1'
        $declaration = [regex]::Replace($statement, '\[[^\]]*\]', ' ')

        $field = [regex]::Match(
            $declaration,
            '(?<!\w)public\s+(?<type>[A-Za-z_][A-Za-z0-9_]*)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*$')
        if (-not $field.Success) { continue }

        $typeName = $field.Groups['type'].Value
        $fieldName = $field.Groups['name'].Value

        if ($typeName -eq 'bool') {
            if (-not $isI1) {
                # A bare bool marshals as a four-byte Win32 BOOL. Silently accepting
                # one would move every following field and produce exactly the class
                # of corruption this module exists to prevent.
                throw ("PrismConfig field '$fieldName' in $Path is a bool without " +
                    '[MarshalAs(UnmanagedType.I1)], which marshals as four bytes. ' +
                    'Declare it as a byte, or attribute it.')
            }
            $shape = @{ Size = 1; Align = 1 }
        }
        elseif ($script:X86Types.ContainsKey($typeName)) {
            $shape = $script:X86Types[$typeName]
        }
        else {
            throw ("PrismConfig field '$fieldName' in $Path has type '$typeName', " +
                'whose marshalled x86 size is unknown to PrismAbiContract.psm1. ' +
                'Add it to $script:X86Types before shipping a launcher that uses it.')
        }

        $align = [int]$shape.Align
        $size = [int]$shape.Size
        if ($align -gt $maxAlign) { $maxAlign = $align }

        $padding = ($align - ($offset % $align)) % $align
        $offset += $padding

        $fields += [pscustomobject]@{
            Name   = $fieldName
            Type   = $typeName
            Offset = $offset
            Size   = $size
            Align  = $align
        }
        $offset += $size
    }

    if ($fields.Count -eq 0) {
        throw "PrismConfig in $Path declares no public fields."
    }

    $tailPadding = ($maxAlign - ($offset % $maxAlign)) % $maxAlign

    return [pscustomobject]@{
        Path   = $Path
        Fields = $fields
        Size   = $offset + $tailPadding
    }
}

function Format-PrismConfigLayout {
    param([Parameter(Mandatory = $true)] $Layout)

    $rendered = $Layout.Fields | ForEach-Object {
        '{0}@{1}:{2}' -f $_.Name, $_.Offset, $_.Size
    }
    return ('{0} bytes over {1} field(s) [{2}]' -f
        $Layout.Size, $Layout.Fields.Count, ($rendered -join ' '))
}

function Assert-PrismConfigContract {
    <#
    .SYNOPSIS
    Throws unless the launcher's PrismConfig marshals identically to the reference
    declaration.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $LauncherSourcePath,
        [Parameter(Mandatory = $true)] [string] $ReferenceSourcePath,
        [int] $ExpectedSize = $script:PrismConfigX86Size
    )

    $launcher = Get-PrismConfigLayout -Path $LauncherSourcePath
    $reference = Get-PrismConfigLayout -Path $ReferenceSourcePath

    $complain = {
        param([string] $Detail)
        throw ("Launcher PrismConfig does not match the Prism ABI contract. $Detail`n" +
            "  launcher  : $LauncherSourcePath`n" +
            '              ' + (Format-PrismConfigLayout -Layout $launcher) + "`n" +
            "  reference : $ReferenceSourcePath`n" +
            '              ' + (Format-PrismConfigLayout -Layout $reference) + "`n" +
            'prism_config_init returns this struct by value, so a mismatch corrupts ' +
            'the stack and the launcher dies on startup for every user. Bringing the ' +
            'Prism DLL forward without this declaration is what broke 0.4.1.')
    }

    if ($launcher.Fields.Count -ne $reference.Fields.Count) {
        & $complain ("Field count is $($launcher.Fields.Count), expected $($reference.Fields.Count).")
    }

    for ($i = 0; $i -lt $reference.Fields.Count; $i++) {
        $actual = $launcher.Fields[$i]
        $expected = $reference.Fields[$i]

        if ($actual.Name -ne $expected.Name) {
            & $complain ("Field $i is '$($actual.Name)', expected '$($expected.Name)'.")
        }
        if ($actual.Offset -ne $expected.Offset -or $actual.Size -ne $expected.Size) {
            & $complain ("Field '$($actual.Name)' marshals at offset $($actual.Offset) " +
                "size $($actual.Size), expected offset $($expected.Offset) size $($expected.Size).")
        }
    }

    # No launcher-versus-reference size comparison here on purpose: two declarations
    # whose fields all match by name, offset and size necessarily marshal to the same
    # total, so such a check can never fire. Mutation testing confirmed it - deleting
    # it left every test green. An unreachable line in a guard implies coverage that
    # does not exist.
    #
    # Both copies agreeing is not the same as either being right. This is the check
    # that catches them drifting together.
    if ($launcher.Size -ne $ExpectedSize) {
        & $complain ("Both declarations marshal to $($launcher.Size) bytes, but Prism " +
            "config version $script:PrismConfigVersion is $ExpectedSize bytes on x86. " +
            'If the bundled Prism DLL changed, update $script:PrismConfigVersion and ' +
            '$script:PrismConfigX86Size in PrismAbiContract.psm1 to match the new ' +
            'header, and re-measure against the DLL rather than assuming.')
    }

    return $launcher
}

Export-ModuleMember -Function Get-PrismConfigLayout, Assert-PrismConfigContract
