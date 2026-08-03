using System.Diagnostics;
using System.Security.Cryptography;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

internal static class FfnxVoicePlaybackHookTarget
{
    internal const uint PlayVoiceRva = 0x004187E0;
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
                if (!string.Equals(hash, FfnxPopupStateReader.SupportedModuleSha256, StringComparison.Ordinal))
                {
                    diagnostic = $"FFNx play_voice hook disabled for unsupported module SHA-256 {hash}.";
                    return false;
                }

                var baseAddress = unchecked((ulong)(nuint)module.BaseAddress);
                var target = baseAddress + PlayVoiceRva;
                if (baseAddress == 0 || target > int.MaxValue)
                {
                    diagnostic = "Verified FFNx play_voice target is outside the 32-bit hook address range.";
                    return false;
                }

                Span<byte> actual = stackalloc byte[PristinePrefix.Length];
                if (!memory.TryRead((uint)target, actual) || !actual.SequenceEqual(PristinePrefix))
                {
                    diagnostic =
                        $"Verified FFNx play_voice prefix mismatch at RVA 0x{PlayVoiceRva:X8}; hook not installed.";
                    return false;
                }

                absoluteAddress = (int)target;
                diagnostic =
                    $"Verified FFNx play_voice at module base 0x{baseAddress:X8}, RVA 0x{PlayVoiceRva:X8}.";
                return true;
            }
        }
        catch (Exception ex)
        {
            diagnostic = $"FFNx play_voice identity check failed: {ex.Message}";
            return false;
        }

        return false;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
