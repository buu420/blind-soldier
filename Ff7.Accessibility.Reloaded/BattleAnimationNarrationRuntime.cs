using System.Text.RegularExpressions;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Speaks the attack name a summon's banner shows, each time it is shown. The banner is
/// ordinary battle text a sighted player reads on every cast, so this is not tied to the
/// once-per-save descriptions. The name is the one the reader resolved from the game's
/// own loaded text while the banner is on screen; where that text is not in the native
/// blob (the legacy process under FFNx), the same kernel entry is looked up by the
/// banner's own action instead.
/// </summary>
public sealed class BattleSummonTitleSpeech
{
    private (byte EventIndex, byte Attacker, byte Command, byte Effect, ushort Action)? row;
    private readonly HashSet<string> spoken = new(StringComparer.Ordinal);
    private readonly Func<ushort, string?>? resolveSummonBannerText;

    /// <param name="resolveSummonBannerText">
    /// The kernel's name for a summon banner's action, used when the reader could not
    /// read the loaded text; null to speak only what the reader resolved.
    /// </param>
    public BattleSummonTitleSpeech(Func<ushort, string?>? resolveSummonBannerText = null)
    {
        this.resolveSummonBannerText = resolveSummonBannerText;
    }

    /// <summary>
    /// One scan; null when it could not be read, which changes nothing. Returns the name
    /// spoken on this scan, if any. A name the speaker refused is offered again while the
    /// banner still shows, and is never spoken after it has gone.
    /// </summary>
    public string? Update(BattleAnimationObservation? observation, Func<string, bool> speak)
    {
        if (observation is not { } seen)
        {
            return null;
        }

        if (!seen.InBattle || !seen.HasRow)
        {
            Reset();
            return null;
        }

        var current = (seen.EventIndex, seen.Attacker, seen.Command, seen.Effect, seen.Action);
        if (row != current)
        {
            row = current;
            spoken.Clear();
        }

        if (!seen.BannerVisible || seen.Command != BattleAnimationIdentities.SummonCommand ||
            TitleOf(seen) is not { Length: > 0 } title || spoken.Contains(title) || !speak(title))
        {
            return null;
        }

        spoken.Add(title);
        return title;
    }

    public void Reset()
    {
        row = null;
        spoken.Clear();
    }

    private string? TitleOf(BattleAnimationObservation seen)
    {
        if (seen.BannerText is { Length: > 0 } read)
        {
            return read;
        }

        if (seen.BannerCommand != BattleAnimationIdentities.SummonCommand || resolveSummonBannerText is null)
        {
            return null;
        }

        try
        {
            return resolveSummonBannerText(seen.BannerAction)?.Trim();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        {
            return null;
        }
    }
}

/// <summary>Shared worker-loop host for the two game runtimes.</summary>
internal sealed class BattleAnimationNarrationRuntime : IDisposable
{
    internal const string CatalogRelativePath = "Assets/cutscene-voice/battle-animations.json";
    private readonly BattleAnimationStateReader reader;
    private readonly BattleAnimationNarrationCoordinator? narration;
    private readonly BattleSummonTitleSpeech titles;
    private readonly Func<string, bool> speak;
    private readonly Action<string> log;
    private string? lastFault;

    private BattleAnimationNarrationRuntime(
        BattleAnimationStateReader reader,
        BattleAnimationNarrationCoordinator? narration,
        Func<string, bool> speak,
        Action<string> log,
        Kernel2TextDatabase? kernelText)
    {
        this.reader = reader;
        this.narration = narration;
        this.speak = speak;
        this.log = log;
        titles = new BattleSummonTitleSpeech(
            kernelText is null ? null : action => kernelText.ResolveSummonBannerText(action));
    }

    /// <summary>
    /// The battle animation host. The summon banner's name is spoken even when the
    /// descriptions cannot be loaded; only a failure to build the reader returns null.
    /// </summary>
    internal static BattleAnimationNarrationRuntime? Create(
        ILegacyAddressSpace memory,
        FieldAreaDescriptionHistory history,
        AccessibilityConfig config,
        string modDirectory,
        Action<string> log,
        Func<string, bool> speak,
        Func<bool> anotherDescriptionIsPlaying,
        Func<bool?>? speechIsPlaying = null,
        Kernel2TextDatabase? kernelText = null)
    {
        try
        {
            return new BattleAnimationNarrationRuntime(
                new BattleAnimationStateReader(memory),
                CreateNarration(history, config, modDirectory, log, speak, anotherDescriptionIsPlaying, speechIsPlaying),
                speak,
                log,
                kernelText);
        }
        catch (Exception ex)
        {
            log($"Battle animation reader could not be created: {ex.Message}");
            return null;
        }
    }

    private static BattleAnimationNarrationCoordinator? CreateNarration(
        FieldAreaDescriptionHistory history,
        AccessibilityConfig config,
        string modDirectory,
        Action<string> log,
        Func<string, bool> speak,
        Func<bool> anotherDescriptionIsPlaying,
        Func<bool?>? speechIsPlaying)
    {
        try
        {
            var catalog = BattleAnimationNarrationCatalog.Parse(
                File.ReadAllText(Path.Combine(modDirectory, CatalogRelativePath)), log);
            var voiceDirectory = Path.Combine(modDirectory, "Assets", "cutscene-voice");
            var voice = CutsceneVoiceManifest.Empty;
            try
            {
                voice = CutsceneVoiceManifest.Parse(
                    File.ReadAllText(Path.Combine(voiceDirectory, "manifest.json")), log);
            }
            catch (IOException ex)
            {
                log($"Battle narration voice manifest unavailable; descriptions use speech: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                log($"Battle narration voice manifest unreadable; descriptions use speech: {ex.Message}");
            }
            if (catalog.Count == 0)
            {
                log("Battle animation descriptions unavailable: the installed catalog is empty.");
                return null;
            }

            if (DetectBattleFrameMultiplier(log) is not { } ticksPerCueFrame)
            {
                return null;
            }

            var coordinator = new BattleAnimationNarrationCoordinator(
                catalog, history, voice,
                clip =>
                {
                    var path = Path.Combine(voiceDirectory, clip.FileName);
                    return File.Exists(path)
                        ? new OpeningMovieAudioTrackPlayer(path,
                            config.FieldMovieNarrationTrackVolumePercent, log, "Battle description")
                        : null;
                },
                log, speak, anotherDescriptionIsPlaying, ticksPerCueFrame, speechIsPlaying);
            log($"Battle animation descriptions ready: {catalog.Count} animations" +
                (catalog.SummonOpening is null ? string.Empty : " and the summon opening") + ", once per save.");
            return coordinator;
        }
        catch (Exception ex)
        {
            log($"Battle animation descriptions could not be loaded: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// FFNx's battle frame multiplier for its <c>ff7_fps_limiter</c> setting: 0 (original)
    /// and 1 (default) keep the vanilla 15 battle ticks a second, 2 runs battle at 30 and 3
    /// at 60, and FFNx then multiplies every animation-script wait by 2 or 4
    /// (battle/animations.cpp: <c>script_wait_frames * battle_frame_multiplier</c>).
    /// No config means no FFNx, which is vanilla. Any other value is refused: timing it as
    /// 15 a second would speak two or four times early.
    /// </summary>
    internal static int? BattleFrameMultiplierFromFfnxConfig(string? toml)
    {
        if (toml is null)
        {
            return 1;
        }

        var setting = Regex.Match(toml, @"^[ \t]*ff7_fps_limiter[ \t]*=[ \t]*(-?\d+)", RegexOptions.Multiline);
        if (!setting.Success)
        {
            return 1;
        }

        return setting.Groups[1].Value switch
        {
            "0" or "1" => 1,
            "2" => 2,
            "3" => 4,
            _ => null,
        };
    }

    /// <summary>
    /// FFNx drives only the legacy 32-bit executable and reads FFNx.toml beside it. The x64
    /// runtime is a translation FFNx does not patch, so it keeps the vanilla rate.
    /// </summary>
    private static int? DetectBattleFrameMultiplier(Action<string> log)
    {
        if (Environment.Is64BitProcess)
        {
            return 1;
        }

        string? toml = null;
        var directory = Path.GetDirectoryName(Environment.ProcessPath);
        var path = directory is null ? null : Path.Combine(directory, "FFNx.toml");
        try
        {
            if (path is not null && File.Exists(path))
            {
                toml = File.ReadAllText(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"Battle animation descriptions unavailable: FFNx.toml could not be read to learn the battle frame rate: {ex.Message}");
            return null;
        }

        var multiplier = BattleFrameMultiplierFromFfnxConfig(toml);
        log(multiplier is { } known
            ? $"Battle animation cue ticks: {known} native battle tick(s) per authored tick ({(toml is null ? "no FFNx.toml" : path)})."
            : $"Battle animation descriptions unavailable: FFNx.toml at {path} sets an unknown ff7_fps_limiter, so the battle frame rate is unknown.");
        return multiplier;
    }

    internal bool IsPlaying => narration?.IsPlaying == true;

    internal void Tick(DateTime nowUtc, bool enabledAndFocused) =>
        Tick(nowUtc, enabledAndFocused, titlesEnabledAndFocused: false);

    /// <param name="enabledAndFocused">The descriptions may play.</param>
    /// <param name="titlesEnabledAndFocused">The summon banner's name may be spoken.</param>
    internal void Tick(DateTime nowUtc, bool enabledAndFocused, bool titlesEnabledAndFocused)
    {
        try
        {
            if (!enabledAndFocused)
            {
                narration?.Stop("disabled or game not in the foreground");
            }

            if (!titlesEnabledAndFocused)
            {
                titles.Reset();
            }

            if (!enabledAndFocused && !titlesEnabledAndFocused)
            {
                return;
            }

            BattleAnimationObservation? observation = reader.TryRead(out var seen) ? seen : null;
            if (titlesEnabledAndFocused && titles.Update(observation, speak) is { } title)
            {
                log($"Summon banner spoken: {title}.");
            }

            if (enabledAndFocused)
            {
                narration?.Update(observation, nowUtc);
            }

            lastFault = null;
        }
        catch (Exception ex)
        {
            narration?.Stop("battle observation failed");
            titles.Reset();
            if (lastFault != ex.Message)
                log($"Battle narration observation failed: {ex.Message}");
            lastFault = ex.Message;
        }
    }

    public void Dispose() => narration?.Dispose();
}
