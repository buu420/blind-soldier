using WholeGameStateAudit.Tests;

// WholeGameStateAudit.Tests [--installed <game working dir>]
// Synthetic tests always run. Installed-data checks run only against an archive given here.
string? installed = null;
for (var index = 0; index < args.Length; index++)
{
    if (args[index] == "--installed" && index + 1 < args.Length)
    {
        installed = args[++index];
    }
}

DecoderTests.Run();
NativeWriterTests.Run();
FlowTests.Run();
ShippingTests.Run();
if (installed is not null)
{
    InstalledTests.Run(installed);
}

Console.WriteLine($"{Check.Passed} checks passed, {Check.Failures.Count} failed{(installed is null ? " (installed-data checks not run)" : string.Empty)}.");
foreach (var failure in Check.Failures)
{
    Console.WriteLine($"FAIL {failure}");
}

return Check.Failures.Count == 0 ? 0 : 1;
