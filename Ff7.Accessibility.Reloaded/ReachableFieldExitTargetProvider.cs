namespace Ff7.Accessibility.Reloaded;

public sealed class ReachableFieldExitTargetProvider
{
    private static readonly IReadOnlyList<FieldNavigationTarget> EmptyTargets =
        Array.Empty<FieldNavigationTarget>();

    private readonly Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>> readNativeTargets;
    private readonly IFieldNavigationRoutePlanner routePlanner;

    public ReachableFieldExitTargetProvider(
        Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>> readNativeTargets,
        IFieldNavigationRoutePlanner routePlanner)
    {
        this.readNativeTargets = readNativeTargets;
        this.routePlanner = routePlanner;
    }

    public string LastDiagnostic { get; private set; } = "not read";

    public IReadOnlyList<FieldNavigationTarget> ReadTargets(FieldPositionSnapshot position)
    {
        if (!FieldPositionReader.IsUsable(position))
        {
            LastDiagnostic = $"field={position.FieldId}, not in field module, reachable=0";
            return EmptyTargets;
        }

        var nativeTargets = readNativeTargets(position);
        if (nativeTargets.Count == 0)
        {
            LastDiagnostic = $"field={position.FieldId}, native=0, reachable=0";
            return EmptyTargets;
        }

        var reachable = new List<FieldNavigationTarget>(nativeTargets.Count);
        var blocked = new List<string>();
        foreach (var target in nativeTargets)
        {
            if (routePlanner.TryBuildRoute(position, target, out _))
            {
                reachable.Add(target);
            }
            else
            {
                blocked.Add(target.Label);
            }
        }

        LastDiagnostic =
            $"field={position.FieldId}, native={nativeTargets.Count}, reachable={reachable.Count}, " +
            $"blocked={(blocked.Count == 0 ? "none" : string.Join(',', blocked))}";
        return reachable.Count == 0 ? EmptyTargets : reachable;
    }
}
