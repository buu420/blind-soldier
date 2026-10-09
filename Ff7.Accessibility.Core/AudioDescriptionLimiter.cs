namespace Ff7.Accessibility.Core;

/// <summary>
/// The gain stage of one recorded description: it applies the player's level and keeps every
/// sample within <see cref="Ceiling"/>, below digital full scale.
///
/// <para>The recordings are already peak-normalised to about -1.7 dBFS, so plain linear gain
/// clips past about 122 percent. A level change glides over <see cref="GlideSeconds"/> rather
/// than jumping, so turning a recording up or down while it plays does not click. The limiter
/// has an instant attack and no look-ahead: a frame whose boosted peak would pass the ceiling
/// is scaled down to it on that very frame, which is what guarantees the bound, and the
/// reduction recovers over <see cref="ReleaseSeconds"/>. The channels of a frame are scaled
/// together, keeping the stereo image. Nothing is delayed, added or dropped, so a recording's
/// timing is untouched.</para>
///
/// <para>It is a limiter, not a transparent amplifier: a strongly boosted recording is
/// audibly compressed and its loudest syllables are flattened. Below the ceiling the output
/// is exactly the input times the gain.</para>
/// </summary>
public sealed class AudioDescriptionLimiter
{
    /// <summary>The largest sample magnitude passed on: about -0.45 dBFS.</summary>
    public const float Ceiling = 0.95f;

    /// <summary>How long a level change takes to arrive.</summary>
    public const double GlideSeconds = 0.02;

    /// <summary>How long a reduction takes to recover by a factor of e.</summary>
    public const double ReleaseSeconds = 0.08;

    private readonly float release;
    private readonly int glideFrames;
    private float envelope;
    private float target = float.NaN;
    private float glideStep;
    private int glideRemaining;

    public AudioDescriptionLimiter(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        Channels = channels;
        release = (float)Math.Exp(-1.0 / (ReleaseSeconds * sampleRate));
        glideFrames = Math.Max(1, (int)Math.Round(GlideSeconds * sampleRate));
    }

    public int Channels { get; }

    /// <summary>The gain applied to the last frame processed.</summary>
    public float CurrentGain { get; private set; }

    /// <summary>Frames turned down to keep within the ceiling. Diagnostic.</summary>
    public long LimitedFrames { get; private set; }

    /// <summary>The smallest factor any frame was turned down by; 1 when none was. Diagnostic.</summary>
    public float DeepestReduction { get; private set; } = 1.0f;

    /// <summary>
    /// Applies <paramref name="targetGain"/> to interleaved samples in place, limited. The
    /// first block plays at its gain from its first sample; a later change glides there.
    /// </summary>
    public void Process(Span<float> interleaved, float targetGain)
    {
        if (!float.IsFinite(targetGain) || targetGain < 0)
        {
            targetGain = float.IsNaN(target) ? 1.0f : target;
        }

        if (float.IsNaN(target))
        {
            target = targetGain;
            CurrentGain = targetGain;
        }
        else if (targetGain != target)
        {
            target = targetGain;
            glideRemaining = glideFrames;
            glideStep = (targetGain - CurrentGain) / glideFrames;
        }

        for (var start = 0; start < interleaved.Length; start += Channels)
        {
            var frame = interleaved.Slice(start, Math.Min(Channels, interleaved.Length - start));
            if (glideRemaining > 0)
            {
                glideRemaining--;
                CurrentGain = glideRemaining == 0 ? target : CurrentGain + glideStep;
            }

            var peak = 0.0f;
            for (var i = 0; i < frame.Length; i++)
            {
                var sample = frame[i] * CurrentGain;
                if (!float.IsFinite(sample))
                {
                    // A sample that is not a number is silenced rather than passed to the device.
                    sample = 0.0f;
                }

                frame[i] = sample;
                peak = Math.Max(peak, Math.Abs(sample));
            }

            envelope = Math.Max(peak, envelope * release);
            if (envelope > Ceiling)
            {
                var reduction = Ceiling / envelope;
                for (var i = 0; i < frame.Length; i++)
                {
                    frame[i] *= reduction;
                }

                LimitedFrames++;
                DeepestReduction = Math.Min(DeepestReduction, reduction);
            }
        }
    }
}
