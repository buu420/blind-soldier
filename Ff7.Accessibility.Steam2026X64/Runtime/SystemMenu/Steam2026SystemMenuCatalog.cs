namespace Ff7.Accessibility.Steam2026X64.Runtime.SystemMenu;

internal sealed record Steam2026SystemMenuCatalogEntry(
    string SceneId,
    string ControlId,
    string Label,
    string? HelpText,
    Steam2026SystemMenuControlKind Kind);

internal sealed class Steam2026SystemMenuCatalog
{
    private readonly IReadOnlyDictionary<(string SceneId, string ControlId), Steam2026SystemMenuCatalogEntry>
        entries;

    private Steam2026SystemMenuCatalog(
        IEnumerable<Steam2026SystemMenuCatalogEntry> entries)
    {
        this.entries = entries.ToDictionary(
            entry => (entry.SceneId, entry.ControlId),
            entry => entry);
    }

    internal static Steam2026SystemMenuCatalog CreateEnglish() =>
        new(CreateEnglishEntries());

    internal string? FormatImmediate(Steam2026SystemMenuObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (!observation.IsFocused
            || !TryGetEntry(observation.SceneId, observation.ControlId, out var entry))
        {
            return null;
        }

        var value = Normalize(observation.Value);
        return entry.Kind switch
        {
            Steam2026SystemMenuControlKind.Button => entry.Label,
            Steam2026SystemMenuControlKind.Toggle =>
                Join(entry.Label, value),
            Steam2026SystemMenuControlKind.List =>
                FormatList(entry.Label, value, observation.Position, observation.Count),
            Steam2026SystemMenuControlKind.Slider =>
                FormatSlider(entry.Label, value),
            Steam2026SystemMenuControlKind.Binding =>
                FormatBinding(
                    entry.Label,
                    observation.PrimaryBinding,
                    observation.SecondaryBinding),
            Steam2026SystemMenuControlKind.ModalChoice =>
                JoinSentence(Normalize(observation.ModalText), value),
            _ => null
        };
    }

    internal string? GetHelpText(string sceneId, string controlId) =>
        TryGetEntry(sceneId, controlId, out var entry)
            ? Normalize(entry.HelpText)
            : null;

    internal bool TryGetEntry(
        string sceneId,
        string controlId,
        out Steam2026SystemMenuCatalogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(sceneId)
            || string.IsNullOrWhiteSpace(controlId))
        {
            entry = null!;
            return false;
        }

        return entries.TryGetValue((sceneId, controlId), out entry!);
    }

    private static Steam2026SystemMenuCatalogEntry Entry(
        string sceneId,
        string controlId,
        string label,
        string? helpText,
        Steam2026SystemMenuControlKind kind) =>
        new(sceneId, controlId, label, helpText, kind);

    private static IEnumerable<Steam2026SystemMenuCatalogEntry> CreateEnglishEntries()
    {
        var entries = new List<Steam2026SystemMenuCatalogEntry>
        {
            Entry("escape-root", "game-options", "Game Options",
                "Change game options.", Steam2026SystemMenuControlKind.Button),
            Entry("escape-root", "boosts", "Boosts",
                "Select in-game boosts.", Steam2026SystemMenuControlKind.Button),
            Entry("escape-root", "exit", "Exit",
                "Exit game.", Steam2026SystemMenuControlKind.Button),
            Entry("escape-root", "back", "Back",
                "Return to game.", Steam2026SystemMenuControlKind.Button),

            Entry("game-options", "system", "System",
                "Change system settings.", Steam2026SystemMenuControlKind.Button),
            Entry("game-options", "edit-controls", "Edit Controls",
                "Edit keybinds and button assignments.",
                Steam2026SystemMenuControlKind.Button),
            Entry("game-options", "autosave", "Autosave",
                "Adjust autosave settings.", Steam2026SystemMenuControlKind.Button),
            Entry("game-options", "back", "Back",
                "Return to previous page.", Steam2026SystemMenuControlKind.Button),

            Entry("controls", "keyboard", "Keyboard",
                "Modify keybinds.", Steam2026SystemMenuControlKind.Button),
            Entry("controls", "controller", "Controller",
                "Modify controller assignments.",
                Steam2026SystemMenuControlKind.Button),
            Entry("controls", "back", "Back",
                "Return to previous page.", Steam2026SystemMenuControlKind.Button),

            Entry("autosave", "autosave", "Autosave",
                "Automatically save game progress at supported checkpoints.",
                Steam2026SystemMenuControlKind.Toggle),
            Entry("autosave", "apply", "Apply",
                "Apply changes and go back to previous menu.",
                Steam2026SystemMenuControlKind.Button),
            Entry("autosave", "default", "Default",
                "Reset to default settings.", Steam2026SystemMenuControlKind.Button),
            Entry("autosave", "back", "Back",
                "Return to previous page.", Steam2026SystemMenuControlKind.Button),

            Entry("boosts", "battle-assist", "Battle Assist",
                "Continuously restores hit points and limit gauge during battle.",
                Steam2026SystemMenuControlKind.Toggle),
            Entry("boosts", "no-encounters", "No Encounters",
                "Turns random encounters on or off.",
                Steam2026SystemMenuControlKind.Toggle),
            Entry("boosts", "speed-boost", "Speed Boost (x3)",
                "Speeds up everything in the game. Does not apply to cutscenes.",
                Steam2026SystemMenuControlKind.Toggle),
            Entry("boosts", "apply", "Apply",
                "Apply selected boosts.", Steam2026SystemMenuControlKind.Button),
            Entry("boosts", "default", "Default",
                "Reset to default settings.", Steam2026SystemMenuControlKind.Button),
            Entry("boosts", "back", "Back",
                "Return to previous page.", Steam2026SystemMenuControlKind.Button),

            Entry("system", "resolution", "Resolution",
                "Change screen resolution.", Steam2026SystemMenuControlKind.List),
            Entry("system", "display-mode", "Display Mode",
                "Change display mode.", Steam2026SystemMenuControlKind.List),
            Entry("system", "primary-display", "Primary Display",
                "Select primary display.", Steam2026SystemMenuControlKind.List),
            Entry("system", "brightness", "Brightness",
                "Change brightness.", Steam2026SystemMenuControlKind.Slider),
            Entry("system", "master-volume", "Master Volume",
                "Change master volume.", Steam2026SystemMenuControlKind.Slider),
            Entry("system", "apply", "Apply",
                "Apply changes and go back to previous menu.",
                Steam2026SystemMenuControlKind.Button),
            Entry("system", "default", "Default",
                "Reset to default settings.", Steam2026SystemMenuControlKind.Button),
            Entry("system", "back", "Back",
                "Return to previous page.", Steam2026SystemMenuControlKind.Button)
        };

        AddBindingPage(entries, "keyboard");
        AddBindingPage(entries, "controller");

        foreach (var sceneId in new[]
                 {
                     "escape-root-modal",
                     "autosave-modal",
                     "boosts-modal",
                     "system-modal",
                     "keyboard-modal",
                     "controller-modal"
                 })
        {
            entries.Add(Entry(
                sceneId,
                "confirm-choice",
                string.Empty,
                null,
                Steam2026SystemMenuControlKind.ModalChoice));
        }

        return entries;
    }

    private static void AddBindingPage(
        ICollection<Steam2026SystemMenuCatalogEntry> entries,
        string sceneId)
    {
        (string Id, string Label)[] actions =
        [
            ("move-up", "Move Up"),
            ("move-down", "Move Down"),
            ("move-left", "Move Left"),
            ("move-right", "Move Right"),
            ("confirm", "Confirm"),
            ("cancel-run", "Cancel / Run"),
            ("menu", "Menu"),
            ("switch", "Switch"),
            ("pause", "Pause"),
            ("toggle-map", "Toggle Map"),
            ("rotate-camera-left", "Rotate Camera Left"),
            ("rotate-camera-right", "Rotate Camera Right"),
            ("flee-battle", "Flee Battle"),
            ("change-pov", "Change POV"),
            ("target", "Target")
        ];

        foreach (var action in actions)
        {
            entries.Add(Entry(
                sceneId,
                action.Id,
                action.Label,
                null,
                Steam2026SystemMenuControlKind.Binding));
        }

        entries.Add(Entry(
            sceneId,
            "apply",
            "Apply",
            "Apply changes and go back to previous menu.",
            Steam2026SystemMenuControlKind.Button));
        entries.Add(Entry(
            sceneId,
            "default",
            "Default",
            "Reset to default settings.",
            Steam2026SystemMenuControlKind.Button));
        entries.Add(Entry(
            sceneId,
            "back",
            "Back",
            "Return to previous page.",
            Steam2026SystemMenuControlKind.Button));
    }

    private static string? FormatList(
        string label,
        string? value,
        int position,
        int count)
    {
        var text = Join(label, value);
        return position > 0 && count > 0 && position <= count
            ? $"{text}, {position} of {count}"
            : text;
    }

    private static string? FormatSlider(string label, string? value)
    {
        if (value is null)
        {
            return label;
        }

        var suffix = value.EndsWith('%')
            ? value[..^1].Trim()
            : value;
        return $"{label}, {suffix} percent";
    }

    private static string FormatBinding(
        string label,
        string? primaryBinding,
        string? secondaryBinding)
    {
        var primary = Normalize(primaryBinding);
        var secondary = Normalize(secondaryBinding);
        return (primary, secondary) switch
        {
            (null, null) => label,
            (not null, null) => $"{label}. Primary, {primary}",
            (null, not null) => $"{label}. Secondary, {secondary}",
            _ => $"{label}. Primary, {primary}. Secondary, {secondary}"
        };
    }

    private static string? Join(string? left, string? right)
    {
        left = Normalize(left);
        right = Normalize(right);
        return (left, right) switch
        {
            (null, null) => null,
            (not null, null) => left,
            (null, not null) => right,
            _ => $"{left}, {right}"
        };
    }

    private static string? JoinSentence(string? left, string? right)
    {
        left = Normalize(left);
        right = Normalize(right);
        return (left, right) switch
        {
            (null, null) => null,
            (not null, null) => left,
            (null, not null) => right,
            _ => $"{left} {right}"
        };
    }

    private static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return string.Join(
            ' ',
            text.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
