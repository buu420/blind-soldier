namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Ensures that only one Blind Soldier entry assembly owns accessibility
/// output in a game process, even when Reloaded exposes duplicate mod paths or
/// assembly load contexts.
/// </summary>
public sealed class BlindSoldierRuntimeLease : IDisposable
{
    private Semaphore? semaphore;

    private BlindSoldierRuntimeLease(Semaphore semaphore)
    {
        this.semaphore = semaphore;
    }

    public static bool TryAcquire(
        int processId,
        out BlindSoldierRuntimeLease? lease)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        var candidate = new Semaphore(
            initialCount: 1,
            maximumCount: 1,
            name: $@"Local\BlindSoldier.Runtime.{processId}");
        if (!candidate.WaitOne(0))
        {
            candidate.Dispose();
            lease = null;
            return false;
        }

        lease = new BlindSoldierRuntimeLease(candidate);
        return true;
    }

    public void Dispose()
    {
        var ownedSemaphore = Interlocked.Exchange(ref semaphore, null);
        if (ownedSemaphore is null)
        {
            return;
        }

        try
        {
            ownedSemaphore.Release();
        }
        finally
        {
            ownedSemaphore.Dispose();
        }
    }
}
