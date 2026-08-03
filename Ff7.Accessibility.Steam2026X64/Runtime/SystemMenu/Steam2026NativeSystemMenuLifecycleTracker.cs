using System.Collections.Concurrent;

namespace Ff7.Accessibility.Steam2026X64.Runtime.SystemMenu;

/// <summary>
/// Associates cached native scene instances with their exact scene identity so
/// a leave callback shared by several scene classes can publish the right close.
/// </summary>
internal sealed class Steam2026NativeSystemMenuLifecycleTracker
{
    private readonly ConcurrentDictionary<ulong, Steam2026NativeSystemMenuScene>
        activeScenes = new();

    internal bool TryOpen(
        Steam2026NativeSystemMenuScene scene,
        nint instance,
        out Steam2026NativeSystemMenuLifecycleEvent lifecycleEvent)
    {
        var address = ToAddress(instance);
        if (address == 0)
        {
            lifecycleEvent = default;
            return false;
        }

        activeScenes[address] = scene;
        lifecycleEvent = new Steam2026NativeSystemMenuLifecycleEvent(
            scene,
            address,
            Opened: true,
            Generation: 0);
        return true;
    }

    internal bool TryClose(
        nint instance,
        out Steam2026NativeSystemMenuLifecycleEvent lifecycleEvent)
    {
        var address = ToAddress(instance);
        if (address == 0 || !activeScenes.TryRemove(address, out var scene))
        {
            lifecycleEvent = default;
            return false;
        }

        lifecycleEvent = new Steam2026NativeSystemMenuLifecycleEvent(
            scene,
            address,
            Opened: false,
            Generation: 0);
        return true;
    }

    internal void Clear() => activeScenes.Clear();

    private static ulong ToAddress(nint value) =>
        unchecked((ulong)(nuint)value);
}
