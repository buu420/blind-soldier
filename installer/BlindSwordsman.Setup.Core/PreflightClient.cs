namespace BlindSwordsman.Setup.Core;

public sealed record PreflightOptions(
    string? GameRoot,
    string? SteamRoot,
    string? ReloadedRoot,
    string? SeventhHeavenRoot);

public sealed class PreflightClient(PowerShellProcessRunner runner)
{
    public async Task<PreflightReport> RunAsync(
        string scriptPath,
        PreflightOptions options,
        string temporaryDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(temporaryDirectory);
        var resultPath = Path.Combine(temporaryDirectory, "preflight-" + Guid.NewGuid().ToString("N") + ".json");
        var arguments = new List<string>();
        AddOptional(arguments, "-GameRoot", options.GameRoot);
        AddOptional(arguments, "-SteamRoot", options.SteamRoot);
        AddOptional(arguments, "-ReloadedRoot", options.ReloadedRoot);
        AddOptional(arguments, "-SeventhHeavenRoot", options.SeventhHeavenRoot);
        arguments.Add("-ResultPath");
        arguments.Add(resultPath);
        try
        {
            var result = await runner.RunAsync(scriptPath, arguments, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Installer preflight failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
            }
            if (!File.Exists(resultPath))
            {
                throw new InvalidDataException("Installer preflight did not produce a result.");
            }
            return PreflightReportParser.Parse(await File.ReadAllTextAsync(resultPath, cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            if (File.Exists(resultPath))
            {
                File.Delete(resultPath);
            }
        }
    }

    private static void AddOptional(List<string> arguments, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            arguments.Add(name);
            arguments.Add(value);
        }
    }
}
