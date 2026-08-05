using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

/// <summary>
/// Applies native progression facts that cannot be recovered safely from the
/// branch-insensitive field-script MAPJUMP scan alone.
/// </summary>
internal static class Steam2026FieldScriptExitPolicy
{
    private static readonly IReadOnlyList<FieldNavigationTarget> NoTargets =
        Array.Empty<FieldNavigationTarget>();

    internal static IReadOnlyList<FieldNavigationTarget> Filter(
        int fieldId,
        int gameMoment,
        IReadOnlyList<FieldNavigationTarget> scriptExits)
    {
        ArgumentNullException.ThrowIfNull(scriptExits);
        scriptExits = FieldScriptExitBranchPolicy.Resolve(fieldId, gameMoment, scriptExits);

        // cargoin briefly restores control while Cloud automatically follows
        // the party through the hatch. It is not a player-selectable exit on
        // the first arrival. Once the player has reached the passenger car,
        // the same hatch is a legitimate backtrack route.
        if (fieldId == 138 && gameMoment is >= 39 and <= 50)
        {
            return NoTargets;
        }

        // tin_1 contains MAPJUMPs for several later train missions and for the
        // mandatory first-ride story line. During GM 51..62, only LINE entity
        // 25's single destination back to cargoin is an ordinary exit. Entity
        // 27 is surfaced by Story after its native Bank 3 flag is set.
        if (fieldId == 139 && gameMoment is >= 51 and <= 62)
        {
            return scriptExits
                .Where(IsFirstRideRearHatch)
                .ToArray();
        }

        return scriptExits;
    }

    private static bool IsFirstRideRearHatch(FieldNavigationTarget target) =>
        target.TriggerEntityId == 25 &&
        target.DestinationFieldIds is { Count: 1 } destinations &&
        destinations[0] == 138;
}

/// <summary>
/// Prevents transient field initialization, model ownership, or script-line
/// snapshots from becoming audible exits. A candidate must be seen coherently
/// at least twice and remain unchanged for both settle windows.
/// </summary>
internal sealed class Steam2026FieldExitPublicationGate
{
    private static readonly IReadOnlyList<FieldNavigationTarget> NoTargets =
        Array.Empty<FieldNavigationTarget>();

    private readonly TimeSpan fieldSettleWindow;
    private readonly TimeSpan snapshotSettleWindow;
    private readonly TimeSpan transientHoldWindow;
    private int fieldId = -1;
    private int modelIndex = -1;
    private DateTime fieldSeenAtUtc;
    private DateTime candidateSeenAtUtc;
    private DateTime lastSuccessfulObservationAtUtc;
    private string candidateFingerprint = string.Empty;
    private int candidateObservations;
    private bool hasPublishedCandidate;
    private IReadOnlyList<FieldNavigationTarget> candidateTargets = NoTargets;

    internal Steam2026FieldExitPublicationGate(
        TimeSpan? fieldSettleWindow = null,
        TimeSpan? snapshotSettleWindow = null,
        TimeSpan? transientHoldWindow = null)
    {
        this.fieldSettleWindow = Clamp(
            fieldSettleWindow ?? TimeSpan.FromMilliseconds(300));
        this.snapshotSettleWindow = Clamp(
            snapshotSettleWindow ?? TimeSpan.FromMilliseconds(100));
        this.transientHoldWindow = Clamp(
            transientHoldWindow ?? TimeSpan.FromSeconds(1));
    }

    internal string LastDiagnostic { get; private set; } = "not observed";

    internal bool IsStable { get; private set; }

    internal IReadOnlyList<FieldNavigationTarget> Observe(
        int observedFieldId,
        int observedModelIndex,
        IReadOnlyList<FieldNavigationTarget> targets,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var fingerprint = CreateFingerprint(targets);
        if (fieldId != observedFieldId || modelIndex != observedModelIndex)
        {
            fieldId = observedFieldId;
            modelIndex = observedModelIndex;
            fieldSeenAtUtc = nowUtc;
            SetCandidate(targets, fingerprint, nowUtc);
            IsStable = false;
            LastDiagnostic = "settling field/model ownership";
            return NoTargets;
        }

        if (!string.Equals(candidateFingerprint, fingerprint, StringComparison.Ordinal))
        {
            SetCandidate(targets, fingerprint, nowUtc);
            IsStable = false;
            LastDiagnostic = "settling changed exit fingerprint";
            return NoTargets;
        }

        candidateObservations++;
        lastSuccessfulObservationAtUtc = nowUtc;
        if (candidateObservations < 2 ||
            nowUtc - fieldSeenAtUtc < fieldSettleWindow ||
            nowUtc - candidateSeenAtUtc < snapshotSettleWindow)
        {
            IsStable = false;
            LastDiagnostic = $"settling stable exit fingerprint ({candidateObservations} reads)";
            return NoTargets;
        }

        IsStable = true;
        hasPublishedCandidate = true;
        LastDiagnostic = $"stable ({candidateTargets.Count} exits)";
        return candidateTargets;
    }

    internal void ObserveUnavailable(
        int observedFieldId,
        int observedModelIndex,
        DateTime nowUtc)
    {
        if (!hasPublishedCandidate ||
            fieldId != observedFieldId ||
            modelIndex != observedModelIndex ||
            nowUtc < lastSuccessfulObservationAtUtc ||
            nowUtc - lastSuccessfulObservationAtUtc > transientHoldWindow)
        {
            Reset();
            LastDiagnostic = "unavailable snapshot discarded exit ownership";
            return;
        }

        IsStable = false;
        LastDiagnostic = "temporarily unavailable; retaining established exit fingerprint";
    }

    internal void Reset()
    {
        fieldId = -1;
        modelIndex = -1;
        fieldSeenAtUtc = default;
        candidateSeenAtUtc = default;
        lastSuccessfulObservationAtUtc = default;
        candidateFingerprint = string.Empty;
        candidateObservations = 0;
        hasPublishedCandidate = false;
        candidateTargets = NoTargets;
        IsStable = false;
        LastDiagnostic = "reset";
    }

    private void SetCandidate(
        IReadOnlyList<FieldNavigationTarget> targets,
        string fingerprint,
        DateTime nowUtc)
    {
        candidateTargets = targets.ToArray();
        candidateFingerprint = fingerprint;
        candidateSeenAtUtc = nowUtc;
        lastSuccessfulObservationAtUtc = nowUtc;
        candidateObservations = 1;
        hasPublishedCandidate = false;
    }

    private static string CreateFingerprint(IReadOnlyList<FieldNavigationTarget> targets) =>
        string.Join(
            '|',
            targets
                .Select(target =>
                    $"{target.StableId}:{target.FieldId}:{target.TriggerEntityId}:{target.Label}:" +
                    $"{target.X},{target.Y},{target.Z}:" +
                    string.Join(',', target.DestinationFieldIds ?? Array.Empty<int>()))
                .OrderBy(value => value, StringComparer.Ordinal));

    private static TimeSpan Clamp(TimeSpan value) =>
        value < TimeSpan.Zero
            ? TimeSpan.Zero
            : value > TimeSpan.FromSeconds(5)
                ? TimeSpan.FromSeconds(5)
                : value;
}
