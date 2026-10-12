using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class NativeCreditsObservationIngressTests
{
    public static void Run()
    {
        ConsumerCannotPromoteUnrenderedRows();
        var memory = new Memory();
        var calls = 0;
        var ingress = new NativeCreditsObservationIngress(memory, () => calls++);
        ingress.OnCallback();
        Check(calls == 1, "Original forwarded once");
        Check(ingress.TryTakeLatest(out var first) && first is not null && first.VisibleRows.Count == 10, "Only drawn viewport rows captured");
        Check(!ingress.TryTakeLatest(out _), "Single snapshot consumed");
        ingress.OnCallback();
        memory.Set(NativeCreditsSnapshotReader.AddressCreditPhase, 3);
        ingress.OnCallback();
        Check(ingress.TryTakeLatest(out var invalid) && invalid is null, "Unreadable/end3 observation replaces stale rows with null");
        memory.Set(NativeCreditsSnapshotReader.AddressCreditPhase, 0);
        var moving = new NativeCreditsObservationIngress(memory, () => memory.Set(NativeCreditsSnapshotReader.AddressFirstRow, 1));
        moving.OnCallback();
        Check(moving.TryTakeLatest(out invalid) && invalid is null, "Changed render state rejected");
        memory.Set(NativeCreditsSnapshotReader.AddressFirstRow, 0);
        ingress.OnCallback(); ingress.Stop();
        Check(ingress.TryTakeLatest(out invalid) && invalid is null, "Stopping invalidates pending rows");
        var reads = memory.Reads;
        var beforeCalls = calls;
        ingress.OnCallback();
        Check(calls == beforeCalls + 1 && memory.Reads == reads, "Late callback forwards original without reading disposed state");
        Check(!ingress.TryTakeLatest(out _), "Late callback publishes no stale observation");
        NativeCreditsObservationIngress? retiring = null;
        retiring = new NativeCreditsObservationIngress(memory, () => retiring!.Stop());
        retiring.OnCallback();
        Check(retiring.TryTakeLatest(out invalid) && invalid is null, "Stop during original defeats pending capture");
        var faultCalls = 0;
        var failing = new NativeCreditsObservationIngress(memory, () => { faultCalls++; throw new InvalidOperationException(); });
        failing.OnCallback();
        Check(faultCalls == 1 && failing.HasOriginalFault, "Original failure not retried");
        Check(failing.TryTakeLatest(out invalid) && invalid is null, "Original failure invalidates rows");
        memory.ThrowReads = true;
        var blindCalls = 0;
        var blind = new NativeCreditsObservationIngress(memory, () => blindCalls++);
        blind.OnCallback();
        Check(blindCalls == 1 && blind.TryTakeLatest(out invalid) && invalid is null, "Read failure preserves original forwarding");
        Console.WriteLine("Native credits observation ingress tests passed.");
    }

    private static void ConsumerCannotPromoteUnrenderedRows()
    {
        var memory = new Memory();
        memory.Text(0, "OLD NAME");
        memory.Text(20, "NEW NAME");
        var spoken = new List<string>();
        using var narrator = new Ff7.Accessibility.Reloaded.NativeCreditsNarrator(() => null,
            text => { spoken.Add(text); return true; }, _ => { });
        var consumer = new Ff7.Accessibility.Reloaded.NativeCreditsObservationConsumer(memory, narrator);
        var now = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
        consumer.Tick(true, new(0, 0, [new(0, 0, "OLD NAME", 0)], now), now, true, true);
        now = now.AddSeconds(4);
        consumer.Tick(true, new(0, 0, [new(0, 0, "OLD NAME", 0)], now), now, true, true);
        memory.Set(NativeCreditsSnapshotReader.AddressFirstRow, 20);
        consumer.Tick(false, null, now.AddMilliseconds(150), true, true);
        Check(!spoken.Any(text => text.Contains("NEW NAME")), "native updates cannot promote rows absent from the renderer observation");
        now = now.AddSeconds(1);
        consumer.Tick(true, new(20, 0, [new(20, 0, "NEW NAME", 0)], now), now, true, true);
        Check(spoken.Last() == "NEW NAME", "a fresh actual renderer observation makes the new visible name readable");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Memory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];
        public int Reads;
        public bool ThrowReads;
        public Memory()
        {
            Set(NativeCreditsSnapshotReader.AddressCurrentModule, 0x1C);
            Set(NativeCreditsSnapshotReader.AddressCreditMode, 1); Set(NativeCreditsSnapshotReader.AddressCreditPhase, 0);
            Set(NativeCreditsSnapshotReader.AddressViewportHeight, 240); Set(NativeCreditsSnapshotReader.AddressPeoplePointer, 0x10000);
            Set(NativeCreditsSnapshotReader.AddressFirstRow, 0); Set(NativeCreditsSnapshotReader.AddressScrollOffset, 0);
            Set(NativeCreditsSnapshotReader.AddressScrolling, 1);
            for (uint row = 0; row < 32; row++)
            {
                for (uint i = 0; i < 64; i++) bytes[0x10000 + row * 64 + i] = 0;
                bytes[0x10000 + row * 64 + 12] = 0; bytes[0x10000 + row * 64 + 13] = 0x7F;
                Set(0x10000 + row * 64 + 60, 24);
            }
        }
        public void Set(uint address, int value)
        {
            Span<byte> raw = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(raw, value);
            for (uint i = 0; i < 4; i++) bytes[address + i] = raw[(int)i];
        }
        public void Text(uint row, string text)
        {
            var address = 0x10000 + row * 64 + 12;
            for (uint i = 0; i < 48; i++) bytes[address + i] = 0;
            for (var i = 0; i < text.Length; i++) bytes[address + (uint)i] = text[i] == ' ' ? (byte)0x2F : (byte)(text[i] - 'A');
            bytes[address + (uint)text.Length] = 0x7F;
        }
        public bool TryRead(uint address, Span<byte> target)
        {
            Reads++; if (ThrowReads) throw new InvalidOperationException();
            for (uint i = 0; i < target.Length; i++) { if (!bytes.TryGetValue(address + i, out var value)) return false; target[(int)i] = value; }
            return true;
        }
    }
}
