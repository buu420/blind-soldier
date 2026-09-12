using System.Diagnostics;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The opening narration's start, and the asynchronous lifetime the warm-up introduces.
///
/// <para>What these are about. The track used to be anchored to the movie file being
/// opened, which happens before the engine raises its own film flag and so before the
/// picture; and the Vorbis file and the output device were opened inside the start itself,
/// costing a measured 81 to 105 ms that then sat in front of the narration for the length
/// of the film because the track always began at its own second zero. Both are real and
/// both are fixed here. Neither is a reproduction of what the player heard.</para>
///
/// <para>The second half is adversarial: a warm-up that can be cancelled, superseded, fail
/// late, or land after a disposal is a state machine, and the cases below are the ones that
/// would otherwise steal a later film's device or cancel its start.</para>
/// </summary>
internal static class OpeningMovieStartupTests
{
    public static void Run()
    {
        // The anchor.
        TheEnginesOwnFlagStartsTheTrackAndTheFileHandleOnlyArmsIt();
        AnUnreadableFlagStillStartsTheTrackWithinTheGrace();
        TheNativeFlagIsVisibleEvenWhileTheHandleIsOpen();
        AStoppedNativeFilmEndsTheTrackOnceTheHandleIsNoLongerPolled();
        TheHandleQueryIsOnlySkippedWhileTheFlagIsReadable();

        // The engine's own clock.
        AProgressingFrameCounterAnchorsTheStart();
        AnAmbiguousOrImplausibleFrameCounterIsRefused();

        // The two start shapes.
        AnOrdinaryStartStaysSynchronousAndFailsClosed();
        AnOrdinaryStartDoesNotSkipItsOwnOpeningWords();
        AWarmDeviceStartsWithoutCompensation();
        ALateDeviceSkipsExactlyTheTimeItCost();
        TheCompensationIsCapped();
        AStartPastTheEndIsRefused();

        // The lifecycle.
        StoppingDuringPreparationCancelsTheStart();
        StoppingWhileTheDeviceOpensReleasesIt();
        // These two come before the generation races below, which exercise the same
        // machinery through a stop during an open and would otherwise be the first thing
        // to fail for an unrelated reason.
        StoppingWhilePlayBeginsLeavesNothingClaimed();
        DisposeIsFinal();
        AFailedOldPreparationDoesNotCancelANewerStart();
        ALateDeviceCannotBeAdoptedByALaterFilm();
        AWaitingStartIsNotDoubled();
        RestartingAfterAStopPreparesAgain();
        NaturalEndOfTrackReleasesTheDevice();

        MeasureRealStartupCost();
    }

    // --- the anchor -------------------------------------------------------------------

    private static void TheEnginesOwnFlagStartsTheTrackAndTheFileHandleOnlyArmsIt()
    {
        var armed = OpeningMovieActivityPolicy.ResolveStart(
            fileHandleActive: true,
            nativeStateReadable: true,
            nativeModule: FieldPositionReader.FieldModule,
            nativeFieldId: DeferredZoneSpeechTracker.OpeningFieldId,
            nativeMovieActive: 0,
            millisecondsSinceArmed: null);
        Equal(OpeningMovieStartAction.Arm, armed.Action, "an open file only arms the device");

        var waiting = OpeningMovieActivityPolicy.ResolveStart(
            true, true, FieldPositionReader.FieldModule,
            DeferredZoneSpeechTracker.OpeningFieldId, 0,
            millisecondsSinceArmed: OpeningMovieActivityPolicy.NativeStartGraceMs - 1);
        Equal(OpeningMovieStartAction.Arm, waiting.Action, "and keeps waiting inside the grace");

        var started = OpeningMovieActivityPolicy.ResolveStart(
            true, true, FieldPositionReader.FieldModule,
            DeferredZoneSpeechTracker.OpeningFieldId, 1,
            millisecondsSinceArmed: 250);
        Equal(OpeningMovieStartAction.Start, started.Action, "the engine's flag starts it");
        Equal(
            OpeningMovieActivitySignal.NativeFieldMovieState,
            started.Signal,
            "on the native signal, not the handle");

        Equal(
            OpeningMovieStartAction.Idle,
            OpeningMovieActivityPolicy.ResolveStart(
                false, true, FieldPositionReader.FieldModule, 109, 1, null).Action,
            "another field's film is not the opening");
        Equal(
            OpeningMovieStartAction.Idle,
            OpeningMovieActivityPolicy.ResolveStart(false, false, 0, 0, 0, null).Action,
            "and nothing at all is idle");
    }

    private static void AnUnreadableFlagStillStartsTheTrackWithinTheGrace()
    {
        var gate = OpeningMovieActivityPolicy.ResolveStart(
            fileHandleActive: true,
            nativeStateReadable: false,
            nativeModule: 0,
            nativeFieldId: 0,
            nativeMovieActive: 0,
            millisecondsSinceArmed: OpeningMovieActivityPolicy.NativeStartGraceMs);
        Equal(OpeningMovieStartAction.Start, gate.Action, "the handle starts it once the grace expires");
        Equal(OpeningMovieActivitySignal.FileHandle, gate.Signal, "and says it was the handle");
        Equal(
            true,
            gate.Anchor.Contains("file handle", StringComparison.Ordinal),
            $"and names the anchor in the log ({gate.Anchor})");
    }

    /// <summary>
    /// The normal case, and the trap. While the film plays, the handle is open and the
    /// engine's flag is raised, and <c>Resolve</c> reports only the handle - so anything
    /// that learned "the flag is up" from its signal never learned it at all, and the
    /// Restart Manager kept being queried for the whole film.
    /// </summary>
    private static void TheNativeFlagIsVisibleEvenWhileTheHandleIsOpen()
    {
        var bothPresent = OpeningMovieActivityPolicy.Resolve(
            fileHandleActive: true,
            nativeStateReadable: true,
            nativeModule: FieldPositionReader.FieldModule,
            nativeFieldId: DeferredZoneSpeechTracker.OpeningFieldId,
            nativeMovieActive: 1);
        Equal(true, bothPresent.IsActive, "both signals present is active");
        Equal(
            OpeningMovieActivitySignal.FileHandle,
            bothPresent.Signal,
            "and Resolve reports the handle, which is why its signal cannot be used to "
                + "detect the native flag");

        Equal(
            true,
            OpeningMovieActivityPolicy.IsNativeOpeningFilmActive(
                true, FieldPositionReader.FieldModule,
                DeferredZoneSpeechTracker.OpeningFieldId, 1),
            "asked directly, the native flag is visible with the handle still open");
        Equal(
            false,
            OpeningMovieActivityPolicy.IsNativeOpeningFilmActive(
                true, FieldPositionReader.FieldModule,
                DeferredZoneSpeechTracker.OpeningFieldId, 0),
            "and is not claimed before the engine raises it");
        Equal(
            false,
            OpeningMovieActivityPolicy.IsNativeOpeningFilmActive(
                false, FieldPositionReader.FieldModule,
                DeferredZoneSpeechTracker.OpeningFieldId, 1),
            "nor when the read itself failed");
        Equal(
            false,
            OpeningMovieActivityPolicy.IsNativeOpeningFilmActive(
                true, FieldPositionReader.FieldModule, 109, 1),
            "nor for a film in another field");
    }

    /// <summary>
    /// The end of the film, and why the two halves are coupled. Once the handle is no
    /// longer polled the flag alone ends the film; if the handle were still consulted, a
    /// lingering open file would keep the narration running past the picture.
    /// </summary>
    private static void AStoppedNativeFilmEndsTheTrackOnceTheHandleIsNoLongerPolled()
    {
        var suppressed = OpeningMovieActivityPolicy.Resolve(
            fileHandleActive: false,
            nativeStateReadable: true,
            nativeModule: FieldPositionReader.FieldModule,
            nativeFieldId: DeferredZoneSpeechTracker.OpeningFieldId,
            nativeMovieActive: 0);
        Equal(false, suppressed.IsActive, "the flag dropping ends the film");

        var lingering = OpeningMovieActivityPolicy.Resolve(
            fileHandleActive: true,
            nativeStateReadable: true,
            nativeModule: FieldPositionReader.FieldModule,
            nativeFieldId: DeferredZoneSpeechTracker.OpeningFieldId,
            nativeMovieActive: 0);
        Equal(
            true,
            lingering.IsActive,
            "while a handle that is still open would hold it open, which is what the "
                + "suppression exists to avoid");

        var stillRunning = OpeningMovieActivityPolicy.Resolve(
            false, true, FieldPositionReader.FieldModule,
            DeferredZoneSpeechTracker.OpeningFieldId, 1);
        Equal(true, stillRunning.IsActive, "and a raised flag keeps it running with no handle at all");
    }

    /// <summary>
    /// The Restart Manager query is the only blocking system call in the monitor loop and
    /// can stop once the engine's flag has proved itself - but only while that flag is
    /// readable, because a read that starts failing must fall back to the handle on the
    /// same tick rather than let the film look as though it had ended.
    /// </summary>
    private static void TheHandleQueryIsOnlySkippedWhileTheFlagIsReadable()
    {
        Equal(
            true,
            OpeningMovieActivityPolicy.ShouldSkipFileHandleQuery(
                movieDetected: true, nativeFlagSeen: true, nativeStateReadable: true),
            "a detected film whose flag has been seen needs no handle query");
        Equal(
            false,
            OpeningMovieActivityPolicy.ShouldSkipFileHandleQuery(true, true, false),
            "but an unreadable flag falls back to the handle on the same tick");
        Equal(
            false,
            OpeningMovieActivityPolicy.ShouldSkipFileHandleQuery(true, false, true),
            "and a flag that has never been seen is no reason to stop asking");
        Equal(
            false,
            OpeningMovieActivityPolicy.ShouldSkipFileHandleQuery(false, true, true),
            "nor is a film that has not been detected yet");
    }

    // --- the engine's own clock -------------------------------------------------------

    private static void AProgressingFrameCounterAnchorsTheStart()
    {
        Equal(
            true,
            OpeningMovieActivityPolicy.TryResolveFrameOffset(
                nativeFilmActive: true,
                firstFrame: 18,
                secondFrame: 20,
                out var offset,
                out var anchor),
            "a progressing counter anchors the start");
        Equal(
            TimeSpan.FromSeconds(20 / 15.0),
            offset,
            "at the film's own 15 frames per second");
        Equal("native movie frame 20", anchor, "and says which frame");

        Equal(
            true,
            OpeningMovieActivityPolicy.TryResolveFrameOffset(
                true, 1760, 1760, out var late, out _),
            "a steady counter late in the film is still this film");
        Equal(
            TimeSpan.FromSeconds(1760 / 15.0),
            late,
            "and 1760 is the frame the engine's own opening-field comparison uses");
    }

    private static void AnAmbiguousOrImplausibleFrameCounterIsRefused()
    {
        foreach (var (name, active, first, second) in new (string, bool, int, int)[]
                 {
                     ("a counter still at zero", true, 0, 0),
                     ("a counter that went backwards", true, 40, 12),
                     ("a counter beyond this film", true, 0, 4000),
                     ("a negative reading", true, -1, 5),
                     ("a film the engine does not claim", false, 10, 20),
                 })
        {
            Equal(
                false,
                OpeningMovieActivityPolicy.TryResolveFrameOffset(
                    active, first, second, out var offset, out var anchor),
                $"{name} is refused");
            Equal(TimeSpan.Zero, offset, $"{name} leaves the offset at zero");
            Equal(string.Empty, anchor, $"{name} claims no anchor");
        }

        // The address this reads is the engine's own, already used by the film cue reader.
        Equal(
            0x00CC0E10,
            FieldAudibleCueStateReader.AddressFieldMovieFrame,
            "the frame counter is the evidenced one");
    }

    // --- the two start shapes ---------------------------------------------------------

    /// <summary>
    /// Short character-action descriptions call <c>Start</c> and speak their text when it
    /// returns false. It must therefore still be true that a false means nothing is
    /// playing, including when the device cannot be opened at all.
    /// </summary>
    private static void AnOrdinaryStartStaysSynchronousAndFailsClosed()
    {
        var host = new Host();
        Equal(true, host.Player.Start("character action"), "a cold ordinary start plays by the time it returns");
        Equal(1, host.Devices.Count, "opening the device synchronously");
        Equal(true, host.Devices[0].Playing, "and playing it");
        Equal(true, host.Player.IsPlaying, "so the caller does not speak the text");

        var broken = new Host { FailToOpen = true };
        Equal(
            false,
            broken.Player.Start("character action"),
            "a device that cannot be opened returns false, so the caller speaks the text instead");
        Equal(false, broken.Player.IsPlaying, "and nothing is claimed");
        Equal(
            true,
            broken.Log.Any(line => line.Contains("failed to start", StringComparison.Ordinal)),
            "and the failure is logged");

        // Nothing is left pending either: a second attempt is free to try again.
        Equal(
            false,
            broken.Player.Start("character action retry"),
            "a retry against the same broken device fails the same way");
    }

    private static void AnOrdinaryStartDoesNotSkipItsOwnOpeningWords()
    {
        var host = new Host();
        host.OpenCostMs = 400;
        Equal(true, host.Player.Start("character action"), "it starts");
        Equal(
            TimeSpan.Zero,
            host.Devices[0].SeekedTo,
            "and begins at the first word however long the device took, because a short "
                + "clip has no timeline to catch up with");
    }

    private static void AWarmDeviceStartsWithoutCompensation()
    {
        var host = new Host();
        Equal(true, host.Player.Prepare("armed"), "preparing is accepted");
        host.Advance(420);
        host.RunScheduled();
        Equal(true, host.Player.IsPrepared, "the device is warm before the film starts");

        host.Advance(10);
        Equal(
            true,
            host.Player.StartTimed("film started", TimeSpan.Zero, "native film flag"),
            "it starts");
        Equal(1, host.Devices.Count, "on the device that was already open");
        Equal(TimeSpan.Zero, host.Devices[0].SeekedTo, "a warm device starts at the top");
        Equal(true, host.Devices[0].Playing, "and plays");
        Equal(
            true,
            host.Log.Any(line => line.Contains("anchor=native film flag", StringComparison.Ordinal)),
            "the anchor is logged");
    }

    private static void ALateDeviceSkipsExactlyTheTimeItCost()
    {
        var host = new Host();
        Equal(
            true,
            host.Player.StartTimed("film started", TimeSpan.Zero, "native film flag"),
            "the timed start is accepted");
        Equal(0, host.Devices.Count, "nothing is open yet");
        Equal(true, host.Player.IsPlaying, "but the track already owns the film");

        host.Advance(350);
        host.RunScheduled();
        Equal(1, host.Devices.Count, "the device lands");
        Equal(
            TimeSpan.FromMilliseconds(350),
            host.Devices[0].SeekedTo,
            "and the track skips exactly the 350 ms it cost");
        Equal(true, host.Devices[0].Playing, "then plays");
        Equal(
            true,
            host.Log.Any(line => line.Contains("start lag=350 ms", StringComparison.Ordinal)),
            "and the lag is logged once");

        // A frame offset and a late device add up.
        var both = new Host();
        both.Player.StartTimed("film started", TimeSpan.FromSeconds(2), "native movie frame 30");
        both.Advance(120);
        both.RunScheduled();
        Equal(
            TimeSpan.FromSeconds(2) + TimeSpan.FromMilliseconds(120),
            both.Devices[0].SeekedTo,
            "the frame offset and the lost time are both applied");
    }

    private static void TheCompensationIsCapped()
    {
        var host = new Host();
        host.Player.StartTimed("film started", TimeSpan.Zero, "native film flag");
        host.Advance(OpeningMovieAudioTrackPlayer.MaximumStartCompensationMs + 4000);
        host.RunScheduled();
        Equal(
            TimeSpan.FromMilliseconds(OpeningMovieAudioTrackPlayer.MaximumStartCompensationMs),
            host.Devices[0].SeekedTo,
            "a pathological delay does not jump arbitrarily far into the description");
    }

    private static void AStartPastTheEndIsRefused()
    {
        var warm = new Host();
        warm.Player.Prepare("armed");
        warm.RunScheduled();
        Equal(
            false,
            warm.Player.StartTimed("film resumed", TimeSpan.FromSeconds(119.5), "native movie frame 1793"),
            "a warm device refuses a start past the end of the track");
        Equal(false, warm.Devices[0].Playing, "and does not play");
        Equal(false, warm.Player.IsPlaying, "so the caller may speak instead");

        var cold = new Host();
        Equal(
            true,
            cold.Player.StartTimed("film resumed", TimeSpan.FromSeconds(119.5), "native movie frame 1793"),
            "a cold start is accepted because the track length is not known yet");
        cold.RunScheduled();
        Equal(1, cold.Devices.Count, "the device was opened to find that out");
        Equal(false, cold.Devices[0].Playing, "it never played");
        Equal(true, cold.Devices[0].Disposed, "and was released");
        Equal(false, cold.Player.IsPlaying, "leaving the film unclaimed");
    }

    // --- the lifecycle ----------------------------------------------------------------

    private static void StoppingDuringPreparationCancelsTheStart()
    {
        var host = new Host();
        host.Player.StartTimed("film started", TimeSpan.Zero, null);
        Equal(true, host.Player.IsPlaying, "the start is pending");
        Equal(true, host.Player.Stop("movie skipped"), "stopping a pending start is reported");
        Equal(false, host.Player.IsPlaying, "and clears it");

        host.Advance(300);
        host.RunScheduled();
        Equal(0, host.Devices.Count, "and no device is opened at all once it is cancelled");
        Equal(
            true,
            host.Log.Any(line => line.Contains("cancelled before it started", StringComparison.Ordinal)),
            "which is said once");
    }

    private static void StoppingWhileTheDeviceOpensReleasesIt()
    {
        // The stop lands while the device is actually opening, so the open cannot be
        // skipped and the finished device has to be released instead.
        var host = new Host();
        host.OnOpening = () => host.Player.Stop("movie skipped while the device opened");
        host.Player.StartTimed("film started", TimeSpan.Zero, null);
        host.Advance(300);
        host.RunScheduled();
        Equal(1, host.Devices.Count, "the orphaned preparation completed");
        Equal(false, host.Devices[0].Playing, "it never played");
        Equal(true, host.Devices[0].Disposed, "and released its device");
        Equal(false, host.Player.IsPrepared, "nothing is left warm");
        Equal(false, host.Player.IsPlaying, "and the film is unclaimed");
        Equal(
            true,
            host.Log.Any(line => line.Contains("preparation discarded", StringComparison.Ordinal)),
            "which is reported once");
    }

    /// <summary>
    /// The failure path scoped by generation. An old preparation that fails must not
    /// cancel the start of the film that began after it was abandoned.
    /// </summary>
    private static void AFailedOldPreparationDoesNotCancelANewerStart()
    {
        // The stop and the next film both land while the first device is still opening, so
        // the failure is raised against a generation that no longer owns anything.
        var host = new Host { FailToOpen = true };
        var secondAccepted = false;
        host.OnOpeningOnce = () =>
        {
            host.Player.Stop("first film skipped");
            host.FailToOpen = false;
            // Asserted after the run, not here: an exception thrown inside the device open
            // would be caught as an open failure and the real reason lost.
            secondAccepted = host.Player.StartTimed(
                "second film started", TimeSpan.Zero, "native film flag");
        };

        host.Player.Prepare("first film armed");
        host.RunScheduled();

        Equal(
            true,
            secondAccepted,
            "the second film's start is accepted while the first device is still opening");
        Equal(true, host.Player.IsPlaying, "the second film survives the older preparation failing");
        Equal(
            1,
            host.Devices.Count(device => device.Playing),
            "and is playing on its own device");
    }

    /// <summary>
    /// The adoption path scoped by generation and identity. A device that finishes opening
    /// for an abandoned film must not be handed to the next one.
    /// </summary>
    private static void ALateDeviceCannotBeAdoptedByALaterFilm()
    {
        // The first film is stopped and a second started while the first device is still
        // opening, so that device arrives with a start pending that is not its own.
        var host = new Host();
        host.OnOpeningOnce = () =>
        {
            host.Player.Stop("first film skipped");
            host.Player.StartTimed("second film", TimeSpan.Zero, null);
        };

        host.Player.StartTimed("first film", TimeSpan.Zero, null);
        host.RunScheduled();

        Equal(2, host.Devices.Count, "both films opened a device");
        Equal(
            false,
            host.Devices[0].Playing,
            "the first film's device is not handed to the second film");
        Equal(true, host.Devices[0].Disposed, "it is released instead");
        Equal(true, host.Devices[1].Playing, "and the second film plays on its own");
        Equal(true, host.Player.IsPlaying, "so the later film owns the track");
    }

    /// <summary>
    /// The ownership transfer and the play are inside one lock, so a stop that arrives in
    /// that window cannot dispose the device between them. Driven here by a device that
    /// stops the player from inside its own Play.
    /// </summary>
    private static void StoppingWhilePlayBeginsLeavesNothingClaimed()
    {
        var host = new Host();
        host.OnPlay = () => host.Player.Stop("movie skipped as playback began");
        Equal(true, host.Player.Start("character action"), "the start completes");
        Equal(false, host.Player.IsPlaying, "the stop that arrived during Play wins");
        Equal(true, host.Devices[0].Disposed, "and the device is released exactly once");
    }

    private static void DisposeIsFinal()
    {
        var host = new Host();
        host.Player.Prepare("armed");
        host.Player.Dispose();
        host.RunScheduled();
        Equal(0, host.Devices.Count, "a preparation abandoned by disposal opens nothing");
        Equal(false, host.Player.Prepare("after dispose"), "preparing after disposal is refused");
        Equal(false, host.Player.Start("after dispose"), "starting after disposal is refused");
        Equal(
            false,
            host.Player.StartTimed("after dispose", TimeSpan.Zero, "native film flag"),
            "and so is a timed start");
        Equal(false, host.Player.IsPlaying, "nothing is claimed");

        // Disposed while the device was already opening: it is released, not installed.
        var opening = new Host();
        opening.OnOpening = () => opening.Player.Dispose();
        opening.Player.StartTimed("film started", TimeSpan.Zero, null);
        opening.RunScheduled();
        Equal(1, opening.Devices.Count, "the device finished opening");
        Equal(true, opening.Devices[0].Disposed, "and was released");
        Equal(false, opening.Devices[0].Playing, "never played");
        Equal(false, opening.Player.Start("after dispose"), "and the player stays final");
    }

    private static void AWaitingStartIsNotDoubled()
    {
        var host = new Host();
        host.Player.StartTimed("film started", TimeSpan.Zero, null);
        Equal(
            false,
            host.Player.StartTimed("film started again", TimeSpan.Zero, null),
            "a second timed start while one is pending is refused");
        Equal(
            false,
            host.Player.Start("a character cue during the film"),
            "and an ordinary start cannot take the device from under it");
        host.RunScheduled();
        Equal(1, host.Devices.Count, "exactly one device");
    }

    private static void RestartingAfterAStopPreparesAgain()
    {
        var host = new Host();
        host.Player.Prepare("armed");
        host.RunScheduled();
        host.Player.StartTimed("film started", TimeSpan.Zero, null);
        Equal(true, host.Devices[0].Playing, "the first film plays");
        host.Player.Stop("movie ended");
        Equal(true, host.Devices[0].Disposed, "and is released");

        Equal(true, host.Player.Prepare("armed again"), "a later film prepares again");
        host.RunScheduled();
        host.Advance(5);
        Equal(true, host.Player.StartTimed("second film", TimeSpan.Zero, null), "and starts");
        Equal(2, host.Devices.Count, "on a fresh device");
        Equal(true, host.Devices[1].Playing, "which plays");
    }

    private static void NaturalEndOfTrackReleasesTheDevice()
    {
        var host = new Host();
        host.Player.Prepare("armed");
        host.RunScheduled();
        host.Player.StartTimed("film started", TimeSpan.Zero, null);
        host.Devices[0].RaiseStopped(null);
        Equal(false, host.Player.IsPlaying, "the end of the track releases the film");
        Equal(true, host.Devices[0].Disposed, "and the device");
        Equal(
            true,
            host.Log.Any(line => line.Contains("reached the end of its track", StringComparison.Ordinal)),
            "reported as a natural end rather than a failure");

        var failed = new Host();
        failed.Player.Prepare("armed");
        failed.RunScheduled();
        failed.Player.StartTimed("film started", TimeSpan.Zero, null);
        failed.Devices[0].RaiseStopped(new InvalidOperationException("device lost"));
        Equal(false, failed.Player.IsPlaying, "a failed playback releases the film");
        Equal(
            true,
            failed.Log.Any(line => line.Contains("playback failed: device lost", StringComparison.Ordinal)),
            "and says why");
    }

    /// <summary>
    /// Not an assertion: what opening the real installed track and a real output device
    /// costs on this machine. That is the time a cold start would add in front of the
    /// narration, which is why the opening warms the device at initialization.
    /// </summary>
    private static void MeasureRealStartupCost()
    {
        var installed = Path.Combine(
            @"C:\Games\Final Fantasy VII\Reloaded-II\Mods\ff7.accessibility.reloaded",
            "Assets", "movies", "opening_audio_description.ogg");
        if (!OperatingSystem.IsWindows() || !File.Exists(installed))
        {
            Console.WriteLine("Opening startup cost: skipped, no installed narration track.");
            return;
        }

        var log = new List<string>();
        var player = new OpeningMovieAudioTrackPlayer(installed, 300, log.Add);
        var watch = Stopwatch.StartNew();
        var prepared = player.Prepare("measurement");
        var prepareCall = watch.ElapsedMilliseconds;
        for (var attempt = 0; attempt < 200 && !player.IsPrepared; attempt++)
        {
            Thread.Sleep(10);
        }

        var ready = watch.ElapsedMilliseconds;
        player.Dispose();
        Console.WriteLine(
            $"Opening startup cost: Prepare() returned in {prepareCall} ms, " +
            $"device ready after {ready} ms, accepted={prepared}.");

        var movie = Path.Combine(
            @"C:\Games\Final Fantasy VII\workingdir", "override", "movies", "opening.avi");
        if (!File.Exists(movie))
        {
            movie = Path.Combine(
                @"C:\Games\Final Fantasy VII\workingdir", "data", "movies", "opening.avi");
        }

        if (File.Exists(movie))
        {
            var probe = Stopwatch.StartNew();
            const int samples = 5;
            for (var attempt = 0; attempt < samples; attempt++)
            {
                _ = RestartManagerProbe.IsFileOpenByProcess(movie, Environment.ProcessId);
            }

            Console.WriteLine(
                "Opening startup cost: Restart Manager query averages " +
                $"{probe.ElapsedMilliseconds / (double)samples:0.0} ms over {samples} calls.");
        }
    }

    private sealed class Host
    {
        private readonly List<Action> scheduled = [];
        private long now;

        public Host(double trackSeconds = 119.47)
        {
            Player = new OpeningMovieAudioTrackPlayer(
                typeof(Host).Assembly.Location,   // any existing file; the device is fake
                300,
                Log.Add,
                "Opening movie",
                (_, _) =>
                {
                    // Captured before the hooks run, so a hook may clear it for the next
                    // open without rescuing this one.
                    var fail = FailToOpen;
                    OnOpening?.Invoke();
                    var once = OnOpeningOnce;
                    OnOpeningOnce = null;
                    once?.Invoke();
                    now += OpenCostMs;
                    if (fail)
                    {
                        throw new InvalidOperationException("no audio device");
                    }

                    var device = new FakeDevice(TimeSpan.FromSeconds(trackSeconds), () => OnPlay?.Invoke());
                    Devices.Add(device);
                    return device;
                },
                scheduled.Add,
                () => now);
        }

        public OpeningMovieAudioTrackPlayer Player { get; }

        public List<FakeDevice> Devices { get; } = [];

        public List<string> Log { get; } = [];

        /// <summary>Runs inside the device open, for the cancel-mid-open races.</summary>
        public Action? OnOpening { get; set; }

        /// <summary>Runs inside the first device open only, for the generation races.</summary>
        public Action? OnOpeningOnce { get; set; }

        /// <summary>Runs inside Play, for the stop-during-handover race.</summary>
        public Action? OnPlay { get; set; }

        public bool FailToOpen { get; set; }

        public long OpenCostMs { get; set; }

        public void Advance(long milliseconds) => now += milliseconds;

        /// <summary>Lets the background preparations complete, deterministically.</summary>
        public void RunScheduled()
        {
            for (var guard = 0; guard < 8 && scheduled.Count > 0; guard++)
            {
                var pending = scheduled.ToArray();
                scheduled.Clear();
                foreach (var work in pending)
                {
                    work();
                }
            }
        }
    }

    private sealed class FakeDevice(TimeSpan length, Action onPlay)
        : OpeningMovieAudioTrackPlayer.INarrationDevice
    {
        private Action<Exception?>? stopped;

        public TimeSpan Length { get; } = length;

        public TimeSpan SeekedTo { get; private set; }

        public bool Playing { get; private set; }

        public bool Disposed { get; private set; }

        public int DisposeCount { get; private set; }

        public void Seek(TimeSpan position) => SeekedTo = position;

        public void OnStopped(Action<Exception?> handler) => stopped = handler;

        public void Play()
        {
            Playing = true;
            onPlay();
        }

        public void RaiseStopped(Exception? exception) => stopped?.Invoke(exception);

        public void Dispose()
        {
            DisposeCount++;
            Disposed = true;
            Playing = false;
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Opening movie startup: {message}. Expected {expected}, actual {actual}.");
        }
    }
}
