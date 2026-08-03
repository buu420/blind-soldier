using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime;
using Ff7.Accessibility.Steam2026X64.Runtime.Menus;

internal static class Steam2026TranslatedMenuIngressTests
{
    private const ulong ModuleImageSize = 0x02100000;
    private const uint KnownWidgetAddress = 0x00DC1150;
    private static readonly DateTime Timestamp =
        new(2026, 7, 20, 1, 0, 0, DateTimeKind.Utc);

    private static readonly Steam2026MenuCallbackKind[] CaptureKinds =
    [
        Steam2026MenuCallbackKind.CursorB,
        Steam2026MenuCallbackKind.CursorA,
        Steam2026MenuCallbackKind.ActiveWidgetUpdate,
        Steam2026MenuCallbackKind.EncodedTextB,
        Steam2026MenuCallbackKind.EncodedTextA,
        Steam2026MenuCallbackKind.AsciiRenderer
    ];

    public static void Run(
        Steam2026FingerprintResult supported,
        Steam2026FingerprintResult unsupported)
    {
        DelegateAndIngressSurfaceMatchOnlySixProvenCallbacks();
        BoundedIngressQueueUsesNoBlockingPrimitive();
        BoundedIngressQueueRejectsOverflowWithoutBlocking();
        BoundedIngressQueueCapsConcurrentProducers();
        ConstructionRequiresExactFingerprintAndEveryCurrentIdentity(supported, unsupported);
        EncodedTextCallbackStaysWithinBoundedNativeReadBudget(supported);
        ActiveLeaseMappingCorruptionFailsTheRateLimitedHealthProbe(supported);
        ActiveLeaseCohortDisableFailsTheRateLimitedHealthProbe(supported);
        HookSetPollsLeaseHealthOutsideTheNativeCallbackPath();
        CapturesEveryGuestPayloadBeforeOriginalAndPublishesAfter(supported);
        ActiveWidgetUsesCheckedPointerFreeNormalization(supported);
        CaptureIdentityClockAndSinkFailuresAreContained(supported);
        QueueOverflowPermanentlyDegradesIngress(supported);
        CommittedSnapshotCanFinishPublicationAfterStop(supported);
        OriginalFailuresAreContainedAndPermanentlyDegradeIngress(supported);
        ReentrantCallbacksKeepCapturesIsolatedAndFailClosed(supported);
        ConcurrentCallbacksDoNotSerializeOriginalInvocations(supported);
        StopDoesNotWaitForInFlightObservation(supported);
        StopAndDisposeAreIdempotentWhileOriginalsRemainCallable(supported);
        IngressHasNoBackendCapabilityInstallationOrSpeechSurface(supported);
    }

    private static void EncodedTextCallbackStaysWithinBoundedNativeReadBudget(
        Steam2026FingerprintResult supported)
    {
        const uint textAddress = 0x00290000;
        var fixture = new TranslatedCallCaptureFixture();
        var paddedText = Enumerable.Repeat((byte)0x21, 128).ToArray();
        paddedText[20] = 0xFF;
        fixture.WriteGuest(textAddress, paddedText);
        fixture.WriteCall(
            0x00192000,
            [unchecked((uint)-4), 77, textAddress, 0x22, 0x3344]);
        var memory = new CountingNativeMemoryReader(fixture.Native);
        var contract = CreateExactContract(fixture, supported, memory);
        var snapshots = new List<TranslatedMenuIngressSnapshot>();
        using var coordinator = CreateCoordinator(contract, captureSink: snapshots.Add);
        contract.ActivateHookLease(_ => true);
        memory.Reset();

        coordinator.OnEncodedTextA();

        Equal(1, snapshots.Count, "bounded encoded-text callback still publishes");
        Equal(
            true,
            memory.ReadOperations <= 24,
            $"encoded-text native read budget is bounded: {memory.ReadOperations}");
        Equal(
            true,
            memory.QueryOperations <= 8,
            $"encoded-text native query budget is bounded: {memory.QueryOperations}");
        contract.RevokeHookLease();
    }

    private static void ActiveLeaseMappingCorruptionFailsTheRateLimitedHealthProbe(
        Steam2026FingerprintResult supported)
    {
        var fixture = new TranslatedCallCaptureFixture();
        var contract = CreateExactContract(fixture, supported);
        contract.ActivateHookLease(_ => true);

        Equal(true, ProbeActiveLeaseHealth(contract, 0), "initial menu lease health");
        var metadata = Steam2026MenuCallbackCatalog.GetMetadata(
            Steam2026MenuCallbackKind.CursorA);
        fixture.Native.Write(
            TranslatedCallCaptureFixture.ModuleBase + metadata.FunctionMap.MappingRecordRva,
            new byte[TranslatedFunctionMapValidator.MappingRecordSize]);

        Equal(
            true,
            ProbeActiveLeaseHealth(contract, 999),
            "menu lease health probe is rate limited for one second");
        Equal(
            false,
            ProbeActiveLeaseHealth(contract, 1000),
            "mapped menu identity loss poisons the active lease");

        contract.RevokeHookLease();
        Equal(
            false,
            ProbeActiveLeaseHealth(contract, 2000),
            "unexpected revoked menu lease is structurally unhealthy");
    }

    private static void ActiveLeaseCohortDisableFailsTheRateLimitedHealthProbe(
        Steam2026FingerprintResult supported)
    {
        var fixture = new TranslatedCallCaptureFixture();
        var contract = CreateExactContract(fixture, supported);
        var cohortEnabled = true;
        contract.ActivateHookLease(_ => cohortEnabled);

        Equal(true, ProbeActiveLeaseHealth(contract, 0), "initial enabled menu cohort");
        cohortEnabled = false;
        Equal(
            true,
            ProbeActiveLeaseHealth(contract, 999),
            "disabled menu cohort waits for the next health interval");
        Equal(
            false,
            ProbeActiveLeaseHealth(contract, 1000),
            "disabled menu cohort poisons the active lease");

        cohortEnabled = true;
        Equal(
            false,
            ProbeActiveLeaseHealth(contract, 2000),
            "poisoned menu lease health remains sticky");
        contract.RevokeHookLease();
    }

    private static void HookSetPollsLeaseHealthOutsideTheNativeCallbackPath()
    {
        var prototypeRoot = FindPrototypeRoot();
        var projectRoot = Path.Combine(
            prototypeRoot,
            "reloaded",
            "Ff7.Accessibility.Steam2026X64");
        var hookSetSource = File.ReadAllText(Path.Combine(
            projectRoot,
            "Runtime",
            "Menus",
            "Steam2026TranslatedMenuHookSet.cs"));
        var ingressSource = File.ReadAllText(Path.Combine(
            projectRoot,
            "Runtime",
            "Menus",
            "Steam2026TranslatedMenuDetourIngressCoordinator.cs"));

        Equal(
            true,
            hookSetSource.Contains(
                "IsActiveHookLeaseHealthy(Environment.TickCount64)",
                StringComparison.Ordinal),
            "menu hook owner polls the rate-limited lease health probe");
        Equal(
            false,
            ingressSource.Contains("IsActiveHookLeaseHealthy", StringComparison.Ordinal),
            "menu native callback ingress performs no full lease-health validation");
    }

    private static void DelegateAndIngressSurfaceMatchOnlySixProvenCallbacks()
    {
        Equal(8, IntPtr.Size, "translated menu ingress tests execute in x64 process");
        var delegateType = typeof(TranslatedMenuCallbackOriginal);
        Equal(false, delegateType.IsPublic, "translated menu original delegate remains internal");
        var unmanaged = delegateType.GetCustomAttribute<UnmanagedFunctionPointerAttribute>()
                        ?? throw new InvalidOperationException("Translated menu original lacks unmanaged ABI metadata.");
        Equal(CallingConvention.Winapi, unmanaged.CallingConvention, "translated menu original Windows ABI");
        var invoke = delegateType.GetMethod("Invoke")
                     ?? throw new InvalidOperationException("Translated menu original lacks Invoke.");
        Equal(typeof(void), invoke.ReturnType, "translated menu original return type");
        Equal(0, invoke.GetParameters().Length, "translated menu original has no native parameters");

        var ingressType = typeof(Steam2026TranslatedMenuDetourIngressCoordinator);
        Equal(false, ingressType.IsPublic, "translated menu ingress remains internal");
        var ingressConstructors = ingressType.GetConstructors(
            BindingFlags.Instance | BindingFlags.NonPublic);
        Equal(1, ingressConstructors.Length, "translated menu ingress internal constructor count");
        Equal(
            6,
            ingressConstructors[0].GetParameters().Count(parameter => parameter.ParameterType == delegateType),
            "translated menu ingress requires one original for each proven callback");
        var callbackMethods = ingressType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(method => method.Name.StartsWith("On", StringComparison.Ordinal))
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        SequenceEqual(
            new[]
            {
                "OnActiveWidgetUpdate",
                "OnAsciiRenderer",
                "OnCursorA",
                "OnCursorB",
                "OnEncodedTextA",
                "OnEncodedTextB"
            },
            callbackMethods,
            "translated menu ingress exposes exactly six callbacks");
        Equal(false, callbackMethods.Any(name => name.Contains("Constructor", StringComparison.Ordinal)), "widget constructor has no ingress method");

        foreach (var kind in CaptureKinds)
        {
            var metadata = Steam2026MenuCallbackCatalog.GetMetadata(kind);
            Equal(true, metadata.IsCaptureEligible, $"{kind} remains capture eligible");
            Equal(TranslatedMenuHostAbi.TranslatedX86VoidNoArguments, metadata.HostAbi, $"{kind} translated host ABI");
        }

        Equal(
            false,
            Steam2026MenuCallbackCatalog.GetMetadata(Steam2026MenuCallbackKind.WidgetConstructor).IsCaptureEligible,
            "widget constructor remains identity-only");
    }

    private static void BoundedIngressQueueRejectsOverflowWithoutBlocking()
    {
        var queue = new BoundedNativeIngressQueue<int>(1);
        Equal(true, queue.TryEnqueue(11), "bounded ingress queue accepts first item");

        var overflow = Task.Factory.StartNew(
            () => queue.TryEnqueue(22),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Equal(true, overflow.Wait(TimeSpan.FromSeconds(1)), "bounded ingress queue overflow returns without blocking");
        Equal(false, overflow.Result, "bounded ingress queue rejects overflow");
        Equal(true, queue.TryDequeue(out var first), "bounded ingress queue exposes queued item");
        Equal(11, first, "bounded ingress queue preserves the existing item");
        Equal(true, queue.TryEnqueue(22), "bounded ingress queue accepts after dequeue");
        Equal(true, queue.TryDequeue(out var second), "bounded ingress queue exposes replacement item");
        Equal(22, second, "bounded ingress queue preserves replacement item");
    }

    private static void BoundedIngressQueueUsesNoBlockingPrimitive()
    {
        var source = File.ReadAllText(Path.Combine(
            FindPrototypeRoot(),
            "reloaded",
            "Ff7.Accessibility.Steam2026X64",
            "Runtime",
            "NativeIngressQueue.cs"));
        foreach (var forbidden in new[]
                 {
                     "System.Threading.Channels",
                     "Channel<",
                     "ConcurrentQueue",
                     "lock (",
                     "Monitor.",
                     ".Wait(",
                     "Semaphore"
                 })
        {
            Equal(false, source.Contains(forbidden, StringComparison.Ordinal), $"native ingress queue excludes blocking primitive {forbidden}");
        }
    }

    private static void BoundedIngressQueueCapsConcurrentProducers()
    {
        const int capacity = 64;
        const int attempts = 256;
        var queue = new BoundedNativeIngressQueue<int>(capacity);
        var accepted = new ConcurrentBag<int>();
        Parallel.For(0, attempts, value =>
        {
            if (queue.TryEnqueue(value))
            {
                accepted.Add(value);
            }
        });

        Equal(capacity, accepted.Count, "bounded ingress queue caps concurrent producers exactly");
        var dequeued = new HashSet<int>();
        while (queue.TryDequeue(out var value))
        {
            Equal(true, dequeued.Add(value), "bounded ingress queue dequeues each accepted item once");
        }

        Equal(capacity, dequeued.Count, "bounded ingress queue drains every accepted concurrent item");
        Equal(true, accepted.All(dequeued.Contains), "bounded ingress queue preserves all accepted concurrent items");
    }

    private static void ConstructionRequiresExactFingerprintAndEveryCurrentIdentity(
        Steam2026FingerprintResult supported,
        Steam2026FingerprintResult unsupported)
    {
        var unsupportedFixture = new TranslatedCallCaptureFixture();
        Equal(
            true,
            Throws<ArgumentException>(() => _ = CreateExactContract(unsupportedFixture, unsupported)),
            "exact menu ingress contract rejects unsupported fingerprint");

        var ungatedFixture = new TranslatedCallCaptureFixture();
        Equal(
            true,
            Throws<InvalidOperationException>(() =>
            {
                using var _ = CreateCoordinator(ungatedFixture.CreateDecoder());
            }),
            "coordinator rejects fixture-only ungated callback contract");

        foreach (var kind in CaptureKinds)
        {
            var fixture = new TranslatedCallCaptureFixture();
            var contract = CreateExactContract(fixture, supported);
            CorruptIdentity(fixture, kind);
            Equal(
                true,
                Throws<InvalidOperationException>(() =>
                {
                    using var _ = CreateCoordinator(contract);
                }),
                $"coordinator rejects stale initial {kind} identity");
        }

        var validFixture = new TranslatedCallCaptureFixture();
        var validContract = CreateExactContract(validFixture, supported);
        Equal(
            false,
            validContract.TryValidateCaptureIdentity(
                Steam2026MenuCallbackKind.WidgetConstructor,
                out _),
            "contract rejects widget constructor as a capture identity");
    }

    private static void CapturesEveryGuestPayloadBeforeOriginalAndPublishesAfter(
        Steam2026FingerprintResult supported)
    {
        var fixture = new TranslatedCallCaptureFixture();
        var contract = CreateExactContract(fixture, supported);
        var order = new List<string>();
        var snapshots = new List<TranslatedMenuIngressSnapshot>();
        var currentKind = Steam2026MenuCallbackKind.CursorB;
        var originalCalls = CaptureKinds.ToDictionary(kind => kind, _ => 0);

        TranslatedMenuCallbackOriginal OriginalFor(Steam2026MenuCallbackKind kind) => () =>
        {
            order.Add($"original:{kind}");
            originalCalls[kind]++;
            MutateCapturedPayload(fixture, kind);
        };

        using var coordinator = CreateCoordinator(
            contract,
            cursorBOriginal: OriginalFor(Steam2026MenuCallbackKind.CursorB),
            cursorAOriginal: OriginalFor(Steam2026MenuCallbackKind.CursorA),
            activeWidgetOriginal: OriginalFor(Steam2026MenuCallbackKind.ActiveWidgetUpdate),
            encodedTextBOriginal: OriginalFor(Steam2026MenuCallbackKind.EncodedTextB),
            encodedTextAOriginal: OriginalFor(Steam2026MenuCallbackKind.EncodedTextA),
            asciiRendererOriginal: OriginalFor(Steam2026MenuCallbackKind.AsciiRenderer),
            widgetNormalizer: address =>
            {
                order.Add("normalize:ActiveWidgetUpdate");
                return (true, CreateKnownWidget(address));
            },
            clock: () =>
            {
                order.Add($"clock:{currentKind}");
                return Timestamp.AddTicks(snapshots.Count);
            },
            captureSink: snapshot =>
            {
                order.Add($"sink:{snapshot.CallbackKind}");
                snapshots.Add(snapshot);
            });

        foreach (var kind in CaptureKinds)
        {
            currentKind = kind;
            PrepareValidCall(fixture, kind);
            Invoke(coordinator, kind);
        }

        foreach (var kind in CaptureKinds)
        {
            Equal(1, originalCalls[kind], $"{kind} original called exactly once");
            var originalIndex = order.IndexOf($"original:{kind}");
            var clockIndex = order.IndexOf($"clock:{kind}");
            var sinkIndex = order.IndexOf($"sink:{kind}");
            Equal(true, originalIndex >= 0 && originalIndex < clockIndex && clockIndex < sinkIndex, $"{kind} original/clock/sink ordering");
        }

        Equal(
            true,
            order.IndexOf("normalize:ActiveWidgetUpdate") < order.IndexOf("original:ActiveWidgetUpdate"),
            "active widget normalization precedes original");
        Equal(6, snapshots.Count, "all six capture-eligible callbacks publish one snapshot");
        Equal(
            new TranslatedMenuCursorObservation(Steam2026MenuCallbackKind.CursorB, -12, 34, 0x55667788),
            snapshots.Single(snapshot => snapshot.CallbackKind == Steam2026MenuCallbackKind.CursorB).Cursor,
            "cursor B preserves pre-original guest arguments");
        Equal(
            new TranslatedMenuCursorObservation(Steam2026MenuCallbackKind.CursorA, -12, 34, 0x55667788),
            snapshots.Single(snapshot => snapshot.CallbackKind == Steam2026MenuCallbackKind.CursorA).Cursor,
            "cursor A preserves pre-original guest arguments");
        foreach (var kind in new[] { Steam2026MenuCallbackKind.EncodedTextB, Steam2026MenuCallbackKind.EncodedTextA })
        {
            var text = snapshots.Single(snapshot => snapshot.CallbackKind == kind).Text;
            Equal(new TranslatedMenuTextObservation(kind, "Hi", -4, 77, 0x22, 0x3344), text, $"{kind} preserves decoded pre-original text");
        }

        Equal(
            new TranslatedMenuTextObservation(Steam2026MenuCallbackKind.AsciiRenderer, "Menu", 12, -9, 0x4455, 0x6677),
            snapshots.Single(snapshot => snapshot.CallbackKind == Steam2026MenuCallbackKind.AsciiRenderer).Text,
            "ASCII renderer preserves decoded pre-original text");
        SequenceEqual(
            CaptureKinds,
            snapshots.OrderBy(snapshot => snapshot.Sequence).Select(snapshot => snapshot.CallbackKind),
            "snapshot sequence follows serialized completion order");
        Equal(true, snapshots.All(snapshot => snapshot.TimestampUtc.Kind == DateTimeKind.Utc), "all menu ingress timestamps are UTC");
    }

    private static void ActiveWidgetUsesCheckedPointerFreeNormalization(
        Steam2026FingerprintResult supported)
    {
        var fixture = new TranslatedCallCaptureFixture();
        var normalizedAddresses = new List<uint>();
        var snapshots = new List<TranslatedMenuIngressSnapshot>();
        var originalCalls = 0;
        using var coordinator = CreateCoordinator(
            CreateExactContract(fixture, supported),
            activeWidgetOriginal: () => originalCalls++,
            widgetNormalizer: address =>
            {
                normalizedAddresses.Add(address);
                return (true, CreateKnownWidget(address));
            },
            captureSink: snapshots.Add);
        PrepareValidCall(fixture, Steam2026MenuCallbackKind.ActiveWidgetUpdate);
        coordinator.OnActiveWidgetUpdate();

        Equal(1, originalCalls, "active widget original once");
        SequenceEqual([KnownWidgetAddress], normalizedAddresses, "normalizer receives exact captured guest address");
        var widget = snapshots.Single().ActiveWidget
                     ?? throw new InvalidOperationException("Active widget payload missing.");
        var identity = typeof(TranslatedMenuWidgetIngressObservation)
            .GetProperty("WidgetIdentity")
            ?? throw new InvalidOperationException(
                "Translated widget ingress does not retain its verified native identity.");
        Equal(
            KnownWidgetAddress,
            (uint)(identity.GetValue(widget)
                ?? throw new InvalidOperationException("Verified widget identity is absent.")),
            "widget sink retains the exact checked guest identity");
        Equal("Item/Main list", widget.VerifiedName, "widget name must be catalog verified");
        Equal(MenuWidgetKind.RootMainMenu, widget.Kind, "widget kind must be catalog verified");
        Equal(0, widget.First, "widget first copied");
        Equal(1, widget.Cursor, "widget cursor copied");
        Equal(2, widget.Columns, "widget columns copied");
        Equal(3, widget.Rows, "widget rows copied");

        foreach (var property in typeof(TranslatedMenuWidgetIngressObservation).GetProperties())
        {
            Equal(false, property.Name.Contains("Address", StringComparison.OrdinalIgnoreCase), $"widget sink excludes address property {property.Name}");
            Equal(false, property.Name.Contains("Pointer", StringComparison.OrdinalIgnoreCase), $"widget sink excludes pointer property {property.Name}");
            Equal(false, property.PropertyType == typeof(IntPtr) || property.PropertyType == typeof(UIntPtr), $"widget sink excludes host pointer type {property.Name}");
        }

        AssertInvalidWidgetNormalizationSuppressesSink(
            supported,
            _ => (false, default),
            "failed widget normalizer");
        AssertInvalidWidgetNormalizationSuppressesSink(
            supported,
            _ => (true, CreateKnownWidget(0x00DC1188)),
            "mismatched normalized widget address");
        AssertInvalidWidgetNormalizationSuppressesSink(
            supported,
            _ => (true, new ActiveMenuWidgetSnapshot(
                KnownWidgetAddress,
                "Item/Main list",
                MenuWidgetKind.RootMainMenu,
                0,
                0,
                0,
                3,
                0,
                0,
                0)),
            "invalid normalized widget structure");
        AssertInvalidWidgetNormalizationSuppressesSink(
            supported,
            _ => (true, new ActiveMenuWidgetSnapshot(
                KnownWidgetAddress,
                "invented widget",
                MenuWidgetKind.RootMainMenu,
                0,
                1,
                2,
                3,
                0,
                0,
                0)),
            "unverified normalized widget identity");
    }

    private static void CaptureIdentityClockAndSinkFailuresAreContained(
        Steam2026FingerprintResult supported)
    {
        var captureFixture = new TranslatedCallCaptureFixture();
        var captureOriginals = 0;
        var captureSinks = 0;
        using (var coordinator = CreateCoordinator(
                   CreateExactContract(captureFixture, supported),
                   encodedTextAOriginal: () => captureOriginals++,
                   captureSink: _ => captureSinks++))
        {
            captureFixture.WriteCall(0x00182000, [1, 2, 0x00333000, 3, 4]);
            coordinator.OnEncodedTextA();
        }

        Equal(1, captureOriginals, "failed capture still calls original once");
        Equal(0, captureSinks, "failed capture publishes nothing");

        var staleFixture = new TranslatedCallCaptureFixture();
        var staleOriginals = 0;
        var staleClocks = 0;
        using (var coordinator = CreateCoordinator(
                   CreateExactContract(staleFixture, supported),
                   cursorBOriginal: () => staleOriginals++,
                   clock: () =>
                   {
                       staleClocks++;
                       return Timestamp;
                   }))
        {
            CorruptIdentity(staleFixture, Steam2026MenuCallbackKind.CursorB);
            PrepareValidCall(staleFixture, Steam2026MenuCallbackKind.CursorB);
            coordinator.OnCursorB();
        }

        Equal(1, staleOriginals, "stale entry identity still calls original once");
        Equal(0, staleClocks, "stale entry identity suppresses clock and publication");

        var postFixture = new TranslatedCallCaptureFixture();
        var postSinks = 0;
        using (var coordinator = CreateCoordinator(
                   CreateExactContract(postFixture, supported),
                   encodedTextBOriginal: () => CorruptIdentity(postFixture, Steam2026MenuCallbackKind.EncodedTextB),
                   captureSink: _ => postSinks++))
        {
            PrepareValidCall(postFixture, Steam2026MenuCallbackKind.EncodedTextB);
            coordinator.OnEncodedTextB();
        }

        Equal(0, postSinks, "identity stale after original suppresses publication");

        var clockFixture = new TranslatedCallCaptureFixture();
        var clockOriginals = 0;
        var clockSinks = 0;
        using (var coordinator = CreateCoordinator(
                   CreateExactContract(clockFixture, supported),
                   cursorAOriginal: () => clockOriginals++,
                   clock: () => throw new InvalidOperationException("clock failed"),
                   captureSink: _ => clockSinks++))
        {
            PrepareValidCall(clockFixture, Steam2026MenuCallbackKind.CursorA);
            coordinator.OnCursorA();
        }

        Equal(1, clockOriginals, "throwing clock preserves original count");
        Equal(0, clockSinks, "throwing clock suppresses publication");

        var localClockFixture = new TranslatedCallCaptureFixture();
        var localClockSinks = 0;
        using (var coordinator = CreateCoordinator(
                   CreateExactContract(localClockFixture, supported),
                   clock: () => DateTime.SpecifyKind(Timestamp, DateTimeKind.Local),
                   captureSink: _ => localClockSinks++))
        {
            PrepareValidCall(localClockFixture, Steam2026MenuCallbackKind.CursorA);
            coordinator.OnCursorA();
        }

        Equal(0, localClockSinks, "non-UTC clock value suppresses publication");

        var sinkFixture = new TranslatedCallCaptureFixture();
        var sinkOriginals = 0;
        var sinkAttempts = 0;
        using (var coordinator = CreateCoordinator(
                   CreateExactContract(sinkFixture, supported),
                   asciiRendererOriginal: () => sinkOriginals++,
                   captureSink: _ =>
                   {
                       sinkAttempts++;
                       throw new InvalidOperationException("sink failed");
                   }))
        {
            PrepareValidCall(sinkFixture, Steam2026MenuCallbackKind.AsciiRenderer);
            coordinator.OnAsciiRenderer();
            Equal(true, coordinator.IsFatallyDegraded, "throwing sink permanently degrades ingress");

            PrepareValidCall(sinkFixture, Steam2026MenuCallbackKind.AsciiRenderer);
            coordinator.OnAsciiRenderer();
        }

        Equal(2, sinkOriginals, "throwing sink leaves later originals callable");
        Equal(1, sinkAttempts, "degraded ingress never retries publication");

        var memoryFixture = new TranslatedCallCaptureFixture();
        var throwingMemory = new SwitchableThrowingNativeMemoryReader(memoryFixture.Native);
        var throwingContract = CreateExactContract(memoryFixture, supported, throwingMemory);
        var memoryOriginals = 0;
        var memorySinks = 0;
        using (var coordinator = CreateCoordinator(
                   throwingContract,
                   cursorAOriginal: () => memoryOriginals++,
                   captureSink: _ => memorySinks++))
        {
            PrepareValidCall(memoryFixture, Steam2026MenuCallbackKind.CursorA);
            throwingMemory.ThrowReads = true;
            coordinator.OnCursorA();
        }

        Equal(1, memoryOriginals, "throwing identity memory still calls original once");
        Equal(0, memorySinks, "throwing identity memory suppresses publication");

        var widgetFixture = new TranslatedCallCaptureFixture();
        var widgetOriginals = 0;
        using (var coordinator = CreateCoordinator(
                   CreateExactContract(widgetFixture, supported),
                   activeWidgetOriginal: () => widgetOriginals++,
                   widgetNormalizer: _ => throw new InvalidOperationException("normalizer failed")))
        {
            PrepareValidCall(widgetFixture, Steam2026MenuCallbackKind.ActiveWidgetUpdate);
            coordinator.OnActiveWidgetUpdate();
        }

        Equal(1, widgetOriginals, "throwing widget normalizer still calls original once");
    }

    private static void OriginalFailuresAreContainedAndPermanentlyDegradeIngress(
        Steam2026FingerprintResult supported)
    {
        foreach (var kind in CaptureKinds)
        {
            var fixture = new TranslatedCallCaptureFixture();
            var calls = 0;
            var shouldThrow = true;
            var sinks = 0;
            TranslatedMenuCallbackOriginal selectedOriginal = () =>
            {
                calls++;
                if (shouldThrow)
                {
                    throw new InvalidOperationException($"{kind} original failed");
                }
            };
            using var coordinator = CreateCoordinator(
                CreateExactContract(fixture, supported),
                cursorBOriginal: kind == Steam2026MenuCallbackKind.CursorB ? selectedOriginal : null,
                cursorAOriginal: kind == Steam2026MenuCallbackKind.CursorA ? selectedOriginal : null,
                activeWidgetOriginal: kind == Steam2026MenuCallbackKind.ActiveWidgetUpdate ? selectedOriginal : null,
                encodedTextBOriginal: kind == Steam2026MenuCallbackKind.EncodedTextB ? selectedOriginal : null,
                encodedTextAOriginal: kind == Steam2026MenuCallbackKind.EncodedTextA ? selectedOriginal : null,
                asciiRendererOriginal: kind == Steam2026MenuCallbackKind.AsciiRenderer ? selectedOriginal : null,
                captureSink: _ => sinks++);

            PrepareValidCall(fixture, kind);
            Invoke(coordinator, kind);
            Equal(1, calls, $"throwing {kind} original called exactly once");
            Equal(0, sinks, $"throwing {kind} original publishes nothing");
            Equal(true, coordinator.IsFatallyDegraded, $"throwing {kind} permanently degrades ingress");

            shouldThrow = false;
            PrepareValidCall(fixture, kind);
            Invoke(coordinator, kind);
            Equal(2, calls, $"{kind} original remains callable after degradation");
            Equal(0, sinks, $"degraded {kind} ingress publishes no later capture");
        }
    }

    private static void QueueOverflowPermanentlyDegradesIngress(
        Steam2026FingerprintResult supported)
    {
        var fixture = new TranslatedCallCaptureFixture();
        var originals = 0;
        var queue = new BoundedNativeIngressQueue<TranslatedMenuIngressSnapshot>(1);
        using var coordinator = CreateCoordinator(
            CreateExactContract(fixture, supported),
            cursorAOriginal: () => originals++,
            captureQueue: queue);

        PrepareValidCall(fixture, Steam2026MenuCallbackKind.CursorA);
        coordinator.OnCursorA();
        Equal(false, coordinator.IsFatallyDegraded, "first queued menu capture remains healthy");

        PrepareValidCall(fixture, Steam2026MenuCallbackKind.CursorA);
        coordinator.OnCursorA();
        Equal(true, coordinator.IsFatallyDegraded, "menu queue overflow permanently degrades ingress");

        PrepareValidCall(fixture, Steam2026MenuCallbackKind.CursorA);
        coordinator.OnCursorA();
        Equal(3, originals, "menu originals remain callable after queue overflow");
        Equal(true, queue.TryDequeue(out _), "first menu capture remains queued after overflow");
        Equal(false, queue.TryDequeue(out _), "overflowed and degraded menu captures are not queued");
    }

    private static void CommittedSnapshotCanFinishPublicationAfterStop(
        Steam2026FingerprintResult supported)
    {
        var fixture = new TranslatedCallCaptureFixture();
        var originals = 0;
        var snapshots = new List<TranslatedMenuIngressSnapshot>();
        Steam2026TranslatedMenuDetourIngressCoordinator coordinator = null!;
        var queue = new DelegatingNativeIngressQueue<TranslatedMenuIngressSnapshot>(snapshot =>
        {
            coordinator.Stop();
            snapshots.Add(snapshot);
            return true;
        });
        coordinator = CreateCoordinator(
            CreateExactContract(fixture, supported),
            cursorAOriginal: () => originals++,
            captureQueue: queue);

        PrepareValidCall(fixture, Steam2026MenuCallbackKind.CursorA);
        coordinator.OnCursorA();
        Equal(1, snapshots.Count, "snapshot committed before Stop completes its publication attempt");

        PrepareValidCall(fixture, Steam2026MenuCallbackKind.CursorA);
        coordinator.OnCursorA();
        Equal(2, originals, "original remains callable after Stop at committed publication boundary");
        Equal(1, snapshots.Count, "Stop suppresses observations that have not committed");
        coordinator.Dispose();
    }

    private static void ConcurrentCallbacksDoNotSerializeOriginalInvocations(
        Steam2026FingerprintResult supported)
    {
        var fixture = new TranslatedCallCaptureFixture();
        PrepareValidCall(fixture, Steam2026MenuCallbackKind.CursorA);
        var calls = 0;
        var active = 0;
        var maximumActive = 0;
        using var firstEntered = new ManualResetEventSlim();
        using var secondEntered = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        var snapshots = new ConcurrentBag<TranslatedMenuIngressSnapshot>();
        using var coordinator = CreateCoordinator(
            CreateExactContract(fixture, supported),
            cursorAOriginal: () =>
            {
                var call = Interlocked.Increment(ref calls);
                var current = Interlocked.Increment(ref active);
                UpdateMaximum(ref maximumActive, current);
                if (call == 1)
                {
                    firstEntered.Set();
                    if (!releaseFirst.Wait(TimeSpan.FromSeconds(5)))
                    {
                        throw new TimeoutException("First translated original was not released.");
                    }
                }
                else
                {
                    secondEntered.Set();
                }

                Interlocked.Decrement(ref active);
            },
            captureSink: snapshot => snapshots.Add(snapshot));

        var first = Task.Factory.StartNew(
            coordinator.OnCursorA,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Equal(true, firstEntered.Wait(TimeSpan.FromSeconds(5)), "first translated original entered");
        var second = Task.Factory.StartNew(
            coordinator.OnCursorA,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        var originalsOverlapped = secondEntered.Wait(TimeSpan.FromSeconds(1));
        var rejectedOverlapReturned = second.Wait(TimeSpan.FromSeconds(1));
        releaseFirst.Set();
        Equal(true, Task.WaitAll([first, second], TimeSpan.FromSeconds(5)), "translated callbacks complete");

        Equal(true, originalsOverlapped, "translated native originals are never serialized by ingress");
        Equal(true, rejectedOverlapReturned, "overlapping translated callback returns without waiting for observation ownership");
        Equal(2, calls, "concurrent callbacks call every original exactly once");
        Equal(2, maximumActive, "translated originals overlap while observations remain coherent");
        Equal(0, snapshots.Count, "overlap invalidates both translated observations");
    }

    private static void ReentrantCallbacksKeepCapturesIsolatedAndFailClosed(
        Steam2026FingerprintResult supported)
    {
        var successFixture = new TranslatedCallCaptureFixture();
        var successSnapshots = new List<TranslatedMenuIngressSnapshot>();
        var outerCalls = 0;
        var innerCalls = 0;
        Steam2026TranslatedMenuDetourIngressCoordinator successCoordinator = null!;
        successCoordinator = CreateCoordinator(
            CreateExactContract(successFixture, supported),
            cursorAOriginal: () =>
            {
                outerCalls++;
                PrepareValidCall(successFixture, Steam2026MenuCallbackKind.EncodedTextA);
                successCoordinator.OnEncodedTextA();
            },
            encodedTextAOriginal: () => innerCalls++,
            captureSink: successSnapshots.Add);
        using (successCoordinator)
        {
            PrepareValidCall(successFixture, Steam2026MenuCallbackKind.CursorA);
            successCoordinator.OnCursorA();
        }

        Equal(1, outerCalls, "reentrant outer original once");
        Equal(1, innerCalls, "reentrant inner original once");
        Equal(0, successSnapshots.Count, "reentrant overlap invalidates both translated observations");

        var failureFixture = new TranslatedCallCaptureFixture();
        var failureSnapshots = new List<TranslatedMenuIngressSnapshot>();
        outerCalls = 0;
        innerCalls = 0;
        Steam2026TranslatedMenuDetourIngressCoordinator failureCoordinator = null!;
        failureCoordinator = CreateCoordinator(
            CreateExactContract(failureFixture, supported),
            cursorAOriginal: () =>
            {
                outerCalls++;
                failureFixture.WriteCall(0x001B0000, [1, 2, 0x00333000, 3, 4]);
                failureCoordinator.OnEncodedTextA();
            },
            encodedTextAOriginal: () => innerCalls++,
            captureSink: failureSnapshots.Add);
        using (failureCoordinator)
        {
            PrepareValidCall(failureFixture, Steam2026MenuCallbackKind.CursorA);
            failureCoordinator.OnCursorA();
        }

        Equal(1, outerCalls, "failed reentrant outer original once");
        Equal(1, innerCalls, "failed reentrant inner original once");
        Equal(0, failureSnapshots.Count, "nested capture failure invalidates pending outer delivery");
    }

    private static void StopDoesNotWaitForInFlightObservation(
        Steam2026FingerprintResult supported)
    {
        var fixture = new TranslatedCallCaptureFixture();
        PrepareValidCall(fixture, Steam2026MenuCallbackKind.CursorA);
        using var clockEntered = new ManualResetEventSlim();
        using var releaseClock = new ManualResetEventSlim();
        var captures = 0;
        var coordinator = CreateCoordinator(
            CreateExactContract(fixture, supported),
            clock: () =>
            {
                clockEntered.Set();
                if (!releaseClock.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Translated ingress clock was not released.");
                }

                return Timestamp;
            },
            captureSink: _ => captures++);

        var callback = Task.Factory.StartNew(
            coordinator.OnCursorA,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Equal(true, clockEntered.Wait(TimeSpan.FromSeconds(5)), "translated observation reached post-original clock");
        var stop = Task.Factory.StartNew(
            coordinator.Stop,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        var stopReturnedWithoutWaiting = stop.Wait(TimeSpan.FromSeconds(1));
        releaseClock.Set();
        Equal(true, Task.WaitAll([callback, stop], TimeSpan.FromSeconds(5)), "translated callback and stop complete");

        Equal(true, stopReturnedWithoutWaiting, "translated Stop never waits for in-flight observation");
        Equal(0, captures, "Stop invalidates the in-flight translated observation");
        coordinator.Dispose();
    }

    private static void StopAndDisposeAreIdempotentWhileOriginalsRemainCallable(
        Steam2026FingerprintResult supported)
    {
        var fixture = new TranslatedCallCaptureFixture();
        var calls = CaptureKinds.ToDictionary(kind => kind, _ => 0);
        var normalizers = 0;
        var clocks = 0;
        var sinks = 0;
        TranslatedMenuCallbackOriginal OriginalFor(Steam2026MenuCallbackKind kind) => () => calls[kind]++;
        var coordinator = CreateCoordinator(
            CreateExactContract(fixture, supported),
            cursorBOriginal: OriginalFor(Steam2026MenuCallbackKind.CursorB),
            cursorAOriginal: OriginalFor(Steam2026MenuCallbackKind.CursorA),
            activeWidgetOriginal: OriginalFor(Steam2026MenuCallbackKind.ActiveWidgetUpdate),
            encodedTextBOriginal: OriginalFor(Steam2026MenuCallbackKind.EncodedTextB),
            encodedTextAOriginal: OriginalFor(Steam2026MenuCallbackKind.EncodedTextA),
            asciiRendererOriginal: OriginalFor(Steam2026MenuCallbackKind.AsciiRenderer),
            widgetNormalizer: address =>
            {
                normalizers++;
                return (true, CreateKnownWidget(address));
            },
            clock: () =>
            {
                clocks++;
                return Timestamp;
            },
            captureSink: _ => sinks++);

        coordinator.Stop();
        coordinator.Stop();
        coordinator.Dispose();
        coordinator.Dispose();
        foreach (var kind in CaptureKinds)
        {
            PrepareValidCall(fixture, kind);
            Invoke(coordinator, kind);
            Equal(1, calls[kind], $"stopped {kind} original remains exactly-once callable");
        }

        Equal(0, normalizers, "stopped ingress performs no widget normalization");
        Equal(0, clocks, "stopped ingress performs no clock reads");
        Equal(0, sinks, "stopped ingress publishes nothing");
    }

    private static void IngressHasNoBackendCapabilityInstallationOrSpeechSurface(
        Steam2026FingerprintResult supported)
    {
        var ingressType = typeof(Steam2026TranslatedMenuDetourIngressCoordinator);
        Equal(false, typeof(IFf7RuntimeBackend).IsAssignableFrom(ingressType), "menu ingress is not a backend");
        Equal(false, typeof(IRuntimeEventSink).IsAssignableFrom(ingressType), "menu ingress is not a runtime event sink");

        var prototypeRoot = FindPrototypeRoot();
        var projectRoot = Path.Combine(prototypeRoot, "reloaded", "Ff7.Accessibility.Steam2026X64");
        var sourcePath = Path.Combine(
            projectRoot,
            "Runtime",
            "Menus",
            "Steam2026TranslatedMenuDetourIngressCoordinator.cs");
        var source = File.ReadAllText(sourcePath);
        foreach (var forbidden in new[]
                 {
                     "IHook<",
                     "CreateHook",
                     "IRuntimeEventSink",
                     "RuntimeCapability",
                     ".Publish(",
                     "captureSink",
                     "Action<",
                     "lock (",
                     "Monitor.",
                     "Process.Start",
                     "Speak"
                 })
        {
            Equal(false, source.Contains(forbidden, StringComparison.Ordinal), $"translated menu ingress excludes {forbidden}");
        }
        Equal(
            true,
            source.Contains("Reloaded.Hooks.Definitions.X64.Function", StringComparison.Ordinal),
            "translated menu callback declares the Microsoft x64 ABI");

        var backendSource = File.ReadAllText(Path.Combine(projectRoot, "Steam2026X64RuntimeBackend.cs"));
        Equal(false, backendSource.Contains(nameof(Steam2026TranslatedMenuDetourIngressCoordinator), StringComparison.Ordinal), "backend has no translated menu ingress integration");
        using var backend = new Steam2026X64RuntimeBackend(supported);
        Equal(RuntimeCapability.None, backend.ValidateCapabilities().Available, "translated menu ingress enables no capability");

        var snapshotType = typeof(TranslatedMenuIngressSnapshot);
        Equal(true, snapshotType.IsSealed, "translated menu ingress snapshot is sealed");
        foreach (var property in snapshotType.GetProperties()
                     .Concat(typeof(TranslatedMenuWidgetIngressObservation).GetProperties()))
        {
            Equal(false, property.Name.Contains("Address", StringComparison.OrdinalIgnoreCase), $"sink surface excludes address property {property.Name}");
            Equal(false, property.Name.Contains("Pointer", StringComparison.OrdinalIgnoreCase), $"sink surface excludes pointer property {property.Name}");
            Equal(false, property.PropertyType == typeof(IntPtr) || property.PropertyType == typeof(UIntPtr), $"sink surface excludes pointer type {property.Name}");
        }
    }

    private static void AssertInvalidWidgetNormalizationSuppressesSink(
        Steam2026FingerprintResult supported,
        Func<uint, (bool Success, ActiveMenuWidgetSnapshot Snapshot)> normalizer,
        string label)
    {
        var fixture = new TranslatedCallCaptureFixture();
        var originalCalls = 0;
        var sinks = 0;
        using var coordinator = CreateCoordinator(
            CreateExactContract(fixture, supported),
            activeWidgetOriginal: () => originalCalls++,
            widgetNormalizer: normalizer,
            captureSink: _ => sinks++);
        PrepareValidCall(fixture, Steam2026MenuCallbackKind.ActiveWidgetUpdate);
        coordinator.OnActiveWidgetUpdate();
        Equal(1, originalCalls, $"{label} still calls original once");
        Equal(0, sinks, $"{label} publishes nothing");
    }

    private static Steam2026MenuCallbackContract CreateExactContract(
        TranslatedCallCaptureFixture fixture,
        Steam2026FingerprintResult fingerprint,
        INativeMemoryReader? memory = null) =>
        new(
            fingerprint,
            TranslatedCallCaptureFixture.ModuleBase,
            ModuleImageSize,
            memory ?? fixture.Native);

    private static bool ProbeActiveLeaseHealth(
        Steam2026MenuCallbackContract contract,
        long monotonicMilliseconds)
    {
        var method = typeof(Steam2026MenuCallbackContract).GetMethod(
            "IsActiveHookLeaseHealthy",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Equal(true, method is not null, "menu worker lease-health probe exists");
        return (bool)(method!.Invoke(contract, [monotonicMilliseconds])
                      ?? throw new InvalidOperationException(
                          "Menu worker lease-health probe returned no result."));
    }

    private static Steam2026TranslatedMenuDetourIngressCoordinator CreateCoordinator(
        Steam2026MenuCallbackContract contract,
        TranslatedMenuCallbackOriginal? cursorBOriginal = null,
        TranslatedMenuCallbackOriginal? cursorAOriginal = null,
        TranslatedMenuCallbackOriginal? activeWidgetOriginal = null,
        TranslatedMenuCallbackOriginal? encodedTextBOriginal = null,
        TranslatedMenuCallbackOriginal? encodedTextAOriginal = null,
        TranslatedMenuCallbackOriginal? asciiRendererOriginal = null,
        Func<uint, (bool Success, ActiveMenuWidgetSnapshot Snapshot)>? widgetNormalizer = null,
        Func<DateTime>? clock = null,
        Action<TranslatedMenuIngressSnapshot>? captureSink = null,
        INativeIngressQueue<TranslatedMenuIngressSnapshot>? captureQueue = null) =>
        new(
            contract,
            cursorBOriginal ?? (() => { }),
            cursorAOriginal ?? (() => { }),
            activeWidgetOriginal ?? (() => { }),
            encodedTextBOriginal ?? (() => { }),
            encodedTextAOriginal ?? (() => { }),
            asciiRendererOriginal ?? (() => { }),
            widgetNormalizer ?? (address => (true, CreateKnownWidget(address))),
            clock ?? (() => Timestamp),
            captureQueue ?? new DelegatingNativeIngressQueue<TranslatedMenuIngressSnapshot>(
                captureSink ?? (_ => { })));

    private static ActiveMenuWidgetSnapshot CreateKnownWidget(uint address)
    {
        if (!MenuWidgetCatalog.TryResolve(address, out var descriptor))
        {
            return new ActiveMenuWidgetSnapshot(
                address,
                $"Widget 0x{address:X8}",
                MenuWidgetKind.Generic,
                0,
                1,
                2,
                3,
                0,
                0,
                0);
        }

        return new ActiveMenuWidgetSnapshot(
            address,
            descriptor.Name,
            descriptor.Kind,
            0,
            1,
            2,
            3,
            4,
            5,
            6);
    }

    private static void PrepareValidCall(
        TranslatedCallCaptureFixture fixture,
        Steam2026MenuCallbackKind kind)
    {
        switch (kind)
        {
            case Steam2026MenuCallbackKind.CursorA:
            case Steam2026MenuCallbackKind.CursorB:
                fixture.WriteCall(0x00190000, [unchecked((uint)-12), 34, 0x55667788]);
                break;
            case Steam2026MenuCallbackKind.ActiveWidgetUpdate:
                fixture.WriteCall(0x00191000, [KnownWidgetAddress]);
                break;
            case Steam2026MenuCallbackKind.EncodedTextA:
            case Steam2026MenuCallbackKind.EncodedTextB:
                fixture.WriteGuest(0x00290000, [0x28, 0x49, 0xFF]);
                fixture.WriteCall(0x00192000, [unchecked((uint)-4), 77, 0x00290000, 0x22, 0x3344]);
                break;
            case Steam2026MenuCallbackKind.AsciiRenderer:
                fixture.WriteGuest(0x00291000, [(byte)'M', (byte)'e', (byte)'n', (byte)'u', 0]);
                fixture.WriteCall(0x00193000, [0x00291000, 12, unchecked((uint)-9), 0x4455, 0x6677]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static void MutateCapturedPayload(
        TranslatedCallCaptureFixture fixture,
        Steam2026MenuCallbackKind kind)
    {
        switch (kind)
        {
            case Steam2026MenuCallbackKind.CursorA:
            case Steam2026MenuCallbackKind.CursorB:
                fixture.WriteCall(0x001A0000, [99, 98, 97]);
                break;
            case Steam2026MenuCallbackKind.ActiveWidgetUpdate:
                fixture.WriteCall(0x001A1000, [0x00DC1188]);
                break;
            case Steam2026MenuCallbackKind.EncodedTextA:
            case Steam2026MenuCallbackKind.EncodedTextB:
                fixture.WriteGuest(0x00290000, [0x22, 0x79, 0x65, 0xFF]);
                break;
            case Steam2026MenuCallbackKind.AsciiRenderer:
                fixture.WriteGuest(0x00291000, [(byte)'B', (byte)'y', (byte)'e', 0]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static void Invoke(
        Steam2026TranslatedMenuDetourIngressCoordinator coordinator,
        Steam2026MenuCallbackKind kind)
    {
        switch (kind)
        {
            case Steam2026MenuCallbackKind.CursorB:
                coordinator.OnCursorB();
                break;
            case Steam2026MenuCallbackKind.CursorA:
                coordinator.OnCursorA();
                break;
            case Steam2026MenuCallbackKind.ActiveWidgetUpdate:
                coordinator.OnActiveWidgetUpdate();
                break;
            case Steam2026MenuCallbackKind.EncodedTextB:
                coordinator.OnEncodedTextB();
                break;
            case Steam2026MenuCallbackKind.EncodedTextA:
                coordinator.OnEncodedTextA();
                break;
            case Steam2026MenuCallbackKind.AsciiRenderer:
                coordinator.OnAsciiRenderer();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static void CorruptIdentity(
        TranslatedCallCaptureFixture fixture,
        Steam2026MenuCallbackKind kind)
    {
        var metadata = Steam2026MenuCallbackCatalog.GetMetadata(kind);
        fixture.Native.Write(
            TranslatedCallCaptureFixture.ModuleBase + metadata.FunctionMap.HostRva,
            [0x90]);
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        while (true)
        {
            var observed = Volatile.Read(ref maximum);
            if (candidate <= observed || Interlocked.CompareExchange(ref maximum, candidate, observed) == observed)
            {
                return;
            }
        }
    }

    private static string FindPrototypeRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "analysis", "dual_runtime")) &&
                Directory.Exists(Path.Combine(current.FullName, "reloaded")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate accessibility_prototype root.");
    }

    private static bool Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string label)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected [{string.Join(',', expected)}], got [{string.Join(',', actual)}].");
        }
    }
}
