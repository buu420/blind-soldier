namespace Ff7.Accessibility.Steam2026X64.Runtime.SystemMenu;

internal sealed class Steam2026SystemMenuSpeechCoordinator
{
    private readonly Steam2026SystemMenuCatalog catalog;
    private readonly TimeSpan helpDelay;
    private ImmediateKey? lastImmediateKey;
    private FocusKey? focusedControl;
    private PendingHelp? pendingHelp;
    private string? announcedModalScene;

    internal Steam2026SystemMenuSpeechCoordinator(
        Steam2026SystemMenuCatalog catalog,
        TimeSpan helpDelay)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        if (helpDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(helpDelay));
        }

        this.helpDelay = helpDelay;
    }

    internal IReadOnlyList<Steam2026SystemMenuSpeechRequest> Observe(
        Steam2026SystemMenuObservation? observation,
        DateTime nowUtc,
        bool repeatUnchangedAutosave = false)
    {
        if (observation is null || !observation.IsFocused)
        {
            Reset();
            return [];
        }

        var focus = new FocusKey(observation.SceneId, observation.ControlId);
        if (focusedControl != focus)
        {
            focusedControl = focus;
            ScheduleHelp(focus, nowUtc);
            if (!IsModal(observation))
            {
                announcedModalScene = null;
            }
        }

        var immediateKey = ImmediateKey.From(observation);
        if (lastImmediateKey == immediateKey
            && !(repeatUnchangedAutosave && IsAutosaveSetting(observation)))
        {
            return [];
        }

        var effective = observation;
        if (IsModal(observation))
        {
            if (string.Equals(
                    announcedModalScene,
                    observation.SceneId,
                    StringComparison.Ordinal))
            {
                effective = observation with { ModalText = null };
            }
            else
            {
                announcedModalScene = observation.SceneId;
            }
        }

        var text = catalog.FormatImmediate(effective);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        lastImmediateKey = immediateKey;
        return [new Steam2026SystemMenuSpeechRequest(text, Interrupt: true)];
    }

    internal IReadOnlyList<Steam2026SystemMenuSpeechRequest> Poll(DateTime nowUtc)
    {
        if (pendingHelp is not { } pending || nowUtc < pending.DueUtc)
        {
            return [];
        }

        pendingHelp = null;
        if (focusedControl != pending.Focus)
        {
            return [];
        }

        return
        [
            new Steam2026SystemMenuSpeechRequest(
                pending.Text,
                Interrupt: false)
        ];
    }

    internal void Reset()
    {
        lastImmediateKey = null;
        focusedControl = null;
        pendingHelp = null;
        announcedModalScene = null;
    }

    private void ScheduleHelp(FocusKey focus, DateTime nowUtc)
    {
        var help = catalog.GetHelpText(focus.SceneId, focus.ControlId);
        pendingHelp = string.IsNullOrWhiteSpace(help)
            ? null
            : new PendingHelp(focus, help, nowUtc + helpDelay);
    }

    private bool IsModal(Steam2026SystemMenuObservation observation) =>
        catalog.TryGetEntry(observation.SceneId, observation.ControlId, out var entry)
        && entry.Kind == Steam2026SystemMenuControlKind.ModalChoice;

    private static bool IsAutosaveSetting(
        Steam2026SystemMenuObservation observation) =>
        string.Equals(observation.SceneId, "autosave", StringComparison.Ordinal)
        && string.Equals(observation.ControlId, "autosave", StringComparison.Ordinal);

    private readonly record struct FocusKey(string SceneId, string ControlId);

    private readonly record struct PendingHelp(
        FocusKey Focus,
        string Text,
        DateTime DueUtc);

    private readonly record struct ImmediateKey(
        string SceneId,
        string ControlId,
        string? Value,
        int Position,
        int Count,
        string? PrimaryBinding,
        string? SecondaryBinding,
        string? ModalText,
        long Generation)
    {
        internal static ImmediateKey From(Steam2026SystemMenuObservation observation) =>
            new(
                observation.SceneId,
                observation.ControlId,
                observation.Value,
                observation.Position,
                observation.Count,
                observation.PrimaryBinding,
                observation.SecondaryBinding,
                observation.ModalText,
                observation.Generation);
    }
}
