$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $scriptRoot 'launcher\Ff7.Launcher.Accessible\FFVII_LAUNCHER.csproj'
$builder = Join-Path $scriptRoot 'Build-AccessibleLauncherBundle.ps1'

Describe 'Accessible FFVII launcher bundle' {
    It 'is portable, x86, and emits only the supported bundle surface' {
        $projectText = [IO.File]::ReadAllText($project)
        $projectText | Should Not Match '(?i)<HintPath>[A-Z]:\\'
        $projectText | Should Match '<PlatformTarget>x86</PlatformTarget>'
        $projectText | Should Match '<PackageDownload Include="Steamworks.NET" Version="\[2024\.8\.0\]" />'

        Test-Path -LiteralPath $builder | Should Be $true

        $output = Join-Path ([IO.Path]::GetTempPath()) (
            'blind-soldier-launcher-bundle-' + [Guid]::NewGuid().ToString('N'))
        try {
            & $builder -OutputPath $output -Configuration Release
            $LASTEXITCODE | Should Be 0

            $expected = @(
                'FFVII_LAUNCHER.exe',
                'FFVII_LAUNCHER.exe.config',
                'launcher-bundle.json',
                'native\x86\FFVII_LAUNCHER.prism.x86.dll'
            )
            foreach ($relativePath in $expected) {
                Test-Path -LiteralPath (Join-Path $output $relativePath) | Should Be $true
            }

            $actual = @(
                Get-ChildItem -LiteralPath $output -File -Recurse |
                    ForEach-Object {
                        $_.FullName.Substring($output.Length + 1)
                    } |
                    Sort-Object
            )
            ($actual -join '|') | Should Be (($expected | Sort-Object) -join '|')

            $manifest = Get-Content -LiteralPath (Join-Path $output 'launcher-bundle.json') -Raw |
                ConvertFrom-Json
            $manifest.schemaVersion | Should Be 2
            $manifest.assemblyName | Should Be 'FFVII_LAUNCHER'
            $manifest.assemblyVersion | Should Be '2.0.0.0'
            $manifest.launcher.sha256 | Should Match '^[A-F0-9]{64}$'
            $manifest.config.sha256 | Should Match '^[A-F0-9]{64}$'
            $manifest.prism.sha256 | Should Match '^[A-F0-9]{64}$'

            $configText = [IO.File]::ReadAllText(
                (Join-Path $output 'FFVII_LAUNCHER.exe.config'))
            $configText | Should Not Match '(?i)netstandard'
            $configText | Should Not Match `
                '(?i)<bindingRedirect[^>]+newVersion="2\.1\.0\.0"'
        }
        finally {
            if (Test-Path -LiteralPath $output) {
                Remove-Item -LiteralPath $output -Recurse -Force
            }
        }
    }
}
