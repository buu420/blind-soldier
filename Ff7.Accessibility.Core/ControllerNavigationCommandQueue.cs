namespace Ff7.Accessibility.Core;

/// <summary>
/// Carries commands from the input hook, which runs on the game's own thread inside
/// a getter the game is waiting on, to the worker that may speak and may walk.
///
/// <para>Two properties matter and nothing else does. It is <b>bounded</b>, because a
/// worker that stalls must cost the game a dropped command rather than a growing
/// allocation in its input path. And its critical section is a fixed number of array
/// writes, so the game thread can never be made to wait on anything the worker does -
/// the worker's own speech and route work all happen after the dequeue returns.</para>
///
/// <para>Overflow drops the <em>oldest</em>. A backlog is stale by definition: the
/// selection the player is trying to reach is the one they pressed last.</para>
/// </summary>
public sealed class ControllerNavigationCommandQueue
{
    private readonly (ControllerNavigationCommand Command, long Generation)[] slots;
    private readonly object sync = new();
    private int head;
    private int count;
    private long dropped;

    public ControllerNavigationCommandQueue(int capacity = 32)
        => slots = new (ControllerNavigationCommand, long)[Math.Max(4, capacity)];

    public int Capacity => slots.Length;

    /// <summary>How many commands the worker was too slow to take. Diagnostic only.</summary>
    public long Dropped => Interlocked.Read(ref dropped);

    public int Count
    {
        get
        {
            lock (sync)
            {
                return count;
            }
        }
    }

    /// <summary>
    /// Called from the hook. <see cref="ControllerNavigationCommand.None"/> is not a
    /// command and is ignored, so callers can enqueue unconditionally.
    /// </summary>
    /// <param name="generation">
    /// The context the command was decided under. A selection made in one room must
    /// not be applied in the next, so the drain compares this against the live one.
    /// Stops carry a generation too but are never rejected for it.
    /// </param>
    public void Enqueue(ControllerNavigationCommand command, long generation = 0)
    {
        if (command == ControllerNavigationCommand.None)
        {
            return;
        }

        lock (sync)
        {
            if (count == slots.Length)
            {
                head = (head + 1) % slots.Length;
                count--;
                Interlocked.Increment(ref dropped);
            }

            slots[(head + count) % slots.Length] = (command, generation);
            count++;
        }
    }

    /// <summary>Called from the worker, one command per call, oldest first.</summary>
    public bool TryDequeue(out ControllerNavigationCommand command) =>
        TryDequeue(out command, out _);

    public bool TryDequeue(out ControllerNavigationCommand command, out long generation)
    {
        lock (sync)
        {
            if (count == 0)
            {
                command = ControllerNavigationCommand.None;
                generation = 0;
                return false;
            }

            (command, generation) = slots[head];
            head = (head + 1) % slots.Length;
            count--;
            return true;
        }
    }

    /// <summary>
    /// Throws away everything queued, for a boundary that makes it all meaningless -
    /// a module change, a reset, an unload.
    ///
    /// <para>Stops are deliberately <em>not</em> discarded. A stop that arrives just
    /// as the context changes is still the player asking for the walking to end, and
    /// dropping it leaves them being walked somewhere by a mod that has stopped
    /// listening.</para>
    /// </summary>
    public void ClearExceptStops()
    {
        lock (sync)
        {
            var kept = 0;
            for (var index = 0; index < count; index++)
            {
                var entry = slots[(head + index) % slots.Length];
                if (entry.Command == ControllerNavigationCommand.StopNavigation)
                {
                    slots[(head + kept) % slots.Length] = entry;
                    kept++;
                }
            }

            count = kept;
        }
    }
}
