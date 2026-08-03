using BlindSwordsman.Setup.Core;

static class PreflightContractTests
{
    public static void Run()
    {
        ParsesDependencyReportInActionableOrder();
        RejectsInconsistentPreflightResult();
        BuildsHiddenPowerShellInvocationWithoutStringQuoting();
    }

    private static void ParsesDependencyReportInActionableOrder()
    {
        var report = PreflightReportParser.Parse("""
            {
              "schemaVersion": 1,
              "canInstall": false,
              "game": null,
              "reloadedRoot": "C:\\Reloaded-II",
              "seventhHeavenRoot": null,
              "dependencies": [
                { "id": "seventh-heaven", "name": "7th Heaven", "severity": "optional", "satisfied": false, "message": "Not found.", "path": null },
                { "id": "game", "name": "Final Fantasy VII", "severity": "blocking", "satisfied": false, "message": "Not found.", "path": null },
                { "id": "reloaded", "name": "Reloaded-II", "severity": "required", "satisfied": true, "message": "Ready.", "path": "C:\\Reloaded-II" }
              ]
            }
            """);

        Equal(false, report.CanInstall, "blocked preflight");
        Equal("game", report.Dependencies[0].Id, "blocking check first");
        Equal("reloaded", report.Dependencies[1].Id, "required check second");
        Equal("seventh-heaven", report.Dependencies[2].Id, "optional check last");
    }

    private static void RejectsInconsistentPreflightResult()
    {
        var inconsistent = """
            {
              "schemaVersion": 1,
              "canInstall": true,
              "game": null,
              "reloadedRoot": null,
              "seventhHeavenRoot": null,
              "dependencies": [
                { "id": "game", "name": "Final Fantasy VII", "severity": "blocking", "satisfied": false, "message": "Not found.", "path": null }
              ]
            }
            """;
        Throws<InvalidDataException>(() => PreflightReportParser.Parse(inconsistent), "inconsistent installable report");
    }

    private static void BuildsHiddenPowerShellInvocationWithoutStringQuoting()
    {
        var info = PowerShellProcessRunner.CreateStartInfo(
            "C:\\A folder\\preflight.ps1",
            ["-GameRoot", "C:\\Games\\Final Fantasy VII", "-ResultPath", "C:\\Temp\\result.json"]);

        Equal(false, info.UseShellExecute, "shell execute disabled");
        Equal(true, info.CreateNoWindow, "hidden process");
        Equal(true, info.RedirectStandardOutput, "stdout captured");
        Equal("-File", info.ArgumentList[4], "file switch is a separate argument");
        Equal("C:\\A folder\\preflight.ps1", info.ArgumentList[5], "script path remains one argument");
        Equal("C:\\Games\\Final Fantasy VII", info.ArgumentList[7], "game path remains one argument");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static void Throws<TException>(Action action, string label)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{label}: expected {typeof(TException).Name}.");
    }
}
