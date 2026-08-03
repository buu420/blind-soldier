using NAudio.Wave;

namespace Ff7.Accessibility.Reloaded;

public static class NavigationBeaconSound
{
    public static float[] LoadMonoSamples(string path, int expectedSampleRate)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Navigation beacon sound was not found.", path);
        }

        using var reader = new WaveFileReader(path);
        if (reader.WaveFormat.SampleRate != expectedSampleRate)
        {
            throw new InvalidDataException(
                $"Navigation beacon sound must be {expectedSampleRate} Hz, got {reader.WaveFormat.SampleRate} Hz.");
        }

        var channels = reader.WaveFormat.Channels;
        if (channels < 1)
        {
            throw new InvalidDataException("Navigation beacon sound has no audio channels.");
        }

        var interleaved = new List<float>();
        var buffer = new float[4096 * channels];
        var provider = reader.ToSampleProvider();
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            interleaved.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        if (interleaved.Count == 0)
        {
            throw new InvalidDataException("Navigation beacon sound contains no samples.");
        }

        if (channels == 1)
        {
            return interleaved.ToArray();
        }

        var frameCount = interleaved.Count / channels;
        var mono = new float[frameCount];
        for (var frame = 0; frame < frameCount; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < channels; channel++)
            {
                sum += interleaved[frame * channels + channel];
            }

            mono[frame] = sum / channels;
        }

        return mono;
    }
}
