using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class StoryNativeBindingTests
{
    // Recorded from installed PC LINE instructions. StoryCoverageAudit also compares
    // these bindings with both licensed archives, so this portable regression suite
    // does not mistake its frozen fixture for fresh native verification.
    public static void Run()
    {
        Check(132, "ln0", "Escape Reactor 5's core", 6, -27,-1049,-184, -147,-1049,-184);
        Check(258, "ln2", "Follow the blood trail out of the cell block", 9, 406,751,0, 418,628,0);
        Check(729, null, "Jump on past the second carriage", 9, 839,548,154, 766,574,139);
        Check(729, null, "Take on the third carriage", 10, 861,784,164, 758,756,138);
        Check(729, null, "Jump on past the third carriage", 11, 834,800,161, 758,835,133);
        Check(729, null, "Take on the fourth carriage", 12, 858,1019,161, 754,999,137);
        Check(747, "mjump", null, 32, -714,607,920, -595,498,988);
        Check(749, "down_8", null, 29, 500,1,-693, 500,126,-693);
        Check(749, "down_9", null, 33, 5,119,-902, 5,-9,-902);
        Check(749, "down_10", null, 38, -7,98,-278, -7,26,-278);
        Check(749, "m_jump", null, 40, -72,127,-1041, -72,-17,-1041);
        Check(751, "lad_2_d", null, 15, -2649,-290,4830, -2600,-160,4814);
        CheckObject(747, "tre1", 34, 98, 84, 64);
        CheckObject(747, "tre2", 35, 72, 83, 64);
        CheckObject(747, "save", 37);
        CheckObject(749, "box_1", 41, 74, 148, 2);
        CheckObject(749, "box_2", 42, 18, 148, 4);
        CheckObject(749, "save", 44);
        CheckObject(757, "box_1", 24, 73, 147, 2);
        CheckObject(757, "box_2", 25, 15, 147, 4);
        CheckObject(757, "save", 27);
    }

    private static void Check(int field, string? source, string? label, int entity,
        int x1, int y1, int z1, int x2, int y2, int z2)
    {
        var rows = FieldStoryEventCatalog.CreateAllFields().Where(r => r.FieldId == field &&
            (source is null || r.SourceEntityName == source) && (label is null || r.Label == label)).ToArray();
        if (rows.Length != 1)
            throw new InvalidOperationException($"Native Story binding {field}/{source}/{label}: expected one row, got {rows.Length}.");
        var row = rows[0];
        var expected = new FieldNavigationTriggerLine(x1,y1,z1,x2,y2,z2);
        if (row.RequiredEnabledLineEntityId != entity || row.EntityId != entity || row.TriggerLine != expected)
            throw new InvalidOperationException($"Native Story binding {field}/{source}/{label}: wrong LINE entity or geometry; expected entity {entity}, {expected}; got entity {row.RequiredEnabledLineEntityId}, {row.TriggerLine}.");
    }

    private static void CheckObject(int field, string name, int entity, int item = -1,
        int collectedAddress = -1, byte collectedMask = 0)
    {
        var rows = FieldNavigationObjectCatalog.CreateAllFields()
            .Where(r => r.FieldId == field && r.SourceEntityName == name).ToArray();
        if (rows.Length != 1 || rows[0].EntityId != entity ||
            rows[0].TargetKind != FieldNavigationObjectTargetKind.Model)
            throw new InvalidOperationException($"Native object binding {field}/{name}: expected model entity {entity}.");
        var row = rows[0];
        if (item < 0)
        {
            if (row.Kind != FieldNavigationObjectKind.SavePoint)
                throw new InvalidOperationException($"Native object binding {field}/{name}: expected Save Point.");
        }
        else if (row.Kind != FieldNavigationObjectKind.Item || row.NativeId != item ||
                 row.CollectedBank != 15 || row.CollectedAddress != collectedAddress ||
                 row.CollectedMask != collectedMask)
            throw new InvalidOperationException($"Native object binding {field}/{name}: pickup or collected flag differs from its native Talk script.");
    }
}
