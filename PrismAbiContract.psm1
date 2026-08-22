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
version its config, but leniently: prism_init rejects a version of zero, or one
newer than the library, while *older* versions are accepted with the fields the
caller lacks treated as absent. So even a strict version check would not have
rejected the one-byte struct. And in any case that check lives in prism_init,
which merely reads a caller-owned struct. It can do nothing for
prism_config_init, which returns one by value: the library writes its own sizeof
into whatever slot the caller provided, having no way to know the caller declared
something smaller. By the time prism_init could reject anything, the stack is
already gone. A correct managed declaration is the only defence.

The layout is computed rather than measured because Marshal.SizeOf reports the
*host* process's layout: under x64 PowerShell the three pointers measure eight
bytes each and the struct comes out at 48, not the 32 the x86 launcher gets.
Both architectures are checked, because the same file is compiled into the x64
runtime as well - see the Compile Include in Ff7.Accessibility.Steam2026X64.csproj.
#>

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# The marshalled sizes of PRISM_CONFIG_VERSION 3, which is what Prism 0.18 defines
# and what the shipped FFVII_LAUNCHER.prism.x86.dll returns. The x86 figure was
# measured against that DLL from a .NET Framework x86 probe: 32 bytes, version 3.
#
# Pinning these is not belt-and-braces. Comparing the launcher against the mod only
# proves the two managed copies agree; if a future Prism changes the struct and both
# copies are brought to the same wrong shape, they still agree and the launcher
# still dies. Prism's documentation undertakes to increment this constant whenever a
# field is added or removed - it went 1, then 2 when the struct was cut back to the
# version byte alone, then 3 when the registry and availability fields arrived - so
# changing the layout should be a deliberate act. Whoever moves the DLL forward
# updates these three numbers, and re-measures rather than assuming.
$script:PrismConfigVersion = 3
$script:PrismConfigX86Size = 32
$script:PrismConfigX64Size = 48

# Marshalled size and alignment by native type. Pointer-sized entries are resolved
# per architecture, which is the whole point - this must describe the targets the
# struct is compiled for, not PowerShell's.
$script:FixedTypes = @{
    'byte'   = @{ Size = 1; Align = 1; Native = 'byte' }
    'sbyte'  = @{ Size = 1; Align = 1; Native = 'sbyte' }
    'short'  = @{ Size = 2; Align = 2; Native = 'short' }
    'ushort' = @{ Size = 2; Align = 2; Native = 'ushort' }
    'int'    = @{ Size = 4; Align = 4; Native = 'int' }
    'uint'   = @{ Size = 4; Align = 4; Native = 'uint' }
    'long'   = @{ Size = 8; Align = 8; Native = 'long' }
    'ulong'  = @{ Size = 8; Align = 8; Native = 'ulong' }
    'float'  = @{ Size = 4; Align = 4; Native = 'float' }
    'double' = @{ Size = 8; Align = 8; Native = 'double' }
}

# nint and IntPtr are the same underlying native-sized type; so are nuint and
# UIntPtr. The launcher spells them IntPtr and the mod spells them nint.
$script:PointerTypes = @{
    'nint'    = 'pointer'
    'IntPtr'  = 'pointer'
    'nuint'   = 'upointer'
    'UIntPtr' = 'upointer'
}

function Remove-CSharpComment {
    param([Parameter(Mandatory = $true)] [AllowEmptyString()] [string] $Text)

    $withoutBlocks = [regex]::Replace($Text, '/\*.*?\*/', ' ', 'Singleline')
    return [regex]::Replace($withoutBlocks, '//[^\r\n]*', ' ')
}

function Get-PrismConfigDeclaration {
    param([Parameter(Mandatory = $true)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "PrismConfig source is missing: $Path"
    }

    $text = Remove-CSharpComment -Text ([IO.File]::ReadAllText($Path))
    $match = [regex]::Match($text, 'struct\s+PrismConfig\b')
    if (-not $match.Success) {
        throw "No PrismConfig declaration found in $Path."
    }

    # Everything between the previous closing brace/semicolon and the struct keyword
    # is the attribute list. The size computation below assumes sequential layout
    # and natural alignment, so anything that changes either has to be refused.
    $searchFrom = [Math]::Max(0, $match.Index - 400)
    $preamble = $text.Substring($searchFrom, $match.Index - $searchFrom)
    $lastBreak = [Math]::Max($preamble.LastIndexOf('}'), $preamble.LastIndexOf(';'))
    if ($lastBreak -ge 0) {
        $preamble = $preamble.Substring($lastBreak + 1)
    }

    if ($preamble -notmatch 'LayoutKind\s*\.\s*Sequential') {
        throw ("PrismConfig in $Path is not declared [StructLayout(LayoutKind.Sequential)]. " +
            'This module computes offsets assuming sequential layout and natural ' +
            'alignment; any other layout makes every offset it reports meaningless.')
    }
    if ($preamble -match '\bPack\s*=') {
        throw ("PrismConfig in $Path sets StructLayout Pack, which overrides the natural " +
            'alignment this module assumes. Remove it, or teach PrismAbiContract.psm1 ' +
            'about the packing before shipping a launcher that uses it.')
    }
    if ($preamble -match '\bSize\s*=') {
        throw ("PrismConfig in $Path sets an explicit StructLayout Size, which this " +
            'module does not model.')
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
    Parses a PrismConfig declaration and returns its marshalled layout.

    .PARAMETER PointerSize
    Bytes per native pointer: 4 for x86, 8 for x64. Both matter - the launcher is
    x86, and the same declaration is compiled into the x64 mod runtime.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $Path,
        [ValidateSet(4, 8)] [int] $PointerSize = 4
    )

    $body = Get-PrismConfigDeclaration -Path $Path

    $fields = @()
    $offset = 0
    $maxAlign = 1

    foreach ($statement in ($body -split ';')) {
        # The launcher must declare the trailing flag as a plain byte, but the mod
        # declares it as a bool carrying [MarshalAs(UnmanagedType.I1)]: .NET
        # Framework throws MarshalDirectiveException on a struct-return delegate
        # whose struct carries that attribute, and .NET 8 does not. I1 is the
        # one-byte C Boolean, so the two spellings are the same contract. This
        # normalization is deliberately limited to that pair - a bool without the
        # attribute is refused below.
        $isI1 = $statement -match 'UnmanagedType\s*\.\s*I1'
        $declaration = [regex]::Replace($statement, '\[[^\]]*\]', ' ')

        if ([string]::IsNullOrWhiteSpace($declaration)) { continue }

        $field = [regex]::Match(
            $declaration,
            '(?<!\w)public\s+(?<type>[A-Za-z_][A-Za-z0-9_]*)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*$')
        if (-not $field.Success) {
            # Fail closed. A private instance field, or anything else this parser
            # does not recognise, still occupies space and would move every field
            # after it. Guessing here would reintroduce exactly the silent
            # corruption this module exists to prevent.
            throw ("PrismConfig in $Path contains a declaration PrismAbiContract.psm1 " +
                "cannot account for: '" + ($declaration.Trim() -replace '\s+', ' ') +
                "'. Every member affects the layout, so the parser refuses to guess.")
        }

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
            $shape = @{ Size = 1; Align = 1; Native = 'byte' }
        }
        elseif ($script:PointerTypes.ContainsKey($typeName)) {
            $shape = @{
                Size = $PointerSize
                Align = $PointerSize
                Native = $script:PointerTypes[$typeName]
            }
        }
        elseif ($script:FixedTypes.ContainsKey($typeName)) {
            $shape = $script:FixedTypes[$typeName]
        }
        else {
            throw ("PrismConfig field '$fieldName' in $Path has type '$typeName', " +
                'whose marshalled size is unknown to PrismAbiContract.psm1. Add it ' +
                'to $script:FixedTypes before shipping a launcher that uses it.')
        }

        $align = [int]$shape.Align
        $size = [int]$shape.Size
        if ($align -gt $maxAlign) { $maxAlign = $align }

        $padding = ($align - ($offset % $align)) % $align
        $offset += $padding

        $fields += [pscustomobject]@{
            Name   = $fieldName
            Type   = $typeName
            Native = [string]$shape.Native
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
        Path        = $Path
        PointerSize = $PointerSize
        Fields      = $fields
        Size        = $offset + $tailPadding
    }
}

function Format-PrismConfigLayout {
    param([Parameter(Mandatory = $true)] $Layout)

    $rendered = $Layout.Fields | ForEach-Object {
        '{0}:{1}@{2}:{3}' -f $_.Name, $_.Native, $_.Offset, $_.Size
    }
    return ('{0} bytes over {1} field(s) [{2}]' -f
        $Layout.Size, $Layout.Fields.Count, ($rendered -join ' '))
}

function Assert-PrismConfigContract {
    <#
    .SYNOPSIS
    Throws unless the launcher's PrismConfig marshals identically to the reference
    declaration, on both x86 and x64, at the pinned Prism sizes.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $LauncherSourcePath,
        [Parameter(Mandatory = $true)] [string] $ReferenceSourcePath,
        [int] $ExpectedX86Size = $script:PrismConfigX86Size,
        [int] $ExpectedX64Size = $script:PrismConfigX64Size
    )

    $results = @()

    foreach ($arch in @(
        @{ PointerSize = 4; Label = 'x86'; Expected = $ExpectedX86Size },
        @{ PointerSize = 8; Label = 'x64'; Expected = $ExpectedX64Size })) {

        $launcher = Get-PrismConfigLayout -Path $LauncherSourcePath -PointerSize $arch.PointerSize
        $reference = Get-PrismConfigLayout -Path $ReferenceSourcePath -PointerSize $arch.PointerSize

        $complain = {
            param([string] $Detail)
            throw ("Launcher PrismConfig does not match the Prism ABI contract on " +
                "$($arch.Label). $Detail`n" +
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
            # Native type, not just size: uint becoming int keeps the size and changes
            # the meaning, and a pointer becoming uint keeps the size on x86 while
            # shifting every later field on x64.
            if ($actual.Native -ne $expected.Native) {
                & $complain ("Field '$($actual.Name)' is $($actual.Native), expected $($expected.Native).")
            }
            if ($actual.Offset -ne $expected.Offset -or $actual.Size -ne $expected.Size) {
                & $complain ("Field '$($actual.Name)' marshals at offset $($actual.Offset) " +
                    "size $($actual.Size), expected offset $($expected.Offset) size $($expected.Size).")
            }
        }

        # No launcher-versus-reference size comparison here on purpose: two
        # declarations whose fields all match by name, native type, offset and size
        # necessarily marshal to the same total, so such a check can never fire.
        # Mutation testing confirmed it - deleting it left every test green. An
        # unreachable line in a guard implies coverage that does not exist.
        #
        # Both copies agreeing is not the same as either being right. This is the
        # check that catches them drifting together.
        if ($launcher.Size -ne $arch.Expected) {
            & $complain ("Both declarations marshal to $($launcher.Size) bytes, but Prism " +
                "config version $script:PrismConfigVersion is $($arch.Expected) bytes on " +
                "$($arch.Label). If the bundled Prism DLL changed, update " +
                '$script:PrismConfigVersion, $script:PrismConfigX86Size and ' +
                '$script:PrismConfigX64Size in PrismAbiContract.psm1 to match the new ' +
                'header, and re-measure against the DLL rather than assuming.')
        }

        $results += $launcher
    }

    return $results[0]
}

Export-ModuleMember -Function Get-PrismConfigLayout, Assert-PrismConfigContract
