using Ff7.Accessibility.Reloaded;

namespace GuideRouteAudit;

// Only manually traced entry sequences belong here. Do not turn every JUMP into
// an arrival: most jumps require a later interaction or a different story state.
internal static class ReviewedEntryLandings
{
    internal sealed record Landing(int X, int Y, ushort Triangle, string Evidence);

    internal static Landing? For(int field, int source, int triangle,
        IReadOnlyList<FieldScriptDefinition> scripts)
    {
        if (field == 213 && source == 212 && triangle == 0)
        {
            // colne_b3 director watches GETAI for triangles 0/1 and requests
            // Cloud's drop before ordinary movement can leave the arrival island.
            RequireBytes(scripts, 0, 0, "8160060000");
            RequireBytes(scripts, 0, 0, "8160080100");
            RequireBytes(scripts, 0, 0, "0101C3");
            RequireJump(scripts, 1, 3, -600, -246, 2);
            RequireBytes(scripts, 1, 3, "3300");
            return new(-600, -246, 2,
                "colne_b3 e0 Main GETAI t0/t1 -> e1 s3: JUMP(-600,-246,t2), then UC on");
        }
        if (field == 252 && source == 253 && triangle == 5)
        {
            // Returning from the vent always lands on the toilet's trigger
            // triangle. The director requests Cloud's jump down to the floor.
            RequireBytes(scripts, 0, 0, "8160020500");
            RequireBytes(scripts, 0, 0, "0101C4");
            RequireJump(scripts, 1, 4, 75, 138, 18);
            RequireBytes(scripts, 1, 4, "3300");
            return new(75, 138, 18,
                "blin66_3 e0 Main GETAI t5 -> e1 s4: JUMP(75,138,t18), then UC on");
        }
        if (field == 378 && source == 367 && triangle == 45)
        {
            // junpb_2 director Main disables control, then requests cl_hei s4
            // at moment 406, or party 0 s4 otherwise. All four controlled actor
            // branches land here before restoring control. The incoming point
            // on the ladder is never an ordinary walking start.
            foreach (var entity in new[] { 5, 6, 7, 8 })
                RequireJump(scripts, entity, 4, -205, 245, 33);
            return new(-205, 245, 33,
                "junpb_2 e2 Main -> e8 s4 or PRQEW party0 s4 (e5/e6/e7): JUMP(-205,245,t33), then UC on");
        }
        if (field == 729 && source == 728 && triangle == 82)
        {
            // zcoal_2 Cid Main always jumps from the transferred position onto
            // the first carriage, then enables control. No battle flag gates it.
            RequireJump(scripts, 3, 0, 790, 200, 0);
            return new(790, 200, 0,
                "zcoal_2 e3 Main: UC off; JUMP(790,200,t0); animation; UC on");
        }
        if (field == 620 && source == 621 && triangle == 25)
        {
            // anfrst_1 polls the leader's triangle after Init and always requests
            // these two drops from its elevated incoming t25/t26. Unlike ujp0's
            // later plant jumps, this arrival requires no direction or puzzle key.
            RequireBytes(scripts, 0, 0, "8160351900");
            RequireBytes(scripts, 0, 0, "8160371A00");
            RequireBytes(scripts, 0, 0, "7566660003050700");
            RequireBytes(scripts, 0, 0, "1666350000000003");
            RequireBytes(scripts, 0, 0, "166637000000000C");
            RequireBytes(scripts, 0, 0, "0600D9");
            RequireBytes(scripts, 0, 0, "3300");
            foreach (var entity in new[] { 4, 5, 6 })
            {
                RequireJump(scripts, entity, 25, 1090, -1106, 28);
                RequireJump(scripts, entity, 25, 1083, -1122, 29);
            }
            return new(1083, -1122, 29,
                "anfrst_1 e0 Main PXYZI t25/t26 -> leader s25 (e4/e5/e6): JUMP t28 then (1083,-1122,t29), then UC on");
        }
        if (field == 764 && (source == 755 && triangle == 9 || source == 759 && triangle == 158))
        {
            // las4_1 Init disables control for either incoming upper branch.
            // Director Main waits for Cloud's corresponding entry jump, then
            // restores control. These are not optional ledge jumps further on.
            var script = source == 755 ? 3 : 4;
            var x = source == 755 ? -342 : 373;
            var y = source == 755 ? 638 : -448;
            ushort landingTriangle = source == 755 ? (ushort)16 : (ushort)108;
            RequireBytes(scripts, 0, 0, source == 755 ? "030243" : "030244");
            RequireBytes(scripts, 0, 0, "3300");
            RequireJump(scripts, 2, script, x, y, landingTriangle);
            return new(x, y, landingTriangle,
                $"las4_1 e0 Main LSTMP {source} -> e2 s{script}: JUMP({x},{y},t{landingTriangle}), then UC on");
        }
        return null;
    }

    private static void RequireBytes(IReadOnlyList<FieldScriptDefinition> scripts,
        int entity, int script, string hex)
    {
        var expected = Convert.FromHexString(hex);
        if (!scripts.Where(s => s.EntityId == entity && s.ScriptId == script)
                .SelectMany(s => s.Opcodes).Any(o => o.Bytes.SequenceEqual(expected)))
            throw new InvalidDataException($"Reviewed entry prerequisite changed in e{entity} s{script}: {hex}.");
    }

    private static void RequireJump(IReadOnlyList<FieldScriptDefinition> scripts,
        int entity, int script, int x, int y, int triangle)
    {
        var found = scripts.Where(s => s.EntityId == entity && s.ScriptId == script)
            .SelectMany(s => s.Opcodes).Any(o => o.Opcode == 0xC0 && o.Bytes.Count == 11 &&
                o.Bytes[1] == 0 && o.Bytes[2] == 0 &&
                unchecked((short)(o.Bytes[3] | o.Bytes[4] << 8)) == x &&
                unchecked((short)(o.Bytes[5] | o.Bytes[6] << 8)) == y &&
                (o.Bytes[7] | o.Bytes[8] << 8) == triangle);
        if (!found)
            throw new InvalidDataException($"Reviewed entry landing no longer matches e{entity} s{script}: ({x},{y}) t{triangle}.");
    }
}
