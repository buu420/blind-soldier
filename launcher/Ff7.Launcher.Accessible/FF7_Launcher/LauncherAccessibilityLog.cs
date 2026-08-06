using System;
using System.IO;
using System.Text;

namespace FF7_Launcher;

internal static class LauncherAccessibilityLog
{
    private static readonly object Sync = new object();
    private static readonly string DirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher_accessibility");
    private static readonly string LogPath = Path.Combine(DirectoryPath, "FFVII_LAUNCHER.accessibility.log");

    internal static string FilePath => LogPath;

    internal static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(
                    LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch
        {
            // Accessibility logging must never prevent the launcher from opening.
        }
    }
}
