using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using System.Text.Json;
namespace Ff7.Accessibility.Reloaded.Tests;

internal static class PostscriptBattleAssistTests
{
    internal static void Run()
    {
        AssemblesActualLootDrawsAndDeduplicatesPresentations();
        RejectsWrongDrawSourcePositionAndPatch();
        SpeaksEffectivenessAtTheVerifiedTargetAnchor();
        ReadsOnlyDisplayedCoherentAutoSenseHelp();
        Console.WriteLine("PASS Postscript battle assists");
    }
    private static readonly PostscriptBattleAssistOwner Owner = new(true, 0, 4, 324, 16, 16, 1, 0, 100, 100,
        PostscriptBattleAssistFeatures.StealMorph | PostscriptBattleAssistFeatures.Effectiveness | PostscriptBattleAssistFeatures.AutoSense);
    private static void AssemblesActualLootDrawsAndDeduplicatesPresentations()
    {
        var now = new DateTime(2026, 10, 13, 1, 0, 0, DateTimeKind.Utc); var t = new PostscriptBattleAssistSpeechTracker(() => now);
        t.ObserveDraw("B", new("Steal", 56, 256, 7, int.MaxValue), Owner);
        t.ObserveDraw("B", new("Potion", 127, 256, 7, -1), Owner);
        t.ObserveDraw("B", new("Morph", 336, 256, 7, 0), Owner);
        t.ObserveDraw("B", new("Ether", 421, 256, 7, int.MinValue), Owner);
        now = now.AddMilliseconds(100);
        Equal("Steal: Potion. Morph: Ether", t.Poll(Owner), "only actually drawn loot labels and values are paired");
        Equal(null, t.Poll(Owner), "same presentation does not repeat");
        t.Poll(null);
        t.ObserveDraw("B", new("Steal", 56, 256, 7, 0), Owner); t.ObserveDraw("B", new("Potion", 127, 256, 7, 0), Owner);
        now = now.AddMilliseconds(100); Equal("Steal: Potion", t.Poll(Owner), "target re-entry reannounces the visible information");
        now = now.AddSeconds(1); Equal(null, t.Poll(Owner), "old draw expires");
        t.ObserveDraw("B", new("Potion", 127, 256, 7, 0), Owner); now = now.AddMilliseconds(100); Equal(null, t.Poll(Owner), "expired label never combines with a fresh value");
    }
    private static void RejectsWrongDrawSourcePositionAndPatch()
    {
        var now = new DateTime(2026, 10, 13, 1, 0, 0, DateTimeKind.Utc);
        foreach (var bad in new[] { new MenuTextRenderEntry("Potion", 127, 255, 7, 0), new("Potion", 127, 256, 0, 0), new("Potion", 640, 256, 7, 0) })
        { var t = new PostscriptBattleAssistSpeechTracker(() => now); t.ObserveDraw("B", new("Steal", 56, 256, 7, 0), Owner); t.ObserveDraw("B", bad, Owner); now = now.AddMilliseconds(100); Equal(null, t.Poll(Owner), "unrelated draw cannot become target loot"); }
        var wrong = new PostscriptBattleAssistSpeechTracker(() => now); wrong.ObserveDraw("A", new("Weak", 148, 100, 7, 0), Owner); now = now.AddMilliseconds(100); Equal(null, wrong.Poll(Owner), "source A is not the injected renderer");
        wrong.ObserveDraw("B", new("Weak", 148, 100, 7, 0), Owner with { Features = PostscriptBattleAssistFeatures.None }); now = now.AddMilliseconds(100); Equal(null, wrong.Poll(Owner), "stock and unknown patches produce no assist speech");
        wrong.ObserveDraw("B", new("Weak", 148, 100, 7, 0), Owner); now = now.AddMilliseconds(100); Equal(null, wrong.Poll(Owner with { SelectedTarget = 5 }), "owner transition rejects prior target text");
    }
    private static void SpeaksEffectivenessAtTheVerifiedTargetAnchor()
    {
        var now = new DateTime(2026, 10, 13, 1, 0, 0, DateTimeKind.Utc); var t = new PostscriptBattleAssistSpeechTracker(() => now);
        t.ObserveDraw("B", new("Weak", 149, 100, 7, 0), Owner);
        t.ObserveDraw("B", new("Status Immune", 100, 116, 7, 0), Owner);
        t.ObserveDraw("B", new("Resists", 148, 100, 7, 0), Owner);
        now = now.AddMilliseconds(100); Equal("Resists. Status Immune", t.Poll(Owner), "actual effectiveness and status line share verified native target anchor");
        t.ObserveDisplayedHpHelp("Guard Scorpion. Level 12. HP 800/800", Owner);
        now = now.AddMilliseconds(100); Equal("Guard Scorpion. Level 12. HP 800/800. Resists. Status Immune", t.Poll(Owner), "already formatted visible HP is coalesced");
    }
    private static void ReadsOnlyDisplayedCoherentAutoSenseHelp()
    {
        var empty = new Memory(); var reader = new PostscriptBattleAssistReader(empty);
        Equal(false, reader.TryReadAssistOwner(out _), "unreadable native patch remains silent");
        Equal(false, reader.TryReadDisplayedHpHelp(false, out _), "no renderer17 witness never reads help");
        var fixtures = Environment.GetEnvironmentVariable("FF7_POSTSCRIPT_PATCH_FIXTURES");
        if (string.IsNullOrWhiteSpace(fixtures)) { Console.WriteLine("Postscript assist positive native hash fixtures not configured; pure draw/negative cases run."); return; }
        var m = NativeMemory(fixtures); reader = new(m);
        Equal(true, reader.TryReadAssistOwner(out var owner), "known private injected hashes identify actual target presentation");
        Equal(4, owner.SelectedTarget, "native selected enemy");
        Equal(PostscriptBattleAssistFeatures.Effectiveness | PostscriptBattleAssistFeatures.StealMorph | PostscriptBattleAssistFeatures.AutoSense,
            owner.Features, "each feature requires its exact injected native hash");
        Equal(100, owner.AnchorX, "native renderer actor anchor");
        m.U16(0xBE1190 + 4 * 0x1AEC, 0); m.U16(0xBF2E20 + 4 * 8, 149); m.U16(0xBF2E22 + 4 * 8, 101);
        Equal(true, reader.TryReadAssistOwner(out owner), "native invisible marker adjustment is readable");
        Equal(100, owner.AnchorX, "native subtract48 and even alignment are both applied");
        Equal(100, owner.AnchorY, "native y even alignment");
        var unknown = NativeMemory(fixtures); unknown.Byte(0x7B56D6 + 585, 0);
        Equal(true, new PostscriptBattleAssistReader(unknown).TryReadAssistOwner(out var unknownOwner), "other independently known assists remain readable");
        Equal(false, unknownOwner.Features.HasFlag(PostscriptBattleAssistFeatures.Effectiveness), "mutation anywhere in injected effectiveness routine disables that feature");
        unknown = NativeMemory(fixtures); unknown.Byte(0x916420 + 208, 0);
        Equal(true, new PostscriptBattleAssistReader(unknown).TryReadAssistOwner(out unknownOwner), "other native features remain independently checked");
        Equal(false, unknownOwner.Features.HasFlag(PostscriptBattleAssistFeatures.StealMorph), "mutation of injected loot subroutine disables loot speech");
        Equal(true, reader.TryReadDisplayedHpHelp(true, out var text), "renderer17 reads the already formatted help buffer");
        Equal("Guard Scorpion. Level 12. HP 800/800", text, "native displayed text, no raw hidden HP");
        m.U16(0xDC0E12, 0); Equal(false, reader.TryReadDisplayedHpHelp(true, out _), "disabled native help is silent"); m.U16(0xDC0E12, 0x4000);
        m.I32(0xDC3C90, 4); Equal(false, reader.TryReadDisplayedHpHelp(true, out _), "group selection does not expose individual HP"); m.I32(0xDC3C90, 0);
        var pointerReads = 0; m.Text(0x9A8300, "Different displayed target");
        m.BeforeRead = (a, _) => { if (a == 0xDC208C && ++pointerReads == 2) m.I32(a, 0x9A8300); };
        Equal(false, reader.TryReadDisplayedHpHelp(true, out _), "torn displayed pointer is rejected");
        m = NativeMemory(fixtures); reader = new(m); m.Write(0x9A80F0, Enumerable.Repeat((byte)0, 512).ToArray());
        Equal(false, reader.TryReadDisplayedHpHelp(true, out _), "unterminated display text fails at the bound");
        m = NativeMemory(fixtures); reader = new(m);
        var bufferReads = 0; m.BeforeRead = (a, _) => { if (a == 0x9A80F0 && ++bufferReads == 2) m.Text(a, "Changed displayed text"); };
        Equal(false, reader.TryReadDisplayedHpHelp(true, out _), "in-place displayed text change fails the content bookend");
        m = NativeMemory(fixtures, true); reader = new(m);
        Equal(true, reader.TryReadAssistOwner(out owner), "SenseRequired native injection is independently recognized");
        Equal(true, owner.Features.HasFlag(PostscriptBattleAssistFeatures.Effectiveness), "SenseRequired only permits actual rendered effectiveness observations");
    }
    private static Memory NativeMemory(string path, bool senseRequired = false)
    {
        var m = new Memory(); using var json = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var r in json.RootElement.EnumerateArray()) { var f = r.GetProperty("file").GetString()!; if (f.Contains(senseRequired ? "ShowWeakness\\SenseRequired" : "ShowWeakness\\Always") || f.Contains("UnlimitedAutoSense\\ShowStealAndMorph\\")) m.Write(r.GetProperty("address").GetInt32(), Convert.FromHexString(r.GetProperty("hex").GetString()!)); }
        m.Write(0x6F5B17, Convert.FromHexString("837D10007505"));
        m.Write(0x6D1CC9, Convert.FromHexString("A15C10DC008945E48B0D6010DC00898DD8FEFFFF66C745"));
        m.U16(0xBE1190 + 4 * 0x1AEC, 1); m.U16(0xBF2E20 + 4 * 8, 100); m.U16(0xBF2E22 + 4 * 8, 100);
        m.Byte(0xCBF9DC, 2); m.Byte(0xDC3C7C, 0); m.I32(0xDC3C9C, 4); m.U16(0x9AB0C8, 324); m.Byte(0x9AB0DC + 4 * 0x68 + 8, 16);
        m.U16(0xDC3C60, 16); m.I32(0xDC3C70, 1); m.I32(0xDC3C90, 0); m.I32(0xDC38D0, 0); m.Byte(0xBFB2EC, 0); m.U16(0x91EF9C, 0);
        m.U16(0xDC0E12, 0x4000); m.I32(0xDC208C, 0x9A80F0); m.Text(0x9A80F0, "Guard Scorpion. Level 12. HP 800/800");
        return m;
    }
    private static void Equal<T>(T expected, T actual, string message) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{message}: expected {expected}, got {actual}"); }
    private sealed class Memory : ILegacyAddressSpace
    {
        private readonly Dictionary<int, byte> bytes = new(); private readonly Dictionary<int, int> counts = new(); internal Action<int, int>? BeforeRead;
        internal void Byte(int a, byte v) => bytes[a] = v;
        internal void U16(int a, ushort v) => Write(a, BitConverter.GetBytes(v)); internal void I32(int a, int v) => Write(a, BitConverter.GetBytes(v));
        internal void Text(int a, string value) => Write(a, value.Select(c => (byte)(c - 32)).Append((byte)255).ToArray());
        internal void Write(int a, byte[] value) { for (int i = 0; i < value.Length; i++) Byte(a + i, value[i]); }
        public bool TryRead(uint address, Span<byte> value) { var a = (int)address; if (a >= 0x9A8E9C && a < 0x9A90C4 || a == 0x9AB0DC + 4 * 0x68 + 0x2C) throw new InvalidOperationException("Never read hidden enemy loot/weakness/HP"); counts.TryGetValue(a, out var n); counts[a] = ++n; BeforeRead?.Invoke(a, n); for (int i = 0; i < value.Length; i++) { if (!bytes.TryGetValue(a + i, out var b)) return false; value[i] = b; } return true; }
    }
}



