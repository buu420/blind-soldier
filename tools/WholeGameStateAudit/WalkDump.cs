using Ff7.Accessibility.Reloaded;

namespace WholeGameStateAudit;

/// <summary>
/// Debug view: every way through each slot of one entity that the production walker
/// returns, with its actions in order. Used to explain a transition that changed.
/// </summary>
internal static class WalkDump
{
    public static int Write(string root, int fieldId, int entity)
    {
        var source = new FlevelDataSource(root);
        if (!source.TryReadField(fieldId, out var encoded))
        {
            Console.Error.WriteLine($"field {fieldId} not in archive");
            return 1;
        }

        var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
        var section = ShippingReflection.ReadSectionOne(bytes)!;
        var (groups, native) = ShippingReflection.ParseScriptGroups(section);
        foreach (var slot in groups[entity].Scripts.Keys.Order())
        {
            var paths = ShippingReflection.CollectNavigationPaths(native, entity, slot);
            Console.WriteLine($"slot {slot}: {paths.Count} ways through");
            foreach (var path in paths.Where(path => path.Count > 0).Take(40))
            {
                Console.WriteLine("  " + string.Join(" -> ", path.Select(action => $"{action.Signature} [e{action.SourceGroup}.s{action.SourceScript}]")));
            }
        }

        return 0;
    }

    /// <summary>How long the production walker takes on each script of every entity of one field.</summary>
    public static int WriteTimings(string root, int fieldId)
    {
        var source = new FlevelDataSource(root);
        if (!source.TryReadField(fieldId, out var encoded))
        {
            Console.Error.WriteLine($"field {fieldId} not in archive");
            return 1;
        }

        var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
        var section = ShippingReflection.ReadSectionOne(bytes)!;
        var (groups, native) = ShippingReflection.ParseScriptGroups(section);
        foreach (var group in groups)
        {
            foreach (var slot in group.Scripts.Keys.Order())
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var paths = ShippingReflection.CollectNavigationPaths(native, group.Index, slot);
                var milliseconds = stopwatch.Elapsed.TotalMilliseconds;
                if (milliseconds >= 50)
                {
                    Console.WriteLine($"e{group.Index} {group.Name} s{slot}: {milliseconds:0} ms, {paths.Count} ways through");
                }
            }
        }

        return 0;
    }
}
