using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using Ff7.Accessibility.Runtime.Abstractions;

namespace Ff7.Accessibility.Reloaded.Runtime;

public sealed record LegacyX86FingerprintResult(
    RuntimeIdentity Identity,
    bool IsSupported,
    string Diagnostic);

public static class LegacyX86Fingerprint
{
    public const ushort ImageFileMachineI386 = 0x014C;
    public const ushort ImageFileMachineAmd64 = 0x8664;
    public const string SupportedSha256 =
        "4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225";

    public static LegacyX86FingerprintResult Inspect(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var fullPath = Path.GetFullPath(executablePath);
        var machine = ReadMachine(fullPath);
        var sha256 = ComputeSha256(fullPath);
        var is64Bit = machine == ImageFileMachineAmd64;
        var isSupported = machine == ImageFileMachineI386
                          && string.Equals(sha256, SupportedSha256, StringComparison.Ordinal);
        var version = FileVersionInfo.GetVersionInfo(fullPath).FileVersion ?? string.Empty;
        var identity = new RuntimeIdentity(
            isSupported ? "ff7-legacy-x86" : "unsupported",
            fullPath,
            sha256,
            is64Bit,
            version);

        var diagnostic = isSupported
            ? "Supported legacy x86 FFVII executable."
            : machine != ImageFileMachineI386
                ? $"PE machine 0x{machine:X4} is not the supported x86 machine 0x{ImageFileMachineI386:X4}."
                : $"Legacy x86 SHA-256 {sha256} is not in the supported-build allowlist.";

        return new LegacyX86FingerprintResult(identity, isSupported, diagnostic);
    }

    private static ushort ReadMachine(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> dosHeader = stackalloc byte[64];
        stream.ReadExactly(dosHeader);
        if (dosHeader[0] != (byte)'M' || dosHeader[1] != (byte)'Z')
        {
            throw new InvalidDataException($"{path} does not have an MZ header.");
        }

        var peOffset = BinaryPrimitives.ReadInt32LittleEndian(dosHeader[0x3C..]);
        if (peOffset < dosHeader.Length || peOffset > stream.Length - 6)
        {
            throw new InvalidDataException($"{path} has an invalid PE header offset.");
        }

        stream.Position = peOffset;
        Span<byte> peHeader = stackalloc byte[6];
        stream.ReadExactly(peHeader);
        if (BinaryPrimitives.ReadUInt32LittleEndian(peHeader) != 0x00004550)
        {
            throw new InvalidDataException($"{path} does not have a PE signature.");
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(peHeader[4..]);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
