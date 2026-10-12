using System.Text;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class LateGameMotionActionDescriptionTests
{
    // Independent native boundaries and ModelLoader bindings. Changing an animation
    // slot while leaving the instruction bytes intact must fail the archive checks.
    private static readonly (int Field, int Entity, int Script, int Byte, string Bytes,
        int Model, string Hrc, int Animation, string Resource)[] Expected =
    [
        (741, 13, 1, 152, "BA0501", 8, "ANBD.HRC", 5, "HYAB.tak"),
        (741, 13, 1, 504, "A20601", 8, "ANBD.HRC", 6, "HYAC.tak"),
        (741, 13, 1, 628, "A20701", 8, "ANBD.HRC", 7, "HYAD.tak"),
        (763, 2, 10, 47, "B10B001301", 0, "AAAA.HRC", 11, "HYCE.chi"),
        (763, 2, 13, 26, "AF0D01", 0, "AAAA.HRC", 13, "HYCF.chi"),
        (763, 2, 15, 0, "B10F000701", 0, "AAAA.HRC", 15, "AJDC.aki"),
        (767, 3, 6, 0, "AF0601", 0, "BHFF.HRC", 6, "EVEF.chi"),
        (767, 3, 6, 19, "AF0701", 0, "BHFF.HRC", 7, "ETAA.yos")
    ];

    public static void Run()
    {
        var cues = FieldLateGameMotionActionDescriptions.CreateAll();
        Require(cues.Count == Expected.Length, "eight distinct reviewed actions");
        Require(cues.Select(c => c.Key).Distinct().Count() == cues.Count, "unique native boundaries");
        var old = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions();
        foreach (var e in Expected)
        {
            var key = new FieldCutsceneDescriptionKey(e.Field, e.Entity, e.Script, e.Byte);
            var cue = cues.Single(c => c.Key == key);
            Require(cue.Opcode == Convert.ToByte(e.Bytes[..2], 16), "matching delivered opcode");
            Require(!cue.IsRecurring && !cue.ObservationOnly, "story actions require executed callback");
            Require(FieldLateGameMotionActionDescriptions.RequiresVisibleActor(key), "native actor visibility required at delivery");
            Require(old.Count(c => c.Key == key) <= 1, "no duplicate composition");
            Require(!old.Any(c => c.Key == key && c.Text != cue.Text), "no earlier action overwritten");
            var tracker = new FieldCutsceneDescriptionTracker(cues);
            var context = new FieldScriptContext(e.Field, e.Entity, e.Script, e.Byte, (byte)cue.Opcode);
            Require(tracker.Observe(context)?.Text == cue.Text, "first actual callback delivers action");
            for (var frame = 0; frame < 120; frame++)
                Require(tracker.Observe(context) is null, "yielding animation must speak only once");
            tracker.Reset();
            Require(tracker.Observe(context with { Opcode = 0x24 }) is null, "wrong opcode stays silent");
            Require(tracker.Observe(context with { ByteIndex = e.Byte + 1 }) is null, "interior byte stays silent");
            Require(tracker.Observe(context)?.Text == cue.Text, "real callback after noise still delivers");
        }
        // Hojo repeats these same gestures on this conversation's path. One reviewed
        // boundary per gesture is intentional; the adjacent copies must stay silent.
        foreach (var index in new[] { 231, 329, 425, 495, 644 })
            Require(!cues.Any(c => c.FieldId == 741 && c.EntityId == 13 && c.ByteIndex == index),
                "repeated neighbouring gesture stays silent");
        Require(!cues.Any(c => c.ScriptId == 0), "no unproven init-continuation boundary");
        Console.WriteLine("Late native motion cues: boundaries, callback identity and yield dedup passed.");
    }

    public static void RunAgainstInstalledArchive()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            Console.WriteLine("Late native motion bindings: installed archive unavailable; skipped.");
            return;
        }
        var scripts = new FieldScriptNavigationCatalog(root);
        var source = new FlevelDataSource(root);
        foreach (var e in Expected)
        {
            var anchor = scripts.ReadScriptOpcodes(e.Field, e.Entity, e.Script)
                .Single(op => op.ByteIndex == e.Byte);
            Require(Convert.ToHexString(anchor.Bytes.ToArray()) == e.Bytes, "exact native instruction bytes");
            var init = scripts.ReadScriptOpcodes(e.Field, e.Entity, 0);
            Require(init.Any(op => op.Opcode == 0xA1 && op.Bytes.Count == 2 && op.Bytes[1] == e.Model),
                "CHAR binds this entity to the reviewed model slot");
            Require(source.TryReadField(e.Field, out var encoded), "native field available");
            var field = Ff7LzsDecoder.DecodeFieldFile(encoded);
            var bindings = ReadModelBindings(field);
            Require(bindings[e.Model].Hrc == e.Hrc, "reviewed skeleton identity");
            Require(bindings[e.Model].Animations[e.Animation] == e.Resource, "reviewed native animation identity");
        }
        // Cloud remains visible after CHAR in both final Crater fields: no actor
        // script changes visibility or rebinds his model. Hojo's defeated branch
        // is different and must be checked explicitly, not treated as VISI01.
        foreach (var (field, entity) in new[] { (763, 2), (767, 3) })
            for (var script = 0; script < 32; script++)
                Require(!scripts.ReadScriptOpcodes(field, entity, script).Any(op => op.Opcode == 0xA4),
                    "reviewed Cloud actor has no hidden branch");
        var hojoInit = scripts.ReadScriptOpcodes(741, 13, 0);
        foreach (var (index, bytes) in new[] { (16, "14F080040907"), (22, "7E01"), (24, "C701"), (26, "A400"), (28, "00") })
            Require(Convert.ToHexString(hojoInit.Single(op => op.ByteIndex == index).Bytes.ToArray()) == bytes,
                "only defeated Hojo init branch hides/disables Talk");
        var talk = scripts.ReadScriptOpcodes(741, 13, 1);
        Require(talk.Where(op => op.Opcode == 0xA4).All(op => op.ByteIndex == 704),
            "all Hojo gestures precede the only post-battle hide");
        Require(Convert.ToHexString(talk.Single(op => op.ByteIndex == 706).Bytes.ToArray()) == "82F08004",
            "post-battle hide sets the exact bit tested in init");
        // No native request bypasses the hidden branch's disabled Talk surface.
        for (var entity = 0; entity < 14; entity++)
        for (var script = 0; script < 32; script++)
            Require(!scripts.ReadScriptOpcodes(741, entity, script).Any(op =>
                op.Opcode is >= 0x01 and <= 0x06 && op.Bytes.Count == 3
                && op.Bytes[1] == 13 && (op.Bytes[2] & 31) == 1), "Hojo Talk has no scripted hidden entry");
        Console.WriteLine($"Late native motion bindings: {Expected.Length} checked against {root}.");
    }

    internal static List<(string Hrc, string[] Animations)> ReadModelBindings(byte[] field)
    {
        var section = BitConverter.ToInt32(field, 6 + 2 * sizeof(int));
        var end = checked(section + 4 + BitConverter.ToInt32(field, section));
        var count = BitConverter.ToUInt16(field, section + 6);
        var position = section + 10;
        var models = new List<(string, string[])>();
        for (var model = 0; model < count; model++)
        {
            var nameLength = BitConverter.ToUInt16(field, position);
            position = checked(position + 2 + nameLength);
            Require(position + 46 <= end, "bounded model tail");
            var hrc = Encoding.ASCII.GetString(field, position + 2, 8).TrimEnd('\0');
            var animations = new string[BitConverter.ToUInt16(field, position + 14)];
            position += 46;
            for (var animation = 0; animation < animations.Length; animation++)
            {
                var length = BitConverter.ToUInt16(field, position);
                position += 2;
                Require(position + length + 2 <= end, "bounded animation record");
                animations[animation] = Encoding.ASCII.GetString(field, position, length).TrimEnd('\0');
                position += length + 2;
            }
            models.Add((hrc, animations));
        }
        return models;
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException($"Late native motion descriptions: {message}");
    }
}
