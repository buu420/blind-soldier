using BlindSwordsman.Setup;
using System.Windows.Forms;

static class SetupUiTests
{
    public static void Run()
    {
        ParsesSupportedCommandLineModesStrictly();
        ExtractsEveryEmbeddedDeploymentResource();
        UsesAccessibleStandardControlsAndLogicalKeyboardOrder();
        ExposesTextEquivalentsForProgressAndErrors();
    }

    private static void ExtractsEveryEmbeddedDeploymentResource()
    {
        using var resources = EmbeddedResourceBundle.Extract();
        True(File.Exists(resources.Paths.PreflightScript), "embedded preflight script");
        True(File.Exists(resources.Paths.InstallScript), "embedded install script");
        True(File.Exists(resources.Paths.UninstallScript), "embedded uninstall script");
        True(File.Exists(Path.Combine(resources.Root, "FF7SteamInstall.psm1")), "embedded deployment module");
        True(File.Exists(Path.Combine(resources.Root, "templates", "Ff7.Native.Steam2026.AppConfig.json")), "embedded native profile");
        True(File.Exists(Path.Combine(resources.Root, "analysis", "dual_runtime", "parity-matrix.json")), "embedded parity matrix");
    }

    private static void ParsesSupportedCommandLineModesStrictly()
    {
        var options = SetupCommandLineOptions.Parse([
            "--check-for-updates",
            "--local-manifest", "C:\\Release Files\\blind-swordsman-channel.json",
            "--update-continuation"
        ]);

        True(options.CheckForUpdates, "update mode");
        Equal("C:\\Release Files\\blind-swordsman-channel.json", options.LocalManifestPath, "local manifest path");
        True(options.UpdateContinuation, "update continuation");
        Throws<ArgumentException>(() => SetupCommandLineOptions.Parse(["--uninstall", "--check-for-updates"]), "conflicting modes");
        Throws<ArgumentException>(() => SetupCommandLineOptions.Parse(["--unknown"]), "unknown switch");
        Throws<ArgumentException>(() => SetupCommandLineOptions.Parse(["--local-manifest"]), "missing manifest value");
    }

    private static void UsesAccessibleStandardControlsAndLogicalKeyboardOrder()
    {
        using var form = SetupForm.CreateForAccessibilityTesting();
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
