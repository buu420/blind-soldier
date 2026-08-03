namespace BlindSwordsman.Setup.Core;

public sealed record SetupResourcePaths(
    string PreflightScript,
    string InstallScript,
    string UninstallScript);

public sealed record SetupInstallRequest(
    ReleaseChannelManifest Release,
    PreflightReport Preflight,
    SetupResourcePaths Resources,
    string CurrentSetupPath,
    string? LocalPayloadPath = null);

public sealed record SetupOperationProgress(string Stage, int Percent, string Message);

public sealed class SetupOrchestrator(
    ArtifactDownloader downloader,
    PowerShellProcessRunner processRunner,
    InstallStateStore stateStore,
    InstallerPaths paths,
    SetupLog log)
{
    public static List<string> BuildDeploymentArguments(
        PreflightReport preflight,
        ReleaseChannelManifest release,
        string packagePath,
        string resultPath)
    {
        ArgumentNullException.ThrowIfNull(preflight);
        ArgumentNullException.ThrowIfNull(release);
        if (!preflight.CanInstall || preflight.Game is null || string.IsNullOrWhiteSpace(preflight.ReloadedRoot))
        {
            throw new InvalidOperationException("Deployment arguments require a successful installer preflight.");
        }

        var arguments = new List<string>
        {
            "-GameRoot", preflight.Game.GameRoot,
            "-ReloadedRoot", preflight.ReloadedRoot,
            "-PackagePath", System.IO.Path.GetFullPath(packagePath),
            "-ResultPath", System.IO.Path.GetFullPath(resultPath),
            "-ProductVersion", release.Version.ToString(),
            "-ReleaseTag", release.ReleaseTag,
            "-SkipSeventhHeavenSettings"
        };
        // FFNx is an optional third-party integration. Blind Swordsman setup
        // detects it for compatibility reporting but never installs or replaces it.
        arguments.Add("-SkipFfnx");
        if (preflight.Game.Runtimes.Any(runtime => runtime.Architecture == "x64"))
        {
            arguments.Add("-AllowResearchNativeProfile");
        }
        return arguments;
    }

    public static void ValidateDeploymentResult(
        InstallState state,
        ReleaseChannelManifest release,
        string gameRoot,
        string reloadedRoot)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(release);
        if (state.ProductVersion != release.Version ||
            !string.Equals(state.ReleaseTag, release.ReleaseTag, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Deployment result does not match the selected release.");
        }

        if (!SamePath(state.Game.GameRoot, gameRoot) || !SamePath(state.ReloadedRoot, reloadedRoot))
        {
            throw new InvalidDataException("Deployment result does not match the preflight locations.");
        }

        var expectedMod = System.IO.Path.Combine(
            System.IO.Path.GetFullPath(reloadedRoot),
            "Mods",
            "ff7.accessibility.reloaded");
        if (!SamePath(state.Mod.Directory, expectedMod))
        {
            throw new InvalidDataException("Deployment result contains an unexpected mod directory.");
        }
    }

    public async Task<InstallState> InstallAsync(
        SetupInstallRequest request,
        IProgress<SetupOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Preflight.CanInstall || request.Preflight.Game is null ||
            string.IsNullOrWhiteSpace(request.Preflight.ReloadedRoot))
        {
            throw new InvalidOperationException("Cannot install while required dependencies are missing.");
        }

        var stagingRoot = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "blind-swordsman-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        log.Write($"Beginning {request.Release.ReleaseTag} deployment into {request.Preflight.Game.GameRoot}.");
        try
        {
            Report(progress, "Download", 5, "Preparing the verified runtime package.");
            string payloadPath;
            if (string.IsNullOrWhiteSpace(request.LocalPayloadPath))
            {
                payloadPath = await downloader.DownloadAsync(
                    request.Release.Payload,
                    stagingRoot,
                    new Progress<TransferProgress>(transfer =>
                        Report(progress, "Download", 5 + transfer.Percent * 35 / 100, $"Downloading runtime package: {transfer.Percent} percent.")),
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                payloadPath = System.IO.Path.GetFullPath(request.LocalPayloadPath);
                var info = new FileInfo(payloadPath);
                if (!info.Exists || info.Length != request.Release.Payload.Size)
                {
                    throw new InvalidDataException("Local runtime package length does not match the channel manifest.");
                }
                var hash = await HashVerifier.ComputeSha256Async(payloadPath, cancellationToken).ConfigureAwait(false);
                if (!HashVerifier.FixedTimeEquals(request.Release.Payload.Sha256, hash))
                {
                    throw new InvalidDataException("Local runtime package SHA-256 does not match the channel manifest.");
                }
            }

            Report(progress, "Verify", 45, "Verifying and extracting the runtime package.");
            var extractedRoot = System.IO.Path.Combine(stagingRoot, "extracted");
            SafeZipExtractor.ExtractAndValidate(payloadPath, extractedRoot);
            var packagePath = System.IO.Path.Combine(extractedRoot, "package", "ff7.accessibility.reloaded");
            if (!Directory.Exists(packagePath))
            {
                throw new InvalidDataException("Runtime payload does not contain the dual-runtime mod package.");
            }

            var resultPath = System.IO.Path.Combine(stagingRoot, "deployment-result.json");
            var arguments = BuildDeploymentArguments(request.Preflight, request.Release, packagePath, resultPath);
            Report(progress, "Install", 60, "Installing Blind Swordsman into Final Fantasy VII and Reloaded-II.");
            var process = await processRunner.RunAsync(
                request.Resources.InstallScript,
                arguments,
                cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(process.StandardOutput))
            {
                log.Write(process.StandardOutput);
            }
            if (process.ExitCode != 0)
            {
                log.Write(process.StandardError);
                throw new InvalidOperationException(
                    $"Blind Swordsman deployment failed with exit code {process.ExitCode}: {LastUsefulLine(process.StandardError)}");
            }
            if (!File.Exists(resultPath))
            {
                throw new InvalidDataException("Deployment completed without producing install state.");
            }

            var state = DeploymentResultParser.Parse(
                await File.ReadAllTextAsync(resultPath, cancellationToken).ConfigureAwait(false));
            ValidateDeploymentResult(
                state,
                request.Release,
                request.Preflight.Game.GameRoot,
                request.Preflight.ReloadedRoot);
            Report(progress, "Register", 88, "Registering repair, update, and uninstall support.");
            stateStore.Save(state);
            CopyManagedSetup(request.CurrentSetupPath, paths.InstalledSetupPath);
            WindowsRegistration.Apply(WindowsRegistration.Build(
                state,
                paths.InstalledSetupPath,
                paths.StartMenuDirectory));
            Report(progress, "Complete", 100, "Blind Swordsman installation completed.");
            log.Write("Installation completed and Windows registration was written.");
            return state;
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }
        }
    }

    public async Task UninstallAsync(
        SetupResourcePaths resources,
        IProgress<SetupOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var state = stateStore.Load() ?? throw new InvalidOperationException("Blind Swordsman is not currently registered as installed.");
        var stagingRoot = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "blind-swordsman-uninstall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        try
        {
            var resultPath = System.IO.Path.Combine(stagingRoot, "uninstall-result.json");
            Report(progress, "Uninstall", 20, "Removing setup-owned Blind Swordsman files.");
            var process = await processRunner.RunAsync(
                resources.UninstallScript,
                ["-StatePath", stateStore.Path, "-ResultPath", resultPath],
                cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                log.Write(process.StandardError);
                throw new InvalidOperationException(
                    $"Blind Swordsman uninstall failed with exit code {process.ExitCode}: {LastUsefulLine(process.StandardError)}");
            }
            if (!File.Exists(resultPath))
            {
                throw new InvalidDataException("Uninstall did not produce a completion report.");
            }

            WindowsRegistration.Remove(paths.StartMenuDirectory);
            stateStore.Delete();
            WindowsRegistration.RemoveInstalledSetup(paths.InstalledSetupPath);
            Report(progress, "Complete", 100, "Blind Swordsman was uninstalled. Changed user files were preserved.");
            log.Write("Uninstall completed.");
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }
        }
    }

    private static void CopyManagedSetup(string sourcePath, string destinationPath)
    {
        var source = System.IO.Path.GetFullPath(sourcePath);
        var destination = System.IO.Path.GetFullPath(destinationPath);
        if (SamePath(source, destination))
        {
            return;
        }
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Running setup executable could not be copied for update support.", source);
        }

        var directory = System.IO.Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var directoryInfo = new DirectoryInfo(directory);
        if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Managed setup directory cannot be a reparse point.");
        }
        if (File.Exists(destination) && (new FileInfo(destination).Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Managed setup executable cannot replace a reparse point.");
        }

        var temporary = System.IO.Path.Combine(directory, ".Blind-Swordsman-Setup-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.Copy(source, temporary, overwrite: false);
            if (File.Exists(destination))
            {
                File.Replace(temporary, destination, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, destination);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(
            System.IO.Path.GetFullPath(left).TrimEnd(System.IO.Path.DirectorySeparatorChar),
            System.IO.Path.GetFullPath(right).TrimEnd(System.IO.Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static void Report(
        IProgress<SetupOperationProgress>? progress,
        string stage,
        int percent,
        string message) =>
        progress?.Report(new SetupOperationProgress(stage, Math.Clamp(percent, 0, 100), message));

    private static string LastUsefulLine(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()
        ?? "No error details were returned.";
}
