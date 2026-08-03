namespace Ff7.Accessibility.Reloaded;

public static class ModPaths
{
    public const string LogFileName = "ff7_accessibility_reloaded.log";

    public static string ResolveLogPath(string? modDirectory)
    {
        if (!string.IsNullOrWhiteSpace(modDirectory))
        {
            Directory.CreateDirectory(modDirectory);
            return Path.Combine(modDirectory, LogFileName);
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FF7AccessibilityReloaded");
        Directory.CreateDirectory(fallback);
        return Path.Combine(fallback, LogFileName);
    }
}
