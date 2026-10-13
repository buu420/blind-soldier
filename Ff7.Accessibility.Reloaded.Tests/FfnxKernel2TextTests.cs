using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class FfnxKernel2TextTests
{
    internal static void Run()
    {
        BoundBackendWinsDiskAndStaysUnavailableAfterOwnershipLoss();
        var fixture = Environment.GetEnvironmentVariable("BLIND_SOLDIER_FFNX_KERNEL_FIXTURE");
        if (string.IsNullOrWhiteSpace(fixture))
        {
            Console.WriteLine("FFNx kernel native cohort integration skipped: set BLIND_SOLDIER_FFNX_KERNEL_FIXTURE to the licensed supported driver; backend consumer regression ran.");
            return;
        }
        LoadedHeapTextAndRelocatedOwnerAreRead();
        ReplacedPointerAndOwnerAreRejected();
        UnreadableAndInvalidHeapEntriesStayUnavailable();
        AlteredModuleAndAllocatorCohortAreRejected();
        VerifiedDriverCanWaitForItsLateGetterHook();
        Console.WriteLine("PASS FFNx kernel loaded text, relocated detour, pointer/owner/cohort changes and invalid entries.");

        Fixture New() => new(fixture);
        void VerifiedDriverCanWaitForItsLateGetterHook()
        {
            var f = New();
            var hook = new byte[5];
            Require(f.Memory.TryRead(0x419457, hook), "Native getter fixture is available.");
            f.Memory.Write(0x419457, [0x55,0x8B,0xEC,0x83,0xEC]);
            Require(FfnxKernel2TextReader.TryCreateVerifiedModule(f.Base, f.ImageSize,
                FfnxKernel2TextReader.SupportedModuleSha256, f.Memory, f.IsRegion,
                out var reader, out _, allowPendingOwnership: true) && reader is not null,
                "Exact relocated loader code can bind before its getter redirect is installed.");
            Require(!reader!.HasCurrentOwnership && reader.ReadEncoded(10, 1) is null,
                "A pending loader cannot publish text before the native getter belongs to it.");
            f.Memory.Write(0x419457, hook);
            Require(reader.HasCurrentOwnership && reader.ReadEncoded(10, 1) is not null,
                "The same binding becomes readable after native initialization installs its hook.");
        }
        void LoadedHeapTextAndRelocatedOwnerAreRead()
        {
            foreach (var baseAddress in new uint[] { 0x10000000, 0x20000000 })
            {
                var f = new Fixture(fixture, baseAddress);
                Require(f.Create(out var reader) && reader is not null, "Verified relocated FFNx detour must bind.");
                var owner = reader!;
                var consumer = new NativeKernel2TextReader(f.Memory);
                consumer.BindVerifiedBackend(owner.ReadEncoded, () => owner.HasCurrentOwnership);
                Require(consumer.ResolveItemName(1) == "Heap override item", "Heap owner text must be selected through the real decoder.");
                Require(owner.ReadEncoded(-1, 0) is null && owner.ReadEncoded(18, 0) is null &&
                    owner.ReadEncoded(10, 128) is null, "Out-of-bank entries cannot alias another section.");
            }
        }
        void ReplacedPointerAndOwnerAreRejected()
        {
            var f = New(); Require(f.Create(out var r), "Stable fixture binds.");
            var pointerReads = 0;
            f.Memory.BeforeRead = address => { if (address == f.SectionOwner && ++pointerReads == 2) f.Memory.UInt(address, 0x03010000); };
            Require(r!.ReadEncoded(10, 1) is null, "Changed heap owner during capture cannot publish old text.");
            f = New(); Require(f.Create(out r), "Stable fixture binds.");
            var textReads = 0;
            f.Memory.BeforeRead = address => { if (address == Fixture.TextAddress && ++textReads == 2) f.Memory.Write(0x419457, [0xE9, 0, 0, 0, 0]); };
            Require(r!.ReadEncoded(10, 1) is null, "Detour ownership lost during the second string read invalidates text.");
            f = New(); Require(f.Create(out r), "Stable fixture binds.");
            textReads = 0;
            f.Memory.BeforeRead = address => { if (address == Fixture.TextAddress && ++textReads == 2) f.Memory.UInt(f.SectionOwner, 0x03010000); };
            Require(r!.ReadEncoded(10, 1) is null, "Late owner replacement requires a final pointer bookend.");
        }
        void UnreadableAndInvalidHeapEntriesStayUnavailable()
        {
            var f = New(); Require(f.Create(out var r), "Stable fixture binds.");
            f.Memory.UInt(f.SectionOwner, 0);
            Require(r!.HasCurrentOwnership && r.ReadEncoded(10, 1) is null, "Recognized unloaded section is unavailable, not stock.");
            f = New(); Require(f.Create(out r), "Stable fixture binds.");
            f.Memory.Write(Fixture.Section, [0, 0]);
            Require(r!.ReadEncoded(10, 1) is null, "Missing offset table header cannot become an empty name.");
            f = New(); Require(f.Create(out r), "Stable fixture binds.");
            f.Memory.Write(Fixture.Section + 2, [2, 0]);
            Require(r!.ReadEncoded(10, 1) is null, "Entries pointing into offset metadata are rejected.");
            f = New(); Require(f.Create(out r), "Stable fixture binds.");
            f.Memory.Write(Fixture.TextAddress, new byte[512]);
            Require(r!.ReadEncoded(10, 1) is null, "Unterminated heap strings are bounded and unavailable.");
            f = New(); Require(f.Create(out r), "Stable fixture binds.");
            f.Memory.UInt(f.Base + 0x02111928, 0);
            Require(r!.ReadEncoded(10, 1) is null, "Reset section counter invalidates stale pointers.");
        }
        void AlteredModuleAndAllocatorCohortAreRejected()
        {
            var f = New();
            Require(!FfnxKernel2TextReader.TryCreateVerifiedModule(f.Base, f.ImageSize, "BAD", f.Memory, f.IsRegion,
                out _, out _), "An unrecognized module cannot authorize the FFNx pointer model.");
            f.Memory.Write(f.Base + 0x003BED20, [0]);
            Require(!f.Create(out _), "Unsupported section allocator cannot authorize known getter bytes.");
            f = New(); f.Memory.Write(0x419457, [0xE9, 0, 0, 0, 0]);
            Require(!f.Create(out _), "A detour owned by another module must not declare FFNx present.");
            f = New(); Require(f.Create(out var r), "Stable fixture binds.");
            f.Memory.Write(f.Base + 0x003BEDD0, [0]);
            Require(!r!.HasCurrentOwnership && r.ReadEncoded(10, 1) is null, "Replacement of lifecycle code after bind is unavailable.");
        }
    }

    private static void BoundBackendWinsDiskAndStaysUnavailableAfterOwnershipLoss()
    {
        var memory = new Memory(); memory.Write(0x419457, [0xE9, 0, 0, 0, 0]);
        var database = Disk(); database.UseNativeText(memory);
        var native = (NativeKernel2TextReader)typeof(Kernel2TextDatabase).GetField("nativeText", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(database)!;
        var owned = true; byte[]? bytes = Encode("Heap override item");
        native.BindVerifiedBackend((bank, id) => bank == 10 && id == 1 ? bytes : null, () => owned);
        Require(database.ResolveItemName(1) == "Heap override item", "Bound FFNx override must win over stale disk item.");
        bytes = null;
        Require(database.ResolveItemName(1) is null, "Unreadable bound FFNx entry must not fall back to disk.");
        owned = false;
        Require(native.HasSupportedLookup && database.ResolveItemName(1) is null, "Ownership loss after verified binding must not re-enable stale disk text.");
        owned = true; bytes = Encode("Restored override");
        Require(database.ResolveItemName(1) == "Restored override", "A restored coherent owner resumes selected text.");
    }
    private static Kernel2TextDatabase Disk()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        int[] counts = [32,256,128,128,32,32,96,64,32,256,128,128,32,32,96,64,128,16];
        foreach (var count in counts)
        {
            var text = Encode("Disk item"); writer.Write(count * 2 + text.Length);
            for (var i = 0; i < count; i++) writer.Write((ushort)(count * 2)); writer.Write(text);
        }
        return Kernel2TextDatabase.TryCreateFromDecodedKernel2(stream.ToArray())!;
    }
    private static byte[] Encode(string text) => [..text.Select(c => (byte)(c - 32)), 255];
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture
    {
        internal const uint Section = 0x03000000, TextAddress = Section + 256;
        internal uint Base, SectionOwner;
        internal int ImageSize;
        internal Memory Memory = new();
        internal Fixture(string path, uint baseAddress = 0x10000000)
        {
            var image = File.ReadAllBytes(path);
            Require(Convert.ToHexString(SHA256.HashData(image)) == FfnxKernel2TextReader.SupportedModuleSha256,
                "Private native fixture must be the independently verified installed driver.");
            Base = baseAddress; SectionOwner = Base + 0x021118D8 + 10 * 4;
            var pe = BitConverter.ToInt32(image, 0x3C); var optional = pe + 24;
            ImageSize = BitConverter.ToInt32(image, optional + 56);
            var preferred = BitConverter.ToUInt32(image, optional + 28);
            var sections = BitConverter.ToUInt16(image, pe + 6); var sectionHeaders = optional + BitConverter.ToUInt16(image, pe + 20);
            int Position(uint rva)
            {
                for (var i = 0; i < sections; i++)
                {
                    var at = sectionHeaders + i * 40; var va = BitConverter.ToUInt32(image, at + 12);
                    var size = Math.Max(BitConverter.ToUInt32(image, at + 8), BitConverter.ToUInt32(image, at + 16));
                    if (rva >= va && rva - va < size) return checked((int)(BitConverter.ToUInt32(image, at + 20) + rva - va));
                }
                throw new InvalidOperationException("Fixture RVA outside image sections.");
            }
            uint[] rvas = [0x003BED80, 0x003BED20, 0x003BEDD0]; int[] lengths = [70,91,100];
            for (var i = 0; i < rvas.Length; i++) Memory.Write(Base + rvas[i], image.AsSpan(Position(rvas[i]), lengths[i]).ToArray());
            var reloc = Position(BitConverter.ToUInt32(image, optional + 96 + 5 * 8));
            var end = reloc + BitConverter.ToInt32(image, optional + 96 + 5 * 8 + 4);
            while (reloc < end)
            {
                var page = BitConverter.ToUInt32(image, reloc); var length = BitConverter.ToInt32(image, reloc + 4); if (length < 8) break;
                for (var at = reloc + 8; at < reloc + length; at += 2)
                {
                    var item = BitConverter.ToUInt16(image, at); if (item >> 12 != 3) continue;
                    var rva = page + (uint)(item & 4095);
                    if (rvas.Select((start, i) => rva >= start && rva - start < lengths[i]).Any(inside => inside))
                        Memory.UInt(Base + rva, unchecked(BitConverter.ToUInt32(image, Position(rva)) - preferred + Base));
                }
                reloc += length;
            }
            var jump = new byte[5]; jump[0] = 0xE9;
            BinaryPrimitives.WriteInt32LittleEndian(jump.AsSpan(1), unchecked((int)(Base + 0x003BED80 - 0x419457 - 5)));
            Memory.Write(0x419457, jump); Memory.UInt(Base + 0x02111928, 18);
            Memory.UInt(SectionOwner, Section); Memory.Write(Section, [0, 1]); Memory.Write(Section + 2, [0, 1]);
            Memory.Write(TextAddress, Encode("Heap override item"));
        }
        internal bool IsRegion(uint address, int length, bool code) => code
            ? address >= Base && (ulong)address + (uint)length <= (ulong)Base + (uint)ImageSize || address == 0x419457
            : address >= Base && (ulong)address + (uint)length <= (ulong)Base + (uint)ImageSize ||
                address >= Section && (ulong)address + (uint)length <= Section + 65536;
        internal bool Create(out FfnxKernel2TextReader? reader) => FfnxKernel2TextReader.TryCreateVerifiedModule(Base,
            ImageSize, FfnxKernel2TextReader.SupportedModuleSha256, Memory, IsRegion, out reader, out _);
    }
    private sealed class Memory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> data = []; internal Action<uint>? BeforeRead;
        internal void Write(uint address, byte[] bytes) { for (var i = 0; i < bytes.Length; i++) data[address + (uint)i] = bytes[i]; }
        internal void UInt(uint address, uint value) => Write(address, BitConverter.GetBytes(value));
        public bool TryRead(uint address, Span<byte> destination)
        {
            BeforeRead?.Invoke(address);
            for (var i = 0; i < destination.Length; i++) if (!data.TryGetValue(address + (uint)i, out destination[i])) return false;
            return true;
        }
    }
}
