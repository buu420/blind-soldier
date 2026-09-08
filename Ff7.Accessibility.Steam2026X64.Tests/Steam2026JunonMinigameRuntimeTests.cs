using System.Reflection;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;

internal static class Steam2026JunonMinigameRuntimeTests
{
    public static void Run()
    {
        Equal(true, new AccessibilityConfig().EnableJunonMinigamePrompts, "Junon prompts default enabled");
        var preservedConfig = System.Text.Json.JsonSerializer.Deserialize<AccessibilityConfig>("{}")!;
        Equal(true, preservedConfig.EnableJunonTimingCue, "existing x64 configuration enables the independent Junon timing sound");
        Equal(100, preservedConfig.JunonTimingCueVolumePercent, "existing x64 configuration keeps the timing sound audible");
        Equal(true, new AccessibilityConfig().EnableJunonParadeAlignmentAssist, "Junon alignment assist default enabled");

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var runtimeType = typeof(Steam2026FieldNavigationCoordinator);
        var coordinatorField = runtimeType.GetField("junonMinigameCueCoordinator", flags);
        Equal(typeof(JunonMinigameCueCoordinator), coordinatorField?.FieldType, "x64 shared Junon coordinator field");
        Equal("JunonTimingCueTracker", runtimeType.GetField("junonTimingCueTracker", flags)?.FieldType.Name,
            "x64 owns an independent native timing cue tracker");
        Equal("JunonTimingCuePlayer", runtimeType.GetField("junonTimingCuePlayer", flags)?.FieldType.Name,
            "x64 owns a sound channel independent of speech");
        var assistField = runtimeType.GetField("junonParadeAlignmentAssist", flags);
        Equal(typeof(JunonParadeAlignmentAssist), assistField?.FieldType, "x64 shared Junon alignment assist field");
        var ownershipField = runtimeType.GetField("junonParadeClaimsFieldInput", flags);
        Equal(typeof(bool), ownershipField?.FieldType, "x64 Junon input ownership field");

        var cueMethod = runtimeType.GetMethod("ObserveJunonMinigameCues", flags);
        Equal(true, cueMethod is not null, "x64 Junon observer exists");
        var timingMethod = runtimeType.GetMethod("PlayJunonTimingCue", flags);
        Equal(true, timingMethod is not null && Calls(cueMethod, timingMethod),
            "x64 Junon observer delivers the native timing sound");
        Equal(true, Calls(timingMethod, runtimeType.GetField("junonTimingCueTracker", flags)!
            .FieldType.GetMethod("Observe")!), "x64 sound uses native prompt identity");
        Equal(true, Calls(timingMethod, runtimeType.GetField("junonTimingCuePlayer", flags)!
            .FieldType.GetMethod("Play")!), "x64 sound reaches its independent output channel");
        Equal(
            true,
            Calls(runtimeType.GetMethod("Observe", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public), cueMethod!),
            "x64 field observer invokes Junon observer");
        Equal(
            true,
            Calls(cueMethod, typeof(JunonMinigameCueCoordinator).GetMethod(nameof(JunonMinigameCueCoordinator.TryRead))!),
            "x64 Junon observer reads one shared coherent snapshot");
        Equal(
            true,
            Calls(cueMethod, typeof(JunonParadeAlignmentAssist).GetMethod("Observe", flags)!),
            "x64 Junon observer drives the shared alignment assist");
        Equal(
            true,
            Calls(cueMethod, typeof(JunonMinigameCueCoordinator).GetMethod(nameof(JunonMinigameCueCoordinator.ObserveSnapshot))!),
            "x64 Junon speech consumes the same snapshot as alignment");
        Equal(
            true,
            ReadsField(runtimeType.GetMethod("Observe", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public), ownershipField!),
            "x64 field navigation yields while the Junon assist owns direction input");
    }

    private static bool ReadsField(MethodInfo? method, FieldInfo target)
    {
        if (method is null)
        {
            return false;
        }

        var il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        for (var index = 0; index + 4 < il.Length; index++)
        {
            if (il[index] != 0x7B)
            {
                continue;
            }

            try
            {
                if (method.Module.ResolveField(BitConverter.ToInt32(il, index + 1)) == target)
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
