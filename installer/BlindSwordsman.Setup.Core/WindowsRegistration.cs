using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace BlindSwordsman.Setup.Core;

public sealed record ShortcutRegistration(string Path, string Target, string Arguments, string Description);

public sealed record WindowsRegistrationData(
    string DisplayName,
    string DisplayVersion,
    string Publisher,
    string ProjectUrl,
    string InstalledSetupPath,
    string UninstallCommand,
    ShortcutRegistration UpdateShortcut);

public static class WindowsRegistration
{
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Blind Soldier";
    private const string LegacyUninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Blind Swordsman";
    private const string UpdateShortcutName = "Check for Blind Soldier Updates.lnk";
    private const string LegacyStartMenuDirectoryName = "Blind Swordsman";
    private const string LegacyUpdateShortcutName = "Check for Blind Swordsman Updates.lnk";

    public static WindowsRegistrationData Build(
        InstallState state,
        string installedSetupPath,
        string startMenuDirectory)
    {
        ArgumentNullException.ThrowIfNull(state);
        var setupPath = System.IO.Path.GetFullPath(installedSetupPath);
        var shortcutPath = System.IO.Path.Combine(
            System.IO.Path.GetFullPath(startMenuDirectory),
            UpdateShortcutName);
        return new WindowsRegistrationData(
            "Blind Soldier",
            state.ProductVersion.ToString(),
            "buu420",
            "https://github.com/buu420/blind-soldier",
            setupPath,
            $"\"{setupPath}\" --uninstall",
            new ShortcutRegistration(
                shortcutPath,
                setupPath,
                "--check-for-updates",
                "Check GitHub for Blind Soldier updates or repair the current installation."));
    }

    public static void Apply(WindowsRegistrationData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not create the per-user uninstall registration."))
        {
            key.SetValue("DisplayName", data.DisplayName, RegistryValueKind.String);
            key.SetValue("DisplayVersion", data.DisplayVersion, RegistryValueKind.String);
            key.SetValue("Publisher", data.Publisher, RegistryValueKind.String);
            key.SetValue("URLInfoAbout", data.ProjectUrl, RegistryValueKind.String);
            key.SetValue("URLUpdateInfo", data.ProjectUrl + "/releases", RegistryValueKind.String);
            key.SetValue("InstallLocation", System.IO.Path.GetDirectoryName(data.InstalledSetupPath)!, RegistryValueKind.String);
            key.SetValue("DisplayIcon", data.InstalledSetupPath, RegistryValueKind.String);
            key.SetValue("UninstallString", data.UninstallCommand, RegistryValueKind.String);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 0, RegistryValueKind.DWord);
        }

        Registry.CurrentUser.DeleteSubKeyTree(LegacyUninstallKeyPath, throwOnMissingSubKey: false);
        RemoveLegacyUpdateShortcut(data.UpdateShortcut.Path);
        CreateShortcut(data.UpdateShortcut);
    }

    public static void Remove(string startMenuDirectory)
    {
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(LegacyUninstallKeyPath, throwOnMissingSubKey: false);
        var directory = System.IO.Path.GetFullPath(startMenuDirectory);
        RemoveShortcutAndEmptyDirectory(System.IO.Path.Combine(directory, UpdateShortcutName));
        RemoveShortcutAndEmptyDirectory(GetLegacyUpdateShortcutPath(directory));
    }

    public static void RemoveInstalledSetup(string installedSetupPath)
    {
        var path = System.IO.Path.GetFullPath(installedSetupPath);
        if (!File.Exists(path))
        {
            return;
        }
        if ((new FileInfo(path).Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Managed setup executable became a reparse point and was preserved.");
        }
        if (!string.Equals(Environment.ProcessPath, path, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            return;
        }
        if (!MoveFileEx(path, null, MoveFileFlags.DelayUntilReboot))
        {
            throw new IOException("Windows could not schedule the running setup executable for removal.",
                new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    private static void RemoveLegacyUpdateShortcut(string currentShortcutPath)
    {
        var currentDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(currentShortcutPath))!;
        var legacyShortcut = GetLegacyUpdateShortcutPath(currentDirectory);
        if (!string.Equals(legacyShortcut, currentShortcutPath, StringComparison.OrdinalIgnoreCase))
        {
            RemoveShortcutAndEmptyDirectory(legacyShortcut);
        }
    }

    private static string GetLegacyUpdateShortcutPath(string currentStartMenuDirectory)
    {
        var programsDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(currentStartMenuDirectory))!;
        return System.IO.Path.Combine(programsDirectory, LegacyStartMenuDirectoryName, LegacyUpdateShortcutName);
    }

    private static void RemoveShortcutAndEmptyDirectory(string shortcut)
    {
        if (File.Exists(shortcut))
        {
            File.Delete(shortcut);
        }
        var directory = System.IO.Path.GetDirectoryName(shortcut)!;
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }

    private static void CreateShortcut(ShortcutRegistration shortcut)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(shortcut.Path)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new PlatformNotSupportedException("Windows Script Host is unavailable for Start Menu shortcut creation.");
        dynamic? shell = null;
        dynamic? link = null;
        try
        {
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Could not start Windows Script Host.");
            link = shell.CreateShortcut(shortcut.Path);
            link.TargetPath = shortcut.Target;
            link.Arguments = shortcut.Arguments;
            link.Description = shortcut.Description;
            link.WorkingDirectory = System.IO.Path.GetDirectoryName(shortcut.Target);
            link.IconLocation = shortcut.Target + ",0";
            link.Save();
        }
        finally
        {
            if (link is not null && System.Runtime.InteropServices.Marshal.IsComObject(link))
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
            }
            if (shell is not null && System.Runtime.InteropServices.Marshal.IsComObject(shell))
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    [Flags]
    private enum MoveFileFlags : uint
    {
        DelayUntilReboot = 0x00000004
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, MoveFileFlags flags);
}
