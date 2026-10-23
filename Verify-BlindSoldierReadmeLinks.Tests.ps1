$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$verifierPath = Join-Path $scriptRoot 'Verify-BlindSoldierReadmeLinks.ps1'
$shippedReadme = Join-Path $scriptRoot 'README.md'

function New-ReadmeFixture {
    param([string] $Content)
    $path = Join-Path ([IO.Path]::GetTempPath()) ("readme-links-{0}.md" -f [Guid]::NewGuid())
    [IO.File]::WriteAllText($path, $Content, (New-Object Text.UTF8Encoding($false)))
    return $path
}

function New-LinkLine {
    param([string] $Tag, [string] $Asset = 'Blind-Soldier-Portable.zip')
    return "- [$Asset](https://github.com/buu420/blind-soldier/releases/download/$Tag/$Asset)"
}

Describe 'Verify-BlindSoldierReadmeLinks' {
    It 'accepts a README whose download links name the release being built' {
        $path = New-ReadmeFixture (@(
            '# Blind Soldier',
            (New-LinkLine -Tag 'v0.4.1'),
            (New-LinkLine -Tag 'v0.4.1' -Asset 'Blind-Soldier-2013-x86-Portable.zip')
        ) -join [Environment]::NewLine)
        try {
            { & $verifierPath -ExpectedVersion '0.4.1' -ReadmePath $path } |
                Should Not Throw
        } finally { Remove-Item -LiteralPath $path -Force }
    }

    It 'fails a release whose README still points at the previous one' {
        # The fault this check exists for. The links still resolve and the ZIP
        # still installs, so nothing looks wrong; the player just silently gets
        # the build before the fix.
        $path = New-ReadmeFixture (New-LinkLine -Tag 'v0.4.0')
        try {
            { & $verifierPath -ExpectedVersion '0.4.1' -ReadmePath $path } |
                Should Throw
        } finally { Remove-Item -LiteralPath $path -Force }
    }

    It 'names the stale tag and the expected tag in the failure' {
        $path = New-ReadmeFixture (New-LinkLine -Tag 'v0.2.8')
        try {
            $message = ''
            try { & $verifierPath -ExpectedVersion '0.4.1' -ReadmePath $path }
            catch { $message = $_.Exception.Message }
            $message | Should Match 'v0\.2\.8'
            $message | Should Match 'v0\.4\.1'
        } finally { Remove-Item -LiteralPath $path -Force }
    }

    It 'rejects a /releases/latest/ link instead of treating it as version-free' {
        # It looks like the maintenance-free answer. GitHub resolves "latest" to
        # the newest release that is not a prerelease, and every Blind Soldier
        # release is one, so on 2026-08-21 that URL served v0.1.11 - older than
        # the links it would have replaced, and from before the screen reader fix.
        $path = New-ReadmeFixture (
            '- [Blind-Soldier-Portable.zip](https://github.com/buu420/blind-soldier/releases/latest/download/Blind-Soldier-Portable.zip)')
        try {
            $message = ''
            try { & $verifierPath -ExpectedVersion '0.4.1' -ReadmePath $path }
            catch { $message = $_.Exception.Message }
            $message | Should Match 'prerelease'
        } finally { Remove-Item -LiteralPath $path -Force }
    }

    It 'fails a README that has lost its download links rather than passing vacuously' {
        $path = New-ReadmeFixture '# Blind Soldier'
        try {
            { & $verifierPath -ExpectedVersion '0.4.1' -ReadmePath $path } |
                Should Throw
        } finally { Remove-Item -LiteralPath $path -Force }
    }

    It 'rejects a link to an asset the release does not publish' {
        $path = New-ReadmeFixture (New-LinkLine -Tag 'v0.4.1' -Asset 'Blind-Soldier-Setup.exe')
        try {
            { & $verifierPath -ExpectedVersion '0.4.1' -ReadmePath $path } |
                Should Throw
        } finally { Remove-Item -LiteralPath $path -Force }
    }

    It 'accepts the sha256 sidecars, which are published alongside the archives' {
        $path = New-ReadmeFixture (
            (New-LinkLine -Tag 'v0.4.1' -Asset 'Blind-Soldier-Portable.zip.sha256'))
        try {
            { & $verifierPath -ExpectedVersion '0.4.1' -ReadmePath $path } |
                Should Not Throw
        } finally { Remove-Item -LiteralPath $path -Force }
    }

    It 'rejects a version that is not a semantic version' {
        $path = New-ReadmeFixture (New-LinkLine -Tag 'v0.4.1')
        try {
            { & $verifierPath -ExpectedVersion 'v0.4.1' -ReadmePath $path } |
                Should Throw
        } finally { Remove-Item -LiteralPath $path -Force }
    }

    It 'reports a missing README rather than passing' {
        $missing = Join-Path ([IO.Path]::GetTempPath()) ("absent-{0}.md" -f [Guid]::NewGuid())
        { & $verifierPath -ExpectedVersion '0.4.1' -ReadmePath $missing } |
            Should Throw
    }

    It 'passes against the README this repository actually ships' {
        # Binds the check to the real file, so a README reorganisation that moved
        # or reworded the download links cannot leave this suite green while the
        # release step it guards would fail.
        $version = ([regex]::Match(
            (Get-Content -LiteralPath $shippedReadme -Raw),
            'releases/download/v(?<version>[^/\s)]+)/')).Groups['version'].Value
        $version | Should Not BeNullOrEmpty
        { & $verifierPath -ExpectedVersion $version -ReadmePath $shippedReadme } |
            Should Not Throw
    }
}
