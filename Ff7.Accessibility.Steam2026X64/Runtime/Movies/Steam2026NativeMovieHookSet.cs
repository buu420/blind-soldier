using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Movies;

/// <summary>
/// Owns the four exact-fingerprint native movie detours used by the Steam
/// 2026 research session. Native callbacks only copy bounded observations to
/// the queue; lifecycle dispatch and audio remain outside the detours.
/// </summary>
internal sealed class Steam2026NativeMovieHookSet : IDisposable
{
    private const int CaptureQueueCapacity = 128;

    private readonly BoundedNativeIngressQueue<NativeMovieIngressSnapshot> captureQueue =
        new(CaptureQueueCapacity);
    private readonly NativeMoviePrepareOriginal prepareDetour;
    private readonly NativeMovieReleaseOriginal releaseDetour;
    private readonly NativeMovieStartOriginal startDetour;
    private readonly NativeMovieStopOriginal stopDetour;

    private IHook<NativeMoviePrepareOriginal>? prepareHook;
    private IHook<NativeMovieReleaseOriginal>? releaseHook;
    private IHook<NativeMovieStartOriginal>? startHook;
    private IHook<NativeMovieStopOriginal>? stopHook;
    private NativeMovieCallbackContract? contract;
    private NativeMovieDetourIngressCoordinator? coordinator;
    private int disposed;

    private Steam2026NativeMovieHookSet(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory,
        IReloadedHooks hooks,
        string expectedOpeningMoviePath)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedOpeningMoviePath);

        prepareDetour = OnPrepare;
        releaseDetour = OnRelease;
        startDetour = OnStart;
        stopDetour = OnStop;

        try
        {
            contract = new NativeMovieCallbackContract(
                fingerprint,
                moduleBase,
                moduleImageSize,
                memory);
            var identities = ValidateAllTargets(contract);

            prepareHook = CreateHook(
                hooks,
                prepareDetour,
                identities[NativeMovieCallbackKind.Prepare].Address);
            releaseHook = CreateHook(
                hooks,
                releaseDetour,
                identities[NativeMovieCallbackKind.Release].Address);
            startHook = CreateHook(
                hooks,
                startDetour,
                identities[NativeMovieCallbackKind.Start].Address);
            stopHook = CreateHook(
                hooks,
                stopDetour,
                identities[NativeMovieCallbackKind.Stop].Address);

            var stateReader = new Steam2026NativeMovieStateReader(
                fingerprint,
                moduleBase,
                moduleImageSize,
                memory);
            var observer = new OpeningMovieLifecycleObserver(
                expectedOpeningMoviePath,
                contract);
            var openingPathIdentity = new Steam2026OpeningMoviePathIdentity(
                expectedOpeningMoviePath);
            coordinator = new NativeMovieDetourIngressCoordinator(
                contract,
                observer,
                prepareHook.OriginalFunction,
                releaseHook.OriginalFunction,
                startHook.OriginalFunction,
                stopHook.OriginalFunction,
                () => stateReader.TryReadCanonicalPath(out var path)
                      && openingPathIdentity.TryMapForObserver(path, out var mappedPath)
                    ? mappedPath
                    : null,
                () => stateReader.TryReadStartState(out var state)
                    ? state
                    : throw new InvalidDataException("The native movie start state was unstable."),
                () => DateTime.UtcNow,
                captureQueue);

            prepareHook.Activate();
            releaseHook.Activate();
            startHook.Activate();
            stopHook.Activate();
            contract.ActivateHookLease(identities, IsHookCohortEnabled);
        }
        catch
        {
            coordinator?.Stop();
            contract?.RevokeHookLease();
            DisableHooks();
            throw;
        }
    }

    internal bool IsFatallyDegraded => coordinator?.IsFatallyDegraded == true;

    internal static bool TryCreate(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory,
        IReloadedHooks hooks,
        string expectedOpeningMoviePath,
        out Steam2026NativeMovieHookSet hookSet,
        out string diagnostic)
    {
        hookSet = null!;
        diagnostic = string.Empty;
        try
        {
            hookSet = new Steam2026NativeMovieHookSet(
                fingerprint,
                moduleBase,
                moduleImageSize,
                memory,
                hooks,
                expectedOpeningMoviePath);
            diagnostic = "Installed four exact-identity native movie callbacks.";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = $"Native movie callbacks are not ready: {ex.Message}";
            return false;
        }
    }

    internal bool TryDequeue(out NativeMovieIngressSnapshot snapshot) =>
        captureQueue.TryDequeue(out snapshot);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        coordinator?.Stop();
        contract?.RevokeHookLease();
        DisableHooks();

        // Retain the stopped coordinator and its original delegates. If a
        // third-party hook backend cannot disable one detour during teardown,
        // a late callback still reaches its original exactly once.
    }

    private static Dictionary<NativeMovieCallbackKind, NativeMovieCallbackIdentity> ValidateAllTargets(
        NativeMovieCallbackContract contract)
    {
        NativeMovieCallbackKind[] required =
        [
            NativeMovieCallbackKind.Prepare,
            NativeMovieCallbackKind.Release,
            NativeMovieCallbackKind.Start,
            NativeMovieCallbackKind.Stop
        ];

        var identities = new Dictionary<NativeMovieCallbackKind, NativeMovieCallbackIdentity>();
        foreach (var kind in required)
        {
            if (!contract.TryValidateIdentity(kind, out var identity)
                || identity.Metadata.Kind != kind
                || !identity.Metadata.IsInlineDetourEligible
                || identity.Metadata.Shape.Abi != NativeMovieCallbackAbi.MicrosoftX64
                || identity.Address == 0)
            {
                throw new InvalidOperationException(
                    $"The exact native movie callback identity for {kind} is unavailable.");
            }

            identities.Add(kind, identity);
        }

        if (identities.Values.Select(identity => identity.Address).Distinct().Count()
            != required.Length)
        {
            throw new InvalidOperationException("Native movie callback targets are not unique.");
        }

        return identities;
    }

    private static IHook<TDelegate> CreateHook<TDelegate>(
        IReloadedHooks hooks,
        TDelegate detour,
        ulong address)
        where TDelegate : Delegate =>
        hooks.CreateHook(detour, checked((long)address), -1);

    private int OnPrepare(int argument0, int argument1) =>
        coordinator?.OnPrepare(argument0, argument1) ?? 0;

    private void OnRelease() => coordinator?.OnRelease();

    private int OnStart() => coordinator?.OnStart() ?? 0;

    private void OnStop() => coordinator?.OnStop();

    private bool IsHookCohortEnabled(NativeMovieCallbackKind kind) =>
        kind is NativeMovieCallbackKind.Prepare
            or NativeMovieCallbackKind.Release
            or NativeMovieCallbackKind.Start
            or NativeMovieCallbackKind.Stop
        && IsEnabled(prepareHook)
        && IsEnabled(releaseHook)
        && IsEnabled(startHook)
        && IsEnabled(stopHook);

    private void DisableHooks()
    {
        Disable(stopHook);
        Disable(startHook);
        Disable(releaseHook);
        Disable(prepareHook);
    }

    private static void Disable<TDelegate>(IHook<TDelegate>? hook)
        where TDelegate : Delegate
    {
        try
        {
            if (hook is { IsHookActivated: true, IsHookEnabled: true })
            {
                hook.Disable();
            }
        }
        catch
        {
            // Teardown is best-effort and must never escape into Reloaded-II.
        }
    }

    private static bool IsEnabled<TDelegate>(IHook<TDelegate>? hook)
        where TDelegate : Delegate =>
        hook is { IsHookActivated: true, IsHookEnabled: true };
}
