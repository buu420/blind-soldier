using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Reads the exact popup state that FFNx 1.24.3 renders at the bottom of the
/// game window. The three RVAs come from the matching shipped PDB and are
/// enabled only for the exact installed module fingerprint.
/// </summary>
internal sealed class FfnxPopupStateReader
{
    internal const string SupportedModuleSha256 =
        "7D7EC5997A4FE5C8F203D8ADF55E90C4663D0B30F9004426659AA7E38386397A";
    internal const uint PopupMessageRva = 0x0210BCB8;
    internal const uint PopupTtlRva = 0x0210C0B8;
    internal const uint PopupColorRva = 0x0210C0BC;
    internal const int PopupMessageCapacity = 1024;

    private readonly uint moduleBase;
    private readonly ILegacyAddressSpace memory;

    internal FfnxPopupStateReader(
        uint moduleBase,
        ILegacyAddressSpace memory)
    {
        if (moduleBase == 0
            || (ulong)moduleBase + PopupColorRva + sizeof(uint) > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(moduleBase));
        }

        this.moduleBase = moduleBase;
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    internal static bool TryCreate(
        Process process,
        ILegacyAddressSpace memory,
        out FfnxPopupStateReader reader,
        out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(memory);
        reader = null!;
        diagnostic = "FFNx is not loaded.";

        try
        {
            foreach (ProcessModule module in process.Modules)
            {
                string? productName = null;
                try
                {
                    productName =
                        FileVersionInfo.GetVersionInfo(module.FileName).ProductName;
                }
                catch
                {
                    // An unreadable module cannot prove the exact FFNx identity.
                }

                if (!FfnxRuntimeDetector.IsFfnxModule(
                        module.ModuleName,
                        productName))
                {
                    continue;
                }

                var hash = ComputeSha256(module.FileName);
                if (!string.Equals(
                        hash,
                        SupportedModuleSha256,
                        StringComparison.Ordinal))
                {
                    diagnostic =
                        $"FFNx popup speech is disabled for unsupported module SHA-256 {hash}.";
                    return false;
                }

                var baseAddress = unchecked((ulong)(nuint)module.BaseAddress);
                if (baseAddress == 0
                    || baseAddress > uint.MaxValue
                    || baseAddress + PopupColorRva + sizeof(uint) > uint.MaxValue)
                {
                    diagnostic = "The verified FFNx module base is outside the 32-bit address space.";
                    return false;
                }

                reader = new FfnxPopupStateReader((uint)baseAddress, memory);
                diagnostic =
                    $"Verified FFNx popup state at module base 0x{baseAddress:X8}.";
                return true;
            }
        }
        catch (Exception ex)
        {
            diagnostic = $"FFNx popup identity check failed: {ex.Message}";
            return false;
        }

        return false;
    }

    internal bool TryRead(out FfnxPopupSnapshot snapshot)
    {
        snapshot = default;
        LastReadWasDefinitelyHidden = false;
        if (!TryReadUInt32(PopupTtlRva, out var ttlBefore))
        {
            return false;
        }

        if (ttlBefore == 0)
        {
            LastReadWasDefinitelyHidden =
                TryReadUInt32(PopupTtlRva, out var hiddenTtlAfter)
                && hiddenTtlAfter == 0;
            return false;
        }

        Span<byte> firstText = stackalloc byte[PopupMessageCapacity];
        Span<byte> secondText = stackalloc byte[PopupMessageCapacity];
        if (!memory.TryRead(moduleBase + PopupMessageRva, firstText)
            || !TryReadUInt32(PopupColorRva, out var colorBefore)
            || !memory.TryRead(moduleBase + PopupMessageRva, secondText)
            || !TryReadUInt32(PopupTtlRva, out var ttlAfter)
            || !TryReadUInt32(PopupColorRva, out var colorAfter)
            || ttlBefore != ttlAfter
            || colorBefore != colorAfter
            || !firstText.SequenceEqual(secondText))
        {
            return false;
        }

        var terminator = firstText.IndexOf((byte)0);
        if (terminator <= 0)
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(firstText[..terminator]);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        snapshot = new FfnxPopupSnapshot(text, ttlAfter, colorAfter);
        return true;
    }

    internal bool LastReadWasDefinitelyHidden { get; private set; }

    private bool TryReadUInt32(uint rva, out uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        var success = memory.TryRead(moduleBase + rva, bytes);
        value = success
            ? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
            : 0;
        return success;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
