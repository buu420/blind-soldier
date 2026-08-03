namespace BlindSwordsman.Setup.Core;

public sealed record InstallerPaths(
    string LocalDataRoot,
    string InstalledSetupPath,
    string InstallStatePath,
    string LogDirectory,
    string StartMenuDirectory,
    string? LegacyInstalledSetupPath = null,
    string? LegacyInstallStatePath = null)
{
    public static InstallerPaths ForCurrentUser()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programs = Path.Combine(localData, "Programs", "Blind Soldier");
        var stateRoot = Path.Combine(localData, "Blind Soldier");
        var legacyPrograms = Path.Combine(localData, "Programs", "Blind Swordsman");
        var legacyStateRoot = Path.Combine(localData, "Blind Swordsman");
        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        return new InstallerPaths(
            stateRoot,
            Path.Combine(programs, "Blind-Soldier-Setup.exe"),
            Path.Combine(stateRoot, "install-state.json"),
            Path.Combine(stateRoot, "Logs"),
            Path.Combine(startMenu, "Programs", "Blind Soldier"),
            Path.Combine(legacyPrograms, "Blind-Swordsman-Setup.exe"),
            Path.Combine(legacyStateRoot, "install-state.json"));
    }
}
