namespace Ff7.Accessibility.Reloaded;

public sealed class FieldOpcodeHookTargetTracker
{
    private readonly HashSet<int> installedTargets = [];

    public bool NeedsInstall(int targetAddress) =>
        targetAddress > 0 && !installedTargets.Contains(targetAddress);

    public void MarkInstalled(int targetAddress)
    {
        if (targetAddress > 0)
        {
            installedTargets.Add(targetAddress);
        }
    }
}
