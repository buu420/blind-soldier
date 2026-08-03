using System.Runtime.InteropServices;
using Ff7.Accessibility.Core;

namespace Ff7.Accessibility.Reloaded;

internal readonly record struct HighwayKeyboardTransition(
    ushort ScanCode,
    bool IsKeyDown);

internal readonly record struct HighwayKeyboardSendResult(
    int InsertedCount,
    int ErrorCode);

internal readonly record struct HighwayAutoSteeringInputResult(
    bool Success,
    int RequestedCount,
    int InsertedCount,
    int ErrorCode,
    string Diagnostic);

internal interface IHighwayKeyboardInputSink
{
    HighwayKeyboardSendResult Send(IReadOnlyList<HighwayKeyboardTransition> transitions);
}

/// <summary>
/// Converts owned direction changes into scan-code keyboard transitions. This
/// class has no FFVII state knowledge and never synthesizes an attack button.
/// </summary>
internal sealed class HighwayAutoSteeringController : IDisposable
{
    internal const ushort ScanCodeUp = 0x48;
    internal const ushort ScanCodeDown = 0x50;
    internal const ushort ScanCodeLeft = 0x4B;
    internal const ushort ScanCodeRight = 0x4D;

    private static readonly ushort[] StableKeyOrder =
    [
        ScanCodeUp,
        ScanCodeDown,
        ScanCodeLeft,
        ScanCodeRight
    ];

    private readonly object sync = new();
    private readonly IHighwayKeyboardInputSink sink;
    private readonly HashSet<ushort> ownedScanCodes = [];
    private bool disposed;
    private bool faulted;
    private string faultDiagnostic = string.Empty;

    internal HighwayAutoSteeringController(IHighwayKeyboardInputSink sink)
    {
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    internal static HighwayAutoSteeringController CreateCurrentProcess() =>
        new(new Win32HighwayKeyboardInputSink());

    internal string LastDiagnostic { get; private set; } = string.Empty;

    internal HighwayAutoSteeringInputResult Apply(HighwaySteeringDirection direction)
    {
        lock (sync)
        {
            if (disposed)
            {
                return Failure(0, 0, 0, "motorcycle auto-steering input is disposed");
            }

            if (faulted)
            {
                var cleanup = ReleaseAllCore(maxAttempts: 2);
                if (!cleanup.Success || ownedScanCodes.Count > 0)
                {
                    var diagnostic = BuildResidualDiagnostic(faultDiagnostic, cleanup);
                    LastDiagnostic = diagnostic;
                    return Failure(
                        cleanup.RequestedCount,
                        cleanup.InsertedCount,
                        cleanup.ErrorCode,
                        diagnostic);
                }

                faulted = false;
                faultDiagnostic = string.Empty;
            }

            var desired = ResolveDesiredScanCodes(direction);
            var transitions = new List<HighwayKeyboardTransition>(4);
            foreach (var scanCode in StableKeyOrder)
            {
                if (ownedScanCodes.Contains(scanCode) && !desired.Contains(scanCode))
                {
                    transitions.Add(new HighwayKeyboardTransition(scanCode, IsKeyDown: false));
                }
            }

            foreach (var scanCode in StableKeyOrder)
            {
                if (desired.Contains(scanCode) && !ownedScanCodes.Contains(scanCode))
                {
                    transitions.Add(new HighwayKeyboardTransition(scanCode, IsKeyDown: true));
                }
            }

            var result = SendTransitions(transitions);
            return result.Success ? result : FailAndCleanup(result);
        }
    }

    internal HighwayAutoSteeringInputResult ReleaseAll()
    {
        lock (sync)
        {
            if (disposed)
            {
                return Success(0, 0);
            }

            var result = ReleaseAllCore(maxAttempts: 2);
            if (result.Success && ownedScanCodes.Count == 0)
            {
                faulted = false;
                faultDiagnostic = string.Empty;
                return result;
            }

            if (string.IsNullOrWhiteSpace(faultDiagnostic))
            {
                faultDiagnostic = result.Diagnostic;
            }

            var diagnostic = BuildResidualDiagnostic(faultDiagnostic, result);
            faulted = true;
            LastDiagnostic = diagnostic;
            return Failure(
                result.RequestedCount,
                result.InsertedCount,
                result.ErrorCode,
                diagnostic);
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            _ = ReleaseAllCore(maxAttempts: 4);
            disposed = true;
        }
    }

    private HighwayAutoSteeringInputResult ReleaseAllCore(int maxAttempts)
    {
        HighwayAutoSteeringInputResult result = Success(0, 0);
        for (var attempt = 0; attempt < maxAttempts && ownedScanCodes.Count > 0; attempt++)
        {
            var releases = StableKeyOrder
                .Where(ownedScanCodes.Contains)
                .Select(scanCode => new HighwayKeyboardTransition(scanCode, IsKeyDown: false))
                .ToArray();
            result = SendTransitions(releases);
        }

        return result;
    }

    private HighwayAutoSteeringInputResult SendTransitions(
        IReadOnlyList<HighwayKeyboardTransition> transitions)
    {
        if (transitions.Count == 0)
        {
            LastDiagnostic = string.Empty;
            return Success(0, 0);
        }

        HighwayKeyboardSendResult sendResult;
        try
        {
            sendResult = sink.Send(transitions);
        }
        catch (Exception ex)
        {
            var diagnostic = $"SendInput threw {ex.GetType().Name}: {ex.Message}";
            LastDiagnostic = diagnostic;
            return Failure(transitions.Count, 0, 0, diagnostic);
        }

        var inserted = Math.Clamp(sendResult.InsertedCount, 0, transitions.Count);
        for (var index = 0; index < inserted; index++)
        {
            ApplyOwnership(transitions[index]);
        }

        if (inserted == transitions.Count)
        {
            LastDiagnostic = string.Empty;
            return Success(transitions.Count, inserted);
        }

        var failureDiagnostic =
            $"SendInput inserted {inserted} of {transitions.Count} keyboard transitions " +
            $"(Win32 error {sendResult.ErrorCode})";
        LastDiagnostic = failureDiagnostic;
        return Failure(
            transitions.Count,
            inserted,
            sendResult.ErrorCode,
            failureDiagnostic);
    }

    private HighwayAutoSteeringInputResult FailAndCleanup(
        HighwayAutoSteeringInputResult primaryFailure)
    {
        faulted = true;
        faultDiagnostic = primaryFailure.Diagnostic;
        var cleanup = ReleaseAllCore(maxAttempts: 2);
        if (cleanup.Success && ownedScanCodes.Count == 0)
        {
            faulted = false;
            faultDiagnostic = string.Empty;
            LastDiagnostic = primaryFailure.Diagnostic;
            return primaryFailure;
        }

        var diagnostic = BuildResidualDiagnostic(primaryFailure.Diagnostic, cleanup);
        LastDiagnostic = diagnostic;
        return Failure(
            primaryFailure.RequestedCount,
            primaryFailure.InsertedCount,
            primaryFailure.ErrorCode != 0
                ? primaryFailure.ErrorCode
                : cleanup.ErrorCode,
            diagnostic);
    }

    private string BuildResidualDiagnostic(
        string primaryDiagnostic,
        HighwayAutoSteeringInputResult cleanup)
    {
        var residual = ownedScanCodes.Count == 0
            ? "none"
            : string.Join(", ", StableKeyOrder
                .Where(ownedScanCodes.Contains)
                .Select(scanCode => $"0x{scanCode:X2}"));
        var cleanupDiagnostic = cleanup.Success
            ? "cleanup did not release every owned key"
            : cleanup.Diagnostic;
        return
            $"{primaryDiagnostic}; cleanup failed: {cleanupDiagnostic}; " +
            $"residual owned scan codes: {residual}";
    }

    private void ApplyOwnership(HighwayKeyboardTransition transition)
    {
        if (transition.IsKeyDown)
        {
            ownedScanCodes.Add(transition.ScanCode);
        }
        else
        {
            ownedScanCodes.Remove(transition.ScanCode);
        }
    }

    private static HashSet<ushort> ResolveDesiredScanCodes(HighwaySteeringDirection direction) =>
        direction switch
        {
            HighwaySteeringDirection.Left => [ScanCodeLeft],
            HighwaySteeringDirection.Right => [ScanCodeRight],
            HighwaySteeringDirection.Up => [ScanCodeUp],
            HighwaySteeringDirection.Down => [ScanCodeDown],
            HighwaySteeringDirection.UpLeft => [ScanCodeUp, ScanCodeLeft],
            HighwaySteeringDirection.UpRight => [ScanCodeUp, ScanCodeRight],
            HighwaySteeringDirection.DownLeft => [ScanCodeDown, ScanCodeLeft],
            HighwaySteeringDirection.DownRight => [ScanCodeDown, ScanCodeRight],
            _ => []
        };

    private static HighwayAutoSteeringInputResult Success(int requested, int inserted) =>
        new(true, requested, inserted, 0, string.Empty);

    private static HighwayAutoSteeringInputResult Failure(
        int requested,
        int inserted,
        int error,
        string diagnostic) =>
        new(false, requested, inserted, error, diagnostic);
}

/// <summary>
/// Architecture-correct Win32 SendInput boundary shared by the x86 and x64
/// builds. Extended scan codes match FFVII's stored DirectInput mappings.
/// </summary>
internal sealed class Win32HighwayKeyboardInputSink : IHighwayKeyboardInputSink
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventScanCode = 0x0008;
    private const uint InputMarker = 0xFF7A5701;

    internal delegate uint SendInputInvoker(
        uint inputCount,
        Win32Input[] inputs,
        int inputSize);

    private readonly SendInputInvoker sendInput;
    private readonly Func<int> getLastError;

    internal Win32HighwayKeyboardInputSink()
        : this(NativeMethods.SendInput, Marshal.GetLastWin32Error)
    {
    }

    internal Win32HighwayKeyboardInputSink(
        SendInputInvoker sendInput,
        Func<int> getLastError)
    {
        this.sendInput = sendInput ?? throw new ArgumentNullException(nameof(sendInput));
        this.getLastError = getLastError ?? throw new ArgumentNullException(nameof(getLastError));
    }

    public HighwayKeyboardSendResult Send(IReadOnlyList<HighwayKeyboardTransition> transitions)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        if (transitions.Count == 0)
        {
            return new HighwayKeyboardSendResult(0, 0);
        }

        var inputs = transitions
            .Select(transition => new Win32Input
            {
                Type = InputKeyboard,
                Data = new Win32InputUnion
                {
                    Keyboard = new Win32KeyboardInput
                    {
                        VirtualKey = 0,
                        ScanCode = transition.ScanCode,
                        Flags = KeyEventScanCode |
                            KeyEventExtendedKey |
                            (transition.IsKeyDown ? 0u : KeyEventKeyUp),
                        Time = 0,
                        ExtraInfo = (nuint)InputMarker
                    }
                }
            })
            .ToArray();
        var inserted = sendInput(
            checked((uint)inputs.Length),
            inputs,
            Marshal.SizeOf<Win32Input>());
        var boundedInserted = checked((int)Math.Min(inserted, (uint)inputs.Length));
        return new HighwayKeyboardSendResult(
            boundedInserted,
            boundedInserted == inputs.Length ? 0 : getLastError());
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32Input
    {
        internal uint Type;
        internal Win32InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct Win32InputUnion
    {
        [FieldOffset(0)]
        internal Win32MouseInput Mouse;

        [FieldOffset(0)]
        internal Win32KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32MouseInput
    {
        internal int X;
        internal int Y;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32KeyboardInput
    {
        internal ushort VirtualKey;
        internal ushort ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint SendInput(
            uint inputCount,
            [In] Win32Input[] inputs,
            int inputSize);
    }
}
