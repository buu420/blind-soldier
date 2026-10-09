using System.Reflection;
using System.Text.Json.Nodes;
using Ff7.Accessibility.Core;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The spoken mod settings: a declarative catalogue of every player switch, a store that
/// keeps only the player's own changes in Configuration/player-settings.json on top of
/// config.json, and a menu that says what each change does - including when it needs a
/// restart, when this version of the game does not use it, and when it could not be saved.
/// </summary>
internal static class ModSettingsTests
{
    private static readonly DateTime T0 = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    public static void Run()
    {
        // The catalogue.
        EveryPlayerSwitchIsListedOrDeliberatelyLeftOut();
        EveryEntryReadsAndWritesItsOwnSetting();
        EveryEntryIsLabelledForThePlayer();
        TheVolumeAndTheResetLeadTheMenu();

        // The store.
        OnlyThePlayersChangesAreStored();
        StoredChangesApplyOverConfigJsonAndNothingElse();
        UnknownAndUnrelatedSettingsSurviveASave();
        AnInvalidStoredValueIsIgnoredNotApplied();
        ADamagedFileIsKeptAsideAndReported();
        DuplicateKeysAreRejectedAndPreserved();
        ALockedFileFailsTheSaveAndSurvives();
        AFileFromANewerVersionIsNeverOverwritten();
        ShortcutsSaveThroughTheSameStore();

        // The menu.
        OpeningSpeaksTheControlsAndTheFirstSetting();
        MovingAnnouncesNewCategoriesAndWraps();
        TheMenuKeepsSpeakingWithModSpeechOff();
        AStartupOnlySettingSaysItNeedsARestart();
        ASettingThisVersionDoesNotUseSaysSoAndStaysPut();
        TurningOnAFeatureThatWasOffAtStartupNeedsARestart();
        TheVolumeStepsWithinItsRangeLive();
        AFailedSaveIsSpokenNotHidden();
        ARestartOnlySettingThatCannotBeSavedIsLeftAlone();
        TheResetNeedsASecondActivation();
        AnyOtherCommandCancelsTheReset();
        ASlowSecondActivationOnlyAsksAgain();
        AResetThatFailsSaysSo();
        AClosedMenuIgnoresCommands();
        Console.WriteLine("PASS mod settings: full player catalogue, override store, spoken menu with restart, save and reset feedback.");
    }

    // --- the catalogue -------------------------------------------------------------------

    private static void EveryPlayerSwitchIsListedOrDeliberatelyLeftOut()
    {
        var catalogue = ModSettingsCatalogue.Default;
        var switches = typeof(AccessibilityConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(bool))
            .Select(property => property.Name)
            .ToList();
        var listed = catalogue.Entries.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        var excluded = ModSettingsCatalogue.Excluded;
        foreach (var name in switches)
        {
            Check(listed.Contains(name) ^ excluded.ContainsKey(name),
                $"{name} must be either offered to the player or deliberately left out, not both or neither");
        }

        foreach (var (name, reason) in excluded)
        {
            Check(switches.Contains(name), $"{name} is excluded but is not a switch in AccessibilityConfig");
            Check(!string.IsNullOrWhiteSpace(reason), $"{name} is excluded without a reason");
        }

        foreach (var name in switches.Where(name =>
                     name.Contains("Diagnostics", StringComparison.Ordinal) ||
                     name.Contains("Probe", StringComparison.Ordinal) ||
                     name.EndsWith("Hook", StringComparison.Ordinal) ||
                     name.EndsWith("Hooks", StringComparison.Ordinal) ||
                     name.Contains("Experimental", StringComparison.Ordinal)))
        {
            Check(!listed.Contains(name), $"{name} is a diagnostic or technical switch and must not be in the player menu");
        }

        Check(listed.Contains(nameof(AccessibilityConfig.EnableSpeech)) &&
              listed.Contains(nameof(AccessibilityConfig.EnableBattleAnimationDescriptions)) &&
              listed.Contains(nameof(AccessibilityConfig.EnableHighwayAutoSteering)) &&
              listed.Contains(nameof(AccessibilityConfig.EnableBattleStatusSpeech)),
            "the player switches of every area are offered, not just the live handful");
        Check(listed.Count(key => switches.Contains(key)) >= 60, "every player switch is offered");
    }

    private static void EveryEntryReadsAndWritesItsOwnSetting()
    {
        var config = new AccessibilityConfig();
        foreach (var entry in ModSettingsCatalogue.Default.Entries)
        {
            if (entry.Kind == ModSettingKind.Action)
            {
                Check(typeof(AccessibilityConfig).GetProperty(entry.Key) is null, $"{entry.Key}: an action is not a stored setting");
                continue;
            }

            var property = typeof(AccessibilityConfig).GetProperty(entry.Key);
            Check(property is not null, $"{entry.Key} names a real setting");
            foreach (var value in entry.Values)
            {
                entry.Write(config, value);
                var raw = property!.GetValue(config);
                var stored = raw is bool flag ? (flag ? 1 : 0) : (int)raw!;
                Check(stored == value && entry.Read(config) == value, $"{entry.Key} writes and reads its own setting ({value})");
            }

            Check(entry.EffectOn(ModSettingsRuntime.Legacy) != ModSettingEffect.NotOnThisRuntime ||
                  entry.EffectOn(ModSettingsRuntime.Steam2026) != ModSettingEffect.NotOnThisRuntime,
                $"{entry.Key} does something on at least one version of the game");
        }
    }

    private static void EveryEntryIsLabelledForThePlayer()
    {
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ModSettingsCatalogue.Default.Entries)
        {
            Check(!string.IsNullOrWhiteSpace(entry.Label) && !string.IsNullOrWhiteSpace(entry.Category),
                $"{entry.Key} has a label and a category");
            Check(labels.Add(entry.Label), $"{entry.Label} is not used twice");
            Check(!entry.Label.Contains("Enable", StringComparison.Ordinal) && entry.Label != entry.Key &&
                  !System.Text.RegularExpressions.Regex.IsMatch(entry.Label, "[a-z][A-Z]"),
                $"{entry.Key} is labelled in words, not by its configuration name");
            foreach (var runtime in Enum.GetValues<ModSettingsRuntime>())
            {
                Check(entry.SupportOn(runtime).Note is not { } note || (note.EndsWith('.') && !note.Contains("Enable", StringComparison.Ordinal)),
                    $"{entry.Key}: a note is a spoken sentence");
            }
        }
    }

    private static void TheVolumeAndTheResetLeadTheMenu()
    {
        var entries = ModSettingsCatalogue.Default.Entries;
        Check(entries[0].Key == nameof(AccessibilityConfig.AudioDescriptionVolumePercent) && entries[0].Kind == ModSettingKind.Level,
            "the description volume comes first");
        Check(entries[0].Values.SequenceEqual([50, 75, 100, 125, 150, 175, 200, 225, 250, 275, 300]),
            "50 to 300 percent in steps of 25");
        Check(entries[0].EffectOn(ModSettingsRuntime.Legacy) == ModSettingEffect.Immediate &&
              entries[0].EffectOn(ModSettingsRuntime.Steam2026) == ModSettingEffect.Immediate, "and applies at once");
        Check(entries[1].Key == ModSettingsCatalogue.ResetBattleDescriptionsKey && entries[1].Kind == ModSettingKind.Action,
            "the battle description reset follows it");
        Check(ModSettingsCatalogue.Default.Find(nameof(AccessibilityConfig.NavigationProgressIntervalPercent)) is
              { Kind: ModSettingKind.Choice } interval && interval.Values.SequenceEqual([5, 10, 15, 20]),
            "the progress interval offers the same four steps as its shortcut");
    }

    // --- the store -----------------------------------------------------------------------

    private static void OnlyThePlayersChangesAreStored()
    {
        WithDirectory(directory =>
        {
            var path = Path.Combine(directory, PlayerSettingsStore.FileName);
            var config = new AccessibilityConfig();
            var store = PlayerSettingsStore.Open(path, config, ModSettingsRuntime.Legacy);
            Check(!File.Exists(path), "opening writes nothing");

            var change = store.Set(nameof(AccessibilityConfig.EnableBattleAnimationDescriptions), 0);
            Check(change.Saved && change.Changed && !config.EnableBattleAnimationDescriptions, "the change is applied and saved");
            Check(Settings(path).Count == 1 && Settings(path)["EnableBattleAnimationDescriptions"]!.GetValue<bool>() == false,
                "only the changed switch is stored, as a switch");

            store.Set(nameof(AccessibilityConfig.AudioDescriptionVolumePercent), 150);
            Check(Settings(path)["AudioDescriptionVolumePercent"]!.GetValue<int>() == 150, "a level is stored as a number");

            var back = store.Set(nameof(AccessibilityConfig.EnableBattleAnimationDescriptions), 1);
            Check(back.Saved && config.EnableBattleAnimationDescriptions && !Settings(path).ContainsKey("EnableBattleAnimationDescriptions"),
                "returning to the config.json value removes the override");
            Check(Settings(path).Count == 1, "leaving only the volume");

            var unchanged = store.Set(nameof(AccessibilityConfig.AudioDescriptionVolumePercent), 150);
            Check(!unchanged.Changed && unchanged.Saved, "setting the same value again is not a change");
            Check(Directory.EnumerateFiles(directory).Count() == 1, "no temporary file is left behind");
        });
    }

    private static void StoredChangesApplyOverConfigJsonAndNothingElse()
    {
        WithDirectory(directory =>
        {
            var path = Path.Combine(directory, PlayerSettingsStore.FileName);
            File.WriteAllText(path, """
                {"Version": 1, "Settings": {
                  "AudioDescriptionVolumePercent": 200,
                  "EnableFieldFootstepFeedback": false,
                  "FieldFootstepVolumePercent": 10,
                  "EnableFieldMessageWindowDiagnostics": false
                }}
                """);

            // As loaded from config.json, including a player's own hand edits there.
            var config = new AccessibilityConfig { FieldFootstepVolumePercent = 150, EnableFieldFootstepFeedback = true };
            var store = PlayerSettingsStore.Open(path, config, ModSettingsRuntime.Steam2026);
            Check(config.AudioDescriptionVolumePercent == 200 && !config.EnableFieldFootstepFeedback,
                "stored choices apply over config.json");
            Check(config.FieldFootstepVolumePercent == 150 && config.EnableFieldMessageWindowDiagnostics,
                "keys outside the player catalogue are never applied");
            Check(store.LoadProblem is null && store.AppliedKeys.Count == 2, "two choices applied, no problem reported");
            Check(store.StartupValue(nameof(AccessibilityConfig.EnableFieldFootstepFeedback)) == 0 &&
                  store.BaselineValue(nameof(AccessibilityConfig.EnableFieldFootstepFeedback)) == 1,
                "the store remembers both the game's starting value and config.json's");
        });
    }

    private static void UnknownAndUnrelatedSettingsSurviveASave()
    {
        WithDirectory(directory =>
        {
            var path = Path.Combine(directory, PlayerSettingsStore.FileName);
            File.WriteAllText(path, """
                {"Version": 1, "Note": "kept", "Settings": {
                  "SomeFutureSetting": "x",
                  "AudioDescriptionVolumePercent": 125
                }}
                """);
            var store = PlayerSettingsStore.Open(path, new AccessibilityConfig(), ModSettingsRuntime.Legacy);
            Check(store.Set(nameof(AccessibilityConfig.EnableBattleAnimationDescriptions), 0).Saved, "saved");
            var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Check(root["Note"]!.GetValue<string>() == "kept", "an unknown top-level entry survives");
            Check(root["Settings"]!["SomeFutureSetting"]!.GetValue<string>() == "x", "an unknown setting survives");
            Check(root["Settings"]!["AudioDescriptionVolumePercent"]!.GetValue<int>() == 125, "an untouched choice survives");
        });
    }

    private static void AnInvalidStoredValueIsIgnoredNotApplied()
    {
        WithDirectory(directory =>
        {
            var path = Path.Combine(directory, PlayerSettingsStore.FileName);
            File.WriteAllText(path, """
                {"Version": 1, "Settings": {
                  "AudioDescriptionVolumePercent": "loud",
                  "EnableSpeech": 3,
                  "NavigationProgressIntervalPercent": 7,
                  "EnableSnowboardReadout": false
                }}
                """);
            var log = new List<string>();
            var config = new AccessibilityConfig();
            var store = PlayerSettingsStore.Open(path, config, ModSettingsRuntime.Legacy, log: log.Add);
            Check(config.AudioDescriptionVolumePercent == 100 && config.EnableSpeech && config.NavigationProgressIntervalPercent == 5,
                "values of the wrong kind or outside their choices are not applied");
            Check(!config.EnableSnowboardReadout && store.AppliedKeys.SequenceEqual(["EnableSnowboardReadout"]),
                "the valid choice beside them still is");
            Check(log.Count(line => line.Contains("ignored", StringComparison.OrdinalIgnoreCase)) == 3, "each one is logged");
        });
    }

    private static void ADamagedFileIsKeptAsideAndReported()
    {
        WithDirectory(directory =>
        {
            var path = Path.Combine(directory, PlayerSettingsStore.FileName);
            File.WriteAllText(path, "{ not json");
            var config = new AccessibilityConfig();
            var store = PlayerSettingsStore.Open(path, config, ModSettingsRuntime.Legacy);
            Check(store.LoadProblem is { Length: > 0 }, "a damaged file is reported");
            Check(config.AudioDescriptionVolumePercent == 100, "and nothing from it is applied");

            var change = store.Set(nameof(AccessibilityConfig.AudioDescriptionVolumePercent), 175);
            Check(change.Saved && change.Notice is { Length: > 0 }, "the next change is saved and says what happened to the old file");
            var kept = Directory.EnumerateFiles(directory).Where(file => file != path).ToList();
            Check(kept.Count == 1 && File.ReadAllText(kept[0]) == "{ not json", "the damaged file is kept, unchanged, beside it");
            Check(Settings(path)["AudioDescriptionVolumePercent"]!.GetValue<int>() == 175, "and a fresh file holds the change");
        });
    }

    private static void DuplicateKeysAreRejectedAndPreserved()
    {
        foreach (var damaged in new[]
        {
            """{"Version":1,"Version":2,"Settings":{}}""",
            """{"Version":1,"Settings":{"EnableSpeech":true,"EnableSpeech":false}}"""
        })
        {
            WithDirectory(directory =>
            {
                var path = Path.Combine(directory, PlayerSettingsStore.FileName);
                File.WriteAllText(path, damaged);
                var config = new AccessibilityConfig();
                var store = PlayerSettingsStore.Open(path, config, ModSettingsRuntime.Legacy);
                Check(store.LoadProblem is { Length: > 0 } && config.EnableSpeech,
                    "duplicate keys are reported without applying an ambiguous setting or breaking startup");
                Check(File.ReadAllText(path) == damaged, "opening preserves the damaged bytes");
                Check(store.Set(nameof(AccessibilityConfig.AudioDescriptionVolumePercent), 150).Saved,
                    "a later choice saves with recovery feedback");
                Check(Directory.EnumerateFiles(directory).Any(file => file != path && File.ReadAllText(file) == damaged),
                    "the original duplicate-key file is retained beside the replacement");
            });
        }
    }

    private static void ALockedFileFailsTheSaveAndSurvives()
    {
        WithDirectory(directory =>
        {
            var path = Path.Combine(directory, PlayerSettingsStore.FileName);
            File.WriteAllText(path, """{"Version": 1, "Settings": {"EnableSnowboardReadout": false}}""");
            var bytes = File.ReadAllBytes(path);
            var config = new AccessibilityConfig();
            var store = PlayerSettingsStore.Open(path, config, ModSettingsRuntime.Legacy);
            PlayerSettingsChange change;
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                change = store.Set(nameof(AccessibilityConfig.EnableBattleAnimationDescriptions), 0);
            }

            Check(!change.Saved && change.SaveFailure is { Length: > 0 }, "a save that cannot be written says why");
            Check(change.Changed && change.AppliedNow && !config.EnableBattleAnimationDescriptions,
                "a live setting still applies for this session");
            Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes), "the existing file is untouched");
            Check(Directory.EnumerateFiles(directory).Count() == 1, "and no temporary file is left behind");
            Check(store.Set(nameof(AccessibilityConfig.EnableBattleAnimationDescriptions), 0) is { Saved: true },
                "once the file is free again, the same choice saves");
            Check(Settings(path).ContainsKey("EnableSnowboardReadout") && Settings(path).ContainsKey("EnableBattleAnimationDescriptions"),
                "keeping what was there");
        });
    }

    private static void AFileFromANewerVersionIsNeverOverwritten()
    {
        WithDirectory(directory =>
        {
            var path = Path.Combine(directory, PlayerSettingsStore.FileName);
            File.WriteAllText(path, """{"Version": 2, "Settings": {"AudioDescriptionVolumePercent": 300}}""");
            var bytes = File.ReadAllBytes(path);
            var config = new AccessibilityConfig();
            var store = PlayerSettingsStore.Open(path, config, ModSettingsRuntime.Steam2026);
            Check(store.LoadProblem is { Length: > 0 } && config.AudioDescriptionVolumePercent == 100,
                "a newer file is reported and not interpreted");
            var change = store.Set(nameof(AccessibilityConfig.AudioDescriptionVolumePercent), 150);
            Check(!change.Saved && change.SaveFailure!.Contains("newer", StringComparison.Ordinal), "and is not overwritten");
            Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes), "its bytes are unchanged");
        });
    }

    private static void ShortcutsSaveThroughTheSameStore()
    {
        WithDirectory(directory =>
        {
            var path = Path.Combine(directory, PlayerSettingsStore.FileName);
            var config = new AccessibilityConfig();
            var store = PlayerSettingsStore.Open(path, config, ModSettingsRuntime.Legacy);
            var heard = new List<(string Key, int Value, bool AppliedNow)>();
            store.Changed += change => heard.Add((change.Setting.Key, change.Value, change.AppliedNow));

            // F7 has already moved the progress interval and F8 has already switched Highway
            // auto-steering in their own controllers; the store records what they did.
            Check(store.Record(nameof(AccessibilityConfig.NavigationProgressIntervalPercent), 10).Saved &&
                  store.Record(nameof(AccessibilityConfig.EnableHighwayAutoSteering), 0).Saved, "shortcut changes save");
            Check(config.NavigationProgressIntervalPercent == 10 && !config.EnableHighwayAutoSteering,
                "and the configuration follows what the shortcut did");
            Check(store.ChosenValue(nameof(AccessibilityConfig.NavigationProgressIntervalPercent)) == 10,
                "so the menu shows the shortcut's value");
            Check(heard.SequenceEqual([("NavigationProgressIntervalPercent", 10, true), ("EnableHighwayAutoSteering", 0, true)]),
                "the host hears each change");
            Check(store.Record(nameof(AccessibilityConfig.NavigationProgressIntervalPercent), 7) is { Changed: false } refused &&
                  refused.SaveFailure is { Length: > 0 } && config.NavigationProgressIntervalPercent == 10,
                "a value outside the choices is refused");
            var reopened = new AccessibilityConfig();
            PlayerSettingsStore.Open(path, reopened, ModSettingsRuntime.Legacy);
            Check(reopened.NavigationProgressIntervalPercent == 10 && !reopened.EnableHighwayAutoSteering,
                "and both survive restarting the game");
        });
    }

    // --- the menu ------------------------------------------------------------------------

    private static void OpeningSpeaksTheControlsAndTheFirstSetting()
    {
        var rig = new MenuRig();
        var opening = rig.Menu.Open();
        Check(rig.Menu.IsOpen, "the menu opens");
        Check(opening.StartsWith("Mod settings.", StringComparison.Ordinal), "it says what it is: " + opening);
        foreach (var key in new[] { "J and L", "U and O", "I ", "K ", "F11" })
        {
            Check(opening.Contains(key, StringComparison.Ordinal), $"the keyboard controls name {key}: {opening}");
        }

        Check(opening.Contains("Narration: Description volume, 100 percent. 1 of 6.", StringComparison.Ordinal),
            "then the first setting with its value and place: " + opening);
        Check(opening.Contains("saved", StringComparison.OrdinalIgnoreCase), "and that changes are saved");
        Check(rig.Menu.Close() == "Mod settings closed." && !rig.Menu.IsOpen, "closing says so");

        var pad = rig.Menu.Open(ModSettingsInputDevice.Controller);
        foreach (var key in new[] { "Up and Down", "Left and Right", "A ", "B " })
        {
            Check(pad.Contains(key, StringComparison.Ordinal), $"the controller controls name {key}: {pad}");
        }

        Check(!pad.Contains("F11", StringComparison.Ordinal), "and not the keyboard's");
    }

    private static void MovingAnnouncesNewCategoriesAndWraps()
    {
        var rig = new MenuRig();
        rig.Menu.Open();
        Check(rig.Menu.Handle(ModSettingsMenuCommand.Next) == "Reset battle descriptions for this save. 2 of 6. Press I twice to reset.",
            "the next item in the same category: " + rig.Last);
        var third = rig.Menu.Handle(ModSettingsMenuCommand.Next);
        Check(third.StartsWith("Speech: Mod speech, on. 3 of 6.", StringComparison.Ordinal), "a new category is named: " + third);
        var wrapped = rig.Menu.Handle(ModSettingsMenuCommand.Previous);
        rig.Menu.Handle(ModSettingsMenuCommand.Previous);
        var last = rig.Menu.Handle(ModSettingsMenuCommand.Previous);
        Check(wrapped.StartsWith("Narration: Reset", StringComparison.Ordinal) && last.Contains("6 of 6", StringComparison.Ordinal),
            "moving back names the category again and wraps to the end: " + last);
        Check(rig.Menu.Handle(ModSettingsMenuCommand.Repeat) == last, "repeat says it again in full");
    }

    private static void TheMenuKeepsSpeakingWithModSpeechOff()
    {
        var rig = new MenuRig();
        rig.Menu.Open();
        rig.Menu.Handle(ModSettingsMenuCommand.Next);
        rig.Menu.Handle(ModSettingsMenuCommand.Next);
        var off = rig.Menu.Handle(ModSettingsMenuCommand.Activate);
        Check(!rig.Config.EnableSpeech, "mod speech is off");
        Check(off.StartsWith("Off.", StringComparison.Ordinal) && off.Contains("this menu", StringComparison.Ordinal),
            "turning it off says the menu keeps speaking: " + off);
        foreach (var command in Enum.GetValues<ModSettingsMenuCommand>())
        {
            Check(rig.Menu.Handle(command).Length > 0, $"{command} still answers with mod speech off");
        }

        rig.Menu.Close();
        var reopened = rig.Menu.Open();
        Check(reopened.Contains("Mod speech is off", StringComparison.Ordinal), "reopening says only the menu is speaking: " + reopened);
    }

    private static void AStartupOnlySettingSaysItNeedsARestart()
    {
        var rig = new MenuRig();
        rig.Menu.Open();
        var item = rig.MoveTo(nameof(AccessibilityConfig.SpeakOnLoad));
        Check(item.Contains("after restarting the game", StringComparison.Ordinal), "the item says when it applies: " + item);
        var change = rig.Menu.Handle(ModSettingsMenuCommand.Activate);
        Check(change.StartsWith("Off.", StringComparison.Ordinal) && change.Contains("restart the game", StringComparison.Ordinal),
            "the change says it needs a restart: " + change);
        Check(rig.Saved(nameof(AccessibilityConfig.SpeakOnLoad))?.GetValue<bool>() == false, "and is saved for then");
        Check(rig.Config.SpeakOnLoad, "while the running game keeps the value it started with");
        Check(rig.Menu.Handle(ModSettingsMenuCommand.Repeat).Contains("off", StringComparison.Ordinal),
            "the menu reads back the saved choice");
        var back = rig.Menu.Handle(ModSettingsMenuCommand.Activate);
        Check(back == "On.", "back at the value the game started with, there is nothing to restart for: " + back);
        Check(!File.Exists(rig.Path) || rig.Saved(nameof(AccessibilityConfig.SpeakOnLoad)) is null,
            "and nothing is left stored for it");
    }

    private static void ASettingThisVersionDoesNotUseSaysSoAndStaysPut()
    {
        var rig = new MenuRig();
        rig.Menu.Open();
        var item = rig.MoveTo(nameof(AccessibilityConfig.EnableHighwayAutoSteering));
        Check(item.Contains("Not used by this version of the game.", StringComparison.Ordinal), "the item says so: " + item);
        var refused = rig.Menu.Handle(ModSettingsMenuCommand.Activate);
        Check(refused.Contains("Not used by this version of the game", StringComparison.Ordinal) &&
              refused.Contains("nothing changed", StringComparison.Ordinal), "changing it is refused: " + refused);
        Check(rig.Config.EnableHighwayAutoSteering && !File.Exists(rig.Path), "nothing is changed or saved");
    }

    private static void TurningOnAFeatureThatWasOffAtStartupNeedsARestart()
    {
        var rig = new MenuRig(config => config.EnableBattleStatusSpeech = false);
        rig.Menu.Open();
        var item = rig.MoveTo(nameof(AccessibilityConfig.EnableBattleStatusSpeech));
        Check(item.Contains("off", StringComparison.Ordinal) && item.Contains("Turning it on takes effect after restarting the game.", StringComparison.Ordinal),
            "an item that was off at startup says turning it on needs a restart: " + item);
        var on = rig.Menu.Handle(ModSettingsMenuCommand.Increase);
        Check(on.StartsWith("On.", StringComparison.Ordinal) && on.Contains("restart the game", StringComparison.Ordinal), "turning it on: " + on);

        var running = new MenuRig();
        running.Menu.Open();
        running.MoveTo(nameof(AccessibilityConfig.EnableBattleStatusSpeech));
        Check(running.Menu.Handle(ModSettingsMenuCommand.Decrease) == "Off." &&
              running.Menu.Handle(ModSettingsMenuCommand.Increase) == "On.",
            "on at startup, turning it off and on again applies at once");
        Check(running.Menu.Handle(ModSettingsMenuCommand.Increase) == "Already on.", "and says when nothing changed");
    }

    private static void TheVolumeStepsWithinItsRangeLive()
    {
        var rig = new MenuRig();
        rig.Menu.Open();
        Check(rig.Menu.Handle(ModSettingsMenuCommand.Increase) == "125 percent." &&
              rig.Config.AudioDescriptionVolumePercent == 125, "up a step, live");
        Check(rig.Saved(nameof(AccessibilityConfig.AudioDescriptionVolumePercent)) is JsonNode saved && saved.GetValue<int>() == 125, "and saved");
        for (var i = 0; i < 20; i++) rig.Press(ModSettingsMenuCommand.Increase);
        Check(rig.Last == "300 percent, the maximum." && rig.Config.AudioDescriptionVolumePercent == 300, "stops at 300: " + rig.Last);
        for (var i = 0; i < 20; i++) rig.Press(ModSettingsMenuCommand.Decrease);
        Check(rig.Last == "50 percent, the minimum." && rig.Config.AudioDescriptionVolumePercent == 50, "stops at 50: " + rig.Last);
        var activate = rig.Menu.Handle(ModSettingsMenuCommand.Activate);
        Check(activate.Contains("U and O", StringComparison.Ordinal) && rig.Config.AudioDescriptionVolumePercent == 50,
            "activating a level says how to change it: " + activate);
    }

    private static void AFailedSaveIsSpokenNotHidden()
    {
        var rig = new MenuRig();
        File.WriteAllText(rig.Path, """{"Version": 2, "Settings": {}}""");
        rig.Reopen();
        var opening = rig.Menu.Open();
        Check(opening.Contains("could not", StringComparison.Ordinal) && opening.Contains("newer", StringComparison.Ordinal),
            "a settings file that cannot be used is mentioned when the menu opens: " + opening);
        var change = rig.Menu.Handle(ModSettingsMenuCommand.Increase);
        Check(change.StartsWith("125 percent, for this session only.", StringComparison.Ordinal) &&
              change.Contains("could not be saved", StringComparison.Ordinal),
            "a live change that could not be saved says it lasts only for this session: " + change);
        Check(rig.Config.AudioDescriptionVolumePercent == 125, "while still applying now");
    }

    private static void ARestartOnlySettingThatCannotBeSavedIsLeftAlone()
    {
        var rig = new MenuRig();
        File.WriteAllText(rig.Path, """{"Version": 2, "Settings": {}}""");
        rig.Reopen();
        rig.Menu.Open();
        rig.MoveTo(nameof(AccessibilityConfig.SpeakOnLoad));
        var change = rig.Menu.Handle(ModSettingsMenuCommand.Activate);
        Check(change.StartsWith("Not changed", StringComparison.Ordinal) && change.Contains("could not be saved", StringComparison.Ordinal),
            "a change that could only apply after a restart, and cannot be saved, is not made: " + change);
        Check(rig.Config.SpeakOnLoad, "the setting keeps its value");
    }

    private static void TheResetNeedsASecondActivation()
    {
        var rig = new MenuRig();
        rig.Menu.Open();
        rig.Menu.Handle(ModSettingsMenuCommand.Next);
        var ask = rig.Menu.Handle(ModSettingsMenuCommand.Activate);
        Check(rig.Resets == 0, "one press resets nothing");
        Check(ask.Contains("summon", StringComparison.Ordinal) && ask.Contains("Other saves", StringComparison.Ordinal) &&
              ask.Contains("Press I again", StringComparison.Ordinal), "it says what will happen and how to confirm: " + ask);
        rig.Clock = rig.Clock.AddSeconds(10);
        var done = rig.Menu.Handle(ModSettingsMenuCommand.Activate);
        Check(rig.Resets == 1 && done == MenuRig.ResetSpeech, "the second press resets and speaks the result");
        Check(rig.Menu.Handle(ModSettingsMenuCommand.Activate) == ask && rig.Resets == 1, "a third press asks again");

        var pad = new MenuRig();
        pad.Menu.Open(ModSettingsInputDevice.Controller);
        pad.Menu.Handle(ModSettingsMenuCommand.Next);
        Check(pad.Menu.Handle(ModSettingsMenuCommand.Activate).Contains("Press A again", StringComparison.Ordinal),
            "the controller prompt names A");
    }

    private static void AnyOtherCommandCancelsTheReset()
    {
        foreach (var command in new[]
                 {
                     ModSettingsMenuCommand.Next, ModSettingsMenuCommand.Previous, ModSettingsMenuCommand.Increase,
                     ModSettingsMenuCommand.Decrease, ModSettingsMenuCommand.Repeat,
                 })
        {
            var rig = new MenuRig();
            rig.Menu.Open();
            rig.Menu.Handle(ModSettingsMenuCommand.Next);
            rig.Menu.Handle(ModSettingsMenuCommand.Activate);
            var cancelled = rig.Menu.Handle(command);
            Check(cancelled.StartsWith("Reset cancelled.", StringComparison.Ordinal), $"{command} cancels: {cancelled}");
            rig.Menu.Handle(ModSettingsMenuCommand.Activate);
            Check(rig.Resets == 0 || command is ModSettingsMenuCommand.Next or ModSettingsMenuCommand.Previous,
                $"after {command} the next press only asks again");
            Check(rig.Resets == 0, $"nothing was reset after {command}");
        }

        var closing = new MenuRig();
        closing.Menu.Open();
        closing.Menu.Handle(ModSettingsMenuCommand.Next);
        closing.Menu.Handle(ModSettingsMenuCommand.Activate);
        Check(closing.Menu.Close() == "Reset cancelled. Mod settings closed.", "closing cancels too");
        closing.Menu.Open();
        closing.Menu.Handle(ModSettingsMenuCommand.Activate);
        Check(closing.Resets == 0, "and reopening does not keep the first press");
    }

    private static void ASlowSecondActivationOnlyAsksAgain()
    {
        var rig = new MenuRig();
        rig.Menu.Open();
        rig.Menu.Handle(ModSettingsMenuCommand.Next);
        var ask = rig.Menu.Handle(ModSettingsMenuCommand.Activate);
        rig.Clock = rig.Clock.AddSeconds(31);
        Check(rig.Menu.Handle(ModSettingsMenuCommand.Activate) == ask && rig.Resets == 0, "a press after 30 seconds asks again");
        rig.Clock = rig.Clock.AddSeconds(5);
        rig.Menu.Handle(ModSettingsMenuCommand.Activate);
        Check(rig.Resets == 1, "and a prompt second press then resets");
    }

    private static void AResetThatFailsSaysSo()
    {
        var rig = new MenuRig(reset: () => throw new IOException("disk gone"));
        rig.Menu.Open();
        rig.Menu.Handle(ModSettingsMenuCommand.Next);
        rig.Menu.Handle(ModSettingsMenuCommand.Activate);
        var failed = rig.Menu.Handle(ModSettingsMenuCommand.Activate);
        Check(failed.Contains("could not be reset", StringComparison.Ordinal) && failed.Contains("disk gone", StringComparison.Ordinal),
            "a failing reset is spoken: " + failed);
    }

    private static void AClosedMenuIgnoresCommands()
    {
        var rig = new MenuRig();
        foreach (var command in Enum.GetValues<ModSettingsMenuCommand>())
        {
            Check(rig.Menu.Handle(command) == string.Empty, $"{command} does nothing while closed");
        }

        Check(rig.Config.AudioDescriptionVolumePercent == 100 && rig.Resets == 0 && !File.Exists(rig.Path), "and changes nothing");
        Check(rig.Menu.Close() == string.Empty, "closing a closed menu says nothing");
        rig.Menu.Open();
        rig.Menu.Handle(ModSettingsMenuCommand.Next);
        rig.Menu.Close();
        Check(rig.Menu.Open().Contains("2 of 6", StringComparison.Ordinal), "reopening returns to the same setting");
    }

    // --- helpers -------------------------------------------------------------------------

    private static JsonObject Settings(string path) =>
        JsonNode.Parse(File.ReadAllText(path))!.AsObject()["Settings"]!.AsObject();

    private static void WithDirectory(Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "blind-soldier-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            test(directory);
        }
        finally
        {
            // Only files created by this test, under its unique temporary directory.
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Mod settings: " + message);
    }

    /// <summary>A menu over a small catalogue with one entry of each kind and effect.</summary>
    private sealed class MenuRig
    {
        public const string ResetSpeech = "Battle descriptions reset for save 1, game 1.";

        private static readonly ModSettingsCatalogue Catalogue = new(
        [
            ModSettingDefinition.Level(
                nameof(AccessibilityConfig.AudioDescriptionVolumePercent), "Narration", "Description volume",
                config => config.AudioDescriptionVolumePercent, (config, value) => config.AudioDescriptionVolumePercent = value,
                AudioDescriptionLevel.MinimumPercent, AudioDescriptionLevel.MaximumPercent, AudioDescriptionLevel.StepPercent,
                ModSettingEffect.Immediate, ModSettingEffect.Immediate),
            ModSettingDefinition.Action(
                ModSettingsCatalogue.ResetBattleDescriptionsKey, "Narration", "Reset battle descriptions for this save"),
            ModSettingDefinition.Toggle(
                nameof(AccessibilityConfig.EnableSpeech), "Speech", "Mod speech",
                config => config.EnableSpeech, (config, value) => config.EnableSpeech = value,
                ModSettingEffect.Immediate, ModSettingEffect.Immediate),
            ModSettingDefinition.Toggle(
                nameof(AccessibilityConfig.SpeakOnLoad), "Speech", "Announce when the mod loads",
                config => config.SpeakOnLoad, (config, value) => config.SpeakOnLoad = value,
                ModSettingEffect.AfterRestart, ModSettingEffect.AfterRestart),
            ModSettingDefinition.Toggle(
                nameof(AccessibilityConfig.EnableHighwayAutoSteering), "Minigames", "Highway automatic steering",
                config => config.EnableHighwayAutoSteering, (config, value) => config.EnableHighwayAutoSteering = value,
                ModSettingEffect.NotOnThisRuntime, ModSettingEffect.Immediate),
            ModSettingDefinition.Toggle(
                nameof(AccessibilityConfig.EnableBattleStatusSpeech), "Battle", "Status changes",
                config => config.EnableBattleStatusSpeech, (config, value) => config.EnableBattleStatusSpeech = value,
                ModSettingEffect.OffNowOnAfterRestart, ModSettingEffect.OffNowOnAfterRestart),
        ]);

        private readonly string directory;
        private readonly Action<AccessibilityConfig>? configJson;
        private readonly Func<string> reset;

        public MenuRig(Action<AccessibilityConfig>? configJson = null, Func<string>? reset = null)
        {
            directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "blind-soldier-menu-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Cleanup.Add(directory);
            Path = System.IO.Path.Combine(directory, PlayerSettingsStore.FileName);
            this.configJson = configJson;
            this.reset = reset ?? (() =>
            {
                Resets++;
                return ResetSpeech;
            });
            Reopen();
        }

        public string Path { get; }

        public AccessibilityConfig Config { get; private set; } = null!;

        public ModSettingsMenu Menu { get; private set; } = null!;

        public int Resets { get; private set; }

        public DateTime Clock { get; set; } = T0;

        public string Last { get; private set; } = string.Empty;

        /// <summary>Starts the game again: config.json, then the stored choices, then a new menu.</summary>
        public void Reopen()
        {
            Config = new AccessibilityConfig();
            configJson?.Invoke(Config);
            var store = PlayerSettingsStore.Open(Path, Config, ModSettingsRuntime.Legacy, Catalogue);
            Menu = new ModSettingsMenu(Config, store, () => reset(), () => Clock);
        }

        public string Press(ModSettingsMenuCommand command) => Last = Menu.Handle(command);

        public string MoveTo(string key)
        {
            for (var i = 0; i < Catalogue.Entries.Count; i++)
            {
                if (Menu.Current.Key == key)
                {
                    return Menu.Handle(ModSettingsMenuCommand.Repeat);
                }

                Menu.Handle(ModSettingsMenuCommand.Next);
            }

            throw new InvalidOperationException("Mod settings: no item " + key);
        }

        public JsonNode? Saved(string key) =>
            File.Exists(Path) ? JsonNode.Parse(File.ReadAllText(Path))!["Settings"]![key] : null;

        private static readonly List<string> Cleanup = [];

        static MenuRig()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                foreach (var folder in Cleanup)
                {
                    try
                    {
                        // Only the folders these tests created, each under a unique name.
                        foreach (var file in Directory.EnumerateFiles(folder)) File.Delete(file);
                        Directory.Delete(folder);
                    }
                    catch (IOException)
                    {
                    }
                }
            };
        }
    }
}
