using System.Reflection;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;

/// <summary>
/// The Steam 2026 coordinator tells the field controller about every explicit auto-walk stop
/// (P, the controller menu, a selection change, the walk giving up) and never about a
/// suspension, so a cross-field approach does not switch the walk back on at the next doorway.
/// </summary>
internal static class Steam2026CrossFieldAutoWalkStopTests
{
    internal static void Run()
    {
        var note = typeof(FieldNavigationController).GetMethod(nameof(FieldNavigationController.NoteAutoWalkStopped))!;
        var type = typeof(Steam2026FieldNavigationCoordinator);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
        foreach (var stop in new[] { "StopEveryControllerAutoWalk", "StopAutoWalkIfItCannotGetCloser" })
        {
            Require(Calls(type.GetMethod(stop, flags), note), $"{stop} records the stop intent");
        }

        // Observe holds the P toggle and the selection-change stop: both stops, both recorded.
        var observe = type.GetMethods(flags).Where(method => method.Name == "Observe").ToArray();
        Require(observe.Count(method => Calls(method, note)) == 1, "Observe records P and selection-change stops");
        Require(CallCount(observe.Single(method => Calls(method, note)), note) >= 2, "both of Observe's stops record the intent");
        foreach (var suspend in type.GetMethods(flags).Where(method => method.Name is "Suspend"))
        {
            Require(!Calls(suspend, note), "a suspension is not a stop");
        }
    }

    private static bool Calls(MethodInfo? caller, MethodInfo target) => CallCount(caller, target) > 0;

    private static int CallCount(MethodInfo? caller, MethodInfo target)
    {
        var il = caller?.GetMethodBody()?.GetILAsByteArray() ?? [];
        var count = 0;
        for (var index = 0; index + 4 < il.Length; index++)
        {
            if (il[index] is not (0x28 or 0x6F))
            {
                continue;
            }

            try
            {
                if (caller!.Module.ResolveMethod(BitConverter.ToInt32(il, index + 1)) is { } resolved &&
                    resolved.DeclaringType == target.DeclaringType && resolved.Name == target.Name)
                {
                    count++;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return count;
    }

    private static void Require(bool condition, string label)
    {
        if (!condition)
        {
            throw new InvalidOperationException(label);
        }
    }
}
