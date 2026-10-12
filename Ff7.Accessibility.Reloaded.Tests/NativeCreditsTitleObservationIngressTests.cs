using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class NativeCreditsTitleObservationIngressTests
{
    public static void Run()
    {
        foreach (var renderer in new[] { NativeCreditsTitleRenderer.LowResolution, NativeCreditsTitleRenderer.HighResolution })
        {
            var memory = new Memory(renderer);
            var calls = 0;
            var ingress = new NativeCreditsTitleObservationIngress(memory);
            var earliest = DateTime.UtcNow;
            ingress.OnCallback(renderer, () => calls++);
            Check(calls == 1, "actual draw forwarded once");
            Check(ingress.TryTakeLatest(out var seen) && seen?.Text == NativeCreditsTitleSnapshotReader.CaptionText
                && seen.ObservedUtc >= earliest, "caption timestamp follows observed renderer");
            Check(!ingress.TryTakeLatest(out _), "bounded latest observation consumed once");

            foreach (var (address, invalid) in new (uint, int)[]
            {
                (0xCBF9DC, 1), (0xF4F408, 0), (0xF4F414, 0), (0xF4F414, 3),
                (0xF4F43C, 2), (0xF4F404, 0), (0xF4F5B8, 1), (0xF4F5B8, -1)
            })
            {
                memory.Reset(renderer); memory.Set(address, invalid);
                var priorCalls = calls;
                ingress.OnCallback(renderer, () => calls++);
                Check(calls == priorCalls + 1, "invalid title gate preserves actual draw");
                Check(ingress.TryTakeLatest(out seen) && seen is null, "invalid title gate clears stale caption");
            }
            memory.Reset(renderer); memory.Set(0xF4F414, 1); memory.Set(0xF4F5B8, 23);
            ingress.OnCallback(renderer, () => memory.Set(0xF4F5B8, 0));
            Check(ingress.TryTakeLatest(out seen) && seen is null, "covered phase-one frame never announces");
            memory.Set(0xF4F414, 2);
            ingress.OnCallback(renderer, () => calls++);
            Check(ingress.TryTakeLatest(out seen) && seen is not null, "uncovered phase two is observed");
            foreach (var address in new uint[] { 0xF4F414, 0xF4F43C, 0xF4F404, 0xF4F5B8 })
            {
                memory.Reset(renderer);
                ingress.OnCallback(renderer, () => memory.Set(address, address == 0xF4F404 ? 0x20000 : 2));
                Check(ingress.TryTakeLatest(out seen) && seen is null, "changed draw state never becomes caption evidence");
            }
            memory.Reset(renderer);
            var wrongRenderer = renderer == NativeCreditsTitleRenderer.LowResolution
                ? NativeCreditsTitleRenderer.HighResolution : NativeCreditsTitleRenderer.LowResolution;
            ingress.OnCallback(wrongRenderer, () => calls++);
            Check(ingress.TryTakeLatest(out seen) && seen is null, "nonselected renderer never supplies title proof");
            ingress.OnCallback(renderer, () => calls++); ingress.Stop();
            Check(ingress.TryTakeLatest(out seen) && seen is null, "stop invalidates pending title");
            var reads = memory.Reads; var beforeCalls = calls;
            ingress.OnCallback(renderer, () => calls++);
            Check(calls == beforeCalls + 1 && memory.Reads == reads, "late callback only forwards original");
            Check(!ingress.TryTakeLatest(out _), "late callback never republishes caption");

            var retiring = new NativeCreditsTitleObservationIngress(memory);
            var readsAtRetirement = 0;
            retiring.OnCallback(renderer, () => { retiring.Stop(); readsAtRetirement = memory.Reads; });
            Check(retiring.TryTakeLatest(out seen) && seen is null, "stop during actual draw defeats publication");
            Check(memory.Reads == readsAtRetirement, "stop during original prevents subsequent native-state reads");
            var faulted = new NativeCreditsTitleObservationIngress(memory);
            var faults = 0;
            faulted.OnCallback(renderer, () => { faults++; throw new InvalidOperationException(); });
            Check(faults == 1 && faulted.HasOriginalFault, "original failure never retries draw");
            Check(faulted.TryTakeLatest(out seen) && seen is null, "original failure clears caption");
            memory.ThrowReads = true;
            var blind = new NativeCreditsTitleObservationIngress(memory);
            beforeCalls = calls;
            blind.OnCallback(renderer, () => calls++);
            Check(calls == beforeCalls + 1 && blind.TryTakeLatest(out seen) && seen is null,
                "unreadable native state still forwards actual draw");
        }
        Console.WriteLine("Native title observation: coherent draw gates, original forwarding and retirement passed.");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private sealed class Memory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];
        public int Reads;
        public bool ThrowReads;
        public Memory(NativeCreditsTitleRenderer renderer) => Reset(renderer);
        public void Reset(NativeCreditsTitleRenderer renderer)
        {
            Set(0xCBF9DC, 28); Set(0xF4F408, 1); Set(0xF4F414, 1);
            Set(0xF4F43C, renderer == NativeCreditsTitleRenderer.LowResolution ? 1 : 0);
            Set(0xF4F404, 0x10000); Set(0xF4F5B8, 0);
        }
        public void Set(uint address, int value)
        {
            Span<byte> raw = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(raw, value);
            for (uint i = 0; i < 4; i++) bytes[address + i] = raw[(int)i];
        }
        public bool TryRead(uint address, Span<byte> target)
        {
            Reads++; if (ThrowReads) throw new InvalidOperationException();
            for (uint i = 0; i < target.Length; i++)
            {
                if (!bytes.TryGetValue(address + i, out var value)) return false;
                target[(int)i] = value;
            }
            return true;
        }
    }
}
