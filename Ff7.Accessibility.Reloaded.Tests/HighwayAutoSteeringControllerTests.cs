using System.Buffers.Binary;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

internal static class HighwayAutoSteeringControllerTests
{
    internal static void Run()
    {
        SendsOnlyChangedCardinalAndDiagonalScanCodes();
        ReleasesEveryOwnedDirectionForNoneAndDisposal();
        CleansUpInsertedKeysAfterAPartialFailure();
        RefusesNewDirectionsUntilResidualKeysAreReleased();
        ResolvesEveryLogicalDirectionFromItsNativeSlot();
        ResolvesRemappedDirectionsAcrossAllThreeLiveBanks();
        RefusesDirectionsWithoutASupportedKeyboardBinding();
        NoneNeedsNoMappingReadAndCanAlwaysReleaseInput();
        RemappingWhileHeldReleasesTheExactOldPhysicalKey();
        MappingReadFailureFailsClosedAndReleasesOwnedInput();
        MapsDefaultKeypadTransitionsToNonExtendedWin32ScanCodeEvents();
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

    private static void MapsDefaultKeypadTransitionsToNonExtendedWin32ScanCodeEvents()
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
            new HighwayKeyboardTransition(0x48, IsKeyDown: true, IsExtended: false),
            new HighwayKeyboardTransition(0x50, IsKeyDown: false, IsExtended: false),
            new HighwayKeyboardTransition(0x48, IsKeyDown: true, IsExtended: true),
            new HighwayKeyboardTransition(0x50, IsKeyDown: false, IsExtended: true)
        ]);

        Equal(4, result.InsertedCount, "Win32 sink reports every inserted transition");
        Equal(4, captured?.Length ?? 0, "Win32 sink sends four INPUT records");
        Equal(1u, captured![0].Type, "Win32 sink uses INPUT_KEYBOARD");
        Equal((ushort)0x48, captured[0].Data.Keyboard.ScanCode, "Win32 sink preserves up scan code");
        Equal(0x0008u, captured[0].Data.Keyboard.Flags, "keypad key down is a nonextended scan code");
        Equal(0x000Au, captured[1].Data.Keyboard.Flags, "keypad key up adds only the key-up flag");
        Equal(0x0009u, captured[2].Data.Keyboard.Flags, "dedicated-arrow key down carries the extended flag");
        Equal(0x000Bu, captured[3].Data.Keyboard.Flags, "dedicated-arrow key up carries extended and key-up flags");
        Equal((nuint)0xFF7A5701u, captured[0].Data.Keyboard.ExtraInfo, "input uses the private marker");
        Equal(System.Runtime.InteropServices.Marshal.SizeOf<Win32HighwayKeyboardInputSink.Win32Input>(), capturedSize, "native INPUT size is exact for this architecture");
        Equal(
            Environment.Is64BitProcess ? 40 : 28,
            capturedSize,
            "INPUT includes the largest native union member on x86 and x64");
    }

    private static void ResolvesRemappedDirectionsAcrossAllThreeLiveBanks()
    {
        var memory = new MutableDirectionMappingAddressSpace();
        memory.SetToken(0, HighwayDirectionInputMappingResolver.UpSlotIndex, 0xDF);
        memory.SetToken(1, HighwayDirectionInputMappingResolver.UpSlotIndex, 0xC8);
        memory.SetToken(0, HighwayDirectionInputMappingResolver.RightSlotIndex, 0xE3);
        memory.SetToken(2, HighwayDirectionInputMappingResolver.RightSlotIndex, 0x20);
        var resolver = new HighwayDirectionInputMappingResolver(memory);

        var resolved = resolver.TryResolve(
            HighwaySteeringDirection.UpRight,
            out var keys,
            out var diagnostic);

        Equal(true, resolved, "a later-bank keyboard binding resolves");
        Equal(string.Empty, diagnostic, "successful mapping has no failure diagnostic");
        SequenceEqual(
            [
                new HighwayKeyboardKey(0x48, IsExtended: true),
                new HighwayKeyboardKey(0x20, IsExtended: false)
            ],
            keys,
            "DIK high bit becomes the SendInput extended flag while an ordinary key stays nonextended");
        Equal(1, memory.ReadCount, "a diagonal is resolved from one coherent mapping-table read");
    }

    private static void ResolvesEveryLogicalDirectionFromItsNativeSlot()
    {
        var memory = new MutableDirectionMappingAddressSpace();
        var cases = new[]
        {
            (HighwaySteeringDirection.Up, HighwayDirectionInputMappingResolver.UpSlotIndex, 0x11u),
            (HighwaySteeringDirection.Right, HighwayDirectionInputMappingResolver.RightSlotIndex, 0x20u),
            (HighwaySteeringDirection.Down, HighwayDirectionInputMappingResolver.DownSlotIndex, 0x1Fu),
            (HighwaySteeringDirection.Left, HighwayDirectionInputMappingResolver.LeftSlotIndex, 0x1Eu)
        };
        foreach (var item in cases)
        {
            memory.SetToken(0, item.Item2, item.Item3);
        }

        var resolver = new HighwayDirectionInputMappingResolver(memory);
        foreach (var item in cases)
        {
            Equal(true, resolver.TryResolve(item.Item1, out var keys, out _), $"{item.Item1} resolves");
            SequenceEqual(
                [new HighwayKeyboardKey(checked((ushort)item.Item3), IsExtended: false)],
                keys,
                $"{item.Item1} reads native slot {item.Item2}");
        }
    }

    private static void RefusesDirectionsWithoutASupportedKeyboardBinding()
    {
        var memory = new MutableDirectionMappingAddressSpace();
        memory.SetToken(0, HighwayDirectionInputMappingResolver.UpSlotIndex, 0xDE);
        memory.SetToken(1, HighwayDirectionInputMappingResolver.UpSlotIndex, 0xDF);
        memory.SetToken(2, HighwayDirectionInputMappingResolver.UpSlotIndex, 0xE3);
        var resolver = new HighwayDirectionInputMappingResolver(memory);

        var resolved = resolver.TryResolve(
            HighwaySteeringDirection.Up,
            out var keys,
            out var diagnostic);

        Equal(false, resolved, "reserved, mouse, and controller slots do not masquerade as keyboard input");
        Equal(0, keys.Count, "an unsupported mapping returns no physical key");
        Equal(
            true,
            diagnostic.Contains("Up", StringComparison.Ordinal) &&
            diagnostic.Contains("keyboard", StringComparison.OrdinalIgnoreCase),
            "the refusal names the direction and tells the caller why it cannot be driven");
    }

    private static void NoneNeedsNoMappingReadAndCanAlwaysReleaseInput()
    {
        var memory = new MutableDirectionMappingAddressSpace
        {
            ReadsSucceed = false
        };
        var resolver = new HighwayDirectionInputMappingResolver(memory);

        Equal(
            true,
            resolver.TryResolve(HighwaySteeringDirection.None, out var keys, out var diagnostic),
            "None remains available when the mapping table cannot be read");
        Equal(0, keys.Count, "None resolves to no physical key");
        Equal(string.Empty, diagnostic, "None has no failure diagnostic");
        Equal(0, memory.ReadCount, "releasing input never depends on another game-memory read");
    }

    private static void RemappingWhileHeldReleasesTheExactOldPhysicalKey()
    {
        var memory = new MutableDirectionMappingAddressSpace();
        memory.SetToken(0, HighwayDirectionInputMappingResolver.UpSlotIndex, 0x48);
        var sink = new RecordingKeyboardInputSink();
        using var controller = new HighwayAutoSteeringController(
            sink,
            new HighwayDirectionInputMappingResolver(memory));

        Equal(true, controller.Apply(HighwaySteeringDirection.Up).Success, "default keypad Up presses");
        memory.SetToken(0, HighwayDirectionInputMappingResolver.UpSlotIndex, 0xC8);
        Equal(true, controller.Apply(HighwaySteeringDirection.Up).Success, "remapped arrow Up presses");

        SequenceEqual(
            [
                new HighwayKeyboardTransition(0x48, IsKeyDown: false, IsExtended: false),
                new HighwayKeyboardTransition(0x48, IsKeyDown: true, IsExtended: true)
            ],
            sink.Batches[1],
            "a live remap releases the nonextended keypad key before pressing the extended arrow");
    }

    private static void MappingReadFailureFailsClosedAndReleasesOwnedInput()
    {
        var memory = new MutableDirectionMappingAddressSpace();
        memory.SetToken(0, HighwayDirectionInputMappingResolver.LeftSlotIndex, 0x4B);
        var sink = new RecordingKeyboardInputSink();
        using var controller = new HighwayAutoSteeringController(
            sink,
            new HighwayDirectionInputMappingResolver(memory));

        Equal(true, controller.Apply(HighwaySteeringDirection.Left).Success, "mapped Left presses");
        memory.ReadsSucceed = false;
        var result = controller.Apply(HighwaySteeringDirection.Left);

        Equal(false, result.Success, "a failed live mapping read fails closed");
        Equal(
            true,
            result.Diagnostic.Contains("mapping", StringComparison.OrdinalIgnoreCase),
            "mapping-read failure is suitable for an audible refusal");
        SequenceEqual(
            [new HighwayKeyboardTransition(0x4B, IsKeyDown: false, IsExtended: false)],
            sink.Batches[1],
            "mapping failure releases the exact physical key already owned");
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

    private sealed class MutableDirectionMappingAddressSpace : ILegacyAddressSpace
    {
        private readonly byte[] table = new byte[HighwayDirectionInputMappingResolver.MappingTableSize];

        internal bool ReadsSucceed { get; set; } = true;

        internal int ReadCount { get; private set; }

        internal void SetToken(int bank, int slot, uint token)
        {
            var offset = checked(
                (bank * HighwayDirectionInputMappingResolver.MappingBankStride) +
                (slot * sizeof(uint)));
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(offset, sizeof(uint)), token);
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            ReadCount++;
            if (!ReadsSucceed ||
                virtualAddress != HighwayDirectionInputMappingResolver.MappingTableAddress ||
                destination.Length != table.Length)
            {
                destination.Clear();
                return false;
            }

            table.CopyTo(destination);
            return true;
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
