namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Delivers an area description for a field whose own entry anchor was never seen.
///
/// The area descriptions run from MPNAM, which every field executes once from its
/// director's init. That is the right anchor while the mod is already attached, but
/// it is missed outright in two ordinary cases: the mod attaching to a game that is
/// already standing in a room, and a save loaded straight into one. In both the
/// player arrives somewhere and is told nothing about it, which is the failure this
/// project treats as worse than a crash.
///
/// This watches the plain field id instead. Once the same field has been current and
/// readable for a short settling period, and the field's own anchor has not been
/// observed during this visit, the area description is offered through the ordinary
/// description queue - so it still waits behind native dialogue, still retries until
/// the speaker actually accepts it, and still happens exactly once per visit.
/// </summary>
public sealed class FieldAreaDescriptionColdStartTracker
{
    /// <summary>
    /// How long the field has to stay put before a missed anchor is assumed. Long
    /// enough that an ordinary entry's own MPNAM wins the race comfortably, short
    /// enough that the player is not left in silence.
    /// </summary>
    public static readonly TimeSpan SettlingWindow = TimeSpan.FromSeconds(2);

    private readonly Dictionary<int, FieldCutsceneDescriptionCue> areaCues;
    private readonly object sync = new();

    private int currentFieldId = -1;
    private DateTime fieldSinceUtc;
    private bool anchorSeenThisVisit;
    private bool deliveredThisVisit;

    // Both runtimes drain the native opcode hook before they observe the field:
    // the x86 monitor runs its deferred hook events before the description tick, and
    // the x64 worker drains its ingress queue before observing the stable field. So a
    // perfectly ordinary entry delivers the field's own anchor while this tracker is
    // still on the previous field, and an anchor that was simply dropped there would
    // leave the visit looking unanchored and produce a second, duplicate description
    // two seconds later. The anchor is therefore held until the matching field
    // actually becomes current, and consumed exactly once when it does.
    private int pendingAnchorFieldId = -1;

    public FieldAreaDescriptionColdStartTracker(IEnumerable<FieldCutsceneDescriptionCue> areaCues)
    {
        ArgumentNullException.ThrowIfNull(areaCues);
        this.areaCues = areaCues
            .Where(cue => cue.Opcode == FieldOpcodeAddressResolver.OpcodeMapNameIndex)
            .GroupBy(cue => cue.FieldId)
            .ToDictionary(group => group.Key, group => group.First());
    }

    /// <summary>
    /// Records that the field's own entry anchor really did run. The anchor usually
    /// arrives before this tracker has been told about the new field, so one that is
    /// not for the current field is held until that field becomes current.
    /// </summary>
    public void NoteNativeAnchor(int fieldId)
    {
        lock (sync)
        {
            if (fieldId < 0)
            {
                return;
            }

            if (fieldId == currentFieldId)
            {
                anchorSeenThisVisit = true;
                deliveredThisVisit = true;
                pendingAnchorFieldId = -1;
                return;
            }

            // One slot, replaced by the next anchor, so a field the player never
            // reaches cannot suppress a genuinely unanchored visit indefinitely.
            pendingAnchorFieldId = fieldId;
        }
    }

    /// <summary>
    /// Returns the area description to queue, or null. Only a field that has been
    /// stable for the settling window without its own anchor produces one, and only
    /// once per visit.
    /// </summary>
    public FieldCutsceneDescriptionCue? Observe(int module, int fieldId, DateTime nowUtc)
    {
        lock (sync)
        {
            if (module != FieldPositionReader.FieldModule || fieldId < 0)
            {
                // The visit is over, but a just-delivered anchor is still owed to the
                // field it belongs to: dropping it here would recreate the duplicate
                // this tracker exists to avoid.
                ResetVisit();
                return null;
            }

            if (fieldId != currentFieldId)
            {
                currentFieldId = fieldId;
                fieldSinceUtc = nowUtc;

                // The anchor for this very field normally arrived a moment ago, from
                // the hook drain that runs before this observation. Consume it here,
                // so an ordinary entry is anchored rather than looking cold.
                anchorSeenThisVisit = pendingAnchorFieldId == fieldId;
                deliveredThisVisit = anchorSeenThisVisit;
                pendingAnchorFieldId = -1;
                return null;
            }

            if (deliveredThisVisit ||
                anchorSeenThisVisit ||
                nowUtc - fieldSinceUtc < SettlingWindow ||
                !areaCues.TryGetValue(fieldId, out var cue))
            {
                return null;
            }

            deliveredThisVisit = true;
            return cue;
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            ResetVisit();
            pendingAnchorFieldId = -1;
        }
    }

    private void ResetVisit()
    {
        currentFieldId = -1;
        fieldSinceUtc = default;
        anchorSeenThisVisit = false;
        deliveredThisVisit = false;
    }
}
