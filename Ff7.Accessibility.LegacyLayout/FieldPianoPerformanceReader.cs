using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <param name="IsPlaying">Both of the piano's native note and chord threads are running.</param>
/// <param name="FieldId">The field that was read.</param>
public readonly record struct FieldPianoPerformance(bool IsPlaying, int FieldId);

/// <summary>
/// Whether one of the game's two pianos is being played right now, read from the field
/// script engine's own threads rather than from the room or a busy flag.
///
/// <para>Both pianos work the same way (installed flevel, byte-identical in the legacy and
/// Steam 2026 archives). Tifa's, niv_ti2 (field 287): the [OK] handler of entity 17
/// "piano" REQSWs entity 18 "molody" and entity 19 "code" to script 2 at priority 6, then
/// polls IFKEY Start; Start REQSWs both to script 3, a RETTO. The Shinra Mansion's,
/// sinin1_2 (field 298): entity 10 "plin0" REQs entity 11 "molody" and entity 12 "code" to
/// script 2 at priority 6, polls IFKEYON Start, and REQs both to script 3. Script 2 of
/// "molody" is the note loop (the four face buttons, L1 or R1 held for the upper octave);
/// script 2 of "code" is the chord loop (the D-pad).</para>
///
/// <para>Both loops running at once is the performance. The room is not: players walk and
/// navigate in it. UC 1 is not either: the whole interaction holds it, its messages and
/// choices included, and so does every cutscene.</para>
///
/// <para>The entity numbers are only trusted while the loaded script is the native one: the
/// right field, its own entity count, and the native names at both entities. Each thread
/// is read by <see cref="FieldScriptControllerReader.TryReadRunningScript"/> from two
/// agreeing captures, so a torn or unreadable frame is reported as unreadable rather than
/// as a performance or as the end of one.</para>
/// </summary>
public sealed class FieldPianoPerformanceReader
{
    /// <summary>Script 2 of both threads is the performance loop; script 3 ends it.</summary>
    public const int PerformanceScript = 2;

    private static readonly NativePiano[] Pianos =
    [
        new(FieldId: 287, EntityCount: 22, NoteEntity: 18, ChordEntity: 19),
        new(FieldId: 298, EntityCount: 16, NoteEntity: 11, ChordEntity: 12),
    ];

    private static readonly byte[] NoteEntityName = "molody"u8.ToArray();
    private static readonly byte[] ChordEntityName = "code"u8.ToArray();

    private readonly ILegacyAddressSpace memory;
    private readonly FieldScriptControllerReader scripts;

    public FieldPianoPerformanceReader(ILegacyAddressSpace memory)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        scripts = new FieldScriptControllerReader(memory);
    }

    /// <summary>
    /// False only when the frame could not be read. Outside the field module, in any other
    /// field, and in a piano room whose script is not the native one, the answer is a
    /// readable "not playing".
    /// </summary>
    public bool TryRead(out FieldPianoPerformance performance)
    {
        performance = default;
        if (!memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            !memory.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var fieldId))
        {
            return false;
        }

        performance = new FieldPianoPerformance(false, fieldId);
        if (module != FieldPositionReader.FieldModule || Find(fieldId) is not { } piano)
        {
            return true;
        }

        if (!TryReadNativeScript(piano, out var isNative))
        {
            return false;
        }

        if (!isNative)
        {
            return true;
        }

        if (!scripts.TryReadRunningScript(fieldId, piano.NoteEntity, out _, out var noteScript) ||
            !scripts.TryReadRunningScript(fieldId, piano.ChordEntity, out _, out var chordScript))
        {
            return false;
        }

        performance = new FieldPianoPerformance(
            noteScript == PerformanceScript && chordScript == PerformanceScript, fieldId);
        return true;
    }

    private static NativePiano? Find(int fieldId)
    {
        foreach (var piano in Pianos)
        {
            if (piano.FieldId == fieldId)
            {
                return piano;
            }
        }

        return null;
    }

    private bool TryReadNativeScript(NativePiano piano, out bool isNative)
    {
        isNative = false;
        var namesLength = (uint)(FieldActivityStateReader.ScriptHeaderEntityNamesOffset +
            (piano.EntityCount * FieldActivityStateReader.ScriptHeaderEntityNameLength));
        if (!memory.TryReadUInt32((uint)FieldScriptControllerReader.AddressFieldScriptPointer, out var section) ||
            section == 0 ||
            section > uint.MaxValue - namesLength ||
            !memory.TryReadByte(section + FieldScriptControllerReader.FieldScriptEntityCountOffset, out var entityCount))
        {
            return false;
        }

        if (entityCount != piano.EntityCount)
        {
            return true;
        }

        if (!TryMatchName(section, piano.NoteEntity, NoteEntityName, out var notesMatch) ||
            !TryMatchName(section, piano.ChordEntity, ChordEntityName, out var chordsMatch))
        {
            return false;
        }

        isNative = notesMatch && chordsMatch;
        return true;
    }

    /// <summary>The entity's eight-byte, NUL-padded name in the script section header.</summary>
    private bool TryMatchName(uint section, int entity, byte[] expected, out bool matches)
    {
        matches = false;
        Span<byte> name = stackalloc byte[FieldActivityStateReader.ScriptHeaderEntityNameLength];
        if (!memory.TryRead(
                section + (uint)(FieldActivityStateReader.ScriptHeaderEntityNamesOffset +
                    (entity * FieldActivityStateReader.ScriptHeaderEntityNameLength)),
                name))
        {
            return false;
        }

        matches = name[..expected.Length].SequenceEqual(expected) &&
            !name[expected.Length..].ContainsAnyExcept((byte)0);
        return true;
    }

    private readonly record struct NativePiano(int FieldId, int EntityCount, int NoteEntity, int ChordEntity);
}
