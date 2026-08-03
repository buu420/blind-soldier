using NAudio.Wave;

namespace Ff7.Accessibility.Reloaded;

internal sealed class NavigationBeaconPlayer : IDisposable
{
    private const int SampleRate = 44100;
    private const int SteamAudioFrameSize = 1024;

    private readonly float volume;
    private readonly int volumePercent;
    private readonly Action<string> log;
    private readonly object sync = new();
    private readonly object renderSync = new();
    private readonly List<ActivePlayback> activePlaybacks = new();
    private readonly float[]? monoSamples;
    private readonly SteamAudioNavigationBeaconRenderer? renderer;
    private bool missingHrtfLogged;
    private bool missingSoundLogged;

    public NavigationBeaconPlayer(string soundPath, int volumePercent, Action<string> log)
    {
        this.volumePercent = FootstepVolumePolicy.ClampVolumePercent(volumePercent);
        volume = FootstepVolumePolicy.ToGain(volumePercent);
        this.log = log;
        try
        {
            monoSamples = NavigationBeaconSound.LoadMonoSamples(soundPath, SampleRate);
            var fileLength = new FileInfo(soundPath).Length;
            log($"Navigation beacon sound loaded: path={soundPath}, bytes={fileLength}, samples={monoSamples.Length}, durationMs={monoSamples.Length * 1000 / SampleRate}.");
        }
        catch (Exception ex)
        {
            log($"Navigation beacon sound unavailable: path={soundPath}, error={ex.Message}. No synthetic fallback will be used.");
        }

        renderer = monoSamples is null
            ? null
            : SteamAudioNavigationBeaconRenderer.TryCreate(SampleRate, SteamAudioFrameSize, log);
        log($"Navigation beacon player initialized: volume={this.volumePercent}%, sound={(monoSamples is null ? "unavailable" : Path.GetFileName(soundPath))}, hrtf={(renderer is null ? "unavailable" : "Steam Audio")}.");
    }

    public bool Play(NavigationBeaconCue cue, float gainMultiplier = 1f)
    {
        var distanceGain = ObjectCueGainPolicy.Clamp(gainMultiplier);
        if (volume <= 0 || distanceGain <= 0f)
        {
            log($"Navigation beacon skipped: effective volume is 0, target={cue.TargetLabel}, direction={cue.Direction}.");
            return false;
        }

        if (monoSamples is null)
        {
            if (!missingSoundLogged)
            {
                missingSoundLogged = true;
                log("Navigation beacon skipped: the configured WAV asset is unavailable; no generated fallback will be used.");
            }

            return false;
        }

        if (renderer is null)
        {
            if (!missingHrtfLogged)
            {
                missingHrtfLogged = true;
                log("Navigation beacon skipped: Steam Audio HRTF backend is unavailable; no fake pan fallback will be used.");
            }

            return false;
        }

        try
        {
            float[] samples;
            lock (renderSync)
            {
                samples = renderer.Render(cue, monoSamples, Math.Min(volume, 4f) * distanceGain);
            }

            var stats = SampleStats.Calculate(samples);
            log($"Navigation beacon render: target={cue.TargetLabel}, samples={samples.Length}, peak={stats.Peak:0.000000}, rms={stats.Rms:0.000000}, volume={volumePercent}%, gain={distanceGain:0.000}.");
            var output = new WaveOutEvent
            {
                DesiredLatency = 80
            };
            var provider = new StereoFloatBufferSampleProvider(samples, SampleRate);
            var playback = new ActivePlayback(output, provider);
            output.PlaybackStopped += (_, args) =>
            {
                if (args.Exception is not null)
                {
                    log($"Navigation beacon playback stopped with error: {args.Exception.Message}");
                }

                RemovePlayback(playback);
            };
            output.Init(provider);
            lock (sync)
            {
                activePlaybacks.Add(playback);
            }

            output.Play();
            log($"Navigation beacon playback started: target={cue.TargetLabel}, direction={cue.Direction}.");
            log($"Navigation beacon played: target={cue.TargetLabel}, direction={cue.Direction}, steamAudio=({cue.SteamAudioX:0.00},{cue.SteamAudioY:0.00},{cue.SteamAudioZ:0.00}), distance={cue.DistanceUnits:0}.");
            return true;
        }
        catch (Exception ex)
        {
            log($"Navigation beacon failed: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        StopAll();
        renderer?.Dispose();
    }

    public void StopAll()
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

    private readonly record struct SampleStats(float Peak, double Rms)
    {
        public static SampleStats Calculate(IReadOnlyList<float> samples)
        {
            if (samples.Count == 0)
            {
                return new SampleStats(0f, 0d);
            }

            var peak = 0f;
            var sumSquares = 0d;
            foreach (var sample in samples)
            {
                var absolute = Math.Abs(sample);
                if (absolute > peak)
                {
                    peak = absolute;
                }

                sumSquares += sample * (double)sample;
            }

            return new SampleStats(peak, Math.Sqrt(sumSquares / samples.Count));
        }
    }

    private sealed class ActivePlayback : IDisposable
    {
        private readonly WaveOutEvent output;
        private readonly StereoFloatBufferSampleProvider provider;
        private bool disposed;

        public ActivePlayback(WaveOutEvent output, StereoFloatBufferSampleProvider provider)
        {
            this.output = output;
            this.provider = provider;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            _ = provider;
            output.Dispose();
        }
    }

    private sealed class StereoFloatBufferSampleProvider : ISampleProvider
    {
        private readonly float[] samples;
        private int position;

        public StereoFloatBufferSampleProvider(float[] samples, int sampleRate)
        {
            this.samples = samples;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var available = Math.Min(count, Math.Max(0, samples.Length - position));
            for (var index = 0; index < available; index++)
            {
                buffer[offset + index] = samples[position + index];
            }

            position += available;
            return available;
        }
    }

    private sealed unsafe class SteamAudioNavigationBeaconRenderer : IDisposable
    {
        private readonly int sampleRate;
        private readonly int frameSize;
        private IntPtr context;
        private IntPtr hrtf;
        private IntPtr effect;
        private SteamAudioNative.AudioBuffer inputBuffer;
        private SteamAudioNative.AudioBuffer outputBuffer;
        private readonly float[] interleavedFrame;
        private bool disposed;

        private SteamAudioNavigationBeaconRenderer(
            int sampleRate,
            int frameSize,
            IntPtr context,
            IntPtr hrtf,
            IntPtr effect,
            SteamAudioNative.AudioBuffer inputBuffer,
            SteamAudioNative.AudioBuffer outputBuffer)
        {
            this.sampleRate = sampleRate;
            this.frameSize = frameSize;
            this.context = context;
            this.hrtf = hrtf;
            this.effect = effect;
            this.inputBuffer = inputBuffer;
            this.outputBuffer = outputBuffer;
            interleavedFrame = new float[frameSize * 2];
        }

        public static SteamAudioNavigationBeaconRenderer? TryCreate(
            int sampleRate,
            int frameSize,
            Action<string> log)
        {
            try
            {
                var contextSettings = new SteamAudioNative.ContextSettings
                {
                    Version = SteamAudioNative.Version
                };
                ThrowIfError(SteamAudioNative.ContextCreate(in contextSettings, out var context), "context create");

                var audioSettings = new SteamAudioNative.AudioSettings
                {
                    SamplingRate = sampleRate,
                    FrameSize = frameSize
                };
                var hrtfSettings = new SteamAudioNative.HrtfSettings
                {
                    Type = SteamAudioNative.HrtfTypeDefault,
                    Volume = 1f,
                    NormType = SteamAudioNative.HrtfNormTypeNone
                };
                ThrowIfError(SteamAudioNative.HrtfCreate(context, in audioSettings, in hrtfSettings, out var hrtf), "HRTF create");

                var effectSettings = new SteamAudioNative.BinauralEffectSettings
                {
                    Hrtf = hrtf
                };
                ThrowIfError(SteamAudioNative.BinauralEffectCreate(context, in audioSettings, in effectSettings, out var effect), "binaural effect create");

                var inputBuffer = new SteamAudioNative.AudioBuffer();
                var outputBuffer = new SteamAudioNative.AudioBuffer();
                ThrowIfError(SteamAudioNative.AudioBufferAllocate(context, 1, frameSize, ref inputBuffer), "input buffer allocate");
                ThrowIfError(SteamAudioNative.AudioBufferAllocate(context, 2, frameSize, ref outputBuffer), "output buffer allocate");

                log("Navigation beacon Steam Audio HRTF backend initialized.");
                log($"Navigation beacon Steam Audio native binding: {SteamAudioNative.BindingName}.");
                return new SteamAudioNavigationBeaconRenderer(
                    sampleRate,
                    frameSize,
                    context,
                    hrtf,
                    effect,
                    inputBuffer,
                    outputBuffer);
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or InvalidOperationException)
            {
                log($"Navigation beacon Steam Audio HRTF backend unavailable: {ex.Message}");
                return null;
            }
        }

        public float[] Render(NavigationBeaconCue cue, float[] monoSamples, float volume)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(SteamAudioNavigationBeaconRenderer));
            }

            SteamAudioNative.BinauralEffectReset(effect);
            var output = new float[monoSamples.Length * 2];
            var outputOffset = 0;
            var inputOffset = 0;
            while (inputOffset < monoSamples.Length)
            {
                var framesThisBlock = Math.Min(frameSize, monoSamples.Length - inputOffset);
                FillInputBuffer(monoSamples, inputOffset, framesThisBlock);

                var effectParams = new SteamAudioNative.BinauralEffectParams
                {
                    Direction = new SteamAudioNative.Vector3(cue.SteamAudioX, cue.SteamAudioY, cue.SteamAudioZ),
                    Interpolation = SteamAudioNative.HrtfInterpolationBilinear,
                    SpatialBlend = 1f,
                    Hrtf = hrtf,
                    PeakDelays = IntPtr.Zero
                };

                SteamAudioNative.BinauralEffectApply(effect, in effectParams, in inputBuffer, ref outputBuffer);
                fixed (float* interleaved = interleavedFrame)
                {
                    SteamAudioNative.AudioBufferInterleave(context, in outputBuffer, interleaved);
                }

                for (var index = 0; index < framesThisBlock * 2; index++)
                {
                    output[outputOffset++] = Math.Clamp(interleavedFrame[index] * volume, -1f, 1f);
                }

                inputOffset += framesThisBlock;
            }

            NavigationBeaconSpatialEmphasis.Apply(output, cue, sampleRate);
            return output;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            SteamAudioNative.AudioBufferFree(context, ref inputBuffer);
            SteamAudioNative.AudioBufferFree(context, ref outputBuffer);
            SteamAudioNative.BinauralEffectRelease(ref effect);
            SteamAudioNative.HrtfRelease(ref hrtf);
            SteamAudioNative.ContextRelease(ref context);
        }

        private void FillInputBuffer(float[] mono, int inputOffset, int framesThisBlock)
        {
            var channelPointers = (IntPtr*)inputBuffer.Data;
            var input = (float*)channelPointers[0];
            for (var index = 0; index < framesThisBlock; index++)
            {
                input[index] = mono[inputOffset + index];
            }

            for (var index = framesThisBlock; index < frameSize; index++)
            {
                input[index] = 0f;
            }
        }

        private static void ThrowIfError(SteamAudioNative.Error error, string operation)
        {
            if (error != SteamAudioNative.Error.Success)
            {
                throw new InvalidOperationException($"Steam Audio {operation} failed: {error}.");
            }
        }

    }

    private static class NavigationBeaconSpatialEmphasis
    {
        public static void Apply(float[] interleavedStereo, NavigationBeaconCue cue, int sampleRate)
        {
            ApplyRearMarker(interleavedStereo, cue.StickY, sampleRate);
        }

        private static void ApplyRearMarker(float[] interleavedStereo, float stickY, int sampleRate)
        {
            var rearAmount = Math.Clamp(stickY, 0f, 1f);
            if (rearAmount < 0.35f)
            {
                return;
            }

            var frameCount = interleavedStereo.Length / 2;
            var dry = (float[])interleavedStereo.Clone();
            var filtered = new float[interleavedStereo.Length];
            var lowPassLeft = 0f;
            var lowPassRight = 0f;
            const float lowPassCoefficient = 0.18f;
            var darkBlend = 0.45f * rearAmount;
            for (var frame = 0; frame < frameCount; frame++)
            {
                var left = frame * 2;
                lowPassLeft += (dry[left] - lowPassLeft) * lowPassCoefficient;
                lowPassRight += (dry[left + 1] - lowPassRight) * lowPassCoefficient;
                filtered[left] = lowPassLeft;
                filtered[left + 1] = lowPassRight;
                interleavedStereo[left] = dry[left] * (1f - darkBlend) + lowPassLeft * darkBlend;
                interleavedStereo[left + 1] = dry[left + 1] * (1f - darkBlend) + lowPassRight * darkBlend;
            }

            AddDelayedRearPulse(interleavedStereo, filtered, sampleRate, delayMs: 10, gain: 0.45f * rearAmount, decayMs: 32);
            AddDelayedRearPulse(interleavedStereo, filtered, sampleRate, delayMs: 28, gain: 1.85f * rearAmount, decayMs: 58);
        }

        private static void AddDelayedRearPulse(
            float[] output,
            float[] filteredSource,
            int sampleRate,
            int delayMs,
            float gain,
            int decayMs)
        {
            var delayFrames = Math.Max(1, sampleRate * delayMs / 1000);
            var decayFrames = Math.Max(1, sampleRate * decayMs / 1000);
            var frameCount = output.Length / 2;
            for (var frame = delayFrames; frame < frameCount; frame++)
            {
                var markerFrame = frame - delayFrames;
                if (markerFrame >= decayFrames)
                {
                    break;
                }

                var attack = Math.Min(1f, markerFrame / (sampleRate * 0.003f));
                var decay = 1f - markerFrame / (float)decayFrames;
                var envelope = attack * decay * gain;
                var source = markerFrame * 2;
                var destination = frame * 2;
                output[destination] = Math.Clamp(
                    output[destination] + filteredSource[source] * envelope,
                    -1f,
                    1f);
                output[destination + 1] = Math.Clamp(
                    output[destination + 1] + filteredSource[source + 1] * envelope,
                    -1f,
                    1f);
            }
        }
    }

}
