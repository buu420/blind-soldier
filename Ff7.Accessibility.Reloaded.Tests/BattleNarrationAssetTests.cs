using System.Text.Json;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class BattleNarrationAssetTests
{
    internal static void Run()
    {
        var directory = ResolveDirectory();
        var catalogJson = File.ReadAllText(Path.Combine(directory, "battle-animations.json"));
        var catalog = BattleAnimationNarrationCatalog.Parse(catalogJson);
        Require(catalog.Count == 93 && catalog.MissingKeys.Count == 0,
            "All 93 native animation identities need an installed description.");
        var manifest = CutsceneVoiceManifest.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        using var rows = JsonDocument.Parse(catalogJson);
        var cueCount = 0;
        var openingCount = 0;
        foreach (var row in rows.RootElement.EnumerateArray())
        {
            Require(row.GetProperty("revision").GetInt32() == 2,
                $"Revised narration must replay once per save: {row.GetProperty("key")}.");
            var cues = row.GetProperty("cues");
            Require(cues.GetArrayLength() > 0, "Every animation needs visual cues.");
            if (row.GetProperty("key").GetString()!.StartsWith("summon.", StringComparison.Ordinal))
                Require(row.GetProperty("anchor").GetString() == "summon", "Summons must wait for their own scene.");
            if (row.GetProperty("key").GetString() == "opening.summon")
            {
                openingCount++;
                Require(row.GetProperty("anchor").GetString() == "banner", "The casting opening must follow the visible title.");
            }
            foreach (var cue in cues.EnumerateArray())
            {
                cueCount++;
                var text = cue.GetProperty("text").GetString()!;
                Require(manifest.TryGet(text, out var clip), $"No own-voice recording for {row.GetProperty("key")}.");
                Require(clip.FileName == CutsceneVoiceManifest.FileNameFor(text), "Recording must match its exact script.");
                Require(clip.Duration > TimeSpan.Zero && clip.Duration < TimeSpan.FromSeconds(30), "Invalid clip duration.");
                var bytes = File.ReadAllBytes(Path.Combine(directory, clip.FileName));
                Require(bytes.Length > 32 && bytes.AsSpan(0, 4).SequenceEqual("OggS"u8), "Missing or invalid Vorbis asset.");
            }
        }
        Require(cueCount == 176 && openingCount == 1, "All reviewed stages and the shared summon opening must be packaged.");
        Console.WriteLine($"PASS all 93 battle descriptions have matching packaged voice recordings ({cueCount} staged cues).");
    }

    private static string ResolveDirectory()
    {
        var source = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (!string.IsNullOrWhiteSpace(source))
        {
            var path = Path.Combine(source, "Ff7.Accessibility.Reloaded", "Assets", "cutscene-voice");
            if (File.Exists(Path.Combine(path, "battle-animations.json"))) return path;
        }
        var installed = Path.Combine(AppContext.BaseDirectory, "Assets", "cutscene-voice");
        if (File.Exists(Path.Combine(installed, "battle-animations.json"))) return installed;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "Ff7.Accessibility.Reloaded", "Assets", "cutscene-voice");
            if (File.Exists(Path.Combine(path, "battle-animations.json"))) return path;
        }
        throw new InvalidOperationException("Battle narration assets were not installed with the test payload.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
