using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void Steam2026NativeCreatureDrawOriginal();

/// <summary>The physical host ABI is void(void); its polygon arguments live on the translated guest stack.</summary>
internal sealed class Steam2026NativeCreatureDrawObservationHook : IDisposable
{
    internal const ulong GuestEspRva = 0x020395C8;
    internal static readonly TranslatedFunctionMapDefinition Renderer = new(0x00660D6E, 0x016EDA30, 0x00DBDA70,
        "48895C2408574883EC208B0D48BB27018B1D46BB270183C1FC890D39BB2701E8");
    internal static readonly IReadOnlyList<TranslatedFunctionMapDefinition> Cohort = Array.AsReadOnly(new[]
    {
        Renderer,
        new TranslatedFunctionMapDefinition(0x006392BB, 0x016EBD20, 0x00CDCD50, "40574883EC308B0D6CC8350183C1FC48895C24408B1D62C83501890D58C83501"),
        new TranslatedFunctionMapDefinition(0x00639252, 0x016EBD10, 0x00CDCA70, "40534883EC208B0D4CCB35018B1D4ACB350183C1FC890D3DCB3501E8102636FF"),
        new TranslatedFunctionMapDefinition(0x0064314F, 0x016EC3A0, 0x00D0E650, "48895C2408574883EC208B0D68AF32018B1D66AF320183C1FC890D59AF3201E8"),
        new TranslatedFunctionMapDefinition(0x0063A60B, 0x016EBDA0, 0x00CE3D10, "48895C2408574883EC208B0DA85835018B1DA658350183C1FC890D99583501E8"),
        new TranslatedFunctionMapDefinition(0x006840DA, 0x016F0390, 0x00EA3460, "40574883EC308B0D5C61190183C1FC48895C24408B1D52611901890D48611901"),
        new TranslatedFunctionMapDefinition(0x0066067A, 0x016ED970, 0x00DBA470, "48895C24084889742410574883EC308B0D43F127018B1D41F1270183C1FC890D"),
        new TranslatedFunctionMapDefinition(0x00660BE3, 0x016ED9A0, 0x00DBCBA0, "48895C2408574883EC208B0D18CA27018B1D16CA270183C1FC890D09CA2701E8")
    });
    private readonly Steam2026NativeCreatureDrawOriginal detour;
    private readonly NativeCreatureDrawObservationIngress ingress;
    private readonly NativeCreatureDrawArgumentReader arguments;
    private readonly Action forwardOriginal;
    private IHook<Steam2026NativeCreatureDrawOriginal>? hook;
    private Steam2026NativeCreatureDrawOriginal? original;
    private int disposed;
    private GCHandle nativeRoot;

    private Steam2026NativeCreatureDrawObservationHook(ulong moduleBase, ulong imageSize, INativeMemoryReader memory,
        ILegacyAddressSpace addressSpace, Action<FieldCreatureApproachSample, DateTime> capture)
    {
        ingress = new NativeCreatureDrawObservationIngress(new FieldCreatureApproachReader(addressSpace), capture);
        arguments = (out uint polygon, out uint owner) => TryCaptureGuestArguments(moduleBase, imageSize, memory, addressSpace, out polygon, out owner);
        detour = OnCallback; forwardOriginal = () => original!(); nativeRoot = GCHandle.Alloc(this);
    }
    private void Install(IReloadedHooks hooks, ulong target)
    {
        hook = hooks.CreateHook(detour, checked((long)target), -1);
        original = hook.OriginalFunction; hook.Activate();
    }
    internal static bool TryCreate(Steam2026FingerprintResult fingerprint, ulong moduleBase, ulong imageSize,
        INativeMemoryReader memory, IReloadedHooks hooks, Action<FieldCreatureApproachSample, DateTime> capture,
        Action<string> log, out Steam2026NativeCreatureDrawObservationHook? result, out string diagnostic)
    {
        result = null; Steam2026NativeCreatureDrawObservationHook? candidate = null;
        try
        {
            if (moduleBase == 0 || imageSize < GuestEspRva + 4 || moduleBase > ulong.MaxValue - imageSize)
                throw new InvalidOperationException("The translated creature guest register root is outside its module.");
            var addressSpace = ValidatedTranslatedX86AddressSpaceFactory.Create(fingerprint, moduleBase, memory);
            var validator = new TranslatedFunctionMapValidator(moduleBase, imageSize, memory);
            ulong target = 0;
            foreach (var entry in Cohort)
            {
                if (!validator.TryValidate(entry, out var address))
                    throw new InvalidOperationException("The exact translated creature-renderer cohort is unavailable.");
                if (entry == Renderer) target = address;
            }
            ArgumentNullException.ThrowIfNull(hooks); ArgumentNullException.ThrowIfNull(capture);
            candidate = new Steam2026NativeCreatureDrawObservationHook(moduleBase, imageSize, memory, addressSpace, capture);
            candidate.Install(hooks, target); result = candidate;
            diagnostic = "Installed exact translated creature draw observation callback.";
            SafeLog(log, diagnostic); return true;
        }
        catch (Exception ex)
        {
            candidate?.Dispose(); result = candidate;
            diagnostic = "Translated creature callback is not ready: " + ex.Message;
            SafeLog(log, diagnostic); return false;
        }
    }
    internal static bool TryCaptureGuestArguments(ulong moduleBase, ulong imageSize, INativeMemoryReader memory,
        ILegacyAddressSpace addressSpace, out uint polygonSet, out uint gameObject)
    {
        polygonSet = gameObject = 0;
        if (moduleBase == 0 || imageSize < GuestEspRva + 4 || moduleBase > ulong.MaxValue - imageSize) return false;
        var address = moduleBase + GuestEspRva;
        if (!memory.TryQueryRegion(address, out var region) || !region.IsCommitted || !region.IsReadable || !region.IsImage
            || region.AllocationBase != moduleBase || address < region.BaseAddress || region.Size < 4
            || address - region.BaseAddress > region.Size - 4) return false;
        Span<byte> before = stackalloc byte[4]; Span<byte> after = stackalloc byte[4];
        if (!memory.TryRead(address, before)) return false;
        var guestEsp = BinaryPrimitives.ReadUInt32LittleEndian(before);
        if (guestEsp < 0x1000 || guestEsp > uint.MaxValue - 11) return false;
        Span<byte> rawArguments = stackalloc byte[8];
        if (!addressSpace.TryRead(guestEsp + 4, rawArguments) || !memory.TryRead(address, after)
            || !before.SequenceEqual(after)) return false;
        polygonSet = BinaryPrimitives.ReadUInt32LittleEndian(rawArguments);
        gameObject = BinaryPrimitives.ReadUInt32LittleEndian(rawArguments[4..]);
        return polygonSet >= 0x1000 && gameObject >= 0x1000;
    }
    private void OnCallback() => ingress.OnCallback(arguments, forwardOriginal);
    internal bool HasOriginalFault => ingress.HasOriginalFault;
    internal bool HasCaptureFault => ingress.HasCaptureFault;
    private static void SafeLog(Action<string>? log, string message) { try { log?.Invoke(message); } catch { } }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        ingress.Stop(); try { hook?.Disable(); } catch { }
        // Keep the process root and original path for already-entered and failed-activation callbacks.
    }
}
