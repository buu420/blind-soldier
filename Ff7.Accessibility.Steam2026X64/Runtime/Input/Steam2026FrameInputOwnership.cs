using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Input;

/// <summary>
/// Routes the shared L key to exactly one owner for the current native module.
/// Field and world navigation own it in their modules; battle status owns it
/// in battle; inactive status polling synchronizes it everywhere else.
/// </summary>
internal static class Steam2026FrameInputOwnership
{
    internal static string? PollBattleStatusBeforeNavigation(
        BattleStatusHotkeyController controller,
        bool ownsBattleStatusHotkeys,
        int currentModule,
        bool navigationObserverAvailable,
        Steam2026ForegroundInputAdapter input,
        Func<int, BattleStatusMemberSnapshot?> readMember,
        bool resetSelectionWhenInactive = true)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(readMember);
        var navigationOwnsLimitKey = navigationObserverAvailable &&
            currentModule is FieldPositionReader.FieldModule or WorldMapStateReader.WorldModule;
        return controller.Poll(
            ownsBattleStatusHotkeys,
            input.ObserveRisingEdge,
            readMember,
            observeLimitKey: !navigationOwnsLimitKey,
            resetSelectionWhenInactive);
    }

    internal static void SynchronizeBattleStatusWithoutFrame(
        BattleStatusHotkeyController controller,
        Steam2026ForegroundInputAdapter input)
    {
        _ = PollBattleStatusBeforeNavigation(
            controller,
            ownsBattleStatusHotkeys: false,
            currentModule: -1,
            navigationObserverAvailable: false,
            input,
            _ => null,
            resetSelectionWhenInactive: false);
    }
}
