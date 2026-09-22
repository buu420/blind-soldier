using System.Text;
using System.Text.Json;

namespace Ff7.Accessibility.Reloaded;

/// <summary>Room narration history belonging to a native save container and game slot.</summary>
public sealed class FieldAreaDescriptionHistory
{
    private readonly object sync = new();
    private readonly string? path;
    private readonly Action<string>? log;
    private readonly Dictionary<string, HashSet<int>> profiles = new(StringComparer.Ordinal);
    private HashSet<int> heard = [];
    private string? activeSlot;
    private bool reportedFailure;

    public FieldAreaDescriptionHistory(string? path, Action<string>? log = null)
    {
        this.path = path;
        this.log = log;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            if (new FileInfo(path).Length > 2 * 1024 * 1024)
                throw new InvalidDataException("The room history file exceeds its size limit.");
            var data = JsonSerializer.Deserialize<HistoryFile>(File.ReadAllText(path));
            if (data is null || data.Version != 1 || data.Slots is null || data.Slots.Count > 150)
                throw new InvalidDataException("The room history file has an unsupported format.");
            foreach (var (slot, fields) in data.Slots)
            {
                if (!IsValidSlotKey(slot) || fields is null || fields.Any(field => field is < 0 or > ushort.MaxValue))
                    throw new InvalidDataException("The room history file contains an invalid save or field.");
            }
            foreach (var (slot, fields) in data.Slots) profiles.Add(slot, fields.ToHashSet());
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            ReportFailure(ex);
        }
    }

    public bool HasHeard(int fieldId)
    {
        lock (sync) return heard.Contains(fieldId);
    }

    /// <summary>Called only after narration or screen-reader output accepts the room description.</summary>
    public void MarkHeard(int fieldId)
    {
        if (fieldId is < 0 or > ushort.MaxValue) return;
        lock (sync)
        {
            if (!heard.Add(fieldId) || activeSlot is null) return;
            profiles[activeSlot] = new HashSet<int>(heard);
            Persist();
        }
    }

    /// <summary>Successful load selects only that save's history, including when restarting the process.</summary>
    public void LoadSave(int saveFile, int gameSlot)
    {
        var key = SlotKey(saveFile, gameSlot);
        lock (sync)
        {
            activeSlot = key;
            heard = profiles.TryGetValue(key, out var previous) ? new HashSet<int>(previous) : [];
        }
    }

    /// <summary>A successful save carries this playthrough into its destination, replacing an overwritten save.</summary>
    public void SaveGame(int saveFile, int gameSlot)
    {
        var key = SlotKey(saveFile, gameSlot);
        lock (sync)
        {
            activeSlot = key;
            profiles[key] = new HashSet<int>(heard);
            Persist();
        }
    }

    /// <summary>Unidentified/new games deduplicate in memory until a native load or save identifies their slot.</summary>
    public void BeginNewGame()
    {
        lock (sync)
        {
            activeSlot = null;
            heard = [];
        }
    }

    private void Persist()
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        string? temporary = null;
        try
        {
            var destination = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var data = new HistoryFile(1, profiles.ToDictionary(
                item => item.Key, item => item.Value.Order().ToArray(), StringComparer.Ordinal));
            File.WriteAllText(temporary, JsonSerializer.Serialize(data), new UTF8Encoding(false));
            File.Move(temporary, destination, overwrite: true);
            temporary = null;
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            ReportFailure(ex);
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (IsStorageFailure(ex)) { ReportFailure(ex); }
            }
        }
    }

    private void ReportFailure(Exception ex)
    {
        if (reportedFailure) return;
        reportedFailure = true;
        log?.Invoke("Room description history could not be persisted; session history remains active: " + ex.Message);
    }

    private static bool IsStorageFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException;

    private static string SlotKey(int saveFile, int gameSlot)
    {
        if (saveFile is < 1 or > 10 || gameSlot is < 1 or > 15)
            throw new ArgumentOutOfRangeException(nameof(saveFile), "A native save must identify file 1-10 and game 1-15.");
        return $"{saveFile}:{gameSlot}";
    }

    private static bool IsValidSlotKey(string key)
    {
        var parts = key.Split(':');
        return parts.Length == 2 && int.TryParse(parts[0], out var file) && int.TryParse(parts[1], out var game)
            && file is >= 1 and <= 10 && game is >= 1 and <= 15 && key == SlotKey(file, game);
    }

    private sealed record HistoryFile(int Version, Dictionary<string, int[]> Slots);
}
