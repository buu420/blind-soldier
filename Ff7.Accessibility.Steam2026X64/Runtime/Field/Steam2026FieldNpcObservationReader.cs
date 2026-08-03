using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

/// <summary>
/// Confirms native NPC targets twice between stable field/model and event-table
/// ownership bookends. Only pointer-free targets from the shared x86 reader are
/// published to the shared navigation controller.
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
                afterEventTable != beforeEventTable ||
                !candidate.AsSpan().SequenceEqual(confirmation))
            {
                LastDiagnostic = "NPC targets or ownership changed during confirmation";
                return false;
            }

            targets = Array.AsReadOnly(confirmation);
            LastDiagnostic =
                $"field={expectedPosition.FieldId}, playerModel={expectedPosition.ModelIndex}, " +
                $"native={targets.Count}";
            return true;
        }
        catch (Exception ex)
        {
            targets = NoTargets;
            LastDiagnostic = $"NPC read failed closed: {ex.Message}";
            return false;
        }
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
