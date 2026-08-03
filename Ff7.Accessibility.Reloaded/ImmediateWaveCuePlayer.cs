using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ff7.Accessibility.Reloaded;

internal sealed class ImmediateWaveCuePlayer : IDisposable
{
    private readonly string path;
    private readonly float volume;
    private readonly string cueName;
    private readonly Action<string> log;
    private readonly object sync = new();
    private readonly List<ActivePlayback> activePlaybacks = [];
    private bool missingLogged;

    public ImmediateWaveCuePlayer(
        string path,
        int volumePercent,
        string cueName,
        Action<string> log)
    {
        this.path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("A WAV cue path is required.", nameof(path))
            : Path.GetFullPath(path);
        volume = FootstepVolumePolicy.ToGain(volumePercent);
        this.cueName = string.IsNullOrWhiteSpace(cueName) ? "Immediate WAV cue" : cueName;
        this.log = log ?? throw new ArgumentNullException(nameof(log));

        if (File.Exists(this.path))
        {
            log(
                $"{this.cueName}: path={this.path}, bytes={new FileInfo(this.path).Length}, " +
                $"volume={FootstepVolumePolicy.ClampVolumePercent(volumePercent)}%.");
        }
        else
        {
            log($"{this.cueName} missing at startup: {this.path}");
        }
    }

    public bool Play(string reason)
    {
        if (volume <= 0)
        {
            log($"{cueName} skipped ({reason}): volume is 0%.");
            return false;
        }

        if (!File.Exists(path))
        {
            if (!missingLogged)
            {
                missingLogged = true;
                log($"{cueName} missing: {path}");
            }

            return false;
        }

        try
        {
            var reader = new WaveFileReader(path);
            var output = new WaveOutEvent
            {
                DesiredLatency = 40,
                NumberOfBuffers = 2
            };
            var volumeProvider = new VolumeSampleProvider(reader.ToSampleProvider())
            {
                Volume = volume
            };
            var playback = new ActivePlayback(reader, output);
            output.PlaybackStopped += (_, args) =>
            {
                if (args.Exception is not null)
                {
                    log($"{cueName} stopped with error: {args.Exception.Message}");
                }

                RemovePlayback(playback);
            };
            output.Init(volumeProvider);
            lock (sync)
            {
                activePlaybacks.Add(playback);
            }

            output.Play();
            log($"{cueName} started ({reason}).");
            return true;
        }
        catch (Exception ex)
        {
            log($"{cueName} failed ({reason}): {ex.Message}");
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

    private sealed class ActivePlayback(
        WaveFileReader reader,
        WaveOutEvent output) : IDisposable
    {
        private bool disposed;

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
