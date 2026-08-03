using BlindSwordsman.Setup.Core;

static class InstallStateTests
{
    public static async Task RunAsync()
    {
        ParsesAndPersistsDeploymentStateAtomically();
        ParsesPreLauncherDeploymentState();
        await RejectsCorruptedStateWithoutOverwritingIt();
        ResolvesInstallUpdateRepairAndDowngradeModes();
        BuildsPerUserWindowsRegistration();
    }

    private static void ParsesAndPersistsDeploymentStateAtomically()
    {
        using var fixture = new TemporaryDirectory();
        var state = DeploymentResultParser.Parse(DeploymentResultWithLauncher());
        var store = new InstallStateStore(System.IO.Path.Combine(fixture.Path, "install-state.json"));

        store.Save(state);
        var loaded = store.Load();

        Equal("0.1.0-pre.1", loaded!.ProductVersion.ToString(), "saved product version");
        Equal("INSTALL-FINGERPRINT", loaded.Mod.Fingerprint, "saved package fingerprint");
        Equal(2, loaded.Loaders.Count, "saved loader count");
        True(loaded.Launcher is not null, "saved launcher state");
        Equal(
            "EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE",
            loaded.Launcher!.Executable.InstalledSha256,
            "saved accessible launcher hash");
        Equal(0, Directory.GetFiles(fixture.Path, "*.tmp", SearchOption.TopDirectoryOnly).Length, "atomic state has no temporary file");
    }

    private static void ParsesPreLauncherDeploymentState()
    {
        var state = DeploymentResultParser.Parse(ValidDeploymentResult());

        True(state.Launcher is null, "pre-launcher install state remains readable");
    }

    private static async Task RejectsCorruptedStateWithoutOverwritingIt()
    {
        using var fixture = new TemporaryDirectory();
        var path = System.IO.Path.Combine(fixture.Path, "install-state.json");
        await File.WriteAllTextAsync(path, "not json");
        var store = new InstallStateStore(path);

        Throws<InvalidDataException>(() => store.Load(), "corrupted state load");

        Equal("not json", await File.ReadAllTextAsync(path), "corrupted state preserved for diagnosis");
    }

    private static void ResolvesInstallUpdateRepairAndDowngradeModes()
    {
        var installed = DeploymentResultParser.Parse(ValidDeploymentResult());

        Equal(SetupMode.Install, SetupModeResolver.Resolve(null, SemanticVersion.Parse("0.1.0-pre.1")), "new install mode");
        Equal(SetupMode.Repair, SetupModeResolver.Resolve(installed, SemanticVersion.Parse("0.1.0-pre.1")), "repair mode");
        Equal(SetupMode.Update, SetupModeResolver.Resolve(installed, SemanticVersion.Parse("0.1.0-pre.2")), "update mode");
        Equal(SetupMode.DowngradeBlocked, SetupModeResolver.Resolve(installed, SemanticVersion.Parse("0.0.9")), "downgrade blocked mode");
    }

    private static void BuildsPerUserWindowsRegistration()
    {
        var state = DeploymentResultParser.Parse(ValidDeploymentResult());
        var data = WindowsRegistration.Build(
            state,
            "C:\\Users\\Player\\AppData\\Local\\Programs\\Blind Swordsman\\Blind-Swordsman-Setup.exe",
            "C:\\Users\\Player\\AppData\\Roaming\\Microsoft\\Windows\\Start Menu\\Programs\\Blind Swordsman");

        Equal("Blind Swordsman", data.DisplayName, "display name");
        Equal("0.1.0-pre.1", data.DisplayVersion, "display version");
        True(data.UninstallCommand.EndsWith(" --uninstall", StringComparison.Ordinal), "uninstall command");
        True(data.UninstallCommand.StartsWith("\"C:\\Users\\Player", StringComparison.Ordinal), "quoted setup path");
        Equal("--check-for-updates", data.UpdateShortcut.Arguments, "update shortcut arguments");
        True(data.UpdateShortcut.Path.EndsWith("Check for Blind Swordsman Updates.lnk", StringComparison.Ordinal), "update shortcut name");
    }

    internal static string ValidDeploymentResult() => """
        {
          "schemaVersion": 1,
          "productVersion": "0.1.0-pre.1",
          "releaseTag": "v0.1.0-pre.1",
          "installedAtUtc": "2026-08-03T12:00:00.0000000Z",
          "game": {
            "version": "Steam2026",
            "gameRoot": "X:\\SteamLibrary\\FINAL FANTASY VII Steam Edition"
          },
          "reloadedRoot": "C:\\Users\\Player\\Reloaded-II",
          "mod": {
            "directory": "C:\\Users\\Player\\Reloaded-II\\Mods\\ff7.accessibility.reloaded",
            "fingerprint": "INSTALL-FINGERPRINT",
            "backupPath": null,
            "backupFingerprint": null
          },
          "profile": {
            "path": "C:\\Users\\Player\\Reloaded-II\\Apps\\Ff7.Native.Steam2026.Research\\AppConfig.json",
            "changed": true,
            "installedSha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            "backupPath": null,
            "backupSha256": null,
            "research": true
          },
          "loaders": [
            {
              "id": "legacy-asi-loader",
              "target": "X:\\SteamLibrary\\FINAL FANTASY VII Steam Edition\\ff7\\workingdir\\dsound.dll",
              "sha256": "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
              "changed": true
            },
            {
              "id": "native-asi-loader",
              "target": "X:\\SteamLibrary\\FINAL FANTASY VII Steam Edition\\d3d11.dll",
              "sha256": "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC",
              "changed": false
            }
          ],
          "openingVoice": {
            "wasPresent": false,
            "target": "X:\\SteamLibrary\\FINAL FANTASY VII Steam Edition\\ff7\\workingdir\\override\\movies\\opening_va.ogg",
            "sourceSha256": "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD"
          },
          "ffnx": {
            "releaseTag": "canary-20260712",
            "assetName": "FFNx-Steam.zip"
          }
        }
        """;

    internal static string DeploymentResultWithLauncher() => ValidDeploymentResult().Replace(
        """  "ffnx": {""",
        """
                  "launcher": {
                    "stockLauncherSha256": "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF",
                    "executable": {
                      "target": "X:\\SteamLibrary\\FINAL FANTASY VII Steam Edition\\FFVII_LAUNCHER.exe",
                      "installedSha256": "EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE",
                      "changed": true,
                      "backupPath": "C:\\Users\\Player\\Reloaded-II\\AccessibilityBackups\\ff7-launcher.backup-1234\\FFVII_LAUNCHER.exe",
                      "backupSha256": "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"
                    },
                    "configuration": {
                      "target": "X:\\SteamLibrary\\FINAL FANTASY VII Steam Edition\\FFVII_LAUNCHER.exe.config",
                      "installedSha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                      "changed": true,
                      "backupPath": null,
                      "backupSha256": null
                    },
                    "prism": {
                      "target": "X:\\SteamLibrary\\FINAL FANTASY VII Steam Edition\\launcher_accessibility\\native\\x86\\FFVII_LAUNCHER.prism.x86.dll",
                      "installedSha256": "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
                      "changed": true,
                      "backupPath": null,
                      "backupSha256": null
                    },
                    "manifestPath": "X:\\SteamLibrary\\FINAL FANTASY VII Steam Edition\\launcher_accessibility\\install-manifest.json",
                    "manifestSha256": "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC"
                  },
                  "ffnx": {
        """,
        StringComparison.Ordinal);

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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "blind-swordsman-state-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
