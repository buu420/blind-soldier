namespace Ff7.Accessibility.Reloaded;

// Receives only completed native draws. Never queries enemy weakness, loot, or HP data.
public sealed class PostscriptBattleAssistSpeechTracker
{
    private readonly object sync = new();
    private readonly Func<DateTime> clock;
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan Freshness = TimeSpan.FromMilliseconds(250);
    private readonly Dictionary<string, Draw> draws = new();
    private PostscriptBattleAssistOwner? owner;
    private DateTime changedAt;
    private string? lastSpeech;
    public PostscriptBattleAssistSpeechTracker(Func<DateTime>? clock = null) => this.clock = clock ?? (() => DateTime.UtcNow);

    public void ObserveDraw(string source, MenuTextRenderEntry draw, PostscriptBattleAssistOwner nativeOwner)
    {
        lock (sync)
        {
            if (source != "B" || !Valid(nativeOwner) || draw.Color != 7 || draw.X > 639 || draw.Y is < 32 or > 347) return;
            var text = draw.Text.Trim();
            if (text.Length is 0 or > 256 || text.Any(char.IsControl)) return;
            string? key = null;
            if (nativeOwner.Features.HasFlag(PostscriptBattleAssistFeatures.StealMorph) && draw.Y == 256)
            {
                if (draw.X == 56 && text.TrimEnd(':') == "Steal") key = "steal-label";
                if (draw.X == 127) key = "steal-value";
                if (draw.X == 336 && text.TrimEnd(':') == "Morph") key = "morph-label";
                if (draw.X == 421) key = "morph-value";
            }
            if (nativeOwner.Features.HasFlag(PostscriptBattleAssistFeatures.Effectiveness) && nativeOwner.AnchorX is >= 0 and <= 591 && nativeOwner.AnchorY is >= 32 and <= 331)
            {
                if (draw.X == nativeOwner.AnchorX + 48 && draw.Y == nativeOwner.AnchorY &&
                    text is "Weak" or "Resists" or "Absorbs" or "Immune") key = "element";
                if (draw.X == nativeOwner.AnchorX && draw.Y == nativeOwner.AnchorY + 16 && text == "Status Immune") key = "status";
            }
            if (key is not null) Observe(key, text, nativeOwner);
        }
    }

    public void ObserveDisplayedHpHelp(string text, PostscriptBattleAssistOwner nativeOwner)
    {
        lock (sync)
        {
            if (!Valid(nativeOwner) || !nativeOwner.Features.HasFlag(PostscriptBattleAssistFeatures.AutoSense) ||
                string.IsNullOrWhiteSpace(text) || text.Length > 512) return;
            Observe("help", text.Trim(), nativeOwner);
        }
    }

    public string? Poll(PostscriptBattleAssistOwner? currentOwner)
    {
        lock (sync)
        {
            if (currentOwner is not { } current || !Valid(current) || owner != current)
            {
                ResetCore();
                return null;
            }
            var now = clock();
            foreach (var key in draws.Where(pair => now - pair.Value.SeenAt > Freshness || now < pair.Value.SeenAt).Select(pair => pair.Key).ToArray()) draws.Remove(key);
            if (draws.Count == 0)
            {
                lastSpeech = null;
                return null;
            }
            if (now < changedAt || now - changedAt < Settle) return null;
            var lines = new List<string>();
            Add("help");
            Pair("steal", "Steal");
            Pair("morph", "Morph");
            Add("element");
            Add("status");
            var speech = string.Join(". ", lines);
            if (speech.Length == 0 || speech == lastSpeech) return null;
            lastSpeech = speech;
            return speech;
            void Add(string key) { if (draws.TryGetValue(key, out var draw)) lines.Add(draw.Text); }
            void Pair(string key, string label)
            {
                if (draws.ContainsKey(key + "-label") && draws.TryGetValue(key + "-value", out var value)) lines.Add($"{label}: {value.Text}");
            }
        }
    }
    public void Reset() { lock (sync) ResetCore(); }
    private void ResetCore() { owner = null; draws.Clear(); changedAt = default; lastSpeech = null; }
    private void Observe(string key, string text, PostscriptBattleAssistOwner current)
    {
        if (owner != current) { ResetCore(); owner = current; }
        var now = clock();
        if (!draws.TryGetValue(key, out var old) || old.Text != text || now - old.SeenAt > Freshness) changedAt = now;
        draws[key] = new(text, now);
    }
    private static bool Valid(PostscriptBattleAssistOwner current) => current.IsValid && current.ActorSlot is >= 0 and <= 2 &&
        current.SelectedTarget is >= 4 and <= 9 && (current.TargetMask & (1 << current.SelectedTarget)) != 0 &&
        (current.TargetMode & 4) == 0 && current.Features != PostscriptBattleAssistFeatures.None;
    private readonly record struct Draw(string Text, DateTime SeenAt);
}
