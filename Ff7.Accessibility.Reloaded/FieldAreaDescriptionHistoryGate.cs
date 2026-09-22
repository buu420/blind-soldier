namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The one place either runtime asks whether a room description has already been heard in
/// this playthrough, and the one place it records that it has.
///
/// <para>Only room descriptions are gated. A room is a cue whose native anchor is the
/// field's own area-name opcode; everything else a field describes - a scene, an action, a
/// movie - keeps happening at its own native event every time, because those are events and
/// a room is a place. The explicit status command is not routed through here at all, so a
/// player who asks what room they are in always gets an answer.</para>
///
/// <para>Both ends are gated on purpose. Filtering only at the queue would let a
/// description that was already queued - the field's own anchor and the cold-start
/// fallback can both produce one - speak after the other had already been accepted. And the
/// commit happens only when output accepts the text: a narration track that would not play
/// or a screen reader that refused the call has not told the player anything, and spending
/// the room on it would silence it for the rest of the save.</para>
/// </summary>
public sealed class FieldAreaDescriptionHistoryGate
{
    private readonly FieldAreaDescriptionHistory? history;

    /// <param name="history">
    /// The playthrough's history, or null when persistent room descriptions are switched
    /// off. A null history gates nothing and records nothing, which is exactly the
    /// behaviour every field had before this existed.
    /// </param>
    public FieldAreaDescriptionHistoryGate(FieldAreaDescriptionHistory? history)
    {
        this.history = history;
    }

    /// <summary>Whether this cue is a room description rather than an event description.</summary>
    public static bool IsRoomCue(FieldCutsceneDescriptionCue cue) =>
        cue.Opcode == FieldOpcodeAddressResolver.OpcodeMapNameIndex;

    /// <summary>
    /// Whether this cue should be offered to the player at all. False only for a room
    /// this playthrough has already heard; the caller drops it without reserving speech,
    /// so a known room never delays dialogue behind it.
    /// </summary>
    public bool ShouldOffer(FieldCutsceneDescriptionCue cue) =>
        history is null || !IsRoomCue(cue) || !history.HasHeard(cue.FieldId);

    /// <summary>
    /// Records that output accepted this room. Called after delivery, never before, and
    /// ignored for anything that is not a room.
    /// </summary>
    public void NoteSpoken(FieldCutsceneDescriptionCue cue)
    {
        if (history is not null && IsRoomCue(cue))
        {
            history.MarkHeard(cue.FieldId);
        }
    }
}
