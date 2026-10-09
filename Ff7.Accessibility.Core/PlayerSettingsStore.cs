using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ff7.Accessibility.Core;

/// <summary>What became of one setting change.</summary>
/// <param name="Value">The value the setting now has as the player's choice.</param>
/// <param name="Changed">Whether that choice changed.</param>
/// <param name="AppliedNow">Whether the running game's configuration was changed too.</param>
/// <param name="SaveFailure">Why it was not saved or not accepted; null when it was saved.</param>
/// <param name="Notice">Anything else the player should hear, such as a damaged file kept aside.</param>
public sealed record PlayerSettingsChange(
    ModSettingDefinition Setting,
    int Value,
    bool Changed,
    bool AppliedNow,
    string? SaveFailure,
    string? Notice = null)
{
    public bool Saved => SaveFailure is null;
}

/// <summary>
/// The player's own choices from the mod settings, kept in Configuration/player-settings.json
/// and applied over config.json at startup.
///
/// <para>config.json is never written: it stays the packaged, validated file with the player's
/// own edits in it. This file holds only the settings the player changed from it, as
/// <c>{"Version": 1, "Settings": {"Key": value}}</c>, so a later default change still reaches
/// every setting the player left alone, and returning a setting to its config.json value removes
/// it. Each save rereads the file and rewrites only what changed in this session, through a
/// temporary file moved into place, so entries it does not know - from another version of the
/// mod or written by hand - survive. A damaged file is kept beside the new one rather than
/// overwritten, and a file from a newer version of the mod is never written at all. Every
/// failure is returned to the caller so the player hears it.</para>
///
/// <para>A setting whose change only takes effect after a restart
/// (<see cref="ModSettingEffect.AfterRestart"/>) is saved without touching the running
/// configuration, so the game never runs half changed; if it cannot be saved it is not
/// changed at all, since it would otherwise never take effect.</para>
/// </summary>
public sealed class PlayerSettingsStore
{
    public const string FileName = "player-settings.json";
    public const int FormatVersion = 1;
    public const int MaximumFileBytes = 64 * 1024;

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly object sync = new();
    private readonly string? path;
    private readonly Action<string>? log;
    private readonly Dictionary<string, int> baseline = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> startup = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> chosen = new(StringComparer.Ordinal);
    private readonly HashSet<string> unsaved = new(StringComparer.Ordinal);
    private readonly List<string> applied = [];

    private PlayerSettingsStore(
        string? path,
        AccessibilityConfig config,
        ModSettingsRuntime runtime,
        ModSettingsCatalogue catalogue,
        Action<string>? log)
    {
        this.path = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        Config = config;
        Runtime = runtime;
        Catalogue = catalogue;
        this.log = log;
    }

    /// <summary>Raised after a choice changes, outside the store's lock.</summary>
    public event Action<PlayerSettingsChange>? Changed;

    public AccessibilityConfig Config { get; }

    public ModSettingsRuntime Runtime { get; }

    public ModSettingsCatalogue Catalogue { get; }

    /// <summary>Why the stored choices could not be used at startup, worded for the player; null when they could.</summary>
    public string? LoadProblem { get; private set; }

    /// <summary>The settings whose stored choice was applied at startup.</summary>
    public IReadOnlyList<string> AppliedKeys => applied;

    /// <summary>
    /// Reads the stored choices and applies them to <paramref name="config"/>, which must have
    /// just been loaded from config.json; call it before anything reads the configuration.
    /// </summary>
    /// <param name="path">The file, normally Configuration/player-settings.json; null keeps choices in memory.</param>
    public static PlayerSettingsStore Open(
        string? path,
        AccessibilityConfig config,
        ModSettingsRuntime runtime,
        ModSettingsCatalogue? catalogue = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var store = new PlayerSettingsStore(path, config, runtime, catalogue ?? ModSettingsCatalogue.Default, log);
        store.Load();
        return store;
    }

    /// <summary>The value the game started with, after the stored choices.</summary>
    public int StartupValue(string key)
    {
        lock (sync) return Lookup(startup, key);
    }

    /// <summary>The value config.json gave it.</summary>
    public int BaselineValue(string key)
    {
        lock (sync) return Lookup(baseline, key);
    }

    /// <summary>The player's current choice: what the menu reads out.</summary>
    public int ChosenValue(string key)
    {
        lock (sync) return Lookup(chosen, key);
    }

    /// <summary>
    /// The player's choice from the menu: applied to the running game as the setting's
    /// <see cref="ModSettingEffect"/> on this version allows, and saved.
    /// </summary>
    public PlayerSettingsChange Set(string key, int value) => Change(key, value, fromShortcut: false);

    /// <summary>
    /// A value a shortcut (F5 to F8) has already put into effect itself: the configuration is
    /// brought into line with it, and it is saved like a menu choice.
    /// </summary>
    public PlayerSettingsChange Record(string key, int value) => Change(key, value, fromShortcut: true);

    private PlayerSettingsChange Change(string key, int value, bool fromShortcut)
    {
        PlayerSettingsChange change;
        lock (sync)
        {
            var setting = Require(key);
            var effect = setting.EffectOn(Runtime);
            if (!fromShortcut && effect == ModSettingEffect.NotOnThisRuntime)
            {
                return new PlayerSettingsChange(setting, chosen[key], false, false, "this version of the game does not use it");
            }

            if (!setting.Accepts(value))
            {
                return new PlayerSettingsChange(
                    setting, chosen[key], false, false, $"{value.ToString(CultureInfo.InvariantCulture)} is not one of its values");
            }

            var previous = chosen[key];
            if (value == previous)
            {
                // Nothing new, but an earlier choice that could not be saved is tried again.
                var (retryFailure, retryNotice) = unsaved.Count == 0 ? (null, null) : Persist();
                return new PlayerSettingsChange(setting, value, false, false, retryFailure, retryNotice);
            }

            var applyNow = fromShortcut || effect != ModSettingEffect.AfterRestart;
            var wasUnsaved = unsaved.Contains(key);
            if (applyNow)
            {
                setting.Write(Config, value);
            }

            chosen[key] = value;
            unsaved.Add(key);
            var (failure, notice) = Persist();
            if (failure is not null && !applyNow)
            {
                // It could only take effect after a restart, and a restart would not have it.
                chosen[key] = previous;
                if (!wasUnsaved)
                {
                    unsaved.Remove(key);
                }

                return new PlayerSettingsChange(setting, previous, false, false, failure, notice);
            }

            change = new PlayerSettingsChange(setting, value, true, applyNow, failure, notice);
        }

        Changed?.Invoke(change);
        return change;
    }

    private void Load()
    {
        foreach (var setting in Settings())
        {
            baseline[setting.Key] = setting.Read(Config);
        }

        if (path is not null)
        {
            var file = ReadFile();
            switch (file.State)
            {
                case FileState.Valid:
                    Apply(file.Root!);
                    break;
                case FileState.Damaged:
                    LoadProblem =
                        $"The saved settings file could not be read ({file.Reason}), so none of its choices were used. " +
                        "It will be kept aside the next time a setting is changed.";
                    break;
                case FileState.Newer:
                    LoadProblem =
                        "The saved settings file was written by a newer version of the mod and could not be used; " +
                        "it will not be changed.";
                    break;
                case FileState.Unreadable:
                    LoadProblem = $"The saved settings file could not be opened ({file.Reason}), so its choices were not used.";
                    break;
            }

            if (LoadProblem is not null)
            {
                log?.Invoke("Player settings: " + LoadProblem);
            }
        }

        foreach (var setting in Settings())
        {
            startup[setting.Key] = chosen[setting.Key] = setting.Read(Config);
        }
    }

    private void Apply(JsonObject root)
    {
        if (root["Settings"] is not JsonObject settings)
        {
            return;
        }

        foreach (var (name, node) in settings)
        {
            if (Catalogue.Find(name) is not { Kind: not ModSettingKind.Action } setting)
            {
                // Not a player setting this version knows: left in the file, never applied.
                continue;
            }

            if (TryParse(setting, node, out var value) && setting.Accepts(value))
            {
                setting.Write(Config, value);
                applied.Add(name);
            }
            else
            {
                log?.Invoke($"Player settings: stored {name} = {node?.ToJsonString() ?? "null"} was ignored; it is not one of its values.");
            }
        }

        if (applied.Count > 0)
        {
            log?.Invoke($"Player settings: applied {applied.Count} stored choice(s) over config.json: {string.Join(", ", applied)}.");
        }
    }

    /// <summary>Writes what changed this session. Returns why it could not, and any notice.</summary>
    private (string? Failure, string? Notice) Persist()
    {
        if (path is null)
        {
            unsaved.Clear();
            return (null, null);
        }

        var file = ReadFile();
        string? notice = null;
        JsonObject root;
        IEnumerable<string> keys;
        switch (file.State)
        {
            case FileState.Valid:
                root = file.Root!;
                keys = unsaved;
                break;
            case FileState.Missing:
                root = NewRoot();
                keys = chosen.Keys;
                break;
            case FileState.Damaged:
                var kept = KeepAside();
                if (kept.Failure is not null)
                {
                    return Failed($"the damaged settings file could not be moved aside: {kept.Failure}");
                }

                notice = $"The settings file that could not be read was kept as {Path.GetFileName(kept.Path)}.";
                root = NewRoot();
                keys = chosen.Keys;
                break;
            case FileState.Newer:
                return Failed("the settings file was written by a newer version of the mod");
            default:
                return Failed($"the settings file could not be opened: {file.Reason}");
        }

        if (root["Settings"] is not JsonObject settings)
        {
            settings = new JsonObject();
            root["Settings"] = settings;
        }

        foreach (var key in keys.ToArray())
        {
            if (chosen[key] == baseline[key])
            {
                settings.Remove(key);
            }
            else
            {
                settings[key] = Catalogue.Find(key)!.Kind == ModSettingKind.Toggle
                    ? JsonValue.Create(chosen[key] != 0)
                    : JsonValue.Create(chosen[key]);
            }
        }

        root["Version"] = FormatVersion;
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, root.ToJsonString(WriteOptions), new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
            temporary = null;
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            return Failed(ex.Message);
        }
        finally
        {
            if (temporary is not null)
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception ex) when (IsStorageFailure(ex))
                {
                    log?.Invoke($"Player settings: temporary file {temporary} could not be removed: {ex.Message}");
                }
            }
        }

        unsaved.Clear();
        return (null, notice);

        (string?, string?) Failed(string reason)
        {
            log?.Invoke($"Player settings could not be saved: {reason}.");
            return (reason, notice);
        }
    }

    private (string? Path, string? Failure) KeepAside()
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var aside = $"{path}.unreadable-{stamp}";
        if (File.Exists(aside))
        {
            aside += "-" + Guid.NewGuid().ToString("N")[..8];
        }

        try
        {
            File.Move(path!, aside);
            log?.Invoke($"Player settings: the unreadable file was kept as {aside}.");
            return (aside, null);
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            return (null, ex.Message);
        }
    }

    private StoredFile ReadFile()
    {
        if (!File.Exists(path))
        {
            return new StoredFile(FileState.Missing, null, null);
        }

        string text;
        try
        {
            if (new FileInfo(path!).Length > MaximumFileBytes)
            {
                return new StoredFile(FileState.Damaged, null, "it is larger than 64 kilobytes");
            }

            text = File.ReadAllText(path!);
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            return new StoredFile(FileState.Unreadable, null, ex.Message);
        }

        try
        {
            if (JsonNode.Parse(text, documentOptions: ReadOptions) is not JsonObject root)
            {
                return new StoredFile(FileState.Damaged, null, "it is not a settings object");
            }

            var version = FormatVersion;
            if (root["Version"] is { } versionNode &&
                (versionNode is not JsonValue versionValue || !versionValue.TryGetValue(out version)))
            {
                return new StoredFile(FileState.Damaged, null, "its version is not a number");
            }

            if (version > FormatVersion)
            {
                return new StoredFile(FileState.Newer, null, null);
            }

            if (version < 1 || (root["Settings"] is { } settings && settings is not JsonObject))
            {
                return new StoredFile(FileState.Damaged, null, "it does not have the settings layout");
            }

            // JsonNode materializes object properties lazily. Duplicate keys throw
            // ArgumentException only when that dictionary is first accessed, so do
            // it inside this damage boundary rather than later during startup Apply.
            if (root["Settings"] is JsonObject settingsObject) _ = settingsObject.Count;

            return new StoredFile(FileState.Valid, root, null);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return new StoredFile(FileState.Damaged, null, ex.Message);
        }
    }

    private static bool TryParse(ModSettingDefinition setting, JsonNode? node, out int value)
    {
        value = 0;
        if (node is not JsonValue json)
        {
            return false;
        }

        if (setting.Kind == ModSettingKind.Toggle)
        {
            if (json.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }

            value = json.GetValue<bool>() ? 1 : 0;
            return true;
        }

        return json.GetValueKind() == JsonValueKind.Number && json.TryGetValue(out value);
    }

    private static JsonObject NewRoot() => new() { ["Version"] = FormatVersion, ["Settings"] = new JsonObject() };

    private IEnumerable<ModSettingDefinition> Settings() =>
        Catalogue.Entries.Where(setting => setting.Kind != ModSettingKind.Action);

    private ModSettingDefinition Require(string key) =>
        Catalogue.Find(key) is { Kind: not ModSettingKind.Action } setting
            ? setting
            : throw new ArgumentException($"{key} is not a player setting.", nameof(key));

    private int Lookup(Dictionary<string, int> values, string key) =>
        values.TryGetValue(Require(key).Key, out var value) ? value : throw new ArgumentException(key, nameof(key));

    private static bool IsStorageFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException;

    private enum FileState
    {
        Missing,
        Valid,
        Damaged,
        Newer,
        Unreadable,
    }

    private readonly record struct StoredFile(FileState State, JsonObject? Root, string? Reason);
}
