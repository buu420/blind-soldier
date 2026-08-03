namespace BlindSwordsman.Setup.Core;

public sealed record ReleasePayloadLayout(string ModPackagePath, string LauncherBundlePath);

public static class ReleasePayloadLayoutValidator
{
    public static ReleasePayloadLayout Validate(string extractedRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extractedRoot);
        var root = Path.GetFullPath(extractedRoot);
        var rootInfo = new DirectoryInfo(root);
        if (!rootInfo.Exists || (rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Validated runtime payload root is missing or unsafe.");
        }

        var modPackage = Path.Combine(root, "package", "ff7.accessibility.reloaded");
        var launcherBundle = Path.Combine(root, "launcher");
        RequireDirectory(modPackage, "dual-runtime mod package");
        RequireFile(Path.Combine(modPackage, "ModConfig.json"), "dual-runtime mod configuration");
        RequireDirectory(launcherBundle, "accessible launcher bundle");
        RequireFile(Path.Combine(launcherBundle, "launcher-bundle.json"), "accessible launcher manifest");
        RequireFile(Path.Combine(launcherBundle, "FFVII_LAUNCHER.exe"), "accessible launcher executable");
        RequireFile(Path.Combine(launcherBundle, "FFVII_LAUNCHER.exe.config"), "accessible launcher configuration");
        RequireFile(
            Path.Combine(launcherBundle, "native", "x86", "FFVII_LAUNCHER.prism.x86.dll"),
            "accessible launcher Prism library");
        return new ReleasePayloadLayout(modPackage, launcherBundle);
    }

    private static void RequireDirectory(string path, string label)
    {
        var item = new DirectoryInfo(path);
        if (!item.Exists || (item.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"Validated runtime payload is missing its {label}.");
        }
    }

    private static void RequireFile(string path, string label)
    {
        var item = new FileInfo(path);
        if (!item.Exists || (item.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"Validated runtime payload is missing its {label}.");
        }
    }
}
