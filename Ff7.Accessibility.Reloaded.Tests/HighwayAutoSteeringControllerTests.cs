using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;

internal static class HighwayAutoSteeringControllerTests
{
    internal static void Run()
    {
        SendsOnlyChangedCardinalAndDiagonalScanCodes();
        ReleasesEveryOwnedDirectionForNoneAndDisposal();
        CleansUpInsertedKeysAfterAPartialFailure();
        RefusesNewDirectionsUntilResidualKeysAreReleased();
        MapsTransitionsToExtendedWin32ScanCodeEvents();
    }

    private static void SendsOnlyChangedCardinalAndDiagonalScanCodes()
    {
        var sink = new RecordingKeyboardInputSink();
        using var controller = new HighwayAutoSteeringController(sink);

        Equal(true, controller.Apply(HighwaySteeringDirection.Left).Success, "left input succeeds");
        Equal(1, sink.Batches.Count, "left emits one batch");
        Equal(
            new HighwayKeyboardTransition(0x4B, IsKeyDown: true),
            sink.Batches[0].Single(),
            "left uses the DirectInput-compatible left scan code");

        Equal(true, controller.Apply(HighwaySteeringDirection.Left).Success, "repeated left succeeds");
        Equal(1, sink.Batches.Count, "repeated direction emits no duplicate input");

        Equal(true, controller.Apply(HighwaySteeringDirection.UpRight).Success, "up-right input succeeds");
        Equal(2, sink.Batches.Count, "direction change emits one transition batch");
        SequenceEqual(
            [
                new HighwayKeyboardTransition(0x4B, IsKeyDown: false),
                new HighwayKeyboardTransition(0x48, IsKeyDown: true),
                new HighwayKeyboardTransition(0x4D, IsKeyDown: true)
            ],
            sink.Batches[1],
            "left to up-right releases only left before pressing the diagonal");
    }

    private static void ReleasesEveryOwnedDirectionForNoneAndDisposal()
    {
        var sink = new RecordingKeyboardInputSink();
        var controller = new HighwayAutoSteeringController(sink);
        _ = controller.Apply(HighwaySteeringDirection.DownLeft);

        Equal(true, controller.Apply(HighwaySteeringDirection.None).Success, "None releases ownership");
        SequenceEqual(
            [
                new HighwayKeyboardTransition(0x50, IsKeyDown: false),
                new HighwayKeyboardTransition(0x4B, IsKeyDown: false)
            ],
            sink.Batches[1],
            "None releases both owned diagonal keys");

        _ = controller.Apply(HighwaySteeringDirection.Right);
        controller.Dispose();
        Equal(
            new HighwayKeyboardTransition(0x4D, IsKeyDown: false),
            sink.Batches[^1].Single(),
            "disposal releases the last owned direction");
        var batchCount = sink.Batches.Count;
        controller.Dispose();
        Equal(batchCount, sink.Batches.Count, "repeated disposal emits no input");
    }

    private static void CleansUpInsertedKeysAfterAPartialFailure()
    {
        var sink = new RecordingKeyboardInputSink();
        sink.Results.Enqueue(new HighwayKeyboardSendResult(InsertedCount: 1, ErrorCode: 5));
        sink.Results.Enqueue(new HighwayKeyboardSendResult(InsertedCount: 1, ErrorCode: 0));
        using var controller = new HighwayAutoSteeringController(sink);

        var result = controller.Apply(HighwaySteeringDirection.UpRight);

        Equal(false, result.Success, "partial SendInput result fails closed");
        Equal(5, result.ErrorCode, "partial SendInput preserves the Win32 error");
        SequenceEqual(
            [new HighwayKeyboardTransition(0x48, IsKeyDown: false)],
            sink.Batches[1],
            "the one inserted key is immediately released");
        var batchCount = sink.Batches.Count;
        Equal(true, controller.ReleaseAll().Success, "cleanup leaves no owned keys");
        Equal(batchCount, sink.Batches.Count, "empty cleanup emits no duplicate release");
    }

    private static void RefusesNewDirectionsUntilResidualKeysAreReleased()
    {
        var sink = new RecordingKeyboardInputSink();
        sink.Results.Enqueue(new HighwayKeyboardSendResult(InsertedCount: 1, ErrorCode: 5));
        sink.Results.Enqueue(new HighwayKeyboardSendResult(InsertedCount: 0, ErrorCode: 5));
        sink.Results.Enqueue(new HighwayKeyboardSendResult(InsertedCount: 0, ErrorCode: 5));
        using var controller = new HighwayAutoSteeringController(sink);

        var initialFailure = controller.Apply(HighwaySteeringDirection.UpRight);
        Equal(false, initialFailure.Success, "partial direction with failed cleanup enters the fault latch");
        Equal(
            true,
            initialFailure.Diagnostic.Contains("residual", StringComparison.OrdinalIgnoreCase),
            "failed cleanup reports residual owned input");

        sink.Results.Enqueue(new HighwayKeyboardSendResult(InsertedCount: 0, ErrorCode: 5));
        sink.Results.Enqueue(new HighwayKeyboardSendResult(InsertedCount: 0, ErrorCode: 5));
        var batchCountBeforeRetry = sink.Batches.Count;
        var blocked = controller.Apply(HighwaySteeringDirection.Right);
        Equal(false, blocked.Success, "new direction remains blocked while cleanup fails");
        Equal(
            true,
            sink.Batches.Skip(batchCountBeforeRetry).All(batch =>
                batch.All(transition => !transition.IsKeyDown && transition.ScanCode == 0x48)),
            "faulted retry sends only releases and never the requested right key-down");

        sink.Results.Enqueue(new HighwayKeyboardSendResult(InsertedCount: 0, ErrorCode: 5));
        sink.Results.Enqueue(new HighwayKeyboardSendResult(InsertedCount: 0, ErrorCode: 5));
        var repeatedBlocked = controller.Apply(HighwaySteeringDirection.Right);
        Equal(
            blocked.Diagnostic,
            repeatedBlocked.Diagnostic,
            "unchanged cleanup failure remains deduplicatable instead of growing each poll");

        sink.Results.Enqueue(new HighwayKeyboardSendResult(InsertedCount: 1, ErrorCode: 0));
        var recovered = controller.Apply(HighwaySteeringDirection.Right);
        Equal(true, recovered.Success, "successful residual release permits a later valid direction");
        SequenceEqual(
            [new HighwayKeyboardTransition(0x48, IsKeyDown: false)],
            sink.Batches[^2],
            "recovery releases the residual up key first");
        SequenceEqual(
            [new HighwayKeyboardTransition(0x4D, IsKeyDown: true)],
            sink.Batches[^1],
            "right is pressed only after cleanup succeeds");
    }

    private static void MapsTransitionsToExtendedWin32ScanCodeEvents()
    {
        Win32HighwayKeyboardInputSink.Win32Input[]? captured = null;
        var capturedSize = 0;
        var sink = new Win32HighwayKeyboardInputSink(
            (count, inputs, size) =>
            {
                captured = inputs.ToArray();
                capturedSize = size;
                return count;
            },
            () => 0);

        var result = sink.Send(
        [
            new HighwayKeyboardTransition(0x48, IsKeyDown: true),
            new HighwayKeyboardTransition(0x50, IsKeyDown: false)
        ]);

        Equal(2, result.InsertedCount, "Win32 sink reports every inserted transition");
        Equal(2, captured?.Length ?? 0, "Win32 sink sends two INPUT records");
        Equal(1u, captured![0].Type, "Win32 sink uses INPUT_KEYBOARD");
        Equal((ushort)0x48, captured[0].Data.Keyboard.ScanCode, "Win32 sink preserves up scan code");
        Equal(0x0009u, captured[0].Data.Keyboard.Flags, "key down uses scan-code and extended flags");
        Equal(0x000Bu, captured[1].Data.Keyboard.Flags, "key up adds the key-up flag");
        Equal((nuint)0xFF7A5701u, captured[0].Data.Keyboard.ExtraInfo, "input uses the private marker");
        Equal(System.Runtime.InteropServices.Marshal.SizeOf<Win32HighwayKeyboardInputSink.Win32Input>(), capturedSize, "native INPUT size is exact for this architecture");
        Equal(
            Environment.Is64BitProcess ? 40 : 28,
            capturedSize,
            "INPUT includes the largest native union member on x86 and x64");
    }

    private sealed class RecordingKeyboardInputSink : IHighwayKeyboardInputSink
    {
        internal List<IReadOnlyList<HighwayKeyboardTransition>> Batches { get; } = [];
        internal Queue<HighwayKeyboardSendResult> Results { get; } = new();

        public HighwayKeyboardSendResult Send(IReadOnlyList<HighwayKeyboardTransition> transitions)
        {
            Batches.Add(transitions.ToArray());
            return Results.Count > 0
                ? Results.Dequeue()
                : new HighwayKeyboardSendResult(transitions.Count, 0);
        }
    }

    private static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string label)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
