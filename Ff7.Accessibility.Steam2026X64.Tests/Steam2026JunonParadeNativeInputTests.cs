using System.Reflection;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;
using Ff7.Accessibility.Steam2026X64.Runtime.Input;

internal static class Steam2026JunonParadeNativeInputTests
{
    internal static void Run()
    {
        using (var host = new Host())
        {
            host.Frame();
            host.AssertMoving("first parade frame");
            for (var index = 1; index <= 12; index++)
            {
                host.SetPlayerY(-200 - 4 * index);
                host.Frame(100, FieldNavigationInputReader.UpMask);
                host.AssertMoving("coordinator renews the same direction beyond 500 ms");
            }
            Require(host.Sink.AcceptedPresses == 1, "lease test must hold one press without reassertion");
            host.Memory.Readable = false;
            host.Frame();
            host.AssertReleased("unreadable snapshot releases movement immediately");
        }

        using (var host = new Host())
        {
            Require(host.AutoWalk.TryStart(NavigationAutoWalkDomain.Field, true), "start preceding route");
            Require(host.AutoWalk.Drive(FieldNavigationInput.Up, true, true).Success, "preceding route holds Up");
            host.AssertMoving("preceding route established");
            host.Frame();
            Require(!host.AutoWalk.Enabled, "parade retires ordinary autowalk");
            host.AssertMoving("ordinary autowalk release cannot erase the parade's same direction");
        }

        foreach (var stop in new[] { "dialogue", "control", "movie", "focus", "reset", "module", "shutdown" })
        {
            using var host = new Host();
            host.Frame();
            host.AssertMoving("establish movement before " + stop);
            switch (stop)
            {
                case "dialogue":
                    host.Fixture.WriteByte(FieldAudibleCueStateReader.AddressActiveFieldMessageCount, 2);
                    host.Fixture.Write((uint)FieldMessageReader.AddressFieldWindowStates, BitConverter.GetBytes(uint.MaxValue));
                    break;
                case "control": host.Fixture.WriteByte(FieldAudibleCueStateReader.AddressUserControl, 1); break;
                case "movie": host.Fixture.Write((uint)FieldAudibleCueStateReader.AddressFieldMovieActive, BitConverter.GetBytes((ushort)1)); break;
                case "focus": host.Foreground = false; break;
                case "reset": host.Coordinator.Reset(); break;
                case "module": host.Module = 2; break;
                case "shutdown": host.ShuttingDown = true; break;
            }
            if (stop != "reset") host.Frame();
            // Restore focus without another coordinator frame: suppression alone must
            // not conceal a held key that could reappear when the game gets focus back.
            host.Foreground = true;
            host.AssertReleased(stop + " removes the owned direction");
        }
    }

    private sealed class Host : IDisposable
    {
        private const uint EventTable = 0x00080000;
        private const uint FieldData = 0x00090000;
        private readonly uint playerObject = (uint)FieldPositionReader.AddressFieldModelsObjs + FieldPositionReader.FieldObjectStride;
        private DateTime now = new(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);
        internal readonly FieldObservationFixture Fixture = FieldObservationFixture.CreatePopulated();
        internal readonly ReadableMemory Memory;
        internal readonly Steam2026NativeDirectionalInputSink Sink;
        internal readonly NavigationAutoWalkController AutoWalk;
        internal readonly Steam2026FieldNavigationCoordinator Coordinator;
        internal bool Foreground = true;
        internal bool ShuttingDown;
        internal int Module = FieldPositionReader.FieldModule;

        internal Host()
        {
            Fixture.Write((uint)FieldPositionReader.AddressFieldId, BitConverter.GetBytes(JunonMinigameStateReader.WelcomeParadeFieldId));
            Fixture.Write((uint)JunonMinigameStateReader.AddressTemporaryFieldBank, new byte[64]);
            Fixture.WriteByte(JunonMinigameStateReader.AddressTemporaryFieldBank + 28, 1);
            Fixture.WriteByte(JunonMinigameStateReader.AddressTemporaryFieldBank + 35, 1);
            Fixture.Write(FieldObservationFixture.TriggerPointer + FieldNavigationControlReader.ControlDirectionOffset, [0]);
            Fixture.Write(FieldObservationFixture.FieldGlobalPointer + FieldBoundaryStateReader.BoundaryBitsOffset, [0, 0]);
            Fixture.Write(playerObject + FieldPositionReader.ObjectXOffset, BitConverter.GetBytes(5 << 12));
            Fixture.Write(playerObject + FieldPositionReader.ObjectZOffset, BitConverter.GetBytes(0));
            Fixture.Write(playerObject + FieldPositionReader.ObjectTriangleOffset, BitConverter.GetBytes((ushort)0));
            SetPlayerY(-200);
            Fixture.Write((uint)FieldNavigationObjectReader.AddressFieldEventDataPtr, BitConverter.GetBytes(EventTable));
            Fixture.Write(EventTable, new byte[2 * FieldNavigationObjectReader.FieldEventDataStride]);
            Fixture.WriteByte(FieldNavigationObjectReader.AddressFieldModelIdArray + 27, 0);
            Fixture.Write(EventTable + FieldNavigationObjectReader.PositionXOffset, BitConverter.GetBytes(32 << 12));
            Fixture.Write(EventTable + FieldNavigationObjectReader.PositionYOffset, BitConverter.GetBytes(-427 << 12));

            // A single clear triangle isolates input delivery from route geometry.
            Fixture.Write((uint)FieldWalkmeshReader.AddressFieldDataPtr, BitConverter.GetBytes(FieldData));
            var sectionEntry = FieldData + FieldWalkmeshReader.SectionOffsetsHeaderOffset + 4 * FieldWalkmeshReader.WalkmeshSectionIndex;
            Fixture.Write(sectionEntry, BitConverter.GetBytes(64));
            Fixture.Write(sectionEntry + 4, BitConverter.GetBytes(128));
            Fixture.Write(FieldData + 68, BitConverter.GetBytes(1));
            var vertex = FieldData + 72;
            foreach (var point in new[] { (-2000, -2000), (2000, -2000), (0, 2000) })
            {
                Fixture.Write(vertex, BitConverter.GetBytes((short)point.Item1));
                Fixture.Write(vertex + 2, BitConverter.GetBytes((short)point.Item2));
                Fixture.Write(vertex + 4, BitConverter.GetBytes((short)0));
                vertex += FieldWalkmeshReader.VertexSize;
            }
            Fixture.Write(vertex, new byte[] { 255, 255, 255, 255, 255, 255 });
            Fixture.Write(HighwayDirectionInputMappingResolver.MappingTableAddress,
                new byte[HighwayDirectionInputMappingResolver.MappingTableSize]);
            foreach (var entry in new[] { (12, 0x48u), (13, 0x4Du), (14, 0x50u), (15, 0x4Bu) })
                Fixture.Write(HighwayDirectionInputMappingResolver.MappingTableAddress + (uint)(4 * entry.Item1), BitConverter.GetBytes(entry.Item2));

            Memory = new ReadableMemory(Fixture.Direct);
            Sink = new Steam2026NativeDirectionalInputSink(() => Foreground, () => now);
            Sink.AttachOverlay(() => true);
            AutoWalk = NavigationAutoWalkController.CreateCurrentProcess(Memory, Sink);
            Coordinator = new Steam2026FieldNavigationCoordinator(
                new AccessibilityConfig
                {
                    EnableFieldNavigationAssistant = true,
                    EnableFieldExitProximityCues = false,
                    EnableFieldLadderProximityCues = false,
                    EnableFieldSwingingBarTimingCue = false,
                    EnableSquatMinigamePrompts = false,
                    EnableFloor60SoldierTurnCue = false,
                    EnableJunonMinigamePrompts = false,
                    EnableJunonTimingCue = false,
                    EnableJunonParadeAlignmentAssist = true
                },
                Memory,
                new Steam2026ForegroundInputAdapter(() => (nint)1, _ => Foreground ? 42u : 0u, _ => (short)0, 42),
                new Steam2026FieldObjectObservationReader(Memory, _ => null, _ => null, Array.Empty<FieldNavigationObjectDefinition>()),
                Path.GetTempPath(), AppContext.BaseDirectory, (_, _) => { }, _ => { },
                autoWalk: AutoWalk, directionalInput: Sink);

            // Fail before observing if an old build would send real Windows keys.
            var assist = GetField(Coordinator, "junonParadeAlignmentAssist");
            Require(ReferenceEquals(Sink, GetField(GetField(assist, "keys"), "sink")), "coordinator must use native input");
        }

        internal void SetPlayerY(int y) => Fixture.Write(playerObject + FieldPositionReader.ObjectYOffset, BitConverter.GetBytes(y << 12));

        internal void Frame(int milliseconds = 0, uint held = 0)
        {
            now = now.AddMilliseconds(milliseconds);
            Fixture.Write((uint)FieldNavigationInputReader.AddressCurrentKeyInput, BitConverter.GetBytes(held));
            Coordinator.Observe(new RuntimeFrameObservation(now,
                new GameLifecycleObservation(Foreground, ShuttingDown, Module, 0),
                RuntimeDomainUpdate<MenuFrameObservation>.Unchanged,
                RuntimeDomainUpdate<DialoguePageObservation>.Unchanged,
                RuntimeDomainUpdate<FieldFrameObservation>.Unchanged,
                RuntimeDomainUpdate<BattleFrameObservation>.Unchanged,
                RuntimeDomainUpdate<NavigationWorldObservation>.Unchanged), now);
        }

        internal void AssertMoving(string reason)
        {
            Span<byte> tokens = stackalloc byte[4];
            Require(Sink.TryTakeDesired(now, tokens, out var count) && count == 1 && tokens[0] == 0x48, reason);
        }

        internal void AssertReleased(string reason)
        {
            Span<byte> tokens = stackalloc byte[4];
            Require(!Sink.TryTakeDesired(now, tokens, out _), reason);
        }

        public void Dispose() => Coordinator.Dispose();
    }

    private sealed class ReadableMemory(ILegacyAddressSpace inner) : ILegacyAddressSpace
    {
        internal bool Readable = true;
        public bool TryRead(uint address, Span<byte> destination)
        {
            if (Readable) return inner.TryRead(address, destination);
            destination.Clear();
            return false;
        }
    }

    private static object GetField(object owner, string name) => owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Junon coordinator: " + message);
    }
}
