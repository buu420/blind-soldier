using System.Diagnostics;
using Ff7.Accessibility.Core;
using NAudio.Vorbis;
using NAudio.Wave;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// One narration track, with an optional warm-up.
///
/// <para>Opening the Vorbis file and opening a WinMM output device are the two slowest
/// things this class does - measured at 81 to 105 ms together on an idle machine with a
/// warm file cache. They used to happen inside <see cref="Start"/>, so for a caller that
/// starts a track the instant a film begins that cost sat in front of the narration, and
/// because the track always began at its own second zero it stayed there as an offset for
/// the length of the film. That is a measured cost and a plausible contribution to the
/// opening being heard out of step; it is not a reproduction of it.</para>
///
/// <para>Two start shapes, deliberately:</para>
/// <list type="bullet">
/// <item><see cref="Start"/> is unchanged in behaviour - it either owns the device and
/// plays by the time it returns, or it returns false. Short character-action descriptions
/// depend on that false to speak their text instead, and they must not lose their first
/// words to a time compensation either.</item>
/// <item><see cref="StartTimed"/> is for the opening film, which knows when it asked and
/// can be told where in the film it already is. It may accept before the device exists and
/// then seek forward by exactly the time that was lost, so the track is anchored to the
/// moment the caller asked rather than to the moment the hardware was ready.</item>
/// </list>
///
/// <para>Everything that can be in flight is scoped to a generation: a preparation, a
/// pending start and the device that satisfies it all carry one, and an epoch bump from
/// <see cref="Stop"/> or <see cref="Dispose"/> orphans all three. A late preparation can
/// therefore neither cancel a newer start nor be adopted by one.</para>
/// </summary>
internal sealed class OpeningMovieAudioTrackPlayer : IFieldMovieNarrationOutput, IFieldMovieNarrationCompletion, IFieldMovieNarrationPause
{
    /// <summary>
    /// The most a late device is allowed to skip. Beyond this something pathological has
    /// happened - a stalled driver, a suspended machine - and jumping a long way into a
    /// description is worse than starting where the caller asked.
    /// </summary>
    internal const int MaximumStartCompensationMs = 5000;

    private readonly string path;
    private readonly float volume;
    private readonly Func<int>? liveMasterVolumePercent;
    private readonly Action<string> log;
    // The same Vorbis/WaveOut playback is used for the opening film and for
    // described in-game films; only the diagnostic wording differs.
    private readonly string label;
    private readonly Func<string, float, INarrationDevice> openDevice;
    private readonly Action<Action> schedule;
    private readonly Func<long> elapsedMs;
    private readonly object sync = new();
    private Preparation? preparation;
    private PendingStart? pendingStart;
    private INarrationDevice? playing;
    private int epoch;
    private bool disposed;
    private bool completedNormally;
    private bool paused;

    /// <param name="volumePercent">This recording's own calibration.</param>
    /// <param name="liveMasterVolumePercent">
    /// The player's description volume (<see cref="AccessibilityConfig.AudioDescriptionVolumePercent"/>),
    /// read for every buffer while the recording plays; null keeps the calibration alone.
    /// </param>
    public OpeningMovieAudioTrackPlayer(
        string path,
        int volumePercent,
        Action<string> log,
        string label = "Opening movie",
        Func<int>? liveMasterVolumePercent = null)
        : this(path, volumePercent, log, label, null, null, null, liveMasterVolumePercent)
    {
    }

    /// <summary>
    /// Test seam. The device, the scheduler and the clock are injected so the whole
    /// asynchronous lifecycle - cancelled, superseded, failed, late, disposed - is
    /// exercised without an audio device and without waiting in real time.
    /// </summary>
    internal OpeningMovieAudioTrackPlayer(
        string path,
        int volumePercent,
        Action<string> log,
        string label,
        Func<string, float, INarrationDevice>? openDevice,
        Action<Action>? schedule,
        Func<long>? elapsedMs,
        Func<int>? liveMasterVolumePercent = null)
    {
        this.path = path;
        volume = OpeningMovieAudioTrackVolumePolicy.ToGain(volumePercent);
        this.liveMasterVolumePercent = liveMasterVolumePercent;
        this.log = log;
        this.label = label;
        this.openDevice = openDevice ?? ((devicePath, _) => new WaveOutNarrationDevice(devicePath, () => CurrentGain));
        this.schedule = schedule ?? (work => ThreadPool.QueueUserWorkItem(_ => work()));
        var stopwatch = Stopwatch.StartNew();
        this.elapsedMs = elapsedMs ?? (() => stopwatch.ElapsedMilliseconds);

        if (File.Exists(path))
        {
            log(
                $"{label} narration track: {path} " +
                $"({new FileInfo(path).Length} bytes), volume={volume * 100:0}%" +
                (liveMasterVolumePercent is null
                    ? "."
                    : $" x live master description volume, now {liveMasterVolumePercent()}% " +
                      $"(limited at {AudioDescriptionLimiter.Ceiling:0.00} of full scale)."));
        }
        else
        {
            log($"{label} narration track missing: {path}");
        }
    }

    /// <summary>
    /// Whether this track owns the device. A timed start that is waiting for its device
    /// counts: the caller has committed to the recording and must not also speak the
    /// paragraph it replaces.
    /// </summary>
    public bool IsPlaying
    {
        get
        {
            lock (sync)
            {
                return playing is not null || pendingStart is not null;
            }
        }
    }

    public bool CompletedNormally
    {
        get { lock (sync) return completedNormally; }
    }

    /// <summary>
    /// The gain the recording plays at now: its own calibration times the player's description
    /// volume. The output reads it for every buffer, so a change reaches a recording that is
    /// already playing without seeking, restarting or touching how its end is reported.
    /// </summary>
    internal float CurrentGain =>
        AudioDescriptionLevel.Gain(volume, liveMasterVolumePercent?.Invoke() ?? AudioDescriptionLevel.DefaultPercent);

    /// <summary>
    /// The samples the output device plays: the decoded recording at <paramref name="gain"/>,
    /// limited within full scale.
    /// </summary>
    internal static ISampleProvider CreateNarrationChain(WaveStream reader, Func<float> gain) =>
        new NarrationGainSampleProvider(reader.ToSampleProvider(), gain);

    /// <summary>Whether a warm device is sitting ready. Diagnostic and test use.</summary>
    internal bool IsPrepared
    {
        get
        {
            lock (sync)
            {
                return preparation?.Device is not null;
            }
        }
    }

    /// <summary>
    /// Opens the file and the output device ahead of the film, off this thread. Safe to
    /// call repeatedly; a no-op once a preparation exists, the track is playing, or the
    /// player has been disposed.
    /// </summary>
    public bool Prepare(string reason)
    {
        int generation;
        lock (sync)
        {
            if (disposed || playing is not null || preparation is not null)
            {
                return false;
            }

            if (!CanStartLocked(reason))
            {
                return false;
            }

            generation = epoch;
            preparation = new Preparation(generation, elapsedMs());
        }

        log($"{label} narration preparing ({reason}).");
        schedule(() => CompletePreparation(generation));
        return true;
    }

    /// <summary>
    /// Starts the track now, from the top. Returns false unless the track is playing by
    /// the time this returns, which is the contract every in-game film and character
    /// description relies on to fall back to spoken text.
    /// </summary>
    public bool Start(string reason)
    {
        INarrationDevice? warm;
        int generation;
        lock (sync)
        {
            if (disposed || playing is not null || pendingStart is not null)
            {
                return false;
            }

            if (!CanStartLocked(reason))
            {
                return false;
            }

            generation = epoch;
            warm = preparation?.Device;
            if (warm is not null)
            {
                preparation = null;
            }
        }

        var device = warm;
        var preparationMs = 0L;
        if (device is null)
        {
            var openedAt = elapsedMs();
            try
            {
                device = openDevice(path, volume);
            }
            catch (Exception ex)
            {
                log($"{label} narration failed to start ({reason}): {ex.Message}");
                return false;
            }

            preparationMs = elapsedMs() - openedAt;
        }

        // No compensation on this path. A short clip that skipped its own opening
        // milliseconds would lose its first word, and this caller was never promised a
        // timeline to stay aligned with.
        return Launch(device, generation, TimeSpan.Zero, anchor: null, reason, preparationMs, lagMs: 0);
    }

    /// <summary>
    /// Starts the opening film's track, from <paramref name="offset"/> into it, and
    /// compensates for a device that is not ready yet.
    /// </summary>
    /// <param name="offset">
    /// How far into the film the caller knows it already is, from a verified native
    /// reading. Zero means from the top.
    /// </param>
    /// <param name="anchor">What established the start, for the log.</param>
    internal bool StartTimed(string reason, TimeSpan offset, string? anchor)
    {
        if (offset < TimeSpan.Zero)
        {
            offset = TimeSpan.Zero;
        }

        INarrationDevice? warm;
        int generation;
        lock (sync)
        {
            if (disposed || playing is not null || pendingStart is not null)
            {
                return false;
            }

            if (!CanStartLocked(reason))
            {
                return false;
            }

            generation = epoch;
            pendingStart = new PendingStart(generation, elapsedMs(), offset, anchor, reason);
            warm = preparation?.Device;
            if (warm is not null)
            {
                preparation = null;
            }
        }

        if (warm is not null)
        {
            return LaunchPending(warm, generation, preparationMs: 0);
        }

        // Nothing warm: get a device coming and let it launch this start when it lands.
        if (Prepare($"{reason} (start is waiting)"))
        {
            return true;
        }

        lock (sync)
        {
            if (pendingStart?.Generation == generation && !disposed)
            {
                // A preparation was already in flight and will adopt this start.
                return true;
            }

            if (pendingStart?.Generation == generation)
            {
                pendingStart = null;
            }

            return false;
        }
    }

    public bool SetPaused(bool value)
    {
        INarrationDevice? failed = null;
        string? failure = null;
        lock (sync)
        {
            var device = playing;
            if (disposed || device is null) return false;
            if (paused == value) return true;
            try
            {
                // Keep ownership across Pause: a paused cue still occupies the voice.
                // Resume reuses the stream cursor rather than replaying or seeking it.
                if (value) device.Pause();
                else device.Play();
                if (!ReferenceEquals(playing, device)) return false;
                paused = value;
                return true;
            }
            catch (Exception ex)
            {
                // Detach before Dispose raises PlaybackStopped, so device failure can
                // never masquerade as natural completion and mark a cue heard.
                if (ReferenceEquals(playing, device)) playing = null;
                completedNormally = false;
                paused = false;
                failed = device;
                failure = ex.Message;
            }
        }
        failed.Dispose();
        log($"{label} narration pause/resume failed: {failure}");
        return false;
    }

    public bool Stop(string reason)
    {
        INarrationDevice? stopping;
        INarrationDevice? warm;
        bool hadPending;
        lock (sync)
        {
            // Every epoch bump orphans whatever is in flight: a preparation that finishes
            // later, and the pending start it would have satisfied.
            epoch++;
            completedNormally = false;
            paused = false;
            stopping = playing;
            warm = preparation?.Device;
            hadPending = pendingStart is not null;
            playing = null;
            preparation = null;
            pendingStart = null;
        }

        warm?.Dispose();
        if (stopping is null)
        {
            if (hadPending || warm is not null)
            {
                log($"{label} narration cancelled before it started ({reason}).");
                return true;
            }

            return false;
        }

        stopping.Dispose();
        log($"{label} narration stopped ({reason}).");
        return true;
    }

    /// <summary>
    /// Final. Nothing may be prepared or started afterwards, including by a preparation
    /// that was already opening a device when this ran.
    /// </summary>
    public void Dispose()
    {
        lock (sync)
        {
            disposed = true;
        }

        Stop("mod unload");
    }

    private bool CanStartLocked(string reason)
    {
        if (volume <= 0)
        {
            log($"{label} narration skipped ({reason}): volume is 0%.");
            return false;
        }

        if (!File.Exists(path))
        {
            log($"{label} narration skipped ({reason}): file is missing at {path}");
            return false;
        }

        return true;
    }

    private void CompletePreparation(int generation)
    {
        long startedAt;
        lock (sync)
        {
            if (disposed || preparation is not { } current || current.Generation != generation)
            {
                return;
            }

            startedAt = current.StartedAtMs;
        }

        INarrationDevice device;
        try
        {
            device = openDevice(path, volume);
        }
        catch (Exception ex)
        {
            lock (sync)
            {
                // Only this generation's state may be cleared. A failure here used to
                // cancel whatever start had arrived since, which is a different film.
                if (preparation?.Generation == generation)
                {
                    preparation = null;
                }

                if (pendingStart?.Generation == generation)
                {
                    pendingStart = null;
                }
            }

            log($"{label} narration failed to prepare: {ex.Message}");
            return;
        }

        var preparationMs = elapsedMs() - startedAt;
        var launch = false;
        lock (sync)
        {
            if (disposed ||
                epoch != generation ||
                preparation?.Generation != generation ||
                playing is not null)
            {
                // Stopped, disposed, superseded, or something else is already playing.
                // Only this generation's record may be cleared: clearing whatever is there
                // would destroy the newer preparation that replaced it, and that one is
                // the reason the film after this one has a device coming at all.
                if (preparation?.Generation == generation)
                {
                    preparation = null;
                }
            }
            else if (pendingStart?.Generation == generation)
            {
                launch = true;
                preparation = null;
            }
            else
            {
                preparation = preparation with { Device = device };
                log($"{label} narration ready after {preparationMs} ms.");
                return;
            }
        }

        if (!launch)
        {
            device.Dispose();
            log($"{label} narration preparation discarded after {preparationMs} ms.");
            return;
        }

        _ = LaunchPending(device, generation, preparationMs);
    }

    private bool LaunchPending(INarrationDevice device, int generation, long preparationMs)
    {
        PendingStart start;
        lock (sync)
        {
            // The device, the pending start and the epoch must all be the same generation.
            // Without the generation a late device could be adopted by a start belonging
            // to a film that began after the stop.
            if (disposed ||
                epoch != generation ||
                pendingStart is not { } requested ||
                requested.Generation != generation)
            {
                if (pendingStart?.Generation == generation)
                {
                    pendingStart = null;
                }

                device.Dispose();
                return false;
            }

            start = requested;
        }

        var lag = Math.Max(0, elapsedMs() - start.RequestedAtMs);
        var compensation = Math.Min(lag, MaximumStartCompensationMs);
        return Launch(
            device,
            generation,
            start.Offset + TimeSpan.FromMilliseconds(compensation),
            start.Anchor,
            start.Reason,
            preparationMs,
            lag);
    }

    /// <summary>
    /// Hands the device over and plays it, with the ownership transfer and the play inside
    /// one lock so a concurrent <see cref="Stop"/> cannot dispose the device between them.
    /// Monitor is re-entrant, so a backend that raised its stopped callback synchronously
    /// on this thread would still be safe.
    /// </summary>
    private bool Launch(
        INarrationDevice device,
        int generation,
        TimeSpan offset,
        string? anchor,
        string reason,
        long preparationMs,
        long lagMs)
    {
        string message;
        lock (sync)
        {
            if (disposed || epoch != generation || playing is not null)
            {
                if (pendingStart?.Generation == generation)
                {
                    pendingStart = null;
                }

                device.Dispose();
                return false;
            }

            var length = device.Length;
            if (length > TimeSpan.Zero && offset >= length - TimeSpan.FromMilliseconds(50))
            {
                if (pendingStart?.Generation == generation)
                {
                    pendingStart = null;
                }

                device.Dispose();
                log(
                    $"{label} narration not started ({reason}): the start offset " +
                    $"{offset.TotalSeconds:0.00}s is past the end of a " +
                    $"{length.TotalSeconds:0.00}s track.");
                return false;
            }

            try
            {
                device.OnStopped(exception => OnPlaybackStopped(device, exception));
                device.Seek(offset);
                pendingStart = null;
                playing = device;
                completedNormally = false;
                paused = false;
                device.Play();
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(playing, device))
                {
                    playing = null;
                }

                if (pendingStart?.Generation == generation)
                {
                    pendingStart = null;
                }

                device.Dispose();
                log($"{label} narration failed to start ({reason}): {ex.Message}");
                return false;
            }

            var anchorText = anchor is null ? string.Empty : $", anchor={anchor}";
            message =
                $"{label} narration started ({reason}): prep={preparationMs} ms, " +
                $"start lag={lagMs} ms, offset={offset.TotalSeconds:0.00}s{anchorText}.";
        }

        log(message);
        return true;
    }

    private void OnPlaybackStopped(INarrationDevice device, Exception? exception)
    {
        var shouldDispose = false;
        lock (sync)
        {
            if (ReferenceEquals(playing, device))
            {
                playing = null;
                completedNormally = exception is null;
                paused = false;
                shouldDispose = true;
            }
        }

        if (!shouldDispose)
        {
            return;
        }

        device.Dispose();
        if (exception is not null)
        {
            log($"{label} narration playback failed: {exception.Message}");
        }
        else
        {
            log($"{label} narration reached the end of its track.");
        }
    }

    private readonly record struct PendingStart(
        int Generation,
        long RequestedAtMs,
        TimeSpan Offset,
        string? Anchor,
        string Reason);

    private sealed record Preparation(int Generation, long StartedAtMs)
    {
        public INarrationDevice? Device { get; init; }
    }

    /// <summary>
    /// One prepared output. Opening it is the expensive part, so it is created once and
    /// then only seeked and played.
    /// </summary>
    internal interface INarrationDevice : IDisposable
    {
        TimeSpan Length { get; }

        void Seek(TimeSpan position);

        void OnStopped(Action<Exception?> handler);

        void Play();

        void Pause();
    }

    private sealed class WaveOutNarrationDevice : INarrationDevice
    {
        private readonly VorbisWaveReader reader;
        private readonly WaveOutEvent output;
        private int disposed;

        public WaveOutNarrationDevice(string path, Func<float> gain)
        {
            reader = new VorbisWaveReader(path);
            var opening = new WaveOutEvent
            {
                DesiredLatency = 80
            };
            try
            {
                // Init opens the device and allocates its buffers; it does not read the
                // provider, so seeking afterwards still decides where playback begins.
                opening.Init(CreateNarrationChain(reader, gain));
            }
            catch
            {
                // A failed Init still leaves a device handle to release.
                opening.Dispose();
                reader.Dispose();
                throw;
            }

            output = opening;
        }

        public TimeSpan Length => reader.TotalTime;

        public void Seek(TimeSpan position)
        {
            if (position <= TimeSpan.Zero)
            {
                return;
            }

            reader.CurrentTime = position;
        }

        public void OnStopped(Action<Exception?> handler) =>
            output.PlaybackStopped += (_, args) => handler(args.Exception);

        public void Play() => output.Play();

        public void Pause() => output.Pause();

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            output.Stop();
            output.Dispose();
            reader.Dispose();
        }
    }
}

/// <summary>
/// A recorded description at the gain the player chose, read afresh for every buffer the
/// output asks for and passed through <see cref="AudioDescriptionLimiter"/>, so no boost can
/// exceed full scale. It returns exactly what the decoder returned, sample for sample, so the
/// recording's length, position, pause and end stay the decoder's own.
/// </summary>
internal sealed class NarrationGainSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly Func<float> gain;
    private float lastGain = 1.0f;

    public NarrationGainSampleProvider(ISampleProvider source, Func<float> gain)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.gain = gain ?? throw new ArgumentNullException(nameof(gain));
        Limiter = new AudioDescriptionLimiter(source.WaveFormat.SampleRate, source.WaveFormat.Channels);
    }

    internal AudioDescriptionLimiter Limiter { get; }

    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = source.Read(buffer, offset, count);
        if (read > 0)
        {
            Limiter.Process(buffer.AsSpan(offset, read), NextGain());
        }

        return read;
    }

    private float NextGain()
    {
        try
        {
            var value = gain();
            if (float.IsFinite(value) && value >= 0)
            {
                lastGain = value;
            }
        }
        catch (Exception)
        {
            // Keep the level it had. Failing here would stop the recording on the device's
            // thread, and a stopped recording also counts as never heard.
        }

        return lastGain;
    }
}
