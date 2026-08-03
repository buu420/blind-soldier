using System.Text;

namespace BlindSwordsman.Setup.Core;

public sealed class SetupLog : IDisposable
{
    private readonly object sync = new();
    private readonly StreamWriter writer;

    public SetupLog(string directory)
    {
        Directory.CreateDirectory(directory);
        var directoryInfo = new DirectoryInfo(directory);
        if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Setup log directory cannot be a reparse point.");
        }
        Path = System.IO.Path.Combine(
            directory,
            $"Blind-Soldier-Setup-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
        writer = new StreamWriter(
            new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
        Write("Blind Soldier setup log started.");
    }

    public string Path { get; }

    public void Write(string message)
    {
        lock (sync)
        {
            writer.WriteLine($"{DateTimeOffset.UtcNow:O} {message.Replace("\r", " ").Replace("\n", " ")}");
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            writer.Dispose();
        }
    }
}
