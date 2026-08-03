using AsmResolver.PE.File;
using AsmResolver.PE.File.Headers;

if (args.Length != 2 || !args[0].Equals("--large-address-aware", StringComparison.Ordinal))
{
    Console.Error.WriteLine("Usage: Ff7PePatcher --large-address-aware <executable>");
    return 2;
}

string path = Path.GetFullPath(args[1]);
if (!File.Exists(path))
{
    Console.Error.WriteLine($"Executable not found: {path}");
    return 3;
}

try
{
    PEFile image = PEFile.FromFile(path);
    if (!image.FileHeader.Characteristics.HasFlag(Characteristics.LargeAddressAware))
    {
        image.FileHeader.Characteristics |= Characteristics.LargeAddressAware;
        image.Write(path);
    }

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 4;
}
