namespace Ff7.Accessibility.Core;

/// <summary>A command to the mod settings menu, from either input device.</summary>
public enum ModSettingsMenuCommand
{
    /// <summary>J, or D-pad Up.</summary>
    Previous,

    /// <summary>L, or D-pad Down.</summary>
    Next,

    /// <summary>U, or D-pad Left.</summary>
    Decrease,

    /// <summary>O, or D-pad Right.</summary>
    Increase,

    /// <summary>I, or A.</summary>
    Activate,

    /// <summary>K.</summary>
    Repeat,
}

/// <summary>Which controls the menu names when it opens and in its prompts.</summary>
public enum ModSettingsInputDevice
{
    Keyboard,
    Controller,
}

/// <summary>
/// The spoken mod settings menu: one entry at a time from the
/// <see cref="ModSettingsCatalogue"/>, each read with its value, its place and, where a change
/// would not simply apply at once, what it needs - a restart, or a version of the game that
/// uses it. Every command returns what to say, and nothing here speaks or reads input: the
/// host speaks the text even while mod speech is off, and maps keys and buttons to commands.
///
/// <para>Changes go through <see cref="PlayerSettingsStore"/>, so they are saved as they are
/// made and a save that fails is said, not hidden. An action such as the battle description
/// reset is carried out only on a second activation within
/// <see cref="ConfirmationWindow"/>; anything else in between cancels it.</para>
/// </summary>
public sealed class ModSettingsMenu
{
    /// <summary>How long a first activation of an action waits for its confirmation.</summary>
    public static readonly TimeSpan ConfirmationWindow = TimeSpan.FromSeconds(30);

    private readonly AccessibilityConfig config;
    private readonly PlayerSettingsStore store;
    private readonly Func<string> resetBattleDescriptions;
    private readonly Func<DateTime> utcNow;
    private readonly IReadOnlyList<ModSettingDefinition> entries;
    private readonly object sync = new();
    private int index;
    private bool open;
    private ModSettingsInputDevice device;
    private DateTime? armedAtUtc;

    /// <param name="config">The configuration the running game reads; the store's own.</param>
    /// <param name="store">Saves and applies every change.</param>
    /// <param name="resetBattleDescriptions">
    /// Resets the battle descriptions of the save being played and returns what to say.
    /// </param>
    /// <param name="utcNow">The clock for the confirmation window.</param>
    public ModSettingsMenu(
        AccessibilityConfig config,
        PlayerSettingsStore store,
        Func<string> resetBattleDescriptions,
        Func<DateTime>? utcNow = null)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.resetBattleDescriptions = resetBattleDescriptions ?? throw new ArgumentNullException(nameof(resetBattleDescriptions));
        this.utcNow = utcNow ?? (static () => DateTime.UtcNow);
        if (!ReferenceEquals(store.Config, config))
        {
            throw new ArgumentException("The store must manage the configuration this menu changes.", nameof(store));
        }

        entries = store.Catalogue.Entries;
    }

    public bool IsOpen
    {
        get { lock (sync) return open; }
    }

    /// <summary>The entry the menu is on; it stays there between visits.</summary>
    public ModSettingDefinition Current
    {
        get { lock (sync) return entries[index]; }
    }

    /// <summary>Opens the menu on the entry it was last on and says where it is and how to use it.</summary>
    public string Open(ModSettingsInputDevice device = ModSettingsInputDevice.Keyboard)
    {
        lock (sync)
        {
            this.device = device;
            open = true;
            armedAtUtc = null;
            var parts = new List<string> { "Mod settings.", Controls(), "Changes are saved as you make them." };
            if (store.LoadProblem is { } problem)
            {
                parts.Add(problem);
            }

            if (!config.EnableSpeech)
            {
                parts.Add("Mod speech is off, but this menu still speaks.");
            }

            parts.Add(Describe(includeCategory: true));
            return string.Join(" ", parts);
        }
    }

    /// <summary>Closes the menu; says nothing when it was not open.</summary>
    public string Close()
    {
        lock (sync)
        {
            if (!open)
            {
                return string.Empty;
            }

            open = false;
            var cancelled = Disarm();
            return cancelled + "Mod settings closed.";
        }
    }

    /// <summary>Carries out one command and returns what to say; nothing while closed.</summary>
    public string Handle(ModSettingsMenuCommand command)
    {
        lock (sync)
        {
            if (!open)
            {
                return string.Empty;
            }

            var cancelled = command == ModSettingsMenuCommand.Activate ? string.Empty : Disarm();
            return cancelled + command switch
            {
                ModSettingsMenuCommand.Previous => Move(-1),
                ModSettingsMenuCommand.Next => Move(1),
                ModSettingsMenuCommand.Decrease => Adjust(up: false),
                ModSettingsMenuCommand.Increase => Adjust(up: true),
                ModSettingsMenuCommand.Activate => Activate(),
                ModSettingsMenuCommand.Repeat => Describe(includeCategory: true),
                _ => string.Empty,
            };
        }
    }

    private string ActivateKey => device == ModSettingsInputDevice.Controller ? "A" : "I";

    private string AdjustKeys => device == ModSettingsInputDevice.Controller ? "Left and Right" : "U and O";

    private string Controls() => device == ModSettingsInputDevice.Controller
        ? "On the D-pad, Up and Down move through settings, Left and Right change a value, A switches or activates, B closes."
        : "J and L move through settings, U and O change a value, I switches or activates, K repeats, F11 closes.";

    /// <summary>Cancels a first activation that was waiting; returns what to say about it.</summary>
    private string Disarm()
    {
        if (armedAtUtc is null)
        {
            return string.Empty;
        }

        armedAtUtc = null;
        return Capitalize(entries[index].Verb ?? "action") + " cancelled. ";
    }

    private string Move(int delta)
    {
        var previousCategory = entries[index].Category;
        index = (index + delta + entries.Count) % entries.Count;
        return Describe(includeCategory: entries[index].Category != previousCategory);
    }

    private string Describe(bool includeCategory)
    {
        var entry = entries[index];
        var place = $"{index + 1} of {entries.Count}.";
        var category = includeCategory ? entry.Category + ": " : string.Empty;
        if (entry.Kind == ModSettingKind.Action)
        {
            return $"{category}{entry.Label}. {place} Press {ActivateKey} twice to {entry.Verb}.";
        }

        var value = store.ChosenValue(entry.Key);
        var text = $"{category}{entry.Label}, {entry.Format(value)}. {place}";
        if (WhenItApplies(entry, value) is { } when)
        {
            text += " " + when;
        }

        if (entry.SupportOn(store.Runtime).Note is { } note)
        {
            text += " " + note;
        }

        return text;
    }

    /// <summary>For reading an entry: when a change to it reaches the game, if not at once.</summary>
    private string? WhenItApplies(ModSettingDefinition entry, int value)
    {
        var started = store.StartupValue(entry.Key);
        return entry.EffectOn(store.Runtime) switch
        {
            ModSettingEffect.NotOnThisRuntime => "Not used by this version of the game.",
            ModSettingEffect.AfterRestart => value != started
                ? "Restart the game for this change to take effect."
                : "Changes take effect after restarting the game.",
            ModSettingEffect.OffNowOnAfterRestart when started == 0 => value != 0
                ? "Restart the game for this change to take effect."
                : "Turning it on takes effect after restarting the game.",
            ModSettingEffect.OffNowOnPartlyUntilRestart when started == 0 => value != 0
                ? "Restart the game for it to work fully."
                : "Turning it on works only in part until the game is restarted.",
            _ => null,
        };
    }

    /// <summary>For a change just made: what it still needs before it is fully in effect.</summary>
    private string? StillNeeds(ModSettingDefinition entry, int value)
    {
        var started = store.StartupValue(entry.Key);
        return entry.EffectOn(store.Runtime) switch
        {
            ModSettingEffect.AfterRestart when value != started =>
                "Saved; restart the game for this change to take effect.",
            ModSettingEffect.OffNowOnAfterRestart when started == 0 && value != 0 =>
                "Saved; restart the game for this change to take effect.",
            ModSettingEffect.OffNowOnPartlyUntilRestart when started == 0 && value != 0 =>
                "Saved; restart the game for it to work fully.",
            _ => null,
        };
    }

    private string Adjust(bool up)
    {
        var entry = entries[index];
        if (entry.Kind == ModSettingKind.Action)
        {
            return $"Press {ActivateKey} twice to {entry.Verb}.";
        }

        var current = store.ChosenValue(entry.Key);
        return Change(entry, current, up ? entry.StepUp(current) : entry.StepDown(current), up);
    }

    private string Activate()
    {
        var entry = entries[index];
        switch (entry.Kind)
        {
            case ModSettingKind.Action:
                var now = utcNow();
                if (armedAtUtc is { } armed && now >= armed && now - armed <= ConfirmationWindow)
                {
                    armedAtUtc = null;
                    return CarryOut(entry);
                }

                armedAtUtc = now;
                return Confirmation(entry);
            case ModSettingKind.Toggle:
                var current = store.ChosenValue(entry.Key);
                return Change(entry, current, current == 0 ? 1 : 0, up: current == 0);
            default:
                return $"{AdjustKeys} change this value.";
        }
    }

    private string Change(ModSettingDefinition entry, int current, int next, bool up)
    {
        var effect = entry.EffectOn(store.Runtime);
        if (effect == ModSettingEffect.NotOnThisRuntime)
        {
            return "Not used by this version of the game; nothing changed.";
        }

        if (next == current)
        {
            return entry.Kind == ModSettingKind.Toggle
                ? $"Already {entry.Format(current)}."
                : $"{Capitalize(entry.Format(current))}, the {(up ? "maximum" : "minimum")}.";
        }

        var change = store.Set(entry.Key, next);
        var notice = change.Notice is null ? string.Empty : " " + change.Notice;
        if (!change.Changed)
        {
            return effect == ModSettingEffect.AfterRestart
                ? $"Not changed: it takes effect only after a restart, and it could not be saved: {change.SaveFailure}.{notice}"
                : $"Not changed: {change.SaveFailure}.{notice}";
        }

        var spoken = Capitalize(entry.Format(change.Value));
        if (entry.Kind != ModSettingKind.Toggle && change.Value == entry.Values[^1])
        {
            spoken += ", the maximum";
        }
        else if (entry.Kind != ModSettingKind.Toggle && change.Value == entry.Values[0])
        {
            spoken += ", the minimum";
        }

        if (!change.Saved)
        {
            return $"{spoken}, for this session only. It could not be saved: {change.SaveFailure}.{notice}";
        }

        var text = spoken + ".";
        if (StillNeeds(entry, change.Value) is { } needs)
        {
            text += " " + needs;
        }

        if (entry.Key == nameof(AccessibilityConfig.EnableSpeech) && change.Value == 0)
        {
            text += " While it is off, this menu keeps speaking.";
        }

        return text + notice;
    }

    private string Confirmation(ModSettingDefinition entry) =>
        entry.Key == ModSettingsCatalogue.ResetBattleDescriptionsKey
            ? "Reset battle descriptions for the save you are playing? Every limit break and summon, and the summoning " +
              "opening, will be described again the next time it plays. Other saves and room descriptions keep their " +
              $"history. Press {ActivateKey} again to confirm; anything else cancels."
            : $"{entry.Label}? Press {ActivateKey} again to confirm; anything else cancels.";

    private string CarryOut(ModSettingDefinition entry)
    {
        if (entry.Key != ModSettingsCatalogue.ResetBattleDescriptionsKey)
        {
            return $"{entry.Label} is not available.";
        }

        try
        {
            var result = resetBattleDescriptions();
            return string.IsNullOrWhiteSpace(result) ? "Battle descriptions reset." : result;
        }
        catch (Exception ex)
        {
            return $"Battle descriptions could not be reset: {ex.Message.TrimEnd('.')}.";
        }
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
