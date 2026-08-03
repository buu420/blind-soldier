using BlindSwordsman.Setup.Core;
using System.Reflection;

namespace BlindSwordsman.Setup;

public sealed class EmbeddedResourceBundle : IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> Resources =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BlindSwordsman.Resources.FF7SteamInstall.psm1"] = "FF7SteamInstall.psm1",
            ["BlindSwordsman.Resources.FF7LauncherInstall.psm1"] = "FF7LauncherInstall.psm1",
            ["BlindSwordsman.Resources.Invoke-BlindSwordsmanPreflight.ps1"] = "Invoke-BlindSwordsmanPreflight.ps1",
            ["BlindSwordsman.Resources.Install-FF7ReloadedMod.ps1"] = "Install-FF7ReloadedMod.ps1",
            ["BlindSwordsman.Resources.Uninstall-FF7ReloadedMod.ps1"] = "Uninstall-FF7ReloadedMod.ps1",
            ["BlindSwordsman.Resources.templates.Ff7.Native.Steam2026.AppConfig.json"] = Path.Combine("templates", "Ff7.Native.Steam2026.AppConfig.json"),
            ["BlindSwordsman.Resources.analysis.dual_runtime.parity-matrix.json"] = Path.Combine("analysis", "dual_runtime", "parity-matrix.json")
        };

    private EmbeddedResourceBundle(string root)
    {
        Root = root;
        Paths = new SetupResourcePaths(
            Path.Combine(root, "Invoke-BlindSwordsmanPreflight.ps1"),
            Path.Combine(root, "Install-FF7ReloadedMod.ps1"),
            Path.Combine(root, "Uninstall-FF7ReloadedMod.ps1"));
    }

    public string Root { get; }

    public SetupResourcePaths Paths { get; }

    public static EmbeddedResourceBundle Extract()
    {
        var root = Path.Combine(Path.GetTempPath(), "blind-swordsman-resources-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var assembly = typeof(EmbeddedResourceBundle).Assembly;
            foreach (var resource in Resources)
            {
                var target = Path.GetFullPath(Path.Combine(root, resource.Value));
                if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("An embedded resource has an unsafe destination.");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var source = assembly.GetManifestResourceStream(resource.Key)
                    ?? throw new InvalidDataException($"Embedded setup resource is missing: {resource.Key}");
                using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                source.CopyTo(destination);
            }
            return new EmbeddedResourceBundle(root);
        }
        catch
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
            throw;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
