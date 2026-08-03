namespace Ff7.Accessibility.Reloaded;

public sealed class Ff7SaveFileRepository
{
    private readonly IReadOnlyList<string> directories;

    public Ff7SaveFileRepository(IEnumerable<string> directories)
    {
        this.directories = directories
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> Directories => directories;

    public static Ff7SaveFileRepository CreateDefault(string? gameRootDirectory)
    {
        var directories = new List<string>();
        AddSteamUserDirectories(directories, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrWhiteSpace(oneDrive))
        {
            AddSteamUserDirectories(directories, Path.Combine(oneDrive, "Documents"));
        }

        if (!string.IsNullOrWhiteSpace(gameRootDirectory))
        {
            directories.Add(Path.Combine(gameRootDirectory, "save"));
            directories.Add(gameRootDirectory);
        }

        return new Ff7SaveFileRepository(directories);
    }

    public bool HasData(int saveFileNumber)
    {
        var path = ResolvePath(saveFileNumber);
        if (path is null)
        {
            return false;
        }

        for (var slot = 1; slot <= Ff7PcSaveFileReader.SlotsPerFile; slot++)
        {
            if (Ff7PcSaveFileReader.TryReadSlot(path, slot, out var preview) && !preview.IsEmpty)
            {
                return true;
            }
        }

        return false;
    }

    public Ff7SaveSlotPreview? ReadSlot(int saveFileNumber, int gameNumber)
    {
        var path = ResolvePath(saveFileNumber);
        return path is not null && Ff7PcSaveFileReader.TryReadSlot(path, gameNumber, out var preview)
            ? preview
            : null;
    }

    public string? ResolvePath(int saveFileNumber)
    {
        if (saveFileNumber is < 1 or > 10)
        {
            return null;
        }

        var fileName = $"save{saveFileNumber - 1:00}.ff7";
        return directories
            .Select(directory => Path.Combine(directory, fileName))
            .FirstOrDefault(File.Exists);
    }

    private static void AddSteamUserDirectories(ICollection<string> directories, string documentsDirectory)
    {
        if (string.IsNullOrWhiteSpace(documentsDirectory))
        {
            return;
        }

        var steamDirectory = Path.Combine(
            documentsDirectory,
            "Square Enix",
            "FINAL FANTASY VII Steam");
        if (!Directory.Exists(steamDirectory))
        {
            return;
        }

        try
        {
            foreach (var userDirectory in Directory.EnumerateDirectories(steamDirectory, "user_*"))
            {
                directories.Add(userDirectory);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
