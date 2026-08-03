namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

internal enum Steam2026FieldCutsceneCallbackKind
{
    Request,
    RequestSw,
    RequestEw,
    Split,
    Wait,
    Scroll2D,
    Fade,
    Anime1,
    Visibility,
    AnimOnceOrHold,
    Canm1Or2,
    BackgroundOn,
    Sound,
    Akao,
    Movie
}

/// <summary>
/// The translated x86 field-opcode handlers are exposed to the x64 host as
/// void(void). Their script context remains in translated guest globals.
/// </summary>
internal enum TranslatedFieldCutsceneHostAbi
{
    TranslatedX86VoidNoArguments
}

internal readonly record struct Steam2026FieldCutsceneCallbackMetadata(
    Steam2026FieldCutsceneCallbackKind Kind,
    TranslatedFunctionMapDefinition FunctionMap,
    TranslatedFieldCutsceneHostAbi HostAbi);

internal readonly record struct Steam2026FieldCutsceneCallbackIdentity(
    Steam2026FieldCutsceneCallbackMetadata Metadata,
    ulong HostAddress);

/// <summary>
/// Exact Steam 2026 translated-function identities for every x86 field-opcode
/// handler used by the shared description catalog. No neighboring or inferred
/// handler is admitted here.
/// </summary>
internal sealed class Steam2026FieldCutsceneCallbackCatalog
{
    private readonly TranslatedFunctionMapValidator validator;

    internal Steam2026FieldCutsceneCallbackCatalog(
        TranslatedFunctionMapValidator validator)
    {
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    internal static Steam2026FieldCutsceneCallbackMetadata GetMetadata(
        Steam2026FieldCutsceneCallbackKind kind) =>
        kind switch
        {
            Steam2026FieldCutsceneCallbackKind.Request => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x006123E2,
                    0x016EA2E0,
                    0x00BB0C30,
                    "48895C2408574883EC208B0D88894801"),
                TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments),
            Steam2026FieldCutsceneCallbackKind.RequestSw => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x0061246A,
                    0x016EA2F0,
                    0x00BB0F30,
                    "48895C2408574883EC208B0D88864801"),
                TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments),
            Steam2026FieldCutsceneCallbackKind.RequestEw => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x006124F2,
                    0x016EA300,
                    0x00BB1230,
                    "48895C2408574883EC208B0D88834801"),
                TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments),
            Steam2026FieldCutsceneCallbackKind.Split => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x0061CE0C,
                    0x016EAE30,
                    0x00BE1A70,
                    "40534883EC208B0D4C7B45018B1D4A7B"),
                TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments),
            Steam2026FieldCutsceneCallbackKind.Wait => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x00610818,
                    0x016EA110,
                    0x00BA8A70,
                    "48895C2408574883EC208B0D480B4901"),
                TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments),
            Steam2026FieldCutsceneCallbackKind.Scroll2D => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x0061A7F9,
                    0x016EABE0,
                    0x00BD5FC0,
                    "48895C2408574883EC208B0DF8354601"),
                TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments),
            Steam2026FieldCutsceneCallbackKind.Fade => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x0061DDB4,
                    0x016EAEA0,
                    0x00BE6490,
                    "48895C2408574883EC208B0D28314501"),
                TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments),
            Steam2026FieldCutsceneCallbackKind.Anime1 => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x0061484A,
                    0x016EA5B0,
                    0x00BBB7C0,
                    "48895C2408574883EC208B0DF8DD4701"),
                TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments),
            Steam2026FieldCutsceneCallbackKind.Visibility => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x00618A01,
                    0x016EA820,
                    0x00BCC320,
                    "48895C2408574883EC208B0D98D24601"),
                TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments),
            Steam2026FieldCutsceneCallbackKind.AnimOnceOrHold => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x006149A5,
                    0x016EA5C0,
                    0x00BBBC40,
                    "48895C2408574883EC208B0D78D94701"),
                TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments),
            Steam2026FieldCutsceneCallbackKind.Canm1Or2 => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x00614E3E,
                    0x016EA5E0,
                    0x00BBCF20,
                    "48895C2408574883EC208B0D98C64701"),
                TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments),
            Steam2026FieldCutsceneCallbackKind.BackgroundOn => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x0061A035,
                    0x016EAB00,
                    0x00BD3CD0,
                    "48895C24084889742410574883EC208B"),
                TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments),
            Steam2026FieldCutsceneCallbackKind.Sound => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x00613A2D,
                    0x016EA430,
                    0x00BB72C0,
                    "48895C2408574883EC208B0DF8224801"),
                TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments),
            Steam2026FieldCutsceneCallbackKind.Akao => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x006137F9,
                    0x016EA410,
                    0x00BB6620,
                    "48895C2408574883EC208B0D982F4801"),
                TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments),
            Steam2026FieldCutsceneCallbackKind.Movie => new(
                kind,
                new TranslatedFunctionMapDefinition(
                    0x0061A321,
                    0x016EAB60,
                    0x00BD4A70,
                    "48895C2408574883EC208B0D484B4601"),
                TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    internal bool TryValidateIdentity(
        Steam2026FieldCutsceneCallbackKind kind,
        out Steam2026FieldCutsceneCallbackIdentity identity) =>
        TryValidateIdentity(kind, requirePristinePrefix: true, out identity);

    internal bool TryValidateMappedIdentity(
        Steam2026FieldCutsceneCallbackKind kind,
        out Steam2026FieldCutsceneCallbackIdentity identity) =>
        TryValidateIdentity(kind, requirePristinePrefix: false, out identity);

    private bool TryValidateIdentity(
        Steam2026FieldCutsceneCallbackKind kind,
        bool requirePristinePrefix,
        out Steam2026FieldCutsceneCallbackIdentity identity)
    {
        identity = default;
        Steam2026FieldCutsceneCallbackMetadata metadata;
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

        identity = new Steam2026FieldCutsceneCallbackIdentity(metadata, hostAddress);
        return true;
    }
}
