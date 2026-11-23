using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class EchoDialoguePreferencesTests
{
    public static void Run()
    {
        var voiced = ModSettingsCatalogue.Default.Find("ReadVoicedDialogue");
        Check(voiced is not null, "the spoken menu must offer Read voiced dialogue");
        Check(voiced!.Read(new AccessibilityConfig()) == 0, "recorded lines are not duplicated by default");
        Check(ModSettingsCatalogue.Default.Find("ResetAllDescriptions")?.Kind == ModSettingKind.Action,
            "a separate full description reset is available");

        var western = (string)typeof(Ff7EncodedTextDecoder).Assembly
            .GetType("Ff7.Accessibility.Reloaded.Ff7TextEncoding")!
            .GetField("Western", BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!;
        byte[] Encode(string text) => text.Select(ch => checked((byte)western.IndexOf(ch))).ToArray();
        byte[] message = [(byte)western.IndexOf('≠'), 0xEA, 0xE7, .. Encode("“Cloud Strife.”"), 0xFF];
        var english = Ff7GameLanguages.Get(Ff7GameLanguage.English);
        var page = Ff7EncodedTextDecoder.DecodeFieldPages(message, english).Single();
        Check(page.Lines[0].Text == "≠Cloud", "native page identity must retain its exact decoded speaker prefix");
        Check(page.Lines[1].Text == "“Cloud Strife.”", "the spoken line is preserved");
        string? spoken = null;
        using var speaker = new PrismNativeSpeaker(_ => { }, (nint)1, (nint)2,
            (_, pointer, _) => { spoken = Marshal.PtrToStringUTF8(pointer); return PrismError.Ok; }, _ => { });
        Check(speaker.Speak(string.Join(" ", page.Lines.Select(line => line.Text))) && spoken == "Cloud “Cloud Strife.”",
            "Prism receives the dialogue without the decorative not-equal glyph");
        speaker.Speak("2 ≠ 3");
        Check(spoken == "2 ≠ 3", "Prism preserves real mathematical symbols");
        Check(Ff7EncodedTextDecoder.DecodeField([.. Encode("2 ≠ 3"), 0xFF], english) == "2 ≠ 3",
            "a real mathematical symbol is preserved");
        PreferencePersistsAndFullResetIsSaveScoped();
        var lobby = FieldCutsceneDescriptionCatalog.CreateAllAreaDescriptions().Single(cue => cue.FieldId == 218);
        Check(lobby.ObservationOnly, "the Honeybee arrival must work with changed Echo-S script offsets");
        var arrival = new FieldAreaDescriptionColdStartTracker(FieldCutsceneDescriptionCatalog.CreateAllAreaDescriptions());
        Check(arrival.Observe(FieldPositionReader.FieldModule, 218, DateTime.UnixEpoch) is null,
            "arrival waits for a stable native field");
        Check(arrival.Observe(FieldPositionReader.FieldModule, 218, DateTime.UnixEpoch.AddSeconds(3))?.Text == lobby.Text,
            "a direct load or ordinary entry into the Honeybee lobby offers its description");
        Check(arrival.Observe(FieldPositionReader.FieldModule, 218, DateTime.UnixEpoch.AddSeconds(4)) is null,
            "the lobby arrival does not repeat on every tick");
        Console.WriteLine("PASS Echo-S dialogue preference, speaker marker and full reset menu.");
    }

    private static void PreferencePersistsAndFullResetIsSaveScoped()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BlindSoldier-echo-dialogue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settingsPath = Path.Combine(directory, PlayerSettingsStore.FileName);
            var config = new AccessibilityConfig();
            var store = PlayerSettingsStore.Open(settingsPath, config, ModSettingsRuntime.Legacy);
            var rooms = new FieldAreaDescriptionHistory(Path.Combine(directory, "rooms.json"));
            var battle = new FieldAreaDescriptionHistory(Path.Combine(directory, "battle.json"));
            foreach (var history in new[] { rooms, battle })
            {
                history.LoadSave(1, 2); history.MarkHeard(218);
                history.LoadSave(1, 1); history.MarkHeard(218);
            }
            var epoch = rooms.ResetEpoch;
            var menu = new ModSettingsMenu(config, store, () => "battle only", resetAllDescriptions: () =>
                BattleAnimationNarrationRuntime.ResetAllDescriptions(rooms, battle));
            menu.Open();
            void Select(string key)
            {
                for (var i = 0; i < ModSettingsCatalogue.Default.Entries.Count && menu.Current.Key != key; i++)
                    menu.Handle(ModSettingsMenuCommand.Next);
                Check(menu.Current.Key == key, "the setting is reachable by ordinary menu navigation");
            }
            Select("ReadVoicedDialogue");
            menu.Handle(ModSettingsMenuCommand.Activate);
            var nextConfig = new AccessibilityConfig();
            PlayerSettingsStore.Open(settingsPath, nextConfig, ModSettingsRuntime.Legacy);
            Check(ModSettingsCatalogue.Default.Find("ReadVoicedDialogue")!.Read(nextConfig) == 1,
                "read voiced dialogue survives a restart");
            Select("ResetAllDescriptions");
            menu.Handle(ModSettingsMenuCommand.Activate);
            Check(rooms.HasHeard(218) && battle.HasHeard(218), "the first press only asks for confirmation");
            var speech = menu.Handle(ModSettingsMenuCommand.Activate);
            Check(speech.Contains("Room descriptions reset", StringComparison.Ordinal) &&
                !rooms.HasHeard(218) && !battle.HasHeard(218), "the confirmed full reset clears both histories");
            Check(!rooms.TryMarkHeard(218, rooms.PlaythroughRevision, epoch),
                "an in-flight room description cannot undo the reset");
            foreach (var path in new[] { "rooms.json", "battle.json" })
            {
                var reloaded = new FieldAreaDescriptionHistory(Path.Combine(directory, path));
                reloaded.LoadSave(1, 1);
                Check(!reloaded.HasHeard(218), "the full reset persists");
                reloaded.LoadSave(1, 2);
                Check(reloaded.HasHeard(218), "other saves retain their description history");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
    }
}
