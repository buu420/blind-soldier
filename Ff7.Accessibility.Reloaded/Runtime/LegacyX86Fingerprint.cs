using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public const string SupportedSha256 = SupportedHostsGenerated.LegacyStockSha256;

    private static readonly HostManifest Manifest = LoadManifest();

    public static LegacyX86FingerprintResult Inspect(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var fullPath = Path.GetFullPath(executablePath);
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        catch (Exception exception)
        {
            return Unsupported(fullPath, string.Empty, false,
                $"Unable to read FFVII executable: {exception.Message}");
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        if (!TryParsePe(bytes, out var image, out var parseDiagnostic))
        {
            return Unsupported(fullPath, sha256, false, parseDiagnostic);
        }

        var is64Bit = image.Machine == ImageFileMachineAmd64;
        if (image.Machine != ImageFileMachineI386)
        {
            return Unsupported(fullPath, sha256, is64Bit,
                $"PE machine 0x{image.Machine:X4} is not the supported x86 machine 0x{ImageFileMachineI386:X4}.");
        }

        var name = Path.GetFileName(fullPath);
        var isStockName = string.Equals(name, Manifest.LegacyStockX86.Name,
            StringComparison.OrdinalIgnoreCase);
        if (isStockName && string.Equals(sha256, Manifest.LegacyStockX86.Sha256,
                StringComparison.Ordinal))
        {
            return Supported(fullPath, sha256,
                "Supported exact stock legacy x86 FFVII executable.");
        }

        if (!Manifest.SevenHeavenX86.Names.Contains(name,
                StringComparer.OrdinalIgnoreCase))
        {
            return Unsupported(fullPath, sha256, false,
                "Legacy host must be named ff7_en.exe or ff7.exe.");
        }
        if (!Manifest.SevenHeavenX86.RequiredImports.All(required =>
                image.Imports.Contains(required, StringComparer.OrdinalIgnoreCase)))
        {
            return Unsupported(fullPath, sha256, false,
                "Compatible x86 host does not import WINMM.DLL.");
        }
        if (Manifest.SevenHeavenX86.ForbidEmbeddedManifest && image.HasEmbeddedManifest)
        {
            return Unsupported(fullPath, sha256, false,
                "Compatible x86 host embeds a manifest that can disable .local WinMM redirection.");
        }

        var lastFailure = "no structural profile matched";
        foreach (var profile in Manifest.SevenHeavenX86.Profiles)
        {
            if (MatchesProfile(bytes, image, profile, out lastFailure))
            {
                return Supported(fullPath, sha256,
                    $"Supported compatible 7th Heaven x86 FFVII executable ({profile.Id}).");
            }
        }

        return Unsupported(fullPath, sha256, false,
            $"Compatible x86 structural validation failed: {lastFailure}.");
    }

    private static LegacyX86FingerprintResult Supported(
        string path, string sha256, string diagnostic)
    {
        var identity = new RuntimeIdentity(
            "ff7-legacy-x86",
            path,
            sha256,
            false,
            GetFileVersion(path));
        return new LegacyX86FingerprintResult(identity, true, diagnostic);
    }

    private static LegacyX86FingerprintResult Unsupported(
        string path, string sha256, bool is64Bit, string diagnostic)
    {
        var identity = new RuntimeIdentity(
            "unsupported",
            path,
            sha256,
            is64Bit,
            GetFileVersion(path));
        return new LegacyX86FingerprintResult(identity, false, diagnostic);
    }

    private static string GetFileVersion(string path)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(path).FileVersion ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static HostManifest LoadManifest()
    {
        var manifest = JsonSerializer.Deserialize<HostManifest>(
            SupportedHostsGenerated.ManifestJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (manifest is null || manifest.SchemaVersion != 1 ||
            manifest.SevenHeavenX86.Profiles.Count == 0 ||
            manifest.SevenHeavenX86.Profiles.Any(profile => profile.Signatures.Count < 3))
        {
            throw new InvalidDataException("Generated FFVII host manifest is invalid.");
        }
        return manifest;
    }

    private static bool MatchesProfile(
        byte[] bytes,
        PeImage image,
        StructuralProfile profile,
        out string failure)
    {
        if (image.ImageBase != profile.ImageBase)
        {
            failure = $"image base differs from profile {profile.Id}";
            return false;
        }
        foreach (var expected in profile.Sections)
        {
            var actual = image.Sections.FirstOrDefault(section =>
                string.Equals(section.Name, expected.Name, StringComparison.Ordinal));
            if (actual is null ||
                actual.Rva != expected.Rva ||
                actual.VirtualSize != expected.VirtualSize ||
                actual.RawSize != expected.RawSize ||
                actual.Characteristics != expected.Characteristics)
            {
                failure = $"section evidence differs for profile {profile.Id}: {expected.Name}";
                return false;
            }
        }

        foreach (var signature in profile.Signatures)
        {
            var expected = Convert.FromHexString(signature.Bytes);
            var mask = Convert.FromHexString(signature.Mask);
            if (expected.Length == 0 || expected.Length != mask.Length ||
                !TryRvaToOffset(image, signature.Rva, expected.Length, out var offset))
            {
                failure = $"signature evidence is invalid for profile {profile.Id}";
                return false;
            }
            for (var index = 0; index < expected.Length; index++)
            {
                if ((bytes[offset + index] & mask[index]) !=
                    (expected[index] & mask[index]))
                {
                    failure = $"code signature differs for profile {profile.Id}";
                    return false;
                }
            }
        }

        failure = string.Empty;
        return true;
    }

    private static bool TryParsePe(
        byte[] bytes,
        out PeImage image,
        out string diagnostic)
    {
        image = new PeImage();
        diagnostic = string.Empty;
        try
        {
            if (bytes.Length < 64 || ReadUInt16(bytes, 0) != 0x5A4D)
            {
                diagnostic = "Executable has an invalid DOS header.";
                return false;
            }
            var peOffset = checked((int)ReadUInt32(bytes, 0x3C));
            RequireRange(bytes, peOffset, 24, "PE header");
            if (ReadUInt32(bytes, peOffset) != 0x00004550)
            {
                diagnostic = "Executable has an invalid PE signature.";
                return false;
            }

            image.Machine = ReadUInt16(bytes, peOffset + 4);
            var sectionCount = ReadUInt16(bytes, peOffset + 6);
            var optionalSize = ReadUInt16(bytes, peOffset + 20);
            if (sectionCount == 0 || sectionCount > 96)
            {
                diagnostic = "Executable has an invalid PE section count.";
                return false;
            }
            var optionalOffset = checked(peOffset + 24);
            RequireRange(bytes, optionalOffset, optionalSize, "optional header");
            var magic = ReadUInt16(bytes, optionalOffset);
            var is64Bit = magic == 0x20B;
            if (magic == 0x10B)
            {
                if (optionalSize < 224) throw new InvalidDataException("PE32 optional header is truncated.");
                image.ImageBase = ReadUInt32(bytes, optionalOffset + 28);
            }
            else if (is64Bit)
            {
                if (optionalSize < 240) throw new InvalidDataException("PE32+ optional header is truncated.");
                image.ImageBase = ReadUInt64(bytes, optionalOffset + 24);
            }
            else
            {
                diagnostic = $"Executable has unsupported optional-header magic 0x{magic:X4}.";
                return false;
            }
            image.SizeOfHeaders = ReadUInt32(bytes, optionalOffset + 60);
            if (image.SizeOfHeaders > bytes.Length)
            {
                diagnostic = "PE header size extends beyond the executable.";
                return false;
            }

            var sectionOffset = checked(optionalOffset + optionalSize);
            RequireRange(bytes, sectionOffset, checked(sectionCount * 40), "section table");
            for (var index = 0; index < sectionCount; index++)
            {
                var offset = checked(sectionOffset + (index * 40));
                var nameBytes = bytes.AsSpan(offset, 8);
                var terminator = nameBytes.IndexOf((byte)0);
                var nameLength = terminator < 0 ? 8 : terminator;
                var section = new PeSection(
                    Encoding.ASCII.GetString(nameBytes[..nameLength]),
                    ReadUInt32(bytes, offset + 12),
                    ReadUInt32(bytes, offset + 8),
                    ReadUInt32(bytes, offset + 20),
                    ReadUInt32(bytes, offset + 16),
                    ReadUInt32(bytes, offset + 36));
                if (section.RawSize != 0)
                {
                    RequireRange(bytes, checked((int)section.RawOffset),
                        checked((int)section.RawSize), $"section {section.Name}");
                }
                image.Sections.Add(section);
            }

            var directoriesOffset = optionalOffset + (is64Bit ? 112 : 96);
            RequireRange(bytes, directoriesOffset, 16 * 8, "data directories");
            var importRva = ReadUInt32(bytes, directoriesOffset + 8);
            var importSize = ReadUInt32(bytes, directoriesOffset + 12);
            var resourceRva = ReadUInt32(bytes, directoriesOffset + 16);
            var resourceSize = ReadUInt32(bytes, directoriesOffset + 20);
            if (!ParseImports(bytes, image, importRva, importSize, out diagnostic))
            {
                return false;
            }
            if (!DetectManifest(bytes, image, resourceRva, resourceSize,
                    out var hasEmbeddedManifest, out diagnostic))
            {
                return false;
            }
            image.HasEmbeddedManifest = hasEmbeddedManifest;
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or OverflowException or ArgumentOutOfRangeException)
        {
            diagnostic = $"Executable PE layout is malformed: {exception.Message}";
            return false;
        }
    }

    private static bool ParseImports(
        byte[] bytes,
        PeImage image,
        uint directoryRva,
        uint directorySize,
        out string diagnostic)
    {
        diagnostic = string.Empty;
        if (directoryRva == 0 || directorySize == 0) return true;
        var maximum = Math.Min(directorySize / 20u, 4096u);
        if (maximum == 0)
        {
            diagnostic = "PE import directory is smaller than one descriptor.";
            return false;
        }
        for (var index = 0u; index < maximum; index++)
        {
            var rva = checked(directoryRva + (index * 20));
            if (!TryRvaToOffset(image, rva, 20, out var offset))
            {
                diagnostic = "PE import descriptor is outside file-backed data.";
                return false;
            }
            var originalThunk = ReadUInt32(bytes, offset);
            var nameRva = ReadUInt32(bytes, offset + 12);
            var firstThunk = ReadUInt32(bytes, offset + 16);
            if (originalThunk == 0 && nameRva == 0 && firstThunk == 0) return true;
            if (!TryReadAscii(bytes, image, nameRva, 512, out var module))
            {
                diagnostic = "PE import module name is invalid or unterminated.";
                return false;
            }
            image.Imports.Add(module);
        }
        diagnostic = "PE import directory has no bounded terminator.";
        return false;
    }

    private static bool DetectManifest(
        byte[] bytes,
        PeImage image,
        uint resourceRva,
        uint resourceSize,
        out bool hasManifest,
        out string diagnostic)
    {
        hasManifest = false;
        diagnostic = string.Empty;
        if (resourceRva == 0 || resourceSize == 0) return true;
        if (resourceSize < 16 || !TryRvaToOffset(image, resourceRva, 16, out var root))
        {
            diagnostic = "PE resource root is outside its declared file-backed range.";
            return false;
        }
        var count = checked(ReadUInt16(bytes, root + 12) + ReadUInt16(bytes, root + 14));
        if (count > (resourceSize - 16) / 8)
        {
            diagnostic = "PE resource entry count is outside the declared resource directory.";
            return false;
        }
        for (var index = 0; index < count; index++)
        {
            var entry = checked(root + 16 + (index * 8));
            RequireRange(bytes, entry, 8, "resource entry");
            var name = ReadUInt32(bytes, entry);
            if ((name & 0x80000000) == 0 && (name & 0xFFFF) == 24)
            {
                hasManifest = true;
                return true;
            }
        }
        return true;
    }

    private static bool TryReadAscii(
        byte[] bytes,
        PeImage image,
        uint rva,
        int maximumLength,
        out string value)
    {
        value = string.Empty;
        if (!TryRvaToOffset(image, rva, 1, out var offset)) return false;
        var length = 0;
        while (length < maximumLength && offset + length < bytes.Length)
        {
            if (bytes[offset + length] == 0)
            {
                value = Encoding.ASCII.GetString(bytes, offset, length);
                return length > 0;
            }
            length++;
        }
        return false;
    }

    private static bool TryRvaToOffset(
        PeImage image,
        uint rva,
        int length,
        out int offset)
    {
        offset = 0;
        if (rva < image.SizeOfHeaders)
        {
            var end = (ulong)rva + (uint)length;
            if (end <= image.SizeOfHeaders)
            {
                offset = checked((int)rva);
                return true;
            }
        }
        foreach (var section in image.Sections)
        {
            var end = (ulong)rva + (uint)length;
            var sectionEnd = (ulong)section.Rva + section.RawSize;
            if (rva < section.Rva || end > sectionEnd) continue;
            offset = checked((int)(section.RawOffset + (rva - section.Rva)));
            return true;
        }
        return false;
    }

    private static ushort ReadUInt16(byte[] bytes, int offset)
    {
        RequireRange(bytes, offset, 2, "UInt16");
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        RequireRange(bytes, offset, 4, "UInt32");
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    }

    private static ulong ReadUInt64(byte[] bytes, int offset)
    {
        RequireRange(bytes, offset, 8, "UInt64");
        return BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset, 8));
    }

    private static void RequireRange(byte[] bytes, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
        {
            throw new InvalidDataException(
                $"{label} is outside the executable (offset={offset}, length={length}, file={bytes.Length}).");
        }
    }

    private sealed class PeImage
    {
        public ushort Machine { get; set; }
        public ulong ImageBase { get; set; }
        public uint SizeOfHeaders { get; set; }
        public bool HasEmbeddedManifest { get; set; }
        public List<PeSection> Sections { get; } = [];
        public List<string> Imports { get; } = [];
    }

    private sealed record PeSection(
        string Name,
        uint Rva,
        uint VirtualSize,
        uint RawOffset,
        uint RawSize,
        uint Characteristics);

    private sealed class HostManifest
    {
        public int SchemaVersion { get; set; }
        public ExactHost LegacyStockX86 { get; set; } = new();
        public CompatibleHost SevenHeavenX86 { get; set; } = new();
        public ExactHost Steam2026X64 { get; set; } = new();
    }

    private sealed class ExactHost
    {
        public string Name { get; set; } = string.Empty;
        public ushort Machine { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }

    private sealed class CompatibleHost
    {
        public List<string> Names { get; set; } = [];
        public ushort Machine { get; set; }
        public List<string> RequiredImports { get; set; } = [];
        public bool ForbidEmbeddedManifest { get; set; }
        public List<StructuralProfile> Profiles { get; set; } = [];
    }

    private sealed class StructuralProfile
    {
        public string Id { get; set; } = string.Empty;
        public string SampleSha256 { get; set; } = string.Empty;
        public long SampleLength { get; set; }
        public ulong ImageBase { get; set; }
        public List<SectionEvidence> Sections { get; set; } = [];
        public List<SignatureEvidence> Signatures { get; set; } = [];
    }

    private sealed class SectionEvidence
    {
        public string Name { get; set; } = string.Empty;
        public uint Rva { get; set; }
        public uint VirtualSize { get; set; }
        public uint RawOffset { get; set; }
        public uint RawSize { get; set; }
        public uint Characteristics { get; set; }
    }

    private sealed class SignatureEvidence
    {
        public uint Rva { get; set; }
        public string Bytes { get; set; } = string.Empty;
        public string Mask { get; set; } = string.Empty;
    }
}
