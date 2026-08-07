$ErrorActionPreference = 'Stop'

$workflowPath = Join-Path $PSScriptRoot 'release.yml'

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
            './tools/Invoke-BlindSoldierGhidraVerification.ps1',
            [StringComparison]::Ordinal)

        $buildIndex | Should BeGreaterThan -1
        $verifyIndex | Should BeGreaterThan $buildIndex
        $ghidraIndex | Should BeGreaterThan $verifyIndex
    }
}
