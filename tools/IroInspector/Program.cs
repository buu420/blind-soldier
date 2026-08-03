using AppWrapper;

if (args.Length < 1)
{
    Console.Error.WriteLine(
        "Usage: IroInspector <archive.iro> [--list [filter] | --extract-prefix <prefix> <output-directory>]");
    return 2;
}

using var archive = new IrosArc(args[0]);
if (args.Length >= 2 && string.Equals(args[1], "--list", StringComparison.OrdinalIgnoreCase))
{
    var filter = args.Length >= 3 ? args[2] : string.Empty;
    foreach (var file in archive.AllFileNames()
                 .Where(file => file.Contains(filter, StringComparison.OrdinalIgnoreCase))
                 .OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine(file);
    }

    return 0;
}

if (args.Length == 4 && string.Equals(args[1], "--extract-prefix", StringComparison.OrdinalIgnoreCase))
{
    var prefix = args[2].TrimEnd('\\', '/') + "\\";
    var outputRoot = Path.GetFullPath(args[3]);
    Directory.CreateDirectory(outputRoot);
    var extracted = 0;
    foreach (var archiveName in archive.AllFileNames()
                 .Where(file => file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                 .OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
    {
        var relativeName = archiveName[prefix.Length..].Replace('\\', Path.DirectorySeparatorChar);
        if (relativeName.Length == 0)
        {
            continue;
        }

        var outputPath = Path.GetFullPath(Path.Combine(outputRoot, relativeName));
        if (!outputPath.StartsWith(outputRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsafe archive path: {archiveName}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var input = archive.GetData(archiveName);
        using var output = File.Create(outputPath);
        await input.CopyToAsync(output);
        extracted++;
    }

    Console.Error.WriteLine($"Extracted {extracted} files from {prefix} to {outputRoot}.");
    return extracted == 0 ? 4 : 0;
}

if (!archive.HasFile("mod.xml"))
{
    Console.Error.WriteLine("Archive does not contain mod.xml.");
    return 3;
}

using var metadata = archive.GetData("mod.xml");
using var reader = new StreamReader(metadata);
Console.Write(await reader.ReadToEndAsync());
return 0;
