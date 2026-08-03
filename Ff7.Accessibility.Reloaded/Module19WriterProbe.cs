using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.Enums;

namespace Ff7.Accessibility.Reloaded;

public readonly record struct Module19WriterSite(int Id, int Address, string Cause);

public static class Module19WriterCatalog
{
    public const int AddressFieldModuleState = 0x00CC0D84;
    public const int AddressFieldModuleRequest = 0x00CC0D89;

    public static IReadOnlyList<Module19WriterSite> RuntimeSites { get; } =
    [
        new(1, 0x00409F94, "keyboard Control+Q handler"),
        new(2, 0x0060E58F, "native quit menu callback"),
        new(3, 0x0063C452, "field module request 0x1A")
    ];

    public static Module19WriterSite? FindById(int id)
    {
        foreach (var site in RuntimeSites)
        {
            if (site.Id == id)
            {
                return site;
            }
        }

        return null;
    }
}

public sealed class Module19WriterProbe : IDisposable
{
    private readonly List<IAsmHook> hooks = new();
    private IntPtr marker;
    private bool disposed;

    public Module19WriterProbe(IReloadedHooks reloadedHooks)
    {
        ArgumentNullException.ThrowIfNull(reloadedHooks);

        marker = Marshal.AllocHGlobal(sizeof(int));
        Marshal.WriteInt32(marker, 0);
        try
        {
            foreach (var site in Module19WriterCatalog.RuntimeSites)
            {
                var hook = reloadedHooks.CreateAsmHook(
                    BuildMarkerAssembly(marker, site),
                    site.Address,
                    AsmHookBehaviour.ExecuteFirst,
                    -1);
                hook.Activate();
                hooks.Add(hook);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public Module19WriterSite? ReadCurrentSite()
    {
        if (disposed)
        {
            return null;
        }

        return Module19WriterCatalog.FindById(Marshal.ReadInt32(marker));
    }

    public static string[] BuildMarkerAssembly(IntPtr markerAddress, Module19WriterSite site) =>
    [
        "use32",
        $"mov dword [0x{markerAddress.ToInt64():X8}], {site.Id}"
    ];

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (var hook in hooks)
        {
            hook.Disable();
        }

        hooks.Clear();
        if (marker != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(marker);
            marker = IntPtr.Zero;
        }
    }
}
