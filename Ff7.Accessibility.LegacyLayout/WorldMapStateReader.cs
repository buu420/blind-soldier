using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Reads the legacy guest world-map player state. The Steam 2026 runtime uses
/// this same reader over its translated guest address space, so no host pointer
/// or runtime-specific structure escapes this boundary.
/// </summary>
public sealed class WorldMapStateReader
{
    public const int WorldModule = 3;
    public const int AddressCurrentModule = FieldPositionReader.AddressCurrentModule;
    public const int AddressWorldProgress = 0x00E28CB4;
    public const int AddressWorldPlayerEntityPointer = 0x00E3A7D0;
    public const int AddressWorldMapType = 0x00E045E8;
    public const int AddressWorldCameraFront = 0x00DFC484;
    public const int AddressGameMoment = 0x00DC08DC;

    public const int PositionXOffset = 0x0C;
    public const int PositionYOffset = 0x10;
    public const int PositionZOffset = 0x14;
    public const int FacingOffset = 0x40;
    public const int WalkmapTypeOffset = 0x4A;
    public const int DirectionOffset = 0x4C;
    public const int ModelIdOffset = 0x50;
    public const int MovementSpeedOffset = 0x55;

    private readonly ILegacyAddressSpace memory;

    public WorldMapStateReader(ILegacyAddressSpace memory)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    public WorldMapStateReadResult Read()
    {
        if (!TryReadFrame(out var first, out var diagnostic))
        {
            return WorldMapStateReadResult.Invalid(first.State, diagnostic);
        }

        if (!TryReadFrame(out var second, out var secondDiagnostic))
        {
            return WorldMapStateReadResult.Invalid(first.State, secondDiagnostic);
        }

        if (first != second)
        {
            return WorldMapStateReadResult.Invalid(first.State, "world player state changed during read");
        }

        return WorldMapStateReadResult.Valid(
            first.State,
            $"module={first.Module}, map={first.WorldMapType}, progress={first.WorldProgress}, " +
            $"player=0x{first.PlayerPointer:X8}, model={first.ModelId}, " +
            $"position={first.X},{first.Y},{first.Z}, terrain={first.TerrainId}, " +
            $"region={first.RegionId}, camera={first.CameraFront}");
    }

    private bool TryReadFrame(out WorldMapFrame frame, out string diagnostic)
    {
        frame = default;
        diagnostic = "world header read failed";
        if (!memory.TryReadByte((uint)AddressCurrentModule, out var module) ||
            !memory.TryReadInt32((uint)AddressWorldMapType, out var worldMapType) ||
            !memory.TryReadInt32((uint)AddressWorldProgress, out var worldProgress) ||
            !memory.TryReadUInt16((uint)AddressGameMoment, out var gameMoment) ||
            !memory.TryReadUInt32((uint)AddressWorldPlayerEntityPointer, out var playerPointer) ||
            !memory.TryReadInt32((uint)AddressWorldCameraFront, out var cameraFront))
        {
            return false;
        }

        frame = new WorldMapFrame(
            module,
            worldMapType,
            worldProgress,
            gameMoment,
            playerPointer,
            cameraFront,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0);
        if (module != WorldModule)
        {
            diagnostic = $"module={module}, not world map";
            return false;
        }

        if (worldMapType is not (0 or 2 or 3))
        {
            diagnostic = $"world map type={worldMapType} is invalid";
            return false;
        }

        if (playerPointer == 0)
        {
            diagnostic = "world player entity pointer is null";
            return false;
        }

        if (!TryAdd(playerPointer, PositionXOffset, out var xAddress) ||
            !TryAdd(playerPointer, PositionYOffset, out var yAddress) ||
            !TryAdd(playerPointer, PositionZOffset, out var zAddress) ||
            !TryAdd(playerPointer, FacingOffset, out var facingAddress) ||
            !TryAdd(playerPointer, WalkmapTypeOffset, out var walkmapAddress) ||
            !TryAdd(playerPointer, DirectionOffset, out var directionAddress) ||
            !TryAdd(playerPointer, ModelIdOffset, out var modelAddress) ||
            !TryAdd(playerPointer, MovementSpeedOffset, out var speedAddress))
        {
            diagnostic = "world player entity address overflowed";
            return false;
        }

        diagnostic = "world player entity read failed";
        if (!memory.TryReadInt32(xAddress, out var x) ||
            !memory.TryReadInt32(yAddress, out var y) ||
            !memory.TryReadInt32(zAddress, out var z) ||
            !memory.TryReadInt16(facingAddress, out var facing) ||
            !memory.TryReadUInt16(walkmapAddress, out var walkmapType) ||
            !memory.TryReadInt16(directionAddress, out var direction) ||
            !memory.TryReadByte(modelAddress, out var modelId) ||
            !memory.TryReadByte(speedAddress, out var movementSpeed))
        {
            return false;
        }

        var terrainId = walkmapType & 0x1F;
        var regionId = (walkmapType >> 9) & 0x1F;
        frame = frame with
        {
            X = x,
            Y = y,
            Z = z,
            Facing = facing,
            TerrainId = terrainId,
            RegionId = regionId,
            Direction = direction,
            ModelId = modelId,
            MovementSpeed = movementSpeed
        };
        diagnostic = string.Empty;
        return true;
    }

    private static bool TryAdd(uint address, int offset, out uint result)
    {
        try
        {
            result = checked(address + (uint)offset);
            return true;
        }
        catch (OverflowException)
        {
            result = 0;
            return false;
        }
    }

    private static int ToSignedControlDirection(int cameraFront)
    {
        var normalized = cameraFront % 4096;
        if (normalized < 0)
        {
            normalized += 4096;
        }

        // The world movement routine rotates controller movement by the
        // negative camera-front angle. Keep that sign here; world-map X is
        // adapted separately before the shared field direction formatter.
        var direction = -(normalized / 16);
        return direction < -128 ? direction + 256 : direction;
    }

    private readonly record struct WorldMapFrame(
        byte Module,
        int WorldMapType,
        int WorldProgress,
        ushort GameMoment,
        uint PlayerPointer,
        int CameraFront,
        int X,
        int Y,
        int Z,
        short Facing,
        int TerrainId,
        int RegionId,
        short Direction,
        byte ModelId,
        byte MovementSpeed)
    {
        public WorldMapStateSnapshot State => new(
            Module,
            WorldMapType,
            WorldProgress,
            GameMoment,
            X,
            Y,
            Z,
            Facing,
            Direction,
            TerrainId,
            RegionId,
            ModelId,
            MovementSpeed,
            CameraFront,
            new FieldNavigationControlTransform(ToSignedControlDirection(CameraFront)));
    }
}

public readonly record struct WorldMapStateSnapshot(
    int CurrentModule,
    int WorldMapType,
    int WorldProgress,
    int GameMoment,
    int X,
    int Y,
    int Z,
    short Facing,
    short Direction,
    int TerrainId,
    int RegionId,
    int PlayerModelId,
    int MovementSpeed,
    int CameraFront,
    FieldNavigationControlTransform ControlTransform)
{
    public bool IsOverworld => WorldMapType == 0;
}

public readonly record struct WorldMapStateReadResult(
    bool IsUsable,
    WorldMapStateSnapshot State,
    string Diagnostic)
{
    public static WorldMapStateReadResult Valid(WorldMapStateSnapshot state, string diagnostic) =>
        new(true, state, diagnostic);

    public static WorldMapStateReadResult Invalid(WorldMapStateSnapshot state, string diagnostic) =>
        new(false, state, diagnostic);
}
