using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Gathers exactly the live state the current field's activity needs, and nothing else,
/// for both runtimes. Anything that cannot be read is reported as unreadable rather than
/// being left out, so the readout can tell an empty room from a failed look.
///
/// <para>The Steam 2026 runtime never built this observation at all, so none of the
/// activities spoke there: the Temple clock's hands and bridges, the rolling corridor,
/// the chase chamber, the pillars, the altar, the dig, the Corel handcar, the cliff, the
/// wind and the Junon rooms were silent on the one runtime the user plays. The reads are
/// the shared readers both runtimes already hold; only the four values that are plain
/// bytes - the game moment, the pillar gate, the temporary bank and whether the player
/// has control - are supplied by the host, because each host reads raw memory its own
/// way.</para>
/// </summary>
public static class FieldActivityObservationBuilder
{
    public static FieldActivityObservation Build(
        FieldPositionSnapshot position,
        FieldActivityStateReader stateReader,
        FieldScriptLineStateReader? lineStateReader,
        FieldBoundaryStateReader? boundaryStateReader,
        FieldNavigationControlReadResult control,
        bool isPlayerControlled,
        int gameMoment,
        int pillarGate,
        Func<int, int>? readTemporaryByte)
    {
        ArgumentNullException.ThrowIfNull(stateReader);
        var fieldId = position.FieldId;
        var models = new List<FieldActivityModelReading>();
        foreach (var entityId in FieldActivityReadout.ObservedEntities(fieldId))
        {
            models.Add(stateReader.ReadModel(fieldId, entityId));
        }

        var waits = new Dictionary<int, FieldActivityWaitState>();
        foreach (var entityId in FieldActivityReadout.ObservedWaitEntities(fieldId))
        {
            waits[entityId] = stateReader.ReadWaitState(
                fieldId,
                entityId,
                FieldActivityReadout.WaitAnchors(fieldId, entityId));
        }

        Func<int, bool>? isLineEnabled = null;
        if (lineStateReader is not null &&
            FieldActivityReadout.ObservedLineEntities(fieldId).Count > 0)
        {
            var enabled = new Dictionary<int, bool>();
            var readable = true;
            foreach (var entityId in FieldActivityReadout.ObservedLineEntities(fieldId))
            {
                if (!lineStateReader.TryRead(entityId, out var state))
                {
                    readable = false;
                    break;
                }

                enabled[entityId] = state;
            }

            if (readable)
            {
                isLineEnabled = entityId => enabled.TryGetValue(entityId, out var state) && state;
            }
        }

        Func<int, bool>? isBoundaryEnabled = null;
        if (boundaryStateReader is not null &&
            FieldActivityReadout.ObservedBoundaryTriangles(fieldId).Count > 0)
        {
            var boundary = boundaryStateReader.Read(
                position,
                FieldBoundaryStateReader.MaximumTriangleCount);
            if (boundary.IsUsable)
            {
                var state = boundary.State;
                isBoundaryEnabled = triangle => state.IsBoundaryEnabled(triangle);
            }
        }

        // The cliff draws its body temperature in a native numeric window, whose value
        // is stored apart from the text a window carries; a reader that only sees
        // strings gets the word "Degrees" and never the number in front of it.
        var numericWindowId = FieldActivityReadout.ObservedNumericWindow(fieldId);
        var numericWindow = numericWindowId >= 0
            ? stateReader.ReadNumericWindow(fieldId, numericWindowId)
            : default;

        return new FieldActivityObservation(
            fieldId,
            position.X,
            position.Y,
            position.Z,
            position.TriangleId,
            isPlayerControlled,
            gameMoment,
            control.Transform,
            control.IsUsable,
            models,
            waits,
            isLineEnabled,
            isBoundaryEnabled,
            pillarGate)
        {
            NumericWindow = numericWindow,
            ReadTemporaryByte = FieldActivityReadout.NeedsTemporaryBank(fieldId) ? readTemporaryByte : null
        };
    }
}
