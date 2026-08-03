namespace Ff7.Accessibility.Reloaded;

public enum NativeFieldHookEventKind : byte
{
    MessageOpen,
    MessagePreview,
    OpcodeMessage,
    AskCursor,
    CutsceneContext,
    TimerSet
}

public readonly record struct NativeFieldHookEvent(
    NativeFieldHookEventKind Kind,
    FieldOpcodeMessageObservation MessageObservation,
    FieldScriptContext ScriptContext,
    int Result,
    int FieldId,
    int WindowId,
    int DialogId,
    int FirstQuestionLine,
    int LastQuestionLine,
    int CurrentQuestionLine,
    long LifecycleToken);

public sealed class NativeFieldHookEventQueue
{
    private sealed class Slot
    {
        public NativeFieldHookEventKind Kind;
        public FieldOpcodeMessageObservation MessageObservation;
        public FieldScriptContext ScriptContext;
        public int Result;
        public int FieldId;
        public int WindowId;
        public int DialogId;
        public int FirstQuestionLine;
        public int LastQuestionLine;
        public int CurrentQuestionLine;
        public long LifecycleToken;
    }

    private readonly Slot[] slots;
    private long readSequence;
    private long writeSequence;
    private long droppedCount;
    private int captureGate;

    public NativeFieldHookEventQueue(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        slots = new Slot[capacity];
        for (var index = 0; index < slots.Length; index++)
        {
            slots[index] = new Slot();
        }
    }

    public long DroppedCount => Interlocked.Read(ref droppedCount);

    public bool TryCaptureMessageOpen(short windowId, short dialogId, int result) =>
        TryCapture(
            NativeFieldHookEventKind.MessageOpen,
            default,
            default,
            result,
            fieldId: 0,
            windowId,
            dialogId,
            firstQuestionLine: -1,
            lastQuestionLine: -1,
            currentQuestionLine: -1,
            lifecycleToken: 0);

    public bool TryCaptureMessagePreview(short dialogId, int result) =>
        TryCapture(
            NativeFieldHookEventKind.MessagePreview,
            default,
            default,
            result,
            fieldId: 0,
            windowId: -1,
            dialogId,
            firstQuestionLine: -1,
            lastQuestionLine: -1,
            currentQuestionLine: -1,
            lifecycleToken: 0);

    public bool TryCaptureOpcodeMessage(FieldOpcodeMessageObservation observation, int result) =>
        TryCapture(
            NativeFieldHookEventKind.OpcodeMessage,
            observation,
            default,
            result,
            observation.FieldId,
            observation.WindowId,
            observation.DialogId,
            observation.FirstQuestionLine,
            observation.LastQuestionLine,
            currentQuestionLine: -1,
            observation.LifecycleToken);

    public bool TryCaptureAskCursor(
        int fieldId,
        int windowId,
        int dialogId,
        int firstQuestionLine,
        int lastQuestionLine,
        int currentQuestionLine,
        long lifecycleToken = 0) =>
        TryCapture(
            NativeFieldHookEventKind.AskCursor,
            default,
            default,
            result: 0,
            fieldId,
            windowId,
            dialogId,
            firstQuestionLine,
            lastQuestionLine,
            currentQuestionLine,
            lifecycleToken);

    public bool TryCaptureCutsceneContext(FieldScriptContext context) =>
        TryCapture(
            NativeFieldHookEventKind.CutsceneContext,
            default,
            context,
            result: 0,
            context.FieldId,
            windowId: -1,
            dialogId: -1,
            firstQuestionLine: -1,
            lastQuestionLine: -1,
            currentQuestionLine: -1,
            lifecycleToken: 0);

    public bool TryCaptureTimerSet(FieldScriptContext context, int result) =>
        TryCapture(
            NativeFieldHookEventKind.TimerSet,
            default,
            context,
            result,
            context.FieldId,
            windowId: -1,
            dialogId: -1,
            firstQuestionLine: -1,
            lastQuestionLine: -1,
            currentQuestionLine: -1,
            lifecycleToken: 0);

    public bool TryDequeue(out NativeFieldHookEvent hookEvent)
    {
        var read = Volatile.Read(ref readSequence);
        if (read >= Volatile.Read(ref writeSequence))
        {
            hookEvent = default;
            return false;
        }

        var slot = slots[(int)(read % slots.Length)];
        hookEvent = new NativeFieldHookEvent(
            slot.Kind,
            slot.MessageObservation,
            slot.ScriptContext,
            slot.Result,
            slot.FieldId,
            slot.WindowId,
            slot.DialogId,
            slot.FirstQuestionLine,
            slot.LastQuestionLine,
            slot.CurrentQuestionLine,
            slot.LifecycleToken);
        Volatile.Write(ref readSequence, read + 1);
        return true;
    }

    public void WarmUp()
    {
        TryCaptureMessageOpen(0, 0, 0);
        TryDequeue(out _);
        TryCaptureMessagePreview(0, 0);
        TryDequeue(out _);
        TryCaptureOpcodeMessage(default, 0);
        TryDequeue(out _);
        TryCaptureAskCursor(0, 0, 0, 0, 0, 0);
        TryDequeue(out _);
        TryCaptureCutsceneContext(default);
        TryDequeue(out _);
        TryCaptureTimerSet(default, 0);
        TryDequeue(out _);
        Interlocked.Exchange(ref droppedCount, 0);
    }

    private bool TryCapture(
        NativeFieldHookEventKind kind,
        FieldOpcodeMessageObservation messageObservation,
        FieldScriptContext scriptContext,
        int result,
        int fieldId,
        int windowId,
        int dialogId,
        int firstQuestionLine,
        int lastQuestionLine,
        int currentQuestionLine,
        long lifecycleToken)
    {
        if (Interlocked.Exchange(ref captureGate, 1) != 0)
        {
            Interlocked.Increment(ref droppedCount);
            return false;
        }

        try
        {
            var write = Volatile.Read(ref writeSequence);
            var read = Volatile.Read(ref readSequence);
            if (write - read >= slots.Length)
            {
                Interlocked.Increment(ref droppedCount);
                return false;
            }

            var slot = slots[(int)(write % slots.Length)];
            slot.Kind = kind;
            slot.MessageObservation = messageObservation;
            slot.ScriptContext = scriptContext;
            slot.Result = result;
            slot.FieldId = fieldId;
            slot.WindowId = windowId;
            slot.DialogId = dialogId;
            slot.FirstQuestionLine = firstQuestionLine;
            slot.LastQuestionLine = lastQuestionLine;
            slot.CurrentQuestionLine = currentQuestionLine;
            slot.LifecycleToken = lifecycleToken;
            Volatile.Write(ref writeSequence, write + 1);
            return true;
        }
        finally
        {
            Volatile.Write(ref captureGate, 0);
        }
    }
}
