$ErrorActionPreference = 'Stop'

$workflowPath = Join-Path $PSScriptRoot 'release.yml'
$releaseTrackResolverPath = Join-Path $PSScriptRoot '..\..\tools\Resolve-BlindSoldierReleaseTrack.ps1'

Describe 'Blind Soldier release workflow evidence binding' {
    It 'runs Ghidra after building and verifying the publishable portable archive' {
        $workflow = [IO.File]::ReadAllText($workflowPath)
        $buildIndex = $workflow.IndexOf(
            './Build-BlindSoldierPortablePackage.ps1 -OutputPath ./artifacts/release/Blind-Soldier-Portable.zip',
            [StringComparison]::Ordinal)
        $verifyIndex = $workflow.IndexOf(
            './Verify-BlindSoldierPortablePackage.ps1 -ArchivePath ./artifacts/release/Blind-Soldier-Portable.zip',
            [StringComparison]::Ordinal)
        $ghidraIndex = $workflow.IndexOf(
            './tools/Invoke-BlindSoldierGhidraVerification.ps1 -ArchivePath ./artifacts/release/Blind-Soldier-Portable.zip',
            [StringComparison]::Ordinal)

        $buildIndex | Should BeGreaterThan -1
        $verifyIndex | Should BeGreaterThan $buildIndex
        $ghidraIndex | Should BeGreaterThan $verifyIndex
    }

    It 'derives and verifies the x86-only archive after native source verification' {
        $workflow = [IO.File]::ReadAllText($workflowPath)
        $ghidraIndex = $workflow.IndexOf(
            './tools/Invoke-BlindSoldierGhidraVerification.ps1 -ArchivePath ./artifacts/release/Blind-Soldier-Portable.zip',
            [StringComparison]::Ordinal)
        $buildIndex = $workflow.IndexOf(
            './Build-BlindSoldier2013PortablePackage.ps1 -SourceArchivePath ./artifacts/release/Blind-Soldier-Portable.zip -OutputPath ./artifacts/release/Blind-Soldier-2013-x86-Portable.zip',
            [StringComparison]::Ordinal)
        $verifyIndex = $workflow.IndexOf(
            './Verify-BlindSoldier2013PortablePackage.ps1 -ArchivePath ./artifacts/release/Blind-Soldier-2013-x86-Portable.zip',
            [StringComparison]::Ordinal)

        $buildIndex | Should BeGreaterThan $ghidraIndex
        $verifyIndex | Should BeGreaterThan $buildIndex
    }

    It 'publishes both portable archives and both checksum sidecars' {
        $workflow = [IO.File]::ReadAllText($workflowPath)
        foreach ($asset in @(
            './artifacts/release/Blind-Soldier-Portable.zip',
            './artifacts/release/Blind-Soldier-Portable.zip.sha256',
            './artifacts/release/Blind-Soldier-2013-x86-Portable.zip',
            './artifacts/release/Blind-Soldier-2013-x86-Portable.zip.sha256'
        )) {
            $workflow | Should Match ([regex]::Escape($asset))
        }
    }

    It 'keeps unsuffixed zero-major versions on the prerelease track' {
        (& $releaseTrackResolverPath -Version '0.2.2') | Should Be 'prerelease'
        (& $releaseTrackResolverPath -Version '0.3.0') | Should Be 'prerelease'
        (& $releaseTrackResolverPath -Version '1.0.0') | Should Be 'stable'

        $workflow = [IO.File]::ReadAllText($workflowPath)
        $workflow | Should Match ([regex]::Escape(
            './tools/Resolve-BlindSoldierReleaseTrack.ps1 -Version $version'))
    }
}
