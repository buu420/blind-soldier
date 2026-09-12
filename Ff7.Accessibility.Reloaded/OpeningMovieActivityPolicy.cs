namespace Ff7.Accessibility.Reloaded;

public enum OpeningMovieActivitySignal
{
    None,
    FileHandle,
    NativeFieldMovieState
}

public readonly record struct OpeningMovieActivity(
    bool IsActive,
    OpeningMovieActivitySignal Signal);

/// <summary>What the opening narration should do about the film this tick.</summary>
public enum OpeningMovieStartAction
{
    /// <summary>No film. Nothing to prepare and nothing to play.</summary>
    Idle,

    /// <summary>
    /// The file is open but the game has not raised its own film flag yet. Open the
    /// output device now so that starting costs nothing later, and keep looking.
    /// </summary>
    Arm,

    /// <summary>Begin the narration.</summary>
    Start
}

public readonly record struct OpeningMovieStartGate(
    OpeningMovieStartAction Action,
    OpeningMovieActivitySignal Signal,
    string Anchor);

public static class OpeningMovieActivityPolicy
{
    /// <summary>
    /// How long the engine's own film flag is waited for after the movie file opens.
    ///
    /// <para>FFNx opens the file before it presents a frame, so the handle is the earlier
    /// of the two signals and starting on it puts the narration ahead of the picture by
    /// however long the decoder takes to get going. The engine's flag is the better
    /// anchor. It is not, however, reachable in every configuration, so the handle stays
    /// as a bounded fallback: a player whose flag cannot be read still hears the
    /// description, at most this late.</para>
    /// </summary>
    public const double NativeStartGraceMs = 1500;

    public static OpeningMovieActivity Resolve(
        bool fileHandleActive,
        bool nativeStateReadable,
        byte nativeModule,
        ushort nativeFieldId,
        ushort nativeMovieActive)
    {
        if (fileHandleActive)
        {
            return new(true, OpeningMovieActivitySignal.FileHandle);
        }

        if (IsNativeFilmActive(nativeStateReadable, nativeModule, nativeFieldId, nativeMovieActive))
        {
            return new(true, OpeningMovieActivitySignal.NativeFieldMovieState);
        }

        return new(false, OpeningMovieActivitySignal.None);
    }

    /// <summary>
    /// Separates arming from starting, which <see cref="Resolve"/> cannot: it answers
    /// "is a film happening", and the file handle is the first thing to say yes.
    /// </summary>
    /// <param name="millisecondsSinceArmed">
    /// How long the file has been open, or null if this is the first tick that saw it.
    /// </param>
    public static OpeningMovieStartGate ResolveStart(
        bool fileHandleActive,
        bool nativeStateReadable,
        byte nativeModule,
        ushort nativeFieldId,
        ushort nativeMovieActive,
        double? millisecondsSinceArmed,
        double graceMs = NativeStartGraceMs)
    {
        if (IsNativeFilmActive(nativeStateReadable, nativeModule, nativeFieldId, nativeMovieActive))
        {
            return new(
                OpeningMovieStartAction.Start,
                OpeningMovieActivitySignal.NativeFieldMovieState,
                "native film flag");
        }

        if (!fileHandleActive)
        {
            return new(OpeningMovieStartAction.Idle, OpeningMovieActivitySignal.None, string.Empty);
        }

        if (millisecondsSinceArmed is { } waited && waited >= graceMs)
        {
            return new(
                OpeningMovieStartAction.Start,
                OpeningMovieActivitySignal.FileHandle,
                $"movie file handle after waiting {waited:0} ms for the native film flag");
        }

        return new(
            OpeningMovieStartAction.Arm,
            OpeningMovieActivitySignal.FileHandle,
            "movie file opened");
    }

    /// <summary>
    /// The opening film's frame rate, from the installed movie itself: 15/1 fps over
    /// 119.467 seconds.
    /// </summary>
    public const double OpeningMovieFramesPerSecond = 15.0;

    /// <summary>
    /// Above this the counter is not this film's. 119.467 s at 15 fps is 1792 frames, and
    /// the engine's own opening-field comparison at 0x0063C2AA is against 1760.
    /// </summary>
    public const int MaximumOpeningMovieFrame = 1800;

    /// <summary>
    /// Turns the engine's own movie frame counter into how far into the film it already
    /// is, when that is safe to believe.
    ///
    /// <para>The counter at <c>0x00CC0E10</c> is written once per delivered video frame in
    /// the field frame loop, and the engine's opening-field logic reads that same word, so
    /// it is the film's clock rather than an inference. Two readings are required and the
    /// later one is used: a single zero is ambiguous - it is also what a film reads before
    /// it has delivered anything - so only actual progression anchors a start. A counter
    /// that went backwards or is beyond this film is refused outright.</para>
    /// </summary>
    public static bool TryResolveFrameOffset(
        bool nativeFilmActive,
        int firstFrame,
        int secondFrame,
        out TimeSpan offset,
        out string anchor)
    {
        offset = TimeSpan.Zero;
        anchor = string.Empty;
        if (!nativeFilmActive ||
            firstFrame < 0 ||
            secondFrame < firstFrame ||
            secondFrame > MaximumOpeningMovieFrame ||
            secondFrame == 0)
        {
            return false;
        }

        offset = TimeSpan.FromSeconds(secondFrame / OpeningMovieFramesPerSecond);
        anchor = $"native movie frame {secondFrame}";
        return true;
    }

    /// <summary>
    /// Whether the Restart Manager query can be skipped this tick.
    ///
    /// <para>Only once the engine's own flag has actually been seen for this film, and
    /// only while it is still readable: a read that starts failing has to fall back to the
    /// handle on the same tick, because otherwise the film would look as though it had
    /// ended and the narration would stop in the middle.</para>
    /// </summary>
    public static bool ShouldSkipFileHandleQuery(
        bool movieDetected,
        bool nativeFlagSeen,
        bool nativeStateReadable) =>
        movieDetected && nativeFlagSeen && nativeStateReadable;

    /// <summary>
    /// Whether the engine itself says the opening film is running.
    ///
    /// <para>Public because a caller cannot learn this from <see cref="Resolve"/>: that
    /// answers "is a film happening" and reports the file handle whenever it is open, so
    /// the native signal is invisible in the normal case where both are true. Anything
    /// that needs to know the engine has raised its flag - to stop polling the handle, or
    /// to trust the frame counter - has to ask directly.</para>
    /// </summary>
    public static bool IsNativeOpeningFilmActive(
        bool nativeStateReadable,
        byte nativeModule,
        ushort nativeFieldId,
        ushort nativeMovieActive) =>
        IsNativeFilmActive(nativeStateReadable, nativeModule, nativeFieldId, nativeMovieActive);

    private static bool IsNativeFilmActive(
        bool nativeStateReadable,
        byte nativeModule,
        ushort nativeFieldId,
        ushort nativeMovieActive) =>
        nativeStateReadable &&
        nativeModule == FieldPositionReader.FieldModule &&
        nativeFieldId == DeferredZoneSpeechTracker.OpeningFieldId &&
        nativeMovieActive != 0;
}
