using NAudio.Wave;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Ff7BeaconAssetBuilder <source 214.wav> <destination wav>");
    return 2;
}

const int outputSampleRate = 44100;
var sourcePath = Path.GetFullPath(args[0]);
var destinationPath = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

using var source = new WaveFileReader(sourcePath);
using var pcm = WaveFormatConversionStream.CreatePcmStream(source);
if (pcm.WaveFormat.SampleRate != outputSampleRate || pcm.WaveFormat.Channels != 1)
{
    throw new InvalidOperationException(
        $"Expected mono 44.1 kHz source after ADPCM decode, got {pcm.WaveFormat.SampleRate} Hz, {pcm.WaveFormat.Channels} channels.");
}

var decoded = ReadAllSamples(pcm.ToSampleProvider());
var remixed = Remix(decoded, outputSampleRate);
using (var writer = new WaveFileWriter(destinationPath, new WaveFormat(outputSampleRate, 16, 1)))
{
    foreach (var sample in remixed)
    {
        writer.WriteSample(sample);
    }
}

Console.WriteLine($"Decoded {decoded.Length} samples and wrote {remixed.Length} remixed samples to {destinationPath}");
return 0;

static float[] ReadAllSamples(ISampleProvider provider)
{
    var result = new List<float>();
    var buffer = new float[4096];
    int read;
    while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
    {
        result.AddRange(buffer.AsSpan(0, read).ToArray());
    }

    return result.ToArray();
}

static float[] Remix(IReadOnlyList<float> decoded, int sampleRate)
{
    const float leadingThreshold = 0.012f;
    const float trailingThreshold = 0.004f;
    var first = 0;
    while (first < decoded.Count && Math.Abs(decoded[first]) < leadingThreshold)
    {
        first++;
    }

    var last = decoded.Count - 1;
    while (last > first && Math.Abs(decoded[last]) < trailingThreshold)
    {
        last--;
    }

    if (first >= decoded.Count)
    {
        throw new InvalidOperationException("Decoded 214.wav contains no audible samples.");
    }

    var sourceLength = last - first + 1;
    var delaySamples = sampleRate * 17 / 1000;
    var minimumLength = sampleRate * 160 / 1000;
    var outputLength = Math.Max(minimumLength, sourceLength + delaySamples);
    var output = new float[outputLength];
    for (var index = 0; index < outputLength; index++)
    {
        var dry = Read(index);
        var resonance = Read(index - delaySamples) * 0.32f;
        var color = Read(index - 7) * 0.15f - Read(index - 19) * 0.08f;
        var shaped = MathF.Tanh((dry + resonance + color) * 1.22f);
        output[index] = shaped;
    }

    var fadeSamples = sampleRate * 28 / 1000;
    for (var index = Math.Max(0, output.Length - fadeSamples); index < output.Length; index++)
    {
        var remaining = output.Length - index;
        output[index] *= remaining / (float)fadeSamples;
    }

    var peak = output.Select(Math.Abs).DefaultIfEmpty().Max();
    if (peak <= 0f)
    {
        throw new InvalidOperationException("Remixed 214.wav contains no audible samples.");
    }

    var gain = 0.92f / peak;
    for (var index = 0; index < output.Length; index++)
    {
        output[index] *= gain;
    }

    return output;

    float Read(int index) =>
        index >= 0 && index < sourceLength
            ? decoded[first + index]
            : 0f;
}
