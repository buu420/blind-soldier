namespace Ff7.Accessibility.Steam2026X64.Runtime.Battle;

internal enum Steam2026BattleRendererCallbackKind
{
    MenuRenderer,
    BattleUpdate,
    TextActivation,
    ResultsUpdate,
    DamageDisplay,
    ActionTextCommit
}

internal enum TranslatedBattleRendererHostAbi
{
    TranslatedX86VoidNoArguments
}

internal static class Steam2026BattleRendererState
{
    internal static bool IsSupported(short rendererState) =>
        rendererState is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 0x18;

    internal static bool IsCapturable(short rendererState) =>
        IsSupported(rendererState) || rendererState == 0x1B;
}

internal readonly record struct Steam2026BattleRendererCallbackMetadata(
    Steam2026BattleRendererCallbackKind Kind,
    TranslatedFunctionMapDefinition FunctionMap,
    TranslatedBattleRendererHostAbi HostAbi);

internal readonly record struct Steam2026BattleRendererCallbackIdentity(
    Steam2026BattleRendererCallbackMetadata Metadata,
    ulong HostAddress);

/// <summary>
/// Exact translated-function identities for the Steam 2026 battle lifecycle.
/// </summary>
internal sealed class Steam2026BattleRendererCallbackCatalog
{
    private readonly TranslatedFunctionMapValidator validator;

    internal Steam2026BattleRendererCallbackCatalog(
        TranslatedFunctionMapValidator validator)
    {
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    internal static Steam2026BattleRendererCallbackMetadata GetMetadata(
        Steam2026BattleRendererCallbackKind kind) =>
        kind switch
        {
            Steam2026BattleRendererCallbackKind.MenuRenderer => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x006D797C,
                    0x016F47B0,
                    0x010ACA10,
                    "48895C2408574883EC208B0DA8CBF800"),
                TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments),
            Steam2026BattleRendererCallbackKind.BattleUpdate => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x006CE8B3,
                    0x016F4580,
                    0x0107CF00,
                    "48895C2408574883EC20B908000000E8"),
                TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments),
            Steam2026BattleRendererCallbackKind.TextActivation => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x006D721C,
                    0x016F4790,
                    0x010AAE10,
                    "40534883EC208B0DACE7F8008B1DAAE7"),
                TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments),
            Steam2026BattleRendererCallbackKind.ResultsUpdate => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x006C9543,
                    0x016F3F50,
                    0x010623D0,
                    "48895C24084889742410574883EC208B"),
                TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments),
            Steam2026BattleRendererCallbackKind.DamageDisplay => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x005BB410,
                    0x016E5910,
                    0x009D7970,
                    "40574883EC308B0D4C1C660183C1FC48"),
                TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments),
            Steam2026BattleRendererCallbackKind.ActionTextCommit => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x006D71FA,
                    0x016F4780,
                    0x010AAD30,
                    "48895C2408574883EC208B0D88E8F800"),
                TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    internal bool TryValidateIdentity(
        Steam2026BattleRendererCallbackKind kind,
        out Steam2026BattleRendererCallbackIdentity identity) =>
        TryValidateIdentity(kind, requirePristinePrefix: true, out identity);

    internal bool TryValidateMappedIdentity(
        Steam2026BattleRendererCallbackKind kind,
        out Steam2026BattleRendererCallbackIdentity identity) =>
        TryValidateIdentity(kind, requirePristinePrefix: false, out identity);

    private bool TryValidateIdentity(
        Steam2026BattleRendererCallbackKind kind,
        bool requirePristinePrefix,
        out Steam2026BattleRendererCallbackIdentity identity)
    {
        identity = default;
        Steam2026BattleRendererCallbackMetadata metadata;
        try
        {
            metadata = GetMetadata(kind);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        ulong hostAddress;
        var isValid = requirePristinePrefix
            ? validator.TryValidate(metadata.FunctionMap, out hostAddress)
            : validator.TryValidateMappedTarget(metadata.FunctionMap, out hostAddress);
        if (!isValid)
        {
            return false;
        }

        identity = new Steam2026BattleRendererCallbackIdentity(metadata, hostAddress);
        return true;
    }
}
