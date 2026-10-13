using System.Security.Cryptography;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

[Flags]
public enum PostscriptBattleAssistFeatures { None = 0, Effectiveness = 1, StealMorph = 2, AutoSense = 4 }
public readonly record struct PostscriptBattleAssistOwner(bool IsValid, int ActorSlot, int SelectedTarget, int FormationId,
    int TargetInstanceId, int TargetMask, int CommandType, int TargetMode, int AnchorX, int AnchorY, PostscriptBattleAssistFeatures Features);

// Reads displayed presentation ownership and static patch code, never hidden enemy data.
public sealed class PostscriptBattleAssistReader
{
    private readonly ILegacyAddressSpace memory;
    private const int MaximumHelpLength = 512;
    public PostscriptBattleAssistReader(ILegacyAddressSpace memory) => this.memory = memory ?? throw new ArgumentNullException(nameof(memory));

    public bool TryReadAssistOwner(out PostscriptBattleAssistOwner owner)
    {
        owner = default;
        try
        {
            var first = ReadOwner();
            var second = ReadOwner();
            if (first is null || first != second) return false;
            owner = first.Value;
            return true;
        }
        catch { return false; }
    }

    // The caller must supply this witness only within the validated renderer 0x17
    // callback, AFTER its original draw returned. No background pointer polling.
    public bool TryReadDisplayedHpHelp(bool renderer17Observed, out string text)
    {
        text = string.Empty;
        if (!renderer17Observed) return false;
        try
        {
            if (!HelpRendererWitness() || !TryReadAssistOwner(out var before) ||
                !before.Features.HasFlag(PostscriptBattleAssistFeatures.AutoSense) ||
                !memory.TryReadUInt16(0xDC0E12, out var config) || (config & 0x4000) == 0 ||
                !memory.TryReadUInt32(0xDC208C, out var pointer) || pointer == 0 ||
                !ReadTerminated(pointer, out var bytes)) return false;
            var decoded = Ff7EncodedTextDecoder.DecodeTerminated(bytes).Trim();
            if (string.IsNullOrWhiteSpace(decoded) ||
                !TryReadAssistOwner(out var after) || before != after ||
                !memory.TryReadUInt16(0xDC0E12, out var configAfter) || config != configAfter ||
                !memory.TryReadUInt32(0xDC208C, out var pointerAfter) || pointer != pointerAfter ||
                !ReadTerminated(pointer, out var bookend) || !bytes.AsSpan().SequenceEqual(bookend) ||
                !HelpRendererWitness()) return false;
            text = decoded;
            return true;
        }
        catch { return false; }
    }

    private PostscriptBattleAssistOwner? ReadOwner()
    {
        var features = ReadFeatures();
        if (features == PostscriptBattleAssistFeatures.None ||
            !memory.TryReadByte(BattleStateReader.AddressCurrentModule, out var module) || module != 2 ||
            !memory.TryReadByte(0xDC3C7C, out var actor) || actor > 2 ||
            !memory.TryReadInt32(0xDC3C9C, out var target) || target is < 4 or > 9 ||
            !memory.TryReadUInt16(0x9AB0C8, out var formation) || formation >= 1024 ||
            !memory.TryReadByte((uint)(0x9AB0DC + target * 0x68 + 8), out var instance) || instance == 255 ||
            !memory.TryReadUInt16(0xDC3C60, out var mask) || (mask & (1 << target)) == 0 ||
            !memory.TryReadInt32(0xDC3C70, out var command) || command is < 0 or > 127 ||
            !memory.TryReadInt32(0xDC3C90, out var mode) || (mode & 4) != 0 ||
            !memory.TryReadInt32(0xDC38D0, out var invalid) || invalid != 0 ||
            !memory.TryReadByte(0xBFB2EC, out var blocked) || blocked != 0 ||
            !memory.TryReadUInt16(0x91EF9C, out var menu) || menu != 0) return null;
        int x = -1, y = -1;
        if (features.HasFlag(PostscriptBattleAssistFeatures.Effectiveness))
        {
            // Ghidra 6D1B88 -> 6D1B5C -> 6D163D passes selectedTarget as rendererActor.
            if (memory.TryReadUInt16((uint)(0xBE1190 + target * 0x1AEC), out var visible) &&
                memory.TryReadUInt16((uint)(0xBF2E20 + target * 8), out var rawX) &&
                memory.TryReadUInt16((uint)(0xBF2E22 + target * 8), out var rawY))
            {
                x = ((short)rawX - (visible == 0 ? 48 : 0)) & 0xFFFE;
                y = (short)rawY & 0xFFFE;
            }
            if (x is < 0 or > 591 || y is < 32 or > 331) features &= ~PostscriptBattleAssistFeatures.Effectiveness;
        }
        return features == PostscriptBattleAssistFeatures.None ? null : new(true, actor, target, formation, instance, mask, command, mode, x, y, features);
    }

    private PostscriptBattleAssistFeatures ReadFeatures()
    {
        // Stable body witnesses are deliberately past entry bytes patched by our draw hooks.
        if (!Matches(0x6F5B17, "837D10007505")) return PostscriptBattleAssistFeatures.None;
        var result = PostscriptBattleAssistFeatures.None;
        if ((Matches(0x6D0D57, "E8A4490E0090") && Hash(0x7B56D6, 586, "0b86418e3c8a8d854387e6a8bf5607ee23bf6e141885d64e7b5af5bfbcd2b3b9")) ||
            (Matches(0x6D0D57, "E85B490E0090") && Hash(0x7B56B7, 613, "2da5b93aef9bb46e47b9901cb11cfa5d602b89483456ccbc26adab2d6b628813"))) result |= PostscriptBattleAssistFeatures.Effectiveness;
        if (Matches(0x419BB2, "E9BEC44F009090"))
        {
            var lootEntry = Hash(0x916075, 77, "316eed1113e234ce4565f7b94d012e0ce9253159a5a92c64ae44a56179d520da");
            if (lootEntry && Hash(0x9160D4, 433, "94005114325af5979f39d21bb36f8b3e7a7e2efbef589bdac13183abfb81f34d") &&
                Hash(0x916420, 209, "7c0895dde0f63c3442f9c4e80356ff3a68858f3b9b6fdc826e5173bca603ab54")) result |= PostscriptBattleAssistFeatures.StealMorph;
            if (Matches(0x4198D2, "909090909090") && (lootEntry ||
                Hash(0x916075, 77, "f6ff49be1f24e4d4ce4af1b25a3326fe10871b438511ef837df20c0ec88a13f3"))) result |= PostscriptBattleAssistFeatures.AutoSense;
        }
        return result;
    }
    private bool HelpRendererWitness() => Matches(0x6D1CC9, "A15C10DC008945E48B0D6010DC00898DD8FEFFFF66C745");
    private bool Matches(uint address, string hex)
    {
        var expected = Convert.FromHexString(hex);
        var actual = new byte[expected.Length];
        return memory.TryRead(address, actual) && actual.AsSpan().SequenceEqual(expected);
    }
    private bool Hash(uint address, int length, string expected)
    {
        var bytes = new byte[length];
        return memory.TryRead(address, bytes) && string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expected, StringComparison.OrdinalIgnoreCase);
    }
    private bool ReadTerminated(uint pointer, out byte[] bytes)
    {
        bytes = [];
        var result = new List<byte>();
        for (var index = 0; index < MaximumHelpLength; index++)
        {
            if ((ulong)pointer + (uint)index > uint.MaxValue || !memory.TryReadByte(pointer + (uint)index, out var value)) return false;
            result.Add(value);
            if (value == 255) { bytes = result.ToArray(); return true; }
        }
        return false;
    }
}
