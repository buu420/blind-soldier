using System.Reflection;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Reloaded host tells the field controller about an explicit auto-walk stop (P, the
/// controller menu, a selection change, the walk giving up), so a cross-field approach does not
/// switch the walk back on at the next doorway. A suspension (battle, focus, a scene owning the
/// field) is not a stop and must not tell it.
/// </summary>
internal static class CrossFieldAutoWalkStopWiringTests
{
    internal static void Run()
    {
        var note = typeof(FieldNavigationController).GetMethod(nameof(FieldNavigationController.NoteAutoWalkStopped))!;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var stop in new[] { "StopNavigationAutoWalk", "StopEveryControllerAutoWalk" })
        {
            Require(Calls(typeof(Mod).GetMethod(stop, flags), note), $"Mod.{stop} records the stop intent");
        }

        foreach (var suspend in new[] { "SuspendNavigationAutoWalk", "SuspendEveryControllerAutoWalk" })
        {
            Require(!Calls(typeof(Mod).GetMethod(suspend, flags), note), $"Mod.{suspend} is not a stop");
        }
    }

    internal static bool Calls(MethodInfo? caller, MethodInfo target)
    {
        var il = caller?.GetMethodBody()?.GetILAsByteArray() ?? [];
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
                    return true;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return false;
    }

    private static void Require(bool condition, string label)
    {
        if (!condition)
        {
            throw new InvalidOperationException(label);
        }
    }
}
