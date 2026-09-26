using Ff7.Accessibility.Reloaded;

namespace GuideRouteAudit;

internal static class EndpointEvidence
{
    internal static bool Reaches(FieldNavigationTarget target, int x, int y, int z, int triangle, bool talk, int? nativeLineRadius = null)
    {
        if (target.CompletionTriangles is { Count: > 0 } triangles) return triangles.Contains(triangle);
        var dx = x - (double)target.X;
        var dy = y - (double)target.Y;
        var dz = z - (double)target.Z;
        var horizontal = Math.Sqrt(dx * dx + dy * dy);
        if (target.TriggerLine is { } line)
        {
            // 00637ABB and gateway routine 00637EBA both test the player's radius,
            // even when navigation deliberately aims at the line itself (radius 0).
            var squared = NativeLineDistanceSquared(x, y, z, line);
            if (squared < 0) return false;
            if (target.LineActivationRadius > 0) return squared < (long)target.LineActivationRadius * target.LineActivationRadius;
            return nativeLineRadius is { } radius ? radius > 0 && squared < (long)radius * radius :
                squared <= Math.Pow(Math.Max(2, target.InteractionRadius), 2);
        }
        // Ghidra 00636284: strict vertical band and strict Talk distance.
        if (talk) return horizontal < target.InteractionRadius && dz > -256 && dz < 256;
        // 00637724 uses the narrower Contact band; ContactReach already supplies
        // the safe horizontal threshold used by the production planner.
        if (target.Activation == FieldNavigationActivation.Contact)
            return horizontal <= target.InteractionRadius && dz > -127 && dz < 128;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz) <= Math.Max(2, target.InteractionRadius);
    }

    private static int NativeLineDistanceSquared(int x, int y, int z, FieldNavigationTriggerLine line)
    {
        // 00637879 uses signed 32-bit arithmetic and an 8-bit fixed-point
        // fraction, then bounds the projected X/Y rather than the fraction.
        // A floating-point projection just beyond 0 or 1 falsely rejects
        // several native doorway endpoints that quantize onto the segment.
        unchecked
        {
            var dx = line.EndX - line.StartX;
            var dy = line.EndY - line.StartY;
            var dz = line.EndZ - line.StartZ;
            var denominator = dx * dx + dy * dy + dz * dz;
            var numerator = ((x - line.StartX) * dx + (y - line.StartY) * dy + (z - line.StartZ) * dz) * 256;
            if (denominator == 0 || numerator == int.MinValue && denominator == -1) return -1;
            var fraction = numerator / denominator;
            var px = line.StartX + (fraction * dx >> 8);
            var py = line.StartY + (fraction * dy >> 8);
            var pz = line.StartZ + (fraction * dz >> 8);
            if (px < Math.Min(line.StartX, line.EndX) || px > Math.Max(line.StartX, line.EndX) ||
                py < Math.Min(line.StartY, line.EndY) || py > Math.Max(line.StartY, line.EndY)) return -1;
            return (px - x) * (px - x) + (py - y) * (py - y) + (pz - z) * (pz - z);
        }
    }

    internal static void Test()
    {
        var talk = new FieldNavigationTarget(100, FieldNavigationCategory.Npcs, "Person", 0, 0, 0, InteractionRadius: 110);
        if (!Reaches(talk, 109, 0, 255, 0, true) || Reaches(talk, 110, 0, 0, 0, true) || Reaches(talk, 0, 0, 256, 0, true))
            throw new InvalidOperationException("Talk endpoint must respect both native strict boundaries.");
        var line = talk with { TriggerLine = new(-100, 0, 0, 100, 0, 0), InteractionRadius = 29 };
        if (!Reaches(line, 0, 0, 29, 0, false) || Reaches(line, 0, 0, 30, 0, false) || Reaches(line, 120, 0, 0, 0, false))
            throw new InvalidOperationException("LINE endpoint must include height in its distance.");
        var cargo = line with { TriggerLine = new(568, -428, 733, 478, -441, 745), InteractionRadius = 0 };
        var bedroom = line with { TriggerLine = new(-122, 156, 1, -124, 231, 0), InteractionRadius = 0 };
        if (!Reaches(cargo, 478, -441, 746, 17, false, 30) || !Reaches(bedroom, -122, 156, 2, 10, false, 30))
            throw new InvalidOperationException("Native fixed-point projection includes the endpoint despite sub-unit mathematical overshoot.");
        var contact = line with { InteractionRadius = 0, LineActivationRadius = 30 };
        if (!Reaches(contact, 0, 29, 0, 0, false) || Reaches(contact, 0, 30, 0, 0, false))
            throw new InvalidOperationException("The shipping native line radius must override the point radius.");
        Console.WriteLine("Endpoint evidence respects native fixed-point three-dimensional lines and strict Talk boundaries.");
    }
}
