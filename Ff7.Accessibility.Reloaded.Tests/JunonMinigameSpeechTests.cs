using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using NAudio.Wave;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class JunonMinigameSpeechTests
{
    public static void Run()
    {
        ReadsTheNativeCprGaugeFromTheSharedTemporaryBank();
        ReadsBankSixFromTheSameNativeTemporaryStorage();
        ReadsTheSelectedSendOffPromptInsteadOfTheChangingRandomByte();
        RejectsATornTemporaryBankFrame();
        RejectsATornParadeInputAndSafetyFrame();
        IdentifiesTheNativeNowWindowByItsScriptOwner();
        CuesEachVisibleNowSequenceWithoutRepeatingHeldPrompts();
        DoesNotCueUnshownOrUnrelatedMinigameState();
        PreloadsTimingAudioOnAChannelThatContinuesBetweenCues();
        ReportsAudioFailureSoTheHostCanKeepThePromptAudible();
        RejectsUnreadableOrTornParadeWindowOwners();
        TargetsTheUnoccupiedLowerSectionOfTheMovingLine();
        UsesTheNativeClampedByteSubtractionForTheMovingFormationLine();
        SpeaksCprGaugeMilestonesWithoutPollingSpam();
        GuidesCloudIntoTheWelcomeParadeAndReadsTheRating();
        LeavesVisibleSendOffCommandsToTheFieldMessageReader();
        KeepsGaugeUpdatesBehindTimeCriticalNativePrompts();
    }

    private static void ReadsTheNativeCprGaugeFromTheSharedTemporaryBank()
    {
        var memory = new JunonMemory();
        memory.EnterField(JunonMinigameStateReader.CprFieldId);
        memory.WriteTemporary(20, 1);
        memory.WriteTemporary(21, 2);
        memory.WriteTemporary(22, 1);
        memory.WriteTemporary(23, 10);
        memory.WriteTemporary(24, 30);

        Equal(true, new JunonMinigameStateReader(memory).TryRead(out var snapshot), "CPR frame readable");
        Equal(JunonMinigameKind.Cpr, snapshot.Kind, "CPR kind");
        Equal(true, snapshot.Cpr.IsActive, "CPR active phase");
        Equal(true, snapshot.Cpr.IsBreathing, "CPR breathing bit");
        Equal(false, snapshot.Cpr.IsStopped, "CPR stopped bit");
        Equal((byte)10, snapshot.Cpr.Gauge, "CPR gauge");
        Equal((byte)30, snapshot.Cpr.TotalDelivered, "CPR delivered total");
        Equal((byte)2, snapshot.Cpr.OverfillCount, "CPR overfill counter");
    }

    private static void ReadsBankSixFromTheSameNativeTemporaryStorage()
    {
        var memory = new JunonMemory();
        memory.EnterField(JunonMinigameStateReader.SendOffFieldId);
        memory.WriteTemporaryUInt16(27, 20);
        memory.WriteTemporary(29, 70);
        memory.WriteSendOffPromptScript(8);

        Equal(true, new JunonMinigameStateReader(memory).TryRead(out var snapshot), "send-off frame readable");
        Equal(JunonMinigameKind.SendOff, snapshot.Kind, "send-off kind");
        Equal(true, snapshot.SendOff.IsActive, "send-off active sequence");
        Equal((ushort)20, snapshot.SendOff.Sequence, "send-off sequence from bank 6");
        Equal(JunonSendOffCommand.Menu, snapshot.SendOff.Command, "send-off command from random band");
        Equal((byte)70, snapshot.SendOff.MoodPercent, "numeric mood window value from bank 6");
    }

    private static void ReadsTheSelectedSendOffPromptInsteadOfTheChangingRandomByte()
    {
        var memory = new JunonMemory();
        memory.EnterField(JunonMinigameStateReader.SendOffFieldId);
        memory.WriteTemporaryUInt16(27, 20);
        memory.WriteTemporary(10, 0); // The random producer has already moved into the OK band.
        memory.WriteSendOffPromptScript(11); // The selected prompt remains Right Face.

        Equal(true, new JunonMinigameStateReader(memory).TryRead(out var snapshot), "send-off prompt frame readable");
        Equal(
            JunonSendOffCommand.RightFace,
            snapshot.SendOff.Command,
            "active captain script, not the changing random byte, identifies the visible prompt");
    }

    private static void RejectsATornTemporaryBankFrame()
    {
        var memory = new JunonMemory(tearTemporaryBank: true);
        memory.EnterField(JunonMinigameStateReader.CprFieldId);
        memory.WriteTemporary(20, 1);

        Equal(false, new JunonMinigameStateReader(memory).TryRead(out _), "torn Junon state rejected");
    }

    private static void RejectsATornParadeInputAndSafetyFrame()
    {
        var memory = new JunonMemory(tearHeldInput: true);
        memory.EnterWelcomeParade();

        Equal(
            false,
            new JunonMinigameStateReader(memory).TryRead(out _),
            "a direction/safety change across the coherent parade snapshot is rejected");
    }

    private static void TargetsTheUnoccupiedLowerSectionOfTheMovingLine()
    {
        var memory = new JunonMemory();
        memory.EnterWelcomeParade();
        memory.SetWelcomeParadePlayerPosition(1600, -300);
        memory.SetWelcomeParadeFormationAnchorPosition(1600, -619);

        Equal(true, new JunonMinigameStateReader(memory).TryRead(out var snapshot),
            "native parade approach readable");
        // junonr4 hei7/5 occupies the top endpoint at (anchor X, -427).
        // hei9/6 demonstrates the open lower approach at Y=-759. On the
        // native line (1600,-427)->(1568,-815), that entry is (1573,-759).
        Equal(1573, snapshot.Parade.FormationTargetX,
            "the entry follows the moving native line below the occupied ranks");
        Equal(-759, snapshot.Parade.FormationTargetY,
            "Cloud approaches the open lower rank instead of a solid top-rank soldier");

        memory.SetWelcomeParadePlayerPosition(1600, -850);
        Equal(true, new JunonMinigameStateReader(memory).TryRead(out snapshot),
            "native approach from below the segment readable");
        Equal(1573, snapshot.Parade.FormationTargetX,
            "an approach from below still aims inside the native trigger span");
        Equal(-759, snapshot.Parade.FormationTargetY,
            "the target does not settle beyond the lower endpoint where LINE cannot trigger");
    }

    private static void IdentifiesTheNativeNowWindowByItsScriptOwner()
    {
        var memory = new JunonMemory();
        memory.EnterWelcomeParade();
        memory.WriteTemporary(35, 1);
        memory.WriteTemporary(25, 7);
        memory.SetParadeMessageCount(2);
        memory.SetParadeWindowOwners(12, 255, 10, 255);
        var reader = new JunonMinigameStateReader(memory);
        Equal(true, reader.TryRead(out var snapshot), "native Now window ownership is readable");
        Equal(true, snapshot.Parade.IsNowPromptVisible,
            "border1-owned window zero with the ratings HUD is the native Now prompt");
        Equal((byte)7, snapshot.Parade.NowPromptSequence,
            "the prompt identity is the visible number from bank 5 index 25");

        foreach (var owners in new byte[][]
        {
            [7, 255, 10, 255], [18, 255, 10, 255], [12, 255, 9, 255],
            [12, 18, 10, 255], [12, 255, 10, 18], [255, 255, 10, 255]
        })
        {
            memory.SetParadeWindowOwners(owners[0], owners[1], owners[2], owners[3]);
            Equal(true, reader.TryRead(out snapshot), "other window ownership is readable");
            Equal(false, snapshot.Parade.IsNowPromptVisible,
                "ordinary, missing, or additional windows are not a movable Now prompt");
        }

        memory.SetParadeWindowOwners(12, 255, 10, 255);
        memory.WriteTemporary(35, 0);
        Equal(true, reader.TryRead(out snapshot), "pre-join state is readable");
        Equal(false, snapshot.Parade.IsNowPromptVisible, "the Now exception requires the native joined latch");
        memory.WriteTemporary(35, 1);
        foreach (var count in new byte[] { 1, 3 })
        {
            memory.SetParadeMessageCount(count);
            Equal(true, reader.TryRead(out snapshot), "a mismatched window count is readable");
            Equal(false, snapshot.Parade.IsNowPromptVisible, "the Now exception requires exactly its two windows");
        }
    }

    private static void CuesEachVisibleNowSequenceWithoutRepeatingHeldPrompts()
    {
        var tracker = new JunonTimingCueTracker();
        var prompt = Parade(true, 24, 0, 0, 0, 0);
        prompt = prompt with
        {
            Parade = prompt.Parade with { IsNowPromptVisible = true, NowPromptSequence = 7 }
        };
        Equal(true, tracker.Observe(prompt), "a visible native Now starts one timing cue");
        Equal(false, tracker.Observe(prompt), "a held Now cannot repeat the timing cue");
        Equal(false, tracker.Observe(prompt with
        {
            Parade = prompt.Parade with { HeldInput = 0x20, Rating = 27 }
        }), "pressing OK and changing the rating cannot replay or cancel a cue");
        prompt = prompt with { Parade = prompt.Parade with { NowPromptSequence = 8 } };
        Equal(true, tracker.Observe(prompt), "a new numbered Now cues even when no hidden frame was sampled");
        Equal(false, tracker.Observe(prompt with
        {
            Parade = prompt.Parade with { IsNowPromptVisible = false }
        }), "closing a prompt is silent");
        Equal(false, tracker.Observe(prompt), "a transient visibility gap does not repeat the same numbered Now");
        prompt = prompt with { Parade = prompt.Parade with { NowPromptSequence = 255 } };
        Equal(true, tracker.Observe(prompt), "a later native prompt cues");
        prompt = prompt with { Parade = prompt.Parade with { NowPromptSequence = 0 } };
        Equal(true, tracker.Observe(prompt), "native byte rollover is a new visible prompt");
        Equal(false, tracker.Observe(JunonMinigameSnapshot.Inactive), "leaving the minigame is silent");
        Equal(true, tracker.Observe(prompt), "reentering the minigame rearms its native sequence");
        tracker.Reset();
        Equal(true, tracker.Observe(prompt), "an explicit lifecycle reset rearms the cue");
    }

    private static void DoesNotCueUnshownOrUnrelatedMinigameState()
    {
        var tracker = new JunonTimingCueTracker();
        var prompt = Parade(true, 24, 0, 0, 0, 0);
        Equal(false, tracker.Observe(prompt), "the hidden parade counter is not an action cue");
        prompt = prompt with
        {
            Parade = prompt.Parade with { NowPromptSequence = 2, IsNowPromptVisible = true }
        };
        Equal(false, tracker.Observe(prompt with
        {
            Parade = prompt.Parade with { JoinedFormation = false }
        }), "no timing cue before the native joined latch");
        Equal(false, tracker.Observe(prompt with
        {
            Parade = prompt.Parade with { IsActive = false }
        }), "inactive parade data is not a timing prompt");
        Equal(false, tracker.Observe(prompt with
        {
            Parade = prompt.Parade with { MovieActive = 1 }
        }), "a movie frame cannot emit a stale timing cue");
        Equal(false, tracker.Observe(SendOff(17, JunonSendOffCommand.Ok, 0)),
            "send-off button identities remain spoken native commands");
        Equal(false, tracker.Observe(Cpr(10, breathing: true)), "CPR is not the Now cue");
        Equal(true, tracker.Observe(prompt), "hidden counter changes do not consume the next visible prompt");
    }

    private static void PreloadsTimingAudioOnAChannelThatContinuesBetweenCues()
    {
        var path = CreateTimingTestWave();
        try
        {
            var device = new CueOutput();
            using var player = new JunonTimingCuePlayer(path, 100, _ => { }, () => device);
            File.Delete(path);
            Equal(true, player.Play(), "preloaded timing audio survives the file disappearing after startup");
            Equal(true, device.ReadHasSound(), "the first cue produces audio samples on its dedicated channel");
            Equal(false, device.ReadHasSound(), "the channel continues with silence after the short cue");
            Equal(PlaybackState.Playing, device.PlaybackState,
                "finishing the cue does not stop the ready audio device");
            Equal(true, player.Play(), "another cue uses the ready channel without reopening its file");
            Equal(true, device.ReadHasSound(), "the next cue produces audio after idle samples");
            player.Dispose();
            Equal(false, player.Play(), "disposed audio reports failure instead of swallowing the prompt");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void ReportsAudioFailureSoTheHostCanKeepThePromptAudible()
    {
        var path = CreateTimingTestWave();
        try
        {
            var failureLog = new List<string>();
            using var unavailable = new JunonTimingCuePlayer(path, 100, failureLog.Add,
                () => throw new InvalidOperationException("audio device unavailable"));
            Equal(false, unavailable.Play(), "opening the audio device fails visibly to the host");
            Equal(true, failureLog.Any(line => line.Contains("audio device unavailable", StringComparison.Ordinal)),
                "the failure reason is recorded for diagnosis");
            using var muted = new JunonTimingCuePlayer(path, 0, _ => { }, () => new CueOutput());
            Equal(false, muted.Play(), "zero audio volume preserves the spoken fallback");
            using var missing = new JunonTimingCuePlayer(path + ".missing", 100, _ => { }, () => new CueOutput());
            Equal(false, missing.Play(), "a missing asset preserves the spoken fallback");
            var device = new CueOutput();
            using var stopped = new JunonTimingCuePlayer(path, 100, _ => { }, () => device);
            device.FailPlayback();
            Equal(false, stopped.Play(), "a device failure after startup is not reported as successful audio");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTimingTestWave()
    {
        var path = Path.Combine(Path.GetTempPath(), "blind-soldier-junon-cue-" + Guid.NewGuid() + ".wav");
        using var writer = new WaveFileWriter(path, new WaveFormat(8000, 16, 1));
        for (var index = 0; index < 8; index++)
        {
            writer.WriteSample(0.25f);
        }
        return path;
    }

    private sealed class CueOutput : IWavePlayer
    {
        private IWaveProvider? provider;
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public PlaybackState PlaybackState { get; private set; }
        public float Volume { get; set; } = 1;
        public WaveFormat OutputWaveFormat => provider!.WaveFormat;
        public void Init(IWaveProvider waveProvider) => provider = waveProvider;
        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() => PlaybackState = PlaybackState.Stopped;
        public void Dispose() => Stop();
        public void FailPlayback()
        {
            Stop();
            PlaybackStopped?.Invoke(this, new StoppedEventArgs(new InvalidOperationException("device lost")));
        }
        public bool ReadHasSound()
        {
            var bytes = new byte[256];
            Equal(bytes.Length, provider!.Read(bytes, 0, bytes.Length), "a ready channel supplies sound or silence continuously");
            return bytes.Any(value => value != 0);
        }
    }

    private static void RejectsUnreadableOrTornParadeWindowOwners()
    {
        var unreadable = new JunonMemory();
        unreadable.EnterWelcomeParade();
        unreadable.WriteTemporary(35, 1);
        unreadable.SetParadeMessageCount(2);
        Equal(false, new JunonMinigameStateReader(unreadable).TryRead(out _),
            "unreadable active window owners cannot permit movement");

        var torn = new JunonMemory(tearWindowOwners: true);
        torn.EnterWelcomeParade();
        torn.WriteTemporary(35, 1);
        torn.SetParadeMessageCount(2);
        torn.SetParadeWindowOwners(12, 255, 10, 255);
        Equal(false, new JunonMinigameStateReader(torn).TryRead(out _),
            "a window owner change across the parade snapshot is rejected");
    }

    private static void UsesTheNativeClampedByteSubtractionForTheMovingFormationLine()
    {
        var memory = new JunonMemory();
        memory.EnterWelcomeParade();
        memory.SetWelcomeParadePlayerPosition(1536, -815);
        memory.SetWelcomeParadeFormationAnchorPosition(1536, -427);

        Equal(
            true,
            new JunonMinigameStateReader(memory).TryRead(out var snapshot),
            "welcome-parade formation frame readable");
        Equal(
            1536,
            snapshot.Parade.FormationTargetX,
            "MINUS! clamps the low byte at zero instead of borrowing into the high byte");
        Equal(-759, snapshot.Parade.FormationTargetY, "open interior of the moving formation line");
    }

    private static void SpeaksCprGaugeMilestonesWithoutPollingSpam()
    {
        var tracker = new JunonMinigameSpeechTracker();

        Equal(
            "CPR gauge ready. Press Switch to start breathing.",
            One(tracker.Observe(Cpr(gauge: 0))),
            "CPR opening instruction");
        Equal(0, tracker.Observe(Cpr(gauge: 0)).Count, "unchanged CPR frame is silent");
        Equal("Breathing.", One(tracker.Observe(Cpr(gauge: 1, breathing: true))), "CPR starts");
        Equal("Full.", One(tracker.Observe(Cpr(gauge: 10, breathing: true))), "full breath gauge");
        Equal(
            "Overfilled. Start again.",
            One(tracker.Observe(Cpr(gauge: 0, breathing: true, overfills: 1))),
            "overfilled breath gauge");
        Equal(
            "Breath delivered. Total 10 of 41.",
            One(tracker.Observe(Cpr(gauge: 0, stopped: true, total: 10, overfills: 1))),
            "delivered breath total");
        Equal(
            "CPR complete. Total 41 of 41.",
            One(tracker.Observe(Cpr(gauge: 0, stopped: true, total: 41, overfills: 1))),
            "CPR completion");
    }

    private static void GuidesCloudIntoTheWelcomeParadeAndReadsTheRating()
    {
        var tracker = new JunonMinigameSpeechTracker();
        var approaching = Parade(
            joined: false,
            rating: 24,
            playerX: 200,
            playerY: 0,
            targetX: 200,
            targetY: -160);

        Equal(
            "Welcome parade. Move into the open place up 2. Live TV rating, 24 percent.",
            One(tracker.Observe(approaching)),
            "welcome parade opening guidance");
        Equal(0, tracker.Observe(approaching).Count, "stable formation guidance is silent");
        Equal(
            "In formation. Match the soldiers and press OK when you hear Now or the timing tone.",
            One(tracker.Observe(approaching with
            {
                Parade = approaching.Parade with { JoinedFormation = true }
            })),
            "formation joined");
        Equal(
            0,
            tracker.Observe(approaching with
            {
                Parade = approaching.Parade with { JoinedFormation = true, Rating = 26 }
            }).Count,
            "two-point rating jitter is not chatter");
        Equal(
            "Live TV rating, 27 percent.",
            One(tracker.Observe(approaching with
            {
                Parade = approaching.Parade with { JoinedFormation = true, Rating = 27 }
            }), expectedInterrupt: false),
            "meaningful rating change");
        Equal(
            "Final live TV rating, 43 percent.",
            One(tracker.Observe(approaching with
            {
                Parade = approaching.Parade with
                {
                    JoinedFormation = true,
                    Rating = 43,
                    FinalRating = 43
                }
            }), expectedInterrupt: false),
            "latched final rating");
    }

    private static void LeavesVisibleSendOffCommandsToTheFieldMessageReader()
    {
        var tracker = new JunonMinigameSpeechTracker();

        Equal(
            "Junon send-off. Match each command. President's mood, 0 percent.",
            One(tracker.Observe(SendOff(16, JunonSendOffCommand.Ok, 0))),
            "send-off opening guidance does not duplicate the visible OK window");
        Equal(0, tracker.Observe(SendOff(16, JunonSendOffCommand.Ok, 0)).Count, "same send-off frame is silent");
        Equal(
            0,
            tracker.Observe(SendOff(17, JunonSendOffCommand.Ok, 0)).Count,
            "the next visible OK window is left to the ordinary field-message reader");
        Equal(
            "President's mood, 10 percent.",
            One(tracker.Observe(SendOff(18, JunonSendOffCommand.Menu, 10)), expectedInterrupt: false),
            "mood remains available without duplicating the visible MENU window");
        Equal(
            0,
            tracker.Observe(SendOff(19, JunonSendOffCommand.Switch, 10)).Count,
            "the visible SWITCH window is not duplicated");
        Equal(0, tracker.Observe(SendOff(20, JunonSendOffCommand.Cancel, 10)).Count, "the visible CANCEL window is not duplicated");
        Equal(0, tracker.Observe(SendOff(21, JunonSendOffCommand.LeftFace, 10)).Count, "the visible left-face window is not duplicated");
        Equal(0, tracker.Observe(SendOff(22, JunonSendOffCommand.RightFace, 10)).Count, "the visible right-face window is not duplicated");
        Equal(
            "Special pose.",
            One(tracker.Observe(SendOff(48, JunonSendOffCommand.None, 10, special: true))),
            "final special pose");
    }

    private static void KeepsGaugeUpdatesBehindTimeCriticalNativePrompts()
    {
        var parade = new JunonMinigameSpeechTracker();
        _ = parade.Observe(Parade(true, 24, 0, 0, 0, 0));
        Equal(
            "Live TV rating, 27 percent.",
            One(parade.Observe(Parade(true, 27, 0, 0, 0, 0)), expectedInterrupt: false),
            "rating update must not interrupt the native Now prompt");

        var sendOff = new JunonMinigameSpeechTracker();
        _ = sendOff.Observe(SendOff(16, JunonSendOffCommand.Ok, 0));
        Equal(
            "President's mood, 10 percent.",
            One(sendOff.Observe(SendOff(17, JunonSendOffCommand.Menu, 10)), expectedInterrupt: false),
            "mood update must not interrupt the native send-off command window");
    }

    private static JunonMinigameSnapshot Cpr(
        byte gauge,
        bool breathing = false,
        bool stopped = false,
        byte total = 0,
        byte overfills = 0) =>
        JunonMinigameSnapshot.FromCpr(new JunonCprState(
            IsActive: true,
            IsBreathing: breathing,
            IsStopped: stopped,
            Gauge: gauge,
            TotalDelivered: total,
            OverfillCount: overfills));

    private static JunonMinigameSnapshot Parade(
        bool joined,
        byte rating,
        int playerX,
        int playerY,
        int targetX,
        int targetY) =>
        JunonMinigameSnapshot.FromWelcomeParade(new JunonWelcomeParadeState(
            IsActive: true,
            JoinedFormation: joined,
            Rating: rating,
            FinalRating: 0,
            HasFormationTarget: true,
            PlayerX: playerX,
            PlayerY: playerY,
            FormationTargetX: targetX,
            FormationTargetY: targetY,
            ControlTransform: new FieldNavigationControlTransform(0),
            HeldInput: 0,
            UserControl: 0,
            ActiveMessageCount: 0,
            MovieActive: 0));

    private static JunonMinigameSnapshot SendOff(
        ushort sequence,
        JunonSendOffCommand command,
        byte mood,
        bool special = false) =>
        JunonMinigameSnapshot.FromSendOff(new JunonSendOffState(
            IsActive: true,
            Sequence: sequence,
            Command: command,
            MoodPercent: mood,
            IsSpecialPose: special));

    private static string One(
        IReadOnlyList<JunonMinigameSpeechCue> cues,
        bool expectedInterrupt = true)
    {
        Equal(1, cues.Count, "one Junon cue");
        Equal(expectedInterrupt, cues[0].Interrupt, "Junon cue interruption policy");
        return cues[0].Text;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private sealed class JunonMemory(
        bool tearTemporaryBank = false,
        bool tearHeldInput = false,
        bool tearWindowOwners = false) : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];
        private int temporaryReads;
        private int heldInputReads;
        private int windowOwnerReads;

        public void SetParadeMessageCount(byte count) =>
            WriteByte(FieldAudibleCueStateReader.AddressActiveFieldMessageCount, count);

        public void SetParadeWindowOwners(byte window0, byte window1, byte window2, byte window3)
        {
            WriteByte(FieldMessageReader.AddressFieldWindowStates, window0);
            WriteByte(FieldMessageReader.AddressFieldWindowStates + 1, window1);
            WriteByte(FieldMessageReader.AddressFieldWindowStates + 2, window2);
            WriteByte(FieldMessageReader.AddressFieldWindowStates + 3, window3);
        }

        public void EnterField(ushort fieldId)
        {
            WriteByte(JunonMinigameStateReader.AddressCurrentModule, JunonMinigameStateReader.FieldModule);
            WriteUInt16(JunonMinigameStateReader.AddressCurrentFieldId, fieldId);
            for (var index = 0; index < JunonMinigameStateReader.TemporaryBankCaptureLength; index++)
            {
                WriteTemporary(index, 0);
            }
        }

        public void WriteTemporary(int offset, byte value) =>
            WriteByte(JunonMinigameStateReader.AddressTemporaryFieldBank + offset, value);

        public void WriteTemporaryUInt16(int offset, ushort value)
        {
            WriteTemporary(offset, (byte)(value & 0xFF));
            WriteTemporary(offset + 1, (byte)(value >> 8));
        }

        public void EnterWelcomeParade()
        {
            EnterField(JunonMinigameStateReader.WelcomeParadeFieldId);
            WriteTemporary(28, 1);

            const uint modelTable = 0x00100000;
            const uint triggerHeader = 0x00200000;
            const uint eventTable = 0x00300000;
            const byte cloudModel = 0;
            const byte formationModel = 1;
            WriteUInt16(FieldPositionReader.AddressFieldCurrentModelId, cloudModel);
            WriteByte(FieldPositionReader.AddressFieldNumModels, 2);
            WriteUInt32(FieldPositionReader.AddressFieldModelsPtr, modelTable);
            WriteInt32(
                FieldPositionReader.AddressFieldModelsObjs +
                    cloudModel * FieldPositionReader.FieldObjectStride +
                    FieldPositionReader.ObjectXOffset,
                1536 << 12);
            WriteInt32(
                FieldPositionReader.AddressFieldModelsObjs +
                    cloudModel * FieldPositionReader.FieldObjectStride +
                    FieldPositionReader.ObjectYOffset,
                -300 << 12);
            WriteInt32(
                FieldPositionReader.AddressFieldModelsObjs +
                    cloudModel * FieldPositionReader.FieldObjectStride +
                    FieldPositionReader.ObjectZOffset,
                0);
            WriteUInt16(
                FieldPositionReader.AddressFieldModelsObjs +
                    cloudModel * FieldPositionReader.FieldObjectStride +
                    FieldPositionReader.ObjectTriangleOffset,
                0);
            WriteByte(
                (int)modelTable +
                    cloudModel * FieldPositionReader.FieldModelStride +
                    FieldPositionReader.ModelDirectionOffset,
                0);

            WriteUInt32(FieldNavigationControlReader.AddressFieldTriggersPtr, triggerHeader);
            WriteByte((int)triggerHeader + FieldNavigationControlReader.ControlDirectionOffset, 0);
            WriteByte(
                FieldNavigationObjectReader.AddressFieldModelIdArray + 27,
                formationModel);
            WriteUInt32(FieldNavigationObjectReader.AddressFieldEventDataPtr, eventTable);
            WriteInt32(
                (int)eventTable +
                    formationModel * FieldNavigationObjectReader.FieldEventDataStride +
                    FieldNavigationObjectReader.PositionXOffset,
                1536 << 12);
            WriteInt32(
                (int)eventTable +
                    formationModel * FieldNavigationObjectReader.FieldEventDataStride +
                    FieldNavigationObjectReader.PositionYOffset,
                -427 << 12);

            WriteUInt32(FieldNavigationInputReader.AddressCurrentKeyInput, 0);
            WriteByte(FieldAudibleCueStateReader.AddressUserControl, 0);
            WriteByte(FieldAudibleCueStateReader.AddressActiveFieldMessageCount, 0);
            WriteUInt16(FieldAudibleCueStateReader.AddressFieldMovieActive, 0);
        }

        public void SetWelcomeParadePlayerPosition(int x, int y)
        {
            const byte cloudModel = 0;
            WriteInt32(
                FieldPositionReader.AddressFieldModelsObjs +
                    cloudModel * FieldPositionReader.FieldObjectStride +
                    FieldPositionReader.ObjectXOffset,
                x << 12);
            WriteInt32(
                FieldPositionReader.AddressFieldModelsObjs +
                    cloudModel * FieldPositionReader.FieldObjectStride +
                    FieldPositionReader.ObjectYOffset,
                y << 12);
        }

        public void SetWelcomeParadeFormationAnchorPosition(int x, int y)
        {
            const uint eventTable = 0x00300000;
            const byte formationModel = 1;
            WriteInt32(
                (int)eventTable +
                    formationModel * FieldNavigationObjectReader.FieldEventDataStride +
                    FieldNavigationObjectReader.PositionXOffset,
                x << 12);
            WriteInt32(
                (int)eventTable +
                    formationModel * FieldNavigationObjectReader.FieldEventDataStride +
                    FieldNavigationObjectReader.PositionYOffset,
                y << 12);
        }

        public void WriteSendOffPromptScript(byte scriptId)
        {
            WriteByte(
                JunonMinigameStateReader.AddressCurrentEntityScriptPriority +
                    JunonMinigameStateReader.SendOffCaptainEntityId,
                JunonMinigameStateReader.SendOffPromptPriority);
            WriteByte(
                JunonMinigameStateReader.AddressCurrentEntityScriptId +
                    JunonMinigameStateReader.SendOffCaptainEntityId *
                    JunonMinigameStateReader.ScriptSlotsPerEntity +
                    JunonMinigameStateReader.SendOffPromptPriority,
                scriptId);
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(virtualAddress + (uint)index, out destination[index]))
                {
                    destination.Clear();
                    return false;
                }
            }

            if (tearTemporaryBank &&
                virtualAddress == JunonMinigameStateReader.AddressTemporaryFieldBank &&
                ++temporaryReads == 2)
            {
                destination[0]++;
            }

            if (tearHeldInput &&
                virtualAddress == FieldNavigationInputReader.AddressCurrentKeyInput &&
                ++heldInputReads == 2)
            {
                destination[0] ^= 0x10;
            }

            if (tearWindowOwners &&
                virtualAddress == FieldMessageReader.AddressFieldWindowStates &&
                ++windowOwnerReads == 2)
            {
                destination[0] = 18;
            }

            return true;
        }

        private void WriteByte(int address, byte value) => bytes[(uint)address] = value;

        private void WriteUInt16(int address, ushort value)
        {
            WriteByte(address, (byte)(value & 0xFF));
            WriteByte(address + 1, (byte)(value >> 8));
        }

        private void WriteUInt32(int address, uint value)
        {
            for (var index = 0; index < sizeof(uint); index++)
            {
                WriteByte(address + index, (byte)(value >> (index * 8)));
            }
        }

        private void WriteInt32(int address, int value) =>
            WriteUInt32(address, unchecked((uint)value));
    }
}
