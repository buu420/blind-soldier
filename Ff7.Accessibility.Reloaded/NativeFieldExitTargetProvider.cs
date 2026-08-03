namespace Ff7.Accessibility.Reloaded;

public sealed class NativeFieldExitTargetProvider
{
    private static readonly IReadOnlyList<FieldNavigationTarget> EmptyTargets =
        Array.Empty<FieldNavigationTarget>();

    private readonly FieldGatewayTargetReader gatewayReader;
    private readonly Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>>? scriptExitProvider;
    private readonly TimeSpan fieldSettleWindow;
    private readonly TimeSpan snapshotSettleWindow;
    private readonly Func<DateTime> clock;
    private readonly FieldExitLabelResolver? labelResolver;

    private int fieldId = -1;
    private DateTime fieldSeenAt;
    private string candidateFingerprint = string.Empty;
    private DateTime candidateSeenAt;
    private IReadOnlyList<FieldNavigationTarget> candidateTargets = EmptyTargets;

    public NativeFieldExitTargetProvider(
        FieldGatewayTargetReader gatewayReader,
        Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>>? scriptExitProvider = null,
        TimeSpan? fieldSettleWindow = null,
        TimeSpan? snapshotSettleWindow = null,
        Func<DateTime>? clock = null,
        FieldExitLabelResolver? labelResolver = null)
    {
        this.gatewayReader = gatewayReader;
        this.scriptExitProvider = scriptExitProvider;
        this.fieldSettleWindow = Clamp(fieldSettleWindow ?? TimeSpan.FromMilliseconds(300));
        this.snapshotSettleWindow = Clamp(snapshotSettleWindow ?? TimeSpan.FromMilliseconds(100));
        this.clock = clock ?? (() => DateTime.UtcNow);
        this.labelResolver = labelResolver;
    }

    public string LastDiagnostic { get; private set; } = "not read";

    public IReadOnlyList<FieldNavigationTarget> ReadTargets(FieldPositionSnapshot position)
    {
        if (!FieldPositionReader.IsUsable(position))
        {
            LastDiagnostic = $"field={position.FieldId}, not in field module, visible=0";
            return EmptyTargets;
        }

        var now = clock();
        var gatewayTargets = gatewayReader.ReadTargets(position);
        var scriptTargets = scriptExitProvider?.Invoke(position) ?? EmptyTargets;
        var nativeTargets = gatewayTargets
            .Concat(scriptTargets)
            .Select(FieldExitNavigationProfileCatalog.Apply)
            .DistinctBy(target => target.StableId)
            .ToArray();
        var fingerprint = CreateFingerprint(nativeTargets);

        if (fieldId != position.FieldId)
        {
            fieldId = position.FieldId;
            fieldSeenAt = now;
            SetCandidate(nativeTargets, fingerprint, now);
            LastDiagnostic = BuildDiagnostic(scriptTargets.Count, "settling field", 0);
            return EmptyTargets;
        }

        if (!string.Equals(candidateFingerprint, fingerprint, StringComparison.Ordinal))
        {
            SetCandidate(nativeTargets, fingerprint, now);
            LastDiagnostic = BuildDiagnostic(scriptTargets.Count, "settling snapshot", 0);
            return EmptyTargets;
        }

        if (now - fieldSeenAt < fieldSettleWindow)
        {
            LastDiagnostic = BuildDiagnostic(scriptTargets.Count, "settling field", 0);
            return EmptyTargets;
        }

        if (now - candidateSeenAt < snapshotSettleWindow)
        {
            LastDiagnostic = BuildDiagnostic(scriptTargets.Count, "settling snapshot", 0);
            return EmptyTargets;
        }

        var visible = labelResolver?.Resolve(candidateTargets) ?? candidateTargets;
        LastDiagnostic = BuildDiagnostic(scriptTargets.Count, "stable", visible.Count);
        return visible.Count == 0 ? EmptyTargets : visible;
    }

    private void SetCandidate(
        IReadOnlyList<FieldNavigationTarget> targets,
        string fingerprint,
        DateTime now)
    {
        candidateTargets = targets;
        candidateFingerprint = fingerprint;
        candidateSeenAt = now;
    }

    private string BuildDiagnostic(int scriptExitCount, string state, int visibleCount) =>
        $"{gatewayReader.LastDiagnostic}, scriptExits={scriptExitCount}, state={state}, visible={visibleCount}";

    private static string CreateFingerprint(IEnumerable<FieldNavigationTarget> targets) =>
        string.Join(
            "\u001e",
            targets.Select(target =>
                $"{target.StableId}\u001f{target.FieldId}\u001f{target.X}\u001f{target.Y}\u001f{target.Z}\u001f" +
                $"{string.Join(',', target.DestinationFieldIds ?? Array.Empty<int>())}"));

    private static TimeSpan Clamp(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}
