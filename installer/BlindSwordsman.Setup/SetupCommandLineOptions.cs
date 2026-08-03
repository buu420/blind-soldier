namespace BlindSwordsman.Setup;

public sealed record SetupCommandLineOptions(
    bool Uninstall,
    bool CheckForUpdates,
    string? LocalManifestPath,
    bool UpdateContinuation)
{
    public static SetupCommandLineOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var uninstall = false;
        var checkForUpdates = false;
        var continuation = false;
        string? localManifest = null;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            switch (argument)
            {
                case "--uninstall":
                    if (uninstall)
                    {
                        throw new ArgumentException("The --uninstall option was specified more than once.");
                    }
                    uninstall = true;
                    break;
                case "--check-for-updates":
                    if (checkForUpdates)
                    {
                        throw new ArgumentException("The --check-for-updates option was specified more than once.");
                    }
                    checkForUpdates = true;
                    break;
                case "--update-continuation":
                    if (continuation)
                    {
                        throw new ArgumentException("The --update-continuation option was specified more than once.");
                    }
                    continuation = true;
                    break;
                case "--local-manifest":
                    if (localManifest is not null || index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new ArgumentException("The --local-manifest option requires one file path.");
                    }
                    localManifest = arguments[++index];
                    break;
                default:
                    throw new ArgumentException($"Unknown setup option: {argument}");
            }
        }

        if (uninstall && checkForUpdates)
        {
            throw new ArgumentException("Uninstall and update-check modes cannot be used together.");
        }

        return new SetupCommandLineOptions(uninstall, checkForUpdates, localManifest, continuation);
    }
}
