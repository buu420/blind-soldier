using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ff7.Accessibility.Reloaded;

internal sealed class FieldZoneTransitionCuePlayer : IDisposable
{
    private readonly string path;
    private readonly float volume;
    private readonly Action<string> log;
    private readonly object sync = new();
    private readonly List<ActivePlayback> activePlaybacks = new();
    private bool missingLogged;

    public FieldZoneTransitionCuePlayer(string path, int volumePercent, Action<string> log)
    {
        this.path = path;
        volume = FootstepVolumePolicy.ToGain(volumePercent);
        this.log = log;

        if (File.Exists(path))
        {
            log(
                $"Field zone transition sound: path={path}, bytes={new FileInfo(path).Length}, " +
                $"volume={FootstepVolumePolicy.ClampVolumePercent(volumePercent)}%.");
        }
        else
        {
            log($"Field zone transition sound missing at startup: {path}");
        }
    }

    public bool Play(int previousFieldId, int currentFieldId)
    {
        if (volume <= 0)
        {
            log($"Field zone transition cue skipped: volume is 0%, field={previousFieldId}->{currentFieldId}.");
            return false;
        }

        if (!File.Exists(path))
        {
            if (!missingLogged)
            {
                missingLogged = true;
                log($"Field zone transition cue missing: {path}");
            }

            return false;
        }

        try
        {
            var reader = new WaveFileReader(path);
            var output = new WaveOutEvent { DesiredLatency = 80 };
            var volumeProvider = new VolumeSampleProvider(reader.ToSampleProvider())
            {
                Volume = volume
            };
            var playback = new ActivePlayback(reader, output);
            output.PlaybackStopped += (_, args) =>
            {
                if (args.Exception is not null)
                {
                    log($"Field zone transition cue stopped with error: {args.Exception.Message}");
                }

                RemovePlayback(playback);
            };
            output.Init(volumeProvider);
            lock (sync)
            {
                activePlaybacks.Add(playback);
            }

            output.Play();
            log($"Field zone transition cue started: field={previousFieldId}->{currentFieldId}.");
            return true;
        }
        catch (Exception ex)
        {
            log($"Field zone transition cue failed: field={previousFieldId}->{currentFieldId}, error={ex.Message}");
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

    private sealed class ActivePlayback : IDisposable
    {
        private readonly WaveFileReader reader;
        private readonly WaveOutEvent output;
        private bool disposed;

        public ActivePlayback(WaveFileReader reader, WaveOutEvent output)
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
