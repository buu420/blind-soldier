using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class FieldNavigationTriggerFallbackTests
{
    internal static void Run()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrEmpty(root)) return;
        var source = new FlevelDataSource(root);
        // The broad guide audit found these real gateway lines falling back to
        // point routes. A failed LINE attempt must not replace their endpoint
        // with default(waypoint), which would steer the party toward (0,0,0).
        Check(542, -1821, -1835, 99, 0);
        foreach (var gateway in new[] { 2, 3, 4 }) Check(439, -777, -291, 0, gateway);

        void Check(int field, int x, int y, ushort triangle, int gateway)
        {
            if (!source.TryReadField(field, out var encoded)) throw new InvalidDataException($"Cannot read native field {field}.");
            var data = Ff7LzsDecoder.DecodeFieldFile(encoded);
            const int ptr = 0x02000000;
            int Int(int a) => a == FieldWalkmeshReader.AddressFieldDataPtr ? ptr :
                a >= ptr && a + 4 <= ptr + data.Length ? BitConverter.ToInt32(data, a - ptr) : 0;
            short Short(int a) => a >= ptr && a + 2 <= ptr + data.Length ? BitConverter.ToInt16(data, a - ptr) : (short)0;
            var reader = new FieldWalkmeshReader(Int, Short);
            var mesh = reader.Read(new(1, field, 0, x, y, 0, triangle, 0)).Walkmesh!;
            var at = BitConverter.ToInt32(data, 6 + 7 * 4) + 4 + 0x38 + gateway * 24;
            var line = new FieldNavigationTriggerLine(BitConverter.ToInt16(data, at), BitConverter.ToInt16(data, at + 2),
                BitConverter.ToInt16(data, at + 4), BitConverter.ToInt16(data, at + 6),
                BitConverter.ToInt16(data, at + 8), BitConverter.ToInt16(data, at + 10));
            var target = new FieldNavigationTarget(field, FieldNavigationCategory.Exits, "Native doorway",
                (line.StartX + line.EndX) / 2, (line.StartY + line.EndY) / 2, (line.StartZ + line.EndZ) / 2,
                TriggerLine: line);
            // For shpin_2 use the gateway arrival from shpin_3, read directly
            // rather than inventing a point on its cargo-room walkmesh.
            if (field == 439)
            {
                if (!source.TryReadField(440, out var incoming)) throw new InvalidDataException("Cannot read shpin_3.");
                var other = Ff7LzsDecoder.DecodeFieldFile(incoming);
                var table = BitConverter.ToInt32(other, 6 + 7 * 4) + 4 + 0x38;
                var found = false;
                for (var i = 0; i < 12; i++)
                {
                    var p = table + i * 24;
                    if (BitConverter.ToInt16(other, p + 18) != field) continue;
                    x = BitConverter.ToInt16(other, p + 12); y = BitConverter.ToInt16(other, p + 14);
                    triangle = BitConverter.ToUInt16(other, p + 16); found = true; break;
                }
                if (!found) throw new InvalidDataException("Native cargo-room arrival missing.");
            }
            var z = (int)Math.Round(mesh.Triangles[triangle].GetCentroid().Z);
            var planner = new FieldWalkmeshRoutePlanner(reader);
            if (!planner.TryBuildRoute(new(1, field, 0, x, y, z, triangle, 0), target, out var plan))
                throw new InvalidOperationException($"Native doorway {field}/{gateway}: {planner.LastDiagnostic}");
            var expected = new FieldNavigationRouteWaypoint(target.X, target.Y, target.Z);
            if (plan.FinalApproach != expected)
                throw new InvalidOperationException($"Native doorway {field}/{gateway}: point fallback endpoint {plan.FinalApproach}, expected {expected}.");
        }
    }
}
