using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime.Menus;
using Ff7.Accessibility.Steam2026X64.Runtime.Movies;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;
using Ff7.Accessibility.LegacyLayout;
using System.Text.Json;

if (args.Contains("--module-tests-only", StringComparer.OrdinalIgnoreCase))
{
    Steam2026BattleStatusHotkeyTests.Run();
    Steam2026BattleObservationTests.ReadsNativeEnemySkillCategoryMapping();
    Steam2026FieldNavigationRuntimeTests.Run();
    NavigationAutoWalkControllerTests.Run();
    Console.WriteLine("Steam 2026 x64 module tests passed.");
    return;
}

if (args.Contains("--repeat-speech-only", StringComparer.OrdinalIgnoreCase))
{
    Steam2026ResearchAccessibilityOutputTests.Run();
    Console.WriteLine("Steam 2026 x64 repeat-speech tests passed.");
    return;
}

if (args.Contains("--wall-market-squat-only", StringComparer.OrdinalIgnoreCase))
{
    Steam2026WallMarketSquatRuntimeTests.Run();
    Console.WriteLine("Steam 2026 x64 Wall Market squat cue runtime tests passed.");
    return;
}

if (args.Contains("--multilingual-menu-only", StringComparer.OrdinalIgnoreCase))
{
    Steam2026InGameMenuSpeechBridgeTests.Run();
    Console.WriteLine("Steam 2026 x64 multilingual menu regression tests passed.");
    return;
}

if (args.Contains("--kalm-junon-descriptions-only", StringComparer.OrdinalIgnoreCase))
{
    var cues = FieldCutsceneDescriptionCatalog.CreateKalmThroughLowerJunonDescriptions();
    AssertEqual(25, cues.Count, "shared Kalm through Lower Junon cue count in x64 build");
    AssertEqual(
        cues.Count,
        cues.Select(cue => cue.Key).Distinct().Count(),
        "shared Kalm through Lower Junon x64 cue keys");
    var supportedOpcodes = new HashSet<int>
    {
        FieldOpcodeAddressResolver.OpcodeRequestIndex,
        FieldOpcodeAddressResolver.OpcodeRequestSwIndex,
        FieldOpcodeAddressResolver.OpcodeRequestEwIndex,
        FieldOpcodeAddressResolver.OpcodeSplitIndex,
        FieldOpcodeAddressResolver.OpcodeWaitIndex,
        FieldOpcodeAddressResolver.OpcodeSoundIndex,
        FieldOpcodeAddressResolver.OpcodeMovieIndex
    };
    AssertEqual(
        true,
        cues.All(cue => supportedOpcodes.Contains(cue.Opcode)),
        "every new cue must use an x64 native-ingress opcode");
    AssertEqual(
        "332:238,277:0,279:4,282:48,282:32,311:207,312:106,313:50,318:26,323:48,323:236," +
        "332:3,304:66,290:4,292:22,292:10,327:290,332:85,343:24,348:13,349:99,428:142,429:117,434:9,359:79",
        string.Join(',', cues.Select(cue => $"{cue.FieldId}:{cue.ByteIndex}")),
        "shared Kalm through Lower Junon x64 cue ordering");
    Console.WriteLine("Steam 2026 x64 Kalm through Lower Junon description tests passed.");
    return;
}

const string nativePath =
    @"X:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\FFVII.exe";
const string legacyPath =
    @"X:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\ff7\workingdir\ff7_en.exe";

var native = Steam2026Fingerprint.Inspect(nativePath);
AssertEqual(true, native.IsSupported, "known native Steam 2026 executable fingerprint");
AssertEqual(true, native.Identity.Is64Bit, "known native executable architecture");
AssertEqual(
    "57A23D166D69E46B9E3339F779D4A3C4FEB402A989FA7291D0D9B4A1953ABB4B",
    native.Identity.Sha256,
    "known native executable hash");

var legacy = Steam2026Fingerprint.Inspect(legacyPath);
AssertEqual(false, legacy.IsSupported, "legacy x86 executable rejected by native backend");
AssertEqual(false, legacy.Identity.Is64Bit, "legacy executable architecture");

if (args.Contains("--field-countdown-only", StringComparer.OrdinalIgnoreCase))
{
    Steam2026FieldCountdownSpeechTests.Run();
    Console.WriteLine("Steam 2026 x64 field-countdown speech tests passed.");
    return;
}

if (args.Contains("--system-menu-only", StringComparer.OrdinalIgnoreCase))
{
    Steam2026SystemMenuSpeechTests.Run();
    Steam2026NativeSystemMenuDirectionInputTests.Run();
    Steam2026NativeSystemMenuReaderTests.Run();
    Console.WriteLine("Steam 2026 x64 native system-menu tests passed.");
    return;
}

if (args.Contains("--quit-menu-only", StringComparer.OrdinalIgnoreCase))
{
    AssertQuitConfirmationReaderMatchesDirectAndTranslatedGuestMemory();
    AssertQuitConfirmationReaderRejectsInactiveAndTornState();
    Steam2026ResearchObservationPumpTests.Run();
    Console.WriteLine("Steam 2026 x64 native Quit-confirmation tests passed.");
    return;
}

if (args.Contains("--dialogue-only", StringComparer.OrdinalIgnoreCase))
{
    Steam2026AskCursorCallbackTests.Run(native);
    Steam2026FieldMessageCallbackTests.Run(native);
    Steam2026FieldDialogueObservationTests.Run(native, legacy);
    Steam2026FieldDialogueSpeechStabilityTests.Run();
    Console.WriteLine("Steam 2026 x64 dialogue tests passed.");
    return;
}

if (args.Contains("--speech-priority-only", StringComparer.OrdinalIgnoreCase))
{
    Steam2026ResearchAccessibilityOutputTests.Run();
    Console.WriteLine("Steam 2026 x64 speech-priority tests passed.");
    return;
}

var alteredNative = InspectAlteredX64Copy(nativePath);
AssertEqual(false, alteredNative.IsSupported, "altered native x64 executable fingerprint rejected");
AssertEqual(true, alteredNative.Identity.Is64Bit, "altered native executable remains x64");
AssertFingerprintRejectsExecutableOpenForMutation(nativePath);
AssertFingerprintDoesNotReopenPathForVersion();
AssertBackendRejectsUnsupportedFingerprint(legacy, "legacy x86 fingerprint");
AssertBackendRejectsUnsupportedFingerprint(alteredNative, "altered native x64 fingerprint");
AssertBackendRejectsFabricatedRuntimeIdentity(native.Identity);
AssertFingerprintResultCannotBeFabricatedPublicly();

using var backend = new Steam2026X64RuntimeBackend(native);
var report = backend.ValidateCapabilities();
AssertEqual(false, report.HasFullParity, "research-only x64 backend parity gate");
foreach (var capability in Enum.GetValues<RuntimeCapability>())
{
    if (capability is RuntimeCapability.None or RuntimeCapability.FullParity)
    {
        continue;
    }

    AssertEqual(
        true,
        report.Failures.Any(failure => failure.Capability == capability),
        $"explicit missing x64 capability {capability}");
}
AssertCapabilityDiagnosticsNameConcreteRemainingGates(report);

var startThrew = false;
try
{
    backend.Start(new RecordingEventSink());
}
catch (InvalidOperationException)
{
    startThrew = true;
}

AssertEqual(true, startThrew, "incomplete x64 backend refuses startup");
Steam2026ForegroundInputTests.Run(native, legacy);
HighwayAutoSteeringControllerTests.Run();
NavigationAutoWalkControllerTests.Run();
HighwayEngagementSteeringTrackerTests.Run();
Steam2026ResearchSpeechPolicyTests.Run();
Steam2026ResearchAccessibilityOutputTests.Run();
Steam2026ResearchObservationPumpTests.Run();
Steam2026FieldFootstepCoordinatorTests.Run();
Steam2026FieldFootstepNavigationProbeTests.Run();
Steam2026RenderedMenuSpeechTrackerTests.Run();
Steam2026SystemMenuSpeechTests.Run();
Steam2026NativeSystemMenuDirectionInputTests.Run();
Steam2026NativeSystemMenuReaderTests.Run();
Steam2026InGameMenuSpeechBridgeTests.Run();
Steam2026TitleLoadMenuSpeechBridgeTests.Run();
Steam2026NativeTitleMenuReaderTests.Run();
Steam2026LifecycleObservationTests.Run(native, legacy);
Steam2026TrustSurfaceTests.Run(native);
Steam2026AskCursorCallbackTests.Run(native);
Steam2026FieldMessageCallbackTests.Run(native);
AssertNativeMovieCallbackContractValidatesExactAslrRelativeSignatures(native);
AssertNativeMovieCallbackContractRequiresSupportedExecutableImage(native, alteredNative);
AssertNativeMovieCallbackContractRejectsForeignAndStaleIdentities(native);
AssertNativeMovieCallbackCapturesAreStableUnderConcurrency(native);
AssertNativeMovieCallbackContractRejectsWrappingModuleBase(native);
AssertOpeningMovieLifecycleRequiresExactPathAndRisingStart(native);
AssertOpeningMovieLifecycleOrdersAndDeduplicatesEvents(native);
AssertOpeningMovieLifecycleRejectsFailedAndNonOpeningSequences(native);
AssertOpeningMovieLifecycleTerminalsResetCleanly(native);
AssertNativeMovieCallbackContractHasNoRuntimeSideEffects();
Steam2026NativeMovieIngressTests.Run(native);
Steam2026NativeMovieHookTests.Run(native);
AssertNativeMemoryRegionReportsReadability();
AssertTranslatedX86AddressSpaceReadsMappedPages();
AssertTranslatedX86AddressSpaceReadsAcrossNoncontiguousPages();
AssertTranslatedX86AddressSpaceRejectsOverflowedPageTableRange();
AssertTranslatedX86AddressSpaceReadsAllocatorAlignedHostPages();
AssertTranslatedX86AddressSpaceAllowsTrustedEntryInsideSplitPageTableRegion();
AssertTranslatedX86AddressSpaceRejectsUncommittedPageTableRegion();
AssertTranslatedX86AddressSpaceRejectsUnreadablePageTableRegion();
AssertTranslatedX86AddressSpaceRejectsRemapAfterCopy();
AssertTranslatedX86AddressSpaceRejectsInvalidMappings();
AssertTranslatedX86AddressSpaceValidatesRuntimeResolverSignature();
AssertTranslatedX86AddressSpaceRejectsUnstableResolverSignature();
AssertTranslatedX86AddressSpaceImplementsSharedGuestMemoryContract();
AssertCurrentProcessRegionQueryRecognizesExecutableCode();
AssertCurrentProcessMemoryReaderSurvivesForcedFinalization();
AssertTranslatedX86CallFrameReaderReadsGuestRegisters();
AssertTranslatedX86CallFrameReaderReadsCdeclArguments();
AssertTranslatedX86CallFrameReaderRefreshesRuntimeState();
AssertTranslatedX86CallFrameReaderRejectsInvalidState();
AssertTranslatedFunctionMapValidatorAndMenuCatalogMatchAtlas();
AssertTranslatedFunctionMapValidatorRejectsInvalidIdentity();
AssertTranslatedFunctionMapValidatorRequiresExecutableImageRegion();
AssertMenuCallCaptureDecodesExactArgumentOrders();
AssertMenuCallCaptureEnforcesTextBoundsAndTerminators();
AssertMenuCallCaptureRejectsInvalidTextPointers();
AssertMenuCallCaptureRejectsPerPageRemapAndTornEsp();
AssertMenuCallCaptureRejectsEspAbaAndStaleCallbackIdentity();
AssertMenuCaptureRequiresResolverIdentity();
AssertMenuCaptureRequiresContractOwnedToken();
AssertMenuCallbackContractHasNoRuntimeSideEffects();
Steam2026TranslatedMenuIngressTests.Run(native, legacy);
AssertInventoryItemReaderMatchesDirectAndTranslatedGuestMemory();
AssertInventoryItemReaderRejectsTranslatedPageRemapping();
AssertMenuObservationReaderMatchesDirectAndTranslatedGuestMemory();
AssertQuitConfirmationReaderMatchesDirectAndTranslatedGuestMemory();
AssertQuitConfirmationReaderRejectsInactiveAndTornState();
AssertMenuObservationReaderRejectsUnmappedDomains();
AssertMenuObservationReaderRejectsNestedPageRemapping();
AssertMenuObservationReaderRejectsMainMenuTransitionTearing();
AssertMenuObservationReaderRejectsPartySelectorTearing();
AssertMenuObservationReaderRejectsNestedStatusRemapping();
AssertMenuObservationReaderRejectsUnknownGuestWidgetAddresses();
AssertMenuObservationReaderPublicConstructionRequiresExactFingerprint(native, legacy);
Steam2026FieldObservationTests.Run(native, legacy);
Steam2026FieldNavigationObservationTests.Run(native, legacy);
Steam2026FieldNavigationRuntimeTests.Run();
Steam2026FieldObjectObservationTests.Run();
Steam2026FieldCutsceneWaitTests.Run(native);
Steam2026FieldDialogueObservationTests.Run(native, legacy);
Steam2026FieldDialogueSpeechStabilityTests.Run();
Steam2026BattleObservationTests.Run(native, legacy);
Steam2026BattleRendererIngressTests.Run(native, legacy);
Steam2026BattleAccessibilityCoordinatorTests.Run(native);
Steam2026NameEntryObservationTests.Run(native, legacy);
Steam2026SaveCandidateDiscoveryTests.Run(native, legacy);
Steam2026SaveContainerProbeTests.Run(native, legacy);
AssertEvidenceAtlasIsCompleteAndStaticCandidatesRemainUnavailable();
Console.WriteLine("Steam 2026 x64 accessibility skeleton tests passed.");

static void AssertCapabilityDiagnosticsNameConcreteRemainingGates(
    RuntimeCapabilityReport report)
{
    var expectedSignals = new Dictionary<RuntimeCapability, string>
    {
        [RuntimeCapability.Lifecycle] = "runtime-lifecycle-ingress",
        [RuntimeCapability.ForegroundInput] = "foreground-input-command-routing",
        [RuntimeCapability.Menus] = "translated-menu-detour-ingress",
        [RuntimeCapability.Dialogue] = "dialogue-hook-ingress",
        [RuntimeCapability.Field] = "field-frame-publication",
        [RuntimeCapability.Navigation] = "navigation-world-completeness",
        [RuntimeCapability.Battle] = "battle-event-ingress",
        [RuntimeCapability.Movies] = "native-movie-hook-installation",
        [RuntimeCapability.Saves] = "native-save-samples"
    };

    foreach (var expected in expectedSignals)
    {
        var failures = report.Failures
            .Where(failure => failure.Capability == expected.Key)
            .ToArray();
        AssertEqual(1, failures.Length, $"single concrete {expected.Key} gate");
        AssertEqual(expected.Value, failures[0].Signal, $"concrete {expected.Key} signal");
        AssertEqual(
            false,
            failures[0].Diagnostic.Contains("not yet validated", StringComparison.OrdinalIgnoreCase),
            $"concrete {expected.Key} diagnostic");
    }
}

static Steam2026FingerprintResult InspectAlteredX64Copy(string sourcePath)
{
    var copyPath = Path.Combine(
        Path.GetTempPath(),
        $"ff7-accessibility-altered-x64-{Guid.NewGuid():N}.exe");
    File.Copy(sourcePath, copyPath, overwrite: false);
    try
    {
        using (var stream = new FileStream(copyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            stream.Position = stream.Length - 1;
            var original = stream.ReadByte();
            if (original < 0)
            {
                throw new InvalidDataException("The copied executable is unexpectedly empty.");
            }

            stream.Position = stream.Length - 1;
            stream.WriteByte(unchecked((byte)(original ^ 0xFF)));
        }

        return Steam2026Fingerprint.Inspect(copyPath);
    }
    finally
    {
        File.Delete(copyPath);
    }
}

static void AssertFingerprintRejectsExecutableOpenForMutation(string sourcePath)
{
    var copyPath = Path.Combine(
        Path.GetTempPath(),
        $"ff7-accessibility-writable-x64-{Guid.NewGuid():N}.exe");
    File.Copy(sourcePath, copyPath, overwrite: false);
    try
    {
        using var writable = new FileStream(
            copyPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete);
        var rejected = false;
        try
        {
            _ = Steam2026Fingerprint.Inspect(copyPath);
        }
        catch (IOException)
        {
            rejected = true;
        }

        AssertEqual(true, rejected, "fingerprint rejects executable open for mutation");
    }
    finally
    {
        File.Delete(copyPath);
    }
}

static void AssertFingerprintDoesNotReopenPathForVersion()
{
    var prototypeRoot = FindAccessibilityPrototypeRoot();
    var sourcePath = Path.Combine(
        prototypeRoot,
        "reloaded",
        "Ff7.Accessibility.Steam2026X64",
        "Steam2026Fingerprint.cs");
    var source = File.ReadAllText(sourcePath);
    AssertEqual(
        false,
        source.Contains("FileVersionInfo.GetVersionInfo", StringComparison.Ordinal),
        "fingerprint does not reopen pathname for version metadata");
}

static void AssertBackendRejectsUnsupportedFingerprint(
    Steam2026FingerprintResult fingerprint,
    string label)
{
    Steam2026X64RuntimeBackend? candidate = null;
    var rejected = false;
    try
    {
        candidate = new Steam2026X64RuntimeBackend(fingerprint);
    }
    catch (ArgumentException)
    {
        rejected = true;
    }
    finally
    {
        candidate?.Dispose();
    }

    AssertEqual(true, rejected, $"backend rejects {label}");
}

static void AssertBackendRejectsFabricatedRuntimeIdentity(RuntimeIdentity inspectedIdentity)
{
    var fabricated = inspectedIdentity with
    {
        ExecutablePath = @"C:\fabricated\FFVII.exe"
    };
    var identityConstructor = typeof(Steam2026X64RuntimeBackend).GetConstructor([typeof(RuntimeIdentity)]);
    AssertEqual(true, identityConstructor is null, "backend has no RuntimeIdentity constructor");

    object? candidate = null;
    var rejected = false;
    try
    {
        candidate = Activator.CreateInstance(typeof(Steam2026X64RuntimeBackend), fabricated);
    }
    catch (MissingMethodException)
    {
        rejected = true;
    }
    finally
    {
        (candidate as IDisposable)?.Dispose();
    }

    AssertEqual(true, rejected, "backend rejects fabricated RuntimeIdentity");
}

static void AssertFingerprintResultCannotBeFabricatedPublicly()
{
    AssertEqual(
        0,
        typeof(Steam2026FingerprintResult).GetConstructors().Length,
        "fingerprint result has no public constructor");
}

static void AssertTranslatedFunctionMapValidatorAndMenuCatalogMatchAtlas()
{
    const ulong moduleBase = 0x0000000180000000;
    const ulong moduleImageSize = 0x02100000;
    var cases = GetMenuCallbackCases();
    var memory = new FakeNativeMemoryReader();
    MapExecutableMenuCodeRegion(memory, moduleBase);
    foreach (var item in cases)
    {
        WriteTranslatedFunctionIdentity(memory, moduleBase, item);
    }

    var validator = new TranslatedFunctionMapValidator(moduleBase, moduleImageSize, memory);
    var catalog = new Steam2026MenuCallbackCatalog(validator);
    foreach (var item in cases)
    {
        var metadata = Steam2026MenuCallbackCatalog.GetMetadata(item.Kind);
        AssertEqual(item.Kind, metadata.Kind, $"menu {item.Kind} catalog kind");
        AssertEqual(item.LegacyVirtualAddress, metadata.FunctionMap.LegacyVirtualAddress, $"menu {item.Kind} legacy VA");
        AssertEqual(item.MappingRecordRva, metadata.FunctionMap.MappingRecordRva, $"menu {item.Kind} map-record RVA");
        AssertEqual(item.HostRva, metadata.FunctionMap.HostRva, $"menu {item.Kind} host RVA");
        AssertEqual(item.PrefixHex, metadata.FunctionMap.ExpectedPrefixHex, $"menu {item.Kind} exact host prefix");
        AssertEqual(TranslatedMenuHostAbi.TranslatedX86VoidNoArguments, metadata.HostAbi, $"menu {item.Kind} translated host ABI");
        AssertEqual(item.CaptureEligible, metadata.IsCaptureEligible, $"menu {item.Kind} capture eligibility");

        AssertEqual(true, catalog.TryValidateIdentity(item.Kind, out var identity), $"menu {item.Kind} validated identity");
        AssertEqual(moduleBase + item.HostRva, identity.HostAddress, $"menu {item.Kind} relocated host target");
        AssertEqual(metadata, identity.Metadata, $"menu {item.Kind} immutable metadata");
        AssertEqual(
            item.CaptureEligible,
            catalog.TryGetValidatedCaptureTarget(item.Kind, out var captureTarget),
            $"menu {item.Kind} capture target availability");
        AssertEqual(item.CaptureEligible ? moduleBase + item.HostRva : 0ul, captureTarget, $"menu {item.Kind} capture target");
    }

    var cursor = cases.Single(item => item.Kind == Steam2026MenuCallbackKind.CursorA);
    memory.Write(moduleBase + cursor.HostRva, [0x90]);
    AssertEqual(false, catalog.TryValidateIdentity(cursor.Kind, out var changedIdentity), "menu catalog revalidates prefix every request");
    AssertEqual(default(Steam2026MenuCallbackIdentity), changedIdentity, "menu catalog clears changed identity");
}

static void AssertTranslatedFunctionMapValidatorRejectsInvalidIdentity()
{
    const ulong moduleBase = 0x0000000190000000;
    const ulong moduleImageSize = 0x02100000;
    var item = GetMenuCallbackCases().Single(entry => entry.Kind == Steam2026MenuCallbackKind.EncodedTextA);
    var definition = Steam2026MenuCallbackCatalog.GetMetadata(item.Kind).FunctionMap;

    var unreadableRecordMemory = new FakeNativeMemoryReader();
    MapExecutableMenuCodeRegion(unreadableRecordMemory, moduleBase);
    var unreadableRecord = new TranslatedFunctionMapValidator(moduleBase, moduleImageSize, unreadableRecordMemory);
    AssertEqual(false, unreadableRecord.TryValidate(definition, out var unreadableRecordTarget), "unreadable translated map record");
    AssertEqual(0ul, unreadableRecordTarget, "unreadable translated map record clears target");

    var highLegacyMemory = new FakeNativeMemoryReader();
    MapExecutableMenuCodeRegion(highLegacyMemory, moduleBase);
    WriteTranslatedFunctionIdentity(highLegacyMemory, moduleBase, item);
    highLegacyMemory.Write(moduleBase + item.MappingRecordRva, BitConverter.GetBytes(0x0000000100000000ul | item.LegacyVirtualAddress));
    AssertEqual(
        false,
        new TranslatedFunctionMapValidator(moduleBase, moduleImageSize, highLegacyMemory).TryValidate(definition, out _),
        "translated map legacy VA must be zero-extended");

    var wrongHostMemory = new FakeNativeMemoryReader();
    MapExecutableMenuCodeRegion(wrongHostMemory, moduleBase);
    WriteTranslatedFunctionIdentity(wrongHostMemory, moduleBase, item);
    wrongHostMemory.Write(moduleBase + item.MappingRecordRva + sizeof(ulong), BitConverter.GetBytes(moduleBase + item.HostRva + 1));
    AssertEqual(
        false,
        new TranslatedFunctionMapValidator(moduleBase, moduleImageSize, wrongHostMemory).TryValidate(definition, out _),
        "translated map relocated host mismatch");

    var unreadablePrefixMemory = new FakeNativeMemoryReader();
    MapExecutableMenuCodeRegion(unreadablePrefixMemory, moduleBase);
    unreadablePrefixMemory.Write(moduleBase + item.MappingRecordRva, BitConverter.GetBytes((ulong)item.LegacyVirtualAddress));
    unreadablePrefixMemory.Write(moduleBase + item.MappingRecordRva + sizeof(ulong), BitConverter.GetBytes(moduleBase + item.HostRva));
    AssertEqual(
        false,
        new TranslatedFunctionMapValidator(moduleBase, moduleImageSize, unreadablePrefixMemory).TryValidate(definition, out _),
        "unreadable translated host prefix");

    var mutatedPrefixMemory = new FakeNativeMemoryReader();
    MapExecutableMenuCodeRegion(mutatedPrefixMemory, moduleBase);
    WriteTranslatedFunctionIdentity(mutatedPrefixMemory, moduleBase, item);
    mutatedPrefixMemory.Write(moduleBase + item.HostRva + 8, [0x90]);
    AssertEqual(
        false,
        new TranslatedFunctionMapValidator(moduleBase, moduleImageSize, mutatedPrefixMemory).TryValidate(definition, out _),
        "mutated translated host prefix");

    var tornRecordMemory = new FakeNativeMemoryReader();
    MapExecutableMenuCodeRegion(tornRecordMemory, moduleBase);
    WriteTranslatedFunctionIdentity(tornRecordMemory, moduleBase, item);
    var tearingRecordReader = new TearingNativeMemoryReader(
        tornRecordMemory,
        moduleBase + item.MappingRecordRva,
        triggerRead: 2,
        () => tornRecordMemory.Write(
            moduleBase + item.MappingRecordRva + sizeof(ulong),
            BitConverter.GetBytes(moduleBase + item.HostRva + 1)));
    AssertEqual(
        false,
        new TranslatedFunctionMapValidator(moduleBase, moduleImageSize, tearingRecordReader).TryValidate(definition, out _),
        "torn translated map record rejected within one validation");

    var tornPrefixMemory = new FakeNativeMemoryReader();
    MapExecutableMenuCodeRegion(tornPrefixMemory, moduleBase);
    WriteTranslatedFunctionIdentity(tornPrefixMemory, moduleBase, item);
    var tearingPrefixReader = new TearingNativeMemoryReader(
        tornPrefixMemory,
        moduleBase + item.HostRva,
        triggerRead: 2,
        () => tornPrefixMemory.Write(moduleBase + item.HostRva + 8, [0x90]));
    AssertEqual(
        false,
        new TranslatedFunctionMapValidator(moduleBase, moduleImageSize, tearingPrefixReader).TryValidate(definition, out _),
        "torn translated host prefix rejected within one validation");

    var wrappingDefinition = definition with { MappingRecordRva = 0x100 };
    AssertEqual(
        false,
        new TranslatedFunctionMapValidator(ulong.MaxValue - 0x7F, 0x40, new FakeNativeMemoryReader())
            .TryValidate(wrappingDefinition, out var wrappingTarget),
        "translated map module-base arithmetic wraps");
    AssertEqual(0ul, wrappingTarget, "translated map overflow clears target");

    AssertEqual(
        false,
        new TranslatedFunctionMapValidator(moduleBase, moduleImageSize, mutatedPrefixMemory)
            .TryValidate(default, out _),
        "invalid translated map definition rejected");
}

static void AssertTranslatedFunctionMapValidatorRequiresExecutableImageRegion()
{
    const ulong moduleBase = 0x00000001A0000000;
    const ulong moduleImageSize = 0x02100000;
    var item = GetMenuCallbackCases().Single(entry => entry.Kind == Steam2026MenuCallbackKind.AsciiRenderer);
    var definition = Steam2026MenuCallbackCatalog.GetMetadata(item.Kind).FunctionMap;
    var prefixLength = Convert.FromHexString(item.PrefixHex).Length;
    var target = moduleBase + item.HostRva;

    var unavailableRegion = new FakeNativeMemoryReader();
    WriteTranslatedFunctionIdentity(unavailableRegion, moduleBase, item);
    AssertEqual(
        false,
        new TranslatedFunctionMapValidator(moduleBase, moduleImageSize, unavailableRegion).TryValidate(definition, out _),
        "unavailable executable-region query fails closed");

    var readWriteOnly = new FakeNativeMemoryReader();
    WriteTranslatedFunctionIdentity(readWriteOnly, moduleBase, item);
    readWriteOnly.MapRegion(target, (ulong)prefixLength, moduleBase, isCommitted: true, isExecutable: false);
    AssertEqual(
        false,
        new TranslatedFunctionMapValidator(moduleBase, moduleImageSize, readWriteOnly).TryValidate(definition, out _),
        "RW-only translated target rejected");

    var uncommitted = new FakeNativeMemoryReader();
    WriteTranslatedFunctionIdentity(uncommitted, moduleBase, item);
    uncommitted.MapRegion(target, (ulong)prefixLength, moduleBase, isCommitted: false, isExecutable: true);
    AssertEqual(
        false,
        new TranslatedFunctionMapValidator(moduleBase, moduleImageSize, uncommitted).TryValidate(definition, out _),
        "uncommitted executable translated target rejected");

    var splitRegion = new FakeNativeMemoryReader();
    WriteTranslatedFunctionIdentity(splitRegion, moduleBase, item);
    splitRegion.MapRegion(target, (ulong)prefixLength - 1, moduleBase, isCommitted: true, isExecutable: true);
    splitRegion.MapRegion(target + (ulong)prefixLength - 1, 1, moduleBase, isCommitted: true, isExecutable: true);
    AssertEqual(
        false,
        new TranslatedFunctionMapValidator(moduleBase, moduleImageSize, splitRegion).TryValidate(definition, out _),
        "translated prefix crossing executable-region boundary rejected");

    var foreignAllocation = new FakeNativeMemoryReader();
    WriteTranslatedFunctionIdentity(foreignAllocation, moduleBase, item);
    foreignAllocation.MapRegion(target, (ulong)prefixLength, moduleBase + 0x1000, isCommitted: true, isExecutable: true);
    AssertEqual(
        false,
        new TranslatedFunctionMapValidator(moduleBase, moduleImageSize, foreignAllocation).TryValidate(definition, out _),
        "translated target in foreign allocation rejected");

    var outsideImage = new FakeNativeMemoryReader();
    var outsideDefinition = definition with { HostRva = moduleImageSize - 8 };
    outsideImage.Write(moduleBase + outsideDefinition.MappingRecordRva, BitConverter.GetBytes((ulong)outsideDefinition.LegacyVirtualAddress));
    outsideImage.Write(moduleBase + outsideDefinition.MappingRecordRva + sizeof(ulong), BitConverter.GetBytes(moduleBase + outsideDefinition.HostRva));
    outsideImage.Write(moduleBase + outsideDefinition.HostRva, Convert.FromHexString(outsideDefinition.ExpectedPrefixHex));
    outsideImage.MapRegion(
        moduleBase + outsideDefinition.HostRva,
        (ulong)prefixLength,
        moduleBase,
        isCommitted: true,
        isExecutable: true);
    AssertEqual(
        false,
        new TranslatedFunctionMapValidator(moduleBase, moduleImageSize, outsideImage).TryValidate(outsideDefinition, out _),
        "translated prefix crossing main-image boundary rejected");

    var validThenRemoved = new FakeNativeMemoryReader();
    WriteTranslatedFunctionIdentity(validThenRemoved, moduleBase, item);
    validThenRemoved.MapRegion(target, (ulong)prefixLength, moduleBase, isCommitted: true, isExecutable: true);
    var validator = new TranslatedFunctionMapValidator(moduleBase, moduleImageSize, validThenRemoved);
    AssertEqual(true, validator.TryValidate(definition, out _), "committed executable image target accepted");
    validThenRemoved.ClearRegions();
    AssertEqual(false, validator.TryValidate(definition, out var removedTarget), "executable region revalidated every request");
    AssertEqual(0ul, removedTarget, "removed executable region clears target");
}

static MenuCallbackCase[] GetMenuCallbackCases() =>
[
    new(Steam2026MenuCallbackKind.CursorB, 0x006EB3B8, 0x016F5180, 0x01118430, "40574883EC308B0D8C11F20083C1FC48", true),
    new(Steam2026MenuCallbackKind.CursorA, 0x006F0D7D, 0x016F5240, 0x0113D0D0, "40574883EC308B0DECC4EF0083C1FC48", true),
    new(Steam2026MenuCallbackKind.WidgetConstructor, 0x006F4D30, 0x016F52E0, 0x01158060, "48895C2408574883EC208B0D5815EE00", false),
    new(Steam2026MenuCallbackKind.ActiveWidgetUpdate, 0x006F4DB2, 0x016F52F0, 0x011584F0, "48895C2408574883EC208B0DC810EE00", true),
    new(Steam2026MenuCallbackKind.EncodedTextB, 0x006F5B03, 0x016F53A0, 0x0115D910, "40534883EC208B0DACBCED008B1DAABC", true),
    new(Steam2026MenuCallbackKind.EncodedTextA, 0x006FAB2F, 0x016F54B0, 0x01180DF0, "40534883EC208B0DCC87EB008B1DCA87", true),
    new(Steam2026MenuCallbackKind.AsciiRenderer, 0x0072F9F4, 0x016F7180, 0x012995F0, "40574883EC508B0DCCFFD90083C1FC48", true)
];

static void WriteTranslatedFunctionIdentity(
    FakeNativeMemoryReader memory,
    ulong moduleBase,
    MenuCallbackCase item)
{
    memory.Write(moduleBase + item.MappingRecordRva, BitConverter.GetBytes((ulong)item.LegacyVirtualAddress));
    memory.Write(moduleBase + item.MappingRecordRva + sizeof(ulong), BitConverter.GetBytes(moduleBase + item.HostRva));
    memory.Write(moduleBase + item.HostRva, Convert.FromHexString(item.PrefixHex));
}

static void MapExecutableMenuCodeRegion(FakeNativeMemoryReader memory, ulong moduleBase) =>
    memory.MapRegion(
        moduleBase + 0x01000000,
        0x00400000,
        moduleBase,
        isCommitted: true,
        isExecutable: true);

static void AssertMenuCallCaptureDecodesExactArgumentOrders()
{
    var fixture = new TranslatedCallCaptureFixture();
    var decoder = fixture.CreateDecoder();

    fixture.WriteCall(0x00110000, [unchecked((uint)-12), 34, 0x55667788]);
    foreach (var kind in new[] { Steam2026MenuCallbackKind.CursorA, Steam2026MenuCallbackKind.CursorB })
    {
        AssertEqual(true, decoder.TryCaptureCursor(kind, out var cursor), $"{kind} capture");
        AssertEqual(new TranslatedMenuCursorObservation(kind, -12, 34, unchecked((int)0x55667788)), cursor, $"{kind} x/y/context order");
    }

    const uint highWidgetAddress = 0xF1234567;
    fixture.WriteCall(0x00111000, [highWidgetAddress]);
    AssertEqual(true, decoder.TryCaptureActiveWidget(out var widget), "active-widget capture");
    AssertEqual(new TranslatedMenuWidgetObservation(highWidgetAddress), widget, "active-widget unsigned guest pointer");

    const uint encodedText = 0x00220000;
    fixture.WriteGuest(encodedText, [0x28, 0x49, 0xFF]);
    foreach (var kind in new[] { Steam2026MenuCallbackKind.EncodedTextA, Steam2026MenuCallbackKind.EncodedTextB })
    {
        fixture.WriteCall(0x00112000, [unchecked((uint)-4), 77, encodedText, 0x22, 0x3344]);
        AssertEqual(true, decoder.TryCaptureEncodedText(kind, out var captured), $"{kind} capture");
        AssertEqual(
            new TranslatedMenuTextObservation(kind, "Hi", -4, 77, 0x22, 0x3344),
            captured,
            $"{kind} x/y/pointer/color/context order");
    }

    const uint asciiText = 0x00221000;
    fixture.WriteGuest(asciiText, [0x4D, 0x65, 0x6E, 0x75, 0x00]);
    fixture.WriteCall(0x00113000, [asciiText, 12, unchecked((uint)-9), 0x4455, 0x6677]);
    AssertEqual(true, decoder.TryCaptureAsciiRenderer(out var ascii), "ASCII renderer capture");
    AssertEqual(
        new TranslatedMenuTextObservation(Steam2026MenuCallbackKind.AsciiRenderer, "Menu", 12, -9, 0x4455, 0x6677),
        ascii,
        "ASCII pointer/x/y/color/context order");

    AssertEqual(false, decoder.TryCaptureCursor(Steam2026MenuCallbackKind.EncodedTextA, out _), "non-cursor source rejected");
    AssertEqual(false, decoder.TryCaptureEncodedText(Steam2026MenuCallbackKind.CursorA, out _), "non-encoded source rejected");
}

static void AssertMenuCallCaptureEnforcesTextBoundsAndTerminators()
{
    var fixture = new TranslatedCallCaptureFixture();
    var decoder = fixture.CreateDecoder();

    const uint encodedAtLimit = 0x00230000;
    fixture.WriteGuest(encodedAtLimit, Enumerable.Repeat((byte)0x21, 127).Append((byte)0xFF).ToArray());
    fixture.WriteCall(0x00120000, [1, 2, encodedAtLimit, 3, 4]);
    AssertEqual(true, decoder.TryCaptureEncodedText(Steam2026MenuCallbackKind.EncodedTextA, out var encodedLimit), "encoded terminator at byte 128");
    AssertEqual(127, encodedLimit.Text.Length, "encoded 128-byte inclusive bound payload length");

    const uint encodedOverLimit = 0x00231000;
    fixture.WriteGuest(encodedOverLimit, Enumerable.Repeat((byte)0x21, 128).Append((byte)0xFF).ToArray());
    fixture.WriteCall(0x00121000, [1, 2, encodedOverLimit, 3, 4]);
    AssertEqual(false, decoder.TryCaptureEncodedText(Steam2026MenuCallbackKind.EncodedTextA, out var encodedOver), "encoded terminator after byte 128 rejected");
    AssertEqual(default(TranslatedMenuTextObservation), encodedOver, "overlong encoded observation cleared");

    const uint asciiAtLimit = 0x00232000;
    fixture.WriteGuest(asciiAtLimit, Enumerable.Repeat((byte)'A', 127).Append((byte)0).ToArray());
    fixture.WriteCall(0x00122000, [asciiAtLimit, 1, 2, 3, 4]);
    AssertEqual(true, decoder.TryCaptureAsciiRenderer(out var asciiLimit), "ASCII terminator at byte 128");
    AssertEqual(127, asciiLimit.Text.Length, "ASCII 128-byte inclusive bound payload length");

    const uint asciiOverLimit = 0x00233000;
    fixture.WriteGuest(asciiOverLimit, Enumerable.Repeat((byte)'A', 128).Append((byte)0).ToArray());
    fixture.WriteCall(0x00123000, [asciiOverLimit, 1, 2, 3, 4]);
    AssertEqual(false, decoder.TryCaptureAsciiRenderer(out _), "ASCII terminator after byte 128 rejected");

    const uint unterminated = 0x00234000;
    fixture.WriteGuest(unterminated, Enumerable.Repeat((byte)0x21, 128).ToArray());
    fixture.WriteCall(0x00124000, [1, 2, unterminated, 3, 4]);
    AssertEqual(false, decoder.TryCaptureEncodedText(Steam2026MenuCallbackKind.EncodedTextB, out _), "unterminated encoded text rejected");
    fixture.WriteCall(0x00125000, [unterminated, 1, 2, 3, 4]);
    AssertEqual(false, decoder.TryCaptureAsciiRenderer(out _), "unterminated ASCII text rejected");
}

static void AssertMenuCallCaptureRejectsInvalidTextPointers()
{
    var fixture = new TranslatedCallCaptureFixture();
    var decoder = fixture.CreateDecoder();

    fixture.WriteCall(0x00130000, [1, 2, 0x00333000, 3, 4]);
    AssertEqual(false, decoder.TryCaptureEncodedText(Steam2026MenuCallbackKind.EncodedTextA, out _), "unmapped encoded text pointer rejected");

    fixture.WriteCall(0x00131000, [0x80000000, 1, 2, 3, 4]);
    AssertEqual(false, decoder.TryCaptureAsciiRenderer(out _), "high-bit ASCII text pointer rejected");

    fixture.WriteCall(0x00132000, [1, 2, 0xFFFFFFFE, 3, 4]);
    AssertEqual(false, decoder.TryCaptureEncodedText(Steam2026MenuCallbackKind.EncodedTextB, out _), "wrapping encoded text pointer rejected");

    fixture.WriteCall(0x00133000, [0, 1, 2, 3, 4]);
    AssertEqual(false, decoder.TryCaptureAsciiRenderer(out _), "null ASCII text pointer rejected");
}

static void AssertMenuCallCaptureRejectsPerPageRemapAndTornEsp()
{
    const uint crossPageText = 0x00123FFE;
    var remapFixture = new TranslatedCallCaptureFixture();
    remapFixture.WriteGuest(crossPageText, [0x28, 0x49, 0x01, 0xFF]);
    remapFixture.WriteCall(0x00140000, [1, 2, crossPageText, 3, 4]);
    const ulong replacementHostPage = 0x0000000500000000;
    remapFixture.Native.Write(replacementHostPage, [0x02, 0xFF]);
    var secondPageEntry = remapFixture.GetPageTableEntryAddress(crossPageText + 2);
    var remappingMemory = new RemappingNativeMemoryReader(
        remapFixture.Native,
        secondPageEntry,
        triggerRead: 3,
        () => remapFixture.MapGuestPage(crossPageText + 2, replacementHostPage));
    var remappingDecoder = remapFixture.CreateDecoder(remappingMemory);
    AssertEqual(
        false,
        remappingDecoder.TryCaptureEncodedText(Steam2026MenuCallbackKind.EncodedTextA, out var remapped),
        "cross-page text remap between complete captures rejected");
    AssertEqual(default(TranslatedMenuTextObservation), remapped, "remapped text observation cleared");

    var tornFixture = new TranslatedCallCaptureFixture();
    const uint firstEsp = 0x00150000;
    const uint secondEsp = 0x00151000;
    tornFixture.WriteCall(firstEsp, [10, 20, 30]);
    tornFixture.WriteStack(secondEsp, [11, 21, 31]);
    var tearingMemory = new TearingNativeMemoryReader(
        tornFixture.Native,
        TranslatedCallCaptureFixture.ModuleBase + TranslatedX86CallFrameReader.EspRva,
        triggerRead: 3,
        () => tornFixture.SetEsp(secondEsp));
    var tearingDecoder = tornFixture.CreateDecoder(tearingMemory);
    AssertEqual(
        false,
        tearingDecoder.TryCaptureCursor(Steam2026MenuCallbackKind.CursorA, out var torn),
        "torn guest ESP during argument capture rejected");
    AssertEqual(default(TranslatedMenuCursorObservation), torn, "torn cursor observation cleared");
}

static void AssertMenuCallCaptureRejectsEspAbaAndStaleCallbackIdentity()
{
    const uint firstEsp = 0x00160000;
    const uint secondEsp = 0x00161000;
    var abaFixture = new TranslatedCallCaptureFixture();
    abaFixture.WriteCall(firstEsp, [10, 20, 30]);
    abaFixture.WriteStack(secondEsp, [10, 20, 30]);
    var abaMemory = new EspAbaNativeMemoryReader(
        abaFixture.Native,
        TranslatedCallCaptureFixture.ModuleBase + TranslatedX86CallFrameReader.EspRva,
        switchRead: 3,
        restoreRead: 4,
        () => abaFixture.SetEsp(secondEsp),
        () => abaFixture.SetEsp(firstEsp));
    var abaContract = abaFixture.CreateDecoder(abaMemory);
    AssertEqual(
        false,
        abaContract.TryCaptureCursor(Steam2026MenuCallbackKind.CursorA, out var aba),
        "guest ESP ABA during menu capture rejected");
    AssertEqual(default(TranslatedMenuCursorObservation), aba, "ESP ABA cursor observation cleared");

    var identityFixture = new TranslatedCallCaptureFixture();
    identityFixture.WriteCall(firstEsp, [10, 20, 30]);
    var metadata = Steam2026MenuCallbackCatalog.GetMetadata(Steam2026MenuCallbackKind.CursorA);
    var hostAddress = TranslatedCallCaptureFixture.ModuleBase + metadata.FunctionMap.HostRva;
    var tearingIdentityMemory = new TearingNativeMemoryReader(
        identityFixture.Native,
        hostAddress,
        triggerRead: 3,
        () => identityFixture.Native.Write(hostAddress, [0x90]));
    var identityContract = identityFixture.CreateDecoder(tearingIdentityMemory);
    AssertEqual(
        false,
        identityContract.TryCaptureCursor(Steam2026MenuCallbackKind.CursorA, out var stale),
        "menu capture rejected after callback identity revalidation fails");
    AssertEqual(default(TranslatedMenuCursorObservation), stale, "stale callback cursor observation cleared");
}

static void AssertMenuCaptureRequiresContractOwnedToken()
{
    var assembly = typeof(Steam2026MenuCallbackCatalog).Assembly;
    var contractType = assembly.GetType(
        "Ff7.Accessibility.Steam2026X64.Runtime.Menus.Steam2026MenuCallbackContract",
        throwOnError: true)!;
    var decoderType = assembly.GetType(
        "Ff7.Accessibility.Steam2026X64.Runtime.Menus.Steam2026MenuCallCaptureDecoder",
        throwOnError: true)!;
    var tokenType = assembly.GetType(
        "Ff7.Accessibility.Steam2026X64.Runtime.Menus.Steam2026MenuCaptureToken",
        throwOnError: true)!;
    AssertEqual(false, contractType.IsPublic, "menu capture contract is research-internal");
    AssertEqual(false, decoderType.IsPublic, "raw menu capture decoder is non-public");
    AssertEqual(false, tokenType.IsPublic, "validated menu capture token is non-public");
    AssertEqual(0, decoderType.GetConstructors().Length, "raw menu capture decoder has no public constructor");
    AssertEqual(0, tokenType.GetConstructors().Length, "menu capture token has no public constructor");
}

static void AssertMenuCaptureRequiresResolverIdentity()
{
    var fixture = new TranslatedCallCaptureFixture();
    fixture.CorruptResolverSignature();
    var rejected = false;
    try
    {
        _ = fixture.CreateDecoder();
    }
    catch (InvalidDataException)
    {
        rejected = true;
    }

    AssertEqual(true, rejected, "menu capture contract requires stable translated resolver identity");
}

static void AssertMenuCallbackContractHasNoRuntimeSideEffects()
{
    var prototypeRoot = FindAccessibilityPrototypeRoot();
    var projectRoot = Path.Combine(
        prototypeRoot,
        "reloaded",
        "Ff7.Accessibility.Steam2026X64");
    var sliceFiles = new[]
    {
        Path.Combine(projectRoot, "INativeMemoryReader.cs"),
        Path.Combine(projectRoot, "CurrentProcessNativeMemoryReader.cs"),
        Path.Combine(projectRoot, "TranslatedFunctionMapValidator.cs"),
        Path.Combine(projectRoot, "Runtime", "Menus", "Steam2026MenuCallbackCatalog.cs"),
        Path.Combine(projectRoot, "Runtime", "Menus", "Steam2026MenuCallbackContract.cs"),
        Path.Combine(projectRoot, "Runtime", "Menus", "Steam2026MenuCallCaptureDecoder.cs")
    };
    var forbiddenRuntimeSurfaces = new[]
    {
        "Reloaded.Hooks",
        "IHook<",
        "CreateHook",
        "OriginalFunction",
        "IRuntimeEventSink",
        ".Publish(",
        "RuntimeCapability",
        "System.Delegate",
        " delegate "
    };

    foreach (var file in sliceFiles)
    {
        AssertEqual(true, File.Exists(file), $"static-audit source exists: {Path.GetFileName(file)}");
        var source = File.ReadAllText(file);
        foreach (var forbidden in forbiddenRuntimeSurfaces)
        {
            AssertEqual(false, source.Contains(forbidden, StringComparison.Ordinal), $"static-audit {Path.GetFileName(file)} excludes {forbidden}");
        }
    }

    var backendSource = File.ReadAllText(Path.Combine(projectRoot, "Steam2026X64RuntimeBackend.cs"));
    AssertEqual(false, backendSource.Contains("Steam2026MenuCallbackCatalog", StringComparison.Ordinal), "backend has no menu callback catalog integration");
    AssertEqual(false, backendSource.Contains("Steam2026MenuCallCaptureDecoder", StringComparison.Ordinal), "backend has no menu decoder integration");

    var decoderType = typeof(Steam2026MenuCallbackCatalog).Assembly.GetType(
        "Ff7.Accessibility.Steam2026X64.Runtime.Menus.Steam2026MenuCallCaptureDecoder",
        throwOnError: true)!;
    var publicDecoderMethods = decoderType.GetMethods()
        .Where(method => method.DeclaringType == decoderType)
        .ToArray();
    AssertEqual(
        true,
        publicDecoderMethods.All(method =>
            !typeof(Delegate).IsAssignableFrom(method.ReturnType)
            && method.GetParameters().All(parameter => !typeof(Delegate).IsAssignableFrom(parameter.ParameterType))),
        "decoder exposes no typed host callback delegate");
    AssertEqual(
        false,
        Steam2026MenuCallbackCatalog.GetMetadata(Steam2026MenuCallbackKind.WidgetConstructor).IsCaptureEligible,
        "constructor remains identity-only in static audit");
}

static void AssertNativeMovieCallbackContractHasNoRuntimeSideEffects()
{
    var projectRoot = Path.Combine(
        FindAccessibilityPrototypeRoot(),
        "reloaded",
        "Ff7.Accessibility.Steam2026X64");
    var sliceFiles = new[]
    {
        Path.Combine(projectRoot, "Runtime", "Movies", "NativeMovieCallbackContract.cs"),
        Path.Combine(projectRoot, "Runtime", "Movies", "OpeningMovieLifecycleObserver.cs")
    };
    var forbiddenRuntimeSurfaces = new[]
    {
        "Reloaded.Hooks",
        "IHook<",
        "CreateHook",
        "OriginalFunction",
        "IRuntimeEventSink",
        ".Publish(",
        "RuntimeCapability",
        "System.Delegate",
        " delegate ",
        "TranslatedX86CallFrameReader"
    };

    foreach (var file in sliceFiles)
    {
        AssertEqual(true, File.Exists(file), $"movie static-audit source exists: {Path.GetFileName(file)}");
        var source = File.ReadAllText(file);
        foreach (var forbidden in forbiddenRuntimeSurfaces)
        {
            AssertEqual(
                false,
                source.Contains(forbidden, StringComparison.Ordinal),
                $"movie static-audit {Path.GetFileName(file)} excludes {forbidden}");
        }
    }

    var backendSource = File.ReadAllText(Path.Combine(projectRoot, "Steam2026X64RuntimeBackend.cs"));
    AssertEqual(
        false,
        backendSource.Contains(nameof(NativeMovieCallbackContract), StringComparison.Ordinal),
        "backend has no movie callback contract integration");
    AssertEqual(
        false,
        backendSource.Contains(nameof(OpeningMovieLifecycleObserver), StringComparison.Ordinal),
        "backend has no movie lifecycle integration");

    var publicContractMethods = typeof(NativeMovieCallbackContract).GetMethods()
        .Where(method => method.DeclaringType == typeof(NativeMovieCallbackContract))
        .ToArray();
    AssertEqual(
        true,
        publicContractMethods.All(method =>
            !typeof(Delegate).IsAssignableFrom(method.ReturnType)
            && method.GetParameters().All(parameter => !typeof(Delegate).IsAssignableFrom(parameter.ParameterType))),
        "movie callback contract exposes no typed native delegate");
    AssertEqual(
        false,
        NativeMovieCallbackContract.GetMetadata(NativeMovieCallbackKind.FrameGetter).IsInlineDetourEligible,
        "frame getter remains identity-only in static audit");
}

static void AssertNativeMovieCallbackContractValidatesExactAslrRelativeSignatures(
    Steam2026FingerprintResult supportedRuntime)
{
    const ulong moduleBase = 0x0000000180000000;
    var fixture = CreateNativeMovieFixture(supportedRuntime, moduleBase);

    foreach (var item in GetNativeMovieCases())
    {
        var metadata = NativeMovieCallbackContract.GetMetadata(item.Kind);
        AssertEqual(item.Shape, metadata.Shape, $"native movie {item.Kind} callback shape");
        AssertEqual(
            NativeMovieCallbackAbi.MicrosoftX64,
            metadata.Shape.Abi,
            $"native movie {item.Kind} ABI");
        AssertEqual(item.Hookable, metadata.IsInlineDetourEligible, $"native movie {item.Kind} detour eligibility");
        AssertEqual(
            true,
            fixture.Contract.TryValidateIdentity(item.Kind, out var identity),
            $"native movie {item.Kind} exact identity");
        AssertEqual(item.Kind, identity.Metadata.Kind, $"native movie {item.Kind} identity kind");
        AssertEqual(item.Shape, identity.Metadata.Shape, $"native movie {item.Kind} identity shape");
        AssertEqual(moduleBase + item.Rva, identity.Address, $"native movie {item.Kind} ASLR address");
        AssertEqual(
            Steam2026Fingerprint.SupportedSha256,
            identity.RuntimeSha256,
            $"native movie {item.Kind} exact runtime hash");
    }

    var frameIdentity = RequireMovieIdentity(fixture.Contract, NativeMovieCallbackKind.FrameGetter);
    AssertEqual(
        false,
        fixture.Contract.TryCapturePrepare(
            frameIdentity,
            DateTime.UnixEpoch,
            @"C:\Games\FF7\data\movies\opening.avi",
            succeeded: true,
            out _),
        "frame getter cannot masquerade as prepare");
    AssertEqual(
        false,
        fixture.Contract.TryValidateIdentity((NativeMovieCallbackKind)int.MaxValue, out var unknownIdentity),
        "unknown native movie callback identity rejected");
    AssertEqual(0ul, unknownIdentity.Address, "unknown native movie identity cleared");
}

static void AssertNativeMovieCallbackContractRequiresSupportedExecutableImage(
    Steam2026FingerprintResult supportedRuntime,
    Steam2026FingerprintResult alteredRuntime)
{
    const ulong moduleBase = 0x0000000190000000;
    const ulong moduleImageSize = 0x02100000;
    var unsupportedRejected = false;
    try
    {
        _ = new NativeMovieCallbackContract(
            alteredRuntime,
            moduleBase,
            moduleImageSize,
            new FakeNativeMemoryReader());
    }
    catch (ArgumentException)
    {
        unsupportedRejected = true;
    }

    AssertEqual(true, unsupportedRejected, "altered runtime rejected by movie callback contract");

    var prepare = GetNativeMovieCases().Single(item => item.Kind == NativeMovieCallbackKind.Prepare);
    var prepareAddress = moduleBase + prepare.Rva;
    var memory = new FakeNativeMemoryReader();
    memory.Write(prepareAddress, Convert.FromHexString(prepare.SignatureHex));
    var contract = new NativeMovieCallbackContract(
        supportedRuntime,
        moduleBase,
        moduleImageSize,
        memory);

    AssertEqual(
        false,
        contract.TryValidateIdentity(NativeMovieCallbackKind.Prepare, out _),
        "unmapped movie callback rejected");

    memory.MapRegion(moduleBase, moduleImageSize, moduleBase, isCommitted: true, isExecutable: false);
    AssertEqual(
        false,
        contract.TryValidateIdentity(NativeMovieCallbackKind.Prepare, out _),
        "read-write movie callback rejected");

    memory.ClearRegions();
    memory.MapRegion(moduleBase, moduleImageSize, moduleBase, isCommitted: false, isExecutable: true);
    AssertEqual(
        false,
        contract.TryValidateIdentity(NativeMovieCallbackKind.Prepare, out _),
        "uncommitted movie callback rejected");

    memory.ClearRegions();
    memory.MapRegion(moduleBase, moduleImageSize, moduleBase + 0x1000, isCommitted: true, isExecutable: true);
    AssertEqual(
        false,
        contract.TryValidateIdentity(NativeMovieCallbackKind.Prepare, out _),
        "foreign allocation movie callback rejected");

    memory.ClearRegions();
    memory.MapRegion(
        moduleBase,
        moduleImageSize,
        moduleBase,
        isCommitted: true,
        isExecutable: true,
        isImage: false);
    AssertEqual(
        false,
        contract.TryValidateIdentity(NativeMovieCallbackKind.Prepare, out _),
        "private executable remap at main-image base rejected");

    memory.ClearRegions();
    memory.MapRegion(
        prepareAddress,
        (ulong)Convert.FromHexString(prepare.SignatureHex).Length - 1,
        moduleBase,
        isCommitted: true,
        isExecutable: true);
    AssertEqual(
        false,
        contract.TryValidateIdentity(NativeMovieCallbackKind.Prepare, out _),
        "split executable region movie callback rejected");

    memory.ClearRegions();
    memory.MapRegion(moduleBase, moduleImageSize, moduleBase, isCommitted: true, isExecutable: true);
    AssertEqual(
        true,
        contract.TryValidateIdentity(NativeMovieCallbackKind.Prepare, out _),
        "committed executable main-image movie callback accepted");

    var tooSmallImage = new NativeMovieCallbackContract(
        supportedRuntime,
        moduleBase,
        prepare.Rva + 8,
        memory);
    AssertEqual(
        false,
        tooSmallImage.TryValidateIdentity(NativeMovieCallbackKind.Prepare, out _),
        "movie callback crossing declared main image rejected");

    var tearingMemory = new FakeNativeMemoryReader();
    tearingMemory.Write(prepareAddress, Convert.FromHexString(prepare.SignatureHex));
    tearingMemory.MapRegion(moduleBase, moduleImageSize, moduleBase, isCommitted: true, isExecutable: true);
    var tearingReader = new TearingNativeMemoryReader(
        tearingMemory,
        prepareAddress,
        triggerRead: 2,
        () => tearingMemory.Write(prepareAddress + 7, [0x90]));
    var tearingContract = new NativeMovieCallbackContract(
        supportedRuntime,
        moduleBase,
        moduleImageSize,
        tearingReader);
    AssertEqual(
        false,
        tearingContract.TryValidateIdentity(NativeMovieCallbackKind.Prepare, out _),
        "torn movie callback prefix rejected");
}

static void AssertNativeMovieCallbackContractRejectsForeignAndStaleIdentities(
    Steam2026FingerprintResult supportedRuntime)
{
    const string openingPath = @"C:\Games\FF7\data\movies\opening.avi";
    const ulong sharedModuleBase = 0x00000001A0000000;
    var first = CreateNativeMovieFixture(supportedRuntime, sharedModuleBase);
    var second = CreateNativeMovieFixture(supportedRuntime, sharedModuleBase);
    var prepareIdentity = RequireMovieIdentity(first.Contract, NativeMovieCallbackKind.Prepare);
    var timestamp = new DateTime(2026, 7, 19, 22, 0, 0, DateTimeKind.Utc);

    AssertEqual(
        false,
        second.Contract.TryCapturePrepare(
            prepareIdentity,
            timestamp,
            openingPath,
            succeeded: true,
            out _),
        "foreign movie callback identity rejected");
    AssertEqual(
        false,
        first.Contract.TryCapturePrepare(
            default,
            timestamp,
            openingPath,
            succeeded: true,
            out var unvalidatedCapture),
        "unvalidated movie callback identity rejected");
    AssertEqual(0L, unvalidatedCapture.Sequence, "unvalidated movie capture cleared");

    var foreignCapture = CaptureMoviePrepare(
        first.Contract,
        prepareIdentity,
        timestamp,
        openingPath,
        succeeded: true);
    var secondObserver = new OpeningMovieLifecycleObserver(openingPath, second.Contract);
    var secondPrepareIdentity = RequireMovieIdentity(second.Contract, NativeMovieCallbackKind.Prepare);
    var secondStartIdentity = RequireMovieIdentity(second.Contract, NativeMovieCallbackKind.Start);
    AssertEqual<MovieLifecycleEvent?>(
        null,
        secondObserver.Observe(CaptureMoviePrepare(
            second.Contract,
            secondPrepareIdentity,
            timestamp,
            openingPath,
            succeeded: true)),
        "second contract valid prepare arms lifecycle");
    AssertEqual<MovieLifecycleEvent?>(
        null,
        secondObserver.Observe(foreignCapture),
        "foreign movie capture cannot alter lifecycle");
    AssertEqual<MovieLifecycleEvent?>(
        null,
        secondObserver.Observe(CaptureMovieStart(
            second.Contract,
            secondStartIdentity,
            timestamp.AddTicks(1),
            stateBefore: 0,
            stateAfter: 1)),
        "foreign movie capture resets prepared lifecycle fail closed");

    var firstObserver = new OpeningMovieLifecycleObserver(openingPath, first.Contract);
    var staleCapture = CaptureMoviePrepare(
        first.Contract,
        prepareIdentity,
        timestamp.AddSeconds(1),
        openingPath,
        succeeded: true);
    first.Memory.Write(prepareIdentity.Address + 7, [0x90]);
    AssertEqual<MovieLifecycleEvent?>(
        null,
        firstObserver.Observe(staleCapture),
        "mutated callback invalidates already captured prepare");
    AssertEqual(
        false,
        first.Contract.TryCapturePrepare(
            prepareIdentity,
            timestamp.AddSeconds(2),
            openingPath,
            succeeded: true,
            out _),
        "mutated callback rejects stale identity at capture time");

    var prepare = GetNativeMovieCases().Single(item => item.Kind == NativeMovieCallbackKind.Prepare);
    first.Memory.Write(prepareIdentity.Address, Convert.FromHexString(prepare.SignatureHex));
    var startIdentity = RequireMovieIdentity(first.Contract, NativeMovieCallbackKind.Start);
    var startAfterRejectedPrepare = CaptureMovieStart(
        first.Contract,
        startIdentity,
        timestamp.AddSeconds(3),
        stateBefore: 0,
        stateAfter: 1);
    AssertEqual<MovieLifecycleEvent?>(
        null,
        firstObserver.Observe(startAfterRejectedPrepare),
        "rejected stale prepare cannot arm later start");

    var pageStaleCapture = CaptureMoviePrepare(
        first.Contract,
        prepareIdentity,
        timestamp.AddSeconds(4),
        openingPath,
        succeeded: true);
    first.Memory.ClearRegions();
    first.Memory.MapRegion(
        first.ModuleBase,
        first.ModuleImageSize,
        first.ModuleBase + 0x1000,
        isCommitted: true,
        isExecutable: true);
    AssertEqual<MovieLifecycleEvent?>(
        null,
        firstObserver.Observe(pageStaleCapture),
        "foreign page ownership invalidates captured prepare at use time");
}

static void AssertNativeMovieCallbackCapturesAreStableUnderConcurrency(
    Steam2026FingerprintResult supportedRuntime)
{
    const string openingPath = @"C:\Games\FF7\data\movies\opening.avi";
    const int captureCount = 64;
    var fixture = CreateNativeMovieFixture(supportedRuntime, 0x00000001C0000000);
    var prepareIdentity = RequireMovieIdentity(fixture.Contract, NativeMovieCallbackKind.Prepare);
    var startIdentity = RequireMovieIdentity(fixture.Contract, NativeMovieCallbackKind.Start);
    var stopIdentity = RequireMovieIdentity(fixture.Contract, NativeMovieCallbackKind.Stop);
    var timestamp = new DateTime(2026, 7, 19, 22, 5, 0, DateTimeKind.Utc);
    var captures = new NativeMovieCallbackCapture[captureCount];

    AssertEqual(
        true,
        typeof(NativeMovieCallbackCapture).GetProperties().All(property => !property.CanWrite),
        "movie callback capture payload is immutable");
    AssertEqual(
        true,
        typeof(NativeMovieCallbackIdentity).GetProperties().All(property => !property.CanWrite),
        "movie callback identity payload is immutable");

    Parallel.For(0, captureCount, index =>
    {
        var path = $@"C:\Games\FF7\data\movies\opening-{index}.avi";
        if (!fixture.Contract.TryCapturePrepare(
                prepareIdentity,
                timestamp.AddTicks(index),
                path,
                succeeded: (index & 1) == 0,
                out captures[index]))
        {
            throw new InvalidOperationException($"Concurrent prepare capture {index} failed.");
        }
    });

    AssertEqual(captureCount, captures.Select(capture => capture.Sequence).Distinct().Count(), "concurrent capture sequence uniqueness");
    for (var index = 0; index < captures.Length; index++)
    {
        AssertEqual(
            $@"C:\Games\FF7\data\movies\opening-{index}.avi",
            captures[index].CanonicalMoviePath,
            $"concurrent immutable capture path {index}");
        AssertEqual((index & 1) == 0, captures[index].Succeeded, $"concurrent immutable capture result {index}");
        AssertEqual(timestamp.AddTicks(index), captures[index].TimestampUtc, $"concurrent immutable capture timestamp {index}");
    }

    var observer = new OpeningMovieLifecycleObserver(openingPath, fixture.Contract);
    AssertEqual<MovieLifecycleEvent?>(
        null,
        observer.Observe(CaptureMoviePrepare(
            fixture.Contract,
            prepareIdentity,
            timestamp.AddSeconds(1),
            openingPath,
            succeeded: true)),
        "concurrent lifecycle prepare");

    var startedEvents = new System.Collections.Concurrent.ConcurrentBag<MovieLifecycleEvent>();
    Parallel.For(0, captureCount, index =>
    {
        var movieEvent = observer.Observe(CaptureMovieStart(
            fixture.Contract,
            startIdentity,
            timestamp.AddSeconds(2).AddTicks(index),
            stateBefore: 0,
            stateAfter: 1));
        if (movieEvent is not null)
        {
            startedEvents.Add(movieEvent);
        }
    });
    AssertEqual(1, startedEvents.Count, "concurrent starts emit exactly one lifecycle event");
    AssertEqual(MovieLifecycleKind.Started, startedEvents.Single().Kind, "concurrent start lifecycle kind");

    var stoppedEvents = new System.Collections.Concurrent.ConcurrentBag<MovieLifecycleEvent>();
    Parallel.For(0, captureCount, index =>
    {
        var movieEvent = observer.Observe(CaptureMovieTerminal(
            fixture.Contract,
            stopIdentity,
            timestamp.AddSeconds(3).AddTicks(index)));
        if (movieEvent is not null)
        {
            stoppedEvents.Add(movieEvent);
        }
    });
    AssertEqual(1, stoppedEvents.Count, "concurrent stops emit exactly one lifecycle event");
    AssertEqual(MovieLifecycleKind.Stopped, stoppedEvents.Single().Kind, "concurrent stop lifecycle kind");
}

static void AssertNativeMovieCallbackContractRejectsWrappingModuleBase(
    Steam2026FingerprintResult supportedRuntime)
{
    var rejected = false;
    try
    {
        _ = new NativeMovieCallbackContract(
            supportedRuntime,
            ulong.MaxValue - 0x100,
            0x200,
            new FakeNativeMemoryReader());
    }
    catch (ArgumentOutOfRangeException)
    {
        rejected = true;
    }

    AssertEqual(true, rejected, "native movie callback wrapping module image");
}

static void AssertOpeningMovieLifecycleRequiresExactPathAndRisingStart(
    Steam2026FingerprintResult supportedRuntime)
{
    const string expectedPath = @"C:\Games\FF7\data\movies\opening.avi";
    var fixture = CreateNativeMovieFixture(supportedRuntime, 0x00000001D0000000);
    var observer = new OpeningMovieLifecycleObserver(expectedPath, fixture.Contract);
    var prepareIdentity = RequireMovieIdentity(fixture.Contract, NativeMovieCallbackKind.Prepare);
    var startIdentity = RequireMovieIdentity(fixture.Contract, NativeMovieCallbackKind.Start);
    var timestamp = new DateTime(2026, 7, 19, 22, 10, 0, DateTimeKind.Utc);

    AssertEqual<MovieLifecycleEvent?>(
        null,
        observer.Observe(CaptureMovieStart(fixture.Contract, startIdentity, timestamp, 0, 1)),
        "opening movie start without prepare");
    AssertEqual<MovieLifecycleEvent?>(
        null,
        observer.Observe(CaptureMoviePrepare(
            fixture.Contract,
            prepareIdentity,
            timestamp.AddMilliseconds(1),
            @"c:/games/ff7/data/movies/../movies/OPENING.AVI",
            succeeded: true)),
        "normalized opening movie prepare is observation-only");

    var startedAt = timestamp.AddMilliseconds(2);
    var started = observer.Observe(CaptureMovieStart(fixture.Contract, startIdentity, startedAt, 0, 1))
                  ?? throw new InvalidOperationException("Expected an opening movie Started event.");
    AssertEqual(MovieLifecycleKind.Started, started.Kind, "opening movie rising start kind");
    AssertEqual("opening", started.NativeMovieKey, "opening movie native key");
    AssertEqual(startedAt, started.TimestampUtc, "opening movie rising start timestamp");
    AssertEqual<MovieLifecycleEvent?>(
        null,
        observer.Observe(CaptureMovieStart(
            fixture.Contract,
            startIdentity,
            timestamp.AddMilliseconds(3),
            1,
            1)),
        "opening movie duplicate start");
}

static void AssertOpeningMovieLifecycleOrdersAndDeduplicatesEvents(
    Steam2026FingerprintResult supportedRuntime)
{
    const string openingPath = @"C:\Games\FF7\data\movies\opening.avi";
    var fixture = CreateNativeMovieFixture(supportedRuntime, 0x00000001E0000000);
    var observer = new OpeningMovieLifecycleObserver(openingPath, fixture.Contract);
    var prepareIdentity = RequireMovieIdentity(fixture.Contract, NativeMovieCallbackKind.Prepare);
    var startIdentity = RequireMovieIdentity(fixture.Contract, NativeMovieCallbackKind.Start);
    var stopIdentity = RequireMovieIdentity(fixture.Contract, NativeMovieCallbackKind.Stop);
    var timestamp = new DateTime(2026, 7, 19, 22, 15, 0, DateTimeKind.Utc);
    var events = new List<MovieLifecycleEvent>();

    AddMovieEvent(events, observer.Observe(CaptureMoviePrepare(fixture.Contract, prepareIdentity, timestamp, openingPath, true)));
    AddMovieEvent(events, observer.Observe(CaptureMovieStart(fixture.Contract, startIdentity, timestamp.AddSeconds(1), 0, 1)));
    AddMovieEvent(events, observer.Observe(CaptureMovieStart(fixture.Contract, startIdentity, timestamp.AddSeconds(2), 1, 1)));
    AddMovieEvent(events, observer.Observe(CaptureMovieTerminal(fixture.Contract, stopIdentity, timestamp.AddSeconds(3))));
    AddMovieEvent(events, observer.Observe(CaptureMovieTerminal(fixture.Contract, stopIdentity, timestamp.AddSeconds(4))));
    AddMovieEvent(events, observer.Observe(CaptureMoviePrepare(fixture.Contract, prepareIdentity, timestamp.AddSeconds(5), openingPath, true)));
    AddMovieEvent(events, observer.Observe(CaptureMovieStart(fixture.Contract, startIdentity, timestamp.AddSeconds(6), 0, 1)));
    AddMovieEvent(events, observer.ObserveSkip(timestamp.AddSeconds(7)));
    AddMovieEvent(events, observer.ObserveSkip(timestamp.AddSeconds(8)));

    AssertEqual(4, events.Count, "opening movie ordered deduplicated event count");
    AssertEqual(MovieLifecycleKind.Started, events[0].Kind, "opening movie first ordered event");
    AssertEqual(MovieLifecycleKind.Stopped, events[1].Kind, "opening movie second ordered event");
    AssertEqual(MovieLifecycleKind.Started, events[2].Kind, "opening movie third ordered event");
    AssertEqual(MovieLifecycleKind.Skipped, events[3].Kind, "opening movie fourth ordered event");
}

static void AssertOpeningMovieLifecycleRejectsFailedAndNonOpeningSequences(
    Steam2026FingerprintResult supportedRuntime)
{
    const string openingPath = @"C:\Games\FF7\data\movies\opening.avi";
    var fixture = CreateNativeMovieFixture(supportedRuntime, 0x00000001F0000000);
    var observer = new OpeningMovieLifecycleObserver(openingPath, fixture.Contract);
    var prepareIdentity = RequireMovieIdentity(fixture.Contract, NativeMovieCallbackKind.Prepare);
    var startIdentity = RequireMovieIdentity(fixture.Contract, NativeMovieCallbackKind.Start);
    var timestamp = new DateTime(2026, 7, 19, 22, 20, 0, DateTimeKind.Utc);

    AssertEqual<MovieLifecycleEvent?>(
        null,
        observer.Observe(CaptureMoviePrepare(fixture.Contract, prepareIdentity, timestamp, openingPath, false)),
        "failed opening movie prepare");
    AssertEqual<MovieLifecycleEvent?>(
        null,
        observer.Observe(CaptureMovieStart(fixture.Contract, startIdentity, timestamp.AddSeconds(1), 0, 1)),
        "start after failed opening movie prepare");

    AssertEqual<MovieLifecycleEvent?>(
        null,
        observer.Observe(CaptureMoviePrepare(
            fixture.Contract,
            prepareIdentity,
            timestamp.AddSeconds(2),
            @"C:\Games\FF7\data\movies\ending.avi",
            true)),
        "non-opening movie prepare");
    AssertEqual<MovieLifecycleEvent?>(
        null,
        observer.Observe(CaptureMovieStart(fixture.Contract, startIdentity, timestamp.AddSeconds(3), 0, 1)),
        "non-opening movie cannot own narration");

    AssertEqual<MovieLifecycleEvent?>(
        null,
        observer.Observe(CaptureMoviePrepare(
            fixture.Contract,
            prepareIdentity,
            timestamp.AddSeconds(4),
            openingPath + ".bak",
            true)),
        "opening movie substring is not exact identity");
    AssertEqual<MovieLifecycleEvent?>(
        null,
        observer.Observe(CaptureMovieStart(fixture.Contract, startIdentity, timestamp.AddSeconds(5), 0, 1)),
        "opening movie substring cannot own narration");

    AssertEqual<MovieLifecycleEvent?>(
        null,
        observer.Observe(CaptureMoviePrepare(fixture.Contract, prepareIdentity, timestamp.AddSeconds(6), openingPath, true)),
        "opening movie prepare before non-rising start");
    AssertEqual<MovieLifecycleEvent?>(
        null,
        observer.Observe(CaptureMovieStart(fixture.Contract, startIdentity, timestamp.AddSeconds(7), 1, 1)),
        "opening movie non-rising first start");
    AssertEqual<MovieLifecycleEvent?>(
        null,
        observer.Observe(CaptureMovieStart(fixture.Contract, startIdentity, timestamp.AddSeconds(8), 0, 1)),
        "invalid first start disarms opening prepare");
}

static void AssertOpeningMovieLifecycleTerminalsResetCleanly(
    Steam2026FingerprintResult supportedRuntime)
{
    const string openingPath = @"C:\Games\FF7\data\movies\opening.avi";
    var fixture = CreateNativeMovieFixture(supportedRuntime, 0x0000000200000000);
    var observer = new OpeningMovieLifecycleObserver(openingPath, fixture.Contract);
    var prepareIdentity = RequireMovieIdentity(fixture.Contract, NativeMovieCallbackKind.Prepare);
    var startIdentity = RequireMovieIdentity(fixture.Contract, NativeMovieCallbackKind.Start);
    var releaseIdentity = RequireMovieIdentity(fixture.Contract, NativeMovieCallbackKind.Release);
    var stopIdentity = RequireMovieIdentity(fixture.Contract, NativeMovieCallbackKind.Stop);
    var timestamp = new DateTime(2026, 7, 19, 22, 25, 0, DateTimeKind.Utc);

    AssertOpeningMovieSequenceStarts(
        observer,
        fixture.Contract,
        prepareIdentity,
        startIdentity,
        openingPath,
        timestamp,
        "failed-prepare terminal setup");
    var failedPrepareStop = observer.Observe(CaptureMoviePrepare(
                                fixture.Contract,
                                prepareIdentity,
                                timestamp.AddSeconds(2),
                                openingPath,
                                false))
                            ?? throw new InvalidOperationException("Failed prepare must stop active opening narration.");
    AssertEqual(MovieLifecycleKind.Stopped, failedPrepareStop.Kind, "failed prepare terminal kind");
    AssertEqual<MovieLifecycleEvent?>(
        null,
        observer.Observe(CaptureMovieTerminal(fixture.Contract, releaseIdentity, timestamp.AddSeconds(3))),
        "failed prepare clean reset");

    AssertOpeningMovieSequenceStarts(
        observer,
        fixture.Contract,
        prepareIdentity,
        startIdentity,
        openingPath,
        timestamp.AddSeconds(4),
        "release terminal setup");
    var releaseStop = observer.Observe(CaptureMovieTerminal(fixture.Contract, releaseIdentity, timestamp.AddSeconds(6)))
                      ?? throw new InvalidOperationException("Release must stop active opening narration.");
    AssertEqual(MovieLifecycleKind.Stopped, releaseStop.Kind, "release terminal kind");
    AssertEqual<MovieLifecycleEvent?>(
        null,
        observer.Observe(CaptureMovieTerminal(fixture.Contract, releaseIdentity, timestamp.AddSeconds(7))),
        "release terminal dedup");

    AssertOpeningMovieSequenceStarts(
        observer,
        fixture.Contract,
        prepareIdentity,
        startIdentity,
        openingPath,
        timestamp.AddSeconds(8),
        "module terminal setup");
    var moduleStop = observer.ObserveModuleTransition(timestamp.AddSeconds(10))
                     ?? throw new InvalidOperationException("Module transition must stop active opening narration.");
    AssertEqual(MovieLifecycleKind.Stopped, moduleStop.Kind, "module transition terminal kind");
    AssertEqual<MovieLifecycleEvent?>(null, observer.ObserveModuleTransition(timestamp.AddSeconds(11)), "module transition terminal dedup");

    AssertOpeningMovieSequenceStarts(
        observer,
        fixture.Contract,
        prepareIdentity,
        startIdentity,
        openingPath,
        timestamp.AddSeconds(12),
        "non-opening reset setup");
    var replacementStop = observer.Observe(CaptureMoviePrepare(
                              fixture.Contract,
                              prepareIdentity,
                              timestamp.AddSeconds(14),
                              @"C:\Games\FF7\data\movies\ending.avi",
                              true))
                          ?? throw new InvalidOperationException("A replacement movie must stop active opening narration.");
    AssertEqual(MovieLifecycleKind.Stopped, replacementStop.Kind, "non-opening replacement terminal kind");
    AssertEqual<MovieLifecycleEvent?>(
        null,
        observer.Observe(CaptureMovieStart(fixture.Contract, startIdentity, timestamp.AddSeconds(15), 0, 1)),
        "replacement movie cannot own opening narration");

    AssertOpeningMovieSequenceStarts(
        observer,
        fixture.Contract,
        prepareIdentity,
        startIdentity,
        openingPath,
        timestamp.AddSeconds(16),
        "final clean reset sequence");
    var finalStop = observer.Observe(CaptureMovieTerminal(fixture.Contract, stopIdentity, timestamp.AddSeconds(18)))
                    ?? throw new InvalidOperationException("Stop must end the final opening sequence.");
    AssertEqual(MovieLifecycleKind.Stopped, finalStop.Kind, "final stop terminal kind");
}

static NativeMovieSignatureCase[] GetNativeMovieCases() =>
[
    new(
        NativeMovieCallbackKind.FrameGetter,
        0x015729F0,
        "8B051EA6B000C3",
        new NativeMovieCallbackShape(
            NativeMovieCallbackAbi.MicrosoftX64,
            NativeMovieCallbackParameterShape.None,
            NativeMovieCallbackReturnShape.Integer32),
        false),
    new(
        NativeMovieCallbackKind.Prepare,
        0x01572A00,
        "48895C2418555657415641574883EC60",
        new NativeMovieCallbackShape(
            NativeMovieCallbackAbi.MicrosoftX64,
            NativeMovieCallbackParameterShape.Two32BitIntegers,
            NativeMovieCallbackReturnShape.BooleanCompatibleInteger),
        true),
    new(
        NativeMovieCallbackKind.Release,
        0x01572E40,
        "48895C2408574883EC20488B3DF766AC",
        new NativeMovieCallbackShape(
            NativeMovieCallbackAbi.MicrosoftX64,
            NativeMovieCallbackParameterShape.None,
            NativeMovieCallbackReturnShape.Void),
        true),
    new(
        NativeMovieCallbackKind.Start,
        0x01572EC0,
        "488B0541A0B00083B8FC010000007406",
        new NativeMovieCallbackShape(
            NativeMovieCallbackAbi.MicrosoftX64,
            NativeMovieCallbackParameterShape.None,
            NativeMovieCallbackReturnShape.BooleanCompatibleInteger),
        true),
    new(
        NativeMovieCallbackKind.Stop,
        0x01572EF0,
        "488B0511A0B00033C98988F801000048",
        new NativeMovieCallbackShape(
            NativeMovieCallbackAbi.MicrosoftX64,
            NativeMovieCallbackParameterShape.None,
            NativeMovieCallbackReturnShape.Void),
        true),
    new(
        NativeMovieCallbackKind.Update,
        0x01572F30,
        "48895C24104889742418574883EC2083",
        new NativeMovieCallbackShape(
            NativeMovieCallbackAbi.MicrosoftX64,
            NativeMovieCallbackParameterShape.None,
            NativeMovieCallbackReturnShape.BooleanCompatibleInteger),
        true)
];

static NativeMovieContractFixture CreateNativeMovieFixture(
    Steam2026FingerprintResult supportedRuntime,
    ulong moduleBase)
{
    const ulong moduleImageSize = 0x02100000;
    var memory = new FakeNativeMemoryReader();
    memory.MapRegion(moduleBase, moduleImageSize, moduleBase, isCommitted: true, isExecutable: true);
    foreach (var item in GetNativeMovieCases())
    {
        memory.Write(moduleBase + item.Rva, Convert.FromHexString(item.SignatureHex));
    }

    return new NativeMovieContractFixture(
        moduleBase,
        moduleImageSize,
        memory,
        new NativeMovieCallbackContract(supportedRuntime, moduleBase, moduleImageSize, memory));
}

static NativeMovieCallbackIdentity RequireMovieIdentity(
    NativeMovieCallbackContract contract,
    NativeMovieCallbackKind kind)
{
    return contract.TryValidateIdentity(kind, out var identity)
        ? identity
        : throw new InvalidOperationException($"Expected a validated {kind} movie callback identity.");
}

static NativeMovieCallbackCapture CaptureMoviePrepare(
    NativeMovieCallbackContract contract,
    NativeMovieCallbackIdentity identity,
    DateTime timestampUtc,
    string? canonicalMoviePath,
    bool succeeded)
{
    return contract.TryCapturePrepare(
        identity,
        timestampUtc,
        canonicalMoviePath,
        succeeded,
        out var capture)
        ? capture
        : throw new InvalidOperationException("Expected a validated prepare capture.");
}

static NativeMovieCallbackCapture CaptureMovieStart(
    NativeMovieCallbackContract contract,
    NativeMovieCallbackIdentity identity,
    DateTime timestampUtc,
    int stateBefore,
    int stateAfter)
{
    return contract.TryCaptureStart(
        identity,
        timestampUtc,
        stateBefore,
        stateAfter,
        out var capture)
        ? capture
        : throw new InvalidOperationException("Expected a validated start capture.");
}

static NativeMovieCallbackCapture CaptureMovieTerminal(
    NativeMovieCallbackContract contract,
    NativeMovieCallbackIdentity identity,
    DateTime timestampUtc)
{
    return contract.TryCaptureTerminal(identity, timestampUtc, out var capture)
        ? capture
        : throw new InvalidOperationException("Expected a validated terminal capture.");
}

static void AssertOpeningMovieSequenceStarts(
    OpeningMovieLifecycleObserver observer,
    NativeMovieCallbackContract contract,
    NativeMovieCallbackIdentity prepareIdentity,
    NativeMovieCallbackIdentity startIdentity,
    string openingPath,
    DateTime timestamp,
    string label)
{
    AssertEqual<MovieLifecycleEvent?>(
        null,
        observer.Observe(CaptureMoviePrepare(contract, prepareIdentity, timestamp, openingPath, true)),
        $"{label} prepare");
    var started = observer.Observe(CaptureMovieStart(contract, startIdentity, timestamp.AddSeconds(1), 0, 1))
                  ?? throw new InvalidOperationException($"{label} did not start.");
    AssertEqual(MovieLifecycleKind.Started, started.Kind, $"{label} start kind");
}

static void AddMovieEvent(List<MovieLifecycleEvent> events, MovieLifecycleEvent? movieEvent)
{
    if (movieEvent is not null)
    {
        events.Add(movieEvent);
    }
}

static void AssertTranslatedX86AddressSpaceImplementsSharedGuestMemoryContract()
{
    AssertEqual(
        true,
        typeof(ILegacyAddressSpace).IsAssignableFrom(typeof(TranslatedX86AddressSpace)),
        "translated x86 address space shared guest-memory contract");
}

static void AssertNativeMemoryRegionReportsReadability()
{
    AssertEqual(
        true,
        typeof(NativeMemoryRegion).GetProperty("IsReadable") is not null,
        "native memory regions expose readable protection state");
}

static void AssertCurrentProcessRegionQueryRecognizesExecutableCode()
{
    var kernel32 = System.Runtime.InteropServices.NativeLibrary.Load("kernel32.dll");
    try
    {
        var export = System.Runtime.InteropServices.NativeLibrary.GetExport(kernel32, "VirtualQueryEx");
        var address = (ulong)(nuint)export;
        var memory = new CurrentProcessNativeMemoryReader();
        AssertEqual(true, memory.TryQueryRegion(address, out var region), "current-process executable region query");
        AssertEqual(true, region.IsCommitted, "current-process code region committed");
        AssertEqual(true, region.IsExecutable, "current-process code region executable");
        AssertEqual((ulong)(nuint)kernel32, region.AllocationBase, "current-process code region allocation base");
        AssertEqual(true, region.BaseAddress <= address, "current-process code region contains export start");
        AssertEqual(
            true,
            region.Size > 0
            && region.BaseAddress <= ulong.MaxValue - (region.Size - 1)
            && address <= region.BaseAddress + region.Size - 1,
            "current-process code region contains complete export address");
    }
    finally
    {
        System.Runtime.InteropServices.NativeLibrary.Free(kernel32);
    }
}

static void AssertCurrentProcessMemoryReaderSurvivesForcedFinalization()
{
    const ulong expected = 0x8877665544332211;
    var allocation = System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(long));
    try
    {
        System.Runtime.InteropServices.Marshal.WriteInt64(allocation, unchecked((long)expected));
        var memory = CreateCurrentProcessReaderBeforeForcedFinalization();

        for (var pass = 0; pass < 3; pass++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        var address = (ulong)(nuint)allocation;
        AssertEqual(true, memory.TryReadUInt64(address, out var actual), "current-process read after forced finalization");
        AssertEqual(expected, actual, "current-process bytes after forced finalization");
        AssertEqual(true, memory.TryQueryRegion(address, out var region), "current-process query after forced finalization");
        AssertEqual(true, region.IsCommitted, "current-process allocation remains committed after forced finalization");
    }
    finally
    {
        System.Runtime.InteropServices.Marshal.FreeHGlobal(allocation);
    }
}

[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static CurrentProcessNativeMemoryReader CreateCurrentProcessReaderBeforeForcedFinalization() => new();

static void AssertEvidenceAtlasIsCompleteAndStaticCandidatesRemainUnavailable()
{
    var prototypeRoot = FindAccessibilityPrototypeRoot();
    var atlasPath = Path.Combine(prototypeRoot, "analysis", "dual_runtime", "evidence-atlas.json");
    var schemaPath = Path.Combine(prototypeRoot, "analysis", "dual_runtime", "evidence-atlas.schema.json");
    AssertEqual(true, File.Exists(atlasPath), "x64 evidence atlas exists");
    AssertEqual(true, File.Exists(schemaPath), "x64 evidence atlas schema exists");

    using var document = JsonDocument.Parse(File.ReadAllText(atlasPath));
    var root = document.RootElement;
    AssertEqual(1, root.GetProperty("schemaVersion").GetInt32(), "x64 evidence atlas schema version");
    AssertEqual(
        Steam2026Fingerprint.SupportedSha256,
        root.GetProperty("runtimeSha256").GetString(),
        "x64 evidence atlas runtime hash");
    var requiredProperties = new[]
    {
        "runtimeSha256",
        "subsystem",
        "capability",
        "signal",
        "signature",
        "matchCount",
        "callingConvention",
        "structure",
        "invariants",
        "staticEvidence",
        "dynamicEvidence",
        "confidence",
        "tests"
    };
    var entries = root.GetProperty("entries").EnumerateArray().ToArray();
    AssertEqual(true, entries.Length >= 7, "x64 evidence atlas subsystem coverage");
    foreach (var entry in entries)
    {
        foreach (var property in requiredProperties)
        {
            AssertEqual(true, entry.TryGetProperty(property, out _), $"x64 evidence field {property}");
        }

        var confidence = entry.GetProperty("confidence").GetString();
        var dynamicEvidenceCount = entry.GetProperty("dynamicEvidence").GetArrayLength();
        if (string.Equals(confidence, "live-validated", StringComparison.Ordinal))
        {
            AssertEqual(true, dynamicEvidenceCount > 0, $"live evidence for {entry.GetProperty("signal").GetString()}");
        }
    }

    AssertEqual(
        true,
        entries.All(entry => !string.Equals(
            entry.GetProperty("confidence").GetString(),
            "live-validated",
            StringComparison.Ordinal)),
        "static-only atlas entries must remain unavailable");
}

static string FindAccessibilityPrototypeRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "analysis", "dual_runtime"))
            && Directory.Exists(Path.Combine(directory.FullName, "reloaded")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate the accessibility_prototype root.");
}

static void AssertTranslatedX86AddressSpaceReadsMappedPages()
{
    const ulong moduleBase = 0x0000000140000000;
    const uint virtualAddress = 0x00CC15D0;
    const ulong hostPage = 0x0000000200100000;
    var memory = new FakeNativeMemoryReader();
    memory.MapVirtualPage(moduleBase, virtualAddress >> 12, hostPage);
    memory.Write(hostPage + (virtualAddress & 0xFFF), [0x74, 0x00]);
    var addressSpace = new TranslatedX86AddressSpace(moduleBase, memory);

    Span<byte> bytes = stackalloc byte[2];
    AssertEqual(true, addressSpace.TryRead(virtualAddress, bytes), "mapped virtual x86 read");
    AssertEqual((byte)0x74, bytes[0], "mapped virtual x86 first byte");
    AssertEqual((byte)0x00, bytes[1], "mapped virtual x86 second byte");
    AssertEqual(true, addressSpace.TryReadUInt16(virtualAddress, out var fieldId), "mapped virtual x86 UInt16 read");
    AssertEqual((ushort)0x0074, fieldId, "mapped virtual x86 UInt16 value");
}

static void AssertTranslatedX86AddressSpaceReadsAcrossNoncontiguousPages()
{
    const ulong moduleBase = 0x0000000140000000;
    const uint virtualAddress = 0x00123FFE;
    const ulong firstHostPage = 0x0000000200200000;
    const ulong secondHostPage = 0x0000000300400000;
    var memory = new FakeNativeMemoryReader();
    memory.MapVirtualPage(moduleBase, virtualAddress >> 12, firstHostPage);
    memory.MapVirtualPage(moduleBase, (virtualAddress >> 12) + 1, secondHostPage);
    memory.Write(firstHostPage + 0xFFE, [0x11, 0x22]);
    memory.Write(secondHostPage, [0x33, 0x44]);
    var addressSpace = new TranslatedX86AddressSpace(moduleBase, memory);

    Span<byte> bytes = stackalloc byte[4];
    AssertEqual(true, addressSpace.TryRead(virtualAddress, bytes), "cross-page virtual x86 read");
    AssertEqual("11223344", Convert.ToHexString(bytes), "cross-page virtual x86 bytes");
}

static void AssertTranslatedX86AddressSpaceRejectsOverflowedPageTableRange()
{
    var pageTableByteLength = checked((ulong)TranslatedX86AddressSpace.PageCount * sizeof(ulong));
    var moduleBase = ulong.MaxValue
                     - TranslatedX86AddressSpace.PageTableRva
                     - pageTableByteLength
                     + 1;
    var rejected = false;
    try
    {
        _ = new TranslatedX86AddressSpace(moduleBase, new FakeNativeMemoryReader());
    }
    catch (ArgumentOutOfRangeException)
    {
        rejected = true;
    }

    AssertEqual(true, rejected, "overflowed translated page-table range rejected");
}

static void AssertTranslatedX86AddressSpaceReadsAllocatorAlignedHostPages()
{
    const ulong moduleBase = 0x0000000140000000;
    const uint virtualAddress = 0x00123040;
    const ulong allocatorAlignedHostPage = 0x0000000200100010;
    var memory = new FakeNativeMemoryReader();
    memory.MapVirtualPage(moduleBase, virtualAddress >> 12, allocatorAlignedHostPage);
    memory.Write(allocatorAlignedHostPage + (virtualAddress & 0xFFF), [0x5A]);
    var addressSpace = new TranslatedX86AddressSpace(moduleBase, memory);
    Span<byte> destination = stackalloc byte[1];
    destination[0] = 0xCC;

    AssertEqual(
        true,
        addressSpace.TryRead(virtualAddress, destination),
        "translated page backed by the game's allocator-aligned buffer");
    AssertEqual((byte)0x5A, destination[0], "allocator-aligned translated byte");
}

static void AssertTranslatedX86AddressSpaceAllowsTrustedEntryInsideSplitPageTableRegion()
{
    const ulong moduleBase = 0x0000000140000000;
    const uint virtualAddress = 0x00123040;
    const ulong hostPage = 0x0000000200100000;
    var pageTableAddress = moduleBase + TranslatedX86AddressSpace.PageTableRva;
    var pageTableByteLength = checked((ulong)TranslatedX86AddressSpace.PageCount * sizeof(ulong));
    var memory = new FakeNativeMemoryReader();
    memory.MapVirtualPage(moduleBase, virtualAddress >> 12, hostPage);
    memory.Write(hostPage + (virtualAddress & 0xFFF), [0x5A]);
    memory.MapRegion(
        pageTableAddress,
        pageTableByteLength - 1,
        moduleBase,
        isCommitted: true,
        isExecutable: false);
    var addressSpace = new TranslatedX86AddressSpace(moduleBase, memory);
    Span<byte> destination = stackalloc byte[1];
    destination[0] = 0xCC;

    AssertEqual(
        true,
        addressSpace.TryRead(virtualAddress, destination),
        "trusted page-table entry remains readable when a later table page is in another image region");
    AssertEqual((byte)0x5A, destination[0], "split translated page-table region byte");
}

static void AssertTranslatedX86AddressSpaceRejectsUncommittedPageTableRegion()
{
    const ulong moduleBase = 0x0000000140000000;
    const uint virtualAddress = 0x00123040;
    const ulong hostPage = 0x0000000200100000;
    var pageTableAddress = moduleBase + TranslatedX86AddressSpace.PageTableRva;
    var pageTableByteLength = checked((ulong)TranslatedX86AddressSpace.PageCount * sizeof(ulong));
    var memory = new FakeNativeMemoryReader();
    memory.MapVirtualPage(moduleBase, virtualAddress >> 12, hostPage);
    memory.Write(hostPage + (virtualAddress & 0xFFF), [0x5A]);
    memory.MapRegion(
        pageTableAddress,
        pageTableByteLength,
        moduleBase,
        isCommitted: false,
        isExecutable: false);
    var addressSpace = new TranslatedX86AddressSpace(moduleBase, memory);
    Span<byte> destination = stackalloc byte[1];
    destination[0] = 0xCC;

    AssertEqual(false, addressSpace.TryRead(virtualAddress, destination), "uncommitted translated page-table region rejected");
    AssertEqual((byte)0, destination[0], "uncommitted translated page-table region clears destination");
}

static void AssertTranslatedX86AddressSpaceRejectsUnreadablePageTableRegion()
{
    const ulong moduleBase = 0x0000000140000000;
    const uint virtualAddress = 0x00123040;
    const ulong hostPage = 0x0000000200100000;
    var pageTableAddress = moduleBase + TranslatedX86AddressSpace.PageTableRva;
    var pageTableByteLength = checked((ulong)TranslatedX86AddressSpace.PageCount * sizeof(ulong));
    var memory = new FakeNativeMemoryReader();
    memory.MapVirtualPage(moduleBase, virtualAddress >> 12, hostPage);
    memory.Write(hostPage + (virtualAddress & 0xFFF), [0x5A]);
    memory.MapRegion(
        pageTableAddress,
        pageTableByteLength,
        moduleBase,
        isCommitted: true,
        isExecutable: false,
        isReadable: false);
    var addressSpace = new TranslatedX86AddressSpace(moduleBase, memory);
    Span<byte> destination = stackalloc byte[1];
    destination[0] = 0xCC;

    AssertEqual(false, addressSpace.TryRead(virtualAddress, destination), "unreadable translated page-table region rejected");
    AssertEqual((byte)0, destination[0], "unreadable translated page-table region clears destination");
}

static void AssertTranslatedX86AddressSpaceRejectsRemapAfterCopy()
{
    const ulong moduleBase = 0x0000000140000000;
    const uint virtualAddress = 0x00123FFE;
    const ulong firstHostPage = 0x0000000200200000;
    const ulong secondHostPage = 0x0000000300400000;
    const ulong replacementHostPage = 0x0000000500600000;
    var memory = new FakeNativeMemoryReader();
    memory.MapVirtualPage(moduleBase, virtualAddress >> 12, firstHostPage);
    memory.MapVirtualPage(moduleBase, (virtualAddress >> 12) + 1, secondHostPage);
    memory.Write(firstHostPage + 0xFFE, [0x11, 0x22]);
    memory.Write(secondHostPage, [0x33, 0x44]);
    memory.Write(replacementHostPage + 0xFFE, [0xAA, 0xBB]);
    var watchedEntry = moduleBase
                       + TranslatedX86AddressSpace.PageTableRva
                       + ((virtualAddress >> 12) * sizeof(ulong));
    var remappingMemory = new RemappingNativeMemoryReader(
        memory,
        watchedEntry,
        triggerRead: 2,
        () => memory.MapVirtualPage(moduleBase, virtualAddress >> 12, replacementHostPage));
    var addressSpace = new TranslatedX86AddressSpace(moduleBase, remappingMemory);
    Span<byte> destination = stackalloc byte[4];
    destination.Fill(0xCC);

    AssertEqual(false, addressSpace.TryRead(virtualAddress, destination), "translated page remap after copy rejected");
    AssertEqual("00000000", Convert.ToHexString(destination), "translated page remap clears copied bytes");
}

static void AssertTranslatedX86AddressSpaceRejectsInvalidMappings()
{
    const ulong moduleBase = 0x0000000140000000;
    var memory = new FakeNativeMemoryReader();
    var addressSpace = new TranslatedX86AddressSpace(moduleBase, memory);
    Span<byte> oneByte = stackalloc byte[1];
    Span<byte> wrapping = stackalloc byte[4];

    AssertEqual(false, addressSpace.TryRead(0, oneByte), "null virtual x86 address");
    AssertEqual(false, addressSpace.TryRead(0x00123000, oneByte), "unmapped virtual x86 page");
    AssertEqual(false, addressSpace.TryRead(0xFFFFFFFE, wrapping), "wrapping virtual x86 range");

    const uint sentinelVirtualAddress = 0x00456000;
    memory.MapVirtualPage(
        moduleBase,
        sentinelVirtualAddress >> 12,
        moduleBase + TranslatedX86AddressSpace.UnmappedSentinelRva);
    AssertEqual(
        false,
        addressSpace.TryRead(sentinelVirtualAddress, oneByte),
        "translator sentinel page must be rejected");
}

static void AssertTranslatedX86AddressSpaceValidatesRuntimeResolverSignature()
{
    const ulong moduleBase = 0x0000000140000000;
    var memory = new FakeNativeMemoryReader();
    var addressSpace = new TranslatedX86AddressSpace(moduleBase, memory);
    var signature = Convert.FromHexString(
        "85C9750333C0C38BC1488D15609F6F018BC948C1E90C488B14CA4885D2740925FF0F00004803C2C3488D05319C6F01C3");
    memory.Write(moduleBase + TranslatedX86AddressSpace.ResolverRva, signature);

    AssertEqual(true, addressSpace.HasExpectedResolverSignature(), "translated x86 resolver signature");
    memory.Write(moduleBase + TranslatedX86AddressSpace.ResolverRva + 10, [0x00]);
    AssertEqual(false, addressSpace.HasExpectedResolverSignature(), "mutated translated x86 resolver signature");
}

static void AssertTranslatedX86AddressSpaceRejectsUnstableResolverSignature()
{
    const ulong moduleBase = 0x0000000140000000;
    var memory = new FakeNativeMemoryReader();
    var signature = Convert.FromHexString(
        "85C9750333C0C38BC1488D15609F6F018BC948C1E90C488B14CA4885D2740925FF0F00004803C2C3488D05319C6F01C3");
    var resolverAddress = moduleBase + TranslatedX86AddressSpace.ResolverRva;
    memory.Write(resolverAddress, signature);
    var tearingMemory = new TearingNativeMemoryReader(
        memory,
        resolverAddress,
        triggerRead: 2,
        () => memory.Write(resolverAddress + 10, [0x00]));
    var addressSpace = new TranslatedX86AddressSpace(moduleBase, tearingMemory);

    AssertEqual(false, addressSpace.HasExpectedResolverSignature(), "unstable translated x86 resolver signature rejected");
}

static void AssertTranslatedX86CallFrameReaderReadsGuestRegisters()
{
    const ulong moduleBase = 0x0000000140000000;
    var memory = new FakeNativeMemoryReader();
    var addressSpace = new TranslatedX86AddressSpace(moduleBase, memory);
    var callFrame = new TranslatedX86CallFrameReader(moduleBase, memory, addressSpace);
    memory.Write(moduleBase + TranslatedX86CallFrameReader.EaxRva, BitConverter.GetBytes(0x12345678u));
    memory.Write(moduleBase + TranslatedX86CallFrameReader.EspRva, BitConverter.GetBytes(0x00ABC000u));
    memory.Write(moduleBase + TranslatedX86CallFrameReader.EbpRva, BitConverter.GetBytes(0x00DEF000u));

    AssertEqual(true, callFrame.TryReadEax(out var eax), "translated wrapper guest EAX read");
    AssertEqual(0x12345678u, eax, "translated wrapper guest EAX value");
    AssertEqual(true, callFrame.TryReadEsp(out var esp), "translated wrapper guest ESP read");
    AssertEqual(0x00ABC000u, esp, "translated wrapper guest ESP value");
    AssertEqual(true, callFrame.TryReadEbp(out var ebp), "translated wrapper guest EBP read");
    AssertEqual(0x00DEF000u, ebp, "translated wrapper guest EBP value");
}

static void AssertTranslatedX86CallFrameReaderReadsCdeclArguments()
{
    const ulong moduleBase = 0x0000000140000000;
    const uint guestEsp = 0x00123000;
    const ulong hostStackPage = 0x0000000200500000;
    var memory = new FakeNativeMemoryReader();
    memory.Write(moduleBase + TranslatedX86CallFrameReader.EspRva, BitConverter.GetBytes(guestEsp));
    memory.MapVirtualPage(moduleBase, guestEsp >> 12, hostStackPage);
    memory.Write(hostStackPage + 4, BitConverter.GetBytes(0x1234FF80u));
    memory.Write(hostStackPage + 8, BitConverter.GetBytes(0x89ABCDEFu));
    var addressSpace = new TranslatedX86AddressSpace(moduleBase, memory);
    var callFrame = new TranslatedX86CallFrameReader(moduleBase, memory, addressSpace);

    AssertEqual(true, callFrame.TryReadArgument(0, out var first), "translated wrapper cdecl argument zero read");
    AssertEqual(0x1234FF80u, first, "translated wrapper cdecl argument zero value");
    AssertEqual(true, callFrame.TryReadArgument(1, out var second), "translated wrapper cdecl argument one read");
    AssertEqual(0x89ABCDEFu, second, "translated wrapper cdecl argument one value");
    AssertEqual(true, callFrame.TryReadArgumentLowByte(0, out var lowByte), "translated wrapper low-byte argument read");
    AssertEqual((byte)0x80, lowByte, "translated wrapper low-byte argument value");
    AssertEqual(true, callFrame.TryReadArgumentSignedLow16(0, out var signedLow16), "translated wrapper signed-low-16 argument read");
    AssertEqual((short)-128, signedLow16, "translated wrapper signed-low-16 argument value");
}

static void AssertTranslatedX86CallFrameReaderRefreshesRuntimeState()
{
    const ulong moduleBase = 0x0000000140000000;
    const uint guestEsp = 0x00145000;
    const ulong firstHostStackPage = 0x0000000200600000;
    const ulong secondHostStackPage = 0x0000000300700000;
    var memory = new FakeNativeMemoryReader();
    memory.Write(moduleBase + TranslatedX86CallFrameReader.EaxRva, BitConverter.GetBytes(0x11111111u));
    memory.Write(moduleBase + TranslatedX86CallFrameReader.EspRva, BitConverter.GetBytes(guestEsp));
    memory.MapVirtualPage(moduleBase, guestEsp >> 12, firstHostStackPage);
    memory.Write(firstHostStackPage + 4, BitConverter.GetBytes(0x22222222u));
    memory.Write(secondHostStackPage + 4, BitConverter.GetBytes(0x33333333u));
    var addressSpace = new TranslatedX86AddressSpace(moduleBase, memory);
    var callFrame = new TranslatedX86CallFrameReader(moduleBase, memory, addressSpace);

    AssertEqual(true, callFrame.TryReadEax(out var entryEax), "translated wrapper entry EAX read");
    AssertEqual(0x11111111u, entryEax, "translated wrapper entry EAX value");
    memory.Write(moduleBase + TranslatedX86CallFrameReader.EaxRva, BitConverter.GetBytes(0x44444444u));
    AssertEqual(true, callFrame.TryReadPostCallEax(out var returnEax), "translated wrapper post-call EAX read");
    AssertEqual(0x44444444u, returnEax, "translated wrapper post-call EAX refresh");

    AssertEqual(true, callFrame.TryReadArgument(0, out var firstArgument), "translated wrapper first mapping argument read");
    AssertEqual(0x22222222u, firstArgument, "translated wrapper first mapping argument value");
    memory.MapVirtualPage(moduleBase, guestEsp >> 12, secondHostStackPage);
    AssertEqual(true, callFrame.TryReadArgument(0, out var remappedArgument), "translated wrapper remapped argument read");
    AssertEqual(0x33333333u, remappedArgument, "translated wrapper remapped argument refresh");
}

static void AssertTranslatedX86CallFrameReaderRejectsInvalidState()
{
    const ulong moduleBase = 0x0000000140000000;
    var memory = new FakeNativeMemoryReader();
    var addressSpace = new TranslatedX86AddressSpace(moduleBase, memory);
    var callFrame = new TranslatedX86CallFrameReader(moduleBase, memory, addressSpace);

    AssertEqual(false, callFrame.TryReadEax(out var unreadableEax), "translated wrapper unreadable EAX");
    AssertEqual(0u, unreadableEax, "translated wrapper unreadable EAX output");
    AssertEqual(false, callFrame.TryReadArgument(0, out var unreadableArgument), "translated wrapper unreadable ESP");
    AssertEqual(0u, unreadableArgument, "translated wrapper unreadable ESP output");

    memory.Write(moduleBase + TranslatedX86CallFrameReader.EspRva, BitConverter.GetBytes(0u));
    AssertEqual(false, callFrame.TryReadArgument(0, out var nullEspArgument), "translated wrapper null ESP");
    AssertEqual(0u, nullEspArgument, "translated wrapper null ESP output");

    memory.Write(moduleBase + TranslatedX86CallFrameReader.EspRva, BitConverter.GetBytes(0x00123000u));
    AssertEqual(false, callFrame.TryReadArgument(0, out var unmappedArgument), "translated wrapper unmapped guest stack");
    AssertEqual(0u, unmappedArgument, "translated wrapper unmapped guest stack output");
    AssertEqual(false, callFrame.TryReadArgument(-1, out var negativeIndexArgument), "translated wrapper negative argument index");
    AssertEqual(0u, negativeIndexArgument, "translated wrapper negative argument index output");
    AssertEqual(false, callFrame.TryReadArgument(int.MaxValue, out var offsetOverflowArgument), "translated wrapper argument offset overflow");
    AssertEqual(0u, offsetOverflowArgument, "translated wrapper argument offset overflow output");

    memory.Write(moduleBase + TranslatedX86CallFrameReader.EspRva, BitConverter.GetBytes(0xFFFFFFFCu));
    AssertEqual(false, callFrame.TryReadArgument(0, out var espWrapArgument), "translated wrapper ESP wrap");
    AssertEqual(0u, espWrapArgument, "translated wrapper ESP wrap output");
    AssertEqual(false, callFrame.TryReadArgumentLowByte(0, out var failedLowByte), "translated wrapper failed low-byte argument");
    AssertEqual((byte)0, failedLowByte, "translated wrapper failed low-byte output");
    AssertEqual(false, callFrame.TryReadArgumentSignedLow16(0, out var failedSignedLow16), "translated wrapper failed signed-low-16 argument");
    AssertEqual((short)0, failedSignedLow16, "translated wrapper failed signed-low-16 output");
}

static void AssertInventoryItemReaderMatchesDirectAndTranslatedGuestMemory()
{
    const uint slotAddress = 0x80001FFF;
    var fixture = new MenuParityFixture();
    fixture.Write(slotAddress, BitConverter.GetBytes((ushort)((5 << 9) | 7)));

    var directReader = new InventoryItemReader(
        fixture.Direct,
        itemId => itemId == 7 ? "Phoenix Down" : null,
        itemId => itemId == 7 ? "Restores life" : null,
        savemapAddress: slotAddress,
        itemsOffset: 0);
    var translatedReader = new InventoryItemReader(
        fixture.Translated,
        itemId => itemId == 7 ? "Phoenix Down" : null,
        itemId => itemId == 7 ? "Restores life" : null,
        savemapAddress: slotAddress,
        itemsOffset: 0);

    AssertEqual(true, directReader.TryRead(0, out var direct), "direct cross-page inventory slot");
    AssertEqual(true, translatedReader.TryRead(0, out var translated), "translated cross-page inventory slot");
    AssertEqual(direct, translated, "direct/translated inventory slot parity");
    AssertEqual(7, translated.ItemId, "translated inventory item id");
    AssertEqual(5, translated.Quantity, "translated inventory quantity");

    fixture.UnmapGuestPage(slotAddress + 1);
    AssertEqual(false, translatedReader.TryRead(0, out _), "unmapped second inventory word page fails closed");
}

static void AssertInventoryItemReaderRejectsTranslatedPageRemapping()
{
    const uint slotAddress = 0x80002040;
    const ulong replacementHostPage = 0x0000000900D00000;
    var fixture = new MenuParityFixture();
    fixture.Write(slotAddress, BitConverter.GetBytes((ushort)((2 << 9) | 3)));
    fixture.Native.Write(
        replacementHostPage + (slotAddress & 0xFFF),
        BitConverter.GetBytes((ushort)((4 << 9) | 7)));

    var watchedEntry = fixture.GetPageTableEntryAddress(slotAddress);
    var remappingMemory = new RemappingNativeMemoryReader(
        fixture.Native,
        watchedEntry,
        triggerRead: 2,
        () => fixture.MapGuestPage(slotAddress, replacementHostPage));
    var translated = new TranslatedX86AddressSpace(MenuParityFixture.ModuleBase, remappingMemory);
    var reader = new InventoryItemReader(translated, savemapAddress: slotAddress, itemsOffset: 0);

    AssertEqual(false, reader.TryRead(0, out _), "translated inventory page remap between bookends fails");
}

static void AssertMenuObservationReaderMatchesDirectAndTranslatedGuestMemory()
{
    var fixture = CreatePopulatedMenuFixture();
    var direct = CreateMenuObservationReader(fixture.Direct);
    var translated = CreateMenuObservationReader(fixture.Translated);

    AssertEqual(true, direct.TryReadMainMenu(out var directMain), "direct main-menu observation");
    AssertEqual(true, translated.TryReadMainMenu(out var translatedMain), "translated main-menu observation");
    AssertEqual(directMain, translatedMain, "direct/translated main-menu parity");
    AssertEqual("Materia", translatedMain.Selection?.Label, "normalized main-menu selection");

    var titleCallback = new TitleMenuCursorSnapshot("B", TitleMenuCursorReader.TitleModule, 206, 219, 0x3DCCCCCD);
    AssertEqual(
        false,
        direct.TryNormalizeTitleCursor(titleCallback, out var directTitle),
        "direct title coordinates alone expose no selection");
    AssertEqual(default(TitleMenuCursorSelection), directTitle, "direct title coordinate output stays empty");
    AssertEqual(
        false,
        translated.TryNormalizeTitleCursor(titleCallback, out var translatedTitle),
        "translated title coordinates alone expose no selection");
    AssertEqual(default(TitleMenuCursorSelection), translatedTitle, "translated title coordinate output stays empty");
    AssertEqual(
        false,
        translated.TryNormalizeTitleCursor(titleCallback with { Y = 195 }, out _),
        "alternate title row coordinates alone expose no selection");

    AssertEqual(true, direct.TryReadActiveWidget(MenuParityFixture.MagicWidgetAddress, out var directWidget), "direct active widget");
    AssertEqual(true, translated.TryReadActiveWidget(MenuParityFixture.MagicWidgetAddress, out var translatedWidget), "translated active widget");
    AssertEqual(directWidget, translatedWidget, "direct/translated active-widget parity");
    AssertEqual("Magic list", translatedWidget.VerifiedName, "address-free verified active-widget name");
    AssertEqual(MenuWidgetKind.MagicList, translatedWidget.Kind, "address-free verified active-widget kind");

    AssertEqual(true, direct.TryReadConfigValue("Battle speed", out var directConfig), "direct config value");
    AssertEqual(true, translated.TryReadConfigValue("Battle speed", out var translatedConfig), "translated config value");
    AssertEqual(directConfig, translatedConfig, "direct/translated config parity");
    AssertEqual("50 percent from Fast to Slow", translatedConfig.Text, "normalized config slider");

    AssertEqual(true, direct.TryReadSoundVolume(0, out var directSound), "direct sound volume");
    AssertEqual(true, translated.TryReadSoundVolume(0, out var translatedSound), "translated sound volume");
    AssertEqual(directSound, translatedSound, "direct/translated sound-volume parity");

    AssertEqual(true, direct.TryReadMagic(MenuParityFixture.MagicWidgetAddress, out var directMagic), "direct magic selection");
    AssertEqual(true, translated.TryReadMagic(MenuParityFixture.MagicWidgetAddress, out var translatedMagic), "translated magic selection");
    AssertEqual(directMagic, translatedMagic, "direct/translated magic parity");
    AssertEqual(translatedWidget, translatedMagic.Widget, "magic observation reuses the address-free widget shape");
    AssertEqual("Fire", translatedMagic.Spell.Name, "normalized magic name");

    AssertEqual(true, direct.TryReadPartyMember(0, out var directParty), "direct savemap party member");
    AssertEqual(true, translated.TryReadPartyMember(0, out var translatedParty), "translated savemap party member");
    AssertEqual(directParty, translatedParty, "direct/translated savemap party parity");
    AssertEqual("A", translatedParty.Name, "normalized savemap party name");

    AssertEqual(true, direct.TryReadStatusSummary(0, out var directStatus), "direct savemap status summary");
    AssertEqual(true, translated.TryReadStatusSummary(0, out var translatedStatus), "translated savemap status summary");
    AssertEqual(directStatus, translatedStatus, "direct/translated savemap status parity");
    AssertEqual(300, translatedStatus.CurrentHp, "normalized savemap status HP");
    AssertEqual("Weapon 1", translatedStatus.WeaponName, "normalized savemap equipment name");
}

static void AssertQuitConfirmationReaderMatchesDirectAndTranslatedGuestMemory()
{
    var fixture = new MenuParityFixture();
    fixture.WriteInt32(QuitConfirmationStateReader.AddressSelection, 1);
    fixture.WriteInt32(QuitConfirmationStateReader.AddressCompletion, 0);
    fixture.WriteInt32(QuitConfirmationStateReader.AddressVisibleLatch, 1);

    var direct = CreateMenuObservationReader(fixture.Direct);
    var translated = CreateMenuObservationReader(fixture.Translated);

    AssertEqual(true, direct.TryReadQuitConfirmation(out var directNo), "direct Quit confirmation");
    AssertEqual(true, translated.TryReadQuitConfirmation(out var translatedNo), "translated Quit confirmation");
    AssertEqual(directNo, translatedNo, "direct/translated Quit-confirmation parity");
    AssertEqual(1, translatedNo.SelectedIndex, "Quit confirmation defaults to No index");
    AssertEqual("No", translatedNo.SelectedLabel, "Quit confirmation defaults to No");

    fixture.WriteInt32(QuitConfirmationStateReader.AddressSelection, 0);
    AssertEqual(true, translated.TryReadQuitConfirmation(out var translatedYes), "translated Quit Yes selection");
    AssertEqual(0, translatedYes.SelectedIndex, "Quit confirmation Yes index");
    AssertEqual("Yes", translatedYes.SelectedLabel, "Quit confirmation Yes label");
}

static void AssertQuitConfirmationReaderRejectsInactiveAndTornState()
{
    var inactive = new MenuParityFixture();
    inactive.WriteInt32(QuitConfirmationStateReader.AddressSelection, 1);
    inactive.WriteInt32(QuitConfirmationStateReader.AddressCompletion, 0);
    inactive.WriteInt32(QuitConfirmationStateReader.AddressVisibleLatch, 0);
    AssertEqual(
        false,
        CreateMenuObservationReader(inactive.Translated).TryReadQuitConfirmation(out _),
        "hidden Quit confirmation rejected");

    var completed = new MenuParityFixture();
    completed.WriteInt32(QuitConfirmationStateReader.AddressSelection, 1);
    completed.WriteInt32(QuitConfirmationStateReader.AddressCompletion, 1);
    completed.WriteInt32(QuitConfirmationStateReader.AddressVisibleLatch, 1);
    AssertEqual(
        false,
        CreateMenuObservationReader(completed.Translated).TryReadQuitConfirmation(out _),
        "completed Quit confirmation rejected");

    var torn = new MenuParityFixture();
    torn.WriteInt32(QuitConfirmationStateReader.AddressSelection, 1);
    torn.WriteInt32(QuitConfirmationStateReader.AddressCompletion, 0);
    torn.WriteInt32(QuitConfirmationStateReader.AddressVisibleLatch, 1);
    var watchedAddress = torn.GetPageTableEntryAddress(
        unchecked((uint)QuitConfirmationStateReader.AddressSelection));
    var tearingMemory = new RemappingNativeMemoryReader(
        torn.Native,
        watchedAddress,
        triggerRead: 4,
        () => torn.WriteInt32(QuitConfirmationStateReader.AddressSelection, 0));
    var translated = new TranslatedX86AddressSpace(MenuParityFixture.ModuleBase, tearingMemory);
    AssertEqual(
        false,
        CreateMenuObservationReader(translated).TryReadQuitConfirmation(out _),
        "Quit confirmation selector tear rejected");
}

static void AssertMenuObservationReaderRejectsUnmappedDomains()
{
    var main = CreatePopulatedMenuFixture();
    main.UnmapGuestPage((uint)MainMenuStateReader.AddressState);
    AssertEqual(false, CreateMenuObservationReader(main.Translated).TryReadMainMenu(out _), "unmapped main-menu page");

    var widget = CreatePopulatedMenuFixture();
    widget.MapGuestPageToSentinel(MenuParityFixture.MagicWidgetAddress);
    AssertEqual(false, CreateMenuObservationReader(widget.Translated).TryReadActiveWidget(MenuParityFixture.MagicWidgetAddress, out _), "sentinel active-widget page");

    var config = CreatePopulatedMenuFixture();
    config.UnmapGuestPage((uint)ConfigMenuValueReader.AddressBattleSpeed);
    AssertEqual(false, CreateMenuObservationReader(config.Translated).TryReadConfigValue("Battle speed", out _), "unmapped config page");

    var magic = CreatePopulatedMenuFixture();
    magic.UnmapGuestPage((uint)MagicMenuSelectionReader.AddressMagicRecords);
    AssertEqual(false, CreateMenuObservationReader(magic.Translated).TryReadMagic(MenuParityFixture.MagicWidgetAddress, out _), "unmapped nested magic page");

    var party = CreatePopulatedMenuFixture();
    party.UnmapGuestPage((uint)(SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset));
    AssertEqual(false, CreateMenuObservationReader(party.Translated).TryReadPartyMember(0, out _), "unmapped savemap party page");

    var computed = CreatePopulatedMenuFixture();
    computed.UnmapGuestPage((uint)SavemapPartyReader.AddressComputedPartyData);
    AssertEqual(false, CreateMenuObservationReader(computed.Translated).TryReadStatusSummary(0, out _), "unmapped computed-status page");

    var equipment = CreatePopulatedMenuFixture();
    equipment.UnmapGuestPage((uint)SavemapPartyReader.AddressWeaponAttackPercent);
    AssertEqual(false, CreateMenuObservationReader(equipment.Translated).TryReadStatusSummary(0, out _), "unmapped equipment-table page");
}

static void AssertMenuObservationReaderRejectsNestedPageRemapping()
{
    var fixture = CreatePopulatedMenuFixture();
    const ulong replacementHostPage = 0x0000000500900000;
    fixture.WriteWidgetHostPage(replacementHostPage, cursor: 1, selectedPartySlot: 1);
    var watchedEntry = fixture.GetPageTableEntryAddress(MenuParityFixture.MagicWidgetAddress);
    var remappingMemory = new RemappingNativeMemoryReader(
        fixture.Native,
        watchedEntry,
        triggerRead: 15,
        () => fixture.MapGuestPage(MenuParityFixture.MagicWidgetAddress, replacementHostPage));
    var translated = new TranslatedX86AddressSpace(MenuParityFixture.ModuleBase, remappingMemory);
    var reader = CreateMenuObservationReader(translated);

    AssertEqual(
        false,
        reader.TryReadMagic(MenuParityFixture.MagicWidgetAddress, out _),
        "nested widget/magic remapping must fail the outer bookend");
}

static void AssertMenuObservationReaderRejectsMainMenuTransitionTearing()
{
    var fixture = CreatePopulatedMenuFixture();
    const ulong replacementHostPage = 0x0000000600A00000;
    fixture.Native.Write(
        replacementHostPage + ((uint)MainMenuStateReader.AddressState & 0xFFF),
        BitConverter.GetBytes(5));
    var watchedEntry = fixture.GetPageTableEntryAddress((uint)MainMenuStateReader.AddressState);
    var remappingMemory = new RemappingNativeMemoryReader(
        fixture.Native,
        watchedEntry,
        triggerRead: 9,
        () => fixture.MapGuestPage((uint)MainMenuStateReader.AddressState, replacementHostPage));
    var translated = new TranslatedX86AddressSpace(MenuParityFixture.ModuleBase, remappingMemory);

    AssertEqual(
        false,
        CreateMenuObservationReader(translated).TryReadMainMenu(out _),
        "main-menu transition remapping must fail the shared bookend");
}

static void AssertMenuObservationReaderRejectsPartySelectorTearing()
{
    var fixture = CreatePopulatedMenuFixture();
    const ulong replacementHostPage = 0x0000000700B00000;
    var partyAddress = (uint)(SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset);
    fixture.Native.Write(replacementHostPage + (partyAddress & 0xFFF), [1]);
    var watchedEntry = fixture.GetPageTableEntryAddress(partyAddress);
    var remappingMemory = new RemappingNativeMemoryReader(
        fixture.Native,
        watchedEntry,
        triggerRead: 2,
        () => fixture.MapGuestPage(partyAddress, replacementHostPage));
    var translated = new TranslatedX86AddressSpace(MenuParityFixture.ModuleBase, remappingMemory);

    AssertEqual(
        false,
        CreateMenuObservationReader(translated).TryReadPartyMember(0, out _),
        "savemap party selector remapping must fail its bookend");
}

static void AssertMenuObservationReaderRejectsNestedStatusRemapping()
{
    var fixture = CreatePopulatedMenuFixture();
    const ulong replacementHostPage = 0x0000000800C00000;
    fixture.Native.Write(
        replacementHostPage + ((uint)SavemapPartyReader.AddressComputedPartyData & 0xFFF)
            + SavemapPartyReader.ComputedStrengthOffset,
        [30, 21, 22, 23, 24, 25]);
    fixture.Native.Write(
        replacementHostPage + ((uint)SavemapPartyReader.AddressComputedPartyData & 0xFFF)
            + SavemapPartyReader.ComputedAttackOffset,
        BitConverter.GetBytes((ushort)30));
    fixture.Native.Write(
        replacementHostPage + ((uint)SavemapPartyReader.AddressComputedPartyData & 0xFFF)
            + SavemapPartyReader.ComputedDefenseOffset,
        BitConverter.GetBytes((ushort)31));
    fixture.Native.Write(
        replacementHostPage + ((uint)SavemapPartyReader.AddressComputedPartyData & 0xFFF)
            + SavemapPartyReader.ComputedMagicAttackOffset,
        BitConverter.GetBytes((ushort)32));
    fixture.Native.Write(
        replacementHostPage + ((uint)SavemapPartyReader.AddressComputedPartyData & 0xFFF)
            + SavemapPartyReader.ComputedMagicDefenseOffset,
        BitConverter.GetBytes((ushort)33));
    var watchedEntry = fixture.GetPageTableEntryAddress((uint)SavemapPartyReader.AddressComputedPartyData);
    var remappingMemory = new RemappingNativeMemoryReader(
        fixture.Native,
        watchedEntry,
        triggerRead: 11,
        () => fixture.MapGuestPage((uint)SavemapPartyReader.AddressComputedPartyData, replacementHostPage));
    var translated = new TranslatedX86AddressSpace(MenuParityFixture.ModuleBase, remappingMemory);

    AssertEqual(
        false,
        CreateMenuObservationReader(translated).TryReadStatusSummary(0, out _),
        "nested savemap status remapping must fail the aggregate bookend");
}

static void AssertMenuObservationReaderRejectsUnknownGuestWidgetAddresses()
{
    const uint highWidgetAddress = 0xF1234000u;
    var fixture = new MenuParityFixture();
    fixture.WriteWidget(highWidgetAddress, cursor: 2, selectedPartySlot: 1);
    var direct = CreateMenuObservationReader(fixture.Direct);
    var translated = CreateMenuObservationReader(fixture.Translated);

    AssertEqual(false, direct.TryReadActiveWidget(highWidgetAddress, out _), "direct unknown high-bit widget is not public evidence");
    AssertEqual(false, translated.TryReadActiveWidget(highWidgetAddress, out _), "translated unknown high-bit widget is not public evidence");

    var rawReader = new ActiveMenuWidgetReader(fixture.Direct);
    AssertEqual(true, rawReader.TryRead(highWidgetAddress, out var rawWidget), "internal raw reader preserves unsigned guest address");
    AssertEqual(highWidgetAddress, rawWidget.Address, "internal raw reader keeps full guest address for validation");

    var unmapped = new MenuParityFixture();
    AssertEqual(false, CreateMenuObservationReader(unmapped.Translated).TryReadActiveWidget(highWidgetAddress, out _), "unmapped high-bit widget fails closed");
    AssertEqual(false, new ActiveMenuWidgetReader(fixture.Direct).TryRead(unchecked((int)highWidgetAddress), out _), "negative int compatibility widget address rejected");
}

static Steam2026MenuObservationReader CreateMenuObservationReader(ILegacyAddressSpace addressSpace) =>
    new(
        addressSpace,
        spellId => spellId == 7 ? "Fire" : null,
        spellId => spellId == 7 ? "Fire damage" : null,
        weaponId => $"Weapon {weaponId}",
        armorId => $"Armor {armorId}",
        accessoryId => $"Accessory {accessoryId}");

static void AssertMenuObservationReaderPublicConstructionRequiresExactFingerprint(
    Steam2026FingerprintResult supported,
    Steam2026FingerprintResult unsupported)
{
    var constructors = typeof(Steam2026MenuObservationReader).GetConstructors();
    AssertEqual(1, constructors.Length, "menu facade public constructor count");
    AssertEqual(
        typeof(Steam2026FingerprintResult),
        constructors[0].GetParameters()[0].ParameterType,
        "menu facade public constructor requires fingerprint");

    var valid = new MenuParityFixture();
    valid.Native.Write(
        MenuParityFixture.ModuleBase + TranslatedX86AddressSpace.ResolverRva,
        Convert.FromHexString(
            "85C9750333C0C38BC1488D15609F6F018BC948C1E90C488B14CA4885D2740925FF0F00004803C2C3488D05319C6F01C3"));
    _ = new Steam2026MenuObservationReader(
        supported,
        MenuParityFixture.ModuleBase,
        valid.Native,
        _ => null,
        _ => null);

    var unsupportedRejected = false;
    try
    {
        _ = new Steam2026MenuObservationReader(
            unsupported,
            MenuParityFixture.ModuleBase,
            valid.Native,
            _ => null,
            _ => null);
    }
    catch (ArgumentException)
    {
        unsupportedRejected = true;
    }

    AssertEqual(true, unsupportedRejected, "menu facade rejects unsupported executable fingerprint");

    var corruptResolver = new MenuParityFixture();
    corruptResolver.Native.Write(
        MenuParityFixture.ModuleBase + TranslatedX86AddressSpace.ResolverRva,
        [0x90]);
    var resolverRejected = false;
    try
    {
        _ = new Steam2026MenuObservationReader(
            supported,
            MenuParityFixture.ModuleBase,
            corruptResolver.Native,
            _ => null,
            _ => null);
    }
    catch (InvalidOperationException)
    {
        resolverRejected = true;
    }

    AssertEqual(true, resolverRejected, "menu facade rejects corrupt translated resolver");
}

static MenuParityFixture CreatePopulatedMenuFixture()
{
    var fixture = new MenuParityFixture();
    fixture.WriteInt32(MainMenuStateReader.AddressState, 1);
    fixture.WriteInt32(MainMenuStateReader.AddressSelectedA, 0);
    fixture.WriteInt32(MainMenuStateReader.AddressSelectedB, 4);
    fixture.WriteInt32(MainMenuStateReader.AddressCursorIndex, 2);
    fixture.WriteInt32(MainMenuStateReader.AddressTarget, 2);
    fixture.WriteInt32(MainMenuStateReader.AddressOpenFlag, 1);
    fixture.WriteUInt32(MainMenuStateReader.AddressEnabledMask, 0x7ffu);
    fixture.WriteUInt32(MainMenuStateReader.AddressDisabledMask, 0);
    fixture.WriteInt32(MainMenuStateReader.AddressAnimation, 16);

    fixture.WriteWidget(MenuParityFixture.MagicWidgetAddress, cursor: 2, selectedPartySlot: 1);
    fixture.WriteUInt16(
        MagicMenuSelectionReader.AddressCurrentMp + MagicMenuSelectionReader.CharacterBlockSize,
        40);
    fixture.WriteByte(ConfigMenuValueReader.AddressBattleSpeed, 128);
    fixture.WriteInt32(ConfigMenuValueReader.AddressCurrentRow, 5);
    fixture.WriteUInt16(ConfigMenuValueReader.AddressSettingsBits, 0x0040);
    fixture.WriteInt32(ConfigMenuValueReader.AddressSoundModalState, ConfigMenuValueReader.SoundModalActiveState);
    fixture.WriteInt32(ConfigMenuValueReader.AddressMusicVolume, 73);

    const int selectedIndex = 10;
    var recordAddress = MagicMenuSelectionReader.AddressMagicRecords
        + MagicMenuSelectionReader.CharacterBlockSize
        + (selectedIndex * MagicMenuSelectionReader.RecordSize);
    fixture.Write((uint)recordAddress, [7, 12]);

    fixture.WriteByte(SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset, 0);
    var characterBase = SavemapPartyReader.AddressSavemap + SavemapPartyReader.CharactersOffset;
    fixture.Write(
        (uint)(characterBase + SavemapPartyReader.CharacterNameOffset),
        [0x21, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);
    fixture.WriteByte(characterBase + SavemapPartyReader.LevelOffset, 15);
    fixture.WriteByte(characterBase + SavemapPartyReader.LimitLevelOffset, 2);
    fixture.Write(
        (uint)(characterBase + SavemapPartyReader.EquippedWeaponOffset),
        [1, 2, 0xFF]);
    fixture.WriteUInt16(characterBase + SavemapPartyReader.CurrentHpOffset, 300);
    fixture.WriteUInt16(characterBase + SavemapPartyReader.CurrentMpOffset, 40);
    fixture.WriteUInt16(characterBase + SavemapPartyReader.MaxHpOffset, 500);
    fixture.WriteUInt16(characterBase + SavemapPartyReader.MaxMpOffset, 60);
    fixture.WriteUInt32(characterBase + SavemapPartyReader.ExperienceOffset, 1234);
    fixture.WriteUInt32(characterBase + SavemapPartyReader.ExperienceToNextLevelOffset, 234);

    var computed = SavemapPartyReader.AddressComputedPartyData;
    fixture.Write(
        (uint)(computed + SavemapPartyReader.ComputedStrengthOffset),
        [20, 21, 22, 23, 24, 25]);
    fixture.WriteUInt16(computed + SavemapPartyReader.ComputedAttackOffset, 30);
    fixture.WriteUInt16(computed + SavemapPartyReader.ComputedDefenseOffset, 31);
    fixture.WriteUInt16(computed + SavemapPartyReader.ComputedMagicAttackOffset, 32);
    fixture.WriteUInt16(computed + SavemapPartyReader.ComputedMagicDefenseOffset, 33);
    fixture.WriteByte(
        SavemapPartyReader.AddressWeaponAttackPercent + SavemapPartyReader.WeaponRecordSize,
        96);
    fixture.Write(
        (uint)(SavemapPartyReader.AddressArmorDefensePercent + (2 * SavemapPartyReader.ArmorRecordSize)),
        [11, 4]);
    return fixture;
}

static void AssertEqual<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    }
}

sealed class FakeNativeMemoryReader : INativeMemoryReader
{
    private readonly Dictionary<ulong, byte> bytes = [];
    private readonly List<NativeMemoryRegion> regions = [];

    public void MapRegion(
        ulong baseAddress,
        ulong size,
        ulong allocationBase,
        bool isCommitted,
        bool isExecutable,
        bool isImage = true,
        bool isReadable = true) =>
        regions.Add(new NativeMemoryRegion(
            baseAddress,
            size,
            allocationBase,
            isCommitted,
            isExecutable,
            isImage,
            isReadable));

    public void ClearRegions() => regions.Clear();

    public void MapVirtualPage(ulong moduleBase, uint pageIndex, ulong hostPage)
    {
        var pageTableAddress = moduleBase + TranslatedX86AddressSpace.PageTableRva;
        var pageTableByteLength = checked((ulong)TranslatedX86AddressSpace.PageCount * sizeof(ulong));
        if (!regions.Any(region =>
                region.BaseAddress == pageTableAddress
                && region.Size == pageTableByteLength
                && region.AllocationBase == moduleBase))
        {
            MapRegion(
                pageTableAddress,
                pageTableByteLength,
                moduleBase,
                isCommitted: true,
                isExecutable: false);
        }

        var entryAddress = moduleBase
                           + TranslatedX86AddressSpace.PageTableRva
                           + (pageIndex * sizeof(ulong));
        Write(entryAddress, BitConverter.GetBytes(hostPage));
    }

    public void Write(ulong address, IReadOnlyList<byte> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            bytes[address + (ulong)index] = values[index];
        }
    }

    public bool TryReadUInt64(ulong address, out ulong value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        if (!TryRead(address, buffer))
        {
            value = 0;
            return false;
        }

        value = BitConverter.ToUInt64(buffer);
        return true;
    }

    public bool TryRead(ulong address, Span<byte> destination)
    {
        for (var index = 0; index < destination.Length; index++)
        {
            if (!bytes.TryGetValue(address + (ulong)index, out destination[index]))
            {
                destination.Clear();
                return false;
            }
        }

        return true;
    }

    public bool TryQueryRegion(ulong address, out NativeMemoryRegion region)
    {
        for (var index = regions.Count - 1; index >= 0; index--)
        {
            var candidate = regions[index];
            if (candidate.Size > 0
                && candidate.BaseAddress <= ulong.MaxValue - (candidate.Size - 1)
                && address >= candidate.BaseAddress
                && address <= candidate.BaseAddress + candidate.Size - 1)
            {
                region = candidate;
                return true;
            }
        }

        region = default;
        return false;
    }
}

sealed class MenuParityFixture
{
    public const ulong ModuleBase = 0x0000000140000000;
    public const uint MagicWidgetAddress = 0x00DD1708;

    private readonly Dictionary<uint, ulong> hostPages = [];
    private ulong nextHostPage = 0x0000000201000000;

    public MenuParityFixture()
    {
        Direct = new DirectGuestMemory();
        Native = new FakeNativeMemoryReader();
        Translated = new TranslatedX86AddressSpace(ModuleBase, Native);
    }

    public DirectGuestMemory Direct { get; }

    public FakeNativeMemoryReader Native { get; }

    public TranslatedX86AddressSpace Translated { get; }

    public void WriteByte(int address, byte value) => Write((uint)address, [value]);

    public void WriteInt32(int address, int value) => Write((uint)address, BitConverter.GetBytes(value));

    public void WriteInt32(uint address, int value) => Write(address, BitConverter.GetBytes(value));

    public void WriteUInt16(int address, ushort value) => Write((uint)address, BitConverter.GetBytes(value));

    public void WriteUInt32(int address, uint value) => Write((uint)address, BitConverter.GetBytes(value));

    public void Write(uint address, IReadOnlyList<byte> values)
    {
        Direct.Write(address, values);
        for (var index = 0; index < values.Count; index++)
        {
            var guestAddress = checked(address + (uint)index);
            var pageIndex = guestAddress >> 12;
            if (!hostPages.TryGetValue(pageIndex, out var hostPage))
            {
                hostPage = nextHostPage;
                nextHostPage += 0x3000;
                hostPages.Add(pageIndex, hostPage);
                Native.MapVirtualPage(ModuleBase, pageIndex, hostPage);
            }

            Native.Write(hostPage + (guestAddress & 0xFFF), [values[index]]);
        }
    }

    public void WriteWidget(uint address, int cursor, byte selectedPartySlot)
    {
        WriteInt32(address, 1);
        WriteInt32(address + 0x04, cursor);
        WriteInt32(address + 0x08, 3);
        WriteInt32(address + 0x0C, 20);
        WriteInt32(address + 0x14, 1);
        WriteInt32(address + 0x24, 0);
        WriteInt32(address + 0x30, 0);
        WriteByte(MagicMenuSelectionReader.AddressSelectedPartySlot, selectedPartySlot);
    }

    public void WriteWidgetHostPage(ulong hostPage, int cursor, byte selectedPartySlot)
    {
        WriteHostInt32(hostPage, MagicWidgetAddress, 0x00, 1);
        WriteHostInt32(hostPage, MagicWidgetAddress, 0x04, cursor);
        WriteHostInt32(hostPage, MagicWidgetAddress, 0x08, 3);
        WriteHostInt32(hostPage, MagicWidgetAddress, 0x0C, 20);
        WriteHostInt32(hostPage, MagicWidgetAddress, 0x14, 1);
        WriteHostInt32(hostPage, MagicWidgetAddress, 0x24, 0);
        WriteHostInt32(hostPage, MagicWidgetAddress, 0x30, 0);
        Native.Write(
            hostPage + ((uint)MagicMenuSelectionReader.AddressSelectedPartySlot & 0xFFF),
            [selectedPartySlot]);
    }

    public ulong GetPageTableEntryAddress(uint guestAddress) =>
        ModuleBase + TranslatedX86AddressSpace.PageTableRva + ((guestAddress >> 12) * sizeof(ulong));

    public void MapGuestPage(uint guestAddress, ulong hostPage) =>
        Native.MapVirtualPage(ModuleBase, guestAddress >> 12, hostPage);

    public void UnmapGuestPage(uint guestAddress) => MapGuestPage(guestAddress, 0);

    public void MapGuestPageToSentinel(uint guestAddress) =>
        MapGuestPage(guestAddress, ModuleBase + TranslatedX86AddressSpace.UnmappedSentinelRva);

    private void WriteHostInt32(ulong hostPage, uint guestBase, int offset, int value) =>
        Native.Write(
            hostPage + ((guestBase + (uint)offset) & 0xFFF),
            BitConverter.GetBytes(value));
}

sealed class DirectGuestMemory : ILegacyAddressSpace
{
    private readonly Dictionary<uint, byte> bytes = [];

    public void Write(uint address, IReadOnlyList<byte> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            bytes[checked(address + (uint)index)] = values[index];
        }
    }

    public bool TryRead(uint virtualAddress, Span<byte> destination)
    {
        if (virtualAddress == 0 || (ulong)virtualAddress + (ulong)destination.Length > (ulong)uint.MaxValue + 1)
        {
            destination.Clear();
            return false;
        }

        for (var index = 0; index < destination.Length; index++)
        {
            if (!bytes.TryGetValue(virtualAddress + (uint)index, out destination[index]))
            {
                destination.Clear();
                return false;
            }
        }

        return true;
    }
}

sealed class RemappingNativeMemoryReader(
    FakeNativeMemoryReader inner,
    ulong watchedAddress,
    int triggerRead,
    Action remap) : INativeMemoryReader
{
    private int matchingReads;

    public bool TryReadUInt64(ulong address, out ulong value)
    {
        if (address == watchedAddress && ++matchingReads == triggerRead)
        {
            remap();
        }

        return inner.TryReadUInt64(address, out value);
    }

    public bool TryRead(ulong address, Span<byte> destination) => inner.TryRead(address, destination);

    public bool TryQueryRegion(ulong address, out NativeMemoryRegion region) =>
        inner.TryQueryRegion(address, out region);
}

sealed class TearingNativeMemoryReader(
    FakeNativeMemoryReader inner,
    ulong watchedAddress,
    int triggerRead,
    Action tear) : INativeMemoryReader
{
    private int matchingReads;

    public bool TryReadUInt64(ulong address, out ulong value) => inner.TryReadUInt64(address, out value);

    public bool TryRead(ulong address, Span<byte> destination)
    {
        if (address == watchedAddress && ++matchingReads == triggerRead)
        {
            tear();
        }

        return inner.TryRead(address, destination);
    }

    public bool TryQueryRegion(ulong address, out NativeMemoryRegion region) =>
        inner.TryQueryRegion(address, out region);
}

sealed class TranslatedCallCaptureFixture
{
    public const ulong ModuleBase = 0x0000000140000000;

    private const ulong ModuleImageSize = 0x02100000;

    private readonly Dictionary<uint, ulong> hostPages = [];
    private ulong nextHostPage = 0x0000000600000000;

    public FakeNativeMemoryReader Native { get; } = new();

    public TranslatedCallCaptureFixture()
    {
        Native.Write(
            ModuleBase + TranslatedX86AddressSpace.ResolverRva,
            Convert.FromHexString(
                "85C9750333C0C38BC1488D15609F6F018BC948C1E90C488B14CA4885D2740925FF0F00004803C2C3488D05319C6F01C3"));
        Native.MapRegion(
            ModuleBase + 0x01000000,
            0x00400000,
            ModuleBase,
            isCommitted: true,
            isExecutable: true);
        foreach (var kind in Enum.GetValues<Steam2026MenuCallbackKind>())
        {
            var metadata = Steam2026MenuCallbackCatalog.GetMetadata(kind);
            Native.Write(
                ModuleBase + metadata.FunctionMap.MappingRecordRva,
                BitConverter.GetBytes((ulong)metadata.FunctionMap.LegacyVirtualAddress));
            Native.Write(
                ModuleBase + metadata.FunctionMap.MappingRecordRva + sizeof(ulong),
                BitConverter.GetBytes(ModuleBase + metadata.FunctionMap.HostRva));
            Native.Write(
                ModuleBase + metadata.FunctionMap.HostRva,
                Convert.FromHexString(metadata.FunctionMap.ExpectedPrefixHex));
        }
    }

    public Steam2026MenuCallbackContract CreateDecoder(INativeMemoryReader? memory = null) =>
        new(ModuleBase, ModuleImageSize, memory ?? Native);

    public void CorruptResolverSignature() =>
        Native.Write(ModuleBase + TranslatedX86AddressSpace.ResolverRva, [0x90]);

    public void WriteCall(uint esp, IReadOnlyList<uint> arguments)
    {
        WriteStack(esp, arguments);
        SetEsp(esp);
    }

    public void WriteStack(uint esp, IReadOnlyList<uint> arguments)
    {
        WriteGuest(esp, BitConverter.GetBytes(0xDEADBEEFu));
        for (var index = 0; index < arguments.Count; index++)
        {
            WriteGuest(
                checked(esp + sizeof(uint) + ((uint)index * sizeof(uint))),
                BitConverter.GetBytes(arguments[index]));
        }
    }

    public void SetEsp(uint esp) =>
        Native.Write(ModuleBase + TranslatedX86CallFrameReader.EspRva, BitConverter.GetBytes(esp));

    public void WriteGuest(uint address, IReadOnlyList<byte> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            var guestAddress = checked(address + (uint)index);
            var pageIndex = guestAddress >> 12;
            if (!hostPages.TryGetValue(pageIndex, out var hostPage))
            {
                hostPage = nextHostPage;
                nextHostPage += 0x2000;
                hostPages.Add(pageIndex, hostPage);
                MapGuestPage(guestAddress, hostPage);
            }

            Native.Write(hostPage + (guestAddress & 0xFFF), [values[index]]);
        }
    }

    public ulong GetPageTableEntryAddress(uint guestAddress) =>
        ModuleBase
        + TranslatedX86AddressSpace.PageTableRva
        + ((guestAddress >> 12) * sizeof(ulong));

    public void MapGuestPage(uint guestAddress, ulong hostPage) =>
        Native.MapVirtualPage(ModuleBase, guestAddress >> 12, hostPage);
}

sealed class EspAbaNativeMemoryReader(
    FakeNativeMemoryReader inner,
    ulong watchedAddress,
    int switchRead,
    int restoreRead,
    Action switchEsp,
    Action restoreEsp) : INativeMemoryReader
{
    private int matchingReads;

    public bool TryReadUInt64(ulong address, out ulong value) => inner.TryReadUInt64(address, out value);

    public bool TryRead(ulong address, Span<byte> destination)
    {
        if (address == watchedAddress)
        {
            matchingReads++;
            if (matchingReads == switchRead)
            {
                switchEsp();
            }
            else if (matchingReads == restoreRead)
            {
                restoreEsp();
            }
        }

        return inner.TryRead(address, destination);
    }

    public bool TryQueryRegion(ulong address, out NativeMemoryRegion region) =>
        inner.TryQueryRegion(address, out region);
}

sealed class RecordingEventSink : IRuntimeEventSink
{
    public RuntimeEventPublishResult Publish(RuntimeEvent runtimeEvent)
    {
        throw new InvalidOperationException("Incomplete x64 backend must not publish runtime events.");
    }
}

readonly record struct NativeMovieSignatureCase(
    NativeMovieCallbackKind Kind,
    ulong Rva,
    string SignatureHex,
    NativeMovieCallbackShape Shape,
    bool Hookable);

readonly record struct NativeMovieContractFixture(
    ulong ModuleBase,
    ulong ModuleImageSize,
    FakeNativeMemoryReader Memory,
    NativeMovieCallbackContract Contract);

readonly record struct MenuCallbackCase(
    Steam2026MenuCallbackKind Kind,
    uint LegacyVirtualAddress,
    ulong MappingRecordRva,
    ulong HostRva,
    string PrefixHex,
    bool CaptureEligible);
