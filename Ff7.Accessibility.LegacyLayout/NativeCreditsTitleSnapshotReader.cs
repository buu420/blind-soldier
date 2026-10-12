namespace Ff7.Accessibility.LegacyLayout;

public enum NativeCreditsTitleRenderer { LowResolution, HighResolution }

/// <summary>Copied player-visible caption evidence; contains no native pointer.</summary>
public sealed record NativeCaptionObservation(string Text, DateTime ObservedUtc);

/// <summary>The native post-credit title's exact draw gates; not a phase-based timer.</summary>
public sealed class NativeCreditsTitleSnapshotReader
{
    public const string CaptionText = "The words, Five hundred years later, appear.";
    public const uint AddressTitleObject = 0x00F4F404;
    public const uint AddressResolutionSelector = 0x00F4F43C;
    public const uint AddressRemainingFadeTiles = 0x00F4F5B8;

    /// <summary>Revalidates a recent renderer observation; this never produces caption evidence on its own.</summary>
    public bool IsCurrentTitleVisible(ILegacyAddressSpace memory) =>
        ReadState(memory, NativeCreditsTitleRenderer.LowResolution) is not null
        || ReadState(memory, NativeCreditsTitleRenderer.HighResolution) is not null;

    // 007ACCAD / 007AD064 draw after_1.tex. A phase-one title can still be
    // covered by fade tiles. The actual selected draw must run with zero tiles
    // both before and after, with the same coherent object and phase.
    internal NativeCaptionObservation? ObserveRendered(ILegacyAddressSpace memory,
        NativeCreditsTitleRenderer renderer, Action original, Func<bool> captureStillActive)
    {
        var before = ReadState(memory, renderer);
        original(); // Read failures must never prevent or duplicate the native renderer.
        if (!captureStillActive()) return null;
        var after = ReadState(memory, renderer);
        return before is not null && before == after
            ? new NativeCaptionObservation(CaptionText, DateTime.UtcNow) : null;
    }

    private static TitleState? ReadState(ILegacyAddressSpace memory, NativeCreditsTitleRenderer renderer)
    {
        try
        {
            return TryReadState(memory, renderer, out var first)
                && TryReadState(memory, renderer, out var second) && first == second ? first : null;
        }
        catch { return null; }
    }

    private static bool TryReadState(ILegacyAddressSpace memory, NativeCreditsTitleRenderer renderer,
        out TitleState state)
    {
        state = default;
        if (memory is null || renderer is not (NativeCreditsTitleRenderer.LowResolution or NativeCreditsTitleRenderer.HighResolution)
            || !memory.TryReadByte(NativeCreditsSnapshotReader.AddressCurrentModule, out var module) || module != 28
            || !memory.TryReadInt32(NativeCreditsSnapshotReader.AddressCreditMode, out var mode) || mode != 1
            || !memory.TryReadInt32(NativeCreditsSnapshotReader.AddressCreditPhase, out var phase) || phase is not (1 or 2)
            || !memory.TryReadInt32(AddressResolutionSelector, out var resolution)
            || resolution != (renderer == NativeCreditsTitleRenderer.LowResolution ? 1 : 0)
            || !memory.TryReadUInt32(AddressTitleObject, out var titleObject) || titleObject == 0
            || !memory.TryReadInt32(AddressRemainingFadeTiles, out var tiles) || tiles != 0)
            return false;
        state = new TitleState(phase, resolution, titleObject);
        return true;
    }

    // Kept entirely inside the reader: no pointer escapes into the worker observation.
    private readonly record struct TitleState(int Phase, int Resolution, uint TitleObject);
}
