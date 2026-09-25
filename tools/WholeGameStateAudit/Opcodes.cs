namespace WholeGameStateAudit;

/// <summary>How an opcode touches a variable operand.</summary>
internal enum OperandAccess
{
    Read,
    Write,
    BitWrite
}

/// <summary>
/// One operand that is a bank variable whenever its bank nibble is non-zero, and an
/// immediate otherwise. <see cref="Offset"/> and <see cref="FieldSize"/> locate the operand
/// in the opcode; <see cref="VariableSize"/> is the width the opcode reads or writes when
/// it is a variable; <see cref="Nibble"/> numbers the bank nibbles from 1 (high nibble of
/// the first bank byte) to 6 (low nibble of the third).
/// </summary>
internal sealed record VariableOperand(
    string Name,
    int Offset,
    int FieldSize,
    int VariableSize,
    int Nibble,
    OperandAccess Access);

internal enum FlowKind
{
    Normal,
    Return,
    ReturnTo,
    Jump,
    ConditionalJump,
    Call,
    PartyCall,
    GameOver,

    /// <summary>Unimplemented on PC: the handler returns without advancing, so the script stops there.</summary>
    Stall,

    /// <summary>
    /// MAPJUMP: the field loop (0063C17F) answers the request by loading the destination, the
    /// same field included, and nothing after the opcode runs.
    /// </summary>
    LeavesField
}

/// <summary>
/// The field opcode set. Names, fixed lengths and variable operands are generated from
/// Makou Reactor's Opcode.h/Opcode.cpp (commit 2452025714c0, read as reference only) and
/// then corrected where Makou's own description of an opcode contradicts its variable
/// table. The fixed lengths are identical to the shipping catalog's table, byte for byte.
/// </summary>
internal static class Opcodes
{
    public const byte RET = 0x00;
    public const byte REQ = 0x01;
    public const byte REQSW = 0x02;
    public const byte REQEW = 0x03;
    public const byte PREQ = 0x04;
    public const byte PRQSW = 0x05;
    public const byte PRQEW = 0x06;
    public const byte RETTO = 0x07;
    public const byte SPECIAL = 0x0F;
    public const byte JMPF = 0x10;
    public const byte JMPFL = 0x11;
    public const byte JMPB = 0x12;
    public const byte JMPBL = 0x13;
    public const byte IFUB = 0x14;
    public const byte IFUBL = 0x15;
    public const byte IFSW = 0x16;
    public const byte IFSWL = 0x17;
    public const byte IFUW = 0x18;
    public const byte IFUWL = 0x19;
    public const byte SAVEMAPCOPY = 0x1A;
    public const byte IFNANAKI = 0x1B;
    public const byte MEMWRITE = 0x1C;
    public const byte MINIGAME = 0x20;
    public const byte BTRLD = 0x23;
    public const byte KAWAI = 0x28;
    public const byte IFKEY = 0x30;
    public const byte IFKEYON = 0x31;
    public const byte IFKEYOFF = 0x32;
    public const byte UC = 0x33;
    public const byte GOLDu = 0x39;
    public const byte GOLDd = 0x3A;
    public const byte CHGLD = 0x3B;
    public const byte MESSAGE = 0x40;
    public const byte MPNAM = 0x43;
    public const byte ASK = 0x48;
    public const byte MENU = 0x49;
    public const byte MENU2 = 0x4A;
    public const byte BTLTB = 0x4B;
    public const byte STITM = 0x58;
    public const byte DLITM = 0x59;
    public const byte CKITM = 0x5A;
    public const byte SMTRA = 0x5B;
    public const byte DMTRA = 0x5C;
    public const byte CMTRA = 0x5D;
    public const byte MAPJUMP = 0x60;
    public const byte IDLCK = 0x6D;
    public const byte BATTLE = 0x70;
    public const byte BTLON = 0x71;
    public const byte PXYZI = 0x75;
    public const byte TLKON = 0x7E;
    public const byte SETBYTE = 0x80;
    public const byte SETWORD = 0x81;
    public const byte BITON = 0x82;
    public const byte BITOFF = 0x83;
    public const byte BITXOR = 0x84;
    public const byte SETX = 0x9D;
    public const byte GETX = 0x9E;
    public const byte SEARCHX = 0x9F;
    public const byte PC = 0xA0;
    public const byte CHAR = 0xA1;
    public const byte VISI = 0xA4;
    public const byte XYZI = 0xA5;
    public const byte XYI = 0xA6;
    public const byte XYZ = 0xA7;
    public const byte TALKR = 0xC5;
    public const byte SLIDR = 0xC6;
    public const byte SOLID = 0xC7;
    public const byte PRTYP = 0xC8;
    public const byte PRTYM = 0xC9;
    public const byte PRTYE = 0xCA;
    public const byte IFPRTYQ = 0xCB;
    public const byte IFMEMBQ = 0xCC;
    public const byte MMBud = 0xCD;
    public const byte MMBLK = 0xCE;
    public const byte MMBUK = 0xCF;
    public const byte LINE = 0xD0;
    public const byte LINON = 0xD1;
    public const byte MPJPO = 0xD2;
    public const byte SLINE = 0xD3;
    public const byte TLKR2 = 0xD6;
    public const byte SLDR2 = 0xD7;
    public const byte PMJMP = 0xD8;
    public const byte PMJMP2 = 0xD9;
    public const byte BGON = 0xE0;
    public const byte BGOFF = 0xE1;
    public const byte BGCLR = 0xE4;
    public const byte PMVIE = 0xF8;
    public const byte MOVIE = 0xF9;
    public const byte MVIEF = 0xFA;
    public const byte GAMEOVER = 0xFF;

    /// <summary>
    /// Opcodes Makou Reactor lists as unused and gives no behaviour. A reachable one is
    /// reported, never silently decoded as if it meant something.
    /// </summary>
    private static readonly HashSet<byte> UndefinedOpcodes =
        [0x0C, 0x0D, 0x1D, 0x1E, 0x1F, 0x44, 0x46, 0x4C, 0x4E, 0xBE];

    /// <summary>
    /// Opcodes that community metadata gives a meaning PC does not implement: 0x1A (savemap
    /// copy), 0x1B (the PSX Nanaki-name test) and 0x1C (raw memory write). Root's native
    /// dispatch table points all three at 006107E1, which returns without advancing the
    /// script, so on PC a script that reaches one stops there.
    /// </summary>
    private static readonly HashSet<byte> RawMemoryOpcodes = [SAVEMAPCOPY, IFNANAKI, MEMWRITE];

    private static readonly Dictionary<byte, string> SpecialNames = new()
    {
        [0xF5] = "ARROW",
        [0xF6] = "PNAME",
        [0xF7] = "GMSPD",
        [0xF8] = "SMSPD",
        [0xF9] = "FLMAT",
        [0xFA] = "FLITM",
        [0xFB] = "BTLCK",
        [0xFC] = "MVLCK",
        [0xFD] = "SPCNM",
        [0xFE] = "RSGLB",
        [0xFF] = "CLITM"
    };

    private static readonly string[] Names =
    [
        "RET", "REQ", "REQSW", "REQEW", "PREQ", "PRQSW", "PRQEW", "RETTO",
        "JOIN", "SPLIT", "SPTYE", "GTPYE", "Unused0C", "Unused0D", "DSKCG", "SPECIAL",
        "JMPF", "JMPFL", "JMPB", "JMPBL", "IFUB", "IFUBL", "IFSW", "IFSWL",
        "IFUW", "IFUWL", "Unused1A", "Unused1B", "Unused1C", "Unused1D", "Unused1E", "Unused1F",
        "MINIGAME", "TUTOR", "BTMD2", "BTRLD", "WAIT", "NFADE", "BLINK", "BGMOVIE",
        "KAWAI", "KAWIW", "PMOVA", "SLIP", "BGPDH", "BGSCR", "WCLS", "WSIZW",
        "IFKEY", "IFKEYON", "IFKEYOFF", "UC", "PDIRA", "PTURA", "WSPCL", "WNUMB",
        "STTIM", "GOLDu", "GOLDd", "CHGLD", "HMPMAX1", "HMPMAX2", "MHMMX", "HMPMAX3",
        "MESSAGE", "MPARA", "MPRA2", "MPNAM", "Unused44", "MPu", "Unused46", "MPd",
        "ASK", "MENU", "MENU2", "BTLTB", "Unused4C", "HPu", "Unused4E", "HPd",
        "WINDOW", "WMOVE", "WMODE", "WREST", "WCLSE", "WROW", "GWCOL", "SWCOL",
        "STITM", "DLITM", "CKITM", "SMTRA", "DMTRA", "CMTRA", "SHAKE", "NOP",
        "MAPJUMP", "SCRLO", "SCRLC", "SCRLA", "SCR2D", "SCRCC", "SCR2DC", "SCRLW",
        "SCR2DL", "MPDSP", "VWOFT", "FADE", "FADEW", "IDLCK", "LSTMP", "SCRLP",
        "BATTLE", "BTLON", "BTLMD", "PGTDR", "GETPC", "PXYZI", "PLUSX", "PLUS2X",
        "MINUSX", "MINUS2X", "INCX", "INC2X", "DECX", "DEC2X", "TLKON", "RDMSD",
        "SETBYTE", "SETWORD", "BITON", "BITOFF", "BITXOR", "PLUS", "PLUS2", "MINUS",
        "MINUS2", "MUL", "MUL2", "DIV", "DIV2", "MOD", "MOD2", "AND",
        "AND2", "OR", "OR2", "XOR", "XOR2", "INC", "INC2", "DEC",
        "DEC2", "RANDOM", "LBYTE", "HBYTE", "TOBYTE", "SETX", "GETX", "SEARCHX",
        "PC", "CHAR_", "DFANM", "ANIME1", "VISI", "XYZI", "XYI", "XYZ",
        "MOVE", "CMOVE", "MOVA", "TURA", "ANIMW", "FMOVE", "ANIME2", "ANIMX1",
        "CANIM1", "CANMX1", "MSPED", "DIR", "TURNGEN", "TURN", "DIRA", "GETDIR",
        "GETAXY", "GETAI", "ANIMX2", "CANIM2", "CANMX2", "ASPED", "UnusedBE", "CC",
        "JUMP", "AXYZI", "LADER", "OFST", "OFSTW", "TALKR", "SLIDR", "SOLID",
        "PRTYP", "PRTYM", "PRTYE", "IFPRTYQ", "IFMEMBQ", "MMBud", "MMBLK", "MMBUK",
        "LINE", "LINON", "MPJPO", "SLINE", "SIN", "COS", "TLKR2", "SLDR2",
        "PMJMP", "PMJMP2", "AKAO2", "FCFIX", "CCANM", "ANIMB", "TURNW", "MPPAL",
        "BGON", "BGOFF", "BGROL", "BGROL2", "BGCLR", "STPAL", "LDPAL", "CPPAL",
        "RTPAL", "ADPAL", "MPPAL2", "STPLS", "LDPLS", "CPPAL2", "RTPAL2", "ADPAL2",
        "MUSIC", "SOUND", "AKAO", "MUSVT", "MUSVM", "MULCK", "BMUSC", "CHMPH",
        "PMVIE", "MOVIE", "MVIEF", "MVCAM", "FMUSC", "CMUSC", "CHMST", "GAMEOVER",
    ];

    private static readonly byte[] BaseLengths =
    [
        1, 3, 3, 3, 3, 3, 3, 2, 2, 15, 6, 6, 1, 1, 2, 2,
        2, 3, 2, 3, 6, 7, 8, 9, 8, 9, 10, 3, 6, 1, 1, 1,
        11, 2, 5, 3, 3, 9, 2, 2, 3, 1, 2, 2, 5, 7, 2, 10,
        4, 4, 4, 2, 2, 4, 5, 8, 6, 6, 6, 4, 1, 1, 1, 1,
        3, 5, 6, 2, 1, 5, 1, 5, 7, 4, 2, 2, 1, 5, 1, 5,
        10, 6, 4, 2, 2, 3, 7, 7, 5, 5, 5, 7, 8, 10, 8, 1,
        10, 2, 5, 6, 6, 1, 9, 1, 9, 2, 7, 9, 1, 4, 3, 6,
        4, 2, 3, 4, 4, 8, 4, 5, 4, 5, 3, 3, 3, 3, 2, 3,
        4, 5, 4, 4, 4, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4,
        5, 4, 5, 4, 5, 3, 3, 3, 3, 3, 4, 5, 6, 7, 7, 11,
        2, 2, 3, 3, 2, 11, 9, 9, 6, 6, 2, 4, 1, 6, 3, 3,
        5, 5, 4, 3, 6, 6, 2, 4, 5, 4, 3, 5, 5, 4, 1, 2,
        11, 8, 15, 12, 1, 3, 3, 2, 2, 2, 4, 3, 3, 3, 2, 2,
        13, 2, 2, 16, 10, 10, 4, 4, 3, 1, 15, 2, 4, 1, 1, 11,
        4, 4, 3, 3, 3, 5, 5, 5, 7, 10, 10, 5, 5, 8, 8, 11,
        2, 5, 14, 2, 2, 2, 2, 4, 2, 1, 3, 2, 2, 8, 3, 1,
    ];

    private static readonly Dictionary<int, VariableOperand[]> VariableOperands = new()
    {
        [0x09] = [new("targetX1", 4, 2, 2, 1, OperandAccess.Read), new("targetY1", 6, 2, 2, 2, OperandAccess.Read), new("direction1", 8, 1, 1, 3, OperandAccess.Read), new("targetX2", 9, 2, 2, 4, OperandAccess.Read), new("targetY2", 11, 2, 2, 5, OperandAccess.Read), new("direction2", 13, 1, 1, 6, OperandAccess.Read)], // SPLIT
        [0x0A] = [new("charID1", 3, 1, 1, 1, OperandAccess.Read), new("charID2", 4, 1, 1, 2, OperandAccess.Read), new("charID3", 5, 1, 1, 3, OperandAccess.Read)], // SPTYE
        [0x0B] = [new("varCharID1", 3, 1, 1, 1, OperandAccess.Write), new("varCharID2", 4, 1, 1, 2, OperandAccess.Write), new("varCharID3", 5, 1, 1, 3, OperandAccess.Write)], // GTPYE
        [0x14] = [new("value1", 2, 1, 1, 1, OperandAccess.Read), new("value2", 3, 1, 1, 2, OperandAccess.Read)], // IFUB
        [0x15] = [new("value1", 2, 1, 1, 1, OperandAccess.Read), new("value2", 3, 1, 1, 2, OperandAccess.Read)], // IFUBL
        [0x16] = [new("value1", 2, 2, 2, 1, OperandAccess.Read), new("value2", 4, 2, 2, 2, OperandAccess.Read)], // IFSW
        [0x17] = [new("value1", 2, 2, 2, 1, OperandAccess.Read), new("value2", 4, 2, 2, 2, OperandAccess.Read)], // IFSWL
        [0x18] = [new("value1", 2, 2, 2, 1, OperandAccess.Read), new("value2", 4, 2, 2, 2, OperandAccess.Read)], // IFUW
        [0x19] = [new("value1", 2, 2, 2, 1, OperandAccess.Read), new("value2", 4, 2, 2, 2, OperandAccess.Read)], // IFUWL
        [0x23] = [new("var", 2, 1, 1, 2, OperandAccess.Read)], // BTRLD
        [0x25] = [new("r", 4, 1, 1, 1, OperandAccess.Read), new("g", 5, 1, 1, 2, OperandAccess.Read), new("b", 6, 1, 1, 3, OperandAccess.Read), new("speed", 7, 2, 2, 4, OperandAccess.Read)], // NFADE
        [0x2C] = [new("targetZ", 3, 2, 2, 1, OperandAccess.Read)], // BGPDH
        [0x2D] = [new("targetX", 3, 2, 2, 1, OperandAccess.Read), new("targetY", 5, 2, 2, 2, OperandAccess.Read)], // BGSCR
        [0x37] = [new("value", 3, 4, 2, 1, OperandAccess.Read), new("valueHi", 5, 2, 2, 2, OperandAccess.Read)], // WNUMB
        [0x38] = [new("h", 3, 1, 1, 1, OperandAccess.Read), new("m", 4, 1, 1, 2, OperandAccess.Read), new("s", 5, 1, 1, 4, OperandAccess.Read)], // STTIM
        [0x39] = [new("value", 2, 4, 2, 1, OperandAccess.Read), new("valueHi", 4, 2, 2, 2, OperandAccess.Read)], // GOLDu
        [0x3A] = [new("value", 2, 4, 2, 1, OperandAccess.Read), new("valueHi", 4, 2, 2, 2, OperandAccess.Read)], // GOLDd
        [0x3B] = [new("var1", 2, 1, 2, 1, OperandAccess.Write), new("var2", 3, 1, 2, 2, OperandAccess.Write)], // CHGLD
        [0x41] = [new("value", 4, 1, 1, 2, OperandAccess.Read)], // MPARA
        [0x42] = [new("value", 4, 2, 2, 2, OperandAccess.Read)], // MPRA2
        [0x45] = [new("value", 3, 2, 2, 2, OperandAccess.Read)], // MPu
        [0x47] = [new("value", 3, 2, 2, 2, OperandAccess.Read)], // MPd
        [0x48] = [new("varAnswer", 6, 1, 1, 2, OperandAccess.Write)], // ASK
        [0x49] = [new("param", 3, 1, 1, 2, OperandAccess.Read)], // MENU
        [0x4D] = [new("value", 3, 2, 2, 2, OperandAccess.Read)], // HPu
        [0x4F] = [new("value", 3, 2, 2, 2, OperandAccess.Read)], // HPd
        [0x56] = [new("corner", 3, 1, 1, 1, OperandAccess.Read), new("varR", 4, 1, 1, 2, OperandAccess.Write), new("varG", 5, 1, 1, 3, OperandAccess.Write), new("varB", 6, 1, 1, 4, OperandAccess.Write)], // GWCOL
        [0x57] = [new("corner", 3, 1, 1, 1, OperandAccess.Read), new("r", 4, 1, 1, 2, OperandAccess.Read), new("g", 5, 1, 1, 3, OperandAccess.Read), new("b", 6, 1, 1, 4, OperandAccess.Read)], // SWCOL
        [0x58] = [new("itemID", 2, 2, 2, 1, OperandAccess.Read), new("quantity", 4, 1, 1, 2, OperandAccess.Read)], // STITM
        [0x59] = [new("itemID", 2, 2, 2, 1, OperandAccess.Read), new("quantity", 4, 1, 1, 2, OperandAccess.Read)], // DLITM
        [0x5A] = [new("itemID", 2, 2, 2, 1, OperandAccess.Read), new("quantity", 4, 1, 1, 2, OperandAccess.Read)], // CKITM
        [0x5B] = [new("materiaID", 3, 1, 1, 1, OperandAccess.Read), new("APCount0", 4, 1, 1, 2, OperandAccess.Read), new("APCount1", 5, 1, 1, 3, OperandAccess.Read), new("APCount2", 6, 1, 1, 4, OperandAccess.Read)], // SMTRA
        [0x5C] = [new("materiaID", 3, 1, 1, 1, OperandAccess.Read), new("APCount0", 4, 1, 1, 2, OperandAccess.Read), new("APCount1", 5, 1, 1, 3, OperandAccess.Read), new("APCount2", 6, 1, 1, 4, OperandAccess.Read)], // DMTRA
        [0x5D] = [new("materiaID", 8, 1, 1, 1, OperandAccess.Read), new("APCount0", 4, 1, 1, 2, OperandAccess.Read), new("APCount1", 5, 1, 1, 3, OperandAccess.Read), new("APCount2", 6, 1, 1, 4, OperandAccess.Read), new("APCount3", 7, 1, 1, 5, OperandAccess.Read), new("varQuantity", 9, 1, 1, 6, OperandAccess.Write)], // CMTRA
        [0x5E] = [new("xAmplitude", 4, 1, 1, 1, OperandAccess.Read), new("xFrames", 5, 1, 1, 2, OperandAccess.Read), new("yAmplitude", 6, 1, 1, 3, OperandAccess.Read), new("yFrames", 7, 1, 1, 4, OperandAccess.Read)], // SHAKE
        [0x62] = [new("speed", 2, 2, 2, 2, OperandAccess.Read)], // SCRLC
        [0x63] = [new("speed", 2, 2, 2, 2, OperandAccess.Read)], // SCRLA
        [0x64] = [new("targetX", 2, 2, 2, 1, OperandAccess.Read), new("targetY", 4, 2, 2, 2, OperandAccess.Read)], // SCR2D
        [0x66] = [new("targetX", 3, 2, 2, 1, OperandAccess.Read), new("targetY", 5, 2, 2, 2, OperandAccess.Read), new("speed", 7, 2, 2, 4, OperandAccess.Read)], // SCR2DC
        [0x68] = [new("targetX", 3, 2, 2, 1, OperandAccess.Read), new("targetY", 5, 2, 2, 2, OperandAccess.Read), new("speed", 7, 2, 2, 4, OperandAccess.Read)], // SCR2DL
        [0x6A] = [new("unknown1", 2, 2, 2, 1, OperandAccess.Read), new("unknown2", 4, 2, 2, 2, OperandAccess.Read)], // VWOFT
        [0x6B] = [new("r", 3, 1, 1, 1, OperandAccess.Read), new("g", 4, 1, 1, 2, OperandAccess.Read), new("b", 5, 1, 1, 4, OperandAccess.Read)], // FADE
        [0x6E] = [new("var", 2, 1, 2, 2, OperandAccess.Write)], // LSTMP
        [0x6F] = [new("speed", 2, 2, 2, 2, OperandAccess.Read)], // SCRLP
        [0x70] = [new("battleID", 2, 2, 2, 2, OperandAccess.Read)], // BATTLE
        [0x73] = [new("varDir", 3, 1, 1, 2, OperandAccess.Write)], // PGTDR
        [0x74] = [new("varPC", 3, 1, 1, 2, OperandAccess.Write)], // GETPC
        [0x75] = [new("varX", 4, 1, 2, 1, OperandAccess.Write), new("varY", 5, 1, 2, 2, OperandAccess.Write), new("varZ", 6, 1, 2, 3, OperandAccess.Write), new("varI", 7, 1, 2, 4, OperandAccess.Write)], // PXYZI
        [0x76] = [new("var", 2, 1, 1, 1, OperandAccess.Write), new("value", 3, 1, 1, 2, OperandAccess.Read)], // PLUSX
        [0x77] = [new("var", 2, 1, 2, 1, OperandAccess.Write), new("value", 3, 2, 2, 2, OperandAccess.Read)], // PLUS2X
        [0x78] = [new("var", 2, 1, 1, 1, OperandAccess.Write), new("value", 3, 1, 1, 2, OperandAccess.Read)], // MINUSX
        [0x79] = [new("var", 2, 1, 2, 1, OperandAccess.Write), new("value", 3, 2, 2, 2, OperandAccess.Read)], // MINUS2X
        [0x7A] = [new("var", 2, 1, 1, 2, OperandAccess.Write)], // INCX
        [0x7B] = [new("var", 2, 1, 2, 2, OperandAccess.Write)], // INC2X
        [0x7C] = [new("var", 2, 1, 1, 2, OperandAccess.Write)], // DECX
        [0x7D] = [new("var", 2, 1, 2, 2, OperandAccess.Write)], // DEC2X
        [0x7F] = [new("value", 2, 1, 2, 2, OperandAccess.Read)], // RDMSD
        [0x80] = [new("var", 2, 1, 1, 1, OperandAccess.Write), new("value", 3, 1, 1, 2, OperandAccess.Read)], // SETBYTE
        [0x81] = [new("var", 2, 1, 2, 1, OperandAccess.Write), new("value", 3, 2, 2, 2, OperandAccess.Read)], // SETWORD
        [0x82] = [new("var", 2, 1, 1, 1, OperandAccess.BitWrite), new("position", 3, 1, 1, 2, OperandAccess.Read)], // BITON
        [0x83] = [new("var", 2, 1, 1, 1, OperandAccess.BitWrite), new("position", 3, 1, 1, 2, OperandAccess.Read)], // BITOFF
        [0x84] = [new("var", 2, 1, 1, 1, OperandAccess.BitWrite), new("position", 3, 1, 1, 2, OperandAccess.Read)], // BITXOR
        [0x85] = [new("var", 2, 1, 1, 1, OperandAccess.Write), new("value", 3, 1, 1, 2, OperandAccess.Read)], // PLUS
        [0x86] = [new("var", 2, 1, 2, 1, OperandAccess.Write), new("value", 3, 2, 2, 2, OperandAccess.Read)], // PLUS2
        [0x87] = [new("var", 2, 1, 1, 1, OperandAccess.Write), new("value", 3, 1, 1, 2, OperandAccess.Read)], // MINUS
        [0x88] = [new("var", 2, 1, 2, 1, OperandAccess.Write), new("value", 3, 2, 2, 2, OperandAccess.Read)], // MINUS2
        [0x89] = [new("var", 2, 1, 1, 1, OperandAccess.Write), new("value", 3, 1, 1, 2, OperandAccess.Read)], // MUL
        [0x8A] = [new("var", 2, 1, 2, 1, OperandAccess.Write), new("value", 3, 2, 2, 2, OperandAccess.Read)], // MUL2
        [0x8B] = [new("var", 2, 1, 1, 1, OperandAccess.Write), new("value", 3, 1, 1, 2, OperandAccess.Read)], // DIV
        [0x8C] = [new("var", 2, 1, 2, 1, OperandAccess.Write), new("value", 3, 2, 2, 2, OperandAccess.Read)], // DIV2
        [0x8D] = [new("var", 2, 1, 1, 1, OperandAccess.Write), new("value", 3, 1, 1, 2, OperandAccess.Read)], // MOD
        [0x8E] = [new("var", 2, 1, 2, 1, OperandAccess.Write), new("value", 3, 2, 2, 2, OperandAccess.Read)], // MOD2
        [0x8F] = [new("var", 2, 1, 1, 1, OperandAccess.Write), new("value", 3, 1, 1, 2, OperandAccess.Read)], // AND
        [0x90] = [new("var", 2, 1, 2, 1, OperandAccess.Write), new("value", 3, 2, 2, 2, OperandAccess.Read)], // AND2
        [0x91] = [new("var", 2, 1, 1, 1, OperandAccess.Write), new("value", 3, 1, 1, 2, OperandAccess.Read)], // OR
        [0x92] = [new("var", 2, 1, 2, 1, OperandAccess.Write), new("value", 3, 2, 2, 2, OperandAccess.Read)], // OR2
        [0x93] = [new("var", 2, 1, 1, 1, OperandAccess.Write), new("value", 3, 1, 1, 2, OperandAccess.Read)], // XOR
        [0x94] = [new("var", 2, 1, 2, 1, OperandAccess.Write), new("value", 3, 2, 2, 2, OperandAccess.Read)], // XOR2
        [0x95] = [new("var", 2, 1, 1, 2, OperandAccess.Write)], // INC
        [0x96] = [new("var", 2, 1, 2, 2, OperandAccess.Write)], // INC2
        [0x97] = [new("var", 2, 1, 1, 2, OperandAccess.Write)], // DEC
        [0x98] = [new("var", 2, 1, 2, 2, OperandAccess.Write)], // DEC2
        [0x99] = [new("var", 2, 1, 1, 2, OperandAccess.Write)], // RANDOM
        [0x9A] = [new("var", 2, 1, 1, 1, OperandAccess.Write), new("value", 3, 1, 1, 2, OperandAccess.Read)], // LBYTE
        [0x9B] = [new("var", 2, 1, 2, 1, OperandAccess.Write), new("value", 3, 2, 2, 2, OperandAccess.Read)], // HBYTE
        [0x9C] = [new("var", 3, 1, 2, 1, OperandAccess.Write), new("value1", 4, 1, 1, 2, OperandAccess.Read), new("value2", 5, 1, 1, 4, OperandAccess.Read)], // TOBYTE
        [0x9D] = [new("varOrValue1", 4, 2, 2, 2, OperandAccess.Read), new("varOrValue2", 6, 1, 1, 4, OperandAccess.Read)], // SETX
        [0x9E] = [new("varOrValue1", 4, 2, 2, 2, OperandAccess.Read), new("var", 6, 1, 1, 4, OperandAccess.Write)], // GETX
        [0x9F] = [new("start", 5, 2, 2, 2, OperandAccess.Read), new("end", 7, 2, 2, 3, OperandAccess.Read), new("value", 9, 1, 1, 4, OperandAccess.Read), new("varResult", 10, 1, 2, 6, OperandAccess.Write)], // SEARCHX
        [0xA5] = [new("targetX", 3, 2, 2, 1, OperandAccess.Read), new("targetY", 5, 2, 2, 2, OperandAccess.Read), new("targetZ", 7, 2, 2, 3, OperandAccess.Read), new("targetI", 9, 2, 2, 4, OperandAccess.Read)], // XYZI
        [0xA6] = [new("targetX", 3, 2, 2, 1, OperandAccess.Read), new("targetY", 5, 2, 2, 2, OperandAccess.Read), new("targetI", 7, 2, 2, 3, OperandAccess.Read)], // XYI
        [0xA7] = [new("targetX", 3, 2, 2, 1, OperandAccess.Read), new("targetY", 5, 2, 2, 2, OperandAccess.Read), new("targetZ", 7, 2, 2, 3, OperandAccess.Read)], // XYZ
        [0xA8] = [new("targetX", 2, 2, 2, 1, OperandAccess.Read), new("targetY", 4, 2, 2, 2, OperandAccess.Read)], // MOVE
        [0xA9] = [new("targetX", 2, 2, 2, 1, OperandAccess.Read), new("targetY", 4, 2, 2, 2, OperandAccess.Read)], // CMOVE
        [0xAD] = [new("targetX", 2, 2, 2, 1, OperandAccess.Read), new("targetY", 4, 2, 2, 2, OperandAccess.Read)], // FMOVE
        [0xB2] = [new("speed", 2, 2, 2, 2, OperandAccess.Read)], // MSPED
        [0xB3] = [new("direction", 2, 1, 1, 2, OperandAccess.Read)], // DIR
        [0xB4] = [new("speed", 4, 1, 1, 2, OperandAccess.Read)], // TURNGEN
        [0xB5] = [new("speed", 4, 1, 1, 2, OperandAccess.Read)], // TURN
        [0xB7] = [new("varDir", 3, 1, 1, 2, OperandAccess.Write)], // GETDIR
        [0xB8] = [new("varX", 3, 1, 2, 1, OperandAccess.Write), new("varY", 4, 1, 2, 2, OperandAccess.Write)], // GETAXY
        [0xB9] = [new("varI", 3, 1, 2, 2, OperandAccess.Write)], // GETAI
        [0xBD] = [new("speed", 2, 2, 2, 2, OperandAccess.Read)], // ASPED
        [0xC0] = [new("targetX", 3, 2, 2, 1, OperandAccess.Read), new("targetY", 5, 2, 2, 2, OperandAccess.Read), new("targetI", 7, 2, 2, 3, OperandAccess.Read), new("height", 9, 2, 2, 4, OperandAccess.Read)], // JUMP
        [0xC1] = [new("varX", 4, 1, 2, 1, OperandAccess.Write), new("varY", 5, 1, 2, 2, OperandAccess.Write), new("varZ", 6, 1, 2, 3, OperandAccess.Write), new("varI", 7, 1, 2, 4, OperandAccess.Write)], // AXYZI
        [0xC2] = [new("targetX", 3, 2, 2, 1, OperandAccess.Read), new("targetY", 5, 2, 2, 2, OperandAccess.Read), new("targetZ", 7, 2, 2, 3, OperandAccess.Read), new("targetI", 9, 2, 2, 4, OperandAccess.Read)], // LADER
        [0xC3] = [new("targetX", 4, 2, 2, 1, OperandAccess.Read), new("targetY", 6, 2, 2, 2, OperandAccess.Read), new("targetZ", 8, 2, 2, 3, OperandAccess.Read), new("speed", 10, 2, 2, 4, OperandAccess.Read)], // OFST
        [0xC5] = [new("range", 2, 1, 1, 2, OperandAccess.Read)], // TALKR
        [0xC6] = [new("range", 2, 1, 1, 2, OperandAccess.Read)], // SLIDR
        [0xD3] = [new("targetX1", 4, 2, 2, 1, OperandAccess.Read), new("targetY1", 6, 2, 2, 2, OperandAccess.Read), new("targetZ1", 8, 2, 2, 3, OperandAccess.Read), new("targetX2", 10, 2, 2, 4, OperandAccess.Read), new("targetY2", 12, 2, 2, 5, OperandAccess.Read), new("targetZ2", 14, 2, 2, 6, OperandAccess.Read)], // SLINE
        [0xD4] = [new("value1", 3, 2, 2, 1, OperandAccess.Read), new("value2", 5, 2, 2, 2, OperandAccess.Read), new("value3", 7, 2, 2, 3, OperandAccess.Read), new("var", 9, 1, 2, 4, OperandAccess.Write)], // SIN
        [0xD5] = [new("value1", 3, 2, 2, 1, OperandAccess.Read), new("value2", 5, 2, 2, 2, OperandAccess.Read), new("value3", 7, 2, 2, 3, OperandAccess.Read), new("var", 9, 1, 2, 4, OperandAccess.Write)], // COS
        [0xD6] = [new("range", 2, 2, 2, 2, OperandAccess.Read)], // TLKR2
        [0xD7] = [new("range", 2, 2, 2, 2, OperandAccess.Read)], // SLDR2
        [0xDA] = [new("param1", 5, 2, 2, 1, OperandAccess.Read), new("param2", 7, 2, 2, 2, OperandAccess.Read), new("param3", 9, 2, 2, 3, OperandAccess.Read), new("param4", 11, 2, 2, 4, OperandAccess.Read), new("param5", 13, 2, 2, 6, OperandAccess.Read)], // AKAO2
        [0xDF] = [new("start", 6, 1, 1, 1, OperandAccess.Read), new("b", 7, 1, 1, 2, OperandAccess.Read), new("g", 8, 1, 1, 3, OperandAccess.Read), new("r", 9, 1, 1, 4, OperandAccess.Read), new("colorCount", 10, 1, 2, 6, OperandAccess.Read)], // MPPAL
        [0xE0] = [new("bgParamID", 2, 1, 1, 1, OperandAccess.Read), new("bgStateID", 3, 1, 1, 2, OperandAccess.Read)], // BGON
        [0xE1] = [new("bgParamID", 2, 1, 1, 1, OperandAccess.Read), new("bgStateID", 3, 1, 1, 2, OperandAccess.Read)], // BGOFF
        [0xE2] = [new("bgParamID", 2, 1, 1, 2, OperandAccess.Read)], // BGROL
        [0xE3] = [new("bgParamID", 2, 1, 1, 2, OperandAccess.Read)], // BGROL2
        [0xE4] = [new("bgParamID", 2, 1, 1, 2, OperandAccess.Read)], // BGCLR
        [0xE5] = [new("palID", 2, 1, 1, 1, OperandAccess.Read), new("position", 3, 1, 1, 2, OperandAccess.Read)], // STPAL
        [0xE6] = [new("position", 2, 1, 1, 1, OperandAccess.Read), new("palID", 3, 1, 1, 2, OperandAccess.Read)], // LDPAL
        [0xE7] = [new("posSrc", 2, 1, 1, 1, OperandAccess.Read), new("posDst", 3, 1, 1, 2, OperandAccess.Read)], // CPPAL
        [0xE8] = [new("posSrc", 3, 1, 1, 1, OperandAccess.Read), new("posDst", 4, 1, 1, 2, OperandAccess.Read), new("start", 5, 1, 1, 4, OperandAccess.Read)], // RTPAL
        [0xE9] = [new("posSrc", 4, 1, 1, 1, OperandAccess.Read), new("posDst", 5, 1, 1, 2, OperandAccess.Read), new("b", 6, 1, 1, 3, OperandAccess.Read), new("g", 7, 1, 1, 4, OperandAccess.Read), new("r", 8, 1, 1, 5, OperandAccess.Read)], // ADPAL
        [0xEA] = [new("posSrc", 4, 1, 1, 1, OperandAccess.Read), new("posDst", 5, 1, 1, 2, OperandAccess.Read), new("b", 6, 1, 1, 3, OperandAccess.Read), new("g", 7, 1, 1, 4, OperandAccess.Read), new("r", 8, 1, 1, 5, OperandAccess.Read)], // MPPAL2
        [0xED] = [new("posSrc", 5, 1, 1, 1, OperandAccess.Read), new("posDst", 6, 1, 1, 2, OperandAccess.Read), new("colorCount", 7, 1, 1, 4, OperandAccess.Read)], // CPPAL2
        [0xEE] = [new("posSrc", 5, 1, 1, 1, OperandAccess.Read), new("posDst", 6, 1, 1, 2, OperandAccess.Read), new("start", 7, 1, 1, 4, OperandAccess.Read)], // RTPAL2
        [0xEF] = [new("start", 6, 1, 1, 1, OperandAccess.Read), new("b", 7, 1, 1, 2, OperandAccess.Read), new("g", 8, 1, 1, 3, OperandAccess.Read), new("r", 9, 1, 1, 4, OperandAccess.Read), new("colorCount", 10, 1, 1, 6, OperandAccess.Read)], // ADPAL2
        [0xF1] = [new("soundID", 2, 2, 2, 1, OperandAccess.Read), new("position", 4, 1, 1, 2, OperandAccess.Read)], // SOUND
        [0xF2] = [new("param1", 5, 1, 1, 1, OperandAccess.Read), new("param2", 6, 2, 2, 2, OperandAccess.Read), new("param3", 8, 2, 2, 3, OperandAccess.Read), new("param4", 10, 2, 2, 4, OperandAccess.Read), new("param5", 12, 2, 2, 6, OperandAccess.Read)], // AKAO
        [0xF7] = [new("var1", 2, 1, 1, 1, OperandAccess.Write), new("var2", 3, 1, 1, 2, OperandAccess.Write)], // CHMPH
        [0xFA] = [new("varCurMovieFrame", 2, 1, 1, 2, OperandAccess.Read)], // MVIEF
        [0xFD] = [new("param1", 4, 2, 1, 1, OperandAccess.Read), new("param2", 6, 2, 1, 2, OperandAccess.Read)], // CMUSC
        [0xFE] = [new("var", 2, 1, 1, 2, OperandAccess.Write)], // CHMST
    };

    /// <summary>
    /// Makou's own descriptions say these three store into their variable ("Stores the
    /// result of the last battle in", "= amount of item ... in the inventory", "Save Movie
    /// frame in"), but its variable table marks the operand as read-only. The engine
    /// writes them, so the audit does too.
    /// </summary>
    public static readonly IReadOnlyList<string> VariableTableCorrections =
    [
        "BTRLD.var: Read -> Write, word helper (Makou text; native 0062002C calls 0061031E(2,2))",
        "CKITM.quantity: Read -> Write (Makou text; native 0061E70B calls 0060FA7D(2,4))",
        "MVIEF.varCurMovieFrame: Read -> Write, word helper (Makou text; native 0061A438 calls 0061031E(2,2))",
        "HBYTE.var: word -> byte destination (native 00610A3C calls 0060FA7D(1,2))",
        "GETX.var: operand 6 -> 5 (native 00610C63 calls 0060FA7D(4,5))",
        "CHMPH.var1: byte -> word destination (native 0061FC23 calls 0061031E(1,2))",
        "SPECIAL F7 (GMSPD): byte destination nibble 4 operand 3 (native 0061E78C calls 0060FA7D(4,3))",
        "SPECIAL lengths: F6 = 6, F7 = 4 (native 0061E78C); both decoders had 3",
        "0x1A/0x1B/0x1C: unimplemented on PC (native 006107E1 returns without advancing)",
        "word helpers write two bytes only on word banks 2/4/6/12/14/7 (native 0061031E, reader 0060FD6C)",
        "BITON/BITOFF/BITXOR change bit (position & 31) of the one byte; positions 8-31 change nothing (native 00611098)"
    ];

    static Opcodes()
    {
        VariableOperands[BTRLD] = [new("var", 2, 1, 2, 2, OperandAccess.Write)];
        VariableOperands[CKITM] =
        [
            new("itemID", 2, 2, 2, 1, OperandAccess.Read),
            new("quantity", 4, 1, 1, 2, OperandAccess.Write)
        ];
        VariableOperands[MVIEF] = [new("varCurMovieFrame", 2, 1, 2, 2, OperandAccess.Write)];
        VariableOperands[0x9B] =
        [
            new("var", 2, 1, 1, 1, OperandAccess.Write),
            new("value", 3, 2, 2, 2, OperandAccess.Read)
        ];
        VariableOperands[GETX] =
        [
            new("varOrValue1", 4, 2, 2, 2, OperandAccess.Read),
            new("var", 5, 1, 1, 4, OperandAccess.Write)
        ];
        VariableOperands[0xF7] =
        [
            new("var1", 2, 1, 2, 1, OperandAccess.Write),
            new("var2", 3, 1, 1, 2, OperandAccess.Write)
        ];
    }

    /// <summary>The banks a word helper reads or writes two bytes of; the others take one.</summary>
    public static bool IsWordBank(int bank) => bank is 2 or 4 or 6 or 12 or 14 or 7;

    public static string Name(byte opcode) => Names[opcode];

    public static int BaseLength(byte opcode) => BaseLengths[opcode];

    public static bool IsUndefined(byte opcode) => UndefinedOpcodes.Contains(opcode);

    public static bool IsRawMemory(byte opcode) => RawMemoryOpcodes.Contains(opcode);

    public static IReadOnlyList<VariableOperand> Variables(byte opcode) =>
        VariableOperands.TryGetValue(opcode, out var operands) ? operands : [];

    public static string DisplayName(byte opcode, ReadOnlySpan<byte> bytes)
    {
        if (opcode == SPECIAL && bytes.Length >= 2)
        {
            return SpecialNames.TryGetValue(bytes[1], out var sub)
                ? $"SPECIAL.{sub}"
                : $"SPECIAL.{bytes[1]:X2}?";
        }

        return Names[opcode];
    }

    /// <summary>
    /// The length of the opcode at <paramref name="offset"/>, or null when it cannot be
    /// decoded from the bytes that are there. <paramref name="issue"/> names anything
    /// unusual about a length that could be decoded.
    /// </summary>
    public static int? GetLength(ReadOnlySpan<byte> code, int offset, out string? issue)
    {
        issue = null;
        if (offset < 0 || offset >= code.Length)
        {
            issue = "offset outside code";
            return null;
        }

        var opcode = code[offset];
        int length = BaseLengths[opcode];
        switch (opcode)
        {
            case MEMWRITE:
                if (code.Length - offset < 6)
                {
                    issue = "0x1C header truncated";
                    return null;
                }

                length += Math.Min(code[offset + 5], (byte)128);
                break;
            case KAWAI:
                if (code.Length - offset < 2)
                {
                    issue = "KAWAI size byte truncated";
                    return null;
                }

                length = Math.Max(1, (int)code[offset + 1]);
                if (length < 3)
                {
                    issue = $"KAWAI declares size {code[offset + 1]}";
                }

                break;
            case SPECIAL:
                if (code.Length - offset < 2)
                {
                    issue = "SPECIAL sub-opcode truncated";
                    return null;
                }

                // Native 0061E78C: F5=3, F6=6, F7=4, F8=4, F9=2, FA=2, FB=3, FC=3, FD=4,
                // FE=2, FF=2, anything else 2. Makou and the shipping catalog give F6 and F7 3.
                var sub = code[offset + 1];
                length += sub switch
                {
                    0xF5 or 0xFB or 0xFC => 1,
                    0xF7 or 0xF8 or 0xFD => 2,
                    0xF6 => 4,
                    _ => 0
                };
                if (sub < 0xF5)
                {
                    issue = $"SPECIAL sub-opcode {sub:X2} is undefined";
                }

                break;
        }

        return length;
    }

    public static FlowKind Flow(byte opcode) => opcode switch
    {
        RET => FlowKind.Return,
        RETTO => FlowKind.ReturnTo,
        REQ or REQSW or REQEW => FlowKind.Call,
        PREQ or PRQSW or PRQEW => FlowKind.PartyCall,
        JMPF or JMPFL or JMPB or JMPBL => FlowKind.Jump,
        IFUB or IFUBL or IFSW or IFSWL or IFUW or IFUWL or
            IFKEY or IFKEYON or IFKEYOFF or IFPRTYQ or IFMEMBQ => FlowKind.ConditionalJump,
        SAVEMAPCOPY or IFNANAKI or MEMWRITE => FlowKind.Stall,
        GAMEOVER => FlowKind.GameOver,
        MAPJUMP => FlowKind.LeavesField,
        _ => FlowKind.Normal
    };

    /// <summary>
    /// Where the jump operand sits in the opcode. Makou measures every forward jump from
    /// that byte (Opcode::jumpShift) and every backward one from the opcode itself; the
    /// shipping walker agrees for all of them except JMPFL, which it measures from byte 2.
    /// </summary>
    public static bool TryGetJumpShape(byte opcode, out int operandOffset, out bool isLong, out bool isBack)
    {
        (operandOffset, isLong, isBack) = opcode switch
        {
            JMPF => (1, false, false),
            JMPFL => (1, true, false),
            IFNANAKI => (1, true, false),
            JMPB => (1, false, true),
            JMPBL => (1, true, true),
            IFUB => (5, false, false),
            IFUBL => (5, true, false),
            IFSW or IFUW => (7, false, false),
            IFSWL or IFUWL => (7, true, false),
            IFKEY or IFKEYON or IFKEYOFF => (3, false, false),
            IFPRTYQ or IFMEMBQ => (2, false, false),
            _ => (-1, false, false)
        };
        return operandOffset >= 0;
    }

    /// <summary>
    /// The absolute target of a jump at <paramref name="offset"/>. Forward jumps land at
    /// the operand's position plus its value; backward jumps at the opcode minus its value.
    /// </summary>
    public static bool TryGetJumpTarget(byte opcode, ReadOnlySpan<byte> bytes, int offset, out int target)
    {
        target = -1;
        if (!TryGetJumpShape(opcode, out var operandOffset, out var isLong, out var isBack) ||
            bytes.Length < operandOffset + (isLong ? 2 : 1))
        {
            return false;
        }

        var value = isLong
            ? bytes[operandOffset] | (bytes[operandOffset + 1] << 8)
            : bytes[operandOffset];
        target = isBack ? offset - value : offset + operandOffset + value;
        return true;
    }

    /// <summary>
    /// The block a bank nibble addresses: 1-5 are the five savemap blocks, "T" the
    /// field's temporary block, and null the immediate (0) or an unmapped nibble.
    /// </summary>
    public static string? BankBlock(int bank) => bank switch
    {
        1 or 2 => "1",
        3 or 4 => "2",
        11 or 12 => "3",
        13 or 14 => "4",
        15 or 7 => "5",
        5 or 6 => "T",
        _ => null
    };

    /// <summary>The savemap offset of a block's first byte (0x0BA4 is GameMoment).</summary>
    public static int? SavemapOffset(string block) => block switch
    {
        "1" => 0x0BA4,
        "2" => 0x0CA4,
        "3" => 0x0DA4,
        "4" => 0x0EA4,
        "5" => 0x0FA4,
        _ => null
    };

    public static int BankNibble(ReadOnlySpan<byte> bytes, int bankOffset, int nibble)
    {
        var index = bankOffset + (nibble - 1) / 2;
        if (index >= bytes.Length)
        {
            return -1;
        }

        return nibble % 2 == 1 ? bytes[index] >> 4 : bytes[index] & 0x0F;
    }

    /// <summary>
    /// Where an opcode's bank bytes start. Every opcode with variables keeps them at byte
    /// 1 except CMUSC, whose first argument comes first.
    /// </summary>
    public static int BankOffset(byte opcode) => opcode == 0xFD ? 2 : 1;
}
