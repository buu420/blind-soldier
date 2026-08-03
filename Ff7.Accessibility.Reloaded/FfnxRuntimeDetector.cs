using System.Diagnostics;

namespace Ff7.Accessibility.Reloaded;

public static class FfnxRuntimeDetector
{
    private static readonly HashSet<string> DriverModuleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "AF3DN.P",
        "7H_GameDriver.dll",
        "FFNx.dll"
    };

    public static bool IsLoaded(Process process)
    {
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
                    // A module without readable version metadata cannot prove FFNx is active.
                }

                if (IsFfnxModule(module.ModuleName, productName))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public static bool IsFfnxModule(string? moduleName, string? productName) =>
        !string.IsNullOrWhiteSpace(moduleName) &&
        DriverModuleNames.Contains(moduleName) &&
        string.Equals(productName, "FFNx", StringComparison.OrdinalIgnoreCase);
}
