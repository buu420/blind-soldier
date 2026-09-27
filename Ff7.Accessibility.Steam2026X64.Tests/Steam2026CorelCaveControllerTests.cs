using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;
using Ff7.Accessibility.Steam2026X64.Runtime.Input;

/// <summary>
/// The Corel Valley Cave controller report (628 sandun_1, 2026-09-27): R1 and L1 did not change
/// category and nothing could be selected for auto walk. The log shows every R3 answered by
/// "Navigation menu closed." and the opening description in the same drain, from the first
/// press on triangle 168 - before the party ever reached a ladder - and the exit cue's three
/// second pulse arriving three or four seconds apart, which is the worker coming round far
/// more slowly than its 50 ms scan.
///
/// <para>These drive the real field coordinator on the installed cave: the field file where
/// the game keeps it, every LINE the scripts create switched on at its own segment, Cloud
/// controlled on triangle 168, and the real SDL capture hook polled from its own thread the way
/// the game polls it, on the wall clock. The menu has to stay open and answer the bumpers and
/// the auto walk button while the worker keeps up with the cave.</para>
/// </summary>
internal static class Steam2026CorelCaveControllerTests
{
    private const int CaveFieldId = 628;
    private const uint FieldDataBase = 0x02000000;
    private const uint EventTable = 0x00090000;
    private const uint ScriptContext = 0x000A0000;
    private const int SdlA = 0;
    private const int SdlX = 2;
    private const int SdlLeftShoulder = 9;
    private const int SdlRightShoulder = 10;
    private const int SdlRightStick = 8;

    public static void Run()
    {
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot) || !Directory.Exists(Path.Combine(dataRoot, "data")))
        {
            Console.WriteLine("Corel Valley Cave controller: FF7_ACCESSIBILITY_DATA_ROOT is unset, native cases not run");
            return;
        }

        TheMenuStaysOpenAndAnswersInTheCave(Path.GetFullPath(dataRoot));
        TheMenuSurvivesASlowWorkerIteration(Path.GetFullPath(dataRoot));
        TheRefreshNeverOutlivesTheGuards(Path.GetFullPath(dataRoot));
        TheRenewalChecksNativeOwnershipFirst(Path.GetFullPath(dataRoot));
        if (Environment.GetEnvironmentVariable("CAVE_TIMING") is { Length: > 0 })
        {
            foreach (var (triangle, ladders) in new[] { (168, true), (132, true), (132, false), (157, true), (157, false) })
            {
                using var cave = CaveSession.Create(Path.GetFullPath(dataRoot), triangle, ladders, exitCues: true);
                cave.Warm(TimeSpan.FromSeconds(6));
                var ticks = cave.TickDurations.Skip(3).ToArray();
                Console.WriteLine(
                    $"timing triangle={triangle} ladderCues={ladders}: ticks={ticks.Length} " +
                    $"max={ticks.Max().TotalMilliseconds:0.0} p90={ticks.OrderBy(t => t).ElementAt(ticks.Length * 9 / 10).TotalMilliseconds:0.0} " +
                    $"median={Median(ticks).TotalMilliseconds:0.0} ms, longest publication gap={cave.MaximumPublicationGap.TotalMilliseconds:0} ms, " +
                    $"ladder cues={cave.Diagnostics.Count(line => line.Contains("ladder cue played", StringComparison.Ordinal))}");
            }
        }
    }

    private static void TheMenuStaysOpenAndAnswersInTheCave(string dataRoot)
    {
        using var cave = CaveSession.Create(dataRoot, playerTriangle: 168, ladderCues: true, exitCues: true);
        cave.Warm(TimeSpan.FromSeconds(2));

        var ticks = cave.TickDurations.Skip(3).ToArray();
        Console.WriteLine(
            $"Corel Valley Cave worker ticks: count={ticks.Length}, " +
            $"max={ticks.DefaultIfEmpty().Max().TotalMilliseconds:0.0} ms, " +
            $"median={Median(ticks).TotalMilliseconds:0.0} ms, " +
            $"transitions={cave.LastDiagnosticTransitions}");

        if (Environment.GetEnvironmentVariable("CAVE_DIAGNOSTICS") is { Length: > 0 } diagnosticsPath)
        {
            File.WriteAllLines(diagnosticsPath, cave.Diagnostics);
        }

        cave.Press(SdlRightStick);
        cave.RunFor(TimeSpan.FromMilliseconds(400));
        Equal(true, cave.Capture.IsOpen, $"R3 opens the menu in the cave ({cave.Speech})");
        cave.Press(SdlRightShoulder);
        cave.RunFor(TimeSpan.FromMilliseconds(900));
        Equal(true, cave.Capture.IsOpen, $"the menu is still open after the bumper ({cave.Speech})");
        Equal(false, cave.Spoken.Any(text => text == "Navigation menu closed."),
            $"the menu is never closed under the player ({cave.Speech})");
        Equal(true, cave.Spoken.Count(text => text.StartsWith("Navigation menu.", StringComparison.Ordinal) ||
                                               text.StartsWith("Exits", StringComparison.Ordinal) ||
                                               text.StartsWith("Objects", StringComparison.Ordinal) ||
                                               text.StartsWith("NPCs", StringComparison.Ordinal) ||
                                               text.StartsWith("Story", StringComparison.Ordinal)) >= 2,
            $"the bumper changed category ({cave.Speech})");
        cave.Press(SdlLeftShoulder);
        cave.RunFor(TimeSpan.FromMilliseconds(900));
        Equal(true, cave.Capture.IsOpen, $"the menu is still open after the other bumper ({cave.Speech})");
        cave.Press(SdlX);
        cave.RunFor(TimeSpan.FromMilliseconds(1200));
        Equal(true, cave.AutoWalkStarted, $"X started auto walk in the cave ({cave.Speech})");
        Console.WriteLine($"Corel Valley Cave speech: {string.Join(" | ", cave.Spoken)}; longest publication gap {cave.MaximumPublicationGap.TotalMilliseconds:0} ms; ticks max {cave.TickDurations.Max().TotalMilliseconds:0} ms");
        Equal(true, cave.MaximumPublicationGap < ControllerNavigationCapture.ContextFreshness,
            $"the worker kept the context fresh: longest gap {cave.MaximumPublicationGap.TotalMilliseconds:0} ms");
    }

    /// <summary>
    /// The session's worker takes its clock once, at the top of an iteration, and runs every
    /// other coordinator before the field's (Steam2026ResearchSession: now = DateTime.UtcNow,
    /// then dialogue, menus, cutscenes, footsteps and the world map, then the field). A context
    /// published with that clock is already as old as everything that ran first, and one is
    /// published only on a scan. Here each iteration spends 420 ms before the field tick: well
    /// inside the 750 ms the capture allows between publications, and more than half of it.
    /// The menu must stay open, answer the bumpers and start auto walk all the same.
    /// </summary>
    private static void TheMenuSurvivesASlowWorkerIteration(string dataRoot)
    {
        using var cave = CaveSession.Create(dataRoot, playerTriangle: 168, ladderCues: true, exitCues: true);
        cave.PreFieldWork = TimeSpan.FromMilliseconds(420);
        cave.Warm(TimeSpan.FromSeconds(2));
        cave.Press(SdlRightStick);
        cave.RunFor(TimeSpan.FromMilliseconds(1800));
        Equal(false, cave.Spoken.Any(text => text == "Navigation menu closed."),
            $"a slow iteration does not close the menu under the player ({cave.Speech}; longest publication gap {cave.MaximumPublicationGap.TotalMilliseconds:0} ms)");
        Equal(true, cave.Capture.IsOpen, $"R3 opened it and it stays open ({cave.Speech})");
        var categoriesBefore = CategorySpeech(cave);
        cave.Press(SdlRightShoulder);
        cave.RunFor(TimeSpan.FromMilliseconds(1800));
        Equal(true, cave.Capture.IsOpen, $"still open after R1 ({cave.Speech})");
        Equal(true, CategorySpeech(cave) > categoriesBefore, $"R1 changed the category ({cave.Speech})");
        var afterRight = CategorySpeech(cave);
        cave.Press(SdlLeftShoulder);
        cave.RunFor(TimeSpan.FromMilliseconds(1800));
        Equal(true, cave.Capture.IsOpen, $"still open after L1 ({cave.Speech})");
        Equal(true, CategorySpeech(cave) > afterRight, $"L1 changed the category back ({cave.Speech})");
        cave.Press(SdlX);
        cave.RunFor(TimeSpan.FromMilliseconds(2400));
        Equal(true, cave.AutoWalkStarted, $"X started auto walk ({cave.Speech})");
    }

    /// <summary>
    /// What keeps the context fresh must not keep the menu open where it has to close: the
    /// window losing the focus, the frame leaving the field module, or a native window taking
    /// the field. A field change is the generation guard's: the menu may stay open, but what
    /// is published belongs to the new field, so nothing queued in the cave is applied there,
    /// and the refresh never republishes the cave.
    /// </summary>
    private static void TheRefreshNeverOutlivesTheGuards(string dataRoot)
    {
        foreach (var (name, breakIt) in new (string, Action<CaveSession>)[]
                 {
                     ("the window loses the focus", cave => cave.Foreground = false),
                     ("the frame leaves the field module", cave => cave.FrameModule = 3),
                     ("a native window takes the field", cave => cave.OpenNativeWindow())
                 })
        {
            using var cave = CaveSession.Create(dataRoot, playerTriangle: 168, ladderCues: false, exitCues: false);
            cave.PreFieldWork = TimeSpan.FromMilliseconds(120);
            cave.Warm(TimeSpan.FromSeconds(1));
            cave.Press(SdlRightStick);
            cave.RunFor(TimeSpan.FromMilliseconds(600));
            Equal(true, cave.Capture.IsOpen, $"{name}: the menu opened first ({cave.Speech})");
            breakIt(cave);
            cave.RunFor(TimeSpan.FromMilliseconds(1500));
            Equal(false, cave.Capture.IsOpen, $"{name}: the menu closes ({cave.Speech})");
        }

        using var moving = CaveSession.Create(dataRoot, playerTriangle: 168, ladderCues: false, exitCues: false);
        moving.PreFieldWork = TimeSpan.FromMilliseconds(120);
        moving.Warm(TimeSpan.FromSeconds(1));
        moving.Press(SdlRightStick);
        moving.RunFor(TimeSpan.FromMilliseconds(600));
        var caveGeneration = moving.Capture.Generation;
        Equal(CaveFieldId, caveGeneration.Identity, "the cave's own generation is published first");
        moving.ChangeField(627);
        moving.RunFor(TimeSpan.FromMilliseconds(1500));
        var after = moving.Capture.Generation;
        Equal(627, after.Identity, "after the field changes, only the new field is published");
        Equal(true, after.Id != caveGeneration.Id, "as a new generation, so the cave's queued selections are not applied there");
    }

    /// <summary>
    /// What renews the context between scans must first see that nobody else owns the pad now:
    /// the field's checked control, window and film state, read on that tick. Here the scan runs
    /// once and then not for ten seconds, so only the renewal runs; the game opens its own
    /// window, locks control, starts a film, or cannot be read. The menu must close at once -
    /// the very next read hands the bumper to the game - not after the context has aged.
    /// </summary>
    private static void TheRenewalChecksNativeOwnershipFirst(string dataRoot)
    {
        var cases = new (string Name, Action<CaveSession> Take)[]
        {
            ("a native window opens", cave => cave.SetByte(FieldAudibleCueStateReader.AddressActiveFieldMessageCount, 1)),
            ("the field locks the player's control", cave => cave.SetByte(FieldAudibleCueStateReader.AddressUserControl, 1)),
            ("a film starts", cave => cave.SetWord(FieldAudibleCueStateReader.AddressFieldMovieActive, 1)),
            ("the ownership state cannot be read", cave => cave.MakeUnreadable(FieldAudibleCueStateReader.AddressActiveFieldMessageCount))
        };
        foreach (var (name, take) in cases.Prepend(("nothing changes", (Action<CaveSession>)(_ => { }))))
        {
            var now = new DateTime(2026, 9, 27, 13, 0, 0, DateTimeKind.Utc);
            using var cave = CaveSession.Create(dataRoot, playerTriangle: 168, ladderCues: false, exitCues: false,
                clock: () => now, scanIntervalMs: 10_000, pollOnItsOwn: false);
            cave.TickAt(now);
            for (var read = 0; read < 3; read++)
            {
                now = now.AddMilliseconds(16);
                _ = cave.GameReads(SdlA);
            }

            now = now.AddMilliseconds(16);
            _ = cave.GameReads(SdlA, SdlRightStick);
            now = now.AddMilliseconds(16);
            _ = cave.GameReads(SdlA);
            Equal(true, cave.Capture.IsOpen,
                $"{name}: R3 opened the menu (generation {cave.Capture.Generation}, refusal '{cave.Capture.LastRefusal}', close '{cave.Capture.LastCloseCause}', diagnostics {string.Join(" / ", cave.Diagnostics.Where(line => line.Contains("read failed") || line.Contains("state:")).TakeLast(2))})");
            now = now.AddMilliseconds(16);
            cave.TickAt(now);
            now = now.AddMilliseconds(16);
            Equal((byte)0, cave.GameReads(SdlRightShoulder, SdlRightShoulder), $"{name}: while it is open the bumper is the menu's");
            now = now.AddMilliseconds(16);
            _ = cave.GameReads(SdlA);

            take(cave);
            now = now.AddMilliseconds(100);
            cave.TickAt(now);
            now = now.AddMilliseconds(16);
            var bumper = cave.GameReads(SdlRightShoulder, SdlRightShoulder);
            if (name == "nothing changes")
            {
                Equal(true, cave.Capture.IsOpen, "with nobody else owning the pad, the renewal keeps the menu open");
                Equal((byte)0, bumper, "and the bumper stays the menu's");
            }
            else
            {
                Equal(false, cave.Capture.IsOpen, $"{name}: the menu closes on the first read after it");
                Equal((byte)1, bumper, $"{name}: and that very read gives the game its bumper");
            }
        }
    }

    private static int CategorySpeech(CaveSession cave) =>
        cave.Spoken.Count(text => text.StartsWith("Exits", StringComparison.Ordinal) ||
                                  text.StartsWith("Objects", StringComparison.Ordinal) ||
                                  text.StartsWith("NPCs", StringComparison.Ordinal) ||
                                  text.StartsWith("Story", StringComparison.Ordinal));

    private static TimeSpan Median(TimeSpan[] values) =>
        values.Length == 0 ? TimeSpan.Zero : values.OrderBy(value => value).ElementAt(values.Length / 2);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Corel Valley Cave controller: {label}: expected {expected}, got {actual}.");
        }
    }

    /// <summary>The cave as the game has it, the real coordinator, and a pad polled on its own thread.</summary>
    internal sealed class CaveSession : IDisposable
    {
        private readonly FieldObservationFixture fixture;
        private readonly Steam2026SdlControllerCaptureHook hook;
        private readonly Steam2026FieldNavigationCoordinator coordinator;
        private readonly NavigationAutoWalkController autoWalk;
        private readonly HashSet<int> held = [];
        private readonly object heldSync = new();
        private readonly List<string> spoken = [];
        private readonly Thread poller;
        private volatile bool stopping;
        private DateTime lastStamp = DateTime.MinValue;

        private CaveSession(
            FieldObservationFixture fixture,
            Steam2026SdlControllerCaptureHook hook,
            Steam2026FieldNavigationCoordinator coordinator,
            NavigationAutoWalkController autoWalk)
        {
            this.fixture = fixture;
            this.hook = hook;
            this.coordinator = coordinator;
            this.autoWalk = autoWalk;
            poller = new Thread(Poll) { IsBackground = true, Name = "cave pad poll" };
        }

        public List<TimeSpan> TickDurations { get; } = [];

        public TimeSpan MaximumPublicationGap { get; private set; }

        /// <summary>Wall-clock time an iteration spends in other coordinators before the field's.</summary>
        public TimeSpan PreFieldWork { get; set; } = TimeSpan.Zero;

        /// <summary>Whether the game's window has the focus.</summary>
        public bool Foreground
        {
            get => foregroundFlag.Value;
            set => foregroundFlag.Value = value;
        }

        /// <summary>The module the lifecycle frame reports.</summary>
        public byte FrameModule { get; set; } = FieldPositionReader.FieldModule;

        public void OpenNativeWindow() => fixture.WriteByte(FieldAudibleCueStateReader.AddressActiveFieldMessageCount, 1);

        public void ChangeField(int fieldId) =>
            fixture.Write((uint)FieldPositionReader.AddressFieldId, BitConverter.GetBytes((ushort)fieldId));

        private StrongBox<bool> foregroundFlag = new(true);

        public ControllerNavigationCapture Capture => hook.Capture!;

        public IReadOnlyList<string> Spoken
        {
            get
            {
                lock (spoken)
                {
                    return spoken.ToArray();
                }
            }
        }

        public string Speech => string.Join(" | ", Spoken.TakeLast(8));

        public IReadOnlyList<string> Diagnostics
        {
            get
            {
                lock (diagnosticSource)
                {
                    return diagnosticSource.ToArray();
                }
            }
        }

        public bool AutoWalkStarted => autoWalk.IsEnabledFor(NavigationAutoWalkDomain.Field);

        public string LastDiagnosticTransitions
        {
            get
            {
                lock (diagnosticSource)
                {
                    return diagnosticSource.LastOrDefault(line => line.Contains("transitions=", StringComparison.Ordinal)) is { } line
                        ? line[(line.IndexOf("transitions=", StringComparison.Ordinal) + 12)..].Split(',')[0]
                        : "unknown";
                }
            }
        }

        public static CaveSession Create(
            string dataRoot,
            int playerTriangle,
            bool ladderCues,
            bool exitCues,
            Func<DateTime>? clock = null,
            int scanIntervalMs = 50,
            bool pollOnItsOwn = true)
        {
            var fixture = FieldObservationFixture.CreatePopulated();
            // The engine's static state is all mapped in the game; only what a case sets is
            // written, so the rest reads as zero instead of as an unmapped page.
            foreach (var (start, end) in new (uint, uint)[]
                     {
                         (0x00CBF000, 0x00CC4000), (0x00CFF000, 0x00D00000), (0x00DC0000, 0x00DC2000),
                         (EventTable, EventTable + 0x2000), (ScriptContext, ScriptContext + 0x1000),
                         (FieldObservationFixture.TriggerPointer, FieldObservationFixture.TriggerPointer + 0x2000),
                         (FieldObservationFixture.FieldGlobalPointer, FieldObservationFixture.FieldGlobalPointer + 0x1000)
                     })
            {
                for (var page = start & ~0xFFFu; page < end; page += 0x1000)
                {
                    try
                    {
                        _ = fixture.GetHostAddress(page);
                    }
                    catch (InvalidOperationException)
                    {
                        fixture.Write(page, new byte[0x1000]);
                    }
                }
            }

            var source = new FlevelDataSource(dataRoot);
            if (!source.TryReadField(CaveFieldId, out var encoded))
            {
                throw new InvalidOperationException($"Installed field {CaveFieldId} is unavailable.");
            }

            var field = Ff7LzsDecoder.DecodeFieldFile(encoded);
            fixture.Write(FieldDataBase, field);
            fixture.Write((uint)FieldWalkmeshReader.AddressFieldDataPtr, BitConverter.GetBytes(FieldDataBase));
            var triggersSection = BitConverter.ToInt32(field, FieldWalkmeshReader.SectionOffsetsHeaderOffset + 7 * sizeof(int));
            fixture.Write(
                (uint)FieldNavigationControlReader.AddressFieldTriggersPtr,
                BitConverter.GetBytes(FieldDataBase + (uint)triggersSection + sizeof(int)));
            fixture.Write((uint)FieldPositionReader.AddressFieldId, BitConverter.GetBytes((ushort)CaveFieldId));
            // No walkmesh boundary switched on: the cave's triangles are all open at the start.
            fixture.Write(FieldObservationFixture.FieldGlobalPointer + FieldBoundaryStateReader.BoundaryBitsOffset, [0, 0]);
            fixture.Write((uint)FieldNavigationObjectReader.AddressFieldBankBase, BitConverter.GetBytes((ushort)677));

            // Cloud, controlled, standing on the triangle's centre.
            var walkmesh = new FieldWalkmeshReader(
                address => address == FieldWalkmeshReader.AddressFieldDataPtr
                    ? (int)FieldDataBase
                    : BitConverter.ToInt32(field, address - (int)FieldDataBase),
                address => BitConverter.ToInt16(field, address - (int)FieldDataBase))
                .Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, CaveFieldId, 0, 0, 0, 0, 0, 0)).Walkmesh!;
            var centre = walkmesh.Triangles[playerTriangle].GetCentroid();
            var (x, y, z) = ((int)Math.Round(centre.X), (int)Math.Round(centre.Y), (int)Math.Round(centre.Z));
            fixture.Write(FieldObservationFixture.ModelBase + FieldPositionReader.ModelXOffset, BitConverter.GetBytes(x));
            fixture.Write(FieldObservationFixture.ModelBase + FieldPositionReader.ModelYOffset, BitConverter.GetBytes(y));
            fixture.Write(FieldObservationFixture.ModelBase + FieldPositionReader.ModelZOffset, BitConverter.GetBytes(z));
            var objectBase = (uint)FieldPositionReader.AddressFieldModelsObjs + FieldPositionReader.FieldObjectStride;
            fixture.Write(objectBase + FieldPositionReader.ObjectXOffset, BitConverter.GetBytes(x * 4096));
            fixture.Write(objectBase + FieldPositionReader.ObjectYOffset, BitConverter.GetBytes(y * 4096));
            fixture.Write(objectBase + FieldPositionReader.ObjectZOffset, BitConverter.GetBytes(z * 4096));
            fixture.Write(objectBase + FieldPositionReader.ObjectTriangleOffset, BitConverter.GetBytes((ushort)playerTriangle));
            fixture.Write((uint)FieldNavigationObjectReader.AddressFieldEventDataPtr, BitConverter.GetBytes(EventTable));
            fixture.Write(EventTable, new byte[2 * FieldNavigationObjectReader.FieldEventDataStride]);
            fixture.Write(
                EventTable + FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationNpcReader.CollisionRadiusOffset,
                BitConverter.GetBytes((ushort)34));
            fixture.Write((uint)FieldControlledEntityReader.AddressScriptContextPointer, BitConverter.GetBytes(ScriptContext));
            fixture.Write(ScriptContext + FieldControlledEntityReader.ControlledModelOffset, [1]);
            for (var entity = 0; entity < 64; entity++)
            {
                fixture.WriteByte(FieldControlledEntityReader.AddressEntityModelIndices + entity, entity == 1 ? (byte)1 : (byte)0xFF);
                fixture.WriteByte(FieldScriptLineStateReader.AddressFieldLineIndexByEntity + entity, 0xFF);
            }

            fixture.WriteByte(FieldControlledEntityReader.AddressPartySlots, 0);
            fixture.Write((uint)FieldNavigationInputReader.AddressCurrentKeyInput, BitConverter.GetBytes(0u));

            // Every LINE the cave's Init scripts create, switched on at its own segment.
            var catalog = new FieldScriptNavigationCatalog(dataRoot);
            var line = 0;
            for (var entity = 0; entity < 64; entity++)
            {
                var init = catalog.ReadScriptOpcodes(CaveFieldId, entity, 0);
                if (init.FirstOrDefault(op => op.Opcode == 0xD0) is not { Bytes.Count: >= 13 } lineOp)
                {
                    continue;
                }

                var bytes = lineOp.Bytes.ToArray();
                fixture.WriteByte(FieldScriptLineStateReader.AddressFieldLineIndexByEntity + entity, (byte)line);
                fixture.Write(
                    (uint)(FieldScriptLineStateReader.AddressFieldLineSegments + line * FieldScriptLineStateReader.LineStateStride),
                    bytes[1..13]);
                fixture.WriteByte(FieldScriptLineStateReader.AddressFieldLineStates + line * FieldScriptLineStateReader.LineStateStride, 1);
                line++;
            }

            const uint processId = 42;
            var foreground = new StrongBox<bool>(true);
            var foregroundInput = new Steam2026ForegroundInputAdapter(
                () => foreground.Value ? (nint)1 : (nint)2,
                window => window == 1 ? processId : processId + 1,
                _ => 0,
                processId);
            var objectReader = new Steam2026FieldObjectObservationReader(fixture.Direct, _ => null, _ => null,
                FieldNavigationObjectCatalog.CreateAllFields());
            var autoWalk = new NavigationAutoWalkController(new AcceptingSink());
            CaveSession? session = null;
            var hook = Steam2026SdlControllerCaptureHook.CreateForDecideTest(
                installed => new ControllerNavigationCapture(
                    suppressor => new ControllerNavigationMenu(EmptyGamepadReader.Instance, suppressor),
                    installed),
                (_, button) => session!.IsHeld(button) ? (byte)1 : (byte)0,
                _ => 1,
                clock ?? (() => DateTime.UtcNow));
            var config = new AccessibilityConfig
            {
                EnableFieldNavigationAssistant = true,
                EnableFieldNavigationDiagnostics = true,
                EnableFieldExitProximityCues = exitCues,
                EnableFieldLadderProximityCues = ladderCues,
                FieldNavigationScanIntervalMs = scanIntervalMs
            };
            var spokenSink = new List<string>();
            var diagnosticSink = new List<string>();
            var coordinator = new Steam2026FieldNavigationCoordinator(
                config,
                fixture.Direct,
                foregroundInput,
                objectReader,
                dataRoot,
                AppContext.BaseDirectory,
                (text, _) => { lock (spokenSink) { spokenSink.Add(text); } },
                text => { lock (diagnosticSink) { diagnosticSink.Add(text); } },
                autoWalk: autoWalk,
                controllerCapture: () => hook.Capture,
                utcClock: clock);
            session = new CaveSession(fixture, hook, coordinator, autoWalk) { foregroundFlag = foreground };
            session.spoken.AddRange(spokenSink);
            session.BindSinks(spokenSink, diagnosticSink);
            if (pollOnItsOwn)
            {
                session.poller.Start();
            }
            return session;
        }

        private List<string> spokenSource = [];
        private List<string> diagnosticSource = [];

        private void BindSinks(List<string> spokenSink, List<string> diagnosticSink)
        {
            spokenSource = spokenSink;
            diagnosticSource = diagnosticSink;
        }

        private bool IsHeld(int button)
        {
            lock (heldSync)
            {
                return held.Contains(button);
            }
        }

        public void Warm(TimeSpan duration) => RunFor(duration);

        public void Press(int button)
        {
            lock (heldSync)
            {
                held.Add(button);
            }

            RunFor(TimeSpan.FromMilliseconds(120));
            lock (heldSync)
            {
                held.Remove(button);
            }
        }

        /// <summary>One worker tick at <paramref name="now"/>, on a clock the test owns.</summary>
        public void TickAt(DateTime now) => coordinator.Observe(FieldFrame(now, FrameModule, Foreground), now);

        /// <summary>
        /// One game read of <paramref name="button"/> with the given buttons held: what the game
        /// is given for it (0 when the menu keeps it from the game).
        /// </summary>
        public byte GameReads(int button, params int[] heldButtons)
        {
            lock (heldSync)
            {
                held.Clear();
                held.UnionWith(heldButtons);
            }

            return hook.InvokeGetButtonForTest(0x1234, button);
        }

        public void SetByte(int address, byte value) => fixture.WriteByte(address, value);

        public void SetWord(int address, ushort value) => fixture.Write((uint)address, BitConverter.GetBytes(value));

        public void MakeUnreadable(int address) => fixture.Direct.Forget((uint)address, 2);

        public void RunFor(TimeSpan duration)
        {
            var until = DateTime.UtcNow + duration;
            do
            {
                var now = DateTime.UtcNow;
                if (PreFieldWork > TimeSpan.Zero)
                {
                    Thread.Sleep(PreFieldWork);
                }

                var watch = Stopwatch.StartNew();
                coordinator.Observe(FieldFrame(now, FrameModule, Foreground), now);
                TickDurations.Add(watch.Elapsed);
                var stamp = Capture.Generation.StampUtc;
                if (lastStamp != DateTime.MinValue && stamp > lastStamp && stamp - lastStamp > MaximumPublicationGap)
                {
                    MaximumPublicationGap = stamp - lastStamp;
                }

                lastStamp = stamp;
                lock (spokenSource)
                {
                    lock (spoken)
                    {
                        spoken.Clear();
                        spoken.AddRange(spokenSource);
                    }
                }

                Thread.Sleep(16);
            }
            while (DateTime.UtcNow < until);
        }

        private void Poll()
        {
            while (!stopping)
            {
                // The game asks about one button at a time; the hook reads the whole pad.
                _ = hook.InvokeGetButtonForTest(0x1234, SdlA);
                Thread.Sleep(16);
            }
        }

        private static RuntimeFrameObservation FieldFrame(DateTime now, byte module, bool foreground) =>
            new(
                now,
                new GameLifecycleObservation(
                    IsForeground: foreground,
                    IsShuttingDown: false,
                    ModuleId: module,
                    Revision: 0),
                RuntimeDomainUpdate<MenuFrameObservation>.Unchanged,
                RuntimeDomainUpdate<DialoguePageObservation>.Unchanged,
                RuntimeDomainUpdate<FieldFrameObservation>.Unchanged,
                RuntimeDomainUpdate<BattleFrameObservation>.Unchanged,
                RuntimeDomainUpdate<NavigationWorldObservation>.Unchanged);

        public void Dispose()
        {
            stopping = true;
            if (poller.IsAlive)
            {
                poller.Join(TimeSpan.FromSeconds(2));
            }

            coordinator.Dispose();
            hook.Dispose();
        }
    }

    private sealed class AcceptingSink : IHighwayKeyboardInputSink
    {
        public HighwayKeyboardSendResult Send(IReadOnlyList<HighwayKeyboardTransition> transitions) =>
            new(transitions.Count, 0);
    }
}
