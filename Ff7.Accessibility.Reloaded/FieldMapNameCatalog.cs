namespace Ff7.Accessibility.Reloaded;

public sealed class FieldMapNameReader
{
    // MPNAM copies at most 23 encoded bytes plus the terminator into this save-location buffer.
    public const int AddressCurrentMapName = FieldMessageReader.AddressFieldMessageLineBuffer;
    public const int BufferLength = 24;

    private readonly Func<int, int, string> readText;

    public FieldMapNameReader(Func<int, int, string> readText)
    {
        this.readText = readText;
    }

    public string Read() => readText(AddressCurrentMapName, BufferLength);
}

public readonly record struct FieldMapNameResolution(
    bool IsKnownField,
    IReadOnlyList<string> Names)
{
    public static FieldMapNameResolution Unknown { get; } =
        new(false, Array.Empty<string>());

    public static FieldMapNameResolution Known(IReadOnlyList<string> names) =>
        new(true, names);
}

public sealed class FieldMapNameCatalog
{
    private readonly FieldScriptNavigationCatalog scriptCatalog;
    private readonly FlevelFieldTextResolver textResolver;
    private readonly Dictionary<int, FieldMapNameResolution> cache = new();
    private readonly object cacheLock = new();

    public FieldMapNameCatalog(
        FieldScriptNavigationCatalog scriptCatalog,
        FlevelFieldTextResolver textResolver)
    {
        this.scriptCatalog = scriptCatalog;
        this.textResolver = textResolver;
    }

    public FieldMapNameResolution Read(int fieldId)
    {
        lock (cacheLock)
        {
            if (cache.TryGetValue(fieldId, out var cached))
            {
                return cached;
            }

            var resolution = ReadCore(fieldId);
            cache[fieldId] = resolution;
            return resolution;
        }
    }

    private FieldMapNameResolution ReadCore(int fieldId)
    {
        var field = scriptCatalog.ReadField(fieldId);
        if (!field.IsUsable)
        {
            return FieldMapNameResolution.Unknown;
        }

        var names = field.MapNameDialogIds
            .Select(dialogId => Normalize(textResolver.ReadMessageById(fieldId, dialogId).Text))
            .Where(name => name.Length != 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return FieldMapNameResolution.Known(names);
    }

    private static string Normalize(string value) =>
        string.Join(
            " ",
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
