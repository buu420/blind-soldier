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
/// one runtime the player uses. These run the real coordinator over translated guest memory,
/// through the same newest-only, dialogue-protecting delivery the Reloaded runtime uses.
/// </summary>
internal static class Steam2026FieldActivityRuntimeTests
{
    // IDdr's own pairs, hour by hour (FieldPuzzleStoryTests re-reads them from the archive).
    private static readonly int[][] BridgePairs =
        [[93, 2], [78, 12], [79, 0], [89, 66], [72, 60], [73, 54], [85, 48], [74, 42], [75, 36], [81, 14], [76, 10], [77, 6]];

    public static void Run()
    {
        SpeaksTheClockTimeAndRepeatsTheDetail();
        MovingReadingsReplaceOneAnother();
        WaitsForTheGuardiansWords();
        ARefusedReadingIsRetriedWhileCurrent();
        LosingFocusOwesNothing();
        RegainingFocusDuringASpinIsNotAStop();
        MutedSpeechSaysOnlyTheCurrentTimeOnUnmute();
        StaysSilentWhenTheReadoutIsTurnedOff();
        WiringMatchesTheReloadedRuntime();
    }

    private static readonly DateTime Start = new(2026, 9, 24, 22, 2, 30, DateTimeKind.Utc);

    private static bool IsClockLine(string text) =>
        text.StartsWith("Moving, ", StringComparison.Ordinal) ||
        text.StartsWith("Stopped, ", StringComparison.Ordinal) ||
        text.Contains("Long hand", StringComparison.Ordinal);

    private static void SpeaksTheClockTimeAndRepeatsTheDetail()
    {
        // The first visit as the Init leaves it, with the long hand already turned to six:
        // long six (bearing 0), short ten (170), second hand at three (64), and the party
        // where the corridor brings it in, on doorway ten's side (triangle 26).
        using (var rig = new ClockRig(enabled: true, longBearing: 0, shortBearing: 170, secondBearing: 64, openHours: [6, 10]))
        {
            // Three frames of one unchanged clock: one line, not three.
            rig.Observe(Start);
            rig.Observe(Start.AddMilliseconds(16));
            rig.Observe(Start.AddSeconds(3));
            var clock = rig.Spoken.Where(line => IsClockLine(line.Text)).ToArray();
            Equal(1, clock.Length, "the clock is said once for one situation");
            Equal("Stopped, ten thirty.", clock[0].Text, "x64 says only whether the hands move and the time they show");
            Equal(true, clock[0].Interrupt, "a newer reading replaces an older one rather than queueing behind it");
            Equal(
                "Stopped, ten thirty. You are by doorway ten. Long hand at six, short hand at ten, second hand at three. " +
                "The bridge to doorway six is open. The bridge to doorway ten is open.",
                rig.Coordinator.FieldActivityCurrentLine,
                "the repeat key answers with where the party stands, all three hands and both bridges");
        }

        // With the long hand still on two nothing claims doorway six.
        using (var rig = new ClockRig(enabled: true, longBearing: 86, shortBearing: 170, secondBearing: 64, openHours: [2, 10]))
        {
            rig.Observe(Start);
            rig.Observe(Start.AddSeconds(0.4));
            Equal("Stopped, ten ten.", rig.Spoken.Single(line => IsClockLine(line.Text)).Text, "the first visit's own clock");
            Equal(
                "Stopped, ten ten. You are by doorway ten. Long hand at two, short hand at ten, second hand at three. " +
                "The bridge to doorway two is open. The bridge to doorway ten is open.",
                rig.Coordinator.FieldActivityCurrentLine,
                "the first visit's repeat");
        }

        // In the middle of the clock; and on a triangle that is no part of it (a part of the
        // walkmesh no way in reaches) no place is claimed.
        foreach (var (triangle, place) in new[] { (131, "You are in the middle of the clock. "), (110, string.Empty) })
        {
            using var rig = new ClockRig(enabled: true, longBearing: 0, shortBearing: 170, secondBearing: 64, openHours: [6, 10],
                playerTriangle: triangle);
            rig.Observe(Start);
            rig.Observe(Start.AddSeconds(0.4));
            Equal("Stopped, ten thirty.", rig.Spoken.Single(line => IsClockLine(line.Text)).Text, $"x64, party on triangle {triangle}");
            Equal(
                "Stopped, ten thirty. " + place + "Long hand at six, short hand at ten, second hand at three. " +
                "The bridge to doorway six is open. The bridge to doorway ten is open.",
                rig.Coordinator.FieldActivityCurrentLine, $"x64 repeat, party on triangle {triangle}");
        }
    }

    /// <summary>One press of OK through the real coordinator: moving, then the new time, each replacing the last.</summary>
    private static void MovingReadingsReplaceOneAnother()
    {
        using var rig = new ClockRig(enabled: true, longBearing: 86, shortBearing: 170, secondBearing: 64, openHours: [2, 10]);
        rig.Observe(Start);
        rig.Observe(Start.AddSeconds(0.4));
        // The long hand leaves two; IDdr has locked every bridge while it turns.
        rig.SetHands(76, 170, []);
        rig.Observe(Start.AddSeconds(2));
        rig.SetHands(64, 170, [3, 10]);
        rig.Observe(Start.AddSeconds(2.1));
        rig.Observe(Start.AddSeconds(2.3));
        rig.Observe(Start.AddSeconds(2.5));
        var clock = rig.Spoken.Where(line => IsClockLine(line.Text)).ToArray();
        Equal("Stopped, ten ten.|Moving, about ten ten.|Stopped, ten fifteen.", string.Join("|", clock.Select(line => line.Text)),
            "the movement and the time it came to rest on");
        Equal(true, clock.All(line => line.Interrupt), "every clock reading replaces the one before");
    }

    /// <summary>
    /// The Time Guardian's "[OK] Stop!" has just been spoken. The clock's reading waits until the
    /// screen reader has finished it, then says the clock as it is by then.
    /// </summary>
    private static void WaitsForTheGuardiansWords()
    {
        using var rig = new ClockRig(enabled: true, longBearing: 86, shortBearing: 170, secondBearing: 64, openHours: [2, 10]);
        rig.Observe(Start);
        rig.Observe(Start.AddSeconds(0.4));
        rig.Coordinator.NoteSpeechDelivered("[OK] Stop!", Start.AddSeconds(2));
        rig.Speaking = true;
        rig.SetHands(76, 170, []);
        rig.Observe(Start.AddSeconds(2.05));
        rig.Observe(Start.AddSeconds(2.5));
        Equal(1, rig.Spoken.Count(line => IsClockLine(line.Text)), "nothing cuts the Guardian's words off");
        rig.Speaking = false;
        rig.SetHands(72, 170, []);
        rig.Observe(Start.AddSeconds(2.6));
        Equal("Moving, about ten ten.", rig.Spoken.Last(line => IsClockLine(line.Text)).Text,
            "then the clock as it is now");
        Equal(2, rig.Spoken.Count(line => IsClockLine(line.Text)), "once");
    }

    /// <summary>This runtime's speaker throws when Prism refuses a line; that is not delivery.</summary>
    private static void ARefusedReadingIsRetriedWhileCurrent()
    {
        using var rig = new ClockRig(enabled: true, longBearing: 86, shortBearing: 170, secondBearing: 64, openHours: [2, 10]);
        rig.Refusals = 1;
        rig.Observe(Start);
        rig.Observe(Start.AddSeconds(0.4));
        rig.Observe(Start.AddSeconds(0.5));
        Equal(0, rig.Spoken.Count(line => IsClockLine(line.Text)), "refused, and not retried on every frame");
        rig.Observe(Start.AddSeconds(0.9));
        Equal("Stopped, ten ten.", rig.Spoken.Single(line => IsClockLine(line.Text)).Text, "said once Prism takes it");
    }

    /// <summary>Losing focus drops what was owed: coming back reads the clock afresh, never the old reading.</summary>
    private static void LosingFocusOwesNothing()
    {
        using var rig = new ClockRig(enabled: true, longBearing: 86, shortBearing: 170, secondBearing: 64, openHours: [2, 10]);
        rig.Refusals = int.MaxValue;
        rig.Observe(Start);
        rig.Observe(Start.AddSeconds(0.4));
        rig.Observe(Start.AddSeconds(0.6), foreground: false);
        rig.Refusals = 0;
        rig.SetHands(64, 170, [3, 10]);
        rig.Observe(Start.AddSeconds(1.2));
        Equal(0, rig.Spoken.Count(line => IsClockLine(line.Text)), "one look on returning is not yet a stop");
        rig.Observe(Start.AddSeconds(1.6));
        Equal("Stopped, ten fifteen.", string.Join("|", rig.Spoken.Where(line => IsClockLine(line.Text)).Select(line => line.Text)),
            "only the clock as it is on returning");
    }

    /// <summary>
    /// Focus comes back while a spin is still going: the long hand sits on a numeral at every
    /// update, and the first look after returning must not be taken for a stop.
    /// </summary>
    private static void RegainingFocusDuringASpinIsNotAStop()
    {
        using var rig = new ClockRig(enabled: true, longBearing: 86, shortBearing: 170, secondBearing: 64, openHours: [2, 10]);
        rig.Observe(Start);
        rig.Observe(Start.AddSeconds(0.4));
        rig.Observe(Start.AddSeconds(1.0), foreground: false);
        rig.Spoken.Clear();
        rig.SetHands(42, 150, []);
        rig.Observe(Start.AddSeconds(1.1));
        rig.SetHands(22, 150, []);
        rig.Observe(Start.AddSeconds(1.13));
        rig.SetHands(0, 150, []);
        rig.Observe(Start.AddSeconds(1.17));
        var clock = rig.Spoken.Where(line => IsClockLine(line.Text)).Select(line => line.Text).ToArray();
        Equal("Moving, eleven twenty-five.", string.Join("|", clock), "the spin going on is movement, never a stop");
    }

    /// <summary>
    /// Speech turned off in the configuration: the coordinator says nothing of the clock, and on
    /// speech coming back says the time as it is then, once - not what it would have said
    /// while muted, and not as though that had been said.
    /// </summary>
    private static void MutedSpeechSaysOnlyTheCurrentTimeOnUnmute()
    {
        using var rig = new ClockRig(enabled: true, longBearing: 86, shortBearing: 170, secondBearing: 64, openHours: [2, 10]);
        rig.Config.EnableSpeech = false;
        rig.Observe(Start);
        rig.Observe(Start.AddSeconds(0.4));
        rig.SetHands(76, 170, []);
        rig.Observe(Start.AddSeconds(2.0));
        rig.SetHands(64, 170, [3, 10]);
        rig.Observe(Start.AddSeconds(2.1));
        rig.Observe(Start.AddSeconds(2.6));
        Equal(0, rig.Spoken.Count(line => IsClockLine(line.Text)), "nothing reaches the speaker while speech is off");
        rig.Config.EnableSpeech = true;
        rig.Observe(Start.AddSeconds(3.2));
        rig.Observe(Start.AddSeconds(4.0));
        var clock = rig.Spoken.Where(line => IsClockLine(line.Text)).ToArray();
        Equal("Stopped, ten fifteen.", string.Join("|", clock.Select(line => line.Text)), "only the current time, once");
        Equal(true, clock.All(line => line.Interrupt), "replacing, as ever");
    }

    private static void StaysSilentWhenTheReadoutIsTurnedOff()
    {
        using var rig = new ClockRig(enabled: false, longBearing: 0, shortBearing: 170, secondBearing: 64, openHours: [6, 10]);
        rig.Observe(Start);
        rig.Observe(Start.AddSeconds(3));
        Equal(false, rig.Spoken.Any(line => IsClockLine(line.Text)), "a readout turned off says nothing");
        Equal(null, rig.Coordinator.FieldActivityCurrentLine, "and owns no repeat line");
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
        Equal(true, Calls(observer, typeof(FieldActivityClockHost).GetMethod(nameof(FieldActivityClockHost.Deliver))!),
            "and the clock's readings go through the same newest-only, focus- and speech-gated delivery the Reloaded runtime uses");
        var ownsInput = runtimeType.GetField("fieldActivityOwnsInput", flags)!;
        Equal(true, ReadsField(observe, ownsInput), "a native wait holds route guidance and auto walk, as on Reloaded");
        var currentLine = runtimeType.GetProperty("FieldActivityCurrentLine", flags)!.GetMethod!;
        Equal(true, CallsAnywhere(typeof(Steam2026ResearchSession), currentLine), "the session's repeat key asks the activity first");
        var noteSpeech = runtimeType.GetMethod("NoteSpeechDelivered", flags)!;
        Equal(true, CallsAnywhere(typeof(Steam2026ResearchSession), noteSpeech),
            "the session tells the field coordinator about everything it speaks");
        Equal(true, new AccessibilityConfig().EnableFieldActivityReadout, "the readout is on by default");
    }

    /// <summary>The real coordinator over translated guest memory showing the Temple clock.</summary>
    private sealed class ClockRig : IDisposable
    {
        private readonly FieldObservationFixture fixture;
        private readonly int secondBearing;

        public ClockRig(bool enabled, int longBearing, int shortBearing, int secondBearing, int[] openHours, int playerTriangle = 26)
        {
            this.secondBearing = secondBearing;
            fixture = FieldObservationFixture.CreatePopulated();
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
            foreach (var (entity, model) in new[] { (21, 2), (22, 3), (23, 4) })
            {
                fixture.WriteByte(FieldScriptControllerReader.AddressEntityModelIds + entity, (byte)model);
                var objectBase = (uint)(FieldPositionReader.AddressFieldModelsObjs + model * FieldScriptControllerReader.ModelStride);
                fixture.Write(objectBase + FieldScriptControllerReader.ModelVisibilityOffset, [1]);
                fixture.Write(objectBase + FieldScriptControllerReader.ModelAnimationIdOffset, [0]);
                fixture.Write(objectBase + FieldPositionReader.ObjectXOffset, BitConverter.GetBytes(5 << 12));
                fixture.Write(objectBase + FieldPositionReader.ObjectYOffset, BitConverter.GetBytes(10 << 12));
                fixture.Write(objectBase + FieldPositionReader.ObjectZOffset, BitConverter.GetBytes(0));
                fixture.Write(objectBase + FieldPositionReader.ObjectTriangleOffset, BitConverter.GetBytes((ushort)131));
            }

            SetHands(longBearing, shortBearing, openHours);

            const uint processId = 42;
            var foregroundInput = new Steam2026ForegroundInputAdapter(() => (nint)1, _ => processId, _ => 0, processId);
            var objectReader = new Steam2026FieldObjectObservationReader(
                fixture.Direct, _ => null, _ => null, Array.Empty<FieldNavigationObjectDefinition>());
            var config = Config = new AccessibilityConfig
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
            Coordinator = new Steam2026FieldNavigationCoordinator(
                config,
                fixture.Direct,
                foregroundInput,
                objectReader,
                Path.GetTempPath(),
                AppContext.BaseDirectory,
                (text, interrupt) =>
                {
                    // As Steam2026ResearchAccessibilityOutput.Speak: a refused line throws.
                    if (Refusals > 0)
                    {
                        Refusals--;
                        throw new InvalidOperationException("Prism did not accept the speech request.");
                    }

                    Spoken.Add((text, interrupt));
                },
                _ => { },
                isSpeechPlaying: () => Speaking);
        }

        public Steam2026FieldNavigationCoordinator Coordinator { get; }

        /// <summary>The coordinator's own configuration, for turning speech off and on.</summary>
        public AccessibilityConfig Config { get; }

        public List<(string Text, bool Interrupt)> Spoken { get; } = [];

        /// <summary>The screen reader's answer to whether it is still speaking; null when it cannot say.</summary>
        public bool? Speaking { get; set; }

        /// <summary>How many more lines Prism refuses.</summary>
        public int Refusals { get; set; }

        /// <summary>The rendered bearings of the long and short hands, and the bridges IDdr has left unlocked.</summary>
        public void SetHands(int longBearing, int shortBearing, int[] openHours)
        {
            foreach (var (model, bearing) in new[] { (2, longBearing), (3, shortBearing), (4, secondBearing) })
            {
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
        }

        public void Observe(DateTime at, bool foreground = true)
        {
            var frame = new RuntimeFrameObservation(
                at,
                new GameLifecycleObservation(IsForeground: foreground, IsShuttingDown: false, ModuleId: FieldPositionReader.FieldModule, Revision: 0),
                RuntimeDomainUpdate<MenuFrameObservation>.Unchanged,
                RuntimeDomainUpdate<DialoguePageObservation>.Unchanged,
                RuntimeDomainUpdate<FieldFrameObservation>.Unchanged,
                RuntimeDomainUpdate<BattleFrameObservation>.Unchanged,
                RuntimeDomainUpdate<NavigationWorldObservation>.Unchanged);
            Coordinator.Observe(frame, at);
        }

        public void Dispose() => Coordinator.Dispose();
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
