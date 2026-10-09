using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// "Reset battle descriptions" in the mod settings. The save being played has its limit
/// break and summon descriptions, and the shared summoning opening, told again the next
/// time each animation natively plays; every other save, and the room history, keep
/// theirs. A description that was already being told when the player reset must not then
/// record itself heard, and the animation on screen is neither cut off nor started again
/// from its middle.
/// </summary>
internal static class BattleDescriptionResetTests
{
    private static readonly DateTime T0 = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    private const byte Summon = 0x03;
    private const byte Limit = 0x14;
    private const int BraverKey = 0x1400;
    private const int BahamutZeroKey = 0x030E;
    private const int SummonOpeningKey = 0x0200;
    private const int StagedOpeningKey = 0x8200;
    private const int StagedKnightsKey = 0x830F;

    public static void Run()
    {
        // The history file.
        AResetClearsOnlyTheCurrentSavesBattleHistory();
        AResetOfAnUnsavedGameStaysInMemory();
        AResetBeforeAnyGameTouchesNoSave();
        AResetThatCannotBeSavedSaysSo();

        // Descriptions around the reset.
        AFinishedDescriptionPlaysAgainAfterAReset();
        ADescriptionPlayingDuringAResetIsNotRecordedAsHeard();
        AnAnimationOnScreenDuringAResetIsNotDescribedFromItsMiddle();
        ADescriptionWaitingDuringAResetIsToldButNotRecorded();
        TheSummoningOpeningIsResetWithTheSummon();
        TheSummoningOpeningPlayingDuringAResetIsNotRecorded();

        // What the player hears.
        TheResetSpeechNamesTheSaveAndAnyFailure();
        Console.WriteLine("PASS battle description reset: current save only, in-flight descriptions stay unheard, nothing restarts.");
    }

    // --- the history file ---------------------------------------------------------------

    private static void AResetClearsOnlyTheCurrentSavesBattleHistory()
    {
        WithDirectory(directory =>
        {
            var roomPath = Path.Combine(directory, "room-descriptions.json");
            var rooms = new FieldAreaDescriptionHistory(roomPath);
            rooms.LoadSave(1, 1);
            rooms.MarkHeard(546);
            var roomBytes = File.ReadAllBytes(roomPath);

            var battlePath = Path.Combine(directory, "battle-descriptions.json");
            var battle = new FieldAreaDescriptionHistory(battlePath);
            battle.LoadSave(1, 2);
            Mark(battle, BraverKey);
            battle.LoadSave(3, 5);
            Mark(battle, BahamutZeroKey);
            battle.LoadSave(1, 1);
            Mark(battle, BraverKey);
            Mark(battle, BahamutZeroKey);
            Mark(battle, SummonOpeningKey);

            var result = battle.ResetActiveSave();
            Check(result.Scope == DescriptionHistoryResetScope.SavedGame && result.SaveFile == 1 &&
                  result.GameSlot == 1 && result.Persisted && result.ClearedCount == 3,
                "the reset names save 1, game 1 and is saved");
            Check(!battle.HasHeard(BraverKey) && !battle.HasHeard(BahamutZeroKey) && !battle.HasHeard(SummonOpeningKey),
                "limit, summon and the summoning opening are all unheard again at once");

            var reloaded = new FieldAreaDescriptionHistory(battlePath);
            reloaded.LoadSave(1, 1);
            Check(!reloaded.HasHeard(BraverKey) && !reloaded.HasHeard(BahamutZeroKey) && !reloaded.HasHeard(SummonOpeningKey),
                "the reset survives restarting the game");
            reloaded.LoadSave(1, 2);
            Check(reloaded.HasHeard(BraverKey), "another game in the same save file keeps its history");
            reloaded.LoadSave(3, 5);
            Check(reloaded.HasHeard(BahamutZeroKey), "another save file keeps its history");

            Check(File.ReadAllBytes(roomPath).AsSpan().SequenceEqual(roomBytes), "the room history file is untouched");
            var roomsAgain = new FieldAreaDescriptionHistory(roomPath);
            roomsAgain.LoadSave(1, 1);
            Check(roomsAgain.HasHeard(546), "and so are the rooms heard in the reset save");
        });
    }

    private static void AResetOfAnUnsavedGameStaysInMemory()
    {
        WithDirectory(directory =>
        {
            var path = Path.Combine(directory, "battle-descriptions.json");
            var seed = new FieldAreaDescriptionHistory(path);
            seed.LoadSave(1, 1);
            Mark(seed, BraverKey);
            var bytes = File.ReadAllBytes(path);

            var battle = new FieldAreaDescriptionHistory(path);
            battle.BeginNewGame();
            Mark(battle, BahamutZeroKey);
            var result = battle.ResetActiveSave();
            Check(result.Scope == DescriptionHistoryResetScope.UnsavedGame && result.Persisted && result.ClearedCount == 1,
                "a new game that was never saved is reset in memory");
            Check(!battle.HasHeard(BahamutZeroKey), "its heard summon is unheard again");
            Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes), "no save's history is rewritten");

            Mark(battle, BraverKey);
            battle.SaveGame(2, 3);
            var reloaded = new FieldAreaDescriptionHistory(path);
            reloaded.LoadSave(2, 3);
            Check(reloaded.HasHeard(BraverKey) && !reloaded.HasHeard(BahamutZeroKey),
                "its first save carries only what was heard after the reset");
            reloaded.LoadSave(1, 1);
            Check(reloaded.HasHeard(BraverKey), "the other save is unchanged");
        });
    }

    private static void AResetBeforeAnyGameTouchesNoSave()
    {
        WithDirectory(directory =>
        {
            var path = Path.Combine(directory, "battle-descriptions.json");
            var seed = new FieldAreaDescriptionHistory(path);
            seed.LoadSave(4, 4);
            Mark(seed, BraverKey);
            var bytes = File.ReadAllBytes(path);

            // The title screen: nothing has been loaded or started in this session.
            var battle = new FieldAreaDescriptionHistory(path);
            var result = battle.ResetActiveSave();
            Check(result.Scope == DescriptionHistoryResetScope.NoGame && result.ClearedCount == 0,
                "before a game is loaded there is nothing to reset");
            Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes), "and no save is touched");
        });
    }

    private static void AResetThatCannotBeSavedSaysSo()
    {
        WithDirectory(directory =>
        {
            // A directory where the file should be: every write fails.
            var log = new List<string>();
            var battle = new FieldAreaDescriptionHistory(directory, log.Add);
            battle.LoadSave(1, 1);
            Mark(battle, BraverKey);
            var result = battle.ResetActiveSave();
            Check(result.Scope == DescriptionHistoryResetScope.SavedGame && !result.Persisted &&
                  !string.IsNullOrWhiteSpace(result.PersistenceFailure),
                "a reset that could not be written says why");
            Check(!battle.HasHeard(BraverKey), "it still applies for the rest of this session");
        });
    }

    // --- descriptions around the reset ----------------------------------------------------

    private static void AFinishedDescriptionPlaysAgainAfterAReset()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        history.LoadSave(1, 1);
        var rig = new Rig(history);
        rig.PlayToEnd(Braver(), 0);
        Check(history.HasHeard(BraverKey), "Braver was heard");

        rig.Update(Braver(), 30);
        rig.Update(BattleAnimationObservation.Idle, 31);
        Check(rig.Voice.Started.Count == 1, "while heard, the next Braver is silent");

        history.ResetActiveSave();
        rig.Update(Braver(), 60);
        Check(rig.Voice.Started.SequenceEqual(["braver.ogg", "braver.ogg"]), "after the reset the next Braver is described");
        rig.Update(BattleAnimationObservation.Idle, 61);
        rig.Voice.Last.Finish();
        rig.Update(BattleAnimationObservation.Idle, 70);
        Check(history.HasHeard(BraverKey), "and, once told in full, it is heard again");
    }

    private static void ADescriptionPlayingDuringAResetIsNotRecordedAsHeard()
    {
        WithDirectory(directory =>
        {
            var path = Path.Combine(directory, "battle-descriptions.json");
            var history = new FieldAreaDescriptionHistory(path);
            history.LoadSave(1, 1);
            var rig = new Rig(history);
            rig.Update(Braver(), 0);
            Check(rig.Voice.Started.SequenceEqual(["braver.ogg"]), "Braver's description is playing");

            history.ResetActiveSave();
            rig.Update(Braver(), 1);
            rig.Update(BattleAnimationObservation.Idle, 3);
            Check(rig.Narration.IsPlaying && rig.Voice.Last.Stops == 0,
                "the description already playing is not cut off by the reset");
            Check(rig.Voice.Started.Count == 1, "nor started a second time");

            rig.Voice.Last.Finish();
            rig.Update(BattleAnimationObservation.Idle, 6.1);
            Check(!history.HasHeard(BraverKey), "told across the reset, it does not record itself heard");
            var reloaded = new FieldAreaDescriptionHistory(path);
            reloaded.LoadSave(1, 1);
            Check(!reloaded.HasHeard(BraverKey), "and does not repopulate the saved history");

            rig.Update(Braver(), 30);
            Check(rig.Voice.Started.Count == 2, "so the next Braver is described");
            rig.Update(BattleAnimationObservation.Idle, 31);
            rig.Voice.Last.Finish();
            rig.Update(BattleAnimationObservation.Idle, 40);
            Check(history.HasHeard(BraverKey), "and that one is heard as usual");
        });
    }

    private static void AnAnimationOnScreenDuringAResetIsNotDescribedFromItsMiddle()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        history.LoadSave(1, 1);
        Mark(history, BraverKey);
        var rig = new Rig(history);
        rig.Update(Braver(), 0);
        Check(rig.Voice.Started.Count == 0, "a heard Braver plays without a description");

        history.ResetActiveSave();
        for (var poll = 1; poll <= 20; poll++)
        {
            // The same native row stays current while the animation runs on.
            rig.Update(Braver(), poll * 0.035);
        }

        Check(rig.Voice.Started.Count == 0 && rig.Spoken.Count == 0,
            "the animation already on screen is not described from its middle");
        rig.Update(BattleAnimationObservation.Idle, 2);
        rig.Update(Braver(), 10);
        Check(rig.Voice.Started.SequenceEqual(["braver.ogg"]), "the next native Braver is");
    }

    private static void ADescriptionWaitingDuringAResetIsToldButNotRecorded()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        history.LoadSave(1, 1);
        var rig = new Rig(history) { OtherDescriptionPlaying = true };
        rig.Update(BahamutZero(), 0);
        Check(rig.Voice.Started.Count == 0, "Bahamut ZERO waits for the voice");

        history.ResetActiveSave();
        rig.OtherDescriptionPlaying = false;
        rig.Update(BahamutZero(), 1);
        Check(rig.Voice.Started.SequenceEqual(["bahamut-zero.ogg"]),
            "the animation on screen is still described once the voice is free");
        rig.Update(BattleAnimationObservation.Idle, 2);
        rig.Voice.Last.Finish();
        rig.Update(BattleAnimationObservation.Idle, 12);
        Check(!history.HasHeard(BahamutZeroKey), "but, opened before the reset, it is not recorded as heard");

        rig.Update(BahamutZero(), 40);
        Check(rig.Voice.Started.Count == 2, "so the next Bahamut ZERO is described too");
    }

    private static void TheSummoningOpeningIsResetWithTheSummon()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        history.LoadSave(1, 1);
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(OpeningJson), history, OpeningManifest);
        CastKnightsToTheEnd(rig, 0);
        Check(history.HasHeard(StagedOpeningKey) && history.HasHeard(StagedKnightsKey), "the opening and Knights were heard");

        CastUntilTheSummonBegins(rig, 20);
        Check(rig.Voice.Started.Count == 2, "while heard, the next cast is silent");
        rig.Update(IdleAt(140), 26);

        history.ResetActiveSave();
        CastKnightsToTheEnd(rig, 40);
        Check(rig.Voice.Started.SequenceEqual(["orbs.ogg", "knights-appear.ogg", "orbs.ogg", "knights-appear.ogg"]),
            "after the reset both the opening and the summon are described on the next cast");
        Check(history.HasHeard(StagedOpeningKey) && history.HasHeard(StagedKnightsKey), "and are heard again");
    }

    private static void TheSummoningOpeningPlayingDuringAResetIsNotRecorded()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        history.LoadSave(1, 1);
        var rig = new Rig(BattleAnimationNarrationCatalog.Parse(OpeningJson), history, OpeningManifest);
        CastUntilTheSummonBegins(rig, 0);
        Check(rig.Voice.Started.SequenceEqual(["orbs.ogg"]) && rig.Narration.IsPlaying, "the opening is playing");

        history.ResetActiveSave();
        rig.Voice.Last.Finish();
        rig.Update(KnightsCast(80), 5.3);
        rig.Update(KnightsCast(81), 5.4);
        Check(rig.Voice.Started.SequenceEqual(["orbs.ogg", "knights-appear.ogg"]),
            "the cast carries on: the summon's own cue plays and the opening does not start again");
        rig.Voice.Last.Finish();
        rig.Update(IdleAt(90), 6);
        Check(!history.HasHeard(StagedOpeningKey) && !history.HasHeard(StagedKnightsKey),
            "neither, opened before the reset, is recorded as heard");

        CastUntilTheSummonBegins(rig, 20);
        Check(rig.Voice.Started.SequenceEqual(["orbs.ogg", "knights-appear.ogg", "orbs.ogg"]),
            "the next cast is described from its opening");
    }

    // --- what the player hears -------------------------------------------------------------

    private static void TheResetSpeechNamesTheSaveAndAnyFailure()
    {
        var log = new List<string>();
        var none = new FieldAreaDescriptionHistory(path: null);
        var nothing = BattleAnimationNarrationRuntime.ResetBattleDescriptions(none, log.Add);
        Check(nothing.Contains("No game", StringComparison.Ordinal), "before a game: " + nothing);

        var unsaved = new FieldAreaDescriptionHistory(path: null);
        unsaved.BeginNewGame();
        var fresh = BattleAnimationNarrationRuntime.ResetBattleDescriptions(unsaved, log.Add);
        Check(fresh.Contains("unsaved game", StringComparison.Ordinal) &&
              fresh.Contains("described again", StringComparison.Ordinal), "an unsaved game: " + fresh);

        var saved = new FieldAreaDescriptionHistory(path: null);
        saved.LoadSave(2, 11);
        var reset = BattleAnimationNarrationRuntime.ResetBattleDescriptions(saved, log.Add);
        Check(reset.Contains("save 2, game 11", StringComparison.Ordinal) &&
              reset.Contains("summoning", StringComparison.Ordinal) &&
              reset.Contains("described again", StringComparison.Ordinal) &&
              !reset.Contains("could not", StringComparison.Ordinal), "a saved game: " + reset);

        WithDirectory(directory =>
        {
            var broken = new FieldAreaDescriptionHistory(directory);
            broken.LoadSave(1, 1);
            var failed = BattleAnimationNarrationRuntime.ResetBattleDescriptions(broken, log.Add);
            Check(failed.Contains("could not be saved", StringComparison.Ordinal) &&
                  failed.Contains("this session", StringComparison.Ordinal), "a failed write: " + failed);
        });

        Check(log.Count >= 4, "every reset is logged");
    }

    // --- helpers -----------------------------------------------------------------------------

    private static BattleAnimationObservation Braver() =>
        BattleAnimationObservation.Playing(0, 0, Limit, 0x00, 0x0000);

    private static BattleAnimationObservation BahamutZero() =>
        BattleAnimationObservation.Playing(0, 0, Summon, 0x0E, 0x000E);

    private static void Mark(FieldAreaDescriptionHistory history, int key) =>
        Check(history.TryMarkHeard(key, history.PlaythroughRevision), "seeding the history");

    private const string OpeningJson = """
        [
          {"key": "opening.summon", "name": "Summoning", "revision": 2, "anchor": "banner", "cues": [
            {"atFrame": 12, "text": "Orbs circle."}]},
          {"key": "summon.knights", "name": "Knights of the Round", "revision": 2, "anchor": "summon", "cues": [
            {"atFrame": 15, "text": "Knights appear."}]}
        ]
        """;

    private const string OpeningManifest = """
        {"entries": [
          {"text": "Orbs circle.", "file": "orbs.ogg", "duration_seconds": 3.0},
          {"text": "Knights appear.", "file": "knights-appear.ogg", "duration_seconds": 1.5}
        ]}
        """;

    private static BattleAnimationObservation KnightsCast(int tick, bool banner = false, bool dispatcherWaiting = false) =>
        BattleAnimationObservation.Playing(2, 0, Summon, 0x0F, 0x000F) with
        {
            Tick = (byte)tick,
            BannerVisible = banner,
            BannerText = banner ? "Ultimate End" : null,
            SummonEffectWaiting = dispatcherWaiting,
        };

    private static BattleAnimationObservation IdleAt(int tick) =>
        BattleAnimationObservation.Idle with { Tick = (byte)tick };

    /// <summary>A Knights cast: banner at tick 7, dispatcher 50-65, the summon begins at 66.</summary>
    private static void CastUntilTheSummonBegins(Rig rig, double from)
    {
        rig.Update(KnightsCast(0), from);
        rig.Update(KnightsCast(7, banner: true), from + 0.5);
        rig.Update(KnightsCast(19, banner: true), from + 1.3);
        rig.Update(KnightsCast(50, dispatcherWaiting: true), from + 3.3);
        rig.Update(KnightsCast(66), from + 4.4);
    }

    /// <summary>The whole cast, each recording reaching its own end.</summary>
    private static void CastKnightsToTheEnd(Rig rig, double from)
    {
        var before = rig.Voice.Created.Count;
        CastUntilTheSummonBegins(rig, from);
        if (rig.Voice.Created.Count > before)
        {
            rig.Voice.Last.Finish();
        }

        rig.Update(KnightsCast(80), from + 5.3);
        rig.Update(KnightsCast(81), from + 5.4);
        if (rig.Voice.Created.Count > before)
        {
            rig.Voice.Last.Finish();
        }

        rig.Update(IdleAt(90), from + 6);
        rig.Update(IdleAt(100), from + 7);
    }

    private static void WithDirectory(Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "blind-soldier-battle-reset-" + Guid.NewGuid().ToString("N"));
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
        if (!value) throw new InvalidOperationException("Battle description reset: " + message);
    }

    /// <summary>A coordinator with fake recordings, speech and time.</summary>
    private sealed class Rig
    {
        public Rig(FieldAreaDescriptionHistory history)
            : this(new BattleAnimationNarrationCatalog(
                [
                    ("limit.cloud.braver", "Cloud: Braver", "Cloud leaps high."),
                    ("summon.bahamut_zero", "Bahamut ZERO", "A dragon rises into space."),
                ]),
                history,
                """
                {"entries": [
                  {"text": "Cloud leaps high.", "file": "braver.ogg", "duration_seconds": 6.0},
                  {"text": "A dragon rises into space.", "file": "bahamut-zero.ogg", "duration_seconds": 9.0}
                ]}
                """)
        {
        }

        public Rig(BattleAnimationNarrationCatalog catalog, FieldAreaDescriptionHistory history, string manifest)
        {
            History = history;
            Narration = new BattleAnimationNarrationCoordinator(
                catalog,
                history,
                CutsceneVoiceManifest.Parse(manifest),
                Voice.Create,
                _ => { },
                text =>
                {
                    Spoken.Add(text);
                    return true;
                },
                () => OtherDescriptionPlaying,
                1,
                () => false);
        }

        public FieldAreaDescriptionHistory History { get; }

        public BattleAnimationNarrationCoordinator Narration { get; }

        public Voices Voice { get; } = new();

        public List<string> Spoken { get; } = [];

        public bool OtherDescriptionPlaying { get; set; }

        public void Update(BattleAnimationObservation? observation, double seconds) =>
            Narration.Update(observation, T0.AddSeconds(seconds));

        public void PlayToEnd(BattleAnimationObservation observation, double seconds)
        {
            Update(observation, seconds);
            Update(BattleAnimationObservation.Idle, seconds + 1);
            Voice.Last.Finish();
            Update(BattleAnimationObservation.Idle, seconds + 10);
        }
    }

    /// <summary>Recording outputs: what was started, and each clip's own end.</summary>
    private sealed class Voices
    {
        public List<Clip> Created { get; } = [];

        public List<string> Started { get; } = [];

        public Clip Last => Created[^1];

        public IFieldMovieNarrationOutput Create(CutsceneVoiceClip clip)
        {
            var created = new Clip(this, clip.FileName);
            Created.Add(created);
            return created;
        }

        public sealed class Clip(Voices owner, string file)
            : IFieldMovieNarrationOutput, IFieldMovieNarrationCompletion, IFieldMovieNarrationPause
        {
            public string File { get; } = file;

            public bool IsPlaying { get; private set; }

            public bool CompletedNormally { get; private set; }

            public int Stops { get; private set; }

            public bool Start(string reason)
            {
                IsPlaying = true;
                CompletedNormally = false;
                owner.Started.Add(File);
                return true;
            }

            public bool Stop(string reason)
            {
                Stops++;
                IsPlaying = false;
                CompletedNormally = false;
                return true;
            }

            public bool SetPaused(bool paused) => IsPlaying;

            /// <summary>The clip reached its own end.</summary>
            public void Finish()
            {
                IsPlaying = false;
                CompletedNormally = true;
            }

            // Like the real player, releasing the device forgets how it ended.
            public void Dispose() => CompletedNormally = false;
        }
    }
}
