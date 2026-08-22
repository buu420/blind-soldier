$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$modulePath = Join-Path $scriptRoot 'PrismAbiContract.psm1'
$launcherSource = Join-Path $scriptRoot 'launcher\Ff7.Launcher.Accessible\FF7_Launcher\PrismNativeSpeaker.cs'
$referenceSource = Join-Path $scriptRoot 'Ff7.Accessibility.Reloaded\PrismNativeSpeaker.cs'
$builder = Join-Path $scriptRoot 'Build-AccessibleLauncherBundle.ps1'

Import-Module $modulePath -Force

function New-SourceFixture {
    param([Parameter(Mandatory = $true)] [string] $Body)

    $path = Join-Path ([IO.Path]::GetTempPath()) (
        'prism-abi-fixture-' + [Guid]::NewGuid().ToString('N') + '.cs')
    [IO.File]::WriteAllText($path, $Body)
    return $path
}

# The declaration Prism 0.18 actually wants, as the mod carries it.
$script:referenceBody = @'
using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
internal struct PrismConfig
{
    public byte Version;
    public nint Registry;
    public nint AvailabilityCallback;
    public nint AvailabilityUserdata;
    public uint AvailabilityPollIntervalMs;
    public uint AvailabilityDebounceSamples;
    public uint AvailabilityBackoffMaxMs;
    [MarshalAs(UnmanagedType.I1)] public bool AvailabilityAutoPowerManage;
}
'@

# What 0.4.1 shipped in the launcher. This is the bug, verbatim.
$script:oneByteBody = @'
using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
internal struct PrismConfig
{
    public byte Version;
}
'@

Describe 'Prism ABI contract' {
    It 'rejects the one-byte PrismConfig that crashed the 0.4.1 launcher' {
        $launcher = New-SourceFixture -Body $script:oneByteBody
        $reference = New-SourceFixture -Body $script:referenceBody
        try {
            # Assert the diagnostic, not merely that something threw. A bare
            # `Should Throw` also accepts the null-reference crash that a missing
            # field-count check produces, which would let that check rot away.
            { Assert-PrismConfigContract `
                -LauncherSourcePath $launcher `
                -ReferenceSourcePath $reference } |
                Should Throw 'does not match the Prism ABI contract'
        }
        finally {
            Remove-Item -LiteralPath $launcher, $reference -Force -ErrorAction SilentlyContinue
        }
    }

    It 'marshals the Prism 0.18 declaration to the 32 bytes the shipped DLL returns' {
        # Measured against the real FFVII_LAUNCHER.prism.x86.dll from a .NET Framework
        # x86 probe: NEW_CONFIG_SIZE=32, OLD_CONFIG_SIZE=1, CONFIG_VERSION=3.
        $reference = New-SourceFixture -Body $script:referenceBody
        try {
            $layout = Get-PrismConfigLayout -Path $reference
            $layout.Size | Should Be 32
            $layout.Fields.Count | Should Be 8
            ($layout.Fields | ForEach-Object { $_.Offset }) -join ',' |
                Should Be '0,4,8,12,16,20,24,28'
        }
        finally {
            Remove-Item -LiteralPath $reference -Force -ErrorAction SilentlyContinue
        }
    }

    It 'treats a plain byte and an I1-attributed bool as the same one-byte field' {
        # The launcher cannot spell this field the way the mod does: .NET Framework
        # throws MarshalDirectiveException on a struct-return delegate whose struct
        # carries [MarshalAs(UnmanagedType.I1)]. Both marshal to one byte.
        $asByte = New-SourceFixture -Body ($script:referenceBody -replace `
            '\[MarshalAs\(UnmanagedType\.I1\)\] public bool ', 'public byte ')
        $asBool = New-SourceFixture -Body $script:referenceBody
        try {
            (Get-PrismConfigLayout -Path $asByte).Size | Should Be 32
            { Assert-PrismConfigContract `
                -LauncherSourcePath $asByte `
                -ReferenceSourcePath $asBool } | Should Not Throw
        }
        finally {
            Remove-Item -LiteralPath $asByte, $asBool -Force -ErrorAction SilentlyContinue
        }
    }

    It 'rejects a bare bool, which marshals as a four-byte Win32 BOOL' {
        $bare = New-SourceFixture -Body ($script:referenceBody -replace `
            '\[MarshalAs\(UnmanagedType\.I1\)\] public bool ', 'public bool ')
        try {
            { Get-PrismConfigLayout -Path $bare } |
                Should Throw 'marshals as four bytes'
        }
        finally {
            Remove-Item -LiteralPath $bare -Force -ErrorAction SilentlyContinue
        }
    }

    It 'rejects a reordered struct even when the total size still comes to 32' {
        $reordered = New-SourceFixture -Body @'
using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
internal struct PrismConfig
{
    public byte Version;
    public nint Registry;
    public nint AvailabilityCallback;
    public nint AvailabilityUserdata;
    public byte AvailabilityAutoPowerManage;
    public uint AvailabilityPollIntervalMs;
    public uint AvailabilityDebounceSamples;
    public uint AvailabilityBackoffMaxMs;
}
'@
        $reference = New-SourceFixture -Body $script:referenceBody
        try {
            (Get-PrismConfigLayout -Path $reordered).Size | Should Be 32
            { Assert-PrismConfigContract `
                -LauncherSourcePath $reordered `
                -ReferenceSourcePath $reference } |
                Should Throw 'does not match the Prism ABI contract'
        }
        finally {
            Remove-Item -LiteralPath $reordered, $reference -Force -ErrorAction SilentlyContinue
        }
    }

    It 'ignores commented-out fields' {
        $commented = New-SourceFixture -Body ($script:referenceBody -replace `
            '(?m)^    public uint AvailabilityBackoffMaxMs;',
            "    /* public uint Removed; */`r`n    public uint AvailabilityBackoffMaxMs; // trailing")
        try {
            (Get-PrismConfigLayout -Path $commented).Fields.Count | Should Be 8
        }
        finally {
            Remove-Item -LiteralPath $commented -Force -ErrorAction SilentlyContinue
        }
    }

    It 'rejects a struct the two copies agree on but the shipped Prism does not' {
        # Guard-1's blind spot: comparing the launcher against the mod only proves
        # the two managed copies agree. If a future Prism changes the struct and
        # both copies are updated to the same wrong shape, they still agree.
        #
        # Prism 0.18 defines PRISM_CONFIG_VERSION 3 and prism_init rejects a config
        # whose version byte does not match, so the layout is a versioned contract.
        # Pinning the size makes changing it a deliberate act rather than a drift.
        $drifted = $script:referenceBody -replace `
            '(?m)^    public uint AvailabilityBackoffMaxMs;',
            "    public uint AvailabilityBackoffMaxMs;`r`n    public uint AvailabilitySomethingNew;"
        $launcher = New-SourceFixture -Body $drifted
        $reference = New-SourceFixture -Body $drifted
        try {
            (Get-PrismConfigLayout -Path $launcher).Size | Should Be 36
            { Assert-PrismConfigContract `
                -LauncherSourcePath $launcher `
                -ReferenceSourcePath $reference } |
                Should Throw 'does not match the Prism ABI contract'
        }
        finally {
            Remove-Item -LiteralPath $launcher, $reference -Force -ErrorAction SilentlyContinue
        }
    }

    It 'marshals the same declaration to 48 bytes on x64' {
        # Not hypothetical: Ff7.Accessibility.Steam2026X64.csproj compiles this exact
        # file (Compile Include="..\Ff7.Accessibility.Reloaded\PrismNativeSpeaker.cs")
        # at PlatformTarget x64. Checking only x86 would leave the x64 runtime
        # unguarded, and this is a dual-runtime mod.
        $reference = New-SourceFixture -Body $script:referenceBody
        try {
            $layout = Get-PrismConfigLayout -Path $reference -PointerSize 8
            $layout.Size | Should Be 48
            ($layout.Fields | ForEach-Object { $_.Offset }) -join ',' |
                Should Be '0,8,16,24,32,36,40,44'
        }
        finally {
            Remove-Item -LiteralPath $reference -Force -ErrorAction SilentlyContinue
        }
    }

    It 'rejects a pointer degraded to uint, which only shows up on x64' {
        # nint -> uint is four bytes on x86, so an x86-only check waves it through
        # while the x64 layout silently shifts by four bytes per pointer.
        $degraded = $script:referenceBody -replace 'public nint Registry;', 'public uint Registry;'
        $launcher = New-SourceFixture -Body $degraded
        $reference = New-SourceFixture -Body $script:referenceBody
        try {
            (Get-PrismConfigLayout -Path $launcher -PointerSize 4).Size | Should Be 32
            { Assert-PrismConfigContract `
                -LauncherSourcePath $launcher `
                -ReferenceSourcePath $reference } |
                Should Throw 'does not match the Prism ABI contract'
        }
        finally {
            Remove-Item -LiteralPath $launcher, $reference -Force -ErrorAction SilentlyContinue
        }
    }

    It 'rejects drift that is invisible on x86 and only shows on x64' {
        # Both copies changed identically, so they agree with each other; and a
        # pointer is four bytes on x86, so the x86 total is still exactly the pinned
        # 32. Only the x64 pass sees it: the field grows to eight bytes there and
        # pushes the struct to 56. Without a genuine x64 check this walks straight
        # through, and the x64 mod runtime compiles this same file.
        $widened = $script:referenceBody -replace `
            'public uint AvailabilityPollIntervalMs;', 'public nint AvailabilityPollIntervalMs;'
        $launcher = New-SourceFixture -Body $widened
        $reference = New-SourceFixture -Body $widened
        try {
            (Get-PrismConfigLayout -Path $launcher -PointerSize 4).Size | Should Be 32
            (Get-PrismConfigLayout -Path $launcher -PointerSize 8).Size | Should Be 56
            { Assert-PrismConfigContract `
                -LauncherSourcePath $launcher `
                -ReferenceSourcePath $reference } |
                Should Throw 'does not match the Prism ABI contract on x64'
        }
        finally {
            Remove-Item -LiteralPath $launcher, $reference -Force -ErrorAction SilentlyContinue
        }
    }

    It 'rejects a field whose type changed without changing size' {
        $signed = $script:referenceBody -replace `
            'public uint AvailabilityPollIntervalMs;', 'public int AvailabilityPollIntervalMs;'
        $launcher = New-SourceFixture -Body $signed
        $reference = New-SourceFixture -Body $script:referenceBody
        try {
            { Assert-PrismConfigContract `
                -LauncherSourcePath $launcher `
                -ReferenceSourcePath $reference } |
                Should Throw 'does not match the Prism ABI contract'
        }
        finally {
            Remove-Item -LiteralPath $launcher, $reference -Force -ErrorAction SilentlyContinue
        }
    }

    It 'fails closed on a declaration inside the struct it cannot account for' {
        # A private instance field still occupies space. Skipping what the parser
        # does not recognise would let it move every following field unseen - the
        # parser has to refuse rather than guess.
        $hidden = New-SourceFixture -Body ($script:referenceBody -replace `
            '(?m)^    public byte Version;', "    public byte Version;`r`n    private int hidden;")
        try {
            { Get-PrismConfigLayout -Path $hidden } | Should Throw 'cannot account for'
        }
        finally {
            Remove-Item -LiteralPath $hidden -Force -ErrorAction SilentlyContinue
        }
    }

    It 'requires sequential layout with natural packing' {
        # The whole size computation assumes LayoutKind.Sequential and natural
        # alignment. Pack = 1 would silently invalidate every offset it produces.
        $packed = New-SourceFixture -Body ($script:referenceBody -replace `
            '\[StructLayout\(LayoutKind\.Sequential\)\]',
            '[StructLayout(LayoutKind.Sequential, Pack = 1)]')
        $explicit = New-SourceFixture -Body ($script:referenceBody -replace `
            'LayoutKind\.Sequential', 'LayoutKind.Explicit')
        try {
            { Get-PrismConfigLayout -Path $packed } | Should Throw 'Pack'
            { Get-PrismConfigLayout -Path $explicit } | Should Throw 'LayoutKind.Sequential'
        }
        finally {
            Remove-Item -LiteralPath $packed, $explicit -Force -ErrorAction SilentlyContinue
        }
    }

    It 'accepts the launcher and mod declarations that ship today' {
        Test-Path -LiteralPath $launcherSource | Should Be $true
        Test-Path -LiteralPath $referenceSource | Should Be $true

        $layout = Assert-PrismConfigContract `
            -LauncherSourcePath $launcherSource `
            -ReferenceSourcePath $referenceSource
        $layout.Size | Should Be 32
    }

    It 'is enforced by the launcher bundle builder before it builds anything' {
        # The guard is worthless if it only runs when someone remembers to call it.
        # 0.4.1 shipped because every gate that did run had nothing to say about
        # struct layout.
        #
        # This asserts on the builder's text rather than its behaviour on purpose:
        # driving it behaviourally would mean writing the broken struct into the
        # tracked launcher source and trusting a finally block to put it back, and a
        # test that can leave the repository broken is worse than the drift it
        # guards. The behaviour itself is covered by the cases above; what is left
        # to check here is the wiring.
        $builderText = [IO.File]::ReadAllText($builder)
        $builderText | Should Match 'PrismAbiContract\.psm1'

        $assertAt = $builderText.IndexOf('Assert-PrismConfigContract')
        $assertAt | Should Not Be -1

        # Ordering matters: a guard that runs after dotnet build still wastes the
        # build, and one that runs after the copies has already published the
        # broken launcher into the bundle directory.
        $buildAt = $builderText.IndexOf('dotnet build')
        $buildAt | Should Not Be -1
        ($assertAt -lt $buildAt) | Should Be $true
    }
}
