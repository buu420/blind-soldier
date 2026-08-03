using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Battle;

/// <summary>
/// Worker-side bridge from validated renderer callbacks to coherent battle
/// frames and the shared native-state battle-menu speech policy.
/// </summary>
internal sealed class Steam2026BattleMenuCoordinator
{
    private readonly object sync = new();
    private readonly Steam2026BattleObservationReader reader;
    private readonly BattleMenuFrameSpeechCoordinator speechCoordinator = new();
    private readonly Queue<string> pendingSpeech = new();
    private BattleMenuStateSnapshot? lastFrame;
    private long lastSequence;
    private int revision;

    internal Steam2026BattleMenuCoordinator(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        INativeMemoryReader memory,
        Steam2026BattleTextResolvers textResolvers)
        : this(new Steam2026BattleObservationReader(
            fingerprint,
            moduleBase,
            memory,
            textResolvers))
    {
    }

    internal Steam2026BattleMenuCoordinator(
        Steam2026BattleObservationReader reader)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    internal RuntimeDomainUpdate<BattleFrameObservation> Observe(
        Steam2026BattleRendererIngressSnapshot snapshot)
    {
        if (snapshot.Sequence <= 0
            || snapshot.TimestampUtc.Kind != DateTimeKind.Utc
            || !Steam2026BattleRendererState.IsSupported(snapshot.RendererState))
        {
            return RuntimeDomainUpdate<BattleFrameObservation>.Unchanged;
        }

        lock (sync)
        {
            if (snapshot.Sequence <= lastSequence)
            {
                return RuntimeDomainUpdate<BattleFrameObservation>.Unchanged;
            }

            lastSequence = snapshot.Sequence;
            BattleMenuStateSnapshot nativeMenu;
            try
            {
                if (!reader.TryReadMenuTrackerSnapshot(
                        snapshot.RendererState,
                        out nativeMenu))
                {
                    return RuntimeDomainUpdate<BattleFrameObservation>.Unchanged;
                }
            }
            catch
            {
                return RuntimeDomainUpdate<BattleFrameObservation>.Unchanged;
            }

            var frameChanged = lastFrame is null || lastFrame.Value != nativeMenu;
            var nextRevision = revision == int.MaxValue ? 1 : revision + 1;
            BattleFrameObservation? frame = null;
            if (frameChanged)
            {
                if (!TryCreateBattleFrame(
                        nextRevision,
                        nativeMenu,
                        out var candidateFrame))
                {
                    return RuntimeDomainUpdate<BattleFrameObservation>.Unchanged;
                }

                frame = candidateFrame;
            }

            speechCoordinator.BeginFrame(snapshot.RendererState);
            speechCoordinator.CompleteFrame(nativeMenu);
            var speech = speechCoordinator.Poll();
            if (!string.IsNullOrWhiteSpace(speech))
            {
                pendingSpeech.Enqueue(new string(speech.AsSpan()));
            }

            if (!frameChanged)
            {
                return RuntimeDomainUpdate<BattleFrameObservation>.Unchanged;
            }

            revision = nextRevision;
            lastFrame = nativeMenu;
            return RuntimeDomainUpdate<BattleFrameObservation>.Present(frame!);
        }
    }

    /// <summary>
    /// Attempts the oldest native-state menu announcement once. Output failure
    /// leaves it queued for the next worker iteration.
    /// </summary>
    internal bool TrySpeakPending(
        Func<string, bool> trySpeak,
        out string spoken)
    {
        ArgumentNullException.ThrowIfNull(trySpeak);
        spoken = string.Empty;
        lock (sync)
        {
            if (pendingSpeech.Count == 0)
            {
                return false;
            }

            var candidate = pendingSpeech.Peek();
            bool accepted;
            try
            {
                accepted = trySpeak(candidate);
            }
            catch
            {
                return false;
            }

            if (!accepted)
            {
                return false;
            }

            pendingSpeech.Dequeue();
            spoken = candidate;
            return true;
        }
    }

    internal void ObserveRootCommandMenuActive(bool active)
    {
        lock (sync)
        {
            speechCoordinator.ObserveRootCommandMenuActive(active);
        }
    }

    internal void Reset()
    {
        lock (sync)
        {
            speechCoordinator.Reset();
            pendingSpeech.Clear();
            lastFrame = null;
            lastSequence = 0;
            revision = 0;
        }
    }

    private static bool TryCreateBattleFrame(
        int revision,
        BattleMenuStateSnapshot menu,
        out BattleFrameObservation frame)
    {
        frame = null!;
        if (revision <= 0
            || menu.PartySlot is < 0 or >= 3
            || !Steam2026BattleRendererState.IsSupported(menu.RendererState)
            || menu.Selection is not { } selection)
        {
            return false;
        }

        var commandId = -1;
        var abilityId = -1;
        var itemId = -1;
        switch (menu.RendererState)
        {
            case 1:
            case 2:
            case 3:
                commandId = selection.EntryId;
                break;
            case 4:
            case 6:
            case 7:
            case 0x18:
                abilityId = selection.EntryId;
                break;
            case 5:
                itemId = selection.EntryId;
                break;
            default:
                return false;
        }

        var actor = menu.Actor;
        frame = new BattleFrameObservation(
            true,
            revision,
            menu.PartySlot,
            commandId,
            abilityId,
            itemId,
            0,
            0,
            [new BattleActorObservation(
                actor.ActorIndex,
                actor.IsEnemy,
                true,
                actor.InformationVisible,
                actor.CurrentHp,
                actor.MaxHp,
                actor.CurrentMp,
                actor.MaxMp,
                actor.StatusMask)]);
        return true;
    }
}
