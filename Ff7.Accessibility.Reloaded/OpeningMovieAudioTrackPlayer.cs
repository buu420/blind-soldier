using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ff7.Accessibility.Reloaded;

internal sealed class OpeningMovieAudioTrackPlayer : IDisposable
{
    private readonly string path;
    private readonly float volume;
    private readonly Action<string> log;
    private readonly object sync = new();
    private ActivePlayback? activePlayback;

    public OpeningMovieAudioTrackPlayer(string path, int volumePercent, Action<string> log)
    {
        this.path = path;
        volume = OpeningMovieAudioTrackVolumePolicy.ToGain(volumePercent);
        this.log = log;

        if (File.Exists(path))
        {
            log(
                $"Opening movie narration track: {path} " +
                $"({new FileInfo(path).Length} bytes), volume={volume * 100:0}%.");
        }
        else
        {
            log($"Opening movie narration track missing: {path}");
        }
    }

    public bool IsPlaying
    {
        get
        {
            lock (sync)
            {
                return activePlayback is not null;
            }
        }
    }

    public bool Start(string reason)
    {
        lock (sync)
        {
            if (activePlayback is not null)
            {
                return false;
            }
        }

        if (volume <= 0)
        {
            log($"Opening movie narration skipped ({reason}): volume is 0%.");
            return false;
        }

        if (!File.Exists(path))
        {
            log($"Opening movie narration skipped ({reason}): file is missing at {path}");
            return false;
        }

        try
        {
            var reader = new VorbisWaveReader(path);
            var output = new WaveOutEvent
            {
                DesiredLatency = 80
            };
            var volumeProvider = new VolumeSampleProvider(reader.ToSampleProvider())
            {
                Volume = volume
            };
            var playback = new ActivePlayback(reader, output);
            output.PlaybackStopped += (_, args) => OnPlaybackStopped(playback, args.Exception);
            output.Init(volumeProvider);

            lock (sync)
            {
                if (activePlayback is not null)
                {
                    playback.Dispose();
                    return false;
                }

                activePlayback = playback;
            }

            output.Play();
            log($"Opening movie narration started ({reason}).");
            return true;
        }
        catch (Exception ex)
        {
            log($"Opening movie narration failed to start ({reason}): {ex.Message}");
            return false;
        }
    }

    public bool Stop(string reason)
    {
        ActivePlayback? playback;
        lock (sync)
        {
            playback = activePlayback;
            activePlayback = null;
        }

        if (playback is null)
        {
            return false;
        }

        playback.Dispose();
        log($"Opening movie narration stopped ({reason}).");
        return true;
    }

    public void Dispose()
    {
        Stop("mod unload");
    }

    private void OnPlaybackStopped(ActivePlayback playback, Exception? exception)
    {
        var shouldDispose = false;
        lock (sync)
        {
            if (ReferenceEquals(activePlayback, playback))
            {
                activePlayback = null;
                shouldDispose = true;
            }
        }

        if (!shouldDispose)
        {
            return;
        }

        playback.Dispose();
        if (exception is not null)
        {
            log($"Opening movie narration playback failed: {exception.Message}");
        }
        else
        {
            log("Opening movie narration reached the end of its track.");
        }
    }

    private sealed class ActivePlayback : IDisposable
    {
        private readonly VorbisWaveReader reader;
        private readonly WaveOutEvent output;
        private int disposed;

        public ActivePlayback(VorbisWaveReader reader, WaveOutEvent output)
        {
            this.reader = reader;
            this.output = output;
        }

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
