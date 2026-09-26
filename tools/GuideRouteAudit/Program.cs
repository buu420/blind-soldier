using GuideRouteAudit;

if (args.Length == 4 && args[0] == "--dump-field")
    return InspectField.Run(args[1], int.Parse(args[2]), args[3]);
if (args.SequenceEqual(new[] { "--endpoint-tests" }))
{
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")))
        throw new InvalidOperationException("Endpoint tests require FF7_ACCESSIBILITY_DATA_ROOT.");
    Ff7.Accessibility.Reloaded.Tests.FieldNavigationTriggerFallbackTests.Run();
    Console.WriteLine("Native doorway fallback endpoint regressions passed.");
    return 0;
}
if (args.SequenceEqual(new[] { "--classification-tests" }))
{
    AssessmentTests.Run();
    NativeNpcWitnesses.TestReaderContract();
    NativeObjectWitnesses.TestReaderContract();
    NativeStoryWitnesses.TestReaderContract();
    EndpointEvidence.Test();
    return 0;
}
if (args.Length != 2)
{
    Console.Error.WriteLine("GuideRouteAudit <licensed-game-root> <report.json> | --classification-tests");
    return 2;
}
return NativeAudit.Run(args[0], args[1]);
