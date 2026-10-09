namespace Ff7.Accessibility.Core;

/// <summary>What an entry in the mod settings menu is.</summary>
public enum ModSettingKind
{
    /// <summary>On or off.</summary>
    Toggle,

    /// <summary>A percentage stepped between a minimum and a maximum.</summary>
    Level,

    /// <summary>One of a fixed set of percentages.</summary>
    Choice,

    /// <summary>Something done rather than a value; it asks for a second press first.</summary>
    Action,
}

/// <summary>When a change made in the menu reaches the running game, on one version of it.</summary>
public enum ModSettingEffect
{
    /// <summary>At once, both ways.</summary>
    Immediate,

    /// <summary>
    /// Off at once. On only after a restart, unless it was already on when the game started:
    /// what it needs (a sound player, a hook) is only created at startup when it is on.
    /// </summary>
    OffNowOnAfterRestart,

    /// <summary>
    /// Off at once. Turned on, it works only in part until a restart (speech without its
    /// sounds, say), unless it was already on when the game started.
    /// </summary>
    OffNowOnPartlyUntilRestart,

    /// <summary>
    /// Saved now and used from the next start. The running game keeps the value it started
    /// with, so it never runs half changed.
    /// </summary>
    AfterRestart,

    /// <summary>This version of the game does not use it; the menu leaves it alone.</summary>
    NotOnThisRuntime,
}

/// <summary>The version of the game the mod is running in.</summary>
public enum ModSettingsRuntime
{
    /// <summary>The legacy 32-bit game under Reloaded-II, with or without FFNx.</summary>
    Legacy,

    /// <summary>The Steam 2026 64-bit game.</summary>
    Steam2026,
}

/// <summary>
/// How an entry behaves on one version of the game, with a spoken note for anything the
/// effect alone would leave the player to discover.
/// </summary>
public readonly record struct ModSettingSupport(ModSettingEffect Effect, string? Note = null)
{
    public static implicit operator ModSettingSupport(ModSettingEffect effect) => new(effect);
}

/// <summary>One entry of the mod settings menu, labelled for the player.</summary>
public sealed class ModSettingDefinition
{
    private readonly Func<AccessibilityConfig, int>? read;
    private readonly Action<AccessibilityConfig, int>? write;
    private readonly ModSettingSupport legacy;
    private readonly ModSettingSupport steam;

    private ModSettingDefinition(
        string key,
        string category,
        string label,
        ModSettingKind kind,
        IReadOnlyList<int> values,
        Func<AccessibilityConfig, int>? read,
        Action<AccessibilityConfig, int>? write,
        ModSettingSupport legacy,
        ModSettingSupport steam,
        string? verb)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Key = key;
        Category = category;
        Label = label;
        Kind = kind;
        Values = values;
        this.read = read;
        this.write = write;
        this.legacy = legacy;
        this.steam = steam;
        Verb = verb;
    }

    /// <summary>The <see cref="AccessibilityConfig"/> property, and the key it is stored under.</summary>
    public string Key { get; }

    public string Category { get; }

    /// <summary>What the player hears; never the configuration name.</summary>
    public string Label { get; }

    public ModSettingKind Kind { get; }

    /// <summary>Every value the menu steps through, lowest first: 0 and 1 for a toggle.</summary>
    public IReadOnlyList<int> Values { get; }

    /// <summary>What an action does, as in "press I twice to reset".</summary>
    public string? Verb { get; }

    public ModSettingSupport SupportOn(ModSettingsRuntime runtime) =>
        runtime == ModSettingsRuntime.Legacy ? legacy : steam;

    public ModSettingEffect EffectOn(ModSettingsRuntime runtime) => SupportOn(runtime).Effect;

    /// <summary>
    /// Whether a value may be stored. A level takes anything within its range, so a hand-edited
    /// value between steps is kept; the menu itself only steps along the grid.
    /// </summary>
    public bool Accepts(int value) => Kind switch
    {
        ModSettingKind.Level => value >= Values[0] && value <= Values[^1],
        ModSettingKind.Toggle or ModSettingKind.Choice => Values.Contains(value),
        _ => false,
    };

    public int Read(AccessibilityConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return read is null ? throw new InvalidOperationException($"{Key} is an action, not a setting.") : read(config);
    }

    public void Write(AccessibilityConfig config, int value)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (write is null) throw new InvalidOperationException($"{Key} is an action, not a setting.");
        if (!Accepts(value)) throw new ArgumentOutOfRangeException(nameof(value), value, $"{Key} does not take {value}.");
        write(config, value);
    }

    /// <summary>The value as spoken: "on", "off" or "125 percent".</summary>
    public string Format(int value) => Kind switch
    {
        ModSettingKind.Toggle => value != 0 ? "on" : "off",
        ModSettingKind.Level => $"{Math.Clamp(value, Values[0], Values[^1])} percent",
        ModSettingKind.Choice => $"{value} percent",
        _ => string.Empty,
    };

    /// <summary>The next value up the grid, or the highest.</summary>
    public int StepUp(int value) =>
        Kind == ModSettingKind.Toggle ? 1 : Values.FirstOrDefault(candidate => candidate > value, Values[^1]);

    /// <summary>The next value down the grid, or the lowest.</summary>
    public int StepDown(int value) =>
        Kind == ModSettingKind.Toggle ? 0 : Values.LastOrDefault(candidate => candidate < value, Values[0]);

    public static ModSettingDefinition Toggle(
        string key,
        string category,
        string label,
        Func<AccessibilityConfig, bool> read,
        Action<AccessibilityConfig, bool> write,
        ModSettingSupport legacy,
        ModSettingSupport steam)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        return new ModSettingDefinition(
            key, category, label, ModSettingKind.Toggle, [0, 1],
            config => read(config) ? 1 : 0, (config, value) => write(config, value != 0),
            legacy, steam, null);
    }

    public static ModSettingDefinition Level(
        string key,
        string category,
        string label,
        Func<AccessibilityConfig, int> read,
        Action<AccessibilityConfig, int> write,
        int minimum,
        int maximum,
        int step,
        ModSettingSupport legacy,
        ModSettingSupport steam)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        if (step <= 0 || maximum <= minimum || (maximum - minimum) % step != 0)
            throw new ArgumentException("A level needs a whole number of steps between its minimum and maximum.", nameof(step));
        var values = Enumerable.Range(0, ((maximum - minimum) / step) + 1).Select(i => minimum + (i * step)).ToArray();
        return new ModSettingDefinition(key, category, label, ModSettingKind.Level, values, read, write, legacy, steam, null);
    }

    public static ModSettingDefinition Choice(
        string key,
        string category,
        string label,
        Func<AccessibilityConfig, int> read,
        Action<AccessibilityConfig, int> write,
        IReadOnlyList<int> choices,
        ModSettingSupport legacy,
        ModSettingSupport steam)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(choices);
        var values = choices.Distinct().Order().ToArray();
        if (values.Length < 2) throw new ArgumentException("A choice needs at least two values.", nameof(choices));
        return new ModSettingDefinition(key, category, label, ModSettingKind.Choice, values, read, write, legacy, steam, null);
    }

    /// <summary>An action, such as a reset: available on both versions, asks for a second press.</summary>
    public static ModSettingDefinition Action(string key, string category, string label, string verb = "reset")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verb);
        return new ModSettingDefinition(
            key, category, label, ModSettingKind.Action, [], null, null,
            ModSettingEffect.Immediate, ModSettingEffect.Immediate, verb);
    }
}

/// <summary>
/// Every player setting the mod has, in the order the menu reads them, each labelled and
/// grouped for the player and marked with how a change behaves on each version of the game.
///
/// <para>The effects record what the code does, from an audit of every read of each setting
/// in both runtimes (9 October 2026): a setting read on every tick is
/// <see cref="ModSettingEffect.Immediate"/>; one whose sound player, reader or hook is only
/// created at startup when it is on cannot be turned on mid-session; one copied into a
/// component at startup (the battle options snapshot, the Fort Condor tracker) applies after a
/// restart. The navigation progress and Highway auto-steering controllers follow the
/// configuration on every tick in both hosts, so they are immediate. When a runtime is
/// changed so a setting behaves differently, its entry here must change with it.
/// Diagnostics, hook switches, tuning, paths and research probes are not player settings
/// and are listed in <see cref="Excluded"/> instead.</para>
/// </summary>
public sealed class ModSettingsCatalogue
{
    /// <summary>The action that resets the save's battle descriptions.</summary>
    public const string ResetBattleDescriptionsKey = "ResetBattleDescriptions";

    private const string Descriptions = "Recorded descriptions";
    private const string Speech = "Speech";
    private const string Menus = "Menus";
    private const string Battle = "Battle";
    private const string Navigation = "Navigation";
    private const string Sounds = "Footsteps and sound cues";
    private const string Activities = "Minigames and activities";
    private const string GoldSaucer = "Gold Saucer";

    private const ModSettingEffect Now = ModSettingEffect.Immediate;
    private const ModSettingEffect OnNeedsRestart = ModSettingEffect.OffNowOnAfterRestart;
    private const ModSettingEffect OnPartly = ModSettingEffect.OffNowOnPartlyUntilRestart;
    private const ModSettingEffect Restart = ModSettingEffect.AfterRestart;
    private const ModSettingEffect NotHere = ModSettingEffect.NotOnThisRuntime;

    private readonly Dictionary<string, ModSettingDefinition> byKey;

    public ModSettingsCatalogue(IEnumerable<ModSettingDefinition> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = entries.ToArray();
        if (Entries.Count == 0) throw new ArgumentException("A catalogue needs at least one entry.", nameof(entries));
        byKey = new Dictionary<string, ModSettingDefinition>(StringComparer.Ordinal);
        foreach (var entry in Entries)
        {
            if (!byKey.TryAdd(entry.Key, entry))
                throw new ArgumentException($"{entry.Key} is listed twice.", nameof(entries));
        }
    }

    public IReadOnlyList<ModSettingDefinition> Entries { get; }

    public ModSettingDefinition? Find(string key) => byKey.GetValueOrDefault(key);

    public static ModSettingsCatalogue Default { get; } = new(CreateDefault());

    /// <summary>Every switch in <see cref="AccessibilityConfig"/> that is not a player setting, and why.</summary>
    public static IReadOnlyDictionary<string, string> Excluded { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(AccessibilityConfig.EnableTitleMenuVisualReader)] =
            "Superseded screen-capture title reader; the title screen menu setting covers the title menu.",
        [nameof(AccessibilityConfig.EnableFieldMessageWindowDiagnostics)] = "Diagnostic logging.",
        [nameof(AccessibilityConfig.EnableFieldMessageOpenHook)] = "Hook installation, not a player choice.",
        [nameof(AccessibilityConfig.EnableFieldMessageOpenDiagnostics)] = "Diagnostic logging.",
        [nameof(AccessibilityConfig.EnableFieldMessagePreviewHook)] = "Hook installation, not a player choice.",
        [nameof(AccessibilityConfig.EnableFieldMessagePreviewDiagnostics)] = "Diagnostic logging.",
        [nameof(AccessibilityConfig.EnableFieldOpcodeMessageHooks)] = "Hook installation, not a player choice.",
        [nameof(AccessibilityConfig.EnableFieldOpcodeMessageDiagnostics)] = "Diagnostic logging.",
        [nameof(AccessibilityConfig.EnableFieldCutsceneDescriptionDiagnostics)] = "Diagnostic logging.",
        [nameof(AccessibilityConfig.EnableSubmarineMissionDiagnostics)] = "Diagnostic logging.",
        [nameof(AccessibilityConfig.EnableSpeedSquareCoasterDiagnostics)] = "Diagnostic logging.",
        [nameof(AccessibilityConfig.EnableWonderSquareBasketballDiagnostics)] = "Diagnostic logging.",
        [nameof(AccessibilityConfig.EnableChocoboSquareDiagnostics)] = "Diagnostic logging.",
        [nameof(AccessibilityConfig.EnableWonderSquare3DBattlerDiagnostics)] = "Diagnostic logging.",
        [nameof(AccessibilityConfig.EnableNameEntryMenuDiagnostics)] = "Diagnostic logging.",
        [nameof(AccessibilityConfig.EnableFieldPositionDiagnostics)] = "Diagnostic logging.",
        [nameof(AccessibilityConfig.PlayFootstepProbeOnLoad)] = "Footstep measurement probe.",
        [nameof(AccessibilityConfig.EnableFieldFootstepDistanceProbe)] = "Footstep measurement probe.",
        [nameof(AccessibilityConfig.EnableFieldNavigationDiagnostics)] = "Diagnostic logging.",
        [nameof(AccessibilityConfig.EnableWorldMapNavigationDiagnostics)] = "Diagnostic logging.",
        [nameof(AccessibilityConfig.EnableExperimentalHooks)] =
            "Master switch for the native hooks the readers depend on; not a player choice.",
        [nameof(AccessibilityConfig.EnableMenuTextRenderDiagnostics)] =
            "Diagnostic logging; on the legacy runtime it also installs the hook that feeds on-screen menu text.",
        [nameof(AccessibilityConfig.EnableInGameMenuTextDrawDiagnostics)] = "Diagnostic logging.",
        [nameof(AccessibilityConfig.EnableInGameMenuTextDrawSpeech)] =
            "Experimental raw text-draw speech that would duplicate the menu readers.",
        [nameof(AccessibilityConfig.EnableCondorMinigameProbe)] =
            "Research probe that floods speech with raw diagnostics during Fort Condor.",
        [nameof(AccessibilityConfig.EnableTitleMenuNativeCursorDiagnostics)] = "Diagnostic logging.",
        [nameof(AccessibilityConfig.EnableMenuWidgetDiagnostics)] = "Diagnostic logging.",
        [nameof(AccessibilityConfig.EnableBattleDiagnostics)] = "Diagnostic logging.",
    };

    private static IEnumerable<ModSettingDefinition> CreateDefault()
    {
        // Recorded descriptions.
        yield return ModSettingDefinition.Level(
            nameof(AccessibilityConfig.AudioDescriptionVolumePercent), Descriptions, "Description volume",
            c => c.AudioDescriptionVolumePercent, (c, v) => c.AudioDescriptionVolumePercent = v,
            AudioDescriptionLevel.MinimumPercent, AudioDescriptionLevel.MaximumPercent, AudioDescriptionLevel.StepPercent,
            Now, Now);
        yield return ModSettingDefinition.Action(
            ResetBattleDescriptionsKey, Descriptions, "Reset battle descriptions for this save");
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableBattleAnimationDescriptions), Descriptions, "Limit break and summon descriptions",
            c => c.EnableBattleAnimationDescriptions, (c, v) => c.EnableBattleAnimationDescriptions = v, Now, Now);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableFieldCutsceneDescriptions), Descriptions, "Scene descriptions",
            c => c.EnableFieldCutsceneDescriptions, (c, v) => c.EnableFieldCutsceneDescriptions = v, Now, Restart);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableFieldMovieNarrationTracks), Descriptions, "Recorded film narration",
            c => c.EnableFieldMovieNarrationTracks, (c, v) => c.EnableFieldMovieNarrationTracks = v,
            new(Now, "When it is off, films are described by the screen reader."),
            new(Now, "When it is off, films are described by the screen reader."));
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableOpeningMovieAudioTrack), Descriptions, "Recorded opening film narration",
            c => c.EnableOpeningMovieAudioTrack, (c, v) => c.EnableOpeningMovieAudioTrack = v, Now, Now);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableOpeningMovieDescription), Descriptions, "Opening film description by the screen reader",
            c => c.EnableOpeningMovieDescription, (c, v) => c.EnableOpeningMovieDescription = v,
            new(Now, "It plays alongside the recorded opening narration when both are on."),
            new(Now, "It plays alongside the recorded opening narration when both are on."));

        // Speech.
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableSpeech), Speech, "Mod speech",
            c => c.EnableSpeech, (c, v) => c.EnableSpeech = v,
            new(Now, "Recorded scene and film descriptions have their own settings and keep playing."),
            new(Now, "Settings stays audible. Recorded scene and film descriptions have their own settings and keep playing."));
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableRuntimeDialogueSpeech), Speech, "Dialogue speech",
            c => c.EnableRuntimeDialogueSpeech, (c, v) => c.EnableRuntimeDialogueSpeech = v,
            new(Now, "In this version of the game it covers world map dialogue; field dialogue has its own settings."),
            Now);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableFieldMessageReader), Speech, "Field message reader",
            c => c.EnableFieldMessageReader, (c, v) => c.EnableFieldMessageReader = v,
            Now,
            new(Now, "In this version of the game it controls location name speech."));
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.SpeakFieldMessages), Speech, "Speak field messages and questions",
            c => c.SpeakFieldMessages, (c, v) => c.SpeakFieldMessages = v, Now, NotHere);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableFieldDialogueDrawSpeech), Speech, "Dialogue text as it is drawn",
            c => c.EnableFieldDialogueDrawSpeech, (c, v) => c.EnableFieldDialogueDrawSpeech = v,
            OnNeedsRestart,
            new(Restart, "In this version of the game it only covers the name entry prompt."));
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableNameEntryMenuSpeech), Speech, "Name entry screen",
            c => c.EnableNameEntryMenuSpeech, (c, v) => c.EnableNameEntryMenuSpeech = v, Now, Restart);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableFfnxPopupSpeech), Speech, "FFNx notices",
            c => c.EnableFfnxPopupSpeech, (c, v) => c.EnableFfnxPopupSpeech = v, Now, NotHere);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.SpeakOnLoad), Speech, "Announce when the mod loads",
            c => c.SpeakOnLoad, (c, v) => c.SpeakOnLoad = v, Restart, NotHere);

        // Menus.
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableRuntimeMenuSpeech), Menus, "Main menu speech",
            c => c.EnableRuntimeMenuSpeech, (c, v) => c.EnableRuntimeMenuSpeech = v,
            NotHere,
            new(Now, "It covers the main menu's own choices and the gil line; other menus have their own settings."));
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableMainMenuReader), Menus, "Main menu reader",
            c => c.EnableMainMenuReader, (c, v) => c.EnableMainMenuReader = v, Now, NotHere);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.SpeakMainMenuSelections), Menus, "Speak main menu selections",
            c => c.SpeakMainMenuSelections, (c, v) => c.SpeakMainMenuSelections = v, Now, NotHere);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableInGameMenuWidgetSpeech), Menus, "Menu option speech",
            c => c.EnableInGameMenuWidgetSpeech, (c, v) => c.EnableInGameMenuWidgetSpeech = v,
            new(Now, "In this version of the game it controls save screen speech; other menus are read either way."),
            new(Now, "In this version of the game it only affects shop menus, which go quiet only when menu help text is off too."));
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableInGameMenuHelpTextSpeech), Menus, "Menu help text",
            c => c.EnableInGameMenuHelpTextSpeech, (c, v) => c.EnableInGameMenuHelpTextSpeech = v,
            new(Now, "In this version of the game help text is read with the menus whatever this is set to."),
            new(Now, "In this version of the game it only affects shop menus, which go quiet only when menu option speech is off too."));
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableRenderedMenuTextSpeech), Menus, "On-screen menu text",
            c => c.EnableRenderedMenuTextSpeech, (c, v) => c.EnableRenderedMenuTextSpeech = v, OnNeedsRestart, NotHere);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableTitleMenuNativeCursorSpeech), Menus, "Title screen menu",
            c => c.EnableTitleMenuNativeCursorSpeech, (c, v) => c.EnableTitleMenuNativeCursorSpeech = v, OnNeedsRestart, NotHere);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableTitleLoadMenuSpeech), Menus, "Load game screen",
            c => c.EnableTitleLoadMenuSpeech, (c, v) => c.EnableTitleLoadMenuSpeech = v, Now, Now);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableNativeSystemMenuSpeech), Menus, "System menu speech",
            c => c.EnableNativeSystemMenuSpeech, (c, v) => c.EnableNativeSystemMenuSpeech = v, NotHere, Restart);

        // Battle. The Steam runtime snapshots these once per session; on the legacy runtime the
        // menu, message and results hooks are installed only for a switch that is on at startup.
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableBattleMenuSpeech), Battle, "Battle menus",
            c => c.EnableBattleMenuSpeech, (c, v) => c.EnableBattleMenuSpeech = v, Restart, Restart);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableBattleTargetSpeech), Battle, "Battle targets",
            c => c.EnableBattleTargetSpeech, (c, v) => c.EnableBattleTargetSpeech = v, OnNeedsRestart, Restart);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableBattleMessageSpeech), Battle, "Battle messages",
            c => c.EnableBattleMessageSpeech, (c, v) => c.EnableBattleMessageSpeech = v, Restart, Restart);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableBattleDamageSpeech), Battle, "Damage and healing",
            c => c.EnableBattleDamageSpeech, (c, v) => c.EnableBattleDamageSpeech = v, OnNeedsRestart, Restart);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableBattleStatusSpeech), Battle, "Status changes",
            c => c.EnableBattleStatusSpeech, (c, v) => c.EnableBattleStatusSpeech = v, OnNeedsRestart, Restart);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableBattleEnemyActionSpeech), Battle, "Enemy actions",
            c => c.EnableBattleEnemyActionSpeech, (c, v) => c.EnableBattleEnemyActionSpeech = v, OnNeedsRestart, Restart);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableBattleEncounterSpeech), Battle, "Enemies at the start of battle",
            c => c.EnableBattleEncounterSpeech, (c, v) => c.EnableBattleEncounterSpeech = v, OnNeedsRestart, Restart);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableBattleResultsSpeech), Battle, "Battle results",
            c => c.EnableBattleResultsSpeech, (c, v) => c.EnableBattleResultsSpeech = v, Restart, Restart);

        // Navigation. Both hosts push the configuration into the progress controller every
        // tick, and F5, F6 and F7 record what they change through the store.
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableFieldNavigationAssistant), Navigation, "Field navigation",
            c => c.EnableFieldNavigationAssistant, (c, v) => c.EnableFieldNavigationAssistant = v, Now, Now);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableWorldMapNavigationAssistant), Navigation, "World map navigation",
            c => c.EnableWorldMapNavigationAssistant, (c, v) => c.EnableWorldMapNavigationAssistant = v, OnPartly, OnPartly);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableNavigationProgressIndicators), Navigation, "Navigation progress",
            c => c.EnableNavigationProgressIndicators, (c, v) => c.EnableNavigationProgressIndicators = v,
            new(Now, "F5 also switches it."),
            new(Now, "F5 also switches it."));
        yield return ModSettingDefinition.Choice(
            nameof(AccessibilityConfig.NavigationProgressIntervalPercent), Navigation, "Navigation progress interval",
            c => c.NavigationProgressIntervalPercent, (c, v) => c.NavigationProgressIntervalPercent = v,
            [5, 10, 15, 20],
            new(Now, "F6 and F7 also change it."),
            new(Now, "F6 and F7 also change it."));

        // Footsteps and sound cues.
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableFieldFootstepFeedback), Sounds, "Field footsteps",
            c => c.EnableFieldFootstepFeedback, (c, v) => c.EnableFieldFootstepFeedback = v, Now, Now);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableWorldMapFootstepFeedback), Sounds, "World map footsteps",
            c => c.EnableWorldMapFootstepFeedback, (c, v) => c.EnableWorldMapFootstepFeedback = v,
            new(Now, "They use the Cosmo footstep sounds, so they are silent while those are off."),
            new(Now, "They use the Cosmo footstep sounds, so they are silent while those are off."));
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.UseCosmoFootstepSounds), Sounds, "Cosmo footstep sounds",
            c => c.UseCosmoFootstepSounds, (c, v) => c.UseCosmoFootstepSounds = v,
            new(Restart, "World map footsteps need them; without them field footsteps use one standard sound."),
            new(Restart, "World map footsteps need them; without them field footsteps use one standard sound."));
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableFieldExitProximityCues), Sounds, "Exit sounds",
            c => c.EnableFieldExitProximityCues, (c, v) => c.EnableFieldExitProximityCues = v, OnNeedsRestart, Restart);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableFieldLadderProximityCues), Sounds, "Ladder sounds",
            c => c.EnableFieldLadderProximityCues, (c, v) => c.EnableFieldLadderProximityCues = v, OnNeedsRestart, Restart);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableFieldObjectProximityCues), Sounds, "Object sounds",
            c => c.EnableFieldObjectProximityCues, (c, v) => c.EnableFieldObjectProximityCues = v, OnNeedsRestart, OnNeedsRestart);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableWorldMapEntranceProximityCues), Sounds, "World map entrance sounds",
            c => c.EnableWorldMapEntranceProximityCues, (c, v) => c.EnableWorldMapEntranceProximityCues = v,
            OnNeedsRestart, OnNeedsRestart);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableFieldZoneTransitionCue), Sounds, "Room change tone",
            c => c.EnableFieldZoneTransitionCue, (c, v) => c.EnableFieldZoneTransitionCue = v, OnNeedsRestart, OnNeedsRestart);

        // Minigames and activities.
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableFieldActivityReadout), Activities, "Field activity readouts",
            c => c.EnableFieldActivityReadout, (c, v) => c.EnableFieldActivityReadout = v, OnPartly, OnPartly);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableFieldSwingingBarTimingCue), Activities, "Swinging bar jump tone",
            c => c.EnableFieldSwingingBarTimingCue, (c, v) => c.EnableFieldSwingingBarTimingCue = v, OnPartly, OnPartly);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableReactor5ButtonCue), Activities, "Reactor 5 button tone",
            c => c.EnableReactor5ButtonCue, (c, v) => c.EnableReactor5ButtonCue = v, OnNeedsRestart, NotHere);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableSquatMinigamePrompts), Activities, "Squats contest prompts",
            c => c.EnableSquatMinigamePrompts, (c, v) => c.EnableSquatMinigamePrompts = v, Now, Now);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableJunonMinigamePrompts), Activities, "Junon minigame prompts",
            c => c.EnableJunonMinigamePrompts, (c, v) => c.EnableJunonMinigamePrompts = v, OnPartly, OnPartly);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableJunonTimingCue), Activities, "Junon timing tone",
            c => c.EnableJunonTimingCue, (c, v) => c.EnableJunonTimingCue = v, OnPartly, OnPartly);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableJunonParadeAlignmentAssist), Activities, "Junon parade alignment help",
            c => c.EnableJunonParadeAlignmentAssist, (c, v) => c.EnableJunonParadeAlignmentAssist = v, Now, Now);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableFloor60SoldierTurnCue), Activities, "Shinra Building sneaking cues",
            c => c.EnableFloor60SoldierTurnCue, (c, v) => c.EnableFloor60SoldierTurnCue = v, OnPartly, OnPartly);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableHighwayAccessibility), Activities, "Highway chase assistance",
            c => c.EnableHighwayAccessibility, (c, v) => c.EnableHighwayAccessibility = v, OnPartly, OnPartly);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableHighwayAutoSteering), Activities, "Highway automatic steering",
            c => c.EnableHighwayAutoSteering, (c, v) => c.EnableHighwayAutoSteering = v,
            new(Now, "F8 also switches it during the chase."),
            new(Now, "F8 also switches it during the chase."));
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableHighwaySteeringGuidance), Activities, "Highway steering tones",
            c => c.EnableHighwaySteeringGuidance, (c, v) => c.EnableHighwaySteeringGuidance = v, OnNeedsRestart, OnNeedsRestart);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableCondorBattleLineAnnouncements), Activities, "Fort Condor battle line",
            c => c.EnableCondorBattleLineAnnouncements, (c, v) => c.EnableCondorBattleLineAnnouncements = v, Restart, Restart);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableCondorEnemyArrivalAnnouncements), Activities, "Fort Condor enemy arrivals",
            c => c.EnableCondorEnemyArrivalAnnouncements, (c, v) => c.EnableCondorEnemyArrivalAnnouncements = v, Restart, Restart);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableSubmarineMissionReadout), Activities, "Submarine mission readout",
            c => c.EnableSubmarineMissionReadout, (c, v) => c.EnableSubmarineMissionReadout = v, OnPartly, OnPartly);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableSnowboardReadout), Activities, "Snowboard readout",
            c => c.EnableSnowboardReadout, (c, v) => c.EnableSnowboardReadout = v, Now, Now);

        // Gold Saucer.
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableSpeedSquareCoasterReadout), GoldSaucer, "Shooting coaster readout",
            c => c.EnableSpeedSquareCoasterReadout, (c, v) => c.EnableSpeedSquareCoasterReadout = v, Now, Now);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableSpeedSquareCoasterTargetCues), GoldSaucer, "Shooting coaster target sounds",
            c => c.EnableSpeedSquareCoasterTargetCues, (c, v) => c.EnableSpeedSquareCoasterTargetCues = v, OnPartly, OnPartly);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableWonderSquareBasketballCues), GoldSaucer, "Basketball cues",
            c => c.EnableWonderSquareBasketballCues, (c, v) => c.EnableWonderSquareBasketballCues = v, OnPartly, OnPartly);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableWonderSquareArmWrestlingCues), GoldSaucer, "Arm wrestling cues",
            c => c.EnableWonderSquareArmWrestlingCues, (c, v) => c.EnableWonderSquareArmWrestlingCues = v, OnPartly, OnPartly);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableWonderSquare3DBattlerCues), GoldSaucer, "3D Battler cues",
            c => c.EnableWonderSquare3DBattlerCues, (c, v) => c.EnableWonderSquare3DBattlerCues = v, Now, Now);
        yield return ModSettingDefinition.Toggle(
            nameof(AccessibilityConfig.EnableChocoboSquareReadout), GoldSaucer, "Chocobo racing readout",
            c => c.EnableChocoboSquareReadout, (c, v) => c.EnableChocoboSquareReadout = v, Now, Now);
    }
}
