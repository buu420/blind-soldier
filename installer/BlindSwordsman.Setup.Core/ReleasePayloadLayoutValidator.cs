namespace BlindSwordsman.Setup.Core;

public sealed record ReleasePayloadLayout(
    string ModPackagePath,
    string LauncherBundlePath,
    string PrerequisiteBundlePath);

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
        var prerequisiteBundle = Path.Combine(root, "prerequisites");
        RequireDirectory(modPackage, "dual-runtime mod package");
        RequireFile(Path.Combine(modPackage, "ModConfig.json"), "dual-runtime mod configuration");
        RequireDirectory(launcherBundle, "accessible launcher bundle");
        RequireFile(Path.Combine(launcherBundle, "launcher-bundle.json"), "accessible launcher manifest");
        RequireFile(Path.Combine(launcherBundle, "FFVII_LAUNCHER.exe"), "accessible launcher executable");
        RequireFile(Path.Combine(launcherBundle, "FFVII_LAUNCHER.exe.config"), "accessible launcher configuration");
        RequireFile(
            Path.Combine(launcherBundle, "native", "x86", "FFVII_LAUNCHER.prism.x86.dll"),
            "accessible launcher Prism library");
        RequireDirectory(prerequisiteBundle, "setup-managed prerequisite bundle");
        foreach (var relativeDirectory in new[]
                 {
                     "reloaded",
                     "reloaded/_asi_extract",
                     "reloaded/Loader/X86",
                     "reloaded/Loader/X86/Bootstrapper",
                     "reloaded/Loader/X64",
                     "reloaded/Loader/X64/Bootstrapper",
                     "shared-hooks",
                     "shared-hooks/x86",
                     "shared-hooks/x64",
                     "dotnet",
                     "notices"
                 })
        {
            RequireDirectory(
                Path.Combine(prerequisiteBundle, relativeDirectory.Replace('/', Path.DirectorySeparatorChar)),
                $"prerequisite directory {relativeDirectory}");
        }
        foreach (var (relativePath, label) in new[]
                 {
                     ("dependency-bundle.json", "prerequisite manifest"),
                     ("reloaded/Reloaded-II.exe", "Reloaded-II executable"),
                     ("reloaded/_asi_extract/ASILoader32.dll", "x86 ASI loader"),
                     ("reloaded/_asi_extract/ASILoader64.dll", "x64 ASI loader"),
                     ("reloaded/Loader/X86/Reloaded.Mod.Loader.dll", "x86 Reloaded loader"),
                     ("reloaded/Loader/X64/Reloaded.Mod.Loader.dll", "x64 Reloaded loader"),
                     ("reloaded/Loader/X86/Bootstrapper/Reloaded.Mod.Loader.Bootstrapper.dll", "x86 Reloaded bootstrapper"),
                     ("reloaded/Loader/X64/Bootstrapper/Reloaded.Mod.Loader.Bootstrapper.dll", "x64 Reloaded bootstrapper"),
                     ("shared-hooks/ModConfig.json", "Shared Hooks configuration"),
                     ("shared-hooks/x86/Reloaded.Hooks.ReloadedII.dll", "x86 Shared Hooks entry assembly"),
                     ("shared-hooks/x64/Reloaded.Hooks.ReloadedII.dll", "x64 Shared Hooks entry assembly"),
                     ("dotnet/windowsdesktop-runtime-9.0.8-win-x86.exe", "x86 .NET desktop runtime installer"),
                     ("dotnet/windowsdesktop-runtime-9.0.8-win-x64.exe", "x64 .NET desktop runtime installer"),
                     ("notices/THIRD-PARTY-NOTICES.md", "prerequisite notices"),
                     ("notices/Reloaded-II-GPL-3.0.txt", "Reloaded-II license"),
                     ("notices/Reloaded-Shared-Hooks-LGPL-3.0.txt", "Shared Hooks license"),
                     ("notices/dotnet-LICENSE.txt", ".NET license"),
                     ("notices/dotnet-THIRD-PARTY-NOTICES.txt", ".NET third-party notices")
                 })
        {
            RequireFile(
                Path.Combine(prerequisiteBundle, relativePath.Replace('/', Path.DirectorySeparatorChar)),
                label);
        }
        return new ReleasePayloadLayout(modPackage, launcherBundle, prerequisiteBundle);
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
