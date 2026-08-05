using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64.Runtime;
using Reloaded.Hooks.Definitions;
using Reloaded.Mod.Interfaces.Internal;

namespace Ff7.Accessibility.Steam2026X64;

public sealed class Mod : IModV1, IModV2
{
    private string modDirectory = AppContext.BaseDirectory;
    private string logPath = Path.Combine(
        AppContext.BaseDirectory,
        "ff7_accessibility_steam2026_x64.log");
    private ILoggerV2? logger;
    private Steam2026X64RuntimeBackend? backend;
    private Steam2026ResearchSession? researchSession;
    private BlindSoldierRuntimeLease? runtimeLease;
    private int started;

    public Action Disposing { get; } = () => { };

    public void Start(IModLoaderV1 loader)
    {
        ConfigureLogPath(loader, null);
        logger = loader.GetLogger() as ILoggerV2;
        StartNativeResearch(loader);
    }

    public void StartEx(IModLoaderV1 loader, IModConfigV1 modConfig)
    {
        ConfigureLogPath(loader, modConfig);
        logger = loader.GetLogger() as ILoggerV2;
        StartNativeResearch(loader);
    }

    public void Suspend() => researchSession?.Suspend();

    public void Resume() => researchSession?.Resume();

    public void Unload()
    {
        try
        {
            researchSession?.Dispose();
            researchSession = null;
            backend?.Dispose();
            backend = null;
        }
        finally
        {
            ReleaseRuntimeOwnership();
        }
    }

    public bool CanUnload() => true;

    public bool CanSuspend() => true;

    private void StartNativeResearch(IModLoaderV1 loader)
    {
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            return;
        }

        if (!BlindSoldierRuntimeLease.TryAcquire(Environment.ProcessId, out runtimeLease))
        {
            Log("Another Blind Soldier runtime already owns accessibility output for this process; this duplicate instance will remain inactive.");
            return;
        }

        try
        {
            var mainModule = Process.GetCurrentProcess().MainModule
                             ?? throw new InvalidOperationException(
                                 "The current executable module is unavailable.");
            var executablePath = mainModule.FileName;
            var fingerprint = Steam2026Fingerprint.Inspect(executablePath);
            Log(fingerprint.Diagnostic);
            if (!fingerprint.IsSupported)
            {
                Log("No native backend was constructed because the current executable is unsupported.");
                ReleaseRuntimeOwnership();
                return;
            }

            backend = new Steam2026X64RuntimeBackend(fingerprint);
            var report = backend.ValidateCapabilities();
            Log($"Native Steam 2026 parity available={report.Available}; failures={report.Failures.Length}.");
            foreach (var failure in report.Failures)
            {
                Log($"Missing {failure.Capability}: {failure.Signal}: {failure.Diagnostic}");
            }

            IReloadedHooks? hooks = null;
            try
            {
                var controller = loader.GetController<IReloadedHooks>();
                if (controller.TryGetTarget(out var target))
                {
                    hooks = target;
                    Log("Reloaded.Hooks controller acquired for the native x64 research path.");
                }
                else
                {
                    Log("Reloaded.Hooks is unavailable; polling speech will run without title callbacks.");
                }
            }
            catch (Exception ex)
            {
                Log($"Reloaded.Hooks acquisition failed; polling speech will continue: {ex.Message}");
            }

            var moduleBase = (ulong)(nuint)mainModule.BaseAddress;
            var moduleImageSize = checked((ulong)mainModule.ModuleMemorySize);
            var config = LoadAccessibilityConfig();
            var executableDirectory = Path.GetDirectoryName(executablePath)
                ?? throw new InvalidOperationException("The native executable directory is unavailable.");
            var gameWorkingDirectory = Path.GetFullPath(Path.Combine(
                executableDirectory,
                "ff7",
                "workingdir"));
            var openingMoviePath = Path.GetFullPath(Path.Combine(
                gameWorkingDirectory,
                "data",
                "movies",
                "opening.avi"));
            Log($"Native Steam 2026 legacy data root: {gameWorkingDirectory}");
            Log($"Native Steam 2026 opening movie identity: {openingMoviePath}");
            researchSession = new Steam2026ResearchSession(
                fingerprint,
                moduleBase,
                moduleImageSize,
                new CurrentProcessNativeMemoryReader(),
                hooks,
                config,
                modDirectory,
                gameWorkingDirectory,
                openingMoviePath,
                Log);
            researchSession.Start();
            Log(
                "Activated the exact-fingerprint native x64 research path for " +
                "Prism startup, title and in-game menu selections, ordinary field dialogue, " +
                "field footsteps, cutscene descriptions, spatial item cues, native battle menus, " +
                "and synchronized opening-movie audio description. " +
                "The production parity capability report remains fail-closed.");
        }
        catch (Exception ex)
        {
            researchSession?.Dispose();
            researchSession = null;
            backend?.Dispose();
            backend = null;
            ReleaseRuntimeOwnership();
            Log($"Native Steam 2026 research bootstrap remained fail-closed: {ex}");
        }
    }

    private void ReleaseRuntimeOwnership()
    {
        Interlocked.Exchange(ref runtimeLease, null)?.Dispose();
        Volatile.Write(ref started, 0);
    }

    private void Log(string message)
    {
        logger?.WriteLine($"[FFVII Accessibility x64] {message}", Color.LightBlue);
        try
        {
            File.AppendAllText(logPath, $"{DateTime.Now:u} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never destabilize the game process.
        }
    }

    private void ConfigureLogPath(IModLoaderV1 loader, IModConfigV1? modConfig)
    {
        try
        {
            if (loader is IModLoaderV2 loaderV2)
            {
                modDirectory = loaderV2.GetDirectoryForModId(
                    modConfig?.ModId ?? "ff7.accessibility.reloaded");
                logPath = Path.Combine(
                    modDirectory,
                    "ff7_accessibility_steam2026_x64.log");
            }
        }
        catch
        {
            // AppContext.BaseDirectory remains the safe fallback.
        }
    }

    private AccessibilityConfig LoadAccessibilityConfig()
    {
        var config = new AccessibilityConfig();
        try
        {
            var configPath = Path.Combine(modDirectory, "Configuration", "config.json");
            if (File.Exists(configPath))
            {
                config = JsonSerializer.Deserialize<AccessibilityConfig>(
                             File.ReadAllText(configPath))
                         ?? config;
                Log($"Loaded native Steam 2026 accessibility config: {configPath}");
            }
            else
            {
                Log($"Native Steam 2026 accessibility config is missing; using defaults: {configPath}");
            }
        }
        catch (Exception ex)
        {
            Log($"Could not load native Steam 2026 accessibility config; using defaults: {ex.Message}");
            config = new AccessibilityConfig();
        }

        var configuredTrack = string.IsNullOrWhiteSpace(config.OpeningMovieAudioTrackPath)
            ? @"Assets\movies\opening_audio_description.ogg"
            : config.OpeningMovieAudioTrackPath;
        config.OpeningMovieAudioTrackPath = Path.GetFullPath(
            Path.IsPathRooted(configuredTrack)
                ? configuredTrack
                : Path.Combine(modDirectory, configuredTrack));
        Log($"Native Steam 2026 opening narration track: {config.OpeningMovieAudioTrackPath}");
        return config;
    }
}
