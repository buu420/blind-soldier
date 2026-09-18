namespace Ff7.Accessibility.Core;

/// <summary>Which navigation domain currently owns the controller menu.</summary>
public enum ControllerNavigationDomain
{
    None,
    Field,
    WorldMap,
}

/// <summary>
/// One coherent answer to "where is the player and may the menu act", published as a
/// single object so the hook can never read half of an update.
/// </summary>
/// <param name="Id">
/// Bumped whenever the domain or the field identity changes. Commands carry the id
/// they were produced under, so a selection made in one room cannot be applied in
/// the next.
/// </param>
public sealed record ControllerNavigationGeneration(
    long Id,
    ControllerNavigationDomain Domain,
    int Identity,
    bool IsHostForeground,
    bool ModuleSupportsNavigation,
    bool GameIsBusy,
    DateTime StampUtc)
{
    public static ControllerNavigationGeneration None { get; } =
        new(0, ControllerNavigationDomain.None, -1, false, false, true, DateTime.MinValue);

    public bool IsFreshAt(DateTime nowUtc, TimeSpan freshness) =>
        StampUtc != DateTime.MinValue && nowUtc - StampUtc <= freshness;
}

/// <summary>
/// The piece both runtimes' input hooks call: it runs the policy against the read the
/// game is making, <em>in</em> that read, and reports which buttons must be taken out
/// of the answer before the game sees it.
///
/// <para>Publication, the policy decision and the enqueue all happen inside one short
/// lock, so a command cannot be produced under a context that has already been
/// replaced and cleared. The lock covers the policy and nothing else: never speech,
/// never a call back into native code.</para>
/// </summary>
public sealed class ControllerNavigationCapture : IGameInputSuppressor
{
    /// <summary>How long a context published by the worker stays usable.</summary>
    public static readonly TimeSpan ContextFreshness = TimeSpan.FromMilliseconds(750);

    private readonly ControllerNavigationMenu menu;
    private readonly Func<bool> captureIsInstalled;
    private readonly Func<bool>? isForegroundNow;
    private readonly object policySync = new();

    private ControllerNavigationGeneration generation = ControllerNavigationGeneration.None;
    private long nextGenerationId = 1;
    private int suppressedMask;
    private long observedPolls;
    private long lastPollTicks;
    private bool retiredForLostPolling;

    public ControllerNavigationCapture(
        Func<IGameInputSuppressor, ControllerNavigationMenu> createMenu,
        Func<bool> captureIsInstalled,
        ControllerNavigationCommandQueue? commands = null,
        Func<bool>? isForegroundNow = null)
    {
        ArgumentNullException.ThrowIfNull(createMenu);
        this.captureIsInstalled = captureIsInstalled ?? throw new ArgumentNullException(nameof(captureIsInstalled));
        this.isForegroundNow = isForegroundNow;
        Commands = commands ?? new ControllerNavigationCommandQueue();
        menu = createMenu(this);
    }

    public ControllerNavigationCommandQueue Commands { get; }

    public ControllerNavigationAnnouncementQueue Announcements { get; } = new();

    public bool IsOpen => menu.IsOpen;

    /// <summary>
    /// Whether any button is being kept from the game right now - the menu is open, or
    /// it has closed and is still holding the press that closed it until the player
    /// lets go.
    ///
    /// <para>A host that can be reading more than one controller needs this to decide
    /// whether another pad may take the menu over. It may not while this is true: an
    /// open menu is not somebody else's to take, and cutting the tail short would
    /// release the press that chose a destination into the game underneath.</para>
    /// </summary>
    public bool IsSuppressing
    {
        get
        {
            lock (policySync)
            {
                return menu.IsSuppressing;
            }
        }
    }

    public string LastRefusal => menu.LastRefusal;

    public long ObservedPolls => Interlocked.Read(ref observedPolls);

    public ControllerNavigationGeneration Generation
    {
        get
        {
            lock (policySync)
            {
                return generation;
            }
        }
    }

    public ControllerNavigationDomain Owner => Generation.Domain;

    public DateTime LastPollUtc => new(Interlocked.Read(ref lastPollTicks), DateTimeKind.Utc);

    /// <summary>
    /// Publishes one coherent context. A change of domain or of field identity starts
    /// a new generation and discards every queued selection - those were about a place
    /// the player has left - while stops are kept.
    /// </summary>
    public void PublishContext(
        ControllerNavigationDomain domain,
        bool isHostForeground,
        bool moduleSupportsNavigation,
        bool gameIsBusy,
        DateTime nowUtc,
        int identity = -1)
    {
        lock (policySync)
        {
            // An inactive coordinator must not speak for the live one. The field tick
            // still runs while the world map owns the pad, and letting it publish
            // would hand the world map's menu a field context.
            if (domain != ControllerNavigationDomain.None &&
                generation.Domain != ControllerNavigationDomain.None &&
                generation.Domain != domain &&
                generation.IsFreshAt(nowUtc, ContextFreshness) &&
                generation.ModuleSupportsNavigation)
            {
                return;
            }

            var changedIdentity = generation.Domain != domain || generation.Identity != identity;
            if (changedIdentity)
            {
                Commands.ClearExceptStops();
            }

            generation = new ControllerNavigationGeneration(
                changedIdentity ? nextGenerationId++ : generation.Id,
                domain,
                identity,
                isHostForeground,
                moduleSupportsNavigation,
                gameIsBusy,
                nowUtc);
        }
    }

    /// <summary>
    /// Says the menu may not act at all. <paramref name="domain"/> of
    /// <see cref="ControllerNavigationDomain.None"/> is the global floor and always
    /// applies; a specific domain applies only where it is the owner, so one
    /// coordinator cannot close another's menu.
    /// </summary>
    public void PublishUnavailable(ControllerNavigationDomain domain, DateTime nowUtc)
    {
        lock (policySync)
        {
            if (domain != ControllerNavigationDomain.None &&
                generation.Domain != ControllerNavigationDomain.None &&
                generation.Domain != domain &&
                generation.IsFreshAt(nowUtc, ContextFreshness))
            {
                return;
            }

            generation = new ControllerNavigationGeneration(
                nextGenerationId++, domain, -1, false, false, true, nowUtc);
            Commands.ClearExceptStops();
            menu.RequestClose();
        }
    }

    /// <summary>
    /// Runs the policy against one raw read and returns the buttons to remove from what
    /// the game is given. Called on the game's thread, inside its own input getter.
    /// </summary>
    public GamepadButton ObserveRawPoll(GamepadSnapshot raw, DateTime nowUtc)
    {
        Interlocked.Increment(ref observedPolls);
        Interlocked.Exchange(ref lastPollTicks, nowUtc.Ticks);
        Volatile.Write(ref retiredForLostPolling, false);

        ControllerNavigationMenuResult result;
        long generationId;
        lock (policySync)
        {
            var current = generation;
            var fresh = current.IsFreshAt(nowUtc, ContextFreshness);
            var foreground = fresh && current.IsHostForeground;
            if (foreground && isForegroundNow is not null)
            {
                try
                {
                    foreground = isForegroundNow();
                }
                catch
                {
                    foreground = false;
                }
            }

            var context = new ControllerNavigationContext(
                foreground,
                fresh && current.ModuleSupportsNavigation,
                !fresh || current.GameIsBusy);

            try
            {
                result = menu.Observe(raw, context, nowUtc);
            }
            catch
            {
                // This frame is the game's input read. Fail open: the game gets its own
                // buttons back. A menu that stops working beats a player who cannot move.
                Interlocked.Exchange(ref suppressedMask, 0);
                return GamepadButton.None;
            }

            // Enqueued inside the lock, tagged with the generation it was decided
            // under. Doing this after releasing would let a command land in a queue
            // that a newly published context had just cleared.
            generationId = current.Id;
            Commands.Enqueue(result.Command, generationId);
        }

        if (result.Announcement is not null)
        {
            Announcements.Enqueue(result.Announcement);
        }

        return result.SuppressedButtons;
    }

    /// <summary>
    /// Closes the menu from the worker when the game has simply stopped asking.
    ///
    /// <para>This is the case <see cref="RequestClose"/> cannot serve: that only raises
    /// a flag for the next poll, and here the whole problem is that there is no next
    /// poll. A controller unplugged with the menu open would otherwise leave it open
    /// for ever, holding the walk suspended and repeating its complaint on every
    /// worker frame. The close happens here, under the same lock the hook takes, and
    /// the policy is left neutral so a reconnected pad must be pressed again.</para>
    /// </summary>
    /// <returns>True exactly once per stall, so the announcement is made once.</returns>
    public bool TryRetireForLostPolling(DateTime nowUtc, TimeSpan freshness)
    {
        if (Volatile.Read(ref retiredForLostPolling))
        {
            return false;
        }

        lock (policySync)
        {
            if (!menu.IsOpen || nowUtc - LastPollUtc <= freshness)
            {
                return false;
            }

            menu.Close();
            Volatile.Write(ref retiredForLostPolling, true);
        }

        Commands.ClearExceptStops();
        Interlocked.Exchange(ref suppressedMask, 0);
        return true;
    }

    /// <summary>Asks the menu to close on its next poll. Safe from the worker.</summary>
    public void RequestClose()
    {
        lock (policySync)
        {
            menu.RequestClose();
        }

        Commands.ClearExceptStops();
    }

    /// <summary>Teardown, once no callback can still run.</summary>
    public void Close()
    {
        lock (policySync)
        {
            menu.Close();
        }

        Commands.ClearExceptStops();
    }

    bool IGameInputSuppressor.IsAvailable => captureIsInstalled();

    bool IGameInputSuppressor.TryHold(GamepadButton buttons, out string diagnostic)
    {
        diagnostic = string.Empty;
        if (!captureIsInstalled())
        {
            diagnostic = "the controller input hook is not installed";
            Interlocked.Exchange(ref suppressedMask, 0);
            return false;
        }

        Interlocked.Exchange(ref suppressedMask, (int)buttons);
        return true;
    }

    void IGameInputSuppressor.ReleaseAll() => Interlocked.Exchange(ref suppressedMask, 0);
}

/// <summary>A bounded queue of things to say, oldest dropped first.</summary>
public sealed class ControllerNavigationAnnouncementQueue
{
    private readonly Queue<string> queue = new();
    private readonly object sync = new();
    private readonly int capacity;

    public ControllerNavigationAnnouncementQueue(int capacity = 8) => this.capacity = Math.Max(1, capacity);

    public void Enqueue(string announcement)
    {
        if (string.IsNullOrWhiteSpace(announcement))
        {
            return;
        }

        lock (sync)
        {
            if (queue.Contains(announcement))
            {
                return;
            }

            while (queue.Count >= capacity)
            {
                queue.Dequeue();
            }

            queue.Enqueue(announcement);
        }
    }

    public bool TryDequeue(out string announcement)
    {
        lock (sync)
        {
            if (queue.Count == 0)
            {
                announcement = string.Empty;
                return false;
            }

            announcement = queue.Dequeue();
            return true;
        }
    }
}
