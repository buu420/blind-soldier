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
        var programs = Path.Combine(localData, "Programs", "Blind Swordsman");
        var stateRoot = Path.Combine(localData, "Blind Swordsman");
        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        return new InstallerPaths(
            stateRoot,
            Path.Combine(programs, "Blind-Swordsman-Setup.exe"),
            Path.Combine(stateRoot, "install-state.json"),
            Path.Combine(stateRoot, "Logs"),
            Path.Combine(startMenu, "Programs", "Blind Swordsman"));
    }
}
