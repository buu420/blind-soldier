namespace Ff7.Accessibility.Steam2026X64.Runtime.SystemMenu;

/// <summary>
/// Records the native MUI vertical-direction callbacks. Codes 1 and 2 are the
/// Up and Down branches proven in the exact Steam 2026 executable; codes 4 and
/// 8 are the horizontal branches used to change a setting value.
/// </summary>
internal sealed class Steam2026NativeSystemMenuDirectionInputTracker
{
    private long generation;

    internal long Generation => Volatile.Read(ref generation);

    internal void Observe(int directionCode)
    {
        if (directionCode is 1 or 2)
        {
            Interlocked.Increment(ref generation);
        }
    }
}
