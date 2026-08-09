using System.Reflection;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;

internal static class WallMarketSquatRuntimeTests
{
    public static void Run()
    {
        Equal(true, new AccessibilityConfig().EnableSquatMinigamePrompts, "squat prompts default enabled");

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var coordinatorField = typeof(Mod).GetField("squatMinigameCueCoordinator", flags);
        Equal(typeof(SquatMinigameCueCoordinator), coordinatorField?.FieldType, "x86 shared squat coordinator field");

        var tick = typeof(Mod).GetMethod("TickSquatMinigameCue", flags);
        Equal(true, tick is not null, "x86 squat tick exists");
        Equal(
            true,
            Calls(typeof(Mod).GetMethod("MonitorLoop", flags), tick!),
            "x86 monitor loop invokes squat tick");
        Equal(
            true,
            Calls(tick, typeof(SquatMinigameCueCoordinator).GetMethod(nameof(SquatMinigameCueCoordinator.Observe))!),
            "x86 squat tick consumes shared native cue");
    }

    private static bool Calls(MethodInfo? caller, MethodInfo target)
    {
        if (caller is null)
        {
            return false;
        }

        var il = caller.GetMethodBody()?.GetILAsByteArray() ?? [];
        for (var index = 0; index + 4 < il.Length; index++)
        {
            if (il[index] is not (0x28 or 0x6F))
            {
                continue;
            }

            try
            {
                var method = caller.Module.ResolveMethod(BitConverter.ToInt32(il, index + 1));
                if (method is { } resolved &&
                    resolved.DeclaringType == target.DeclaringType &&
                    resolved.Name == target.Name)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return false;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
