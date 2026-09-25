using System.Reflection;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64.Runtime;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;
using Ff7.Accessibility.Steam2026X64.Runtime.Input;

/// <summary>
/// The field activity readout on the Steam 2026 runtime. Until 2026-09-25 this runtime never
/// built its observation, so the Temple clock said nothing about its hands or bridges on the
/// one runtime the player uses. These run the real coordinator over translated guest memory.
/// </summary>
internal static class Steam2026FieldActivityRuntimeTests
{
    // IDdr's own pairs, hour by hour (FieldPuzzleStoryTests re-reads them from the archive).
    private static readonly int[][] BridgePairs =
        [[93, 2], [78, 12], [79, 0], [89, 66], [72, 60], [73, 54], [85, 48], [74, 42], [75, 36], [81, 14], [76, 10], [77, 6]];

    public static void Run()
    {
        SpeaksTheClockHandsAndBothBridges();
        StaysSilentWhenTheReadoutIsTurnedOff();
        WiringMatchesTheReloadedRuntime();
    }

    private static void SpeaksTheClockHandsAndBothBridges()
    {
        // The first visit as the Init leaves it, with the long hand already turned to six:
        // long six (bearing 0), short ten (170), second hand at three (64), and the party
        // where the corridor brings it in, on doorway ten's side (triangle 26).
        var (spoken, coordinator) = ObserveClock(enabled: true, longBearing: 0, shortBearing: 170, secondBearing: 64, openHours: [6, 10]);
        using (coordinator)
        {
            var clock = spoken.Where(line => line.Text.Contains("Long hand", StringComparison.Ordinal)).ToArray();
            Equal(1, clock.Length, "the clock is said once for one situation");
            Equal(
                "You are by doorway ten. Long hand at six, short hand at ten, second hand at three. " +
                "The bridge to doorway six is open. The bridge to doorway ten is open.",
                clock[0].Text,
                "x64 says where the party stands, both hands and both bridges from the native models and IDLCK state");
            Equal(false, clock[0].Interrupt, "the clock does not cut off dialogue");
            Equal(clock[0].Text, coordinator.FieldActivityCurrentLine, "the repeat key answers with the clock as it is now");
        }

        // With the long hand still on two nothing claims doorway six.
        (spoken, coordinator) = ObserveClock(enabled: true, longBearing: 86, shortBearing: 170, secondBearing: 64, openHours: [2, 10]);
        using (coordinator)
        {
            Equal(
                "You are by doorway ten. Long hand at two, short hand at ten, second hand at three. " +
                "The bridge to doorway two is open. The bridge to doorway ten is open.",
                spoken.Single(line => line.Text.Contains("Long hand", StringComparison.Ordinal)).Text,
                "the first visit's own clock");
        }

        // In the middle of the clock; and on a triangle that is no part of it (a part of the
        // walkmesh no way in reaches) no place is claimed.
        foreach (var (triangle, place) in new[] { (131, "You are in the middle of the clock. "), (110, string.Empty) })
        {
            (spoken, coordinator) = ObserveClock(enabled: true, longBearing: 0, shortBearing: 170, secondBearing: 64, openHours: [6, 10],
                playerTriangle: triangle);
            using (coordinator)
            {
                Equal(
                    place + "Long hand at six, short hand at ten, second hand at three. " +
                    "The bridge to doorway six is open. The bridge to doorway ten is open.",
                    spoken.Single(line => line.Text.Contains("Long hand", StringComparison.Ordinal)).Text,
                    $"x64, party on triangle {triangle}");
                Equal(spoken.Single(line => line.Text.Contains("Long hand", StringComparison.Ordinal)).Text,
                    coordinator.FieldActivityCurrentLine, $"x64 repeat, party on triangle {triangle}");
            }
        }
    }

    private static void StaysSilentWhenTheReadoutIsTurnedOff()
    {
        var (spoken, coordinator) = ObserveClock(enabled: false, longBearing: 0, shortBearing: 170, secondBearing: 64, openHours: [6, 10]);
        using (coordinator)
        {
            Equal(false, spoken.Any(line => line.Text.Contains("Long hand", StringComparison.Ordinal)), "a readout turned off says nothing");
            Equal(null, coordinator.FieldActivityCurrentLine, "and owns no repeat line");
        }
    }

    private static void WiringMatchesTheReloadedRuntime()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var runtimeType = typeof(Steam2026FieldNavigationCoordinator);
        Equal(typeof(FieldActivityReadout), runtimeType.GetField("fieldActivityReadout", flags)?.FieldType,
            "x64 owns the shared field activity readout");
        var observer = runtimeType.GetMethod("ObserveFieldActivity", flags);
        var observe = runtimeType.GetMethod("Observe", flags | BindingFlags.Public);
        Equal(true, observer is not null && Calls(observe, observer), "x64 field observer runs the activity readout every frame");
        Equal(true, Calls(observer, typeof(FieldActivityObservationBuilder).GetMethod(nameof(FieldActivityObservationBuilder.Build))!),
            "from the same shared observation the Reloaded runtime builds");
        Equal(true, Calls(observer, typeof(FieldActivityReadout).GetMethod(nameof(FieldActivityReadout.Observe),
                [typeof(FieldActivityObservation), typeof(DateTime)])!),
            "and the shared readout decides what is said");
        var ownsInput = runtimeType.GetField("fieldActivityOwnsInput", flags)!;
        Equal(true, ReadsField(observe, ownsInput), "a native wait holds route guidance and auto walk, as on Reloaded");
        var currentLine = runtimeType.GetProperty("FieldActivityCurrentLine", flags)!.GetMethod!;
        Equal(true, CallsAnywhere(typeof(Steam2026ResearchSession), currentLine), "the session's repeat key asks the activity first");
        Equal(true, new AccessibilityConfig().EnableFieldActivityReadout, "the readout is on by default");
    }

    private static (List<(string Text, bool Interrupt)> Spoken, Steam2026FieldNavigationCoordinator Coordinator) ObserveClock(
        bool enabled, int longBearing, int shortBearing, int secondBearing, int[] openHours, int playerTriangle = 26)
    {
        var fixture = FieldObservationFixture.CreatePopulated();
        fixture.Write((uint)FieldPositionReader.AddressFieldId, BitConverter.GetBytes((ushort)607));
        // The party is model 1, whose triangle the populated fixture leaves at 9.
        fixture.Write((uint)FieldPositionReader.AddressFieldModelsObjs + FieldPositionReader.FieldObjectStride +
            FieldPositionReader.ObjectTriangleOffset, BitConverter.GetBytes((ushort)playerTriangle));
        // kuro_4 has twenty-six entities; the hands are long 21, short 22 and second 23.
        fixture.Write(FieldObservationFixture.ScriptPointer + FieldActivityStateReader.ScriptHeaderEntityCountOffset, [26]);
        fixture.WriteByte(FieldPositionReader.AddressFieldNumModels, 5);
        for (var entity = 0; entity < 26; entity++)
        {
            fixture.WriteByte(FieldScriptControllerReader.AddressEntityModelIds + entity, (byte)FieldScriptControllerReader.NoModel);
        }

        fixture.Write((uint)FieldScriptControllerReader.AddressModelTablePointer,
            BitConverter.GetBytes((uint)FieldPositionReader.AddressFieldModelsObjs));
        foreach (var (entity, model, bearing) in new[] { (21, 2, longBearing), (22, 3, shortBearing), (23, 4, secondBearing) })
        {
            fixture.WriteByte(FieldScriptControllerReader.AddressEntityModelIds + entity, (byte)model);
            var objectBase = (uint)(FieldPositionReader.AddressFieldModelsObjs + model * FieldScriptControllerReader.ModelStride);
            fixture.Write(objectBase + FieldScriptControllerReader.ModelVisibilityOffset, [1]);
            fixture.Write(objectBase + FieldScriptControllerReader.ModelAnimationIdOffset, [0]);
            fixture.Write(objectBase + FieldPositionReader.ObjectXOffset, BitConverter.GetBytes(5 << 12));
            fixture.Write(objectBase + FieldPositionReader.ObjectYOffset, BitConverter.GetBytes(10 << 12));
            fixture.Write(objectBase + FieldPositionReader.ObjectZOffset, BitConverter.GetBytes(0));
            fixture.Write(objectBase + FieldPositionReader.ObjectTriangleOffset, BitConverter.GetBytes((ushort)131));
            fixture.Write(FieldObservationFixture.ModelTable + (uint)(model * FieldPositionReader.FieldModelStride) +
                FieldPositionReader.ModelDirectionOffset, [(byte)bearing]);
        }

        // IDdr locks all twenty-four; each hand's own script unlocks its hour's pair.
        var locks = new byte[FieldBoundaryStateReader.BoundaryByteCount];
        foreach (var triangle in BridgePairs.Where((_, hour) => !openHours.Contains(hour)).SelectMany(pair => pair))
        {
            locks[triangle >> 3] |= (byte)(1 << (triangle & 7));
        }

        fixture.Write(FieldObservationFixture.FieldGlobalPointer + FieldBoundaryStateReader.BoundaryBitsOffset, locks);

        const uint processId = 42;
        var foregroundInput = new Steam2026ForegroundInputAdapter(() => (nint)1, _ => processId, _ => 0, processId);
        var objectReader = new Steam2026FieldObjectObservationReader(
            fixture.Direct, _ => null, _ => null, Array.Empty<FieldNavigationObjectDefinition>());
        var spoken = new List<(string Text, bool Interrupt)>();
        var config = new AccessibilityConfig
        {
            EnableFieldNavigationAssistant = false,
            EnableFieldExitProximityCues = false,
            EnableFieldLadderProximityCues = false,
            EnableFieldSwingingBarTimingCue = false,
            EnableSquatMinigamePrompts = false,
            EnableJunonMinigamePrompts = false,
            EnableJunonParadeAlignmentAssist = false,
            EnableFloor60SoldierTurnCue = false,
            EnableFieldActivityReadout = enabled
        };
        var coordinator = new Steam2026FieldNavigationCoordinator(
            config,
            fixture.Direct,
            foregroundInput,
            objectReader,
            Path.GetTempPath(),
            AppContext.BaseDirectory,
            (text, interrupt) => spoken.Add((text, interrupt)),
            _ => { });
        var now = new DateTime(2026, 9, 24, 22, 2, 30, DateTimeKind.Utc);
        var frame = new RuntimeFrameObservation(
            now,
            new GameLifecycleObservation(IsForeground: true, IsShuttingDown: false, ModuleId: FieldPositionReader.FieldModule, Revision: 0),
            RuntimeDomainUpdate<MenuFrameObservation>.Unchanged,
            RuntimeDomainUpdate<DialoguePageObservation>.Unchanged,
            RuntimeDomainUpdate<FieldFrameObservation>.Unchanged,
            RuntimeDomainUpdate<BattleFrameObservation>.Unchanged,
            RuntimeDomainUpdate<NavigationWorldObservation>.Unchanged);
        // Three frames of one unchanged clock: one sentence, not three.
        coordinator.Observe(frame, now);
        coordinator.Observe(frame, now.AddMilliseconds(16));
        coordinator.Observe(frame, now.AddSeconds(3));
        return (spoken, coordinator);
    }

    private static bool CallsAnywhere(Type type, MethodInfo target)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var candidate in new[] { type }.Concat(type.GetNestedTypes(all)))
        {
            foreach (var method in candidate.GetMethods(all))
            {
                if (Calls(method, target))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ReadsField(MethodInfo? method, FieldInfo target)
    {
        var il = method?.GetMethodBody()?.GetILAsByteArray() ?? [];
        for (var index = 0; index + 4 < il.Length; index++)
        {
            if (il[index] != 0x7B)
            {
                continue;
            }

            try
            {
                if (method!.Module.ResolveField(BitConverter.ToInt32(il, index + 1)) == target)
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
