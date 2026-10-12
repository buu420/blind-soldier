namespace Ff7.Accessibility.Reloaded;

public sealed class FieldCreatureApproachNarrationCoordinator
{
    private static readonly TimeSpan EpisodeLifetime = TimeSpan.FromSeconds(10);
    private static readonly FieldCutsceneDescriptionCue[] Cues = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions()
        .Where(c => c.FieldId == 767 && c.EntityId == 12 && c.Opcode == 0xC4 &&
                    ((c.ScriptId == 3 && c.ByteIndex == 12) || (c.ScriptId == 4 && c.ByteIndex == 22))).ToArray();
    private readonly object sync = new();
    private readonly Func<FieldCreatureApproachSample, bool> isCurrent;
    private readonly Func<LoadedFieldScriptIdentity?> readIdentity;
    private readonly Episode?[] episodes = new Episode?[2];
    private FieldCreatureApproachSample? generation;
    private LoadedFieldScriptIdentity? boundIdentity;
    private long epoch;
    private bool delivering;
    private AcceptedLease? lastAccepted;
    private long acceptedVersion;

    public FieldCreatureApproachNarrationCoordinator(FieldCreatureApproachReader reader, Func<LoadedFieldScriptIdentity?> readIdentity)
        : this(CurrentReader(reader), readIdentity) { }

    internal FieldCreatureApproachNarrationCoordinator(Func<FieldCreatureApproachSample, bool> isCurrent, Func<LoadedFieldScriptIdentity?> readIdentity)
    {
        this.isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
        this.readIdentity = readIdentity ?? throw new ArgumentNullException(nameof(readIdentity));
    }

    /// <summary>Native callbacks only replace immutable samples in two bounded slots.</summary>
    public void Capture(FieldCreatureApproachSample sample, DateTime capturedAtUtc)
    {
        var slot = RawEpisode(sample);
        if (slot < 0 || capturedAtUtc.Kind != DateTimeKind.Utc) return;
        lock (sync)
        {
            if (generation is { } previous && !SameGeneration(previous, sample)) ClearUnsafe();
            generation = sample;
            var episode = episodes[slot];
            if (episode is null) episodes[slot] = new Episode(sample, capturedAtUtc);
            else
            {
                episode.Sample = sample;
                if (!episode.Delivered) episode.Pending = true;
                if (lastAccepted is { } accepted && accepted.Slot == slot && accepted.Epoch == epoch)
                    lastAccepted = accepted with { Sample = sample, Version = unchecked(++acceptedVersion) };
                // Per-bone callbacks and later frames never renew FirstSeenUtc.
            }
        }
    }

    public bool TrySpeakPending(DateTime nowUtc, Func<FieldCreatureApproachSample, bool> canStart,
        Func<string, bool> trySpeak, out FieldCutsceneDescriptionCue spokenCue)
    {
        spokenCue = default;
        if (nowUtc.Kind != DateTimeKind.Utc || canStart is null || trySpeak is null) return false;
        lock (sync) { if (delivering) return false; delivering = true; }
        try
        {
            for (var slot = 0; slot < 2; slot++)
            {
                Episode episode; FieldCreatureApproachSample sample; long selectedEpoch;
                lock (sync)
                {
                    if (episodes[slot] is not { Pending: true, Delivered: false } selected) continue;
                    if (nowUtc < selected.FirstSeenUtc) return false;
                    if (nowUtc - selected.FirstSeenUtc > EpisodeLifetime)
                    { selected.Pending = false; continue; }
                    episode = selected; sample = selected.Sample; selectedEpoch = epoch;
                }
                if (!TryIdentity(sample, out var before)) { ClearIfCurrent(selectedEpoch); return false; }
                lock (sync)
                {
                    if (!AliveUnsafe(slot, episode, selectedEpoch)) return false;
                    if (boundIdentity is { } bound && !SameIdentity(bound, before)) { ClearUnsafe(); return false; }
                    boundIdentity = before;
                }
                var key = EchoSCompatibilityManifest.ResolveCanonicalCueKey(sample.Context, before);
                var expectedKey = new FieldCutsceneDescriptionKey(767, 12, slot == 0 ? 3 : 4, slot == 0 ? 12 : 22);
                if (key != expectedKey) { Invalidate(slot, episode, selectedEpoch); continue; }
                var matches = Cues.Where(c => c.Key == expectedKey).ToArray();
                if (matches.Length != 1 || matches[0].Text != (slot == 0 ? "The huge creature glides closer." : "The creature closes in."))
                { Invalidate(slot, episode, selectedEpoch); continue; }
                bool current;
                try { current = isCurrent(sample); } catch { current = false; }
                if (!current) { Invalidate(slot, episode, selectedEpoch); continue; }
                bool permitted;
                try { permitted = canStart(sample); } catch { permitted = false; }
                if (!permitted) return false;
                if (!TryIdentity(sample, out var after) || !SameIdentity(before, after))
                { ClearIfCurrent(selectedEpoch); return false; }
                lock (sync) { if (!AliveUnsafe(slot, episode, selectedEpoch)) return false; }
                bool accepted;
                try { accepted = trySpeak(matches[0].Text); } catch { accepted = false; }
                if (!accepted) return false;
                lock (sync)
                {
                    if (!AliveUnsafe(slot, episode, selectedEpoch)) return false;
                    episode.Delivered = true; episode.Pending = false;
                    lastAccepted = new(sample, before, slot, selectedEpoch, unchecked(++acceptedVersion));
                    spokenCue = matches[0];
                    return true;
                }
            }
            return false;
        }
        finally { lock (sync) delivering = false; }
    }

    public void Reset() { lock (sync) ClearUnsafe(); }
    /// <summary>Fresh worker-side visibility for stopping only an owned creature recording.</summary>
    public bool IsCurrentDescriptionVisible()
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            AcceptedLease accepted;
            lock (sync) { if (lastAccepted is not { } current) return false; accepted = current; }
            var valid = TryIdentity(accepted.Sample, out var before) && SameIdentity(before, accepted.Identity);
            if (valid) { try { valid = isCurrent(accepted.Sample); } catch { valid = false; } }
            valid = valid && TryIdentity(accepted.Sample, out var after) && SameIdentity(before, after);
            lock (sync)
            {
                if (epoch != accepted.Epoch || lastAccepted is not { } current) return false;
                if (current.Version != accepted.Version) continue;
                if (!valid) lastAccepted = null;
                return valid;
            }
        }
        return false;
    }

    private bool TryIdentity(FieldCreatureApproachSample sample, out LoadedFieldScriptIdentity identity)
    {
        identity = default;
        try
        {
            if (readIdentity() is not { } current || current.FieldId != 767 ||
                (current.ScriptPointer != 0 && current.ScriptPointer != sample.ScriptPointer) ||
                WholeGameEchoNavigationManifest.ResolveDescriptionVariant(current) == SupportedFieldScriptVariant.Unknown) return false;
            identity = current; return true;
        }
        catch { return false; }
    }
    private void Invalidate(int slot, Episode episode, long selectedEpoch)
    { lock (sync) { if (AliveUnsafe(slot, episode, selectedEpoch)) episode.Pending = false; } }
    private void ClearIfCurrent(long selectedEpoch) { lock (sync) { if (epoch == selectedEpoch) ClearUnsafe(); } }
    private bool AliveUnsafe(int slot, Episode episode, long selectedEpoch) =>
        epoch == selectedEpoch && ReferenceEquals(episodes[slot], episode) && episode.Pending && !episode.Delivered;
    private void ClearUnsafe()
    { episodes[0] = null; episodes[1] = null; generation = null; boundIdentity = null; lastAccepted = null; epoch = unchecked(epoch + 1); }
    private static bool SameIdentity(LoadedFieldScriptIdentity a, LoadedFieldScriptIdentity b) =>
        a.FieldId == b.FieldId && a.ScriptPointer == b.ScriptPointer && string.Equals(a.ScriptPrefixSha256, b.ScriptPrefixSha256, StringComparison.OrdinalIgnoreCase);
    private static bool SameGeneration(FieldCreatureApproachSample a, FieldCreatureApproachSample b) =>
        a.ScriptPointer == b.ScriptPointer && a.ObjectTable == b.ObjectTable && a.RenderTable == b.RenderTable &&
        a.ObjectBase == b.ObjectBase && a.RenderModel == b.RenderModel && a.RenderFrame == b.RenderFrame &&
        a.HrcModel == b.HrcModel && a.GameObject == b.GameObject && a.GraphicsDriver == b.GraphicsDriver && a.ModelId == b.ModelId;
    private static int RawEpisode(FieldCreatureApproachSample sample)
    {
        var context = sample.Context;
        if (context.FieldId != 767 || context.EntityId != 12 || context.Opcode != 0xC4 || sample.ModelId != 8 ||
            sample.ScriptPointer == 0 || sample.ObjectTable == 0 || sample.RenderTable == 0 || sample.ObjectBase == 0 ||
            sample.RenderFrame == 0 || sample.HrcModel == 0 || sample.PolygonSet == 0 || sample.GameObject == 0 ||
            sample.MovementPhase is not (1 or 2) || sample.Duration == 0 || sample.Duration > short.MaxValue || sample.Step > sample.Duration ||
            (sample.CurrentX == sample.InitialX && sample.CurrentY == sample.InitialY && sample.CurrentZ == sample.InitialZ)) return -1;
        return context.ScriptId == 3 && context.ByteIndex is 12 or 14 ? 0 : context.ScriptId == 4 && context.ByteIndex == 22 ? 1 : -1;
    }
    private static Func<FieldCreatureApproachSample, bool> CurrentReader(FieldCreatureApproachReader reader)
    { ArgumentNullException.ThrowIfNull(reader); return sample => reader.IsCurrent(sample, requireMoving: false); }
    private sealed class Episode(FieldCreatureApproachSample sample, DateTime firstSeenUtc)
    {
        public FieldCreatureApproachSample Sample = sample;
        public readonly DateTime FirstSeenUtc = firstSeenUtc;
        public bool Pending = true;
        public bool Delivered;
    }
    private readonly record struct AcceptedLease(FieldCreatureApproachSample Sample, LoadedFieldScriptIdentity Identity, int Slot, long Epoch, long Version);
}
