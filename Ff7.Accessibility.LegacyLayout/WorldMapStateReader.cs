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

    /// <summary>
    /// The entity the native collision last reported contact with, written by
    /// FUN_00762993 and read by FUN_0076420A when Confirm is pressed.
    /// </summary>
    public const int ContactEntityOffset = 0x04;

    public const int PositionXOffset = 0x0C;
    public const int PositionYOffset = 0x10;
    public const int PositionZOffset = 0x14;

    /// <summary>
    /// The model's drawn rotation. FUN_0076420A eases it an eighth of the way to
    /// <see cref="FacingOffset"/> every frame through FUN_00761C07.
    /// </summary>
    public const int ModelRotationOffset = 0x3C;

    /// <summary>
    /// The turn the ground follow adds while it slides the entity round an obstacle:
    /// FUN_00751EFC hands FUN_00761DF5 the angle of the sample it accepted, and that eases
    /// back towards zero once nothing is being slid round.
    /// </summary>
    public const int SlideRotationOffset = 0x3E;

    public const int FacingOffset = 0x40;
    public const int WalkmapTypeOffset = 0x4A;
    public const int DirectionOffset = 0x4C;
    public const int ModelIdOffset = 0x50;

    /// <summary>
    /// Entity flag byte. Bit 0x80 on the Tiny Bronco switches its get-off to a different
    /// probe and terrain mask (FUN_00766417, FUN_0074CECA case 5).
    /// </summary>
    public const int EntityFlagsOffset = 0x51;

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
            $"terrainScript={first.TerrainScriptId}, " +
            $"region={first.RegionId}, camera={first.CameraFront}" +
            (first.HasModelRotation
                ? $", facing={first.Facing}, rotation={first.ModelRotation}{first.SlideRotation:+0;-0;+0}, " +
                  $"flags=0x{first.EntityFlags:X2}"
                : string.Empty));
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
            false,
            0,
            0,
            0,
            0,
            false,
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

        if (!TryAdd(playerPointer, ContactEntityOffset, out var contactAddress) ||
            !TryAdd(playerPointer, PositionXOffset, out var xAddress) ||
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
        if (!memory.TryReadUInt32(contactAddress, out var contactEntity) ||
            !memory.TryReadInt32(xAddress, out var x) ||
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

        // Only the Tiny Bronco's landing prediction uses these, and it declines to predict
        // without them, so an unreadable rotation costs that one feature rather than the
        // whole world state every other feature depends on.
        short modelRotation = 0;
        short slideRotation = 0;
        byte entityFlags = 0;
        var hasModelRotation =
            TryAdd(playerPointer, ModelRotationOffset, out var modelRotationAddress) &&
            TryAdd(playerPointer, SlideRotationOffset, out var slideRotationAddress) &&
            TryAdd(playerPointer, EntityFlagsOffset, out var flagsAddress) &&
            memory.TryReadInt16(modelRotationAddress, out modelRotation) &&
            memory.TryReadInt16(slideRotationAddress, out slideRotation) &&
            memory.TryReadByte(flagsAddress, out entityFlags);
        if (!hasModelRotation)
        {
            modelRotation = 0;
            slideRotation = 0;
            entityFlags = 0;
        }

        var terrainId = walkmapType & 0x1F;
        var terrainScriptId = (walkmapType >> 5) & 0x07;
        var regionId = (walkmapType >> 9) & 0x1F;
        var hasChocoboTracks = (walkmapType & 0x8000) != 0;
        frame = frame with
        {
            X = x,
            Y = y,
            Z = z,
            Facing = facing,
            TerrainId = terrainId,
            TerrainScriptId = terrainScriptId,
            RegionId = regionId,
            HasChocoboTracks = hasChocoboTracks,
            Direction = direction,
            ModelId = modelId,
            MovementSpeed = movementSpeed,
            ContactEntity = contactEntity,
            HasModelRotation = hasModelRotation,
            ModelRotation = modelRotation,
            SlideRotation = slideRotation,
            EntityFlags = entityFlags
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
        int TerrainScriptId,
        int RegionId,
        bool HasChocoboTracks,
        short Direction,
        byte ModelId,
        byte MovementSpeed,
        uint ContactEntity,
        bool HasModelRotation,
        short ModelRotation,
        short SlideRotation,
        byte EntityFlags)
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
            new FieldNavigationControlTransform(ToSignedControlDirection(CameraFront)))
        {
            HasChocoboTracks = HasChocoboTracks,
            TerrainScriptId = TerrainScriptId,
            NativePlayerEntityPointer = PlayerPointer,
            NativeContactEntityPointer = ContactEntity,
            HasModelRotation = HasModelRotation,
            ModelRotation = ModelRotation,
            SlideRotation = SlideRotation,
            EntityFlags = EntityFlags
        };
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

    public bool HasChocoboTracks { get; init; }

    public int TerrainScriptId { get; init; }

    /// <summary>
    /// The native world entity the party currently is, from
    /// <see cref="WorldMapStateReader.AddressWorldPlayerEntityPointer"/>. Zero when the
    /// host has not supplied one.
    /// </summary>
    public uint NativePlayerEntityPointer { get; init; }

    /// <summary>
    /// The native contact pointer at <c>player + 4</c>. FUN_00762993 stores the entity
    /// FUN_00762A21 reported contact with, before the move is accepted or refused, and
    /// FUN_0076420A reads it when Confirm is pressed. Dynamic, not topology: it survives
    /// the step being rolled back and nothing here assumes the game clears it. Zero is no
    /// witness.
    /// </summary>
    public uint NativeContactEntityPointer { get; init; }

    /// <summary>
    /// Whether <see cref="ModelRotation"/>, <see cref="SlideRotation"/> and
    /// <see cref="EntityFlags"/> were read from the live entity. A snapshot built any other
    /// way has no rotation to predict a landing from, and must not be treated as facing
    /// angle zero.
    /// </summary>
    public bool HasModelRotation { get; init; }

    /// <summary>Entity +0x3C: the drawn rotation, eased towards <see cref="Facing"/>.</summary>
    public short ModelRotation { get; init; }

    /// <summary>Entity +0x3E: the extra turn left by sliding round an obstacle.</summary>
    public short SlideRotation { get; init; }

    /// <summary>Entity +0x51.</summary>
    public byte EntityFlags { get; init; }
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
