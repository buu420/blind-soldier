using System.Text;

namespace BlindSwordsman.Setup.Core;

public sealed class InstallStateStore(string path)
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public InstallState? Load()
    {
        if (!File.Exists(Path))
        {
            return null;
        }
        var item = new FileInfo(Path);
        if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Install state cannot be a reparse point.");
        }
        return DeploymentResultParser.Parse(File.ReadAllText(Path, Encoding.UTF8));
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
        if (!File.Exists(Path))
        {
            return;
        }
        var item = new FileInfo(Path);
        if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Install state cannot delete a reparse point.");
        }
        File.Delete(Path);
    }
}
