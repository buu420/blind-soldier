using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public readonly record struct EchoSReactorTimerOverrideDecision(
    uint ScriptPointer,
    FieldScriptContext Context,
    uint Address,
    int Seconds);

public sealed class EchoSReactorTimerOverrideTracker
{
    public const uint NativeCountdownAddress = 0x00DC08BC;
    public const int RestoredSeconds = 10 * 60;

    private const int ReactorFieldId = 125;
    private const int TimerEntityId = 1;
    private const int TimerScriptId = 0;
    private const int FirstEchoTimerByteIndex = 0x89;
    private const int SecondEchoTimerByteIndex = 0x91;

    private readonly Queue<FieldScriptContext> pendingContexts = [];
    private readonly HashSet<FieldScriptContext> queuedContexts = [];
    private int fieldId = -1;
    private uint scriptPointer;
    private bool applied;

    public bool HasPending => pendingContexts.Count > 0;

    public bool Queue(FieldScriptContext context)
    {
        if (!IsExactEchoTimerCandidate(context) ||
            queuedContexts.Contains(context) ||
            pendingContexts.Count >= 2)
        {
            return false;
        }

        pendingContexts.Enqueue(context);
        queuedContexts.Add(context);
        return true;
    }

    public EchoSReactorTimerOverrideDecision? TryResolve(LoadedFieldScriptIdentity identity)
    {
        ObserveLifecycle(identity);
        if (applied ||
            identity.FieldId != ReactorFieldId ||
            EchoSCompatibilityManifest.ResolveVariant(identity) != SupportedFieldScriptVariant.EchoS124 ||
            pendingContexts.Count == 0)
        {
            return null;
        }

        var context = pendingContexts.Peek();
        return new EchoSReactorTimerOverrideDecision(
            identity.ScriptPointer,
            context,
            NativeCountdownAddress,
            RestoredSeconds);
    }

    public void Acknowledge(EchoSReactorTimerOverrideDecision decision, bool applied)
    {
        if (!applied ||
            scriptPointer != decision.ScriptPointer ||
            pendingContexts.Count == 0 ||
            pendingContexts.Peek() != decision.Context)
        {
            return;
        }

        queuedContexts.Remove(pendingContexts.Dequeue());
        pendingContexts.Clear();
        queuedContexts.Clear();
        this.applied = true;
    }

    public void ObserveLifecycle(LoadedFieldScriptIdentity identity)
    {
        if (fieldId == identity.FieldId && scriptPointer == identity.ScriptPointer)
        {
            return;
        }

        var preserveIdentityRaceCandidate =
            identity.FieldId == ReactorFieldId && fieldId != ReactorFieldId;
        fieldId = identity.FieldId;
        scriptPointer = identity.ScriptPointer;
        applied = false;
        if (!preserveIdentityRaceCandidate)
        {
            pendingContexts.Clear();
            queuedContexts.Clear();
        }
    }

    public void Reset()
    {
        fieldId = -1;
        scriptPointer = 0;
        applied = false;
        pendingContexts.Clear();
        queuedContexts.Clear();
    }

    public static bool IsExactEchoTimerCandidate(FieldScriptContext context) =>
        context.FieldId == ReactorFieldId &&
        context.EntityId == TimerEntityId &&
        context.ScriptId == TimerScriptId &&
        context.Opcode == FieldOpcodeAddressResolver.OpcodeTimerIndex &&
        context.ByteIndex is FirstEchoTimerByteIndex or SecondEchoTimerByteIndex;
}
