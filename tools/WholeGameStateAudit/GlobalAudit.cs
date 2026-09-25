using System.Text.Json.Nodes;

namespace WholeGameStateAudit;

internal sealed class FlagRecord
{
    public const int KeptRecords = 40;

    public List<JsonObject> Writers { get; } = [];

    public List<JsonObject> Readers { get; } = [];

    public List<JsonObject> Gates { get; } = [];

    public int WriterCount { get; set; }

    public int ReaderCount { get; set; }

    public int GateCount { get; set; }

    public SortedSet<int> WriterFields { get; } = [];

    public SortedSet<int> ReaderFields { get; } = [];

    public SortedSet<int> GateFields { get; } = [];

    public SortedSet<int> StoryFields { get; } = [];

    public SortedSet<int> ObjectFields { get; } = [];

    public SortedSet<string> WriterTriggers { get; } = new(StringComparer.Ordinal);

    public SortedSet<string> GatedKinds { get; } = new(StringComparer.Ordinal);

    /// <summary>Bits of the byte that scripts write (0xFF for a whole-byte write).</summary>
    public int WrittenMask { get; set; }

    /// <summary>Bits of the byte that a test guarding something perceivable reads.</summary>
    public int GatedMask { get; set; }

    /// <summary>Bits of the byte some Story or Object row reads.</summary>
    public int TrackedMask { get; set; }

    public void AddWriter(int field, JsonObject record, string trigger)
    {
        WriterCount++;
        WriterFields.Add(field);
        WriterTriggers.Add(trigger);
        if (Writers.Count < KeptRecords)
        {
            Writers.Add(record);
        }
    }

    public void AddReader(int field, JsonObject record)
    {
        ReaderCount++;
        ReaderFields.Add(field);
        if (Readers.Count < KeptRecords)
        {
            Readers.Add(record);
        }
    }

    public void AddGate(int field, JsonObject record, string kind)
    {
        GateCount++;
        GateFields.Add(field);
        GatedKinds.Add(kind);
        if (Gates.Count < KeptRecords)
        {
            Gates.Add(record);
        }
    }
}

/// <summary>Counters, the cross-field flag graph and every candidate, for one archive.</summary>
internal sealed class GlobalAudit
{
    public SortedDictionary<string, long> Counts { get; } = new(StringComparer.Ordinal);

    public List<Candidate> Candidates { get; } = [];

    public Dictionary<ByteKey, FlagRecord> Flags { get; } = new();

    public List<JsonObject> FieldRows { get; } = [];

    public Dictionary<int, string> FieldHashes { get; } = new();

    public Dictionary<int, string> FieldNames { get; } = new();

    public List<JsonObject> Unreadable { get; } = [];

    /// <summary>Maplist names the archive ships no field for, as released (not failures).</summary>
    public List<JsonObject> MaplistOnly { get; } = [];

    /// <summary>Every field-to-field transition: gateways and live map jumps, with how they are triggered.</summary>
    public Dictionary<int, HashSet<(int Destination, string Kind)>> Transitions { get; } = new();

    /// <summary>
    /// Developer rooms: the debug start menu and the black/white test backgrounds and
    /// q-rooms, which set story flags wholesale to jump into scenes. Their writes are not
    /// progress anyone makes in play.
    /// </summary>
    public static bool IsDebugRoom(string name) =>
        name is "startmap" or "qa" or "qb" or "qc" or "qd" or "qe" or "xmvtes" ||
        name.StartsWith("blackbg", StringComparison.Ordinal) ||
        name.StartsWith("whitebg", StringComparison.Ordinal);

    public void AddTransition(int from, int destination, string kind)
    {
        if (!Transitions.TryGetValue(from, out var set))
        {
            set = [];
            Transitions[from] = set;
        }

        set.Add((destination, kind));
    }

    public void Count(string key, long by = 1)
    {
        Counts.TryGetValue(key, out var current);
        Counts[key] = current + by;
    }

    public FlagRecord Flag(ByteKey key)
    {
        if (!Flags.TryGetValue(key, out var record))
        {
            record = new FlagRecord();
            Flags[key] = record;
        }

        return record;
    }

    public void Add(Candidate candidate)
    {
        if (IsDebugRoom(candidate.FieldName) && candidate.Priority < 4)
        {
            candidate.Witness["debugRoom"] = true;
            candidate = candidate with { Priority = 4 };
        }

        Candidates.Add(candidate);
        Count($"candidates.{candidate.Class}");
        Count($"candidates.p{candidate.Priority}");
    }
}
