using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ff7.Accessibility.Reloaded;

internal sealed class JunonTimingCuePlayer : IDisposable
{
    private readonly object sync = new();
    private readonly Action<string> log;
    private readonly MixingSampleProvider? mixer;
    private readonly float[]? samples;
    private IWavePlayer? output;
    private volatile bool playbackFailed;
    private bool disposed;

    public JunonTimingCuePlayer(string path, int volumePercent, Action<string> log)
        : this(path, volumePercent, log, () => new WaveOutEvent
        {
            DesiredLatency = 40,
            NumberOfBuffers = 2
        })
    {
    }

    internal JunonTimingCuePlayer(
        string path,
        int volumePercent,
        Action<string> log,
        Func<IWavePlayer> createOutput)
    {
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        var gain = FootstepVolumePolicy.ToGain(volumePercent);
        if (gain <= 0)
        {
            log("Junon timing sound unavailable: volume is zero; spoken Now remains available.");
            return;
        }

        try
        {
            // Preload the asset and start a dedicated, continuously ready
            // output. Neither dialogue cancellation nor the OK input owns
            // this player, and cue onset does not reopen a file or device.
            using var reader = new WaveFileReader(Path.GetFullPath(path));
            var source = reader.ToSampleProvider();
            var loaded = new List<float>();
            var buffer = new float[4096];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (var index = 0; index < read; index++)
                {
                    loaded.Add(buffer[index] * gain);
                }
            }

            if (loaded.Count == 0)
            {
                throw new InvalidDataException("The timing WAV contains no audio samples.");
            }

            samples = loaded.ToArray();
            mixer = new MixingSampleProvider(source.WaveFormat) { ReadFully = true };
            output = createOutput();
            output.PlaybackStopped += OnPlaybackStopped;
            output.Init(mixer.ToWaveProvider());
            output.Play();
            log($"Junon timing sound ready: samples={samples.Length}, volume={volumePercent}%, latencyMs=40.");
        }
        catch (Exception exception)
        {
            playbackFailed = true;
            output?.Dispose();
            output = null;
            log($"Junon timing sound unavailable: {exception.Message}; spoken Now remains available.");
        }
    }

    public bool Play()
    {
        lock (sync)
        {
            if (disposed || playbackFailed || samples is null || mixer is null ||
                output?.PlaybackState != PlaybackState.Playing)
            {
                return false;
            }

            try
            {
                mixer.AddMixerInput(new CueSamples(samples, mixer.WaveFormat));
                return true;
            }
            catch (Exception exception)
            {
                playbackFailed = true;
                log($"Junon timing sound failed: {exception.Message}; spoken Now remains available.");
                return false;
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (output is not null)
            {
                output.PlaybackStopped -= OnPlaybackStopped;
                output.Dispose();
                output = null;
            }
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs args)
    {
        playbackFailed = true;
        log($"Junon timing sound stopped: {args.Exception?.Message ?? "audio output ended"}; spoken Now remains available.");
    }

    private sealed class CueSamples(float[] samples, WaveFormat format) : ISampleProvider
    {
        private int position;
        public WaveFormat WaveFormat => format;

        public int Read(float[] buffer, int offset, int count)
        {
            var available = Math.Min(count, samples.Length - position);
            Array.Copy(samples, position, buffer, offset, available);
            position += available;
            return available;
        }
    }
}
