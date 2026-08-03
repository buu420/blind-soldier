using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using Ff7.Accessibility.Runtime.Abstractions;

namespace Ff7.Accessibility.Steam2026X64;

public sealed class Steam2026FingerprintResult
{
    internal Steam2026FingerprintResult(
        RuntimeIdentity identity,
        bool isSupported,
        string diagnostic)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        IsSupported = isSupported;
        Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
    }

    public RuntimeIdentity Identity { get; }

    public bool IsSupported { get; }

    public string Diagnostic { get; }
}

public static class Steam2026Fingerprint
{
    public const ushort ImageFileMachineI386 = 0x014C;
    public const ushort ImageFileMachineAmd64 = 0x8664;
    public const string SupportedSha256 =
        "57A23D166D69E46B9E3339F779D4A3C4FEB402A989FA7291D0D9B4A1953ABB4B";
    public const string SupportedRuntimeId = "ff7-steam-2026-x64";

    public static Steam2026FingerprintResult Inspect(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var fullPath = Path.GetFullPath(executablePath);
        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        var before = ReadInspectionStamp(stream.SafeFileHandle);
        if (stream.Length < 64 || stream.Length > int.MaxValue)
        {
            throw new InvalidDataException($"{fullPath} has an unsupported executable size.");
        }

        var snapshot = GC.AllocateUninitializedArray<byte>(checked((int)stream.Length));
        stream.ReadExactly(snapshot);
        var machine = ReadMachine(snapshot, fullPath);
        var sha256 = Convert.ToHexString(SHA256.HashData(snapshot));
        var is64Bit = machine == ImageFileMachineAmd64;
        var isSupported = is64Bit
                          && string.Equals(sha256, SupportedSha256, StringComparison.Ordinal);
        // FileVersionInfo is path-based and would require reopening a name that is not
        // part of the trusted snapshot. Version text is therefore intentionally omitted.
        const string version = "";
        var after = ReadInspectionStamp(stream.SafeFileHandle);
        if (before != after || stream.Length != snapshot.Length)
        {
            throw new IOException($"{fullPath} changed while its executable identity was inspected.");
        }

        var identity = new RuntimeIdentity(
            isSupported ? SupportedRuntimeId : "unsupported",
            fullPath,
            sha256,
            is64Bit,
            version);
        var diagnostic = isSupported
            ? "Supported native Steam 2026 x64 FFVII executable."
            : machine != ImageFileMachineAmd64
                ? $"PE machine 0x{machine:X4} is not the supported x64 machine 0x{ImageFileMachineAmd64:X4}."
                : $"Native x64 SHA-256 {sha256} is not in the supported-build allowlist.";

        return new Steam2026FingerprintResult(identity, isSupported, diagnostic);
    }

    private static ushort ReadMachine(ReadOnlySpan<byte> image, string path)
    {
        if (image[0] != (byte)'M' || image[1] != (byte)'Z')
        {
            throw new InvalidDataException($"{path} does not have an MZ header.");
        }

        var peOffset = BinaryPrimitives.ReadInt32LittleEndian(image[0x3C..]);
        if (peOffset < 64 || peOffset > image.Length - 6)
        {
            throw new InvalidDataException($"{path} has an invalid PE header offset.");
        }

        var peHeader = image.Slice(peOffset, 6);
        if (BinaryPrimitives.ReadUInt32LittleEndian(peHeader) != 0x00004550)
        {
            throw new InvalidDataException($"{path} does not have a PE signature.");
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(peHeader[4..]);
    }

    private static FileInspectionStamp ReadInspectionStamp(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var information))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return new FileInspectionStamp(
            information.VolumeSerialNumber,
            information.FileIndexHigh,
            information.FileIndexLow,
            information.FileSizeHigh,
            information.FileSizeLow,
            information.LastWriteTimeHigh,
            information.LastWriteTimeLow);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation information);

    private readonly record struct FileInspectionStamp(
        uint VolumeSerialNumber,
        uint FileIndexHigh,
        uint FileIndexLow,
        uint FileSizeHigh,
        uint FileSizeLow,
        uint LastWriteTimeHigh,
        uint LastWriteTimeLow);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
