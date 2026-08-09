using System.Reflection;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;

internal static class Steam2026WallMarketSquatRuntimeTests
{
    public static void Run()
    {
        Equal(true, new AccessibilityConfig().EnableSquatMinigamePrompts, "squat prompts default enabled");

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var runtimeType = typeof(Steam2026FieldNavigationCoordinator);
        var coordinatorField = runtimeType.GetField("squatMinigameCueCoordinator", flags);
        Equal(typeof(SquatMinigameCueCoordinator), coordinatorField?.FieldType, "x64 shared squat coordinator field");

        var cueMethod = runtimeType.GetMethod("ObserveSquatMinigameCue", flags);
        Equal(true, cueMethod is not null, "x64 squat observer exists");
        Equal(
            true,
            Calls(runtimeType.GetMethod("Observe", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public), cueMethod!),
            "x64 field observer invokes squat observer");
        Equal(
            true,
            Calls(cueMethod, typeof(SquatMinigameCueCoordinator).GetMethod(nameof(SquatMinigameCueCoordinator.Observe))!),
            "x64 squat observer consumes shared native cue");
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
