namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Applies verified, field-specific presentation to native exits without
/// changing their reachability or treating common destinations as duplicates.
/// </summary>
public sealed class FieldExitPresentationPolicy
{
    private const string KalmWorldMapGatewayA = "gateway:335:9:2";
    private const string KalmWorldMapGatewayB = "gateway:335:10:2";
    private readonly Func<bool?> readKalmCompletion;

    public FieldExitPresentationPolicy(Func<bool?> readKalmCompletion)
    {
        this.readKalmCompletion = readKalmCompletion ?? throw new ArgumentNullException(nameof(readKalmCompletion));
    }

    public IReadOnlyList<FieldNavigationTarget> Apply(IReadOnlyList<FieldNavigationTarget> targets)
    {
        if (targets.Count == 0 || !targets.Any(IsKalmWorldMapGateway))
        {
            return targets;
        }

        var kalmComplete = TryReadKalmCompletion();
        var visible = new List<FieldNavigationTarget>(targets.Count);
        var addedWorldMapExit = false;
        foreach (var target in targets)
        {
            if (!IsKalmWorldMapGateway(target))
            {
                visible.Add(target);
                continue;
            }

            if (kalmComplete != true || addedWorldMapExit)
            {
                continue;
            }

            visible.Add(target with { Label = "Leave Kalm for the World Map" });
            addedWorldMapExit = true;
        }

        return visible;
    }

    private bool? TryReadKalmCompletion()
    {
        try
        {
            return readKalmCompletion();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsKalmWorldMapGateway(FieldNavigationTarget target) =>
        target.StableId is KalmWorldMapGatewayA or KalmWorldMapGatewayB;
}
