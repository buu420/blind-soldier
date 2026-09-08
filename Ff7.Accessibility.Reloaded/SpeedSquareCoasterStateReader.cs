using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The visible state of the Speed Square shooting coaster: where the player's own
/// sight is pointing and how far the shot has charged. Both are drawn on screen for
/// a sighted player. Nothing here exposes target positions, spawn lists or
/// outcomes - only the aim the player is themselves controlling.
/// </summary>
public readonly record struct SpeedSquareCoasterState(
    bool IsActive,
    bool IsSuspended,
    int CursorX,
    int CursorY,
    int ShotPower)
{
    public const int ScreenWidth = 320;
    public const int ScreenHeight = 240;
    public const int MaximumShotPower = 128;
}

/// <summary>
/// Reads the coaster globals verified against the installed x86 executable. The
/// Steam 2026 x64 build runs the same code through a page-translated guest address
/// space, so one reader serves both runtimes.
///
/// Evidence, all from ff7_en.exe in the installed workingdir:
///  - FUN_0063C17F byte 0x0063C5BA switches on the MINIGAME gameType byte at
///    0x00CC0E7A through the jump table at 0x0063CC31 and writes the module
///    selector 0x00CBF9DC. gameType 0..6 map to modules 6, 7, 8, 9, 10, 11 and 14.
///    gameType 0 -> 6 matches HighwayStateReader.HighwayModule and gameType 3 -> 9
///    matches CondorMinigameProbe.CondorModule, so gameType 5 -> module 11 is the
///    shooting coaster.
///  - FUN_005EE150 reads the suspend byte at 0x00C3F760 first and skips all input
///    while it is non-zero, then clamps the sight to 0..0x140 horizontally
///    (0x00C3FB58) and 0..0xF0 vertically (0x00C3FB5C), and the shot charge at
///    0x00C3FB50 to at most 0x80.
///  - FUN_005EAB70 initialises the sight to (160, 120) and the charge to 128.
/// </summary>
public sealed class SpeedSquareCoasterStateReader
{
    public const byte CoasterModule = 11;
    public const int AddressCursorX = 0x00C3FB58;
    public const int AddressCursorY = 0x00C3FB5C;
    public const int AddressShotPower = 0x00C3FB50;
    public const int AddressSuspended = 0x00C3F760;

    private readonly ILegacyAddressSpace memory;

    public SpeedSquareCoasterStateReader(ILegacyAddressSpace memory)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    public string LastDiagnostic { get; private set; } = string.Empty;

    public bool TryRead(out SpeedSquareCoasterState state)
    {
        state = default;
        if (!memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module))
        {
            LastDiagnostic = "module selector unreadable";
            return false;
        }

        if (module != CoasterModule)
        {
            LastDiagnostic = $"module {module} is not the shooting coaster";
            return false;
        }

        if (!memory.TryReadInt16((uint)AddressCursorX, out var cursorX) ||
            !memory.TryReadInt16((uint)AddressCursorY, out var cursorY) ||
            !memory.TryReadInt16((uint)AddressShotPower, out var shotPower) ||
            !memory.TryReadByte((uint)AddressSuspended, out var suspended))
        {
            LastDiagnostic = "coaster globals unreadable";
            return false;
        }

        // The native routine clamps every one of these itself. A sample outside the
        // clamp is a torn read across the game's own write, not a real position.
        if (cursorX < 0 || cursorX > SpeedSquareCoasterState.ScreenWidth ||
            cursorY < 0 || cursorY > SpeedSquareCoasterState.ScreenHeight ||
            shotPower < 0 || shotPower > SpeedSquareCoasterState.MaximumShotPower)
        {
            LastDiagnostic =
                $"coaster sample outside the native clamp: cursor={cursorX},{cursorY}, power={shotPower}";
            return false;
        }

        state = new SpeedSquareCoasterState(
            IsActive: true,
            IsSuspended: suspended != 0,
            CursorX: cursorX,
            CursorY: cursorY,
            ShotPower: shotPower);
        LastDiagnostic =
            $"coaster cursor={cursorX},{cursorY}, power={shotPower}, suspended={suspended != 0}";
        return true;
    }
}
