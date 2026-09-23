using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

/// <summary>
/// Confirms native NPC targets twice between stable field/model and event-table
/// ownership bookends. Only pointer-free targets from the shared x86 reader are
/// published to the shared navigation controller.
///
/// <para>Confirmation is about who is there, not where they are standing. Field NPCs
/// walk: the two reads of a moving model differ by a unit or two in the ordinary case,
/// and comparing whole targets threw the entire list away for it - the 2026-09-22
/// capture reports <c>npcs=-1</c> in 213 of Rocket Town's 541 samples, which the player
/// hears as "NPCs: none for this field yet" in a street with three people in it. What
/// has to be stable between the bookends is the cast and how each of them may be
/// interacted with: the entity behind the row, its label, the reach the native talk and
/// collision widths give it, the line that stands in for it and how it activates. Those
/// changing mid-read means the field is being rewritten underneath, and the read still
/// fails closed. Position is then taken from the later read, so what is published is
/// current rather than an average of two frames.</para>
/// </summary>
internal sealed class Steam2026FieldNpcObservationReader
{
    private static readonly IReadOnlyList<FieldNavigationTarget> NoTargets =
        Array.Empty<FieldNavigationTarget>();

    private readonly Func<FieldPositionReadResult> readPosition;
    private readonly Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>> readTargets;
    private readonly Func<uint?> readEventTable;

    internal Steam2026FieldNpcObservationReader(
        Func<FieldPositionReadResult> readPosition,
        Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>> readTargets,
        Func<uint?> readEventTable)
    {
        this.readPosition = readPosition ?? throw new ArgumentNullException(nameof(readPosition));
        this.readTargets = readTargets ?? throw new ArgumentNullException(nameof(readTargets));
        this.readEventTable = readEventTable ?? throw new ArgumentNullException(nameof(readEventTable));
    }

    internal string LastDiagnostic { get; private set; } = "not read";

    internal bool TryRead(
        FieldPositionSnapshot expectedPosition,
        out IReadOnlyList<FieldNavigationTarget> targets)
    {
        targets = NoTargets;
        try
        {
            var before = readPosition();
            if (!MatchesExpectedOwnership(before, expectedPosition))
            {
                LastDiagnostic = "NPC ownership before-read is unavailable";
                return false;
            }

            var beforeEventTable = readEventTable();
            if (beforeEventTable is null or 0)
            {
                LastDiagnostic = "NPC event-table ownership before-read is unavailable";
                return false;
            }

            var candidate = readTargets(before.Position).ToArray();
            var middle = readPosition();
            var middleEventTable = readEventTable();
            if (!HasSameOwnership(before, middle, expectedPosition) ||
                middleEventTable != beforeEventTable)
            {
                LastDiagnostic = "NPC ownership changed before confirmation";
                return false;
            }

            var confirmation = readTargets(middle.Position).ToArray();
            var after = readPosition();
            var afterEventTable = readEventTable();
            if (!HasSameOwnership(before, after, expectedPosition) ||
                afterEventTable != beforeEventTable)
            {
                LastDiagnostic = "NPC ownership changed during confirmation";
                return false;
            }

            if (!DescribesTheSameActors(candidate, confirmation, out var difference, out var moved))
            {
                LastDiagnostic = $"NPC targets changed during confirmation: {difference}";
                return false;
            }

            targets = Array.AsReadOnly(confirmation);
            LastDiagnostic =
                $"field={expectedPosition.FieldId}, playerModel={expectedPosition.ModelIndex}, " +
                $"native={targets.Count}" +
                (moved > 0 ? $", moving={moved}" : string.Empty);
            return true;
        }
        catch (Exception ex)
        {
            targets = NoTargets;
            LastDiagnostic = $"NPC read failed closed: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// The furthest a model may be seen to move between the two confirmations and still
    /// be the same model walking. A field model covers about sixteen units a step, so
    /// this is far more than any walk inside one scan; beyond it the difference is a
    /// script placing somebody somewhere else, or a torn read of the position words, and
    /// neither is something to publish.
    /// </summary>
    private const int MaximumActorMovement = 512;

    /// <summary>
    /// Whether both reads describe the same people, offered the same way. Everything
    /// except where they are standing has to match.
    /// </summary>
    private static bool DescribesTheSameActors(
        FieldNavigationTarget[] candidate,
        FieldNavigationTarget[] confirmation,
        out string difference,
        out int moved)
    {
        moved = 0;
        if (candidate.Length != confirmation.Length)
        {
            difference = $"actor count {candidate.Length} then {confirmation.Length}";
            return false;
        }

        for (var index = 0; index < candidate.Length; index++)
        {
            var before = candidate[index];
            var after = confirmation[index];
            if (!string.Equals(before.StableId, after.StableId, StringComparison.Ordinal) ||
                before.FieldId != after.FieldId ||
                before.Category != after.Category ||
                before.TriggerEntityId != after.TriggerEntityId)
            {
                difference = $"actor {index} is {before.StableId} then {after.StableId}";
                return false;
            }

            if (!string.Equals(before.Label, after.Label, StringComparison.Ordinal))
            {
                difference = $"{before.StableId} is named {before.Label} then {after.Label}";
                return false;
            }

            if (before.InteractionRadius != after.InteractionRadius)
            {
                difference =
                    $"{before.StableId} reach {before.InteractionRadius} then {after.InteractionRadius}";
                return false;
            }

            if (before.TriggerLine != after.TriggerLine ||
                before.Activation != after.Activation ||
                before.CompletesOnArrival != after.CompletesOnArrival ||
                before.ObjectCueKind != after.ObjectCueKind ||
                !string.Equals(
                    before.ManualNavigationGuidance,
                    after.ManualNavigationGuidance,
                    StringComparison.Ordinal))
            {
                difference = $"{before.StableId} is interacted with differently";
                return false;
            }

            var offsetX = after.X - (long)before.X;
            var offsetY = after.Y - (long)before.Y;
            var offsetZ = after.Z - (long)before.Z;
            var distanceSquared = offsetX * offsetX + offsetY * offsetY + offsetZ * offsetZ;
            if (distanceSquared > (long)MaximumActorMovement * MaximumActorMovement)
            {
                difference =
                    $"{before.StableId} jumped from {before.X},{before.Y},{before.Z} " +
                    $"to {after.X},{after.Y},{after.Z}";
                return false;
            }

            if (distanceSquared > 0)
            {
                moved++;
            }
        }

        difference = string.Empty;
        return true;
    }

    private static bool HasSameOwnership(
        FieldPositionReadResult expected,
        FieldPositionReadResult actual,
        FieldPositionSnapshot requested) =>
        expected.ModelBase == actual.ModelBase &&
        MatchesExpectedOwnership(expected, requested) &&
        MatchesExpectedOwnership(actual, requested);

    private static bool MatchesExpectedOwnership(
        FieldPositionReadResult read,
        FieldPositionSnapshot expected) =>
        read.IsUsable &&
        read.ModelBase != 0 &&
        read.Position.CurrentModule == FieldPositionReader.FieldModule &&
        read.Position.CurrentModule == expected.CurrentModule &&
        read.Position.FieldId == expected.FieldId &&
        read.Position.ModelIndex == expected.ModelIndex;
}
