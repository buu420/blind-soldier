using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class LifestreamMotionActionDescriptionTests
{
    private static readonly (int Field, int Entity, int Script, int Byte, string Bytes,
        int Model, int Animation, string Resource)[] Expected =
    [
        (723, 2, 3, 4, "A20301", 1, 3, "HUJA.tak"),
        (727, 6, 9, 0, "BC0C000E01", 5, 12, "AHDA.tak"),
        (727, 6, 10, 0, "AF0D01", 5, 13, "HTIE.tak"),
        (727, 6, 11, 4, "AF0E01", 5, 14, "HPAD.yos")
    ];
    public static void Run()
    {
        var cues = FieldLifestreamMotionActionDescriptions.CreateAll();
        Require(cues.Count == 4 && cues.Select(c => c.Key).Distinct().Count() == 4, "four distinct native actions");
        foreach (var e in Expected)
        {
            var key = new FieldCutsceneDescriptionKey(e.Field, e.Entity, e.Script, e.Byte);
            var cue = cues.Single(c => c.Key == key);
            Require(cue.Opcode == Convert.ToByte(e.Bytes[..2], 16) && !cue.ObservationOnly && !cue.IsRecurring,
                "native actor animation boundary, never arrival/timer evidence");
            Require(FieldLifestreamMotionActionDescriptions.RequiresVisibleActor(key), "hidden memory actor requires live visibility proof");
            var tracker = new FieldCutsceneDescriptionTracker(cues);
            var context = new FieldScriptContext(e.Field, e.Entity, e.Script, e.Byte, (byte)cue.Opcode);
            Require(tracker.Observe(context)?.Text == cue.Text, "actual executed motion delivered");
            for (var frame = 0; frame < 240; frame++)
                Require(tracker.Observe(context) is null, "animation yield does not repeat narration");
        }
        Require(!FieldLifestreamMotionActionDescriptions.RequiresVisibleActor(new(723, 0, 0, 0)), "area cue never reclassified as actor motion");
        Console.WriteLine("Lifestream native motion boundaries and yield dedup passed; visible-actor delivery guard required.");
    }
    public static void RunAgainstInstalledArchive()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            Console.WriteLine("Lifestream native motion: installed archive unavailable; skipped."); return;
        }
        var scripts = new FieldScriptNavigationCatalog(root); var source = new FlevelDataSource(root);
        foreach (var e in Expected)
        {
            var anchor = scripts.ReadScriptOpcodes(e.Field, e.Entity, e.Script).Single(op => op.ByteIndex == e.Byte);
            Require(Convert.ToHexString(anchor.Bytes.ToArray()) == e.Bytes, "exact instruction and explicit playback range");
            Require(scripts.ReadScriptOpcodes(e.Field, e.Entity, 0).Any(op => op.Opcode == 0xA1
                && op.Bytes.Count == 2 && op.Bytes[1] == e.Model), "native actor ModelLoader binding");
            Require(source.TryReadField(e.Field, out var encoded), "native field available");
            var model = LateGameMotionActionDescriptionTests.ReadModelBindings(Ff7LzsDecoder.DecodeFieldFile(encoded))[e.Model];
            Require(model.Hrc == "AAGB.HRC" && model.Animations[e.Animation] == e.Resource, "exact reviewed Tifa skeleton/animation");
        }
        foreach (var (index, bytes) in new[] { (13, "030669"), (103, "0306AA"), (122, "0306AB") })
            Require(Convert.ToHexString(scripts.ReadScriptOpcodes(727, 1, 1)
                .Single(op => op.ByteIndex == index).Bytes.ToArray()) == bytes, "conversation requests each reviewed motion in order");
        Require(Convert.ToHexString(scripts.ReadScriptOpcodes(727, 6, 5)
            .Single(op => op.ByteIndex == 16).Bytes.ToArray()) == "A400", "separate hidden branch remains explicit; default CHAR alone is insufficient");
        Console.WriteLine($"Lifestream native motion: four script/animation bindings checked against {root}.");
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Lifestream motion: " + message);
    }
}
