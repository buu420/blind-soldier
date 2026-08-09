namespace Ff7.Accessibility.Reloaded;

public sealed class FlevelDataSource
{
    private readonly string? extractedDirectory;
    private readonly LgpArchiveReader? archive;

    public FlevelDataSource(string gameRootDirectory)
        : this(gameRootDirectory, Ff7GameLanguageDetector.Detect(gameRootDirectory))
    {
    }

    public FlevelDataSource(string gameRootDirectory, Ff7GameLanguageContext language)
    {
        var fieldDirectory = Path.Combine(language.DataDirectory, "field");
        var extractedNames = new[]
        {
            Path.GetFileNameWithoutExtension(language.Descriptor.FieldArchiveName),
            "flevel"
        }.Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var extractedName in extractedNames)
        {
            var extractedCandidate = Path.Combine(fieldDirectory, extractedName);
            var extractedMapList = Path.Combine(extractedCandidate, "maplist");
            if (!File.Exists(extractedMapList))
            {
                continue;
            }

            var names = FieldMapListResolver.ReadFieldNames(extractedMapList);
            if (names.Count != 0)
            {
                extractedDirectory = extractedCandidate;
                FieldNames = names;
                IsUsable = true;
                Diagnostic = $"extracted field data: {extractedCandidate}";
                return;
            }
        }

        var archivePath = language.FieldArchivePath;
        if (!File.Exists(archivePath))
        {
            FieldNames = new Dictionary<int, string>();
            Diagnostic = $"no extracted maplist or {language.Descriptor.FieldArchiveName} exists under {fieldDirectory}";
            return;
        }

        try
        {
            var candidate = new LgpArchiveReader(archivePath);
            if (!candidate.TryReadFile("maplist", out var mapListBytes))
            {
                FieldNames = new Dictionary<int, string>();
                Diagnostic = $"{language.Descriptor.FieldArchiveName} does not contain maplist: {archivePath}";
                return;
            }

            var names = FieldMapListResolver.ReadFieldNames(mapListBytes);
            if (names.Count == 0)
            {
                FieldNames = names;
                Diagnostic = $"{language.Descriptor.FieldArchiveName} maplist contains no field names: {archivePath}";
                return;
            }

            archive = candidate;
            FieldNames = names;
            IsUsable = true;
            Diagnostic = $"native {language.Descriptor.FieldArchiveName} archive: {archivePath}";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            FieldNames = new Dictionary<int, string>();
            Diagnostic = $"could not read {language.Descriptor.FieldArchiveName} {archivePath}: {exception.Message}";
        }
    }

    public bool IsUsable { get; }

    public string Diagnostic { get; } = string.Empty;

    public IReadOnlyDictionary<int, string> FieldNames { get; }

    public bool HasField(int fieldId) =>
        FieldNames.TryGetValue(fieldId, out var fieldName) && HasField(fieldName);

    public bool TryReadField(int fieldId, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        return FieldNames.TryGetValue(fieldId, out var fieldName) && TryReadField(fieldName, out bytes);
    }

    public bool TryReadField(string fieldName, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (extractedDirectory is not null)
        {
            var path = Path.Combine(extractedDirectory, fieldName);
            if (!File.Exists(path))
            {
                return false;
            }

            bytes = File.ReadAllBytes(path);
            return true;
        }

        return archive is not null && archive.TryReadFile(fieldName, out bytes);
    }

    private bool HasField(string fieldName) => extractedDirectory is not null
        ? File.Exists(Path.Combine(extractedDirectory, fieldName))
        : archive?.ContainsFile(fieldName) == true;
}
