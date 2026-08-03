namespace BlindSwordsman.Setup.Core;

public sealed record InstallerPaths(
    string LocalDataRoot,
    string InstalledSetupPath,
    string InstallStatePath,
    string LogDirectory,
    string StartMenuDirectory)
{
    public static InstallerPaths ForCurrentUser()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programs = Path.Combine(localData, "Programs", "Blind Soldier");
        var stateRoot = Path.Combine(localData, "Blind Soldier");
        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        return new InstallerPaths(
            stateRoot,
            Path.Combine(programs, "Blind-Soldier-Setup.exe"),
            Path.Combine(stateRoot, "install-state.json"),
            Path.Combine(stateRoot, "Logs"),
            Path.Combine(startMenu, "Programs", "Blind Soldier"));
    }
}
