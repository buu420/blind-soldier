namespace BlindSwordsman.Setup.Core;

public enum SetupMode
{
    Install,
    Update,
    Repair,
    DowngradeBlocked,
    Uninstall
}

public static class SetupModeResolver
{
    public static SetupMode Resolve(InstallState? installed, SemanticVersion available)
    {
        if (installed is null)
        {
            return SetupMode.Install;
        }

        var comparison = available.CompareTo(installed.ProductVersion);
        return comparison switch
        {
            > 0 => SetupMode.Update,
            0 => SetupMode.Repair,
            _ => SetupMode.DowngradeBlocked
        };
    }
}
