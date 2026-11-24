using Ff7.Accessibility.Reloaded;
using NAudio.Wave;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class NavigationCueOutputTests
{
    internal static void Run()
    {
        var path = Path.Combine(Path.GetTempPath(), "blind-soldier-cue-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
                writer.WriteSamples(Enumerable.Range(0, 441).Select(i => (float)Math.Sin(i * 0.15) * 0.1f).ToArray(), 0, 441);
            var output = new Output();
            var opens = 0;
            var logs = new List<string>();
            using var player = new NavigationBeaconPlayer(path, 100, logs.Add, () => { opens++; return output; });
            Check(opens == 1 && output.Initializations == 1, "prepare the output once before gameplay cues");
            var cue = new NavigationBeaconCue("Ladder", "left", -1, 0, -1, 0, 0,
                NavigationBeaconMovementState.Correcting, 10, 100);
            for (var i = 0; i < 100; i++)
            {
                Check(player.Play(cue), "a repeated ladder cue can be queued");
                if (i % 3 == 0) player.StopAll();
            }
            Check(opens == 1 && output.Initializations == 1 && output.Starts == 1,
                "ladder and corner cues never reopen or restart the native sound device");
            Check(output.Stops == 0 && output.Disposals == 0, "leaving a ladder only clears queued samples");
            player.StopAll();
            Check(output.Read().All(x => x == 0f), "StopAll clears every pending cue while the output stays ready");
            Check(player.Play(cue), "the next ladder can cue after leaving the previous one");
            Check(output.Read().Any(x => x != 0f), "the persistent output receives the spatially rendered cue");
            output.Fail();
            Check(!player.Play(cue), "a failed audio device cannot repeatedly enter native initialization during gameplay");
            player.Dispose();
            player.Dispose();
            Check(output.Disposals == 1, "the output is released exactly once");
            Check(!player.Play(cue), "disposed cue players do not touch the native renderer");
            Console.WriteLine("PASS navigation cue output is reused, clears without device calls, and fails safely.");
        }
        finally { File.Delete(path); }
    }

    private static void Check(bool test, string why)
    {
        if (!test) throw new InvalidOperationException("Navigation cue output: " + why);
    }

    private sealed class Output : IWavePlayer
    {
        private IWaveProvider? provider;
        public int Initializations, Starts, Stops, Disposals;
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public PlaybackState PlaybackState { get; private set; }
        public float Volume { get; set; }
        public WaveFormat OutputWaveFormat => provider!.WaveFormat;
        public void Init(IWaveProvider source) { Initializations++; provider = source; }
        public void Play() { Starts++; PlaybackState = PlaybackState.Playing; }
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() { Stops++; PlaybackState = PlaybackState.Stopped; }
        public void Dispose() { Disposals++; PlaybackState = PlaybackState.Stopped; }
        public void Fail() { PlaybackState = PlaybackState.Stopped; PlaybackStopped?.Invoke(this, new StoppedEventArgs(new IOException("test device failure"))); }
        public float[] Read()
        {
            var buffer = new byte[4096];
            var length = provider!.Read(buffer, 0, buffer.Length);
            var floats = new float[length / sizeof(float)];
            Buffer.BlockCopy(buffer, 0, floats, 0, length);
            return floats;
        }
    }
}
