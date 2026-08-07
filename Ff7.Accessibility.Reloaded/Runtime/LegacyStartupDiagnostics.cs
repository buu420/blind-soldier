using System.Diagnostics;
using System.Reflection;

namespace Ff7.Accessibility.Reloaded.Runtime;

internal readonly record struct LegacyStartupSnapshot(
    bool Is64Bit,
    IReadOnlyList<string> NativeModules,
    IReadOnlyList<string> ManagedAssemblies);

internal static class LegacyStartupDiagnostics
{
    private static readonly HashSet<string> RecognizedReloadedNativeModuleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Reloaded.Mod.Loader.dll",
        "Reloaded.Mod.Interfaces.dll"
    };

    private static readonly HashSet<string> RecognizedReloadedManagedAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Reloaded.Mod.Loader",
        "Reloaded.Mod.Interfaces"
    };

    internal static string Classify(LegacyStartupSnapshot snapshot)
    {
        var hasAppLoader = HasModule(snapshot.NativeModules, "dinput.dll") ||
            HasModule(snapshot.NativeModules, "AppLoader.dll");
        var hasFfnx = HasModule(snapshot.NativeModules, "AF3DN.P") ||
            HasModule(snapshot.NativeModules, "7H_GameDriver.dll") ||
            HasModule(snapshot.NativeModules, "FFNx.dll");
        var hasCoreClr = HasModule(snapshot.NativeModules, "coreclr.dll");
        var hasHostFxr = HasModule(snapshot.NativeModules, "hostfxr.dll");
        var hasReloaded = HasReloadedModule(snapshot.NativeModules) ||
            HasReloadedAssembly(snapshot.ManagedAssemblies);
        var hasAppProxy = HasAssembly(snapshot.ManagedAssemblies, "AppProxy");
        var hasAppWrapper = HasAssembly(snapshot.ManagedAssemblies, "AppWrapper");

        if (!snapshot.Is64Bit &&
            hasAppLoader &&
            hasFfnx &&
            hasCoreClr &&
            hasHostFxr &&
            hasReloaded &&
            hasAppProxy &&
            hasAppWrapper)
        {
            return "stock-7h-ffnx-late-attach";
        }

        if (!snapshot.Is64Bit &&
            hasCoreClr &&
            hasHostFxr &&
            hasReloaded &&
            !hasAppLoader &&
            !hasFfnx &&
            !hasAppProxy &&
            !hasAppWrapper)
        {
            return "direct-reloaded";
        }

        return "partial-unexpected";
    }

    internal static LegacyStartupSnapshot Capture()
    {
        var nativeModules = new List<string>();
        try
        {
            using var process = Process.GetCurrentProcess();
            var executableDirectory = TryGetDirectory(process.MainModule?.FileName);
            foreach (ProcessModule module in process.Modules)
            {
                if (IsRelevantNativeModule(module, executableDirectory))
                {
                    nativeModules.Add(DescribeModule(module));
                }
            }
        }
        catch
        {
            // Diagnostics must not destabilize an already-running game process.
        }

        var managedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(IsRelevantManagedAssembly)
            .Select(DescribeAssembly)
            .OrderBy(static evidence => evidence, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new LegacyStartupSnapshot(
            Environment.Is64BitProcess,
            nativeModules.OrderBy(static evidence => evidence, StringComparer.OrdinalIgnoreCase).ToArray(),
            managedAssemblies);
    }

    internal static bool IsRelevantNativeEvidence(
        string? moduleName,
        string? modulePath,
        string? productName,
        string? executableDirectory)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            return false;
        }

        if (string.Equals(moduleName, "dinput.dll", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(moduleName, "AppLoader.dll", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(
                TryGetDirectory(modulePath),
                executableDirectory,
                StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(moduleName, "coreclr.dll", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(moduleName, "hostfxr.dll", StringComparison.OrdinalIgnoreCase) ||
            IsRecognizedReloadedNativeModuleName(moduleName) ||
            FfnxRuntimeDetector.IsFfnxModule(moduleName, productName);
    }

    internal static bool IsRecognizedReloadedNativeModuleName(string? moduleName) =>
        !string.IsNullOrWhiteSpace(moduleName) &&
        RecognizedReloadedNativeModuleNames.Contains(moduleName);

    internal static bool IsRecognizedReloadedManagedAssemblyName(string? assemblyName) =>
        !string.IsNullOrWhiteSpace(assemblyName) &&
        RecognizedReloadedManagedAssemblyNames.Contains(assemblyName);

    private static bool IsRelevantNativeModule(ProcessModule module, string? executableDirectory)
    {
        string? productName = null;
        try
        {
            productName = FileVersionInfo.GetVersionInfo(module.FileName).ProductName;
        }
        catch
        {
            // Version metadata is only needed to identify FFNx.
        }

        return IsRelevantNativeEvidence(
            module.ModuleName,
            module.FileName,
            productName,
            executableDirectory);
    }

    private static bool IsRelevantManagedAssembly(Assembly assembly) =>
        string.Equals(assembly.GetName().Name, "AppProxy", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(assembly.GetName().Name, "AppWrapper", StringComparison.OrdinalIgnoreCase) ||
        IsRecognizedReloadedManagedAssemblyName(assembly.GetName().Name);

    private static string DescribeModule(ProcessModule module)
    {
        string version;
        try
        {
            version = FileVersionInfo.GetVersionInfo(module.FileName).FileVersion ?? "<unknown>";
        }
        catch
        {
            version = "<unknown>";
        }

        return $"{module.ModuleName} | {version} | {module.FileName}";
    }

    private static string DescribeAssembly(Assembly assembly)
    {
        var name = assembly.GetName();
        var location = string.IsNullOrWhiteSpace(assembly.Location) ? "<dynamic>" : assembly.Location;
        return $"{name.Name} | {name.Version?.ToString() ?? "<unknown>"} | {location}";
    }

    private static bool HasModule(IReadOnlyList<string> modules, string moduleName) =>
        modules.Any(evidence =>
            evidence.StartsWith($"{moduleName} |", StringComparison.OrdinalIgnoreCase));

    private static bool HasAssembly(IReadOnlyList<string> assemblies, string assemblyName) =>
        assemblies.Any(evidence =>
            evidence.StartsWith($"{assemblyName} |", StringComparison.OrdinalIgnoreCase));

    private static bool HasReloadedModule(IReadOnlyList<string> modules) =>
        RecognizedReloadedNativeModuleNames.Any(moduleName =>
            HasModule(modules, moduleName));

    private static bool HasReloadedAssembly(IReadOnlyList<string> assemblies) =>
        RecognizedReloadedManagedAssemblyNames.Any(assemblyName =>
            HasAssembly(assemblies, assemblyName));

    private static string? TryGetDirectory(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path);
        }
        catch
        {
            return null;
        }
    }
}
