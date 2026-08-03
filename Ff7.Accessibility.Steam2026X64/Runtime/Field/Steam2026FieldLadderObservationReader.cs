using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

/// <summary>
/// Confirms native ladder state twice between stable field/model and event-table
/// ownership bookends. A single usable ladder read validates values, but cannot
/// prove that the translated event table did not change during the read.
/// </summary>
internal sealed class Steam2026FieldLadderObservationReader
{
    private readonly Func<FieldPositionReadResult> readPosition;
    private readonly Func<FieldPositionSnapshot, FieldLadderStateReadResult> readLadder;
    private readonly Func<uint?> readEventTable;

    internal Steam2026FieldLadderObservationReader(
        Func<FieldPositionReadResult> readPosition,
        Func<FieldPositionSnapshot, FieldLadderStateReadResult> readLadder,
        Func<uint?> readEventTable)
    {
        this.readPosition = readPosition ?? throw new ArgumentNullException(nameof(readPosition));
        this.readLadder = readLadder ?? throw new ArgumentNullException(nameof(readLadder));
        this.readEventTable = readEventTable ?? throw new ArgumentNullException(nameof(readEventTable));
    }

    internal bool TryRead(
        FieldPositionSnapshot expectedPosition,
        out FieldLadderStateSnapshot state)
    {
        state = default;
        var before = readPosition();
        if (!MatchesExpectedOwnership(before, expectedPosition))
        {
            return false;
        }

        var beforeEventTable = readEventTable();
        if (beforeEventTable is null or 0)
        {
            return false;
        }

        var candidate = readLadder(before.Position);
        var middle = readPosition();
        var middleEventTable = readEventTable();
        if (!candidate.IsUsable ||
            !HasSameOwnership(before, middle, expectedPosition) ||
            middleEventTable != beforeEventTable)
        {
            return false;
        }

        var confirmation = readLadder(middle.Position);
        var after = readPosition();
        var afterEventTable = readEventTable();
        if (!confirmation.IsUsable ||
            !HasSameOwnership(before, after, expectedPosition) ||
            afterEventTable != beforeEventTable ||
            candidate.State != confirmation.State)
        {
            return false;
        }

        state = confirmation.State;
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
