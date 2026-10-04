using System.Reflection.PortableExecutable;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

/// <summary>
/// The Battle Square's screen between two battles (battle menu window 0x1C, FUN_006E3C9C and
/// FUN_006DAA08). On 2026-10-03 the x64 log has five arena battles chained with nothing said
/// between them, then a loss for 0 BP: the "Keep goin'?" question, its two answers, the points
/// and the handicap slot were all silent, because no reader looked at this screen at all.
/// The strings are the bytes at the addresses the drawing calls pass; with the installed
/// legacy ff7_en.exe present they are checked against it, and its whole handicap table is read.
/// </summary>
internal static class BattleArenaScreenTests
{
    private static readonly (uint Address, string Hex)[] NativeTexts =
    [
        (BattleArenaScreenReader.AddressKeepGoingText, "2B45455000474F494E071FFF"),
        (BattleArenaScreenReader.AddressOptionsText, "2F4600434F555253450100000000002E4F0057415901FF"),
        (BattleArenaScreenReader.AddressPointsText, "23555252454E5400224154544C4500304F494E5453FF"),
        (BattleArenaScreenReader.AddressSlotStartText, "334C4F5400535441525401FF"),
        (BattleArenaScreenReader.AddressGreatText, "27322521340101FF"),
        (BattleArenaScreenReader.AddressGoForItText, "3448454E0C00474F00464F5200495401FF"),
        (0x0091EE28, "244F574E0015004C4556454C530EFF"),
        (0x0091EEE8, "394553535301002E4F0048414E44494341505001FF")
    ];

    public static void Run()
    {
        TheQuestionIsTheGamesOwnWithItsCursor();
        TheSlotAndItsHandicapAreSaid();
        NothingIsSaidOffTheScreen();
        ATornOrStrayReadSaysNothing();
        TheInstalledGameHasTheseTexts();
        Console.WriteLine("Battle Square between-battle screen tests passed.");
    }

    private static void TheQuestionIsTheGamesOwnWithItsCursor()
    {
        var memory = Arena(phase: BattleArenaScreenReader.KeepGoingPhase);
        memory.UInt32(BattleArenaScreenReader.AddressBattlePoints, 120);
        var reader = new BattleArenaScreenReader(memory);
        var tracker = new BattleArenaScreenTracker();
        Require(reader.TryRead(out var screen) && screen is not null, "the question is read");
        Equal("Keep goin'?|Current Battle Points 120", string.Join("|", screen!.Lines), "its own lines");
        Equal("Of course!|No way!", string.Join("|", screen.Options), "the two answers, from one native string");
        Equal("Keep goin'? Current Battle Points 120. Choose: Of course!, or No way! Of course!, 1 of 2.",
            tracker.Observe(screen), "opening: question, points, answers, cursor");
        Equal(null, tracker.Observe(Read(reader)), "nothing more while it waits");

        memory.Int32(BattleArenaScreenReader.AddressCursor, 1);
        Equal("No way!, 2 of 2.", tracker.Observe(Read(reader)), "Right: only the answer now under the cursor");
        Equal(null, tracker.Observe(Read(reader)), "once");
        memory.Int32(BattleArenaScreenReader.AddressCursor, 0);
        Equal("Of course!, 1 of 2.", tracker.Observe(Read(reader)), "Left: back to Of course!");
    }

    private static void TheSlotAndItsHandicapAreSaid()
    {
        var memory = Arena(phase: BattleArenaScreenReader.GreatPhase);
        var reader = new BattleArenaScreenReader(memory);
        var tracker = new BattleArenaScreenTracker();
        Equal("GREAT!!", tracker.Observe(Read(reader)), "a won battle's GREAT!!");
        memory.Byte(BattleArenaScreenReader.AddressPhase, BattleArenaScreenReader.KeepGoingPhase);
        Require(tracker.Observe(Read(reader))?.StartsWith("Keep goin'?", StringComparison.Ordinal) == true,
            "then the question");
        memory.Byte(BattleArenaScreenReader.AddressPhase, BattleArenaScreenReader.SlotStartPhase);
        Equal("Slot start!", tracker.Observe(Read(reader)), "Of course! starts the slot");
        Equal(null, tracker.Observe(Read(reader)), "said once");
        memory.Byte(BattleArenaScreenReader.AddressPhase, BattleArenaScreenReader.ReelsPhase);
        Equal(null, tracker.Observe(Read(reader)), "the turning reels are pictures");

        // Second battle won: reel 1 is the one counted. Its position 16 is row |(1 - 2) % 3| = 1,
        // so the symbol is the top byte of DC3B00[1 * 3 + 1]; 16 is "Down 5 levels.".
        memory.Byte(BattleArenaScreenReader.AddressCurrentReel, 1);
        memory.Int16(BattleArenaScreenReader.AddressReelPositions + 2, 16);
        memory.Int32(BattleArenaScreenReader.AddressReelSymbols + 4 * 4, 16 << 24);
        memory.Byte(BattleArenaScreenReader.AddressPhase, BattleArenaScreenReader.HandicapPhase);
        Equal("Then, go for it! Down 5 levels.", tracker.Observe(Read(reader)), "the handicap the reel stopped on");
        Equal(null, tracker.Observe(Read(reader)), "said once");

        // Position 9 rounds to row 0 (|(1 - 1) % 3|): DC3B00[3] is read instead.
        memory.Int16(BattleArenaScreenReader.AddressReelPositions + 2, 9);
        memory.Int32(BattleArenaScreenReader.AddressReelSymbols + 3 * 4, 22 << 24);
        Equal("Then, go for it! Yesss! No handicapp!", tracker.Observe(Read(reader)), "row from the reel's own position");
    }

    private static void NothingIsSaidOffTheScreen()
    {
        var memory = Arena(phase: BattleArenaScreenReader.KeepGoingPhase);
        var reader = new BattleArenaScreenReader(memory);
        var tracker = new BattleArenaScreenTracker();
        Require(tracker.Observe(Read(reader)) is not null, "on screen");

        memory.Byte((uint)BattleStateReader.AddressMenuWindowStates + BattleArenaScreenReader.ArenaMenuState, 3);
        Require(reader.TryRead(out var closing) && closing is null, "a window that is not active is not the screen");
        Equal(null, tracker.Observe(closing), "and says nothing");
        memory.Byte((uint)BattleStateReader.AddressMenuWindowStates + BattleArenaScreenReader.ArenaMenuState,
            BattleStateReader.ActiveWindowState);
        Require(tracker.Observe(Read(reader))?.StartsWith("Keep goin'?", StringComparison.Ordinal) == true,
            "reopened, it is said again");

        memory.Int16((uint)BattleStateReader.AddressBattleMenuTextState, 0x13);
        Require(reader.TryRead(out var other) && other is null, "another battle menu is not this screen");
        memory.Int16((uint)BattleStateReader.AddressBattleMenuTextState, BattleArenaScreenReader.ArenaMenuState);
        memory.Byte((uint)BattleStateReader.AddressCurrentModule, 1);
        Require(reader.TryRead(out var field) && field is null, "outside the battle module there is no screen");
        memory.Byte((uint)BattleStateReader.AddressCurrentModule, BattleStateReader.BattleModule);
        memory.Byte(BattleArenaScreenReader.AddressPhase, 4);
        Require(reader.TryRead(out var leaving) && leaving is null, "phase 4, the next battle starting, says nothing");
    }

    private static void ATornOrStrayReadSaysNothing()
    {
        var memory = Arena(phase: BattleArenaScreenReader.KeepGoingPhase);
        var reader = new BattleArenaScreenReader(memory);
        memory.TearCursor = true;
        Require(!reader.TryRead(out _), "two samples that disagree are no answer");
        memory.TearCursor = false;

        memory.Int32(BattleArenaScreenReader.AddressCursor, 5);
        var tracker = new BattleArenaScreenTracker();
        Equal(null, tracker.Observe(Read(reader)), "a cursor on no answer names none");

        memory.Fail = BattleArenaScreenReader.AddressBattlePoints;
        Require(!reader.TryRead(out _), "an unreadable point total is not zero points");
        memory.Fail = 0;

        memory.Byte(BattleArenaScreenReader.AddressPhase, BattleArenaScreenReader.HandicapPhase);
        memory.Byte(BattleArenaScreenReader.AddressCurrentReel, 0);
        memory.Int16(BattleArenaScreenReader.AddressReelPositions, 0);
        memory.Int32(BattleArenaScreenReader.AddressReelSymbols + 4, 24 << 24);
        Require(!reader.TryRead(out _), "symbol 24 is past the handicap table and is not read as one");
    }

    /// <summary>The embedded bytes are the installed game's, and every handicap reads from it.</summary>
    private static void TheInstalledGameHasTheseTexts()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        var path = string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, "ff7_en.exe");
        if (path is null || !File.Exists(path))
        {
            Console.WriteLine("Battle Square screen: no installed ff7_en.exe; native string check not run.");
            return;
        }

        var memory = Arena(phase: BattleArenaScreenReader.HandicapPhase, image: path);
        foreach (var (address, hex) in NativeTexts)
        {
            var expected = Convert.FromHexString(hex);
            var actual = new byte[expected.Length];
            Require(memory.TryRead(address, actual) && actual.AsSpan().SequenceEqual(expected),
                $"installed bytes at 0x{address:X8}");
        }

        var reader = new BattleArenaScreenReader(memory);
        var said = new List<string>();
        for (var symbol = 0; symbol < BattleArenaScreenReader.HandicapCount; symbol++)
        {
            memory.Int32(BattleArenaScreenReader.AddressReelSymbols + 4, symbol << 24);
            Require(reader.TryRead(out var screen) && screen is not null, $"handicap {symbol} reads");
            said.Add(screen!.Lines[1]);
        }

        Equal("Magic Materia is broken.|Summon Materia is broken.|Support Materia is broken.|" +
              "Independent Materia is broken.|Command Materia is broken.|All Materia is broken.|" +
              "Accessory is broken.|Item command is sealed.|Armor is broken.|Weapon is broken.|1/2 speed.|" +
              "1/2 accuracy.|Minimum|Poison|Toad|Time x30 damage.|Down 5 levels.|Down 10 levels.|1/2 HP.|" +
              "1/2 MP.|1/2 HP&MP.|Zero MP.|Yesss! No handicapp!|HP restored.",
            string.Join("|", said), "the installed game's 24 handicaps, in table order");
    }

    private static BattleArenaScreenSnapshot? Read(BattleArenaScreenReader reader)
    {
        Require(reader.TryRead(out var screen), "coherent read");
        return screen;
    }

    private static ArenaMemory Arena(byte phase, string? image = null)
    {
        var memory = new ArenaMemory();
        if (image is null)
        {
            // The text reader reads whole chunks, as the guest has bytes past each terminator.
            memory.Bytes(0x0091E000, new byte[0x1000]);
            foreach (var (address, hex) in NativeTexts)
            {
                memory.Bytes(address, Convert.FromHexString(hex));
            }
        }
        else
        {
            memory.LoadImage(image, 0x0091E000, 0x0091F000);
        }

        memory.Byte((uint)BattleStateReader.AddressCurrentModule, BattleStateReader.BattleModule);
        memory.Int16((uint)BattleStateReader.AddressBattleMenuTextState, BattleArenaScreenReader.ArenaMenuState);
        memory.Byte((uint)BattleStateReader.AddressMenuWindowStates + BattleArenaScreenReader.ArenaMenuState,
            BattleStateReader.ActiveWindowState);
        memory.Byte(BattleArenaScreenReader.AddressPhase, phase);
        memory.Int32(BattleArenaScreenReader.AddressCursor, 0);
        memory.UInt32(BattleArenaScreenReader.AddressBattlePoints, 0);
        memory.Byte(BattleArenaScreenReader.AddressCurrentReel, 0);
        for (var i = 0; i < 8; i++)
        {
            memory.Int16(BattleArenaScreenReader.AddressReelPositions + (uint)i * 2, 0);
        }

        for (var i = 0; i < 0x15; i++)
        {
            memory.Int32(BattleArenaScreenReader.AddressReelSymbols + (uint)i * 4, 0);
        }

        return memory;
    }

    private static void Require(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    }

    private sealed class ArenaMemory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = new();
        private int cursorReads;
        public bool TearCursor;
        public uint Fail;

        public void Byte(uint address, int value) => bytes[address] = (byte)value;
        public void Int16(uint address, short value) => Bytes(address, BitConverter.GetBytes(value));
        public void Int32(uint address, int value) => Bytes(address, BitConverter.GetBytes(value));
        public void UInt32(uint address, uint value) => Bytes(address, BitConverter.GetBytes(value));

        public void Bytes(uint address, byte[] values)
        {
            for (var i = 0; i < values.Length; i++) bytes[address + (uint)i] = values[i];
        }

        public void LoadImage(string path, uint low, uint high)
        {
            var image = File.ReadAllBytes(path);
            using var stream = new MemoryStream(image, writable: false);
            using var pe = new PEReader(stream);
            var imageBase = checked((uint)pe.PEHeaders.PEHeader!.ImageBase);
            foreach (var section in pe.PEHeaders.SectionHeaders)
            {
                for (var i = 0; i < section.SizeOfRawData; i++)
                {
                    var address = imageBase + (uint)section.VirtualAddress + (uint)i;
                    if (address >= low && address < high) bytes[address] = image[section.PointerToRawData + i];
                }
            }
        }

        public bool TryRead(uint address, Span<byte> destination)
        {
            if (Fail != 0 && address == Fail) return false;
            for (var i = 0; i < destination.Length; i++)
            {
                if (!bytes.TryGetValue(address + (uint)i, out var value)) return false;
                destination[i] = value;
            }

            if (address == BattleArenaScreenReader.AddressCursor && TearCursor && ++cursorReads % 2 == 0)
            {
                destination[0] ^= 1;
            }

            return true;
        }
    }
}
