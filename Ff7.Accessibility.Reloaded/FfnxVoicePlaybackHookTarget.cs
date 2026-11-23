using System.Diagnostics;
using System.Security.Cryptography;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

internal static class FfnxVoicePlaybackHookTarget
{
    internal const uint PlayVoiceRva = 0x004187E0;
    internal const string Ffnx1252Sha256 = "9C9859B1F66EE3A25D29BD5FC43171A3C298F4372A6AEE589808E8EC7431C77B";
    internal const uint Ffnx1252PlayVoiceRva = 0x00442C50;
    private static ReadOnlySpan<byte> PristinePrefix =>
    [
        // Stop before the following relocated absolute address.
        0x55, 0x8B, 0xEC, 0x81, 0xEC, 0x0C, 0x01, 0x00, 0x00
    ];

    internal static bool TryResolve(
        Process process,
        ILegacyAddressSpace memory,
        out int absoluteAddress,
        out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(memory);
        absoluteAddress = 0;
        diagnostic = "FFNx is not loaded.";

        try
        {
            foreach (ProcessModule module in process.Modules)
            {
                string? productName = null;
                try
                {
                    productName = FileVersionInfo.GetVersionInfo(module.FileName).ProductName;
                }
                catch
                {
                    // An unreadable version record cannot establish identity.
                }

                if (!FfnxRuntimeDetector.IsFfnxModule(module.ModuleName, productName))
                {
                    continue;
                }

                var hash = ComputeSha256(module.FileName);
                var baseAddress = unchecked((ulong)(nuint)module.BaseAddress);
                return TryResolveModule(hash, baseAddress, memory, out absoluteAddress, out diagnostic);
            }
        }
        catch (Exception ex)
        {
            diagnostic = $"FFNx play_voice identity check failed: {ex.Message}";
            return false;
        }

        return false;
    }

    internal static bool TryResolveModule(string hash, ulong baseAddress, ILegacyAddressSpace memory,
        out int absoluteAddress, out string diagnostic)
    {
        absoluteAddress = 0;
        // Both release profiles have a PDB-confirmed bool __cdecl(char*,byte,byte,byte)
        // ABI and the same non-relocated prologue. Unknown builds remain disabled.
        var rva = hash switch
        {
            FfnxPopupStateReader.SupportedModuleSha256 => PlayVoiceRva,
            Ffnx1252Sha256 => Ffnx1252PlayVoiceRva,
            _ => 0u,
        };
        if (rva == 0)
        {
            diagnostic = $"FFNx play_voice hook disabled for unsupported module SHA-256 {hash}.";
            return false;
        }
        if (baseAddress == 0 || baseAddress > (ulong)int.MaxValue - rva)
        {
            diagnostic = "Verified FFNx play_voice target is outside the 32-bit hook address range.";
            return false;
        }
        var target = (uint)(baseAddress + rva);
        Span<byte> actual = stackalloc byte[PristinePrefix.Length];
        if (!memory.TryRead(target, actual) || !actual.SequenceEqual(PristinePrefix))
        {
            diagnostic = $"Verified FFNx play_voice prefix mismatch at RVA 0x{rva:X8}; hook not installed.";
            return false;
        }
        absoluteAddress = (int)target;
        diagnostic = $"Verified FFNx play_voice at module base 0x{baseAddress:X8}, RVA 0x{rva:X8}.";
        return true;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
