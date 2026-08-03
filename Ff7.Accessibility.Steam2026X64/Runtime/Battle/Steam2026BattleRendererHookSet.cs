using Ff7.Accessibility.Steam2026X64.Runtime;
using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Battle;

/// <summary>
/// Owns the exact-fingerprint six-callback translated battle lifecycle cohort.
/// No callback is considered observable until every identity, hook, and original
/// wrapper has been created and the entire cohort is enabled.
/// </summary>
internal sealed class Steam2026BattleRendererHookSet : IDisposable
{
    private const int CaptureQueueCapacity = 2048;

    private readonly BoundedNativeIngressQueue<Steam2026BattleRendererIngressSnapshot>
        captureQueue = new(CaptureQueueCapacity);
    private readonly TranslatedBattleRendererCallbackOriginal rendererDetour;
    private readonly TranslatedBattleUpdateCallbackOriginal updateDetour;
    private readonly TranslatedBattleTextActivationCallbackOriginal textDetour;
    private readonly TranslatedBattleResultsUpdateCallbackOriginal resultsDetour;
    private readonly TranslatedBattleDamageDisplayCallbackOriginal damageDetour;
    private readonly TranslatedBattleActionTextCommitCallbackOriginal actionTextCommitDetour;
    private IHook<TranslatedBattleRendererCallbackOriginal>? rendererHook;
    private IHook<TranslatedBattleUpdateCallbackOriginal>? updateHook;
    private IHook<TranslatedBattleTextActivationCallbackOriginal>? textHook;
    private IHook<TranslatedBattleResultsUpdateCallbackOriginal>? resultsHook;
    private IHook<TranslatedBattleDamageDisplayCallbackOriginal>? damageHook;
    private IHook<TranslatedBattleActionTextCommitCallbackOriginal>? actionTextCommitHook;
    private Steam2026BattleRendererCallbackContract? contract;
    private Steam2026BattleRendererDetourIngressCoordinator? coordinator;
    private int disposed;

    private Steam2026BattleRendererHookSet(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory,
        IReloadedHooks hooks)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(hooks);
        rendererDetour = OnMenuRenderer;
        updateDetour = OnBattleUpdate;
        textDetour = OnTextActivation;
        resultsDetour = OnResultsUpdate;
        damageDetour = OnDamageDisplay;
        actionTextCommitDetour = OnActionTextCommit;

        try
        {
            contract = new Steam2026BattleRendererCallbackContract(
                fingerprint,
                moduleBase,
                moduleImageSize,
                memory);
            var targets = ValidateAllTargets(contract);

            rendererHook = hooks.CreateHook(
                rendererDetour,
                checked((long)targets[Steam2026BattleRendererCallbackKind.MenuRenderer]),
                -1);
            updateHook = hooks.CreateHook(
                updateDetour,
                checked((long)targets[Steam2026BattleRendererCallbackKind.BattleUpdate]),
                -1);
            textHook = hooks.CreateHook(
                textDetour,
                checked((long)targets[Steam2026BattleRendererCallbackKind.TextActivation]),
                -1);
            resultsHook = hooks.CreateHook(
                resultsDetour,
                checked((long)targets[Steam2026BattleRendererCallbackKind.ResultsUpdate]),
                -1);
            damageHook = hooks.CreateHook(
                damageDetour,
                checked((long)targets[Steam2026BattleRendererCallbackKind.DamageDisplay]),
                -1);
            actionTextCommitHook = hooks.CreateHook(
                actionTextCommitDetour,
                checked((long)targets[Steam2026BattleRendererCallbackKind.ActionTextCommit]),
                -1);

            coordinator = new Steam2026BattleRendererDetourIngressCoordinator(
                contract,
                rendererHook.OriginalFunction,
                updateHook.OriginalFunction,
                textHook.OriginalFunction,
                resultsHook.OriginalFunction,
                damageHook.OriginalFunction,
                actionTextCommitHook.OriginalFunction,
                () => DateTime.UtcNow,
                captureQueue);

            rendererHook.Activate();
            updateHook.Activate();
            textHook.Activate();
            resultsHook.Activate();
            damageHook.Activate();
            actionTextCommitHook.Activate();
            contract.ActivateHookLease(IsHookCohortEnabled);
        }
        catch
        {
            coordinator?.Stop();
            contract?.RevokeHookLease();
            DisableHooks();
            throw;
        }
    }

    internal bool IsFatallyDegraded =>
        coordinator?.IsFatallyDegraded == true
        || (Volatile.Read(ref disposed) == 0
            && contract is { } activeContract
            && !activeContract.IsActiveHookLeaseHealthy(Environment.TickCount64));

    internal static bool TryCreate(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory,
        IReloadedHooks hooks,
        out Steam2026BattleRendererHookSet hookSet,
        out string diagnostic)
    {
        hookSet = null!;
        diagnostic = string.Empty;
        try
        {
            hookSet = new Steam2026BattleRendererHookSet(
                fingerprint,
                moduleBase,
                moduleImageSize,
                memory,
                hooks);
            diagnostic = "Installed six exact-identity translated battle lifecycle callbacks.";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = $"Translated battle lifecycle callbacks are not ready: {ex.Message}";
            return false;
        }
    }

    internal bool TryDequeue(out Steam2026BattleRendererIngressSnapshot snapshot) =>
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
        // Retain the stopped coordinator so third-party teardown failures still
        // leave every native original reachable through a fail-closed detour.
    }

    private static Dictionary<Steam2026BattleRendererCallbackKind, ulong> ValidateAllTargets(
        Steam2026BattleRendererCallbackContract contract)
    {
        var targets = new Dictionary<Steam2026BattleRendererCallbackKind, ulong>();
        foreach (var kind in Enum.GetValues<Steam2026BattleRendererCallbackKind>())
        {
            if (!contract.TryValidateCaptureIdentity(kind, out var identity)
                || identity.Metadata.Kind != kind
                || identity.Metadata.HostAbi
                    != TranslatedBattleRendererHostAbi.TranslatedX86VoidNoArguments
                || identity.HostAddress == 0)
            {
                throw new InvalidOperationException(
                    $"The exact translated battle callback identity for {kind} is unavailable.");
            }

            targets.Add(kind, identity.HostAddress);
        }

        if (targets.Values.Distinct().Count() != targets.Count)
        {
            throw new InvalidOperationException("Translated battle callback targets are not unique.");
        }

        return targets;
    }

    private void OnMenuRenderer() => coordinator?.OnMenuRenderer();

    private void OnBattleUpdate() => coordinator?.OnBattleUpdate();

    private void OnTextActivation() => coordinator?.OnTextActivation();

    private void OnResultsUpdate() => coordinator?.OnResultsUpdate();

    private void OnDamageDisplay() => coordinator?.OnDamageDisplay();

    private void OnActionTextCommit() => coordinator?.OnActionTextCommit();

    private bool IsHookCohortEnabled(Steam2026BattleRendererCallbackKind kind) =>
        Enum.IsDefined(kind)
        && IsEnabled(rendererHook)
        && IsEnabled(updateHook)
        && IsEnabled(textHook)
        && IsEnabled(resultsHook)
        && IsEnabled(damageHook)
        && IsEnabled(actionTextCommitHook);

    private void DisableHooks()
    {
        Disable(actionTextCommitHook);
        Disable(damageHook);
        Disable(resultsHook);
        Disable(textHook);
        Disable(updateHook);
        Disable(rendererHook);
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
            // Teardown is best effort and must not escape into Reloaded-II.
        }
    }

    private static bool IsEnabled<TDelegate>(IHook<TDelegate>? hook)
        where TDelegate : Delegate =>
        hook is { IsHookActivated: true, IsHookEnabled: true };

}
