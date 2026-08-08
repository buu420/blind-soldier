using Ff7.Accessibility.Core;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Samples the physical L key once per x86 monitor frame, keeps held-key state
/// synchronized while backgrounded or suppressed, and dispatches a rising edge
/// to either battle status or the navigation module that owned the press.
/// </summary>
internal sealed class BattleStatusLimitKeyFrameRouter
{
    private const int VirtualKeyL = 0x4C;
    private readonly NavigationKeyPressTracker tracker = new();
    private bool navigationPressPending;
    private int navigationOwnerModule = -1;

    internal bool BeginFrame(
        bool isLimitDown,
        bool isForeground,
        int currentModule,
        bool navigationOwnsLimitKey)
    {
        if (!isForeground ||
            !navigationOwnsLimitKey ||
            (navigationPressPending && navigationOwnerModule != currentModule))
        {
            ClearNavigationPress();
        }

        var pressed = tracker.Observe(VirtualKeyL, isLimitDown, isForeground);
        if (navigationOwnsLimitKey)
        {
            if (pressed)
            {
                navigationPressPending = true;
                navigationOwnerModule = currentModule;
            }

            return false;
        }

        return pressed;
    }

    internal bool HasNavigationPress(int ownerModule) =>
        navigationPressPending && navigationOwnerModule == ownerModule;

    internal bool TakeNavigationPress(int ownerModule)
    {
        if (!HasNavigationPress(ownerModule))
        {
            return false;
        }

        ClearNavigationPress();
        return true;
    }

    internal void DiscardNavigationPress(int ownerModule)
    {
        if (HasNavigationPress(ownerModule))
        {
            ClearNavigationPress();
        }
    }

    private void ClearNavigationPress()
    {
        navigationPressPending = false;
        navigationOwnerModule = -1;
    }
}
