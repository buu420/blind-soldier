namespace Ff7.Accessibility.Reloaded;

public sealed class FlevelDataSource
{
    private readonly string? extractedDirectory;
    private readonly LgpArchiveReader? archive;

    public FlevelDataSource(string gameRootDirectory)
    {
        var fieldDirectory = Path.Combine(gameRootDirectory, "data", "field");
        var extractedCandidate = Path.Combine(fieldDirectory, "flevel");
        var extractedMapList = Path.Combine(extractedCandidate, "maplist");
        if (File.Exists(extractedMapList))
        {
            var names = FieldMapListResolver.ReadFieldNames(extractedMapList);
            if (names.Count != 0)
            {
                extractedDirectory = extractedCandidate;
                FieldNames = names;
                IsUsable = true;
                Diagnostic = $"extracted field data: {extractedCandidate}";
                return;
            }

            FieldNames = new Dictionary<int, string>();
            Diagnostic = $"extracted maplist contains no field names: {extractedMapList}";
            return;
        }

        var archivePath = Path.Combine(fieldDirectory, "flevel.lgp");
        if (!File.Exists(archivePath))
        {
            FieldNames = new Dictionary<int, string>();
            Diagnostic = $"no extracted maplist or flevel.lgp exists under {fieldDirectory}";
            return;
        }

        try
        {
            var candidate = new LgpArchiveReader(archivePath);
            if (!candidate.TryReadFile("maplist", out var mapListBytes))
            {
                FieldNames = new Dictionary<int, string>();
                Diagnostic = $"flevel.lgp does not contain maplist: {archivePath}";
                return;
            }

            var names = FieldMapListResolver.ReadFieldNames(mapListBytes);
            if (names.Count == 0)
            {
                FieldNames = names;
                Diagnostic = $"flevel.lgp maplist contains no field names: {archivePath}";
                return;
            }

            archive = candidate;
            FieldNames = names;
            IsUsable = true;
            Diagnostic = $"native flevel.lgp archive: {archivePath}";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            FieldNames = new Dictionary<int, string>();
            Diagnostic = $"could not read flevel.lgp {archivePath}: {exception.Message}";
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
