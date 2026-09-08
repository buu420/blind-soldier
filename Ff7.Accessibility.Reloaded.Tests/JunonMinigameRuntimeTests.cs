using System.Reflection;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;

internal static class JunonMinigameRuntimeTests
{
    public static void Run()
    {
        Equal(true, new AccessibilityConfig().EnableJunonMinigamePrompts, "Junon prompts default enabled");
        var preservedConfig = System.Text.Json.JsonSerializer.Deserialize<AccessibilityConfig>("{}")!;
        Equal(true, preservedConfig.EnableJunonTimingCue, "existing configuration enables the independent Junon timing sound");
        Equal(100, preservedConfig.JunonTimingCueVolumePercent, "existing configuration keeps the timing sound audible");
        Equal(false, System.Text.Json.JsonSerializer.Deserialize<AccessibilityConfig>(
            "{\"EnableJunonTimingCue\":false}")!.EnableJunonTimingCue, "explicit timing sound opt-out is preserved");
        Equal(true, new AccessibilityConfig().EnableJunonParadeAlignmentAssist, "Junon alignment assist default enabled");

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var coordinatorField = typeof(Mod).GetField("junonMinigameCueCoordinator", flags);
        Equal(typeof(JunonMinigameCueCoordinator), coordinatorField?.FieldType, "x86 shared Junon coordinator field");
        Equal("JunonTimingCueTracker", typeof(Mod).GetField("junonTimingCueTracker", flags)?.FieldType.Name,
            "x86 owns an independent native timing cue tracker");
        Equal("JunonTimingCuePlayer", typeof(Mod).GetField("junonTimingCuePlayer", flags)?.FieldType.Name,
            "x86 owns a sound channel independent of speech");
        var assistField = typeof(Mod).GetField("junonParadeAlignmentAssist", flags);
        Equal(typeof(JunonParadeAlignmentAssist), assistField?.FieldType, "x86 shared Junon alignment assist field");
        var ownershipField = typeof(Mod).GetField("junonParadeClaimsFieldInput", flags);
        Equal(typeof(bool), ownershipField?.FieldType, "x86 Junon input ownership field");

        var tick = typeof(Mod).GetMethod("TickJunonMinigameCues", flags);
        Equal(true, tick is not null, "x86 Junon tick exists");
        var timingMethod = typeof(Mod).GetMethod("PlayJunonTimingCue", flags);
        Equal(true, timingMethod is not null && Calls(tick, timingMethod),
            "x86 Junon tick delivers the native timing sound");
        Equal(true, Calls(timingMethod, typeof(Mod).GetField("junonTimingCueTracker", flags)!
            .FieldType.GetMethod("Observe")!), "x86 sound uses native prompt identity");
        Equal(true, Calls(timingMethod, typeof(Mod).GetField("junonTimingCuePlayer", flags)!
            .FieldType.GetMethod("Play")!), "x86 sound reaches its independent output channel");
        Equal(
            true,
            Calls(typeof(Mod).GetMethod("MonitorLoop", flags), tick!),
            "x86 monitor loop invokes Junon tick");
        Equal(
            true,
            Calls(tick, typeof(JunonMinigameCueCoordinator).GetMethod(nameof(JunonMinigameCueCoordinator.TryRead))!),
            "x86 Junon tick reads one shared coherent snapshot");
        Equal(
            true,
            Calls(tick, typeof(JunonParadeAlignmentAssist).GetMethod("Observe", flags)!),
            "x86 Junon tick drives the shared alignment assist");
        Equal(
            true,
            Calls(tick, typeof(JunonMinigameCueCoordinator).GetMethod(nameof(JunonMinigameCueCoordinator.ObserveSnapshot))!),
            "x86 Junon speech consumes the same snapshot as alignment");
        Equal(
            true,
            ReadsField(typeof(Mod).GetMethod("TickFieldNavigationAssistant", flags), ownershipField!),
            "x86 field navigation yields while the Junon assist owns direction input");
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
