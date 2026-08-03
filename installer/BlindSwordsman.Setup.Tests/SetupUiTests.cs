using BlindSwordsman.Setup;
using BlindSwordsman.Setup.Core;
using System.Reflection;
using System.Windows.Forms;

static class SetupUiTests
{
    public static void Run()
    {
        ParsesSupportedCommandLineModesStrictly();
        SetupEngineVersionMatchesItsPublishedAssemblyVersion();
        ExtractsEveryEmbeddedDeploymentResource();
        UsesAccessibleStandardControlsAndLogicalKeyboardOrder();
        DoesNotBlockOnMissingOptionalIntegrations();
        ExposesTextEquivalentsForProgressAndErrors();
    }

    private static void SetupEngineVersionMatchesItsPublishedAssemblyVersion()
    {
        var assembly = typeof(SetupApplicationContext).Assembly;
        var publishedVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? throw new InvalidOperationException("Setup has no informational version.");
        var field = typeof(SetupApplicationContext).GetField(
            "CurrentSetupVersion",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Setup has no current-version field.");
        var engineVersion = (SemanticVersion?)field.GetValue(null)
            ?? throw new InvalidOperationException("Setup current version is unavailable.");

        Equal(publishedVersion, engineVersion.ToString(), "setup engine version matches published assembly");
    }

    private static void ExtractsEveryEmbeddedDeploymentResource()
    {
        using var resources = EmbeddedResourceBundle.Extract();
        True(File.Exists(resources.Paths.PreflightScript), "embedded preflight script");
        True(File.Exists(resources.Paths.InstallScript), "embedded install script");
        True(File.Exists(resources.Paths.UninstallScript), "embedded uninstall script");
        True(File.Exists(Path.Combine(resources.Root, "FF7SteamInstall.psm1")), "embedded deployment module");
        True(File.Exists(Path.Combine(resources.Root, "FF7LauncherInstall.psm1")), "embedded accessible launcher module");
        True(File.Exists(Path.Combine(resources.Root, "templates", "Ff7.Native.Steam2026.AppConfig.json")), "embedded native profile");
        True(File.Exists(Path.Combine(resources.Root, "templates", "Ff7.Legacy.Steam.AppConfig.json")), "embedded legacy profile");
        True(File.Exists(Path.Combine(resources.Root, "analysis", "dual_runtime", "parity-matrix.json")), "embedded parity matrix");
    }

    private static void ParsesSupportedCommandLineModesStrictly()
    {
        var options = SetupCommandLineOptions.Parse([
            "--check-for-updates",
            "--local-manifest", "C:\\Release Files\\blind-soldier-channel.json",
            "--update-continuation"
        ]);

        True(options.CheckForUpdates, "update mode");
        Equal("C:\\Release Files\\blind-soldier-channel.json", options.LocalManifestPath, "local manifest path");
        True(options.UpdateContinuation, "update continuation");
        Throws<ArgumentException>(() => SetupCommandLineOptions.Parse(["--uninstall", "--check-for-updates"]), "conflicting modes");
        Throws<ArgumentException>(() => SetupCommandLineOptions.Parse(["--unknown"]), "unknown switch");
        Throws<ArgumentException>(() => SetupCommandLineOptions.Parse(["--local-manifest"]), "missing manifest value");
    }

    private static void UsesAccessibleStandardControlsAndLogicalKeyboardOrder()
    {
        using var form = SetupForm.CreateForAccessibilityTesting();
        Equal("Blind Soldier Setup", form.Text, "product window title");
        Equal("Blind Soldier setup", form.AccessibleName, "accessible product name");
        True(form.AccessibleDescription?.Contains("Blind Soldier", StringComparison.Ordinal) == true, "accessible product description");
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new System.Drawing.Point(-32000, -32000);
        form.Show();
        True(form.FormBorderStyle != FormBorderStyle.None, "standard window border");
        True(form.AutoScaleMode == AutoScaleMode.Font, "font scaling");

        foreach (var page in Enum.GetValues<SetupPage>())
        {
            form.ShowPageForTesting(page);
            var interactive = Descendants(form)
                .Where(control => control.Visible && control.Enabled && control.TabStop)
                .ToList();
            True(interactive.Count > 0, $"{page} has keyboard controls");
            foreach (var control in interactive)
            {
                True(!string.IsNullOrWhiteSpace(control.AccessibleName), $"{page}/{control.Name} accessible name");
                True(control is not (PictureBox or Panel), $"{page}/{control.Name} is a standard semantic control");
            }

            var tabIndices = interactive.Select(control => control.TabIndex).ToList();
            Equal(tabIndices.Count, tabIndices.Distinct().Count(), $"{page} unique tab order");

            var mnemonicButtons = interactive.OfType<Button>().Where(button => button.Text.Contains('&')).ToList();
            var mnemonics = mnemonicButtons.Select(Mnemonic).ToList();
            Equal(mnemonics.Count, mnemonics.Distinct().Count(), $"{page} unique button mnemonics");
        }

        form.ShowPageForTesting(SetupPage.Welcome);
        Equal(form.WelcomeNextButton, form.InitialFocusControl, "welcome initial focus");
        form.ShowPageForTesting(SetupPage.Locations);
        Equal(form.GameRootTextBox, form.InitialFocusControl, "locations initial focus");
    }

    private static void ExposesTextEquivalentsForProgressAndErrors()
    {
        using var form = SetupForm.CreateForAccessibilityTesting();
        form.ShowPageForTesting(SetupPage.Progress);
        form.ReportProgressForTesting("Install", 65, "Installing the dual-runtime mod.");

        Equal(65, form.OperationProgressBar.Value, "progress value");
        True(form.ProgressStatusLabel.Text.Contains("65 percent", StringComparison.Ordinal), "visible progress percentage");
        True(form.OperationProgressBar.AccessibleDescription?.Contains("65 percent", StringComparison.Ordinal) == true, "progress accessible description");
        form.ShowErrorForTesting("The Reloaded-II dependency is missing.");
        True(form.VisibleStatusLog.Text.Contains("Reloaded-II dependency is missing", StringComparison.Ordinal), "visible error text");
    }

    private static void DoesNotBlockOnMissingOptionalIntegrations()
    {
        using var form = SetupForm.CreateForAccessibilityTesting();
        var report = PreflightReportParser.Parse("""
            {
              "schemaVersion": 1,
              "canInstall": true,
              "game": {
                "version": "Steam2026",
                "steamAppId": "3837340",
                "gameRoot": "X:\\FINAL FANTASY VII",
                "runtimes": [
                  { "id": "ff7-steam-legacy-x86", "architecture": "x86", "root": "X:\\FINAL FANTASY VII\\ff7\\workingdir", "executable": "X:\\FINAL FANTASY VII\\ff7\\workingdir\\ff7_en.exe" }
                ]
              },
              "reloadedRoot": "C:\\Reloaded-II",
              "seventhHeavenRoot": null,
              "dependencies": [
                { "id": "game", "name": "Final Fantasy VII", "severity": "required", "satisfied": true, "message": "Ready.", "path": "X:\\FINAL FANTASY VII" },
                { "id": "reloaded", "name": "Reloaded-II", "severity": "required", "satisfied": true, "message": "Ready.", "path": "C:\\Reloaded-II" },
                { "id": "seventh-heaven", "name": "7th Heaven", "severity": "optional", "satisfied": false, "message": "Not installed. Optional.", "path": null },
                { "id": "ffnx", "name": "FFNx", "severity": "optional", "satisfied": false, "message": "Not installed. Optional.", "path": null }
              ]
            }
            """);

        form.SetPreflight(report);

        True(form.WelcomeNextButton.Enabled, "missing optional integrations leave Next enabled");
        var dependencyList = Descendants(form).OfType<ListBox>().Single(control => control.Name == "DependencyList");
        var dependencyText = string.Join(Environment.NewLine, dependencyList.Items.Cast<object>());
        True(dependencyText.Contains("Optional, not installed: 7th Heaven", StringComparison.Ordinal), "7th Heaven optional status");
        True(dependencyText.Contains("Optional, not installed: FFNx", StringComparison.Ordinal), "FFNx optional status");
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static char Mnemonic(Button button)
    {
        var index = button.Text.IndexOf('&');
        if (index < 0 || index + 1 >= button.Text.Length || button.Text[index + 1] == '&')
        {
            throw new InvalidOperationException($"Button '{button.Name}' has no usable mnemonic.");
        }
        return char.ToUpperInvariant(button.Text[index + 1]);
    }

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
