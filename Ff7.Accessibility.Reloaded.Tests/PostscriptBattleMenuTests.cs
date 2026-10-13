using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
namespace Ff7.Accessibility.Reloaded.Tests;

internal static class PostscriptBattleMenuTests
{
    internal static void Run()
    {
        ReadsVisiblePhsRowsInsteadOfMagic();
        ReadsLearnedLimitRowsAndNativeGaugeAvailability();
        RejectsUnrecognizedAndRacingMenuOwnership();
        PreservesStockAbilityRows();
        ReadsNativeCustomRootCommandLabels();
        SpeaksDifferentMenusThatReuseTheSameRenderer();
        Console.WriteLine("PASS Postscript battle PHS and Limit menus");
    }
    private static void ReadsVisiblePhsRowsInsteadOfMagic()
    {
        var m = Setup(6, 29); var r = Reader(m);
        var s = r.ReadMenuState(6);
        Equal("Red XIII", s.Selection?.Name, "PHS reads the visible character, never a magic record");
        Equal(3, s.Selection?.EntryId, "PHS native 3-column cursor index");
        Equal(true, s.Selection?.IsAvailable, "healthy reserve is available");
        Equal(null, s.Selection?.MpCost, "PHS does not expose stale spell MP");
        m.U16(0xDBFDB8 + 3 * 0x84, 0);
        Equal(false, r.ReadMenuState(6).Selection?.IsAvailable, "KO reserve is greyed out");
        m.U16(0xDBFDB8 + 3 * 0x84, 100); m.Byte(0xDC0232, 3);
        Equal(false, r.ReadMenuState(6).Selection?.IsAvailable, "current party member is greyed out");
        m.U16(0xDC0DDE, 0);
        Equal(null, r.ReadMenuState(6).Selection, "hidden PHS character is silent");
    }
    private static void ReadsLearnedLimitRowsAndNativeGaugeAvailability()
    {
        var m = Setup(4, 30); var r = Reader(m); var s = r.ReadMenuState(4);
        Equal("Cross-slash", s.Selection?.Name, "custom Limit ID comes from native character limit table");
        Equal(1, s.Selection?.EntryId, "native limit name index");
        Equal(true, s.Selection?.IsAvailable, "higher current level affords low-level limit");
        Equal(null, s.Selection?.MpCost, "Limit does not expose Enemy Skill MP");
        m.Byte(0x9A8DC0 + 0x34, 63);
        Equal(false, r.ReadMenuState(4).Selection?.IsAvailable, "one gauge unit below native threshold is unavailable");
        m.U16(0xDBFDAE, 0);
        Equal(null, r.ReadMenuState(4).Selection, "unlearned limit has no rendered row");
        m.U16(0xDBFDAE, 2); m.I32(0xDC2184 + 0x700, 3); m.I32(0xDC2180 + 0x700, 1);
        Equal(null, r.ReadMenuState(4).Selection, "eighth custom Limit slot is not rendered");
    }
    private static void RejectsUnrecognizedAndRacingMenuOwnership()
    {
        Action<Memory>[] bad = [
            m=>m.Byte(0x6D9528,0x90),m=>m.Byte(0x9A7A21,0x90),
            m=>m.Byte(0xDC3C70+1,1),m=>m.Byte(0xDC3C70,31),m=>m.Byte(0xDC3C74,4),
            m=>m.U16(0x91EF9C,4),m=>m.Byte(0xDC3C7C,3),
            m=>m.I32(0xDC2110+0x700,3),m=>m.I32(0xDC2114+0x700,-1),
            m=>m.I32(0xDC2124+0x700,int.MaxValue),
            m=>m.BeforeRead=(a,n)=>{if(a==0x9AB0DC+0x68+0x2C&&n==1)m.Byte(0xDC3C70,2);},
            m=>m.BeforeRead=(a,n)=>{if(a==0xDC3C70&&n==2)m.Byte(a,2);},
            m=>m.BeforeRead=(a,n)=>{if(a==0xDC0231&&n==2)m.Byte(a,1);},
            m=>m.BeforeRead=(a,n)=>{if(a==0xDC0DDE&&n==2)m.U16(a,0);}
        ];
        foreach (var corrupt in bad) { var m = Setup(6, 29); corrupt(m); var s = Reader(m).ReadMenuState(6); Equal(false, s.IsValid && s.Selection is not null, "unknown or torn PHS must not become stock magic speech"); }
        var limit = Setup(4, 30); limit.Byte(0x916A51, 0x90);
        Equal(null, Reader(limit).ReadMenuState(4).Selection, "unknown Limit renderer must not become Enemy Skill speech");
    }
    private static void PreservesStockAbilityRows()
    {
        var m = Setup(6, 2); m.Byte(0xDC3C74, 1);
        Equal("Fire", Reader(m).ReadMenuState(6).Selection?.Name, "recognized patch keeps ordinary Magic semantics");
        var stock = Setup(1, 2); stock.Byte(0x6D9528, 0x90);
        Equal(true, Reader(stock).ReadMenuState(1).IsValid, "stock root actor remains readable without a command-name resolver");
    }
    private static void ReadsNativeCustomRootCommandLabels()
    {
        var m = Setup(1, 29); m.Byte(0xDBA4B9 + 0x440, 1); m.I32(0xDC20A0 + 0x700, 0); m.I32(0xDC20A4 + 0x700, 0);
        m.Byte(0xDBA4E4 + 0x440, 29); m.Text(0x91A960, "PHS", 12); m.Text(0x91A938, "Limit", 12);
        var conflictingKernel = new BattleStateReader(m, new SavemapPartyReader(m), resolveCommandName: _ => "Different kernel name");
        Equal("PHS", conflictingKernel.ReadMenuState(1).Selection?.Name, "displayed native PHS label wins over conflicting kernel data");
        Equal("PHS", Reader(m).ReadMenuState(1).Selection?.Name, "custom root PHS reads actual patched native label");
        m.Byte(0xDBA4E4 + 0x440, 30);
        Equal("Limit", conflictingKernel.ReadMenuState(1).Selection?.Name, "displayed native Limit label wins over conflicting kernel data");
        Equal("Limit", Reader(m).ReadMenuState(1).Selection?.Name, "custom root Limit reads actual patched native label");
    }
    private static void SpeaksDifferentMenusThatReuseTheSameRenderer()
    {
        var speech = new BattleMenuFrameSpeechCoordinator();
        var m = Setup(6, 29); var phs = Reader(m).ReadMenuState(6);
        speech.BeginFrame(6); speech.CompleteFrame(phs);
        Equal("Cloud. Red XIII", speech.Poll(), "first PHS selection is announced");
        speech.BeginFrame(6); speech.CompleteFrame(phs); Equal(null, speech.Poll(), "unchanged PHS does not repeat");
        var magic = phs with { Selection = phs.Selection!.Value with { CommandId = -1 } };
        speech.BeginFrame(6); speech.CompleteFrame(magic);
        Equal("Red XIII", speech.Poll(), "different native submenu speaks even if it has identical text and index");
        speech.BeginFrame(6); speech.CompleteFrame(magic with { Selection = null }); Equal(null, speech.Poll(), "omitted native row is silent");
        speech.BeginFrame(6); speech.CompleteFrame(magic); Equal("Red XIII", speech.Poll(), "returning from an omitted row reannounces the visible selection");
    }
    private static BattleStateReader Reader(Memory m) => new(m, new SavemapPartyReader(m),
        resolveAbilityName: _ => "Fire", resolveLimitName: id => id == 1 ? "Cross-slash" : null);
    private static Memory Setup(int renderer, int command)
    {
        var m = new Memory(); m.Byte(0xCBF9DC, 2); m.Byte(0xDC3C7C, 1); m.Byte(0xDC2068 + renderer, 2);
        m.I32(0xDC3C70, command); m.Byte(0xDC3C74, (byte)(renderer == 6 ? 1 : 4)); m.U16(0x91EF9C, (ushort)renderer);
        m.Write(0x6D9528, Convert.FromHexString("8A0C10880D703CDC008A44100180F91D7C1031C088C883E81D6BC003409090909090A2743CDC00"));
        m.Write(0x9A7A21, Convert.FromHexString("833D703CDC001D740F0F1F400081F9FF000000"));
        m.Write(0x916A51, Convert.FromHexString("833D703CDC001E740E0F1F40008B45D431C9"));
        m.Byte(0xDC0230, 2); m.Byte(0xDC0231, 0); m.Byte(0xDC0232, 8);
        for (int c = 0; c < 9; c++) { m.Fill(0xDBFD8C + c * 0x84, 0x84, 0); m.Byte(0xDBFD8C + c * 0x84, (byte)c); m.Text(0xDBFD9C + c * 0x84, c == 0 ? "Cloud" : c == 3 ? "Red XIII" : "Member", 12); m.U16(0xDBFDB8 + c * 0x84, 100); }
        m.Fill(0x9AB0DC + 0x68, 0x68, 0); m.Byte(0x9AB0DC + 0x68 + 8, 0); m.I32(0x9AB0DC + 0x68 + 0x2C, 100); m.I32(0x9AB0DC + 0x68 + 0x30, 100);
        m.U16(0xDC0DDE, 0x1FF);
        m.I32(0xDC2110 + 0x700, 0); m.I32(0xDC2114 + 0x700, 1); m.I32(0xDC2124 + 0x700, 0);
        m.Fill(0xDBA5A0 + 0x440 + 3 * 8, 8, 0); // Deliberately plausible stale Magic row.
        m.I32(0xDC2180 + 0x700, 1); m.I32(0xDC2184 + 0x700, 0); m.I32(0xDC2194 + 0x700, 0);
        m.Fill(0xDBA7E0 + 0x440 + 8, 8, 0); // Deliberately plausible stale Enemy Skill row.
        m.Byte(0xDBFD9A, 4); m.U16(0xDBFDAE, 2); m.Byte(0x99CE1D, 0x81);
        m.U16(0x99CE38, 100); m.U16(0x99CE44, 400); m.Byte(0x9A8DC0 + 0x34, 64);
        return m;
    }
    private static void Equal<T>(T want, T got, string why) { if (!EqualityComparer<T>.Default.Equals(want, got)) throw new InvalidOperationException($"{why}: expected {want}, got {got}"); }
    private sealed class Memory : ILegacyAddressSpace
    {
        private readonly Dictionary<int, byte> bytes = new(); private readonly Dictionary<int, int> reads = new();
        internal Action<int, int>? BeforeRead;
        internal void Byte(int a, byte b) => bytes[a] = b;
        internal void U16(int a, ushort b) => Write(a, BitConverter.GetBytes(b));
        internal void I32(int a, int b) => Write(a, BitConverter.GetBytes(b));
        internal void Fill(int a, int n, byte b) => Write(a, Enumerable.Repeat(b, n).ToArray());
        internal void Text(int a, string text, int n) { Fill(a, n, 255); Write(a, text.Select(c => (byte)(c - 32)).ToArray()); }
        internal void Write(int a, byte[] b) { for (int i = 0; i < b.Length; i++) Byte(a + i, b[i]); }
        public bool TryRead(uint address, Span<byte> target) { int a = (int)address; reads.TryGetValue(a, out var n); reads[a] = ++n; BeforeRead?.Invoke(a, n); for (int i = 0; i < target.Length; i++) { if (!bytes.TryGetValue(a + i, out var b)) return false; target[i] = b; } return true; }
    }
}








