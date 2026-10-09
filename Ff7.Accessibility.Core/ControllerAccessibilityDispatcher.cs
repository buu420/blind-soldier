namespace Ff7.Accessibility.Core;

/// <summary>Runs settings and native party queries on the worker, outside input detours.</summary>
public sealed class ControllerAccessibilityDispatcher
{
    private readonly ModSettingsKeyboardController keyboard = new();
    private readonly Func<ControllerNavigationCapture?> capture;
    private readonly Func<bool> settingsIsOpen;
    private readonly Func<ModSettingsInputCommand, string?> settings;
    private readonly Func<ControllerNavigationCommand, string?> party;
    private readonly Action<string> speakSettings;
    private readonly Action<string> speakParty;
    private readonly Action pauseMovement;
    private readonly Action<string>? log;
    private bool openedByKeyboard;
    private string? pendingSettingsSpeech;
    private DateTime retrySettingsUtc;
    private ControllerNavigationCommand pendingParty;
    private long pendingPartyGeneration;
    private DateTime retryPartyUtc;
    private DateTime nowUtc;

    public ControllerAccessibilityDispatcher(Func<ControllerNavigationCapture?> capture,
        Func<bool> settingsIsOpen, Func<ModSettingsInputCommand, string?> settings,
        Func<ControllerNavigationCommand, string?> party, Action<string> speakSettings,
        Action<string> speakParty, Action pauseMovement, Action<string>? log = null)
    {
        this.capture = capture; this.settingsIsOpen = settingsIsOpen; this.settings = settings;
        this.party = party; this.speakSettings = speakSettings; this.speakParty = speakParty;
        this.pauseMovement = pauseMovement;
        this.log = log;
    }

    public bool IsOpen => settingsIsOpen();
    public bool UseControllerSettingsControls => !openedByKeyboard;

    public void Tick(bool foreground, Func<int, bool> isKeyDown, DateTime nowUtc)
    {
        this.nowUtc = nowUtc;
        try { TickCore(foreground, isKeyDown, nowUtc); }
        catch (Exception ex) { Report($"Controller accessibility command failed: {ex.Message}"); }
        finally
        {
            // A declined menu announcement must not let existing auto-walk continue.
            try { if (IsOpen) pauseMovement(); }
            catch (Exception ex) { Report($"Mod settings movement pause failed: {ex.Message}"); }
        }
    }

    private void TickCore(bool foreground, Func<int, bool> isKeyDown, DateTime nowUtc)
    {
        var input = capture();
        if (foreground && IsOpen && pendingSettingsSpeech is { } waiting && nowUtc >= retrySettingsUtc)
            DeliverSettings(waiting);
        if (!foreground || IsOpen || input is null || input.Generation.Id != pendingPartyGeneration ||
            input.Generation.Domain != ControllerNavigationDomain.Battle || !input.Generation.SupportsPartyReadout ||
            !input.Generation.IsFreshAt(nowUtc, ControllerNavigationCapture.ContextFreshness))
            pendingParty = ControllerNavigationCommand.None;
        else if (pendingParty != ControllerNavigationCommand.None && nowUtc >= retryPartyUtc)
            DeliverParty(pendingParty, input.Generation.Id);
        var keyCommand = keyboard.Poll(IsOpen, foreground, isKeyDown);
        if (keyCommand != ModSettingsInputCommand.None)
        {
            if (keyCommand == ModSettingsInputCommand.Open) openedByKeyboard = true;
            ApplySettings(keyCommand, input, foreground);
        }
        if (input is not null)
        {
            var generation = input.Generation;
            for (var n = 0; n < 8 && input.AccessibilityCommands.TryDequeue(out var command, out var id); n++)
            {
                if (command == ControllerNavigationCommand.SettingsClose)
                { ApplySettings(ModSettingsInputCommand.Close, input, foreground); continue; }
                if (!foreground || id != generation.Id || !generation.IsFreshAt(nowUtc, ControllerNavigationCapture.ContextFreshness))
                    continue;
                var menuCommand = command switch
                {
                    ControllerNavigationCommand.SettingsOpen => ModSettingsInputCommand.Open,
                    ControllerNavigationCommand.SettingsPrevious => ModSettingsInputCommand.Previous,
                    ControllerNavigationCommand.SettingsNext => ModSettingsInputCommand.Next,
                    ControllerNavigationCommand.SettingsDecrease => ModSettingsInputCommand.Decrease,
                    ControllerNavigationCommand.SettingsIncrease => ModSettingsInputCommand.Increase,
                    ControllerNavigationCommand.SettingsActivate => ModSettingsInputCommand.Activate,
                    _ => ModSettingsInputCommand.None
                };
                if (menuCommand != ModSettingsInputCommand.None)
                {
                    if (menuCommand == ModSettingsInputCommand.Open) openedByKeyboard = false;
                    ApplySettings(menuCommand, input);
                }
                else if (!IsOpen && generation.Domain == ControllerNavigationDomain.Battle && generation.SupportsPartyReadout &&
                    command is >= ControllerNavigationCommand.PartyPrevious and <= ControllerNavigationCommand.PartyLimit)
                {
                    DeliverParty(command, generation.Id);
                }
            }
            if (IsOpen && !openedByKeyboard &&
                (!input.SettingsIsOpen || input.TryRetireForLostPolling(nowUtc, TimeSpan.FromMilliseconds(500))))
                ApplySettings(ModSettingsInputCommand.Close, input);
            // A keyboard menu also owns the native pad. If a stale context closed
            // it between worker frames, restore capture before the next input read.
            if (IsOpen && openedByKeyboard && !input.SettingsIsOpen) input.SetSettingsOpen(true);
        }
    }

    private void ApplySettings(ModSettingsInputCommand command, ControllerNavigationCapture? input, bool announce = true)
    {
        var wasOpen = IsOpen;
        pendingSettingsSpeech = null;
        var text = settings(command);
        if (command == ModSettingsInputCommand.Open && IsOpen && input?.SettingsIsOpen != true)
            input?.SetSettingsOpen(true);
        if (command == ModSettingsInputCommand.Close)
        { openedByKeyboard = false; input?.SetSettingsOpen(false); }
        if (announce && !string.IsNullOrWhiteSpace(text) && (wasOpen || IsOpen)) DeliverSettings(text);
    }

    private void DeliverSettings(string text)
    {
        try { speakSettings(text); pendingSettingsSpeech = null; }
        catch (Exception ex)
        {
            pendingSettingsSpeech = IsOpen ? text : null;
            retrySettingsUtc = nowUtc.AddMilliseconds(500);
            Report($"Mod settings speech was declined; current line remains retryable: {ex.Message}");
        }
    }

    private void DeliverParty(ControllerNavigationCommand command, long generation)
    {
        try
        {
            // Requery on retry: HP, statuses and the current selection may have changed.
            var speech = party(command);
            if (!string.IsNullOrWhiteSpace(speech)) speakParty(speech);
            pendingParty = ControllerNavigationCommand.None;
        }
        catch (Exception ex)
        {
            pendingParty = command is ControllerNavigationCommand.PartyPrevious or ControllerNavigationCommand.PartyNext
                ? ControllerNavigationCommand.PartySummary : command;
            pendingPartyGeneration = generation;
            retryPartyUtc = nowUtc.AddMilliseconds(500);
            Report($"Native party readout failed; current selection remains retryable: {ex.Message}");
        }
    }

    private void Report(string message)
    {
        try { log?.Invoke(message); }
        catch { /* Diagnostics must not terminate the accessibility worker either. */ }
    }
}
