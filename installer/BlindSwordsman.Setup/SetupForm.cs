using BlindSwordsman.Setup.Core;
using System.Text;

namespace BlindSwordsman.Setup;

public sealed class SetupForm : Form
{
    private readonly Panel contentHost = new();
    private readonly Panel navigationHost = new();
    private readonly Dictionary<SetupPage, Control> pages = [];
    private readonly Label welcomeHeading = new();
    private readonly Label welcomeBody = new();
    private readonly Label releaseStatusLabel = new();
    private readonly ListBox dependencyList = new();
    private readonly TextBox reviewTextBox = new();
    private readonly Label completeHeading = new();
    private readonly Label completeBody = new();
    private readonly LinkLabel logLink = new();
    private readonly Button backButton = new();
    private readonly Button nextButton = new();
    private readonly Button installButton = new();
    private readonly Button cancelButton = new();
    private readonly Button finishButton = new();
    private readonly Button gameBrowseButton = new();
    private readonly Button reloadedBrowseButton = new();
    private readonly Button scanButton = new();
    private readonly Label progressStatusLabel = new();
    private readonly ProgressBar operationProgressBar = new();
    private readonly TextBox visibleStatusLog = new();
    private bool busy;

    public SetupForm()
    {
        Name = "BlindSwordsmanSetupForm";
        Text = "Blind Swordsman Setup";
        AccessibleName = "Blind Swordsman setup";
        AccessibleDescription = "Install, update, repair, or remove the Blind Swordsman accessibility mod for Final Fantasy VII.";
        AutoScaleMode = AutoScaleMode.Font;
        var systemFont = SystemFonts.MessageBoxFont ?? Control.DefaultFont;
        Font = new Font(systemFont.FontFamily, Math.Max(10F, systemFont.Size));
        MinimumSize = new Size(720, 520);
        ClientSize = new Size(800, 580);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        KeyPreview = true;

        contentHost.Name = "ContentHost";
        contentHost.Dock = DockStyle.Fill;
        contentHost.TabStop = false;
        navigationHost.Name = "NavigationHost";
        navigationHost.Dock = DockStyle.Bottom;
        navigationHost.Height = 62;
        navigationHost.Padding = new Padding(12, 10, 12, 10);
        navigationHost.TabStop = false;

        BuildPages();
        BuildNavigation();
        Controls.Add(contentHost);
        Controls.Add(navigationHost);

        Shown += (_, _) => FocusInitialControl();
        FormClosing += HandleFormClosing;
        ShowPage(SetupPage.Welcome);
    }

    public event EventHandler? NextRequested;
    public event EventHandler? BackRequested;
    public event EventHandler? InstallRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? ScanRequested;
    public event EventHandler? FinishRequested;

    public SetupPage CurrentPage { get; private set; }

    public Control InitialFocusControl { get; private set; } = null!;

    public TextBox GameRootTextBox { get; } = CreatePathTextBox(
        "GameRootTextBox",
        "Final Fantasy VII installation folder",
        "Folder containing the supported Final Fantasy VII Steam installation.",
        10);

    public TextBox ReloadedRootTextBox { get; } = CreatePathTextBox(
        "ReloadedRootTextBox",
        "Reloaded-II installation folder",
        "Folder containing Reloaded-II, its loaders, and Shared Hooks.",
        20);

    public Button WelcomeNextButton => nextButton;

    public ProgressBar OperationProgressBar => operationProgressBar;

    public Label ProgressStatusLabel => progressStatusLabel;

    public TextBox VisibleStatusLog => visibleStatusLog;

    public string GameRoot
    {
        get => GameRootTextBox.Text.Trim();
        set => GameRootTextBox.Text = value;
    }

    public string ReloadedRoot
    {
        get => ReloadedRootTextBox.Text.Trim();
        set => ReloadedRootTextBox.Text = value;
    }

    public static SetupForm CreateForAccessibilityTesting() => new();

    public void SetWelcome(string heading, string body, string releaseStatus)
    {
        welcomeHeading.Text = heading;
        welcomeBody.Text = body;
        releaseStatusLabel.Text = releaseStatus;
        releaseStatusLabel.AccessibleName = releaseStatus;
    }

    public void SetNextEnabled(bool enabled) => nextButton.Enabled = enabled;

    public void SetPreflight(PreflightReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Game is not null && string.IsNullOrWhiteSpace(GameRootTextBox.Text))
        {
            GameRootTextBox.Text = report.Game.GameRoot;
        }
        if (!string.IsNullOrWhiteSpace(report.ReloadedRoot) && string.IsNullOrWhiteSpace(ReloadedRootTextBox.Text))
        {
            ReloadedRootTextBox.Text = report.ReloadedRoot;
        }

        dependencyList.BeginUpdate();
        try
        {
            dependencyList.Items.Clear();
            foreach (var dependency in report.Dependencies)
            {
                var state = dependency.Severity switch
                {
                    DependencySeverity.Optional when dependency.Satisfied => "Optional, detected",
                    DependencySeverity.Optional => "Optional, not installed",
                    DependencySeverity.Blocking => "Not ready",
                    _ => "Ready"
                };
                dependencyList.Items.Add($"{state}: {dependency.Name}. {dependency.Message}");
            }
        }
        finally
        {
            dependencyList.EndUpdate();
        }

        dependencyList.AccessibleDescription = report.CanInstall
            ? "All required dependencies are ready."
            : "One or more required dependencies are not ready. Review the list and choose the correct folders.";
        nextButton.Enabled = report.CanInstall;
    }

    public void SetReview(SetupMode mode, ReleaseChannelManifest release, PreflightReport preflight)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(preflight);
        var action = mode switch
        {
            SetupMode.Install => "Install",
            SetupMode.Update => "Update",
            SetupMode.Repair => "Repair",
            SetupMode.Uninstall => "Uninstall",
            _ => "Unavailable"
        };
        var builder = new StringBuilder();
        builder.AppendLine($"Action: {action} Blind Swordsman {release.Version}");
        builder.AppendLine($"Final Fantasy VII: {preflight.Game?.GameRoot ?? "Not detected"}");
        builder.AppendLine($"Reloaded-II: {preflight.ReloadedRoot ?? "Not detected"}");
        builder.AppendLine($"Runtimes: {string.Join(", ", preflight.Game?.Runtimes.Select(runtime => runtime.Architecture) ?? [])}");
        builder.AppendLine("The setup will preserve unrelated 7th Heaven and FFNx configuration.");
        builder.AppendLine("Repair reinstalls setup-owned files while preserving changed user files when possible.");
        reviewTextBox.Text = builder.ToString();
        installButton.Text = mode switch
        {
            SetupMode.Update => "&Update",
            SetupMode.Repair => "&Repair",
            _ => "&Install"
        };
        installButton.AccessibleName = installButton.Text.Replace("&", string.Empty, StringComparison.Ordinal);
    }

    public void SetUninstallReview(InstallState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        reviewTextBox.Text =
            $"Action: Uninstall Blind Swordsman {state.ProductVersion}{Environment.NewLine}" +
            $"Final Fantasy VII: {state.Game.GameRoot}{Environment.NewLine}" +
            $"Reloaded-II: {state.ReloadedRoot}{Environment.NewLine}" +
            "Changed user files are preserved. A prior mod backup is restored only when it still matches the recorded state.";
        installButton.Text = "&Uninstall";
        installButton.AccessibleName = "Uninstall";
    }

    public void ShowPage(SetupPage page)
    {
        CurrentPage = page;
        foreach (var entry in pages)
        {
            entry.Value.Visible = entry.Key == page;
        }

        backButton.Visible = page is SetupPage.Locations or SetupPage.Review;
        nextButton.Visible = page is SetupPage.Welcome or SetupPage.Locations;
        installButton.Visible = page == SetupPage.Review;
        cancelButton.Visible = page != SetupPage.Complete;
        finishButton.Visible = page == SetupPage.Complete;

        InitialFocusControl = page switch
        {
            SetupPage.Welcome => nextButton,
            SetupPage.Locations => GameRootTextBox,
            SetupPage.Review => reviewTextBox,
            SetupPage.Progress => visibleStatusLog,
            SetupPage.Complete => finishButton,
            _ => nextButton
        };
        AcceptButton = page switch
        {
            SetupPage.Welcome or SetupPage.Locations => nextButton,
            SetupPage.Review => installButton,
            SetupPage.Complete => finishButton,
            _ => null
        };
        CancelButton = cancelButton.Visible && !busy ? cancelButton : null;
        if (Visible)
        {
            BeginInvoke(FocusInitialControl);
        }
    }

    public void SetBusy(bool value)
    {
        busy = value;
        backButton.Enabled = !value;
        nextButton.Enabled = !value && nextButton.Enabled;
        installButton.Enabled = !value;
        cancelButton.Enabled = true;
        cancelButton.Text = value ? "&Cancel" : "&Cancel";
        CancelButton = cancelButton;
    }

    public void EnableBackAfterError()
    {
        busy = false;
        backButton.Visible = true;
        backButton.Enabled = true;
        cancelButton.Enabled = true;
        CancelButton = cancelButton;
        InitialFocusControl = backButton;
        if (Visible)
        {
            backButton.Select();
        }
    }

    public void ReportProgress(SetupOperationProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        RunOnUiThread(() =>
        {
            operationProgressBar.Value = Math.Clamp(progress.Percent, operationProgressBar.Minimum, operationProgressBar.Maximum);
            var spoken = $"{progress.Stage}: {progress.Percent} percent. {progress.Message}";
            progressStatusLabel.Text = spoken;
            progressStatusLabel.AccessibleName = spoken;
            operationProgressBar.AccessibleDescription = spoken;
            AppendStatus(progress.Message);
            AccessibleNotifier.Notify(progressStatusLabel, spoken);
        });
    }

    public void ShowError(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        RunOnUiThread(() =>
        {
            var text = "Error: " + message;
            progressStatusLabel.Text = text;
            progressStatusLabel.AccessibleName = text;
            AppendStatus(text);
            AccessibleNotifier.Notify(progressStatusLabel, text, important: true);
        });
    }

    public void ShowComplete(string heading, string body, string logPath)
    {
        completeHeading.Text = heading;
        completeBody.Text = body;
        logLink.Text = "Open setup log";
        logLink.AccessibleName = "Open setup log";
        logLink.Tag = logPath;
        ShowPage(SetupPage.Complete);
        AccessibleNotifier.Notify(completeHeading, $"{heading}. {body}");
    }

    public void AppendStatus(string message)
    {
        if (visibleStatusLog.TextLength > 0)
        {
            visibleStatusLog.AppendText(Environment.NewLine);
        }
        visibleStatusLog.AppendText(message);
        visibleStatusLog.SelectionStart = visibleStatusLog.TextLength;
        visibleStatusLog.ScrollToCaret();
    }

    public void ShowPageForTesting(SetupPage page) => ShowPage(page);

    public void ReportProgressForTesting(string stage, int percent, string message) =>
        ReportProgress(new SetupOperationProgress(stage, percent, message));

    public void ShowErrorForTesting(string message) => ShowError(message);

    private void BuildPages()
    {
        pages.Add(SetupPage.Welcome, BuildWelcomePage());
        pages.Add(SetupPage.Locations, BuildLocationsPage());
        pages.Add(SetupPage.Review, BuildReviewPage());
        pages.Add(SetupPage.Progress, BuildProgressPage());
        pages.Add(SetupPage.Complete, BuildCompletePage());
        foreach (var page in pages.Values)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            page.TabStop = false;
            contentHost.Controls.Add(page);
        }
    }

    private Control BuildWelcomePage()
    {
        var layout = PageLayout("WelcomePage");
        welcomeHeading.Name = "WelcomeHeading";
        welcomeHeading.Text = "Welcome to Blind Swordsman Setup";
        welcomeHeading.AutoSize = true;
        welcomeHeading.Font = new Font(Font, FontStyle.Bold);
        welcomeHeading.AccessibleName = welcomeHeading.Text;
        welcomeBody.Name = "WelcomeBody";
        welcomeBody.Text = "This accessible setup detects supported Final Fantasy VII runtimes, checks Reloaded-II, and installs the same Blind Swordsman features for both versions of the game.";
        welcomeBody.AutoSize = true;
        welcomeBody.MaximumSize = new Size(700, 0);
        welcomeBody.AccessibleName = welcomeBody.Text;
        releaseStatusLabel.Name = "ReleaseStatusLabel";
        releaseStatusLabel.Text = "Checking the release channel and installed version.";
        releaseStatusLabel.AutoSize = true;
        releaseStatusLabel.MaximumSize = new Size(700, 0);
        releaseStatusLabel.AccessibleName = releaseStatusLabel.Text;
        layout.Controls.Add(welcomeHeading, 0, 0);
        layout.Controls.Add(welcomeBody, 0, 1);
        layout.Controls.Add(releaseStatusLabel, 0, 2);
        return layout;
    }

    private Control BuildLocationsPage()
    {
        var layout = PageLayout("LocationsPage");
        layout.ColumnCount = 2;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var heading = Heading("LocationsHeading", "Game and dependency locations");
        var instructions = BodyLabel(
            "LocationsInstructions",
            "Setup discovers the game through Steam and Reloaded-II through its registered launcher or portable game folder. 7th Heaven and FFNx are optional. If a required dependency is not ready, choose the correct folder and scan again.");
        var gameLabel = BodyLabel("GameRootLabel", "Final Fantasy VII folder:");
        gameLabel.UseMnemonic = false;
        gameBrowseButton.Name = "GameBrowseButton";
        gameBrowseButton.Text = "Choose &Game folder...";
        gameBrowseButton.AccessibleName = "Choose Final Fantasy VII folder";
        gameBrowseButton.TabIndex = 11;
        gameBrowseButton.AutoSize = true;
        gameBrowseButton.Click += (_, _) => BrowseForFolder(GameRootTextBox, "Choose the Final Fantasy VII installation folder");
        var reloadedLabel = BodyLabel("ReloadedRootLabel", "Reloaded-II folder:");
        reloadedLabel.UseMnemonic = false;
        reloadedBrowseButton.Name = "ReloadedBrowseButton";
        reloadedBrowseButton.Text = "Choose &Reloaded-II folder...";
        reloadedBrowseButton.AccessibleName = "Choose Reloaded-II folder";
        reloadedBrowseButton.TabIndex = 21;
        reloadedBrowseButton.AutoSize = true;
        reloadedBrowseButton.Click += (_, _) => BrowseForFolder(ReloadedRootTextBox, "Choose the Reloaded-II installation folder");
        dependencyList.Name = "DependencyList";
        dependencyList.AccessibleName = "Dependency status";
        dependencyList.AccessibleDescription = "Setup dependency checks.";
        dependencyList.TabIndex = 30;
        dependencyList.IntegralHeight = false;
        dependencyList.Height = 190;
        dependencyList.HorizontalScrollbar = true;
        scanButton.Name = "ScanButton";
        scanButton.Text = "&Scan again";
        scanButton.AccessibleName = "Scan dependencies again";
        scanButton.TabIndex = 40;
        scanButton.AutoSize = true;
        scanButton.Click += (_, _) => ScanRequested?.Invoke(this, EventArgs.Empty);

        layout.Controls.Add(heading, 0, 0);
        layout.SetColumnSpan(heading, 2);
        layout.Controls.Add(instructions, 0, 1);
        layout.SetColumnSpan(instructions, 2);
        layout.Controls.Add(gameLabel, 0, 2);
        layout.SetColumnSpan(gameLabel, 2);
        layout.Controls.Add(GameRootTextBox, 0, 3);
        layout.Controls.Add(gameBrowseButton, 1, 3);
        layout.Controls.Add(reloadedLabel, 0, 4);
        layout.SetColumnSpan(reloadedLabel, 2);
        layout.Controls.Add(ReloadedRootTextBox, 0, 5);
        layout.Controls.Add(reloadedBrowseButton, 1, 5);
        layout.Controls.Add(dependencyList, 0, 6);
        layout.SetColumnSpan(dependencyList, 2);
        layout.Controls.Add(scanButton, 0, 7);
        layout.SetColumnSpan(scanButton, 2);
        return layout;
    }

    private Control BuildReviewPage()
    {
        var layout = PageLayout("ReviewPage");
        var heading = Heading("ReviewHeading", "Ready to install Blind Swordsman");
        reviewTextBox.Name = "ReviewTextBox";
        reviewTextBox.AccessibleName = "Installation summary";
        reviewTextBox.AccessibleDescription = "Review the action and detected paths before continuing.";
        reviewTextBox.ReadOnly = true;
        reviewTextBox.Multiline = true;
        reviewTextBox.ScrollBars = ScrollBars.Vertical;
        reviewTextBox.TabIndex = 10;
        reviewTextBox.Dock = DockStyle.Fill;
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(reviewTextBox, 0, 1);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        return layout;
    }

    private Control BuildProgressPage()
    {
        var layout = PageLayout("ProgressPage");
        var heading = Heading("ProgressHeading", "Setup progress");
        progressStatusLabel.Name = "ProgressStatusLabel";
        progressStatusLabel.Text = "Preparing setup. 0 percent.";
        progressStatusLabel.AccessibleName = progressStatusLabel.Text;
        progressStatusLabel.AutoSize = true;
        progressStatusLabel.MaximumSize = new Size(700, 0);
        operationProgressBar.Name = "OperationProgressBar";
        operationProgressBar.AccessibleName = "Setup progress";
        operationProgressBar.AccessibleDescription = "Setup progress: 0 percent.";
        operationProgressBar.Minimum = 0;
        operationProgressBar.Maximum = 100;
        operationProgressBar.Value = 0;
        operationProgressBar.TabStop = false;
        operationProgressBar.Dock = DockStyle.Top;
        visibleStatusLog.Name = "VisibleStatusLog";
        visibleStatusLog.AccessibleName = "Setup status log";
        visibleStatusLog.AccessibleDescription = "A readable list of setup progress messages and errors.";
        visibleStatusLog.ReadOnly = true;
        visibleStatusLog.Multiline = true;
        visibleStatusLog.ScrollBars = ScrollBars.Vertical;
        visibleStatusLog.TabIndex = 20;
        visibleStatusLog.Dock = DockStyle.Fill;
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(progressStatusLabel, 0, 1);
        layout.Controls.Add(operationProgressBar, 0, 2);
        layout.Controls.Add(visibleStatusLog, 0, 3);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        return layout;
    }

    private Control BuildCompletePage()
    {
        var layout = PageLayout("CompletePage");
        completeHeading.Name = "CompleteHeading";
        completeHeading.Text = "Setup complete";
        completeHeading.AutoSize = true;
        completeHeading.Font = new Font(Font, FontStyle.Bold);
        completeHeading.AccessibleName = completeHeading.Text;
        completeBody.Name = "CompleteBody";
        completeBody.Text = "Blind Swordsman is ready.";
        completeBody.AutoSize = true;
        completeBody.MaximumSize = new Size(700, 0);
        completeBody.AccessibleName = completeBody.Text;
        logLink.Name = "LogLink";
        logLink.Text = "Open setup log";
        logLink.AccessibleName = "Open setup log";
        logLink.AccessibleDescription = "Open the detailed text log in the default text editor.";
        logLink.TabIndex = 10;
        logLink.AutoSize = true;
        logLink.LinkClicked += (_, _) => OpenLog();
        layout.Controls.Add(completeHeading, 0, 0);
        layout.Controls.Add(completeBody, 0, 1);
        layout.Controls.Add(logLink, 0, 2);
        return layout;
    }

    private void BuildNavigation()
    {
        var buttons = new FlowLayoutPanel
        {
            Name = "NavigationButtons",
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            TabStop = false,
            AutoSize = false
        };
        ConfigureNavigationButton(finishButton, "FinishButton", "&Finish", "Finish setup", 114, (_, _) => FinishRequested?.Invoke(this, EventArgs.Empty));
        ConfigureNavigationButton(cancelButton, "CancelButton", "&Cancel", "Cancel setup", 113, (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty));
        ConfigureNavigationButton(installButton, "InstallButton", "&Install", "Install Blind Swordsman", 112, (_, _) => InstallRequested?.Invoke(this, EventArgs.Empty));
        ConfigureNavigationButton(nextButton, "NextButton", "&Next", "Next", 111, (_, _) => NextRequested?.Invoke(this, EventArgs.Empty));
        ConfigureNavigationButton(backButton, "BackButton", "&Back", "Back", 110, (_, _) => BackRequested?.Invoke(this, EventArgs.Empty));
        buttons.Controls.Add(finishButton);
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(installButton);
        buttons.Controls.Add(nextButton);
        buttons.Controls.Add(backButton);
        navigationHost.Controls.Add(buttons);
    }

    private static TableLayoutPanel PageLayout(string name) => new()
    {
        Name = name,
        AccessibleName = name.Replace("Page", " page", StringComparison.Ordinal),
        ColumnCount = 1,
        RowCount = 8,
        Padding = new Padding(24),
        AutoScroll = true,
        TabStop = false
    };

    private Label Heading(string name, string text) => new()
    {
        Name = name,
        Text = text,
        AccessibleName = text,
        AutoSize = true,
        Font = new Font(Font, FontStyle.Bold),
        Margin = new Padding(3, 3, 3, 16)
    };

    private static Label BodyLabel(string name, string text) => new()
    {
        Name = name,
        Text = text,
        AccessibleName = text,
        AutoSize = true,
        MaximumSize = new Size(700, 0),
        Margin = new Padding(3, 3, 3, 8)
    };

    private static TextBox CreatePathTextBox(string name, string accessibleName, string description, int tabIndex) => new()
    {
        Name = name,
        AccessibleName = accessibleName,
        AccessibleDescription = description,
        TabIndex = tabIndex,
        Dock = DockStyle.Top
    };

    private static void ConfigureNavigationButton(
        Button button,
        string name,
        string text,
        string accessibleName,
        int tabIndex,
        EventHandler click)
    {
        button.Name = name;
        button.Text = text;
        button.AccessibleName = accessibleName;
        button.TabIndex = tabIndex;
        button.AutoSize = true;
        button.MinimumSize = new Size(96, 32);
        button.Click += click;
    }

    private void BrowseForFolder(TextBox target, string description)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(target.Text) ? target.Text : string.Empty
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            target.Text = dialog.SelectedPath;
            target.Focus();
            ScanRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void FocusInitialControl()
    {
        if (InitialFocusControl.Visible && InitialFocusControl.Enabled)
        {
            InitialFocusControl.Select();
        }
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (!busy)
        {
            return;
        }
        eventArgs.Cancel = true;
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenLog()
    {
        if (logLink.Tag is not string path || !File.Exists(path))
        {
            ShowError("The setup log is no longer available.");
            return;
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void RunOnUiThread(Action action)
    {
        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }
}
