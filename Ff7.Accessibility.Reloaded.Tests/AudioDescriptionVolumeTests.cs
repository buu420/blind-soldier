using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;
using NAudio.Vorbis;
using NAudio.Wave;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The player's description volume: a live master level, 50 to 300 percent in steps of 25,
/// multiplying each recording's own calibration, with a limiter so a boost can never push a
/// sample past full scale. The recorded descriptions are already peak-normalised to about
/// -1.7 dBFS (narration-peaks.txt, 1,185 clips), so without the limiter anything above about
/// 122 percent would clip. The checks on real recordings decode the four loudest packaged
/// clips with the runtime's own NAudio.Vorbis reader through the exact sample chain the
/// output device plays.
/// </summary>
internal static class AudioDescriptionVolumeTests
{
    public static void Run()
    {
        // The level.
        TheLevelStepsByTwentyFiveBetweenFiftyAndThreeHundred();
        TheLevelMultipliesEachRecordingsCalibration();

        // The limiter.
        TheLimiterNeverExceedsFullScale();
        BelowTheCeilingTheLimiterChangesNothing();
        ALevelChangeGlidesInsteadOfJumping();

        // The real recordings through the runtime's decoder and sample chain.
        AtTheDefaultLevelTheRecordingsAreUnchanged();
        AtTheLoudestLevelTheLoudestRecordingsStayWithinFullScale();
        EveryLevelKeepsTheLoudestDescriptionsWithinFullScale();
        ALiveChangeReachesThePlayingRecordingWithoutMovingIt();

        // The player.
        ThePlayerReadsTheMasterLevelLive();
        ALevelChangeDoesNotDisturbPlaybackPauseOrCompletion();
        Console.WriteLine("PASS description volume: 50-300% live master level, limiter within full scale on the loudest recordings.");
    }

    // --- the level ---------------------------------------------------------------------

    private static void TheLevelStepsByTwentyFiveBetweenFiftyAndThreeHundred()
    {
        Check(new AccessibilityConfig().AudioDescriptionVolumePercent == 100 &&
              AudioDescriptionLevel.DefaultPercent == 100, "the level starts at 100 percent");

        var up = new List<int> { AudioDescriptionLevel.MinimumPercent };
        while (up[^1] < AudioDescriptionLevel.MaximumPercent)
        {
            up.Add(AudioDescriptionLevel.StepUp(up[^1]));
        }

        Check(up.SequenceEqual([50, 75, 100, 125, 150, 175, 200, 225, 250, 275, 300]), "steps of 25 from 50 to 300");
        Check(AudioDescriptionLevel.StepUp(300) == 300 && AudioDescriptionLevel.StepDown(50) == 50, "it stops at either end");
        Check(AudioDescriptionLevel.StepUp(110) == 125 && AudioDescriptionLevel.StepDown(110) == 100,
            "a hand-edited level steps onto the grid");
        Check(AudioDescriptionLevel.Effective(0) == 50 && AudioDescriptionLevel.Effective(-20) == 50 &&
              AudioDescriptionLevel.Effective(1000) == 300 && AudioDescriptionLevel.Effective(110) == 110,
            "an out-of-range level is held within 50 to 300, never muted");
    }

    private static void TheLevelMultipliesEachRecordingsCalibration()
    {
        Check(AudioDescriptionLevel.Gain(1.0f, 100) == 1.0f, "scene, film and battle recordings at 100 percent");
        Check(AudioDescriptionLevel.Gain(3.0f, 100) == 3.0f, "the quieter opening keeps its own 300 percent");
        Check(AudioDescriptionLevel.Gain(1.0f, 250) == 2.5f, "250 percent of a recording calibrated at 100");
        Check(AudioDescriptionLevel.Gain(3.0f, 150) == 4.5f, "the opening is raised by the same master level");
        Check(AudioDescriptionLevel.Gain(1.0f, 0) == 0.5f, "a broken level cannot silence descriptions");
        Check(AudioDescriptionLevel.Gain(0.0f, 300) == 0.0f,
            "a recording calibrated to 0 percent stays off, so its words still go to speech");
    }

    // --- the limiter -------------------------------------------------------------------

    private static void TheLimiterNeverExceedsFullScale()
    {
        // A full-scale square wave nine times over: the opening's 300 percent at 300 percent.
        var limiter = new AudioDescriptionLimiter(44100, 2);
        var square = new float[44100 * 2 * 2];
        for (var i = 0; i < square.Length; i++)
        {
            square[i] = (i / 100) % 2 == 0 ? 1.0f : -1.0f;
        }

        limiter.Process(square, 9.0f);
        Check(Peak(square) <= AudioDescriptionLimiter.Ceiling + 1e-6f, "a square wave at nine times stays under the ceiling");
        Check(Peak(square) > AudioDescriptionLimiter.Ceiling * 0.99f, "and is not crushed far below it");

        // One sample out of nowhere: there is no look-ahead, so it must be caught on its own.
        var spike = new float[2000];
        spike[777] = 100.0f;
        spike[1501] = -100.0f;
        new AudioDescriptionLimiter(44100, 1).Process(spike, 3.0f);
        Check(Peak(spike) <= AudioDescriptionLimiter.Ceiling + 1e-6f, "an isolated spike is caught on its own sample");

        var broken = new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0.5f };
        new AudioDescriptionLimiter(44100, 1).Process(broken, 2.0f);
        Check(broken.Take(3).All(sample => sample == 0f) && float.IsFinite(broken[3]) &&
              Math.Abs(broken[3]) <= AudioDescriptionLimiter.Ceiling + 1e-6f,
            "a sample that is not a number is silenced, never passed on");

        // A block that ends part way through a frame is still bounded.
        var odd = Enumerable.Repeat(1.0f, 7).ToArray();
        new AudioDescriptionLimiter(44100, 2).Process(odd, 4.0f);
        Check(Peak(odd) <= AudioDescriptionLimiter.Ceiling + 1e-6f, "a partial frame is still limited");

        // Both channels of a frame are turned down together, keeping the stereo image.
        var stereo = new float[] { 1.0f, 0.1f, 1.0f, 0.1f, 1.0f, 0.1f };
        new AudioDescriptionLimiter(44100, 2).Process(stereo, 3.0f);
        Check(Math.Abs((stereo[5] / stereo[4]) - 0.1f) < 1e-5f && stereo[4] <= AudioDescriptionLimiter.Ceiling + 1e-6f,
            "the quieter channel is reduced in proportion with the louder one");
        Check(AudioDescriptionLimiter.Ceiling < 1.0f, "the ceiling sits below digital full scale");
    }

    private static void BelowTheCeilingTheLimiterChangesNothing()
    {
        var limiter = new AudioDescriptionLimiter(48000, 1);
        var sine = Sine(48000, 0.3f, 440, 48000);
        var expected = sine.Select(sample => sample * 2.0f).ToArray();
        limiter.Process(sine, 2.0f);
        Check(sine.SequenceEqual(expected), "a boost that stays below the ceiling is exactly the gain");
        Check(limiter.LimitedFrames == 0 && limiter.DeepestReduction == 1.0f, "and nothing was limited");
    }

    private static void ALevelChangeGlidesInsteadOfJumping()
    {
        var limiter = new AudioDescriptionLimiter(44100, 1);
        var first = Enumerable.Repeat(0.2f, 4410).ToArray();
        limiter.Process(first, 1.0f);
        Check(first[0] == 0.2f && first.All(sample => sample == 0.2f), "the first block plays at its level from its first sample");

        var second = Enumerable.Repeat(0.2f, 4410).ToArray();
        limiter.Process(second, 2.0f);
        var largestStep = 0.0f;
        var previous = first[^1];
        foreach (var sample in second)
        {
            largestStep = Math.Max(largestStep, Math.Abs(sample - previous));
            previous = sample;
        }

        Check(largestStep < 0.001f, $"a doubled level glides there (largest step {largestStep})");
        Check(second[^1] == 0.4f && Math.Abs(limiter.CurrentGain - 2.0f) < 1e-6f, "and arrives exactly");
        var rampEnd = Array.FindIndex(second, sample => sample == 0.4f);
        Check(rampEnd > 0 && rampEnd <= 44100 / 20, $"within 50 ms ({rampEnd} samples)");
    }

    // --- real recordings ----------------------------------------------------------------

    private static void AtTheDefaultLevelTheRecordingsAreUnchanged()
    {
        foreach (var recording in LoudestRecordings())
        {
            using var raw = new VorbisWaveReader(recording.Path);
            using var played = new VorbisWaveReader(recording.Path);
            var reference = raw.ToSampleProvider();
            var chain = OpeningMovieAudioTrackPlayer.CreateNarrationChain(
                played, () => AudioDescriptionLevel.Gain(recording.Calibration, AudioDescriptionLevel.DefaultPercent));
            var block = Block(played.WaveFormat);
            var expected = new float[block];
            var actual = new float[block];
            long samples = 0;
            while (true)
            {
                var wanted = reference.Read(expected, 0, block);
                var got = chain.Read(actual, 0, block);
                Check(wanted == got, $"{recording.Name}: the chain returns exactly what the decoder does");
                if (got == 0)
                {
                    break;
                }

                for (var i = 0; i < got; i++)
                {
                    if (actual[i] != expected[i] * recording.Calibration)
                    {
                        throw new InvalidOperationException(
                            $"Description volume: {recording.Name} changed at the default level, sample {samples + i}.");
                    }
                }

                samples += got;
            }

            Check(samples > played.WaveFormat.SampleRate, $"{recording.Name}: decoded a real recording");
        }
    }

    private static void AtTheLoudestLevelTheLoudestRecordingsStayWithinFullScale()
    {
        foreach (var recording in LoudestRecordings())
        {
            var master = AudioDescriptionLevel.MaximumPercent;
            var gain = AudioDescriptionLevel.Gain(recording.Calibration, master);
            var (rawPeak, rawSamples) = Measure(recording.Path, null, 1.0f, out _);
            var (peak, samples) = Measure(recording.Path, () => gain, gain, out var limiter);
            Check(rawPeak * gain > 1.0f, $"{recording.Name}: unlimited, {gain:0.##}x would clip (raw peak {rawPeak:0.###})");
            Check(peak <= AudioDescriptionLimiter.Ceiling + 1e-6f && peak <= 1.0f,
                $"{recording.Name}: limited peak {peak:0.####} stays within full scale");
            Check(samples == rawSamples, $"{recording.Name}: no sample is added or lost, so its timing is untouched");
            Console.WriteLine(
                $"  {recording.Name} at {master}%: gain {gain:0.##}x, raw peak {rawPeak:0.###}, output peak {peak:0.####}, " +
                $"{100.0 * limiter.LimitedFrames / (samples / limiter.Channels):0.0}% of frames reduced, deepest " +
                $"{20 * Math.Log10(limiter.DeepestReduction):0.0} dB.");
        }
    }

    private static void EveryLevelKeepsTheLoudestDescriptionsWithinFullScale()
    {
        var shortest = LoudestRecordings().Where(recording => recording.Kind == "battle" || recording.Kind == "scene");
        foreach (var recording in shortest)
        {
            for (var master = AudioDescriptionLevel.MinimumPercent;
                 master <= AudioDescriptionLevel.MaximumPercent;
                 master += AudioDescriptionLevel.StepPercent)
            {
                var gain = AudioDescriptionLevel.Gain(recording.Calibration, master);
                var (peak, _) = Measure(recording.Path, () => gain, gain, out _);
                Check(peak <= AudioDescriptionLimiter.Ceiling + 1e-6f, $"{recording.Name} at {master}%: peak {peak}");
            }
        }
    }

    private static void ALiveChangeReachesThePlayingRecordingWithoutMovingIt()
    {
        var recording = LoudestRecordings().First(candidate => candidate.Kind == "battle");
        var master = 100;
        using var played = new VorbisWaveReader(recording.Path);
        using var raw = new VorbisWaveReader(recording.Path);
        var chain = OpeningMovieAudioTrackPlayer.CreateNarrationChain(
            played, () => AudioDescriptionLevel.Gain(recording.Calibration, master));
        var reference = raw.ToSampleProvider();
        var block = Block(played.WaveFormat);
        var expected = new float[block];
        var actual = new float[block];

        void Lockstep(int blocks, float factor, bool exact)
        {
            for (var n = 0; n < blocks; n++)
            {
                var wanted = reference.Read(expected, 0, block);
                var got = chain.Read(actual, 0, block);
                Check(wanted == got && got > 0, "the recording plays on in step with the decoder");
                for (var i = 0; i < got; i++)
                {
                    Check(Math.Abs(actual[i]) <= AudioDescriptionLimiter.Ceiling + 1e-6f, "always within full scale");
                    if (exact && actual[i] != expected[i] * factor)
                    {
                        throw new InvalidOperationException(
                            $"Description volume: after a live change sample {i} is {actual[i]}, expected {expected[i] * factor}.");
                    }
                }
            }
        }

        Lockstep(5, 1.0f, exact: true);
        master = 50; // the player turns it down while it plays
        Lockstep(1, 0.5f, exact: false);
        Lockstep(5, 0.5f, exact: true);
        Check(played.Position == raw.Position, "the change neither restarted nor skipped the recording");

        // Paused: nothing is read while the level changes, and playing on resumes in place.
        var pausedAt = played.Position;
        master = 300;
        Check(played.Position == pausedAt, "changing the level while paused does not move the recording");
        var loud = 0.0f;
        long remaining = 0;
        while (true)
        {
            var wanted = reference.Read(expected, 0, block);
            var got = chain.Read(actual, 0, block);
            Check(wanted == got, "the boosted recording ends exactly where the recording does");
            if (got == 0)
            {
                break;
            }

            remaining += got;
            loud = Math.Max(loud, Peak(actual.AsSpan(0, got)));
        }

        Check(remaining > 0 && loud <= AudioDescriptionLimiter.Ceiling + 1e-6f, "turned up to 300 percent, still within full scale");
        Check(loud > 0.9f, $"and audibly louder (peak {loud:0.###})");
        Check(chain.Read(actual, 0, block) == 0, "the end of the recording still reports its end");
    }

    // --- the player ---------------------------------------------------------------------

    private static void ThePlayerReadsTheMasterLevelLive()
    {
        var log = new List<string>();
        var master = 100;
        var device = new FakeDevice();
        var player = Player(100, () => master, device, log);
        Check(player.CurrentGain == 1.0f, "100 percent of a recording at 100 percent");
        master = 175;
        Check(player.CurrentGain == 1.75f, "the player reads the level as it is now");
        master = 1000;
        Check(player.CurrentGain == 3.0f, "held at 300 percent");
        master = 0;
        Check(player.CurrentGain == 0.5f, "and never below 50 percent");

        master = 150;
        var opening = Player(300, () => master, new FakeDevice(), log);
        Check(opening.CurrentGain == 4.5f, "the opening's own 300 percent is raised by the master level");

        var unconnected = Player(100, null, new FakeDevice(), log);
        Check(unconnected.CurrentGain == 1.0f, "a player without a master level keeps its calibration");
        Check(log.Any(line => line.Contains("master", StringComparison.Ordinal)), "the log names the live master level");
    }

    private static void ALevelChangeDoesNotDisturbPlaybackPauseOrCompletion()
    {
        var log = new List<string>();
        var master = 100;
        var device = new FakeDevice();
        var opened = 0;
        var player = new OpeningMovieAudioTrackPlayer(
            typeof(AudioDescriptionVolumeTests).Assembly.Location,
            100,
            log.Add,
            "Battle description",
            (_, _) =>
            {
                opened++;
                return device;
            },
            work => work(),
            () => 0,
            () => master);

        Check(player.Start("test"), "the recording starts");
        master = 250;
        Check(player.IsPlaying && opened == 1 && device.Plays == 1 && device.Seeks == 0,
            "turning it up while it plays opens nothing new and replays nothing");
        Check(player.SetPaused(true) && device.Pauses == 1, "it pauses");
        master = 50;
        Check(player.SetPaused(false) && device.Plays == 2 && device.Seeks == 0, "and resumes where it was");
        device.Finish();
        Check(!player.IsPlaying && player.CompletedNormally, "and still reports reaching its own end");
    }

    // --- helpers ------------------------------------------------------------------------

    private static OpeningMovieAudioTrackPlayer Player(int calibration, Func<int>? master, FakeDevice device, List<string> log) =>
        new(
            typeof(AudioDescriptionVolumeTests).Assembly.Location,
            calibration,
            log.Add,
            "Battle description",
            (_, _) => device,
            work => work(),
            () => 0,
            master);

    private sealed record Recording(string Kind, string Name, string Path, float Calibration);

    /// <summary>
    /// The loudest packaged recording of each kind, from the decode of all 1,185 recordings
    /// (narration-peaks.txt): scene 0.821, battle 0.817, film 0.818, opening 0.314.
    /// </summary>
    private static IReadOnlyList<Recording> LoudestRecordings()
    {
        var assets = ResolveAssets();
        return
        [
            new("scene", "loudest scene description",
                Path.Combine(assets, "cutscene-voice", "c33f46e6d942f3a2b68d485cd11e92920416d71cf6e91f42c3f3cccbc23a095d.ogg"), 1.0f),
            new("battle", "loudest battle description",
                Path.Combine(assets, "cutscene-voice", "c67bff4ee926f89b3eb41e99b59377fe31b59d654f310c950151e00c91678153.ogg"), 1.0f),
            new("film", "loudest film narration",
                Path.Combine(assets, "movies", "ending1_audio_description.ogg"), 1.0f),
            new("opening", "opening film narration",
                Path.Combine(assets, "movies", "opening_audio_description.ogg"),
                OpeningMovieAudioTrackVolumePolicy.ToGain(new AccessibilityConfig().OpeningMovieAudioTrackVolumePercent)),
        ];
    }

    private static string ResolveAssets()
    {
        static bool Has(string assets) =>
            File.Exists(Path.Combine(assets, "movies", "opening_audio_description.ogg")) &&
            File.Exists(Path.Combine(assets, "cutscene-voice", "manifest.json"));

        var source = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (!string.IsNullOrWhiteSpace(source))
        {
            var path = Path.Combine(source, "Ff7.Accessibility.Reloaded", "Assets");
            if (Has(path)) return path;
        }

        var installed = Path.Combine(AppContext.BaseDirectory, "Assets");
        if (Has(installed)) return installed;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "Ff7.Accessibility.Reloaded", "Assets");
            if (Has(path)) return path;
        }

        throw new InvalidOperationException("Description volume: the packaged narration recordings were not found.");
    }

    /// <summary>The peak and sample count of a recording through the runtime chain, or raw when no gain.</summary>
    private static (float Peak, long Samples) Measure(
        string path, Func<float>? gain, float expectedGain, out AudioDescriptionLimiter limiter)
    {
        using var reader = new VorbisWaveReader(path);
        ISampleProvider provider;
        if (gain is null)
        {
            provider = reader.ToSampleProvider();
            limiter = new AudioDescriptionLimiter(reader.WaveFormat.SampleRate, reader.WaveFormat.Channels);
        }
        else
        {
            var chain = OpeningMovieAudioTrackPlayer.CreateNarrationChain(reader, gain);
            provider = chain;
            limiter = ((NarrationGainSampleProvider)chain).Limiter;
        }

        var buffer = new float[Block(reader.WaveFormat)];
        var peak = 0.0f;
        long samples = 0;
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            peak = Math.Max(peak, Peak(buffer.AsSpan(0, read)));
            samples += read;
        }

        if (gain is not null)
        {
            Check(Math.Abs(limiter.CurrentGain - expectedGain) < 1e-5f, "the chain applied the requested gain");
        }

        return (peak, samples);
    }

    /// <summary>One WaveOut buffer: 80 ms of latency over two buffers.</summary>
    private static int Block(WaveFormat format) => format.SampleRate * format.Channels / 25;

    private static float Peak(ReadOnlySpan<float> samples)
    {
        var peak = 0.0f;
        foreach (var sample in samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        return peak;
    }

    private static float[] Sine(int count, float amplitude, double hertz, int sampleRate) =>
        Enumerable.Range(0, count)
            .Select(i => (float)(amplitude * Math.Sin(2 * Math.PI * hertz * i / sampleRate)))
            .ToArray();

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Description volume: " + message);
    }

    private sealed class FakeDevice : OpeningMovieAudioTrackPlayer.INarrationDevice
    {
        private Action<Exception?>? stopped;

        public TimeSpan Length => TimeSpan.FromSeconds(6);

        public int Plays { get; private set; }

        public int Pauses { get; private set; }

        public int Seeks { get; private set; }

        public void Seek(TimeSpan position)
        {
            if (position > TimeSpan.Zero)
            {
                Seeks++;
            }
        }

        public void OnStopped(Action<Exception?> handler) => stopped += handler;

        public void Play() => Plays++;

        public void Pause() => Pauses++;

        public void Finish() => stopped?.Invoke(null);

        public void Dispose()
        {
        }
    }
}
