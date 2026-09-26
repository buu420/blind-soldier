using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class Sector5BedroomNpcTests
{
    internal static void Run()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            Console.WriteLine("Sector 5 native bedroom routes skipped: FF7_ACCESSIBILITY_DATA_ROOT is not set.");
            return;
        }
        var catalog = new FieldScriptNavigationCatalog(root);
        const int field = 175, events = 0x02404000;
        var native = catalog.ReadField(field).Npcs;
        var model = events + FieldNavigationObjectReader.FieldEventDataStride;
        var currentEntity = 5;
        var sleeping = true;
        var enabled = true;
        var visible = true;
        byte Byte(int a) => a == FieldPositionReader.AddressFieldNumModels ? (byte)2 :
            a == FieldNavigationObjectReader.AddressFieldModelIdArray + currentEntity ? (byte)1 :
            a == model + FieldNavigationObjectReader.VisibilityOffset && visible ? (byte)1 : (byte)0;
        short Short(int a) => a == events + FieldNavigationNpcReader.CollisionRadiusOffset ? (short)30 :
            a == model + FieldNavigationNpcReader.TalkRadiusOffset ? (short)80 : (short)0;
        int Int(int a) => a == FieldNavigationObjectReader.AddressFieldEventDataPtr ? events :
            a == model + FieldNavigationObjectReader.PositionXOffset ? (sleeping ? 174 : 0) << 12 :
            a == model + FieldNavigationObjectReader.PositionYOffset ? (sleeping ? -139 : -49) << 12 :
            a == model + FieldNavigationObjectReader.PositionZOffset ? (sleeping ? -65 : -166) << 12 : 0;
        var reader = new FieldNavigationNpcReader(Int, Short, Byte, (_, _) => [], _ => native, isLineEnabled: e => e == 7 && enabled);
        var position = new FieldPositionSnapshot(1, field, 0, -89, 299, -166, 33, 0);
        FieldNavigationTarget Target() => reader.ReadTargets(position).Single(t => t.StableId == $"npc:{field}:{currentEntity}");
        var boy = Target();
        Check(boy.TriggerEntityId == 7 && boy.TriggerLine is not null, "sleeping boy must use the enabled bedside interaction");
        Check(boy.Label == "Boy", "retain the visible character label");
        sleeping = false;
        enabled = false;
        var awake = Target();
        Check(awake.TriggerLine is null && awake.TriggerEntityId == 5 && awake.X == 0 && awake.Y == -49,
            "once the bedside line is off, the boy's own Talk still offers his later reward");
        currentEntity = 4;
        sleeping = true;
        enabled = true;
        var adult = Target();
        Check(adult.TriggerEntityId == 7 && adult.TriggerLine == boy.TriggerLine, "later bed occupant uses the same native line");
        enabled = false;
        Check(reader.ReadTargets(position).Count == 0, "adult's empty Talk cannot replace a disabled bedside line");
        enabled = true;
        visible = false;
        Check(reader.ReadTargets(position).Count == 0, "hidden bed occupant is not offered");

        var init = catalog.ReadScriptOpcodes(field, 7, 0).Single(o => o.Opcode == 0xD0).Bytes.ToArray();
        var line = boy.TriggerLine!.Value;
        Check(line.StartX == BitConverter.ToInt16(init, 1) && line.StartY == BitConverter.ToInt16(init, 3) &&
            line.StartZ == BitConverter.ToInt16(init, 5) && line.EndX == BitConverter.ToInt16(init, 7) &&
            line.EndY == BitConverter.ToInt16(init, 9) && line.EndZ == BitConverter.ToInt16(init, 11), "proxy matches native BLINE");
        var source = new FlevelDataSource(root);
        Check(source.TryReadField(field, out var encoded), "bedroom data is available");
        var data = Ff7LzsDecoder.DecodeFieldFile(encoded);
        Check(BitConverter.ToInt16(data, BitConverter.ToInt32(data, 6) + 4 + 8) == 512,
            "native scale preserves the tested default collision and Talk ranges");
        const int pointer = 0x02000000;
        int DataInt(int a) => a == FieldWalkmeshReader.AddressFieldDataPtr ? pointer :
            a >= pointer && a + 4 <= pointer + data.Length ? BitConverter.ToInt32(data, a - pointer) : 0;
        short DataShort(int a) => a >= pointer && a + 2 <= pointer + data.Length ? BitConverter.ToInt16(data, a - pointer) : (short)0;
        var planner = new FieldWalkmeshRoutePlanner(new FieldWalkmeshReader(DataInt, DataShort));
        foreach (var target in new[] { boy, awake, adult })
        {
            Check(planner.TryBuildRoute(position, target, out var plan), $"actual reader target reaches interaction: {planner.LastDiagnostic}");
            if (target.TriggerLine is not null)
            {
                var dx = line.EndX - (double)line.StartX;
                var dy = line.EndY - (double)line.StartY;
                var q = ((plan.FinalApproach.X - line.StartX) * dx + (plan.FinalApproach.Y - line.StartY) * dy) / (dx * dx + dy * dy);
                var distanceSquared = Math.Pow(plan.FinalApproach.X - line.StartX - q * dx, 2) +
                    Math.Pow(plan.FinalApproach.Y - line.StartY - q * dy, 2) + Math.Pow(plan.FinalApproach.Z - line.StartZ, 2);
                Check(q >= 0 && q <= 1 && distanceSquared < 30 * 30, "bedside approach reaches the native three-dimensional activation range");
            }
        }
        Console.WriteLine("Sector 5 bedside and later standing conversations passed with native routes.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Sector 5 bedroom: " + message);
    }
}
