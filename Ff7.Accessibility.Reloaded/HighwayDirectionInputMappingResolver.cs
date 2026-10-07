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

    /// <summary>
    /// The keyboard key the live control table assigns to one action slot. Resolvers that
    /// know only directions answer no, which fails the caller closed.
    /// </summary>
    bool TryResolveAction(int slotIndex, out HighwayKeyboardKey key, out string diagnostic)
    {
        key = default;
        diagnostic = $"action slot {slotIndex} cannot be resolved";
        return false;
    }

    bool TryResolveSubmarine(HighwaySteeringDirection direction, bool accelerate, bool brake,
        out IReadOnlyList<HighwayKeyboardKey> keys, out string diagnostic)
    {
        keys = Array.Empty<HighwayKeyboardKey>();
        diagnostic = "submarine controls cannot be resolved";
        return false;
    }

    bool TryResolveSubmarineFiringView(out IReadOnlyList<HighwayKeyboardKey> keys, out string diagnostic)
    {
        keys = Array.Empty<HighwayKeyboardKey>();
        diagnostic = "the submarine view control cannot be resolved";
        return false;
    }

    bool TryResolveWorldSubmarine(HighwaySteeringDirection direction, bool thrust,
        out IReadOnlyList<HighwayKeyboardKey> keys, out string diagnostic)
    {
        keys = Array.Empty<HighwayKeyboardKey>();
        diagnostic = "world submarine controls cannot be resolved";
        return false;
    }

    bool TryResolveWorldSubmarineDive(out IReadOnlyList<HighwayKeyboardKey> keys, out string diagnostic)
    {
        keys = Array.Empty<HighwayKeyboardKey>();
        diagnostic = "the world submarine dive control cannot be resolved";
        return false;
    }
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

    /// <summary>
    /// Slot 7, native bit 0x80 (slot n is bit 1 &lt;&lt; n, as Up..Left are 0x1000..0x8000).
    /// FUN_0074EA48 in flight camera 3 translates the Highwind on the directions only while
    /// it is held; without it they turn the ship and change its height.
    /// </summary>
    internal const int FlightActionSlotIndex = 7;

    // FUN_00798580: held 0x10 raises throttle, held 0x40 lowers it; pressed 0x80 fires.
    internal const int SubmarineAccelerateSlotIndex = 4;
    internal const int SubmarineBrakeSlotIndex = 6;
    internal const int SubmarineFireSlotIndex = 7;
    internal const int SubmarineFiringViewSlotIndex = 1;

    private static readonly DirectionComponent[] SubmarineActions =
    [
        new("Dive", 0), new("Change view", 1), new("Rise", 2),
        new("Overview", 3), new("Confirm", 5), new("Fire", SubmarineFireSlotIndex),
        new("Quit", 8), new("Pause", 11),
        new("Accelerate", SubmarineAccelerateSlotIndex), new("Brake", SubmarineBrakeSlotIndex),
        new("Up", UpSlotIndex), new("Right", RightSlotIndex),
        new("Down", DownSlotIndex), new("Left", LeftSlotIndex)
    ];

    private const uint KeyboardTokenLimitExclusive = 0xDE;

    // Module3: slot5 is thrust. Module10 throttle/fire bindings are unrelated.
    private static readonly string[] WorldActionNames =
        ["Camera", "Camera", "Turn camera left", "Turn camera right", "Menu", "Thrust",
         "Dive or surface", "Switch", "Map", "Action9", "Action10", "Map", "Up", "Right", "Down", "Left"];

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

    public bool TryResolveAction(int slotIndex, out HighwayKeyboardKey key, out string diagnostic)
    {
        if (slotIndex is < 0 or >= MappingBankStride / sizeof(uint))
        {
            key = default;
            diagnostic = $"action slot {slotIndex} is outside the control table";
            return false;
        }

        Span<byte> table = stackalloc byte[MappingTableSize];
        if (!addressSpace.TryRead(MappingTableAddress, table))
        {
            key = default;
            diagnostic = "could not read Final Fantasy VII's live control mapping";
            return false;
        }

        if (!TryResolveCardinal(table, slotIndex, out key, out var configured))
        {
            diagnostic =
                $"control slot {slotIndex} is not assigned to a supported keyboard key in " +
                $"Final Fantasy VII's controls (live banks: {configured})";
            return false;
        }

        diagnostic = string.Empty;
        return true;
    }

    public bool TryResolveSubmarine(HighwaySteeringDirection direction, bool accelerate, bool brake,
        out IReadOnlyList<HighwayKeyboardKey> keys, out string diagnostic)
        => TryResolveSubmarineCore(direction, accelerate, brake, false, out keys, out diagnostic);

    public bool TryResolveSubmarineFiringView(out IReadOnlyList<HighwayKeyboardKey> keys, out string diagnostic)
        => TryResolveSubmarineCore(HighwaySteeringDirection.None, false, false, true, out keys, out diagnostic);

    public bool TryResolveWorldSubmarine(HighwaySteeringDirection direction, bool thrust,
        out IReadOnlyList<HighwayKeyboardKey> keys, out string diagnostic)
        => TryResolveWorldSubmarineCore(direction, thrust, false, out keys, out diagnostic);

    public bool TryResolveWorldSubmarineDive(out IReadOnlyList<HighwayKeyboardKey> keys, out string diagnostic)
        => TryResolveWorldSubmarineCore(HighwaySteeringDirection.None, false, true, out keys, out diagnostic);

    private bool TryResolveWorldSubmarineCore(HighwaySteeringDirection direction, bool thrust, bool dive,
        out IReadOnlyList<HighwayKeyboardKey> keys, out string diagnostic)
    {
        keys = Array.Empty<HighwayKeyboardKey>();
        diagnostic = string.Empty;
        var components = GetComponents(direction);
        if (components is null) { diagnostic = "unsupported world submarine direction"; return false; }
        var requested = components.ToList();
        if (thrust) requested.Add(new("Thrust", 5));
        if (dive) requested.Add(new("Dive", 6));
        if (requested.Count == 0) return true;
        Span<byte> table = stackalloc byte[MappingTableSize];
        if (!addressSpace.TryRead(MappingTableAddress, table))
        { diagnostic = "could not read the live world submarine controls"; return false; }
        var resolved = new List<HighwayKeyboardKey>();
        foreach (var component in requested)
        {
            if (!TryResolveCardinal(table, component.SlotIndex, out var key, out _))
            { diagnostic = $"{component.Name} has no supported keyboard binding"; return false; }
            var token = (uint)(key.ScanCode | (key.IsExtended ? 0x80 : 0));
            for (var slot = 0; slot < WorldActionNames.Length; slot++)
            {
                if (requested.Any(c => c.SlotIndex == slot)) continue;
                for (var bank = 0; bank < MappingBankCount; bank++)
                {
                    if (BinaryPrimitives.ReadUInt32LittleEndian(table.Slice(bank * MappingBankStride + slot * 4, 4)) == token)
                    { diagnostic = $"{component.Name} shares a key with {WorldActionNames[slot]}; remap those controls before navigating"; return false; }
                }
            }
            if (!resolved.Contains(key)) resolved.Add(key);
        }
        keys = resolved.AsReadOnly();
        return true;
    }

    private bool TryResolveSubmarineCore(HighwaySteeringDirection direction, bool accelerate, bool brake,
        bool firingView, out IReadOnlyList<HighwayKeyboardKey> keys, out string diagnostic)
    {
        keys = Array.Empty<HighwayKeyboardKey>();
        diagnostic = string.Empty;
        var components = GetComponents(direction);
        if (components is null || (accelerate && brake))
        {
            diagnostic = "unsupported submarine steering or conflicting throttle commands";
            return false;
        }
        if (components.Length == 0 && !accelerate && !brake && !firingView) return true;

        Span<byte> table = stackalloc byte[MappingTableSize];
        if (!addressSpace.TryRead(MappingTableAddress, table))
        {
            diagnostic = "could not read Final Fantasy VII's live submarine controls";
            return false;
        }
        var requested = components.ToList();
        if (accelerate) requested.Add(new("Accelerate", SubmarineAccelerateSlotIndex));
        if (brake) requested.Add(new("Brake", SubmarineBrakeSlotIndex));
        // Target leaves the overview without toggling it back on. It is an
        // ordinary pressed-edge action, delivered alone and checked for aliases.
        if (firingView) requested.Add(new("Change view", SubmarineFiringViewSlotIndex));
        var resolved = new List<HighwayKeyboardKey>(requested.Count);
        foreach (var component in requested)
        {
            if (!TryResolveCardinal(table, component.SlotIndex, out var key, out var configured))
            {
                diagnostic = $"{component.Name} has no supported keyboard binding (live banks: {configured})";
                return false;
            }
            // Any live bank can cause an action. A movement key must not also
            // fire, change the camera, operate a menu, or add movement not in this plan.
            foreach (var action in SubmarineActions)
            {
                if (requested.Any(request => request.SlotIndex == action.SlotIndex)) continue;
                for (var bank = 0; bank < MappingBankCount; bank++)
                {
                    var token = BinaryPrimitives.ReadUInt32LittleEndian(table.Slice(
                        bank * MappingBankStride + action.SlotIndex * sizeof(uint), sizeof(uint)));
                    if (token == (uint)(key.ScanCode | (key.IsExtended ? 0x80 : 0)))
                    {
                        diagnostic = $"{component.Name} shares a key with {action.Name}; remap those controls before pursuing";
                        return false;
                    }
                }
            }
            if (!resolved.Contains(key)) resolved.Add(key);
        }
        keys = resolved.AsReadOnly();
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
