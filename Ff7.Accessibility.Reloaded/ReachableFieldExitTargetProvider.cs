namespace Ff7.Accessibility.Reloaded;

public sealed class ReachableFieldExitTargetProvider
{
    private static readonly IReadOnlyList<FieldNavigationTarget> EmptyTargets =
        Array.Empty<FieldNavigationTarget>();

    /// <summary>
    /// How long a previously reachable exit set survives a whole-field routing
    /// failure that the caller cannot attribute to scripted movement. The native
    /// triangle id lags the player's coordinates for a few frames after a climb
    /// or a jump ends, so routing stays impossible slightly longer than the
    /// scripted movement itself does.
    /// </summary>
    private static readonly TimeSpan TransientBlockWindow = TimeSpan.FromMilliseconds(1500);

    private readonly Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>> readNativeTargets;
    private readonly IFieldNavigationRoutePlanner routePlanner;
    private readonly Func<DateTime> utcNow;
    private IReadOnlyList<FieldNavigationTarget> lastReachable = EmptyTargets;
    private DateTime lastReachableAtUtc = DateTime.MinValue;
    private int lastReachableFieldId = -1;

    public ReachableFieldExitTargetProvider(
        Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>> readNativeTargets,
        IFieldNavigationRoutePlanner routePlanner,
        Func<DateTime>? utcNow = null)
    {
        this.readNativeTargets = readNativeTargets;
        this.routePlanner = routePlanner;
        this.utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public string LastDiagnostic { get; private set; } = "not read";

    /// <param name="positionIsScripted">
    /// True while the player is somewhere their own walking input did not take
    /// them: mounted on a ladder, or moving under a scripted control lock. Fort
    /// Condor's watch room is the clearest case - walking onto the lookout line
    /// hands the player to a script that jumps them onto a walkmesh triangle
    /// nothing else connects to, holds them there for three seconds and jumps
    /// them back down. Nothing routes from that triangle because nothing is
    /// meant to, and a timer long enough to cover it would also have to cover
    /// the fort's full-height save-room climb.
    /// </param>
    public IReadOnlyList<FieldNavigationTarget> ReadTargets(
        FieldPositionSnapshot position,
        bool positionIsScripted = false)
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

        var now = utcNow();
        if (reachable.Count > 0)
        {
            lastReachable = reachable;
            lastReachableAtUtc = now;
            lastReachableFieldId = position.FieldId;
            LastDiagnostic =
                $"field={position.FieldId}, native={nativeTargets.Count}, reachable={reachable.Count}, " +
                $"blocked={(blocked.Count == 0 ? "none" : string.Join(',', blocked))}";
            return reachable;
        }

        // Losing every exit at once is the signature of an unresolvable player
        // position, not of a field whose exits are all genuinely shut. Dropping
        // the list here is what made auto-walk abandon its target on every rung
        // of a ladder. A field whose exits really are all blocked never cached a
        // set in the first place, so nothing is held open for it.
        if (lastReachableFieldId == position.FieldId &&
            lastReachable.Count > 0 &&
            (positionIsScripted ||
             (now >= lastReachableAtUtc && now - lastReachableAtUtc <= TransientBlockWindow)))
        {
            // The hold may outlive an exit: a gateway the script switched off
            // leaves the native list even while the player is still airborne.
            // Announcing a doorway the game has taken away is its own failure,
            // so the held set never says more than the live one does.
            var held = lastReachable
                .Where(target => nativeTargets.Any(native =>
                    string.Equals(native.StableId, target.StableId, StringComparison.Ordinal)))
                .ToArray();
            if (held.Length > 0)
            {
                lastReachable = held;
                LastDiagnostic =
                    $"field={position.FieldId}, native={nativeTargets.Count}, reachable=0, " +
                    $"holding {held.Length} through " +
                    (positionIsScripted ? "scripted movement" : "unresolved position") +
                    $", blocked={string.Join(',', blocked)}";
                return held;
            }
        }

        lastReachable = EmptyTargets;
        lastReachableFieldId = -1;
        LastDiagnostic =
            $"field={position.FieldId}, native={nativeTargets.Count}, reachable=0, " +
            $"blocked={string.Join(',', blocked)}";
        return EmptyTargets;
    }
}
