using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime.Movies;
using Ff7.Accessibility.Steam2026X64.Runtime.Saves;

internal static class GogCompatibilityTests
{
    public static void Run()
    {
        foreach (var hash in new[] { Steam2026Fingerprint.SupportedSha256, Steam2026Fingerprint.GogSha256 })
        {
            var fingerprint = new Steam2026FingerprintResult(new RuntimeIdentity(
                Steam2026Fingerprint.SupportedRuntimeId, Path.GetFullPath("FFVII.exe"), hash, true, ""), true, "test identity");
            _ = new Steam2026X64RuntimeBackend(fingerprint);
            var contract = new NativeMovieCallbackContract(fingerprint, 0x140000000, 0x4000000, new UnreadableMemory());
            Check(!contract.HasExpectedSignature(NativeMovieCallbackKind.Prepare),
                "an allowed fingerprint does not bypass native callback code checks");
            var validCodeContract = new NativeMovieCallbackContract(fingerprint, 0x140000000, 0x4000000, new PrefixMemory());
            Check(validCodeContract.TryValidateIdentity(NativeMovieCallbackKind.Prepare, out var identity) &&
                identity.RuntimeSha256 == hash, "movie identity proofs retain the actual edition's fingerprint");
            try
            {
                _ = ValidatedTranslatedX86AddressSpaceFactory.Create(fingerprint, 0x140000000, new UnreadableMemory());
                throw new Exception("an unreadable resolver was accepted");
            }
            catch (InvalidOperationException ex)
            {
                Check(ex.Message.Contains("resolver signature", StringComparison.Ordinal),
                    "both approved editions still require a verified resolver");
            }
        }
        Check(!Steam2026Fingerprint.IsSupportedSha256(new string('A', 64)), "unknown executables remain unsupported");
        Check(!Steam2026Fingerprint.IsSupportedSha256(Steam2026Fingerprint.GogSha256.ToLowerInvariant()),
            "runtime identities use canonical fingerprints");
        var seen = new List<string>();
        var discovery = new Steam2026SaveCandidateDiscovery(Path.GetTempPath(), "12345", path =>
        {
            seen.Add(path); return path.EndsWith("save00.ff7", StringComparison.Ordinal);
        }, Steam2026SaveCandidateDiscovery.GogGameDirectoryName);
        Check(discovery.TryDiscover(out var saves) && saves.Count == 1, "native GOG save candidates are discovered");
        Check(seen.All(path => path.Contains("FINAL FANTASY VII GOG Edition", StringComparison.Ordinal)),
            "GOG discovery does not scan Steam saves");
        Console.WriteLine("PASS GOG exact fingerprint, native guards and save directory selection (synthetic identity).");
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
    }

    private sealed class UnreadableMemory : INativeMemoryReader
    {
        public bool TryReadUInt64(ulong address, out ulong value) { value = 0; return false; }
        public bool TryRead(ulong address, Span<byte> destination) => false;
        public bool TryQueryRegion(ulong address, out NativeMemoryRegion region) { region = default; return false; }
    }

    private sealed class PrefixMemory : INativeMemoryReader
    {
        public bool TryReadUInt64(ulong address, out ulong value) { value = 0; return false; }
        public bool TryRead(ulong address, Span<byte> destination)
        {
            var prefix = Convert.FromHexString("48895C2418555657415641574883EC60");
            if (address != 0x140000000 + NativeMovieCallbackContract.PrepareRva || destination.Length != prefix.Length)
                return false;
            prefix.CopyTo(destination);
            return true;
        }
        public bool TryQueryRegion(ulong address, out NativeMemoryRegion region)
        {
            region = new(0x140000000, 0x4000000, 0x140000000, true, true, true, true);
            return true;
        }
    }
}
