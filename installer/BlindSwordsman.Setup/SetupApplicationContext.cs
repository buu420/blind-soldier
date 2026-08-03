using BlindSwordsman.Setup.Core;
using System.Diagnostics;
using System.Text.Json;

namespace BlindSwordsman.Setup;

public sealed class SetupApplicationContext : ApplicationContext
{
    private static readonly SemanticVersion CurrentSetupVersion = SemanticVersion.Parse("0.1.0-pre.3");
    private readonly SetupCommandLineOptions options;
    private readonly SetupForm form;
    private readonly InstallerPaths paths;
    private readonly InstallStateStore stateStore;
    private readonly SetupLog log;
    private readonly EmbeddedResourceBundle resources;
    private readonly HttpClient httpClient;
    private readonly ArtifactDownloader downloader;
    private readonly GitHubReleaseClient releaseClient;
    private readonly PreflightClient preflightClient;
    private readonly SetupOrchestrator orchestrator;
    private CancellationTokenSource operationCancellation = new();
    private ReleaseChannelManifest? release;
    private PreflightReport? preflight;
    private InstallState? installedState;
    private SetupMode mode;
    private bool operationRunning;
    private bool disposed;

    public SetupApplicationContext(SetupCommandLineOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        paths = InstallerPaths.ForCurrentUser();
        stateStore = new InstallStateStore(paths.InstallStatePath);
        log = new SetupLog(paths.LogDirectory);
        resources = EmbeddedResourceBundle.Extract();
        httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        downloader = new ArtifactDownloader(httpClient);
        releaseClient = new GitHubReleaseClient(httpClient, "buu420", "blind-swordsman");
        var processRunner = new PowerShellProcessRunner();
        preflightClient = new PreflightClient(processRunner);
        orchestrator = new SetupOrchestrator(downloader, processRunner, stateStore, paths, log);
        form = new SetupForm();
        MainForm = form;
        WireEvents();
        form.FormClosed += (_, _) => ExitThread();
        form.Shown += async (_, _) => await InitializeAsync();
        form.SetNextEnabled(false);
        form.Show();
    }

    private void WireEvents()
    {
        form.NextRequested += async (_, _) => await NextAsync();
        form.BackRequested += (_, _) => Back();
        form.ScanRequested += async (_, _) => await ScanAsync(showErrorsOnProgressPage: false);
        form.InstallRequested += async (_, _) => await PerformOperationAsync();
        form.CancelRequested += (_, _) => CancelOrClose();
        form.FinishRequested += (_, _) => form.Close();
    }

    private async Task InitializeAsync()
    {
        try
        {
            installedState = stateStore.Load();
            if (installedState is not null)
            {
                form.GameRoot = installedState.Game.GameRoot;
                form.ReloadedRoot = installedState.ReloadedRoot;
            }
            if (options.Uninstall)
            {
                if (installedState is null)
                {
                    throw new InvalidOperationException("Blind Swordsman is not registered as installed for this Windows user.");
                }
                mode = SetupMode.Uninstall;
                form.SetWelcome(
                    "Uninstall Blind Swordsman",
                    "Setup will remove files it owns and preserve files that changed after installation.",
                    $"Installed version: {installedState.ProductVersion}");
                form.SetUninstallReview(installedState);
                form.ShowPage(SetupPage.Review);
                return;
            }

            form.SetWelcome(
                "Welcome to Blind Swordsman Setup",
                "This accessible setup detects Final Fantasy VII and Reloaded-II, then installs the dual-runtime accessibility mod.",
                "Checking GitHub for the available release.");
            release = await LoadReleaseAsync(operationCancellation.Token);
            if (release.MinimumSetupVersion > CurrentSetupVersion)
            {
                await ContinueWithUpdatedSetupAsync(release, operationCancellation.Token);
                return;
            }

            mode = SetupModeResolver.Resolve(installedState, release.Version);
            if (mode == SetupMode.DowngradeBlocked)
            {
                form.SetWelcome(
                    "A newer Blind Swordsman version is already installed",
                    "This setup will not replace a newer installation with an older release.",
                    $"Installed: {installedState!.ProductVersion}. Available here: {release.Version}.");
                form.SetNextEnabled(false);
                return;
            }

            var action = mode switch
            {
                SetupMode.Install => "Install",
                SetupMode.Update => "Update",
                SetupMode.Repair => options.CheckForUpdates ? "Already current; repair" : "Repair",
                _ => "Install"
            };
            form.SetWelcome(
                $"{action} Blind Swordsman",
                "The next page checks both supported game runtimes and every required Reloaded-II component.",
                $"Available version: {release.Version}. Setup action: {action}.");
            await ScanAsync(showErrorsOnProgressPage: false);
            form.SetNextEnabled(preflight?.CanInstall == true);
        }
        catch (OperationCanceledException)
        {
            CloseAfterCancellation();
        }
        catch (Exception exception)
        {
            HandleError("Setup could not initialize", exception, canGoBack: false);
        }
    }

    private async Task NextAsync()
    {
        if (operationRunning)
        {
            return;
        }
        if (form.CurrentPage == SetupPage.Welcome)
        {
            form.ShowPage(SetupPage.Locations);
            return;
        }
        if (form.CurrentPage != SetupPage.Locations || release is null)
        {
            return;
        }

        await ScanAsync(showErrorsOnProgressPage: false);
        if (preflight is null || !preflight.CanInstall)
        {
            form.SetNextEnabled(false);
            AccessibleNotifier.Notify(form, "Required dependencies are not ready. Review the dependency status list.", important: true);
            return;
        }

        form.SetReview(mode, release, preflight);
        form.ShowPage(SetupPage.Review);
    }

    private void Back()
    {
        if (operationRunning)
        {
            return;
        }
        switch (form.CurrentPage)
        {
            case SetupPage.Locations:
                form.ShowPage(SetupPage.Welcome);
                break;
            case SetupPage.Review:
            case SetupPage.Progress:
                if (options.Uninstall)
                {
                    form.ShowPage(SetupPage.Review);
                }
                else
                {
                    form.ShowPage(SetupPage.Locations);
                }
                break;
        }
    }

    private async Task ScanAsync(bool showErrorsOnProgressPage)
    {
        if (options.Uninstall || operationRunning)
        {
            return;
        }
        try
        {
            form.SetNextEnabled(false);
            form.AppendStatus("Checking Final Fantasy VII and Reloaded-II dependencies.");
            var temporaryDirectory = Path.Combine(resources.Root, "preflight");
            preflight = await preflightClient.RunAsync(
                resources.Paths.PreflightScript,
                new PreflightOptions(
                    EmptyToNull(form.GameRoot),
                    null,
                    EmptyToNull(form.ReloadedRoot),
                    null),
                temporaryDirectory,
                operationCancellation.Token);
            form.SetPreflight(preflight);
            form.SetNextEnabled(preflight.CanInstall);
            var status = preflight.CanInstall
                ? "Dependency check complete. All required components are ready."
                : "Dependency check complete. One or more required components are not ready.";
            form.AppendStatus(status);
            AccessibleNotifier.Notify(form, status, important: !preflight.CanInstall);
        }
        catch (OperationCanceledException)
        {
            CloseAfterCancellation();
        }
        catch (Exception exception)
        {
            if (showErrorsOnProgressPage)
            {
                HandleError("Dependency check failed", exception, canGoBack: true);
            }
            else
            {
                log.Write(exception.ToString());
                form.ShowError($"Dependency check failed: {exception.Message}");
                form.SetNextEnabled(false);
            }
        }
    }

    private async Task PerformOperationAsync()
    {
        if (operationRunning)
        {
            return;
        }
        operationRunning = true;
        operationCancellation.Dispose();
        operationCancellation = new CancellationTokenSource();
        form.ShowPage(SetupPage.Progress);
        form.SetBusy(true);
        var progress = new Progress<SetupOperationProgress>(form.ReportProgress);
        try
        {
            if (options.Uninstall)
            {
                await orchestrator.UninstallAsync(resources.Paths, progress, operationCancellation.Token);
                form.SetBusy(false);
                form.ShowComplete(
                    "Blind Swordsman was uninstalled",
                    "Setup-owned files were removed. Any changed files were preserved and recorded in the setup log.",
                    log.Path);
            }
            else
            {
                if (release is null || preflight is null || !preflight.CanInstall)
                {
                    throw new InvalidOperationException("The release or dependency check is not ready.");
                }
                var localPayload = FindLocalPayload(options.LocalManifestPath, release.Payload.Name);
                var state = await orchestrator.InstallAsync(
                    new SetupInstallRequest(
                        release,
                        preflight,
                        resources.Paths,
                        Environment.ProcessPath ?? Application.ExecutablePath,
                        localPayload),
                    progress,
                    operationCancellation.Token);
                installedState = state;
                form.SetBusy(false);
                form.ShowComplete(
                    mode switch
                    {
                        SetupMode.Update => "Blind Swordsman was updated",
                        SetupMode.Repair => "Blind Swordsman was repaired",
                        _ => "Blind Swordsman was installed"
                    },
                    $"Version {state.ProductVersion} is ready for both supported Final Fantasy VII runtimes.",
                    log.Path);
            }
        }
        catch (OperationCanceledException)
        {
            log.Write("The operation was canceled by the user.");
            form.SetBusy(false);
            form.ShowError("The operation was canceled. Setup did not report completion.");
            form.EnableBackAfterError();
        }
        catch (Exception exception)
        {
            form.SetBusy(false);
            HandleError("Setup operation failed", exception, canGoBack: true);
        }
        finally
        {
            operationRunning = false;
        }
    }

    private async Task<ReleaseChannelManifest> LoadReleaseAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.LocalManifestPath))
        {
            return await releaseClient.GetLatestAsync(ReleaseTrack.Prerelease, cancellationToken);
        }

        var path = Path.GetFullPath(options.LocalManifestPath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The local release manifest was not found.", path);
        }
        var info = new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length > 1024 * 1024)
        {
            throw new InvalidDataException("The local release manifest is unsafe or unexpectedly large.");
        }
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 10 });
        if (!document.RootElement.TryGetProperty("track", out var trackElement) || trackElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("The local release manifest does not declare a release track.");
        }
        var track = trackElement.GetString() switch
        {
            "stable" => ReleaseTrack.Stable,
            "prerelease" => ReleaseTrack.Prerelease,
            _ => throw new InvalidDataException("The local release manifest has an unknown release track.")
        };
        return ReleaseManifestParser.Parse(json, track);
    }

    private async Task ContinueWithUpdatedSetupAsync(
        ReleaseChannelManifest manifest,
        CancellationToken cancellationToken)
    {
        if (options.UpdateContinuation)
        {
            throw new InvalidOperationException(
                $"The downloaded setup is version {CurrentSetupVersion}, but this release requires setup {manifest.MinimumSetupVersion} or newer.");
        }

        form.ShowPage(SetupPage.Progress);
        form.SetBusy(true);
        form.ReportProgress(new SetupOperationProgress("Update setup", 5, "Downloading a required newer setup executable."));
        var directory = Path.Combine(paths.LocalDataRoot, "Updates", manifest.ReleaseTag + "-" + Guid.NewGuid().ToString("N"));
        var downloaded = await downloader.DownloadAsync(
            manifest.Setup,
            directory,
            new Progress<TransferProgress>(transfer =>
                form.ReportProgress(new SetupOperationProgress("Update setup", 5 + transfer.Percent * 85 / 100, "Downloading the verified setup update."))),
            cancellationToken);
        var startInfo = new ProcessStartInfo(downloaded)
        {
            UseShellExecute = true,
            WorkingDirectory = directory
        };
        if (options.Uninstall)
        {
            startInfo.ArgumentList.Add("--uninstall");
        }
        if (options.CheckForUpdates)
        {
            startInfo.ArgumentList.Add("--check-for-updates");
        }
        if (!string.IsNullOrWhiteSpace(options.LocalManifestPath))
        {
            startInfo.ArgumentList.Add("--local-manifest");
            startInfo.ArgumentList.Add(Path.GetFullPath(options.LocalManifestPath));
        }
        startInfo.ArgumentList.Add("--update-continuation");
        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Windows could not start the verified setup update.");
        log.Write($"Continued setup with verified executable {downloaded}.");
        form.SetBusy(false);
        form.Close();
    }

    private void CancelOrClose()
    {
        if (operationRunning)
        {
            operationCancellation.Cancel();
            form.AppendStatus("Cancellation requested. Waiting for the current safe operation to stop.");
            return;
        }
        operationCancellation.Cancel();
        form.Close();
    }

    private void CloseAfterCancellation()
    {
        form.SetBusy(false);
        form.Close();
    }

    private void HandleError(string heading, Exception exception, bool canGoBack)
    {
        log.Write(exception.ToString());
        form.ShowPage(SetupPage.Progress);
        form.SetBusy(false);
        form.ShowError($"{heading}: {exception.Message}. Detailed log: {log.Path}");
        if (canGoBack)
        {
            form.EnableBackAfterError();
        }
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? FindLocalPayload(string? localManifestPath, string payloadName)
    {
        if (string.IsNullOrWhiteSpace(localManifestPath))
        {
            return null;
        }
        var directory = Path.GetDirectoryName(Path.GetFullPath(localManifestPath))!;
        var candidate = Path.Combine(directory, payloadName);
        return File.Exists(candidate) ? candidate : null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            operationCancellation.Dispose();
            httpClient.Dispose();
            resources.Dispose();
            log.Dispose();
            form.Dispose();
        }
        base.Dispose(disposing);
    }
}
