using Ff7.Accessibility.Steam2026X64.Runtime.SystemMenu;

internal static class Steam2026NativeSystemMenuDirectionInputTests
{
    internal static void Run()
    {
        TracksOnlyNativeUpAndDownAttempts();
    }

    private static void TracksOnlyNativeUpAndDownAttempts()
    {
        var tracker = new Steam2026NativeSystemMenuDirectionInputTracker();

        Equal(0L, tracker.Generation, "initial native direction generation");
        tracker.Observe(directionCode: 4);
        tracker.Observe(directionCode: 8);
        Equal(0L, tracker.Generation, "left and right do not request a repeat");

        tracker.Observe(directionCode: 1);
        Equal(1L, tracker.Generation, "native Up requests one repeat");
        tracker.Observe(directionCode: 2);
        Equal(2L, tracker.Generation, "native Down requests one repeat");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected {expected}, got {actual}.");
        }
    }
}
