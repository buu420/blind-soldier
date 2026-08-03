using System.Buffers.Binary;
using System.Text;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Menus;

internal readonly record struct TranslatedMenuCursorObservation(
    Steam2026MenuCallbackKind Source,
    int X,
    int Y,
    int Context);

internal readonly record struct TranslatedMenuWidgetObservation(
    uint GuestWidgetAddress);

internal readonly record struct TranslatedMenuTextObservation(
    Steam2026MenuCallbackKind Source,
    string Text,
    int X,
    int Y,
    int Color,
    int Context);

/// <summary>
/// Captures translated guest-stack state before any original callback could run.
/// It has no original-call, hook, publication, or runtime-capability surface.
/// </summary>
internal sealed class Steam2026MenuCallCaptureDecoder
{
    public const int MaximumTextBytesIncludingTerminator = 128;

    private readonly ulong moduleBase;
    private readonly INativeMemoryReader memory;
    private readonly TranslatedX86AddressSpace translatedAddressSpace;
    private readonly ILegacyAddressSpace addressSpace;
    private readonly object tokenAuthority;

    internal Steam2026MenuCallCaptureDecoder(
        ulong moduleBase,
        INativeMemoryReader memory,
        TranslatedX86AddressSpace translatedAddressSpace,
        object tokenAuthority)
    {
        if (moduleBase == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(moduleBase));
        }

        this.moduleBase = moduleBase;
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.translatedAddressSpace = translatedAddressSpace
                                      ?? throw new ArgumentNullException(nameof(translatedAddressSpace));
        addressSpace = translatedAddressSpace;
        this.tokenAuthority = tokenAuthority ?? throw new ArgumentNullException(nameof(tokenAuthority));
    }

    internal bool TryCaptureCursor(
        Steam2026MenuCaptureToken token,
        Steam2026MenuCallbackKind source,
        out TranslatedMenuCursorObservation observation)
    {
        observation = default;
        if (!token.IsValidFor(tokenAuthority, source)
            || source is not Steam2026MenuCallbackKind.CursorA
            and not Steam2026MenuCallbackKind.CursorB
            || !TryReadCursorSnapshot(token, source, out var first)
            || !TryReadCursorSnapshot(token, source, out var second)
            || first != second)
        {
            return false;
        }

        observation = first;
        return true;
    }

    internal bool TryCaptureActiveWidget(
        Steam2026MenuCaptureToken token,
        out TranslatedMenuWidgetObservation observation)
    {
        observation = default;
        if (!token.IsValidFor(tokenAuthority, Steam2026MenuCallbackKind.ActiveWidgetUpdate)
            || !TryReadWidgetSnapshot(token, out var first)
            || !TryReadWidgetSnapshot(token, out var second)
            || first != second)
        {
            return false;
        }

        observation = first;
        return true;
    }

    internal bool TryCaptureEncodedText(
        Steam2026MenuCaptureToken token,
        Steam2026MenuCallbackKind source,
        out TranslatedMenuTextObservation observation)
    {
        observation = default;
        if (!token.IsValidFor(tokenAuthority, source)
            || source is not Steam2026MenuCallbackKind.EncodedTextA
            and not Steam2026MenuCallbackKind.EncodedTextB
            || !TryReadEncodedTextSnapshot(token, source, out var first)
            || !TryReadEncodedTextSnapshot(token, source, out var second)
            || first != second)
        {
            return false;
        }

        observation = first;
        return true;
    }

    internal bool TryCaptureAsciiRenderer(
        Steam2026MenuCaptureToken token,
        out TranslatedMenuTextObservation observation)
    {
        observation = default;
        if (!token.IsValidFor(tokenAuthority, Steam2026MenuCallbackKind.AsciiRenderer)
            || !TryReadAsciiTextSnapshot(token, out var first)
            || !TryReadAsciiTextSnapshot(token, out var second)
            || first != second)
        {
            return false;
        }

        observation = first;
        return true;
    }

    private bool TryReadCursorSnapshot(
        Steam2026MenuCaptureToken token,
        Steam2026MenuCallbackKind source,
        out TranslatedMenuCursorObservation observation)
    {
        observation = default;
        var frame = CreateFrameReader();
        Span<uint> arguments = stackalloc uint[3];
        if (!HasExpectedEsp(frame, token.GuestEsp)
            || !TryReadExpectedArguments(token, arguments)
            || !HasExpectedEsp(frame, token.GuestEsp))
        {
            return false;
        }

        observation = new TranslatedMenuCursorObservation(
            source,
            unchecked((int)arguments[0]),
            unchecked((int)arguments[1]),
            unchecked((int)arguments[2]));
        return true;
    }

    private bool TryReadWidgetSnapshot(
        Steam2026MenuCaptureToken token,
        out TranslatedMenuWidgetObservation observation)
    {
        observation = default;
        var frame = CreateFrameReader();
        Span<uint> arguments = stackalloc uint[1];
        if (!HasExpectedEsp(frame, token.GuestEsp)
            || !TryReadExpectedArguments(token, arguments)
            || !HasExpectedEsp(frame, token.GuestEsp))
        {
            return false;
        }

        observation = new TranslatedMenuWidgetObservation(arguments[0]);
        return true;
    }

    private bool TryReadEncodedTextSnapshot(
        Steam2026MenuCaptureToken token,
        Steam2026MenuCallbackKind source,
        out TranslatedMenuTextObservation observation)
    {
        observation = default;
        var frame = CreateFrameReader();
        Span<uint> arguments = stackalloc uint[5];
        if (!HasExpectedEsp(frame, token.GuestEsp)
            || !TryReadExpectedArguments(token, arguments)
            || !TryReadTerminatedText(
                token,
                frame,
                arguments[2],
                0xFF,
                encoded: true,
                out var text)
            || !HasExpectedEsp(frame, token.GuestEsp))
        {
            return false;
        }

        observation = new TranslatedMenuTextObservation(
            source,
            text,
            unchecked((int)arguments[0]),
            unchecked((int)arguments[1]),
            unchecked((int)arguments[3]),
            unchecked((int)arguments[4]));
        return true;
    }

    private bool TryReadAsciiTextSnapshot(
        Steam2026MenuCaptureToken token,
        out TranslatedMenuTextObservation observation)
    {
        observation = default;
        var frame = CreateFrameReader();
        Span<uint> arguments = stackalloc uint[5];
        if (!HasExpectedEsp(frame, token.GuestEsp)
            || !TryReadExpectedArguments(token, arguments)
            || !TryReadTerminatedText(
                token,
                frame,
                arguments[0],
                0x00,
                encoded: false,
                out var text)
            || !HasExpectedEsp(frame, token.GuestEsp))
        {
            return false;
        }

        observation = new TranslatedMenuTextObservation(
            Steam2026MenuCallbackKind.AsciiRenderer,
            text,
            unchecked((int)arguments[1]),
            unchecked((int)arguments[2]),
            unchecked((int)arguments[3]),
            unchecked((int)arguments[4]));
        return true;
    }

    private bool TryReadTerminatedText(
        Steam2026MenuCaptureToken token,
        TranslatedX86CallFrameReader frame,
        uint guestTextAddress,
        byte terminator,
        bool encoded,
        out string text)
    {
        text = string.Empty;
        if (guestTextAddress == 0 || (guestTextAddress & 0x80000000u) != 0)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[MaximumTextBytesIncludingTerminator];
        var copied = 0;
        while (copied < bytes.Length)
        {
            var address = checked(guestTextAddress + (uint)copied);
            var bytesUntilPageEnd = TranslatedX86AddressSpace.PageSize
                                    - (int)(address & (TranslatedX86AddressSpace.PageSize - 1));
            var blockLength = Math.Min(bytes.Length - copied, bytesUntilPageEnd);
            var block = bytes.Slice(copied, blockLength);
            if (!HasExpectedEsp(frame, token.GuestEsp))
            {
                return false;
            }

            if (!addressSpace.TryRead(address, block))
            {
                if (!TryReadTextBlockByteByByte(
                        token,
                        frame,
                        address,
                        block,
                        terminator,
                        out var fallbackTerminatorOffset))
                {
                    return false;
                }

                if (fallbackTerminatorOffset >= 0)
                {
                    return TryDecodeText(
                        bytes[..(copied + fallbackTerminatorOffset + 1)],
                        terminator,
                        encoded,
                        out text);
                }
            }
            else
            {
                var terminatorOffset = block.IndexOf(terminator);
                if (terminatorOffset >= 0)
                {
                    return TryDecodeText(
                        bytes[..(copied + terminatorOffset + 1)],
                        terminator,
                        encoded,
                        out text);
                }
            }

            if (!HasExpectedEsp(frame, token.GuestEsp))
            {
                return false;
            }

            copied += blockLength;
        }

        return false;
    }

    private bool TryReadTextBlockByteByByte(
        Steam2026MenuCaptureToken token,
        TranslatedX86CallFrameReader frame,
        uint guestAddress,
        Span<byte> destination,
        byte terminator,
        out int terminatorOffset)
    {
        terminatorOffset = -1;
        Span<byte> oneByte = stackalloc byte[1];
        for (var index = 0; index < destination.Length; index++)
        {
            if (!HasExpectedEsp(frame, token.GuestEsp)
                || !addressSpace.TryRead(checked(guestAddress + (uint)index), oneByte))
            {
                return false;
            }

            destination[index] = oneByte[0];
            if (oneByte[0] == terminator)
            {
                terminatorOffset = index;
                return true;
            }
        }

        return true;
    }

    private static bool TryDecodeText(
        ReadOnlySpan<byte> terminatedBytes,
        byte terminator,
        bool encoded,
        out string text)
    {
        text = string.Empty;
        if (terminatedBytes.IsEmpty
            || terminatedBytes[^1] != terminator)
        {
            return false;
        }

        text = encoded
            ? Ff7EncodedTextDecoder.DecodeTerminated(terminatedBytes)
            : Encoding.ASCII.GetString(terminatedBytes[..^1]);
        return true;
    }

    private TranslatedX86CallFrameReader CreateFrameReader() =>
        new(moduleBase, memory, translatedAddressSpace);

    private bool TryReadExpectedArguments(
        Steam2026MenuCaptureToken token,
        Span<uint> values)
    {
        values.Clear();
        if (!token.IsValidFor(tokenAuthority, token.Identity.Metadata.Kind)
            || values.IsEmpty)
        {
            return false;
        }

        var byteLength = checked(values.Length * sizeof(uint));
        var guestAddress = (ulong)token.GuestEsp + sizeof(uint);
        if (guestAddress > uint.MaxValue
            || guestAddress + (uint)byteLength > (ulong)uint.MaxValue + 1)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[byteLength];
        if (!addressSpace.TryRead((uint)guestAddress, bytes))
        {
            return false;
        }

        for (var index = 0; index < values.Length; index++)
        {
            values[index] = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.Slice(index * sizeof(uint), sizeof(uint)));
        }

        return true;
    }

    private static bool HasExpectedEsp(
        TranslatedX86CallFrameReader frame,
        uint expectedEsp) =>
        frame.TryReadEsp(out var observedEsp) && observedEsp == expectedEsp;
}
