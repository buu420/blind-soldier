namespace Ff7.Accessibility.Reloaded;

public sealed class CosmoFootstepSequencer
{
    private const int FootstepSfxId = 159;

    private readonly CosmoFootstepConfig config;
    private readonly IReadOnlyDictionary<int, string> fieldNames;
    private readonly string sfxDirectory;
    private readonly Dictionary<string, int> sequenceOffsets = new(StringComparer.OrdinalIgnoreCase);

    public CosmoFootstepSequencer(
        CosmoFootstepConfig config,
        IReadOnlyDictionary<int, string> fieldNames,
        string sfxDirectory)
    {
        this.config = config;
        this.fieldNames = fieldNames;
        this.sfxDirectory = sfxDirectory;
    }

    public CosmoFootstepSelection SelectNext(FieldPositionSnapshot position) =>
        TrySelectNext(position, out var selection) ? selection : default;

    public bool TrySelectNext(FieldPositionSnapshot position, out CosmoFootstepSelection selection)
    {
        foreach (var trackName in EnumerateTrackNames(position))
        {
            if (TrySelectFromTrack(trackName, out selection))
            {
                return true;
            }
        }

        selection = default;
        return false;
    }

    public CosmoFootstepSelection SelectNext(WorldMapStateSnapshot position) =>
        TrySelectNext(position, out var selection) ? selection : default;

    public bool TrySelectNext(WorldMapStateSnapshot position, out CosmoFootstepSelection selection)
    {
        foreach (var trackName in EnumerateTrackNames(position))
        {
            if (TrySelectFromTrack(trackName, out selection))
            {
                return true;
            }
        }

        selection = default;
        return false;
    }

    public bool TrySelectProbe(out CosmoFootstepSelection selection)
    {
        selection = default;
        return false;
    }

    private bool TrySelectFromTrack(string trackName, out CosmoFootstepSelection selection)
    {
        if (!config.TryGetSequence(trackName, out var sequence))
        {
            selection = default;
            return false;
        }

        if (sequence.Count == 0)
        {
            selection = new CosmoFootstepSelection(trackName, 0, string.Empty);
            return true;
        }

        sequenceOffsets.TryGetValue(trackName, out var offset);
        var soundId = sequence[offset % sequence.Count];
        sequenceOffsets[trackName] = offset + 1;
        selection = new CosmoFootstepSelection(trackName, soundId, Path.Combine(sfxDirectory, $"{soundId}.ogg"));
        return true;
    }

    private IEnumerable<string> EnumerateTrackNames(FieldPositionSnapshot position)
    {
        if (fieldNames.TryGetValue(position.FieldId, out var fieldName) && !string.IsNullOrWhiteSpace(fieldName))
        {
            yield return $"{fieldName}_{position.TriangleId}_{FootstepSfxId}";
            yield return $"{fieldName}_{FootstepSfxId}";
        }
    }

    private static IEnumerable<string> EnumerateTrackNames(WorldMapStateSnapshot position)
    {
        yield return $"wm_footsteps_{position.PlayerModelId}_{position.TerrainId}_{FootstepSfxId}";
        yield return $"wm_footsteps_{position.TerrainId}_{FootstepSfxId}";
    }
}

public readonly record struct CosmoFootstepSelection(string TrackName, int SoundId, string Path)
{
    public bool IsSilent => SoundId <= 0 || Path.Length == 0;
}
