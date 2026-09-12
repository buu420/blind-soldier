using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ff7.Accessibility.Reloaded;

/// <summary>One recorded description: the file that says it, and how long it takes.</summary>
public readonly record struct CutsceneVoiceClip(string FileName, TimeSpan Duration);

/// <summary>
/// The recorded descriptions, looked up by the exact words they say.
///
/// <para>Keyed on the text rather than on a script address for two reasons. Several
/// anchors say the same sentence and want one recording between them; and the cues a
/// film falls back to when the game talks over it are the same strings, so one map
/// covers the field descriptions and the film's own timed cues without either side
/// knowing about the other.</para>
///
/// <para>No normalisation anywhere. The file name is the SHA-256 of the exact UTF-8
/// bytes of the text, so a recording either belongs to the words about to be spoken
/// or it does not, and a stray space cannot quietly match the wrong clip.</para>
/// </summary>
public sealed class CutsceneVoiceManifest
{
    private readonly Dictionary<string, CutsceneVoiceClip> byText;

    private CutsceneVoiceManifest(
        Dictionary<string, CutsceneVoiceClip> byText,
        string? sourceHash)
    {
        this.byText = byText;
        SourceHash = sourceHash;
    }

    /// <summary>Nothing recorded; every description falls back to the screen reader.</summary>
    public static CutsceneVoiceManifest Empty { get; } = new([], null);

    public int Count => byText.Count;

    /// <summary>
    /// The hash of the catalog the recordings were generated from, when the manifest
    /// carries one. Logged rather than enforced: a mismatch means some descriptions
    /// have no clip and fall back to speech, which is exactly what a missing entry
    /// already does.
    /// </summary>
    public string? SourceHash { get; }

    /// <summary>The file name a text would be recorded under.</summary>
    public static string FileNameFor(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant() + ".ogg";

    public bool TryGet(string? text, out CutsceneVoiceClip clip)
    {
        if (text is not null && byText.TryGetValue(text, out clip))
        {
            return true;
        }

        clip = default;
        return false;
    }

    /// <summary>
    /// Reads a manifest. Anything malformed yields an empty manifest rather than a
    /// throw, and an entry missing its file name or its duration is skipped rather
    /// than guessed at: a wrong duration holds or releases the dialogue window at the
    /// wrong moment, and speech is a perfectly good answer.
    /// </summary>
    public static CutsceneVoiceManifest Parse(string? json, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var entries = root.ValueKind == JsonValueKind.Array
                ? root
                : root.TryGetProperty("entries", out var nested) ? nested : default;
            if (entries.ValueKind != JsonValueKind.Array)
            {
                log?.Invoke("Cutscene voice manifest has no entries array.");
                return Empty;
            }

            string? sourceHash = null;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("sourceHash", out var hash) &&
                hash.ValueKind == JsonValueKind.String)
            {
                sourceHash = hash.GetString();
            }

            var byText = new Dictionary<string, CutsceneVoiceClip>(StringComparer.Ordinal);
            var skipped = 0;
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object ||
                    !entry.TryGetProperty("text", out var textValue) ||
                    textValue.ValueKind != JsonValueKind.String ||
                    textValue.GetString() is not { Length: > 0 } text)
                {
                    skipped++;
                    continue;
                }

                var file = entry.TryGetProperty("file", out var fileValue) &&
                    fileValue.ValueKind == JsonValueKind.String &&
                    fileValue.GetString() is { Length: > 0 } named
                    ? named
                    : FileNameFor(text);

                if (!entry.TryGetProperty("duration_seconds", out var durationValue) ||
                    !durationValue.TryGetDouble(out var seconds) ||
                    double.IsNaN(seconds) || seconds <= 0d)
                {
                    skipped++;
                    continue;
                }

                byText[text] = new CutsceneVoiceClip(file, TimeSpan.FromSeconds(seconds));
            }

            if (skipped > 0)
            {
                log?.Invoke($"Cutscene voice manifest: {skipped} entry(s) skipped for a missing " +
                    "text or duration; those descriptions fall back to speech.");
            }

            return new CutsceneVoiceManifest(byText, sourceHash);
        }
        catch (Exception ex)
        {
            log?.Invoke($"Cutscene voice manifest could not be read: {ex.Message}");
            return Empty;
        }
    }
}
