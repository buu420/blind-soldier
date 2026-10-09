using System.Globalization;
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
    private bool newGame;
    private bool reportedFailure;
    private long playthroughRevision;
    private long resetEpoch;

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

    /// <summary>
    /// Whether it was heard, and the <see cref="ResetEpoch"/> that answer belongs to, read
    /// together so a reset cannot fall between them.
    /// </summary>
    public bool HasHeard(int fieldId, out long resetEpoch)
    {
        lock (sync)
        {
            resetEpoch = this.resetEpoch;
            return heard.Contains(fieldId);
        }
    }

    /// <summary>Changes on a load or new game, but not when saving this playthrough.</summary>
    public long PlaythroughRevision
    {
        get { lock (sync) return playthroughRevision; }
    }

    /// <summary>
    /// Changes when the player resets this history (<see cref="ResetActiveSave"/>), and only
    /// then. It is deliberately not <see cref="PlaythroughRevision"/>: the same playthrough
    /// stays on screen, so nothing already playing is stopped or started again from its
    /// middle, but a description that was already being told when the player reset must not
    /// then record itself heard - the player asked to hear it again at its next occurrence.
    /// </summary>
    public long ResetEpoch
    {
        get { lock (sync) return resetEpoch; }
    }

    /// <summary>A delayed recording cannot mark a different playthrough as heard.</summary>
    public bool TryMarkHeard(int fieldId, long expectedPlaythroughRevision)
    {
        lock (sync)
        {
            if (playthroughRevision != expectedPlaythroughRevision || fieldId is < 0 or > ushort.MaxValue)
                return false;
            MarkHeard(fieldId);
            return true;
        }
    }

    /// <summary>
    /// As <see cref="TryMarkHeard(int, long)"/>, and refused too when the player has reset
    /// the history since <paramref name="expectedResetEpoch"/> was read: a description that
    /// was in flight across the reset cannot put back the entry the player just cleared.
    /// </summary>
    public bool TryMarkHeard(int fieldId, long expectedPlaythroughRevision, long expectedResetEpoch)
    {
        lock (sync)
        {
            return resetEpoch == expectedResetEpoch && TryMarkHeard(fieldId, expectedPlaythroughRevision);
        }
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
            playthroughRevision++;
            activeSlot = key;
            newGame = false;
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
            newGame = false;
            profiles[key] = new HashSet<int>(heard);
            Persist();
        }
    }

    /// <summary>Unidentified/new games deduplicate in memory until a native load or save identifies their slot.</summary>
    public void BeginNewGame()
    {
        lock (sync)
        {
            playthroughRevision++;
            activeSlot = null;
            newGame = true;
            heard = [];
        }
    }

    /// <summary>
    /// The player's reset: forgets everything heard in the playthrough being played, so each
    /// description is told again at its next occurrence. Only the bound save's entry in the
    /// file is removed; every other save keeps its history. An unsaved new game is reset in
    /// memory, and its first save then carries only what was heard afterwards. Descriptions
    /// already in flight finish but are not recorded (<see cref="ResetEpoch"/>).
    /// </summary>
    public DescriptionHistoryResetResult ResetActiveSave()
    {
        lock (sync)
        {
            resetEpoch++;
            var cleared = heard.Count;
            heard = [];
            if (activeSlot is null)
            {
                var scope = newGame || cleared > 0
                    ? DescriptionHistoryResetScope.UnsavedGame
                    : DescriptionHistoryResetScope.NoGame;
                return new DescriptionHistoryResetResult(scope, 0, 0, cleared, null);
            }

            var parts = activeSlot.Split(':');
            profiles.Remove(activeSlot);
            return new DescriptionHistoryResetResult(
                DescriptionHistoryResetScope.SavedGame,
                int.Parse(parts[0], CultureInfo.InvariantCulture),
                int.Parse(parts[1], CultureInfo.InvariantCulture),
                cleared,
                Persist());
        }
    }

    /// <returns>Why the file could not be written, or null when it was or there is no file.</returns>
    private string? Persist()
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
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
            return null;
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            ReportFailure(ex);
            return ex.Message;
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

/// <summary>Whose history a reset cleared.</summary>
public enum DescriptionHistoryResetScope
{
    /// <summary>Nothing has been loaded or started yet, so there was nothing to clear.</summary>
    NoGame,

    /// <summary>A game not yet tied to a native save: cleared in memory only.</summary>
    UnsavedGame,

    /// <summary>The loaded or last saved native save, in memory and in the file.</summary>
    SavedGame,
}

/// <param name="SaveFile">The native save file, 1-10, for <see cref="DescriptionHistoryResetScope.SavedGame"/>.</param>
/// <param name="GameSlot">The game within it, 1-15, for <see cref="DescriptionHistoryResetScope.SavedGame"/>.</param>
/// <param name="ClearedCount">How many heard entries the playthrough had.</param>
/// <param name="PersistenceFailure">Why the file could not be written, or null.</param>
public sealed record DescriptionHistoryResetResult(
    DescriptionHistoryResetScope Scope,
    int SaveFile,
    int GameSlot,
    int ClearedCount,
    string? PersistenceFailure)
{
    public bool Persisted => PersistenceFailure is null;
}
