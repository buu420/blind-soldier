using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ff7.Accessibility.Reloaded;

internal sealed class FootstepSoundPlayer : IDisposable
{
    private readonly string path;
    private readonly float volume;
    private readonly Action<string> log;
    private readonly object sync = new();
    private readonly List<ActivePlayback> activePlaybacks = new();
    private readonly HashSet<string> missingLoggedPaths = new(StringComparer.OrdinalIgnoreCase);

    public FootstepSoundPlayer(string path, int volumePercent, Action<string> log)
    {
        this.path = path;
        volume = FootstepVolumePolicy.ToGain(volumePercent);
        this.log = log;
        LogSoundStatus(volumePercent);
    }

    public bool Play(string reason) => Play(reason, null);

    public bool Play(string reason, string? pathOverride)
    {
        var playbackPath = string.IsNullOrWhiteSpace(pathOverride) ? path : pathOverride;
        if (volume <= 0)
        {
            log($"Footstep playback skipped ({reason}): volume is 0%.");
            return false;
        }

        if (!File.Exists(playbackPath))
        {
            lock (sync)
            {
                if (missingLoggedPaths.Add(playbackPath))
                {
                    log($"Footstep sound file missing ({reason}): {playbackPath}");
                }
            }

            return false;
        }

        try
        {
            log($"Footstep play requested ({reason}): {playbackPath}");
            var reader = new VorbisWaveReader(playbackPath);
            var output = new WaveOutEvent
            {
                DesiredLatency = 80
            };
            var volumeProvider = new VolumeSampleProvider(reader.ToSampleProvider())
            {
                Volume = volume
            };
            var playback = new ActivePlayback(reader, output);
            output.PlaybackStopped += (_, _) => RemovePlayback(playback);
            output.Init(volumeProvider);
            lock (sync)
            {
                activePlaybacks.Add(playback);
            }

            output.Play();
            log($"Footstep playback started ({reason}).");
            return true;
        }
        catch (Exception ex)
        {
            log($"Footstep playback failed ({reason}): {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        ActivePlayback[] playbacks;
        lock (sync)
        {
            playbacks = activePlaybacks.ToArray();
            activePlaybacks.Clear();
        }

        foreach (var playback in playbacks)
        {
            playback.Dispose();
        }
    }

    private void RemovePlayback(ActivePlayback playback)
    {
        lock (sync)
        {
            activePlaybacks.Remove(playback);
        }

        playback.Dispose();
    }

    private void LogSoundStatus(int volumePercent)
    {
        if (File.Exists(path))
        {
            var length = new FileInfo(path).Length;
            log($"Footstep sound file: {path} ({length} bytes), volume={FootstepVolumePolicy.ClampVolumePercent(volumePercent)}%.");
            return;
        }

        log($"Footstep sound file missing at startup: {path}");
    }

    private sealed class ActivePlayback : IDisposable
    {
        private readonly VorbisWaveReader reader;
        private readonly WaveOutEvent output;
        private bool disposed;

        public ActivePlayback(VorbisWaveReader reader, WaveOutEvent output)
        {
            this.reader = reader;
            this.output = output;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            output.Dispose();
            reader.Dispose();
        }
    }
}
