namespace Ff7.Accessibility.Steam2026X64.Runtime.Menus;

internal enum Steam2026MenuCallbackKind
{
    CursorB,
    CursorA,
    WidgetConstructor,
    ActiveWidgetUpdate,
    EncodedTextB,
    EncodedTextA,
    AsciiRenderer
}

/// <summary>
/// The x64 entry point is a translated-x86 wrapper whose host signature is
/// <c>void(void)</c>. Gameplay arguments remain on the guest x86 stack.
/// </summary>
internal enum TranslatedMenuHostAbi
{
    TranslatedX86VoidNoArguments
}

internal readonly record struct Steam2026MenuCallbackMetadata(
    Steam2026MenuCallbackKind Kind,
    TranslatedFunctionMapDefinition FunctionMap,
    TranslatedMenuHostAbi HostAbi,
    bool IsCaptureEligible);

internal readonly record struct Steam2026MenuCallbackIdentity(
    Steam2026MenuCallbackMetadata Metadata,
    ulong HostAddress);

/// <summary>
/// Exact callback identities recovered from the Steam 2026 translated-function
/// map. The widget constructor is retained as identity evidence only.
/// </summary>
internal sealed class Steam2026MenuCallbackCatalog
{
    private readonly TranslatedFunctionMapValidator validator;

    public Steam2026MenuCallbackCatalog(TranslatedFunctionMapValidator validator)
    {
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public static Steam2026MenuCallbackMetadata GetMetadata(Steam2026MenuCallbackKind kind) =>
        kind switch
        {
            Steam2026MenuCallbackKind.CursorB => Create(
                kind, 0x006EB3B8, 0x016F5180, 0x01118430,
                "40574883EC308B0D8C11F20083C1FC48", true),
            Steam2026MenuCallbackKind.CursorA => Create(
                kind, 0x006F0D7D, 0x016F5240, 0x0113D0D0,
                "40574883EC308B0DECC4EF0083C1FC48", true),
            Steam2026MenuCallbackKind.WidgetConstructor => Create(
                kind, 0x006F4D30, 0x016F52E0, 0x01158060,
                "48895C2408574883EC208B0D5815EE00", false),
            Steam2026MenuCallbackKind.ActiveWidgetUpdate => Create(
                kind, 0x006F4DB2, 0x016F52F0, 0x011584F0,
                "48895C2408574883EC208B0DC810EE00", true),
            Steam2026MenuCallbackKind.EncodedTextB => Create(
                kind, 0x006F5B03, 0x016F53A0, 0x0115D910,
                "40534883EC208B0DACBCED008B1DAABC", true),
            Steam2026MenuCallbackKind.EncodedTextA => Create(
                kind, 0x006FAB2F, 0x016F54B0, 0x01180DF0,
                "40534883EC208B0DCC87EB008B1DCA87", true),
            Steam2026MenuCallbackKind.AsciiRenderer => Create(
                kind, 0x0072F9F4, 0x016F7180, 0x012995F0,
                "40574883EC508B0DCCFFD90083C1FC48", true),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    public bool TryValidateIdentity(
        Steam2026MenuCallbackKind kind,
        out Steam2026MenuCallbackIdentity identity)
        => TryValidateIdentity(kind, requirePristinePrefix: true, out identity);

    internal bool TryValidateMappedIdentity(
        Steam2026MenuCallbackKind kind,
        out Steam2026MenuCallbackIdentity identity)
        => TryValidateIdentity(kind, requirePristinePrefix: false, out identity);

    private bool TryValidateIdentity(
        Steam2026MenuCallbackKind kind,
        bool requirePristinePrefix,
        out Steam2026MenuCallbackIdentity identity)
    {
        identity = default;
        Steam2026MenuCallbackMetadata metadata;
        try
        {
            metadata = GetMetadata(kind);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        ulong hostAddress;
        var valid = requirePristinePrefix
            ? validator.TryValidate(metadata.FunctionMap, out hostAddress)
            : validator.TryValidateMappedTarget(metadata.FunctionMap, out hostAddress);
        if (!valid)
        {
            return false;
        }

        identity = new Steam2026MenuCallbackIdentity(metadata, hostAddress);
        return true;
    }

    public bool TryGetValidatedCaptureTarget(
        Steam2026MenuCallbackKind kind,
        out ulong hostAddress)
    {
        hostAddress = 0;
        if (!TryValidateIdentity(kind, out var identity)
            || !identity.Metadata.IsCaptureEligible)
        {
            return false;
        }

        hostAddress = identity.HostAddress;
        return true;
    }

    private static Steam2026MenuCallbackMetadata Create(
        Steam2026MenuCallbackKind kind,
        uint legacyVirtualAddress,
        ulong mappingRecordRva,
        ulong hostRva,
        string prefixHex,
        bool isCaptureEligible) =>
        new(
            kind,
            new TranslatedFunctionMapDefinition(
                legacyVirtualAddress,
                mappingRecordRva,
                hostRva,
                prefixHex),
            TranslatedMenuHostAbi.TranslatedX86VoidNoArguments,
            isCaptureEligible);
}
