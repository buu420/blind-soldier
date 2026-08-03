using System.Text;

namespace BlindSwordsman.Setup.Core;

public sealed class InstallStateStore(string path, string? legacyPath = null)
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);
    public string? LegacyPath { get; } = string.IsNullOrWhiteSpace(legacyPath)
        ? null
        : System.IO.Path.GetFullPath(legacyPath);

    public InstallState? Load()
    {
        var sourcePath = File.Exists(Path) ? Path : LegacyPath;
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            return null;
        }
        var item = new FileInfo(sourcePath);
        if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Install state cannot be a reparse point.");
        }
        return DeploymentResultParser.Parse(File.ReadAllText(sourcePath, Encoding.UTF8));
    }

    public void Save(InstallState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var directory = System.IO.Path.GetDirectoryName(Path)
            ?? throw new InvalidOperationException("Install-state path has no parent directory.");
        Directory.CreateDirectory(directory);
        var directoryInfo = new DirectoryInfo(directory);
        if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Install-state directory cannot be a reparse point.");
        }
        if (File.Exists(Path) && (new FileInfo(Path).Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Install state cannot replace a reparse point.");
        }

        var temporary = System.IO.Path.Combine(directory, ".install-state-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(DeploymentResultParser.Serialize(state));
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(Path))
            {
                File.Replace(temporary, Path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, Path);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public void Delete()
    {
        DeletePath(Path);
        DeleteLegacy();
    }

    public void DeleteLegacy()
    {
        if (LegacyPath is not null &&
            !string.Equals(LegacyPath, Path, StringComparison.OrdinalIgnoreCase))
        {
            DeletePath(LegacyPath);
        }
    }

    private static void DeletePath(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }
        var item = new FileInfo(path);
        if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Install state cannot delete a reparse point.");
        }
        File.Delete(path);
    }
}
