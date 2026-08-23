using System.Buffers.Binary;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// One physical keyboard key suitable for a scan-code <c>SendInput</c> event.
/// The extended bit is part of the key identity: keypad 8 and dedicated Up
/// share scan code 0x48 but are different keys.
/// </summary>
internal readonly record struct HighwayKeyboardKey(
    ushort ScanCode,
    bool IsExtended);

internal interface IHighwayDirectionInputMappingResolver
{
    bool TryResolve(
        HighwaySteeringDirection direction,
        out IReadOnlyList<HighwayKeyboardKey> keys,
        out string diagnostic);
}

/// <summary>
/// Resolves logical movement through FFVII's live three-bank control table.
/// This is shared by highway steering, field/world auto-walk, and Fort Condor;
/// none of those features may invent a separate idea of what Up means.
/// </summary>
internal sealed class HighwayDirectionInputMappingResolver(
    ILegacyAddressSpace addressSpace) : IHighwayDirectionInputMappingResolver
{
    internal const uint MappingTableAddress = 0x009A85E8;
    internal const int MappingBankCount = 3;
    internal const int MappingBankStride = 0x64;
    internal const int MappingTableSize = MappingBankCount * MappingBankStride;

    internal const int UpSlotIndex = 12;
    internal const int RightSlotIndex = 13;
    internal const int DownSlotIndex = 14;
    internal const int LeftSlotIndex = 15;

    private const uint KeyboardTokenLimitExclusive = 0xDE;

    private readonly ILegacyAddressSpace addressSpace =
        addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));

    public bool TryResolve(
        HighwaySteeringDirection direction,
        out IReadOnlyList<HighwayKeyboardKey> keys,
        out string diagnostic)
    {
        var components = GetComponents(direction);
        if (components is null)
        {
            keys = Array.Empty<HighwayKeyboardKey>();
            diagnostic = $"unsupported steering direction {direction}";
            return false;
        }

        if (components.Length == 0)
        {
            keys = Array.Empty<HighwayKeyboardKey>();
            diagnostic = string.Empty;
            return true;
        }

        Span<byte> table = stackalloc byte[MappingTableSize];
        if (!addressSpace.TryRead(MappingTableAddress, table))
        {
            keys = Array.Empty<HighwayKeyboardKey>();
            diagnostic = "could not read Final Fantasy VII's live direction mapping";
            return false;
        }

        var resolved = new List<HighwayKeyboardKey>(components.Length);
        foreach (var component in components)
        {
            if (!TryResolveCardinal(table, component.SlotIndex, out var key, out var configured))
            {
                keys = Array.Empty<HighwayKeyboardKey>();
                diagnostic =
                    $"{component.Name} is not assigned to a supported keyboard key in " +
                    $"Final Fantasy VII's controls (live banks: {configured})";
                return false;
            }

            if (!resolved.Contains(key))
            {
                resolved.Add(key);
            }
        }

        keys = resolved.AsReadOnly();
        diagnostic = string.Empty;
        return true;
    }

    /// <summary>
    /// Preserves the sink-only constructor used by deterministic controller
    /// tests. Production construction always supplies the live address-space
    /// resolver through <see cref="HighwayAutoSteeringController.CreateCurrentProcess(ILegacyAddressSpace)"/>.
    /// </summary>
    internal static IHighwayDirectionInputMappingResolver CreateDefaultTestResolver() =>
        new HighwayDirectionInputMappingResolver(DefaultTestAddressSpace.Instance);

    private static bool TryResolveCardinal(
        ReadOnlySpan<byte> table,
        int slotIndex,
        out HighwayKeyboardKey key,
        out string configured)
    {
        Span<uint> configuredTokens = stackalloc uint[MappingBankCount];
        for (var bank = 0; bank < MappingBankCount; bank++)
        {
            var offset = checked((bank * MappingBankStride) + (slotIndex * sizeof(uint)));
            var token = BinaryPrimitives.ReadUInt32LittleEndian(table.Slice(offset, sizeof(uint)));
            configuredTokens[bank] = token;

            // FUN_0041A21E treats values below 0xDE as keyboard tokens. Token
            // zero and an extended token whose base scan is zero cannot name a
            // SendInput keyboard key, so both are refused rather than guessed.
            if (token == 0 || token >= KeyboardTokenLimitExclusive || (token & 0x7Fu) == 0)
            {
                continue;
            }

            key = new HighwayKeyboardKey(
                checked((ushort)(token & 0x7Fu)),
                IsExtended: (token & 0x80u) != 0);
            configured = string.Empty;
            return true;
        }

        key = default;
        configured = string.Join(
            ", ",
            configuredTokens.ToArray().Select(token => $"0x{token:X2}"));
        return false;
    }

    private static DirectionComponent[]? GetComponents(HighwaySteeringDirection direction) =>
        direction switch
        {
            HighwaySteeringDirection.None => [],
            HighwaySteeringDirection.Up => [new("Up", UpSlotIndex)],
            HighwaySteeringDirection.Right => [new("Right", RightSlotIndex)],
            HighwaySteeringDirection.Down => [new("Down", DownSlotIndex)],
            HighwaySteeringDirection.Left => [new("Left", LeftSlotIndex)],
            HighwaySteeringDirection.UpRight =>
                [new("Up", UpSlotIndex), new("Right", RightSlotIndex)],
            HighwaySteeringDirection.DownRight =>
                [new("Down", DownSlotIndex), new("Right", RightSlotIndex)],
            HighwaySteeringDirection.DownLeft =>
                [new("Down", DownSlotIndex), new("Left", LeftSlotIndex)],
            HighwaySteeringDirection.UpLeft =>
                [new("Up", UpSlotIndex), new("Left", LeftSlotIndex)],
            _ => null
        };

    private readonly record struct DirectionComponent(string Name, int SlotIndex);

    private sealed class DefaultTestAddressSpace : ILegacyAddressSpace
    {
        private static readonly byte[] DefaultTable = CreateDefaultTable();

        internal static DefaultTestAddressSpace Instance { get; } = new();

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            if (virtualAddress != MappingTableAddress || destination.Length != DefaultTable.Length)
            {
                destination.Clear();
                return false;
            }

            DefaultTable.CopyTo(destination);
            return true;
        }

        private static byte[] CreateDefaultTable()
        {
            var table = new byte[MappingTableSize];
            Write(UpSlotIndex, 0x48);
            Write(RightSlotIndex, 0x4D);
            Write(DownSlotIndex, 0x50);
            Write(LeftSlotIndex, 0x4B);
            return table;

            void Write(int slotIndex, uint token) =>
                BinaryPrimitives.WriteUInt32LittleEndian(
                    table.AsSpan(slotIndex * sizeof(uint), sizeof(uint)),
                    token);
        }
    }
}
