namespace Ff7.Accessibility.Reloaded;

public sealed class TifaSlotSpeechTracker
{
    private readonly Queue<string> pending = new();
    private int announcedMask;
    private TifaSlotResultSnapshot previousPostFrame;

    public void Observe(TifaSlotResultSnapshot snapshot)
    {
        if (previousPostFrame.IsValid)
        {
            ObserveFrame(previousPostFrame, snapshot);
        }

        previousPostFrame = snapshot;
    }

    public void ObserveFrame(
        TifaSlotResultSnapshot before,
        TifaSlotResultSnapshot after)
    {
        if (!before.IsValid ||
            !after.IsValid ||
            before.Reels.Count != after.Reels.Count)
        {
            return;
        }

        var stoppedMask = 0;
        for (var reelIndex = 0; reelIndex < after.Reels.Count; reelIndex++)
        {
            var beforeReel = before.Reels[reelIndex];
            var afterReel = after.Reels[reelIndex];
            if (afterReel.IsStopped)
            {
                stoppedMask |= 1 << afterReel.ReelIndex;
            }

            var reelMask = 1 << afterReel.ReelIndex;
            var isSameVisibleResult =
                beforeReel.ReelIndex == afterReel.ReelIndex &&
                beforeReel.Position == afterReel.Position &&
                beforeReel.IsStopped &&
                afterReel.IsStopped &&
                beforeReel.IsAligned &&
                afterReel.IsAligned &&
                beforeReel.Symbol == afterReel.Symbol;
            if (!isSameVisibleResult || (announcedMask & reelMask) != 0)
            {
                continue;
            }

            announcedMask |= reelMask;
            pending.Enqueue(afterReel.Symbol switch
            {
                TifaSlotSymbol.Miss => "Miss",
                TifaSlotSymbol.Hit => "Hit",
                TifaSlotSymbol.Yeah => "Yeah!",
                _ => throw new InvalidOperationException("Unsupported Tifa slot symbol.")
            });
        }

        if (stoppedMask == 0)
        {
            announcedMask = 0;
        }
    }

    public string? Poll() => pending.Count == 0 ? null : pending.Dequeue();

    public void ObserveCommitted(TifaSlotCommittedResultSnapshot snapshot)
    {
        if (!snapshot.IsValid)
        {
            return;
        }

        for (var reelIndex = 0; reelIndex < snapshot.Symbols.Count; reelIndex++)
        {
            var reelMask = 1 << reelIndex;
            if ((announcedMask & reelMask) != 0)
            {
                continue;
            }

            announcedMask |= reelMask;
            pending.Enqueue(snapshot.Symbols[reelIndex] switch
            {
                TifaSlotSymbol.Miss => "Miss",
                TifaSlotSymbol.Hit => "Hit",
                TifaSlotSymbol.Yeah => "Yeah!",
                _ => throw new InvalidOperationException("Unsupported committed Tifa slot symbol.")
            });
        }
    }

    public void Reset()
    {
        announcedMask = 0;
        pending.Clear();
        previousPostFrame = default;
    }
}
