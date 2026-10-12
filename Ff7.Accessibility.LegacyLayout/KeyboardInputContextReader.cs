using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.LegacyLayout;

/// <summary>Cheap native input ownership; inactive field/map globals are never queried.</summary>
public sealed class KeyboardInputContextReader(ILegacyAddressSpace memory, Func<nint> foregroundWindow,
    Func<bool> settingsOwner, Func<bool>? fieldNavigationSuppressed = null,
    Func<bool>? worldNavigationSuppressed = null)
{
    private static readonly int[] NavigationKeys = ['U', 'O', 'J', 'L', 'K', 'I', 'P'];
    private const long FieldNavigationSuppressedBit = 1L << 20;
    private const long WorldNavigationSuppressedBit = 1L << 21;
    public static IReadOnlyList<int> NavigationVirtualKeys => NavigationKeys;
    public static bool IsNormalFieldOwner(long token) =>
        ((token >> 24) & 0xFF) == FieldPositionReader.FieldModule && (token & 1) == 0;
    public static bool IsNormalWorldOwner(long token) =>
        ((token >> 24) & 0xFF) == WorldMapStateReader.WorldModule && (token & 1) == 0;
    public static bool IsCondorActionKey(int key) => key is 'U' or 'O' or 'J' or 'L' or 'I';
    public static bool IsSubmarineActionKey(int key) => key is 'J' or 'L' or 'O' or 'I';
    private static readonly int[] ProgressKeys = [0x74, 0x75, 0x76];

    public static IReadOnlyList<int>? OrderedKeys(long token, int key)
    {
        var module = (int)((token >> 24) & 0xFF);
        if (ProgressKeys.Contains(key)) return ProgressKeys;
        if (module is FieldPositionReader.FieldModule or WorldMapStateReader.WorldModule && NavigationKeys.Contains(key))
            return NavigationKeys;
        if (module == BattleStateReader.BattleModule && BattleStatusHotkeyController.VirtualKeys.Contains(key))
            return BattleStatusHotkeyController.VirtualKeys;
        return null;
    }
    public long? Read()
    {
        var window = foregroundWindow();
        var settings = settingsOwner();
        if (window == 0 || !memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module))
            return null;
        var blocked = module == FieldPositionReader.FieldModule && !settings &&
            (fieldNavigationSuppressed?.Invoke() ?? false);
        var worldBlocked = module == WorldMapStateReader.WorldModule && !settings &&
            (worldNavigationSuppressed?.Invoke() ?? false);
        int scope = 0;
        if (module == FieldPositionReader.FieldModule)
        {
            if (!memory.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var field) ||
                !memory.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var again) || field != again)
                return null;
            scope = field;
        }
        else if (module == WorldMapStateReader.WorldModule)
        {
            if (!memory.TryReadInt32((uint)WorldMapStateReader.AddressWorldMapType, out var map) ||
                !memory.TryReadInt32((uint)WorldMapStateReader.AddressWorldMapType, out var again) || map != again || map is < 0 or > 3)
                return null;
            scope = map;
        }
        if (!memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var endingModule) ||
            module != endingModule || window != foregroundWindow() || settings != settingsOwner() ||
            blocked != (module == FieldPositionReader.FieldModule && !settings &&
                (fieldNavigationSuppressed?.Invoke() ?? false)) ||
            worldBlocked != (module == WorldMapStateReader.WorldModule && !settings &&
                (worldNavigationSuppressed?.Invoke() ?? false))) return null;
        return unchecked(((long)window << 32) | ((long)module << 24) | ((long)scope << 1) |
            (settings ? 1L : 0L) | (blocked ? FieldNavigationSuppressedBit : 0L) |
            (worldBlocked ? WorldNavigationSuppressedBit : 0L));
    }
}
