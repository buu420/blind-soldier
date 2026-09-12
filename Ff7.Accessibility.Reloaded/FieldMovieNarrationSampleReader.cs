using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Builds a <see cref="FieldMovieNarrationSample"/> from a guest address space.
///
/// This exists because the sample has to be taken at two different instants that
/// mean different things. The MOVIE handler's own state is only meaningful
/// *before* the native original runs: FUN_0061A321 turns a fresh 0 into a 4 on that
/// very call, so a sample taken when a queued opcode is later drained reports every
/// genuine first film as a repeat. The film's active flag and number, by contrast,
/// are wanted live when deciding whether a track may still start or must stop.
///
/// Both runtimes therefore capture one sample at the native boundary and read
/// another when they act, and both use this one reader so the two agree.
/// </summary>
public static class FieldMovieNarrationSampleReader
{
    /// <summary>
    /// Reads a sample for <paramref name="fieldId"/>. Returns false when the film
    /// state itself is unreadable; an unreadable *handler* state is not a failure,
    /// it is reported as unknown so the caller falls back to its own bookkeeping
    /// rather than treating a failed read as a fresh entry.
    /// </summary>
    public static bool TryRead(
        ILegacyAddressSpace memory,
        int fieldId,
        out FieldMovieNarrationSample sample)
    {
        sample = default;
        if (memory is null)
        {
            return false;
        }

        if (!memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            !memory.TryReadUInt16((uint)FieldAudibleCueStateReader.AddressFieldMovieActive, out var movieActive) ||
            !memory.TryReadUInt16((uint)FieldAudibleCueStateReader.AddressFieldMovieNumber, out var movieNumber))
        {
            return false;
        }

        TryReadHandlerState(memory, out var handlerState, out var handlerPhase);

        // Both are byte reads, and both must fail closed. An unreadable command byte
        // means the argument word cannot be trusted as a film number; an unreadable
        // skip gate means we do not know whether the player is being shown anything.
        var command = memory.TryReadByte(
            (uint)FieldAudibleCueStateReader.AddressFieldMovieCommand, out var commandByte)
            ? commandByte
            : FieldMovieNarrationSample.CommandUnknown;
        var skipped = memory.TryReadByte(
            (uint)FieldAudibleCueStateReader.AddressFieldMoviesSkipped, out var skipByte)
            ? skipByte
            : 1;

        sample = new FieldMovieNarrationSample(
            MovieActive: movieActive != 0,
            MovieNumber: movieNumber,
            CurrentModule: module,
            CurrentFieldId: fieldId,
            MovieHandlerState: handlerState,
            MovieHandlerPhase: handlerPhase,
            Disc: ReadDisc(memory),
            MovieCommand: command,
            MoviesSkipped: skipped,
            MovieFrame: memory.TryReadUInt16(
                (uint)FieldAudibleCueStateReader.AddressFieldMovieFrame, out var frame)
                ? frame
                : FieldMovieNarrationSample.FrameUnknown);
        return true;
    }

    /// <summary>
    /// The disc the game would resolve a film number against, or
    /// <see cref="MovieFilmNameResolver.DiscUnknown"/> when it cannot be read. A
    /// failed read is not evidence that the disc changed, so it leaves the film
    /// number check in charge rather than silencing every description.
    /// </summary>
    public static int ReadDisc(ILegacyAddressSpace memory) =>
        memory is not null &&
        memory.TryReadByte((uint)MovieFilmNameResolver.AddressMovieDisc, out var disc)
            ? disc
            : MovieFilmNameResolver.DiscUnknown;

    /// <summary>
    /// The handler's state byte and phase word, through the field script context
    /// pointer. Both come back unknown when the pointer is not a plausible guest
    /// address or either read fails.
    /// </summary>
    public static bool TryReadHandlerState(
        ILegacyAddressSpace memory,
        out int state,
        out int phase)
    {
        state = FieldMovieNarrationPolicy.MovieHandlerStateUnknown;
        phase = FieldMovieNarrationPolicy.MovieHandlerStateUnknown;
        if (memory is null ||
            !memory.TryReadUInt32(
                (uint)FieldAudibleCueStateReader.AddressFieldScriptContextPointer, out var context) ||
            !IsPlausibleGuestPointer(context) ||
            !memory.TryReadByte(
                context + FieldAudibleCueStateReader.FieldScriptContextStateOffset, out var stateByte) ||
            !memory.TryReadInt16(
                context + FieldAudibleCueStateReader.FieldScriptContextPhaseOffset, out var phaseWord))
        {
            return false;
        }

        state = stateByte;
        phase = phaseWord;
        return true;
    }

    public static bool IsPlausibleGuestPointer(uint pointer) =>
        pointer >= 0x00010000u && pointer <= 0x7FFF0000u;
}
