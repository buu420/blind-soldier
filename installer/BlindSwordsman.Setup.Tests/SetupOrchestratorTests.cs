using BlindSwordsman.Setup.Core;

static class SetupOrchestratorTests
{
    public static void Run()
    {
        BuildsArgumentListWithoutShellQuoting();
        NeverInstallsOptionalFfnxOrRequiresSeventhHeaven();
        RequiresLauncherBundleOnlyForNativeRuntime();
        RejectsADeploymentResultForAnotherReleaseOrLocation();
    }

    private static void BuildsArgumentListWithoutShellQuoting()
    {
        var preflight = PreflightReportParser.Parse("""
            {
              "schemaVersion": 1,
              "canInstall": true,
              "game": {
                "version": "Steam2026",
                "steamAppId": "3837340",
                "gameRoot": "X:\\Steam Library\\FINAL FANTASY VII",
                "runtimes": [
                  { "id": "ff7-steam-legacy-x86", "architecture": "x86", "root": "X:\\Steam Library\\FINAL FANTASY VII\\ff7\\workingdir", "executable": "X:\\Steam Library\\FINAL FANTASY VII\\ff7\\workingdir\\ff7_en.exe" },
                  { "id": "ff7-steam-2026-x64", "architecture": "x64", "root": "X:\\Steam Library\\FINAL FANTASY VII", "executable": "X:\\Steam Library\\FINAL FANTASY VII\\FFVII.exe" }
                ]
              },
              "reloadedRoot": "C:\\Users\\Player\\Reloaded II",
              "seventhHeavenRoot": "C:\\Users\\Player\\7th Heaven",
              "dependencies": [
                { "id": "game", "name": "Final Fantasy VII", "severity": "required", "satisfied": true, "message": "Ready.", "path": "X:\\Steam Library\\FINAL FANTASY VII" },
                { "id": "reloaded", "name": "Reloaded-II", "severity": "required", "satisfied": true, "message": "Ready.", "path": "C:\\Users\\Player\\Reloaded II" },
                { "id": "ffnx", "name": "FFNx", "severity": "optional", "satisfied": true, "message": "Ready.", "path": "X:\\Steam Library\\FINAL FANTASY VII\\ff7\\workingdir\\AF3DN.P" }
              ]
            }
            """);
        var manifest = ReleaseManifestParser.Parse(ChannelManifest(), ReleaseTrack.Prerelease);

        var arguments = SetupOrchestrator.BuildDeploymentArguments(
            preflight,
            manifest,
            "C:\\Stage Folder\\package\\ff7.accessibility.reloaded",
            "C:\\State Folder\\result.json",
            "C:\\Stage Folder\\launcher",
            "C:\\Stage Folder\\prerequisites");

        Equal("-GameRoot", arguments[0], "first argument name");
        Equal("X:\\Steam Library\\FINAL FANTASY VII", arguments[1], "game root remains one argument");
        True(arguments.Contains("-AllowResearchNativeProfile"), "x64 research switch");
        True(arguments.Contains("-SkipFfnx"), "existing FFNx is not needlessly replaced");
        True(!arguments.Contains("-SeventhHeavenRoot"), "detected optional 7th Heaven is not passed to deployment");
        Equal(
            System.IO.Path.GetFullPath("C:\\Stage Folder\\launcher"),
            arguments[arguments.IndexOf("-LauncherBundlePath") + 1],
            "launcher bundle remains one argument");
        Equal(
            System.IO.Path.GetFullPath("C:\\Stage Folder\\prerequisites"),
            arguments[arguments.IndexOf("-PrerequisiteBundlePath") + 1],
            "prerequisite bundle remains one argument");
        Equal("v0.1.0-pre.1", arguments[arguments.IndexOf("-ReleaseTag") + 1], "release tag argument");
    }

    private static void NeverInstallsOptionalFfnxOrRequiresSeventhHeaven()
    {
        var preflight = PreflightReportParser.Parse("""
            {
              "schemaVersion": 1,
              "canInstall": true,
              "game": {
                "version": "Steam2026",
                "steamAppId": "3837340",
                "gameRoot": "X:\\Steam Library\\FINAL FANTASY VII",
                "runtimes": [
                  { "id": "ff7-steam-legacy-x86", "architecture": "x86", "root": "X:\\Steam Library\\FINAL FANTASY VII\\ff7\\workingdir", "executable": "X:\\Steam Library\\FINAL FANTASY VII\\ff7\\workingdir\\ff7_en.exe" },
                  { "id": "ff7-steam-2026-x64", "architecture": "x64", "root": "X:\\Steam Library\\FINAL FANTASY VII", "executable": "X:\\Steam Library\\FINAL FANTASY VII\\FFVII.exe" }
                ]
              },
              "reloadedRoot": "C:\\Users\\Player\\Reloaded II",
              "seventhHeavenRoot": null,
              "dependencies": [
                { "id": "game", "name": "Final Fantasy VII", "severity": "required", "satisfied": true, "message": "Ready.", "path": "X:\\Steam Library\\FINAL FANTASY VII" },
                { "id": "reloaded", "name": "Reloaded-II", "severity": "required", "satisfied": true, "message": "Ready.", "path": "C:\\Users\\Player\\Reloaded II" },
                { "id": "seventh-heaven", "name": "7th Heaven", "severity": "optional", "satisfied": false, "message": "Not installed. Optional.", "path": null },
                { "id": "ffnx", "name": "FFNx", "severity": "optional", "satisfied": false, "message": "Not installed. Optional.", "path": null }
              ]
            }
            """);
        var manifest = ReleaseManifestParser.Parse(ChannelManifest(), ReleaseTrack.Prerelease);

        var arguments = SetupOrchestrator.BuildDeploymentArguments(
            preflight,
            manifest,
            "C:\\Stage Folder\\package\\ff7.accessibility.reloaded",
            "C:\\State Folder\\result.json",
            "C:\\Stage Folder\\launcher",
            "C:\\Stage Folder\\prerequisites");

        True(arguments.Contains("-SkipFfnx"), "missing optional FFNx is not installed");
        True(arguments.Contains("-SkipSeventhHeavenSettings"), "7th Heaven settings are never required");
        True(!arguments.Contains("-SeventhHeavenRoot"), "missing optional 7th Heaven has no deployment argument");
    }

    private static void RequiresLauncherBundleOnlyForNativeRuntime()
    {
        var nativePreflight = PreflightReportParser.Parse("""
            {
              "schemaVersion": 1,
              "canInstall": true,
              "game": {
                "version": "Steam2026",
                "steamAppId": "3837340",
                "gameRoot": "X:\\Steam Library\\FINAL FANTASY VII",
                "runtimes": [
                  { "id": "ff7-steam-2026-x64", "architecture": "x64", "root": "X:\\Steam Library\\FINAL FANTASY VII", "executable": "X:\\Steam Library\\FINAL FANTASY VII\\FFVII.exe" }
                ]
              },
              "reloadedRoot": "C:\\Users\\Player\\Reloaded II",
              "seventhHeavenRoot": null,
              "dependencies": [
                { "id": "game", "name": "Final Fantasy VII", "severity": "required", "satisfied": true, "message": "Ready.", "path": "X:\\Steam Library\\FINAL FANTASY VII" },
                { "id": "reloaded", "name": "Reloaded-II", "severity": "required", "satisfied": true, "message": "Ready.", "path": "C:\\Users\\Player\\Reloaded II" }
              ]
            }
            """);
        var legacyPreflight = PreflightReportParser.Parse("""
            {
              "schemaVersion": 1,
              "canInstall": true,
              "game": {
                "version": "SteamLegacy",
                "steamAppId": "39140",
                "gameRoot": "X:\\Steam Library\\FINAL FANTASY VII",
                "runtimes": [
                  { "id": "ff7-steam-legacy-x86", "architecture": "x86", "root": "X:\\Steam Library\\FINAL FANTASY VII\\ff7\\workingdir", "executable": "X:\\Steam Library\\FINAL FANTASY VII\\ff7\\workingdir\\ff7_en.exe" }
                ]
              },
              "reloadedRoot": "C:\\Users\\Player\\Reloaded II",
              "seventhHeavenRoot": null,
              "dependencies": [
                { "id": "game", "name": "Final Fantasy VII", "severity": "required", "satisfied": true, "message": "Ready.", "path": "X:\\Steam Library\\FINAL FANTASY VII" },
                { "id": "reloaded", "name": "Reloaded-II", "severity": "required", "satisfied": true, "message": "Ready.", "path": "C:\\Users\\Player\\Reloaded II" }
              ]
            }
            """);
        var manifest = ReleaseManifestParser.Parse(ChannelManifest(), ReleaseTrack.Prerelease);

        Throws<InvalidOperationException>(() => SetupOrchestrator.BuildDeploymentArguments(
            nativePreflight,
            manifest,
            "C:\\Stage\\package",
            "C:\\Stage\\result.json",
            null,
            "C:\\Stage\\prerequisites"), "native runtime without launcher bundle");
        var legacyArguments = SetupOrchestrator.BuildDeploymentArguments(
            legacyPreflight,
            manifest,
            "C:\\Stage\\package",
            "C:\\Stage\\result.json",
            null,
            "C:\\Stage\\prerequisites");
        True(!legacyArguments.Contains("-LauncherBundlePath"), "legacy-only install omits launcher bundle");
    }

    private static void RejectsADeploymentResultForAnotherReleaseOrLocation()
    {
        var state = DeploymentResultParser.Parse(InstallStateTests.DeploymentResultWithLauncher());
        var missingLauncherState = DeploymentResultParser.Parse(InstallStateTests.ValidDeploymentResult());
        var manifest = ReleaseManifestParser.Parse(ChannelManifest(), ReleaseTrack.Prerelease);

        SetupOrchestrator.ValidateDeploymentResult(
            state,
            manifest,
            "X:\\SteamLibrary\\FINAL FANTASY VII Steam Edition",
            "C:\\Users\\Player\\Reloaded-II");
        Throws<InvalidDataException>(() => SetupOrchestrator.ValidateDeploymentResult(
            state with { LegacyProfile = null },
            manifest,
            state.Game.GameRoot,
            state.ReloadedRoot), "legacy deployment result without legacy profile state");
        Throws<InvalidDataException>(() => SetupOrchestrator.ValidateDeploymentResult(
            state with
            {
                LegacyProfile = state.LegacyProfile! with
                {
                    Path = "C:\\Users\\Player\\Reloaded-II\\Apps\\Forged\\AppConfig.json"
                }
            },
            manifest,
            state.Game.GameRoot,
            state.ReloadedRoot), "legacy deployment result with wrong legacy profile path");
        Throws<InvalidDataException>(() => SetupOrchestrator.ValidateDeploymentResult(
            missingLauncherState,
            manifest,
            missingLauncherState.Game.GameRoot,
            missingLauncherState.ReloadedRoot), "native deployment result without launcher state");
        Throws<InvalidDataException>(() => SetupOrchestrator.ValidateDeploymentResult(
            state,
            manifest with { Version = SemanticVersion.Parse("0.1.0-pre.2"), ReleaseTag = "v0.1.0-pre.2" },
            state.Game.GameRoot,
            state.ReloadedRoot), "wrong release result");
        Throws<InvalidDataException>(() => SetupOrchestrator.ValidateDeploymentResult(
            state,
            manifest,
            "C:\\Another Game",
            state.ReloadedRoot), "wrong game result");
    }

    private static string ChannelManifest() => $$"""
        {
          "schemaVersion": 1,
          "version": "0.1.0-pre.1",
          "releaseTag": "v0.1.0-pre.1",
          "track": "prerelease",
          "minimumSetupVersion": "0.1.0-pre.1",
          "payload": {
            "name": "Blind-Swordsman-Runtime.zip",
            "url": "https://github.com/buu420/blind-swordsman/releases/download/v0.1.0-pre.1/Blind-Swordsman-Runtime.zip",
            "sha256": "{{new string('A', 64)}}",
            "size": 1234
          },
          "setup": {
            "name": "Blind-Swordsman-Setup.exe",
            "url": "https://github.com/buu420/blind-swordsman/releases/download/v0.1.0-pre.1/Blind-Swordsman-Setup.exe",
            "sha256": "{{new string('B', 64)}}",
            "size": 5678
          }
        }
        """;

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static void True(bool value, string label)
    {
        if (!value)
        {
            throw new InvalidOperationException($"{label}: expected true.");
        }
    }

    private static void Throws<TException>(Action action, string label)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"{label}: expected {typeof(TException).Name}.");
    }
}
