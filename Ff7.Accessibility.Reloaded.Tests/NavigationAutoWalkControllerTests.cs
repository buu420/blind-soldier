using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
#if BLIND_SOLDIER_LEGACY_TESTS
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Reloaded.Hooks.Definitions;
#endif

internal static class NavigationAutoWalkControllerTests
{
    internal static void Run()
    {
        SamplesPAsAForegroundRisingEdgeWithoutDelayedActivation();
        StartsOnlyForAnActiveRouteAndTogglesOffCleanly();
        DrivesTheCurrentRouteDirectionAndReleasesDuringSuspension();
        ReassertsAnOwnedDirectionWhenTheGameStopsReportingIt();
        FailsClosedAfterPartialDirectionalInputFailure();
        ResolvesFieldRouteAndMountedLadderDirections();
        NativeExitsKeepAutomaticDirectionInsideTheProximityRadius();
        StoryCrossingsDoNotCompleteAtTheOrdinaryArrivalRadius();
        StoryGatewayCrossingsWaitForTheNativeFieldTransition();
        InteractionTargetsKeepTheirArrivalRadius();
        ADirectionNearAFlytrapActsOnlyInItsBudgetedPolls();
        TheGamesPollsEndTheHoldWhileTheMonitorIsBlocked();
        AReleasedDirectionStaysOutUntilTheGameReadsItGone();
        ElsewhereEveryPollActsAsItAlwaysHas();
        AFieldBudgetIsMeasuredJustBeforeThePress();
        ASlowScanStillLeavesItsBudgetToThePress();
#if BLIND_SOLDIER_LEGACY_TESTS
        TheFieldKeyReadHookGoesOnlyOverTheDecompiledCode();
        FieldScriptsSeeTheBoundedKeysToo();
        AHookThatCannotGoLiveStaysRootedAndUnpolled();
#endif
    }

    internal static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        Run();
        NativeLockerRoomCornerRemainsUntilTheNextLegIsWalkable(createWalkmeshReader);
        NativeCornerPortalHandoffHonorsTraversalBlocks(createWalkmeshReader);
#if BLIND_SOLDIER_LEGACY_TESTS
        TheFieldKeyReadHookMatchesTheInstalledImage();
#endif
    }

    // Field mask bits FUN_006342C6 walks by, and the run bit it also reads.
    private const uint MaskRight = 0x2000;
    private const uint MaskDown = 0x4000;
    private const uint MaskRun = 0x40;

    /// <summary>
    /// Armed from a mark of five polls whose latest acted, with three polls to spare: that
    /// latest step may not be in the measured position yet, so two more polls act and none
    /// after, however many come. From a mark whose latest poll did not act, all three do.
    /// </summary>
    private static void ADirectionNearAFlytrapActsOnlyInItsBudgetedPolls()
    {
        using var controller = new NavigationAutoWalkController(new RecordingSink());
        var lease = controller.PollLease;
        _ = controller.TryStart(NavigationAutoWalkDomain.Field, routeActive: true);
        for (var poll = 0; poll < 5; poll++)
        {
            Equal(true, lease.Poll(), $"unbounded poll {poll + 1} acts");
        }

        var mark = lease.Mark();
        Equal(new AutoWalkPollMark(5, true), mark, "the mark holds the count and that the latest acted");
        _ = controller.Drive(FieldNavigationInput.Right, canMove: true, routeActive: true,
            pollBudget: new AutoWalkPollBudget(mark, 3));
        var acted = Enumerable.Range(0, 40).Count(_ => lease.Poll());
        Equal(2, acted, "the in-flight step and two more make the three polls the planner allowed");

        // The next decision: the latest poll did not act, so nothing is in flight.
        var quiet = lease.Mark();
        Equal(false, quiet.LatestActed, "a spent lease's polls do not act");
        _ = controller.Drive(FieldNavigationInput.Right, canMove: true, routeActive: true,
            pollBudget: new AutoWalkPollBudget(quiet, 3));
        Equal(3, Enumerable.Range(0, 40).Count(_ => lease.Poll()), "all three polls act");

        _ = controller.Drive(FieldNavigationInput.Right, canMove: true, routeActive: true,
            pollBudget: new AutoWalkPollBudget(lease.Mark(), 0));
        Equal(0, Enumerable.Range(0, 40).Count(_ => lease.Poll()), "a budget of none acts in no poll");
    }

    /// <summary>
    /// The monitor arms a decision and then stops dead - its thread blocked, as a long scan or a
    /// hang would leave it - while the game goes on polling on its own thread: the direction is
    /// taken out of the field mask after the budget, and the run bit and other keys pass.
    /// </summary>
    private static void TheGamesPollsEndTheHoldWhileTheMonitorIsBlocked()
    {
        var lease = new AutoWalkPollLease(releaseLagsThePoll: true);
        using var controller = new NavigationAutoWalkController(new RecordingSink(), lease);
        _ = controller.TryStart(NavigationAutoWalkDomain.Field, routeActive: true);
        using var armed = new ManualResetEventSlim();
        using var unblock = new ManualResetEventSlim();
        var monitor = new Thread(() =>
        {
            _ = controller.Drive(FieldNavigationInput.Right, canMove: true, routeActive: true,
                pollBudget: new AutoWalkPollBudget(lease.Mark(), 4));
            armed.Set();
            unblock.Wait();
        }) { IsBackground = true };
        monitor.Start();
        Equal(true, armed.Wait(TimeSpan.FromSeconds(10)), "the monitor armed its decision");

        var masks = Enumerable.Range(0, 60).Select(_ => lease.FilterFieldKeys(MaskRight | MaskRun)).ToArray();
        Equal(true, monitor.IsAlive, "the monitor is still blocked while the game polls");
        Equal(4, masks.Count(mask => (mask & MaskRight) != 0), "the direction acts in its four polls and no more");
        Equal(true, masks.Skip(4).All(mask => mask == MaskRun), "after them only the run bit reaches the movement");
        unblock.Set();
        Equal(true, monitor.Join(TimeSpan.FromSeconds(10)), "the monitor comes back");
    }

    /// <summary>
    /// SendInput's key-up can still be read on the game's next polls. Let go of while bounded,
    /// the direction auto walk was pressing - and only that one - stays out of the mask until a
    /// poll reads it gone: any other direction the player presses acts at once, and the poll
    /// that reads the released one up ends the bound - unless a new decision has armed it first.
    /// </summary>
    private static void AReleasedDirectionStaysOutUntilTheGameReadsItGone()
    {
        var lease = new AutoWalkPollLease(releaseLagsThePoll: true);
        using var controller = new NavigationAutoWalkController(new RecordingSink(), lease);
        _ = controller.TryStart(NavigationAutoWalkDomain.Field, routeActive: true);
        _ = controller.Drive(FieldNavigationInput.Right, canMove: true, routeActive: true,
            pollBudget: new AutoWalkPollBudget(lease.Mark(), 30));
        Equal(MaskRight, lease.FilterFieldKeys(MaskRight), "inside the budget the direction acts");

        Equal(true, controller.Stop(), "arrival stops auto walk");
        Equal(0u, lease.FilterFieldKeys(MaskRight), "the key the game still reads is held out");
        Equal(MaskDown | MaskRun, lease.FilterFieldKeys(MaskRight | MaskDown | MaskRun),
            "while the player's own Down and run act at once");
        Equal(MaskDown, lease.FilterFieldKeys(MaskDown), "the poll that reads Right up passes the rest");
        Equal(false, lease.IsBounded, "and ends the bound");
        Equal(MaskRight, lease.FilterFieldKeys(MaskRight), "so even Right, pressed again, acts");

        // Released into neutral: the next poll that reads nothing resumes everything.
        _ = controller.TryStart(NavigationAutoWalkDomain.Field, routeActive: true);
        _ = controller.Drive(FieldNavigationInput.UpLeft, canMove: true, routeActive: true,
            pollBudget: new AutoWalkPollBudget(lease.Mark(), 30));
        controller.Suspend();
        Equal(0u, lease.FilterFieldKeys(0), "a neutral poll");
        Equal(false, lease.IsBounded, "is the end of the bound");
        Equal(0x9000u, lease.FilterFieldKeys(0x9000), "and the same diagonal acts at once after it");

        _ = controller.TryStart(NavigationAutoWalkDomain.Field, routeActive: true);
        _ = controller.Drive(FieldNavigationInput.Right, canMove: true, routeActive: true,
            pollBudget: new AutoWalkPollBudget(lease.Mark(), 30));
        controller.Suspend();
        _ = controller.Drive(FieldNavigationInput.Right, canMove: true, routeActive: true,
            pollBudget: new AutoWalkPollBudget(lease.Mark(), 1));
        Equal(0u, lease.FilterFieldKeys(0), "a poll reading nothing after a new decision");
        Equal(true, lease.IsBounded, "leaves that decision's bound in place");
        Equal(0u, lease.FilterFieldKeys(MaskRight), "and its spent budget still holds the direction out");
    }

    /// <summary>Without a budget - every other field, the world map, every minigame - every poll acts.</summary>
    private static void ElsewhereEveryPollActsAsItAlwaysHas()
    {
        var lease = new AutoWalkPollLease(releaseLagsThePoll: true);
        using var controller = new NavigationAutoWalkController(new RecordingSink(), lease);
        _ = controller.TryStart(NavigationAutoWalkDomain.Field, routeActive: true);
        _ = controller.Drive(FieldNavigationInput.Down, canMove: true, routeActive: true);
        Equal(false, lease.IsBounded, "an ordinary field direction is unbounded");
        Equal(true, Enumerable.Range(0, 1000).All(_ => lease.FilterFieldKeys(MaskDown | MaskRun) == (MaskDown | MaskRun)),
            "and every poll's mask goes back untouched");
        _ = controller.Stop();
        Equal(false, lease.IsBounded, "a stop there leaves nothing held out");
        Equal(MaskDown, lease.FilterFieldKeys(MaskDown), "the player's own direction included");

        _ = controller.TryStart(NavigationAutoWalkDomain.Field, routeActive: true);
        _ = controller.Drive(FieldNavigationInput.Right, canMove: true, routeActive: true,
            pollBudget: new AutoWalkPollBudget(lease.Mark(), 2));
        Equal(true, lease.IsBounded, "a field direction near a jaw is bounded");
        _ = controller.TryStart(NavigationAutoWalkDomain.WorldMap, routeActive: true);
        _ = controller.Drive(FieldNavigationInput.Up, canMove: true, routeActive: true);
        Equal(false, lease.IsBounded, "and a world map direction after it is not");
    }

    /// <summary>
    /// The budget is measured just before the press: where no jaw bites, none, and nothing more
    /// is read. Where one does, from a mark taken then and the position read then, in the lease's
    /// own pace. A read that fails, throws, or finds the leader in another field holds the press
    /// back; the walk goes back to the player at a jaw's touch, and wherever no poll spends it -
    /// told to let go of the direction keys first.
    /// </summary>
    private static void AFieldBudgetIsMeasuredJustBeforeThePress()
    {
        var decided = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 622, 0, 340, -930, 40, 24, 0);
        var now = decided with { X = 353, Y = -937 };
        var lease = new AutoWalkPollLease(unitsPerPoll: 24);
        lease.AttachPoller(() => true);
        var reads = 0;
        FieldPositionSnapshot? Read()
        {
            reads++;
            return now;
        }

        Equal(default, lease.ResolveFreshFieldBudget(new BoundPlanner(null, 24), decided, Read), "no jaw bites: unbounded");
        Equal(default, lease.ResolveFreshFieldBudget(null, decided, Read), "no planner: unbounded");
        Equal(0, reads, "and nothing further is read");

        for (var poll = 0; poll < 7; poll++)
        {
            _ = lease.Poll();
        }

        var planner = new BoundPlanner(2, 24);
        var measured = lease.ResolveFreshFieldBudget(planner, decided, Read);
        Equal(new AutoWalkPollBudget(new AutoWalkPollMark(7, true), 2), measured.Budget, "the mark and bound taken now");
        Equal(now, planner.LastMeasured, "at the position read now, not the decision's");
        Equal<string?>(null, measured.Handoff, "a polled lease drives");
        Equal<string?>(null, measured.Blocked, "and presses");

        Equal(true, lease.ResolveFreshFieldBudget(planner, decided, () => null).Blocked is not null, "an unreadable position holds the press back");
        Equal(true, lease.ResolveFreshFieldBudget(planner, decided, () => throw new InvalidOperationException("torn read")).Blocked is not null,
            "as does a read that throws");
        Equal(true, lease.ResolveFreshFieldBudget(planner, decided, () => now with { FieldId = 621 }).Blocked is not null,
            "and a leader already in the next field");

        var touching = lease.ResolveFreshFieldBudget(new BoundPlanner(0, 24), decided, Read);
        Equal(true, touching.Handoff?.Contains("Let go of the direction keys, then walk the rest yourself", StringComparison.Ordinal) == true,
            $"at a jaw's touch the walk goes back to the player ({touching.Handoff})");
        var unpolled = new AutoWalkPollLease(unitsPerPoll: 24);
        Equal(true, unpolled.ResolveFreshFieldBudget(planner, decided, Read).Handoff?.Contains("cannot stop it in time", StringComparison.Ordinal) == true,
            "a lease no poll spends hands the walk back");
        lease.AttachPoller(() => throw new InvalidOperationException("hook state unreadable"));
        Equal(false, lease.IsPolled, "an unreadable poller is not polled");
    }

    /// <summary>
    /// The scan that decided a direction spends sixteen polls on its route work before pressing,
    /// with two polls to spare at the leader's position - the cadence 0.10.8 logged in 622. A
    /// budget counted from before that work is spent before the press and never moves the leader
    /// (root's probe, scan after scan); measured just before the press, both polls act every time.
    /// </summary>
    private static void ASlowScanStillLeavesItsBudgetToThePress()
    {
        var planner = new BoundPlanner(2, AutoWalkPollLease.MaximumUnitsPerPoll);
        var position = new FieldPositionSnapshot(FieldPositionReader.FieldModule, 622, 0, 353, -937, 40, 24, 0);
        foreach (var fresh in new[] { false, true })
        {
            var lease = new AutoWalkPollLease();
            lease.AttachPoller(() => true);
            using var controller = new NavigationAutoWalkController(new RecordingSink(), lease);
            _ = controller.TryStart(NavigationAutoWalkDomain.Field, routeActive: true);
            // Already pressed beside the jaw, its last budget spent.
            _ = controller.Drive(FieldNavigationInput.Right, canMove: true, routeActive: true,
                pollBudget: new AutoWalkPollBudget(lease.Mark(), 0));
            var acted = 0;
            for (var scan = 0; scan < 8; scan++)
            {
                var scanMark = lease.Mark();
                acted += Enumerable.Range(0, 16).Count(_ => lease.Poll());
                var budget = fresh
                    ? lease.ResolveFreshFieldBudget(planner, position, () => position).Budget
                    : new AutoWalkPollBudget(scanMark, 2);
                _ = controller.Drive(FieldNavigationInput.Right, canMove: true, routeActive: true, pollBudget: budget);
                acted += Enumerable.Range(0, 2).Count(_ => lease.Poll());
            }

            Equal(fresh ? 16 : 0, acted, fresh
                ? "measured just before the press, both polls act after every slow scan"
                : "counted from before the scan, the budget is spent before it is pressed");
        }
    }

    private sealed class BoundPlanner(int? safe, double pace = AutoWalkPollLease.MaximumUnitsPerPoll)
        : IFieldNavigationRoutePlanner, IFieldNavigationFlytrapHoldBound
    {
        public string LastDiagnostic => string.Empty;

        public FieldPositionSnapshot? LastMeasured { get; private set; }

        public int? FlytrapSafeFrames(FieldPositionSnapshot position, double unitsPerFrame)
        {
            if (unitsPerFrame != pace)
            {
                throw new InvalidOperationException($"measured at {unitsPerFrame} a poll, not the lease's {pace}");
            }

            LastMeasured = position;
            return safe;
        }

        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            triangle = -1;
            return false;
        }

        public bool TryBuildRoute(FieldPositionSnapshot position, FieldNavigationTarget target, out FieldNavigationRoutePlan plan)
        {
            plan = null!;
            return false;
        }

        public bool TryGetNextWaypoint(FieldPositionSnapshot position, FieldNavigationTarget target, out FieldNavigationRouteWaypoint waypoint)
        {
            waypoint = default;
            return false;
        }
    }

#if BLIND_SOLDIER_LEGACY_TESTS
    /// <summary>
    /// FUN_006499F7's detour is the typed cdecl the field loop calls, and is created only over
    /// the decompiled entry, call and hand-off: any one altered - another exe, another hook -
    /// is refused before any native hook is made.
    /// </summary>
    private static void TheFieldKeyReadHookGoesOnlyOverTheDecompiledCode()
    {
        var invoke = typeof(FieldKeyPollHook.FieldKeyPollDelegate).GetMethod("Invoke")!;
        Equal(true,
            invoke.ReturnType == typeof(uint) &&
            invoke.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual([typeof(int), typeof(int)]) &&
            typeof(FieldKeyPollHook.FieldKeyPollDelegate).GetCustomAttribute<UnmanagedFunctionPointerAttribute>()!.CallingConvention == CallingConvention.Cdecl &&
            typeof(FieldKeyPollHook.FieldKeyPollDelegate).GetCustomAttribute<global::Reloaded.Hooks.Definitions.X86.FunctionAttribute>() is not null,
            "the key read is a cdecl returning the mask, two unused arguments");
        Equal(true, FieldKeyPollHook.IsDecompiledCode(new DecompiledCode(), out _), "the decompiled bytes are accepted");
        foreach (var (altered, offset) in new[]
                 {
                     (FieldKeyPollHook.KeyPollAddress, 0), (FieldKeyPollHook.KeyPollAddress, 0xAD),
                     (FieldKeyPollHook.KeyPollCallSite, 0), (FieldKeyPollHook.MovementArgumentSite, 0)
                 })
        {
            var hooks = DispatchProxy.Create<IReloadedHooks, NoHooks>();
            var lease = new AutoWalkPollLease(releaseLagsThePoll: true);
            Equal(false, FieldKeyPollHook.TryInstall(hooks, new DecompiledCode(altered, offset), lease, new KeyGlobals(),
                    out var installed, out var diagnostic),
                $"0x{altered + offset:X8} altered is refused ({diagnostic})");
            Equal(true, installed is null && ((NoHooks)(object)hooks).Calls == 0 && !lease.IsPolled,
                $"0x{altered + offset:X8} altered: no hook is made and the lease is not polled");
        }
    }

    /// <summary>
    /// The field scripts read the same keys: IFKEY the current words, IFKEYON the pressed and
    /// IFKEYOFF the released. Once the lease is spent they see what the leader moves by - the
    /// direction let go of on that poll, pressed again only when it acts again - and every other
    /// button exactly as the game read it; inside the lease nothing is written at all.
    /// </summary>
    private static void FieldScriptsSeeTheBoundedKeysToo()
    {
        const uint Ok = 0x20;
        var lease = new AutoWalkPollLease(releaseLagsThePoll: true);
        var globals = new KeyGlobals();
        using var controller = new NavigationAutoWalkController(new RecordingSink(), lease);
        _ = controller.TryStart(NavigationAutoWalkDomain.Field, routeActive: true);
        _ = controller.Drive(FieldNavigationInput.Right, canMove: true, routeActive: true,
            pollBudget: new AutoWalkPollBudget(lease.Mark(), 1));

        var held = MaskRight | MaskRun;
        Equal(held, globals.Poll(held, lease), "inside the lease the mask is the game's own");
        Equal(0, globals.Writes, "and nothing is written");
        Equal((MaskRight | MaskRun, MaskRight | MaskRun, 0u), globals.Words(), "the scripts saw Right and run pressed");

        Equal(MaskRun, globals.Poll(held, lease), "spent, the leader moves by run alone");
        Equal((MaskRun, 0u, MaskRight), globals.Words(), "and the scripts see Right let go, run still held");
        Equal((MaskRun, 0u, MaskRight), globals.WordsAgain(), "in both copies");

        Equal(MaskRun | Ok, globals.Poll(held | Ok, lease), "the player's OK goes through");
        Equal((MaskRun | Ok, Ok, 0u), globals.Words(), "pressed as the game read it, Right still not held");

        Equal(true, controller.Stop(), "auto walk stops");
        Equal(MaskDown, globals.Poll(MaskDown, lease), "the poll that reads Right up passes the player's Down");
        Equal((MaskDown, MaskDown, MaskRun | Ok), globals.Words(), "as the game derived it: nothing to rewrite");
    }

    /// <summary>
    /// A hook the backend creates but cannot activate is disposed of, handed back to its owner
    /// all the same - its detour must outlive anything the failed activation left behind - and
    /// never reported polled, so auto walk hands the jaws' fields to the player.
    /// </summary>
    private static void AHookThatCannotGoLiveStaysRootedAndUnpolled()
    {
        var hooks = DispatchProxy.Create<IReloadedHooks, FailingActivation>();
        var lease = new AutoWalkPollLease(releaseLagsThePoll: true);
        Equal(false, FieldKeyPollHook.TryInstall(hooks, new DecompiledCode(), lease, new KeyGlobals(), out var installed, out var diagnostic),
            "an activation that throws is not an installed hook");
        Equal(true, installed is not null, $"but the created hook is handed back to be kept ({diagnostic})");
        Equal(false, installed!.IsInstalled, "disposed");
        Equal(false, lease.IsPolled, "and the lease is not polled");
        installed.Dispose();
    }

    /// <summary>A backend whose field key read hook is created and then refuses to activate.</summary>
    public class FailingActivation : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method is { Name: "CreateHook", IsGenericMethod: true } &&
            method.GetGenericArguments()[0] == typeof(FieldKeyPollHook.FieldKeyPollDelegate)
                ? new UnactivatableHook()
                : throw new NotSupportedException(method?.Name);
    }

    /// <summary>A created hook that never goes live: activation throws, and it reads as off.</summary>
    private sealed class UnactivatableHook : IHook<FieldKeyPollHook.FieldKeyPollDelegate>
    {
        public FieldKeyPollHook.FieldKeyPollDelegate OriginalFunction => static (_, _) => 0;

        public IReverseWrapper<FieldKeyPollHook.FieldKeyPollDelegate> ReverseWrapper => null!;

        public bool IsHookEnabled => false;

        public bool IsHookActivated => false;

        public nint OriginalFunctionAddress => 0;

        public nint OriginalFunctionWrapperAddress => 0;

        public IHook<FieldKeyPollHook.FieldKeyPollDelegate> Activate() =>
            throw new InvalidOperationException("the backend could not write the detour");

        IHook IHook.Activate() => Activate();

        public void Disable()
        {
        }

        public void Enable()
        {
        }
    }

    /// <summary>
    /// The key words FUN_006499F7 keeps, starting from nothing held. A poll writes them exactly
    /// as it does - previous from current, current from the mask, pressed and released from
    /// both - and then hands the mask to the hook's delivery, as the detour does.
    /// </summary>
    private sealed class KeyGlobals : IFieldKeyGlobals
    {
        private readonly Dictionary<uint, uint> words = [];

        public int Writes { get; private set; }

        public uint Read(uint address) => words.TryGetValue(address, out var value) ? value : 0;

        public void Write(uint address, uint value)
        {
            Writes++;
            words[address] = value;
        }

        public uint Poll(uint mask, AutoWalkPollLease lease)
        {
            foreach (var (current, previous, pressed, released) in new[]
                     {
                         (FieldKeyPollHook.CurrentKeys, FieldKeyPollHook.PreviousKeys, FieldKeyPollHook.PressedKeys, FieldKeyPollHook.ReleasedKeys),
                         (FieldKeyPollHook.CurrentKeysAgain, FieldKeyPollHook.PreviousKeysAgain, FieldKeyPollHook.PressedKeysAgain, FieldKeyPollHook.ReleasedKeysAgain)
                     })
            {
                var before = Read(current);
                words[previous] = before;
                words[current] = mask;
                words[pressed] = (mask ^ before) & mask;
                words[released] = (mask ^ before) & ~mask;
            }

            Writes = 0;
            return FieldKeyPollHook.Deliver(mask, lease, this);
        }

        public (uint Held, uint Pressed, uint Released) Words() =>
            (Read(FieldKeyPollHook.CurrentKeys), Read(FieldKeyPollHook.PressedKeys), Read(FieldKeyPollHook.ReleasedKeys));

        public (uint Held, uint Pressed, uint Released) WordsAgain() =>
            (Read(FieldKeyPollHook.CurrentKeysAgain), Read(FieldKeyPollHook.PressedKeysAgain), Read(FieldKeyPollHook.ReleasedKeysAgain));
    }

    /// <summary>
    /// The three checks are the installed 1.02 US ff7_en.exe's own bytes, read from the file:
    /// SHA-256 4274ab2d...8225, the image every legacy native address in this mod is taken from.
    /// </summary>
    private static void TheFieldKeyReadHookMatchesTheInstalledImage()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") ??
                   Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_RUNTIME");
        var exe = string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, "ff7_en.exe");
        if (exe is null || !File.Exists(exe))
        {
            Console.WriteLine("Field key read hook: native image check not run (no installed ff7_en.exe).");
            return;
        }

        var image = File.ReadAllBytes(exe);
        Equal("4274ab2d52b67e547786fd959474e020fd3052a34dbcd7da708f86bcf5e48225",
            Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant(), "the installed image is the 1.02 US exe");
        Equal(true, FieldKeyPollHook.IsDecompiledCode(new PortableExecutable(image), out var diagnostic),
            $"its entry, call and hand-off are the hook's ({diagnostic})");
        Console.WriteLine("Field key read hook: the installed image's FUN_006499F7, its call and its hand-off match.");
    }

    public class NoHooks : DispatchProxy
    {
        public int Calls;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls++;
            throw new NotSupportedException();
        }
    }

    /// <summary>The decompiled bytes at the three addresses, one of them optionally altered at an offset.</summary>
    private sealed class DecompiledCode(uint altered = 0, int offset = 0) : ILegacyAddressSpace
    {
        public bool TryRead(uint address, Span<byte> target)
        {
            var hex = address switch
            {
                FieldKeyPollHook.KeyPollAddress => FieldKeyPollHook.ExpectedBodyHex,
                FieldKeyPollHook.KeyPollCallSite => FieldKeyPollHook.ExpectedPollCallHex,
                FieldKeyPollHook.MovementArgumentSite => FieldKeyPollHook.ExpectedMovementArgumentHex,
                _ => null
            };
            if (hex is null || target.Length > hex.Length / 2)
            {
                return false;
            }

            var bytes = Convert.FromHexString(hex);
            if (address == altered)
            {
                // A jmp over the entry, a redirected call, or another key word in the body's
                // writes: not the code these addresses were taken from.
                bytes[offset] ^= 0xFF;
            }

            bytes.AsSpan(0, target.Length).CopyTo(target);
            return true;
        }
    }

    /// <summary>A PE file's sections at their image addresses.</summary>
    private sealed class PortableExecutable(byte[] file) : ILegacyAddressSpace
    {
        public bool TryRead(uint address, Span<byte> target)
        {
            var header = BitConverter.ToInt32(file, 0x3C);
            var sections = BitConverter.ToUInt16(file, header + 6);
            var optionalSize = BitConverter.ToUInt16(file, header + 20);
            var imageBase = BitConverter.ToUInt32(file, header + 24 + 28);
            for (var index = 0; index < sections; index++)
            {
                var entry = header + 24 + optionalSize + index * 40;
                var start = imageBase + BitConverter.ToUInt32(file, entry + 12);
                var rawSize = BitConverter.ToUInt32(file, entry + 16);
                var rawStart = BitConverter.ToUInt32(file, entry + 20);
                if (address >= start && address - start + (uint)target.Length <= rawSize)
                {
                    file.AsSpan((int)(rawStart + address - start), target.Length).CopyTo(target);
                    return true;
                }
            }

            return false;
        }
    }
#endif

    private static void NativeLockerRoomCornerRemainsUntilTheNextLegIsWalkable(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var target = new FieldNavigationTarget(
            387, FieldNavigationCategory.Story, "Leave the locker room for the send-off",
            -1271, -652, 605, "gateway:387:0:386", CompletesOnArrival: true,
            DestinationFieldIds: [386],
            TriggerLine: new FieldNavigationTriggerLine(-1269, -688, 605, -1272, -615, 605));
        // Replay the 2026-09-05 19:27:16-19:27:17Z native observations, with both
        // the logged model elevation and the walkmesh elevation. The next corner
        // is only 16 units away at (-1546,-764), but its continuation crosses a wall.
        foreach (var z in new[] { 595, 605 })
        {
            var reader = createWalkmeshReader(387);
            var tracker = new FieldNavigationRouteTracker(new FieldWalkmeshRoutePlanner(reader));
            var samples = new (int X, int Y, ushort Triangle)[]
            {
                (-1561, -796, 22), (-1554, -786, 22), (-1550, -771, 21),
                (-1546, -764, 21), (-1544, -760, 21), (-1541, -757, 21)
            };
            foreach (var (sample, index) in samples.Select((sample, index) => (sample, index)))
            {
                var position = new FieldPositionSnapshot(
                    FieldPositionReader.FieldModule, 387, 0,
                    sample.X, sample.Y, z, sample.Triangle, 0);
                FieldNavigationRouteGuidance guidance;
                Equal(true, index == 0
                        ? tracker.TryStart(position, target, out guidance)
                        : tracker.TryUpdate(position, target, out guidance),
                    "the native locker-room replay retains its route");
                AssertClearGuidance(position, guidance);
            }

            // This native triangle-20 position is beyond the portal. The rounded
            // funnel endpoint (-1535,-753) itself still resolves to triangle 21.
            var corner = new FieldPositionSnapshot(
                FieldPositionReader.FieldModule, 387, 0, -1534, -748, z, 20, 0);
            Equal(true, tracker.TryUpdate(corner, target, out var beyondCorner),
                "reaching the native corner retains forward guidance");
            Equal(true, beyondCorner.Waypoint.X > corner.X,
                "the route advances after the next native segment becomes walkable");
            AssertClearGuidance(corner, beyondCorner);

            // Starting again at the actual stopped position must also survive a
            // stationary update instead of immediately dropping its nearby corner.
            var stopped = corner with { X = -1541, Y = -757, TriangleId = 21 };
            Equal(true, tracker.TryStart(stopped, target, out _),
                "a fresh route can start at the logged stopping point");
            Equal(true, tracker.TryUpdate(stopped, target, out var stationary),
                "the unchanged stopping point retains guidance");
            AssertClearGuidance(stopped, stationary);

            foreach (var freshAtCorner in new[] { false, true })
            {
                var exactCorner = corner with { X = -1535, Y = -753, TriangleId = 21 };
                var origin = freshAtCorner
                    ? exactCorner
                    : exactCorner with { X = -1561, Y = -796, TriangleId = 22 };
                var controller = new FieldNavigationController(
                    new FieldNavigationTargetSource([target]), new FieldWalkmeshRoutePlanner(reader));
                var transform = new FieldNavigationControlTransform(-128);
                var noInput = new FieldNavigationInputSnapshot(0, FieldNavigationInput.None);
                controller.HandleAction(FieldNavigationAction.NextCategory, origin, transform);
                controller.HandleAction(FieldNavigationAction.ToggleBeacon, origin, transform);
                if (!freshAtCorner)
                {
                    foreach (var sample in samples.Skip(1))
                    {
                        controller.UpdateLiveTracking(origin with
                        {
                            X = sample.X, Y = sample.Y, TriangleId = sample.Triangle
                        }, noInput, transform, false, 80);
                    }
                }

                controller.UpdateLiveTracking(exactCorner, noInput, transform, false, 80);
                Equal(true, controller.TryResolveAutomaticInput(exactCorner, transform, 80, out _),
                    $"fresh={freshAtCorner}: exact native corner must keep a nonzero automatic direction");
                var handoff = controller.CurrentRouteGuidance!.Value.Waypoint;
                var firstLeg = FieldWalkmeshPathfinder.TraceWalkableSegment(
                    reader.Read(exactCorner).Walkmesh!, 21,
                    new FieldNavigationRouteWaypoint(exactCorner.X, exactCorner.Y, exactCorner.Z), handoff);
                Equal(true, firstLeg.IsClear,
                    $"fresh={freshAtCorner}: the exact corner handoff is native-walkable: {firstLeg.Diagnostic}");
                Equal(20, firstLeg.EndTriangle,
                    "the short handoff crosses the native portal into its destination triangle");
                for (var sample = 1; sample < 4; sample++)
                {
                    var between = exactCorner with
                    {
                        X = (int)Math.Round(exactCorner.X + (handoff.X - exactCorner.X) * sample / 4d),
                        Y = (int)Math.Round(exactCorner.Y + (handoff.Y - exactCorner.Y) * sample / 4d)
                    };
                    controller.UpdateLiveTracking(between, noInput, transform, false, 80);
                    Equal(true, controller.TryResolveAutomaticInput(between, transform, 80, out _),
                        "quantized movement across the corner handoff keeps driving");
                    var intermediateTrace = FieldWalkmeshPathfinder.TraceWalkableSegment(
                        reader.Read(between).Walkmesh!, 21,
                        new FieldNavigationRouteWaypoint(between.X, between.Y, between.Z),
                        controller.CurrentRouteGuidance!.Value.Waypoint);
                    Equal(true, intermediateTrace.IsClear,
                        $"each corner handoff sample keeps a clear next leg: {intermediateTrace.Diagnostic}");
                }

                var afterHandoff = exactCorner with
                {
                    X = handoff.X, Y = handoff.Y, TriangleId = (ushort)firstLeg.EndTriangle
                };
                controller.UpdateLiveTracking(afterHandoff, noInput, transform, false, 80);
                Equal(true, controller.TryResolveAutomaticInput(afterHandoff, transform, 80, out _),
                    "reaching the portal handoff must continue automatically toward the exit");
                var secondLeg = FieldWalkmeshPathfinder.TraceWalkableSegment(
                    reader.Read(afterHandoff).Walkmesh!, firstLeg.EndTriangle,
                    new FieldNavigationRouteWaypoint(afterHandoff.X, afterHandoff.Y, afterHandoff.Z),
                    controller.CurrentRouteGuidance!.Value.Waypoint);
                Equal(true, secondLeg.IsClear,
                    $"the handoff reconnects to a clear forward route: {secondLeg.Diagnostic}");
            }

            void AssertClearGuidance(
                FieldPositionSnapshot position,
                FieldNavigationRouteGuidance guidance)
            {
                var probe = tracker.CurrentProbeSnapshot!;
                var trace = FieldWalkmeshPathfinder.TraceWalkableSegment(
                    reader.Read(position).Walkmesh!, probe.ResolvedTriangle,
                    new FieldNavigationRouteWaypoint(position.X, position.Y, position.Z),
                    guidance.Waypoint);
                Equal(true, trace.IsClear,
                    $"locker-room ({position.X},{position.Y},{position.Z}) next step must be native-walkable: " +
                    $"{trace.Diagnostic}; {guidance.Diagnostic}");
            }
        }
    }

    private static void NativeCornerPortalHandoffHonorsTraversalBlocks(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var position = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule, 387, 0, -1535, -753, 605, 21, 0);
        var target = new FieldNavigationTarget(
            387, FieldNavigationCategory.Story, "Locker-room gateway", -1271, -652, 605,
            CompletesOnArrival: true, DestinationFieldIds: [386],
            TriggerLine: new FieldNavigationTriggerLine(-1269, -688, 605, -1272, -615, 605));
        var reader = createWalkmeshReader(387);
        var planner = new FieldWalkmeshRoutePlanner(reader);
        Equal(true, planner.TryBuildRoute(position, target, out var plan),
            "the corner handoff safety fixture has a native route");
        var tracker = new FieldNavigationRouteTracker(planner);
        Equal(true, tracker.TryStart(position, target, out _),
            "the corner handoff safety fixture has stable steps");
        var steps = tracker.CurrentProbeSnapshot!.StableWaypoints;
        var mesh = reader.Read(position).Walkmesh!;
        var clear = Observe();
        Equal(true, IsHandoff(clear), "the unblocked exact corner uses a traced portal handoff");
        Equal(false, IsHandoff(Observe(isTriangleBlocked: triangle => triangle == 20)),
            "a locked destination triangle forbids the corner handoff");
        FieldNavigationDynamicObstacle[] obstacles =
        [
            new(4, clear.Waypoint.X, clear.Waypoint.Y, clear.Waypoint.Z, 4d)
        ];
        Equal(false, IsHandoff(Observe(obstacles: obstacles)),
            "a native actor cylinder on the handoff forbids that movement");
        var action = new FieldNavigationRouteAction(
            FieldNavigationTransitionKind.Ladder, "pending-native-action", plan.Portals[0].Midpoint,
            FieldNavigationInput.Up, RequiresAction: true, PortalIndex: 0);
        var actionBlocked = Observe(action: action);
        Equal(false, IsHandoff(actionBlocked), "a pending native action forbids walking through its portal");
        Equal(action.Waypoint, actionBlocked.Waypoint,
            "the blocked route retains the native action approach");

        FieldNavigationCorridorObservation Observe(
            Func<int, bool>? isTriangleBlocked = null,
            IReadOnlyList<FieldNavigationDynamicObstacle>? obstacles = null,
            FieldNavigationRouteAction? action = null)
        {
            Equal(true, FieldNavigationCorridorLookahead.TryResolve(
                    mesh, 21, position, plan, steps, 0, action, default,
                    isTriangleBlocked, obstacles, out var observation),
                "the native corner still provides an observation when blocked");
            return observation;
        }

        static bool IsHandoff(FieldNavigationCorridorObservation observation) =>
            observation.Diagnostic.Contains("corner-portal-handoff", StringComparison.Ordinal);
    }

    private static void SamplesPAsAForegroundRisingEdgeWithoutDelayedActivation()
    {
        var tracker = new NavigationKeyPressTracker();
        var isDown = false;
        var isForeground = true;
        bool Observe(int virtualKey) => tracker.Observe(virtualKey, isDown, isForeground);

        Equal(false, NavigationAutoWalkKeyRouter.ObserveToggle(Observe),
            "released P has no action");
        isDown = true;
        Equal(true, NavigationAutoWalkKeyRouter.ObserveToggle(Observe),
            "foreground P press starts one toggle");
        Equal(false, NavigationAutoWalkKeyRouter.ObserveToggle(Observe),
            "held P cannot toggle repeatedly");

        isDown = false;
        _ = NavigationAutoWalkKeyRouter.ObserveToggle(Observe);
        isForeground = false;
        isDown = true;
        Equal(false, NavigationAutoWalkKeyRouter.ObserveToggle(Observe),
            "background P is observed but suppressed");
        isForeground = true;
        Equal(false, NavigationAutoWalkKeyRouter.ObserveToggle(Observe),
            "a P key held across refocus cannot start walking late");
        isDown = false;
        _ = NavigationAutoWalkKeyRouter.ObserveToggle(Observe);
        isDown = true;
        Equal(true, NavigationAutoWalkKeyRouter.ObserveToggle(Observe),
            "a fresh foreground press works after release");
    }

    private static void StartsOnlyForAnActiveRouteAndTogglesOffCleanly()
    {
        var sink = new RecordingSink();
        using var controller = new NavigationAutoWalkController(sink);

        Equal(false, controller.TryStart(NavigationAutoWalkDomain.Field, routeActive: false),
            "auto walk cannot start without the selected route");
        Equal(false, controller.Enabled, "failed start leaves auto walk off");
        Equal(true, controller.TryStart(NavigationAutoWalkDomain.Field, routeActive: true),
            "P starts auto walk after navigation locks the selected target");
        Equal(true, controller.IsEnabledFor(NavigationAutoWalkDomain.Field),
            "field route owns auto walk");

        _ = controller.Drive(FieldNavigationInput.Right, canMove: true, routeActive: true);
        Equal(true, controller.Stop(), "second P stops active auto walk");
        SequenceEqual(
            [new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeRight, false)],
            sink.Batches[^1],
            "stopping releases the owned direction");
        Equal(false, controller.Stop(), "stopping an inactive route is idempotent");
    }

    private static void DrivesTheCurrentRouteDirectionAndReleasesDuringSuspension()
    {
        var sink = new RecordingSink();
        using var controller = new NavigationAutoWalkController(sink);
        _ = controller.TryStart(NavigationAutoWalkDomain.Field, routeActive: true);

        var driven = controller.Drive(FieldNavigationInput.UpLeft, canMove: true, routeActive: true);
        Equal(true, driven.Success, "route direction is injected");
        SequenceEqual(
            [
                new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeUp, true),
                new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeLeft, true)
            ],
            sink.Batches.Single(),
            "diagonal route owns both matching arrow keys");

        controller.Suspend();
        SequenceEqual(
            [
                new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeUp, false),
                new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeLeft, false)
            ],
            sink.Batches[^1],
            "battle, focus, or frame suspension releases every direction");
        Equal(true, controller.Enabled, "temporary suspension retains user intent");

        _ = controller.Drive(FieldNavigationInput.Down, canMove: true, routeActive: true);
        Equal(
            new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeDown, true),
            sink.Batches[^1].Single(),
            "the same route resumes after coherent navigation returns");

        _ = controller.Drive(FieldNavigationInput.Down, canMove: true, routeActive: false);
        Equal(false, controller.Enabled, "completed or failed navigation disables auto walk");
        Equal(
            new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeDown, false),
            sink.Batches[^1].Single(),
            "route completion releases the final key");
    }

    private static void FailsClosedAfterPartialDirectionalInputFailure()
    {
        var sink = new RecordingSink();
        sink.Results.Enqueue(new HighwayKeyboardSendResult(1, 5));
        sink.Results.Enqueue(new HighwayKeyboardSendResult(1, 0));
        using var controller = new NavigationAutoWalkController(sink);
        _ = controller.TryStart(NavigationAutoWalkDomain.Field, routeActive: true);

        var result = controller.Drive(FieldNavigationInput.UpRight, canMove: true, routeActive: true);

        Equal(false, result.Success, "partial SendInput fails closed");
        Equal(false, controller.Enabled, "input failure disables automatic movement");
        Equal(
            true,
            controller.LastDiagnostic.Contains("inserted 1 of 2", StringComparison.OrdinalIgnoreCase),
            "failure preserves actionable input diagnostics");
    }

    private static void ReassertsAnOwnedDirectionWhenTheGameStopsReportingIt()
    {
        var sink = new RecordingSink();
        using var controller = new NavigationAutoWalkController(sink);
        _ = controller.TryStart(NavigationAutoWalkDomain.Field, routeActive: true);

        _ = controller.Drive(
            FieldNavigationInput.Left,
            canMove: true,
            routeActive: true,
            observedInput: FieldNavigationInput.None);
        Equal(1, sink.Batches.Count, "the initial route direction is pressed once");

        _ = controller.Drive(
            FieldNavigationInput.Left,
            canMove: true,
            routeActive: true,
            observedInput: FieldNavigationInput.None);
        _ = controller.Drive(
            FieldNavigationInput.Left,
            canMove: true,
            routeActive: true,
            observedInput: FieldNavigationInput.None);
        _ = controller.Drive(
            FieldNavigationInput.Left,
            canMove: true,
            routeActive: true,
            observedInput: FieldNavigationInput.None);

        Equal(3, sink.Batches.Count,
            "three missing native samples reassert the swallowed route direction");
        SequenceEqual(
            [new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeLeft, false)],
            sink.Batches[1],
            "reassertion first releases the stale owned key");
        SequenceEqual(
            [new HighwayKeyboardTransition(HighwayAutoSteeringController.ScanCodeLeft, true)],
            sink.Batches[2],
            "reassertion presses the required route direction again");
    }

    private static void ResolvesFieldRouteAndMountedLadderDirections()
    {
        var target = new FieldNavigationTarget(
            500,
            FieldNavigationCategory.Story,
            "Test destination",
            0,
            -1000,
            0,
            "test-destination",
            CompletesOnArrival: true);
        var planner = new StraightRoutePlanner();
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([target]),
            planner);
        var position = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule,
            500,
            0,
            0,
            0,
            0,
            0,
            0);
        var transform = new FieldNavigationControlTransform(0);

        _ = controller.HandleAction(FieldNavigationAction.NextCategory, position, transform);
        var activation = controller.HandleAction(FieldNavigationAction.ToggleBeacon, position, transform);
        Equal(true, controller.BeaconEnabled,
            $"test route activates; speech={activation?.Speech ?? "none"}; diagnostic={controller.LastNavigationDiagnostic}");
        Equal(true, controller.CurrentRouteGuidance is not null,
            $"test route has guidance; diagnostic={controller.LastNavigationDiagnostic}");
        Equal(
            true,
            controller.TryResolveAutomaticInput(position, transform, 80, out var routeInput),
            "active route exposes a safe automatic direction");
        Equal(FieldNavigationInput.Up, routeInput, "camera-relative route direction is reused");

        controller.Reset();
        var mounted = FieldLadderStateSnapshot.NotMounted with
        {
            IsMounted = true,
            Phase = FieldLadderPhase.Climbing,
            RequiredInput = FieldNavigationInput.Left,
            Target = new FieldNavigationRouteWaypoint(0, -1000, 500),
            TargetTriangle = 0
        };
        _ = controller.HandleAction(FieldNavigationAction.ToggleBeacon, position, transform, mounted);
        Equal(
            true,
            controller.TryResolveAutomaticInput(position, transform, 80, out var ladderInput),
            "mounted ladder exposes its native route-owned climb input");
        Equal(FieldNavigationInput.Left, ladderInput, "auto walk follows the proven ladder direction");
    }

    private static void NativeExitsKeepAutomaticDirectionInsideTheProximityRadius()
    {
        var gateway = new FieldNavigationTarget(
            500, FieldNavigationCategory.Exits, "Next area", 100, 0, 0,
            "gateway:500:0:501", CompletesOnArrival: true, DestinationFieldIds: [501]);
        var line = gateway with
        {
            StableId = "script-exit:500:1:501",
            TriggerLine = new FieldNavigationTriggerLine(100, -50, 0, 100, 50, 0),
            InteractionRadius = 128
        };
        foreach (var (target, shortX) in new[] { (gateway, 50), (line, 90) })
        {
            var start = new FieldPositionSnapshot(1, 500, 0, 0, 0, 0, 0, 0);
            var controller = StartFieldRoute(target, start);
            var shortOfExit = start with { X = shortX };
            var update = controller.UpdateLiveTracking(
                shortOfExit, new FieldNavigationInputSnapshot(0, FieldNavigationInput.None),
                new FieldNavigationControlTransform(0), isSuppressed: false, arrivalDistanceUnits: 80);

            Equal(true, controller.BeaconEnabled, "an exit is not complete before its crossing");
            Equal(true, controller.TryResolveAutomaticInput(
                shortOfExit, new FieldNavigationControlTransform(0), 80, out var input),
                $"{target.StableId} must keep driving inside its proximity radius");
            Equal(FieldNavigationInput.Left, input, "control byte zero maps positive world X to Left");
            Equal(false, (update?.Speech ?? "").Contains("reached", StringComparison.Ordinal),
                "proximity must not announce native exit completion");
            if (target.TriggerLine is null)
            {
                Equal(true, controller.CreateSpokenGuidance(
                    shortOfExit, new FieldNavigationControlTransform(0), 80) is not null,
                    "spoken guidance must keep describing the unfinished gateway approach");
            }
        }
    }

    private static void StoryCrossingsDoNotCompleteAtTheOrdinaryArrivalRadius()
    {
        var target = new FieldNavigationTarget(
            500, FieldNavigationCategory.Story, "Cross the story line", 100, 0, 0,
            "story:crossing", CompletesOnArrival: true,
            TriggerLine: new FieldNavigationTriggerLine(100, -50, 0, 100, 50, 0));
        var start = new FieldPositionSnapshot(1, 500, 0, 0, 0, 0, 0, 0);
        var controller = StartFieldRoute(target, start);
        var shortOfLine = start with { X = 50 };
        var update = controller.UpdateLiveTracking(
            shortOfLine, new FieldNavigationInputSnapshot(0, FieldNavigationInput.None),
            new FieldNavigationControlTransform(0), isSuppressed: false, arrivalDistanceUnits: 80);

        Equal(true, controller.BeaconEnabled, "a Story LINE is not reached 50 units before crossing");
        Equal(false, (update?.Speech ?? "").Contains("reached", StringComparison.Ordinal),
            "Story category must not turn crossing proximity into completion");
        Equal(true, controller.TryResolveAutomaticInput(
            shortOfLine, new FieldNavigationControlTransform(0), 80, out var input),
            "automatic movement continues to the actual Story crossing");
        Equal(FieldNavigationInput.Left, input, "the Story crossing remains ahead in native control space");

        _ = controller.UpdateLiveTracking(
            start with { X = 100 }, new FieldNavigationInputSnapshot(0, FieldNavigationInput.None),
            new FieldNavigationControlTransform(0), isSuppressed: false, arrivalDistanceUnits: 80);
        Equal(false, controller.BeaconEnabled, "the same-field Story LINE completes at its crossing");
    }

    private static void StoryGatewayCrossingsWaitForTheNativeFieldTransition()
    {
        var line = new FieldNavigationTriggerLine(100, -50, 0, 100, 50, 0);
        var nativeExit = new FieldNavigationTarget(
            500, FieldNavigationCategory.Exits, "Next area", 100, 0, 0,
            "gateway:500:0:501", CompletesOnArrival: true,
            DestinationFieldIds: [501], TriggerLine: line);
        var storyLine = nativeExit with
        {
            Category = FieldNavigationCategory.Story,
            StableId = "story:gateway-line",
            DestinationFieldIds = null
        };
        var directStoryGateway = storyLine with
        {
            StableId = "story:direct-gateway",
            TriggerLine = null,
            DestinationFieldIds = [501]
        };
        foreach (var target in new[] { storyLine, directStoryGateway })
        {
            var start = new FieldPositionSnapshot(1, 500, 0, 0, 0, 0, 0, 0);
            var controller = StartFieldRoute(target, start, [nativeExit]);
            var shortOfGateway = start with { X = 50 };
            var update = controller.UpdateLiveTracking(
                shortOfGateway, new FieldNavigationInputSnapshot(0, FieldNavigationInput.None),
                new FieldNavigationControlTransform(0), isSuppressed: false, arrivalDistanceUnits: 80);

            Equal(true, controller.BeaconEnabled, "a Story gateway keeps its route before transition");
            Equal(false, (update?.Speech ?? "").Contains("Interact", StringComparison.Ordinal),
                "a native crossing must never become an interaction prompt");
            Equal(true, controller.TryResolveAutomaticInput(
                shortOfGateway, new FieldNavigationControlTransform(0), 80, out var input),
                "a Story gateway keeps automatic movement inside the ordinary arrival radius");
            Equal(FieldNavigationInput.Left, input, "a Story gateway remains ahead in native control space");

            var transition = controller.UpdateLiveTracking(
                shortOfGateway with { FieldId = 501 },
                new FieldNavigationInputSnapshot(0, FieldNavigationInput.None),
                new FieldNavigationControlTransform(0), isSuppressed: false, arrivalDistanceUnits: 80);
            Equal(false, controller.BeaconEnabled, "the matching native field transition ends the route");
            Equal(true, (transition?.Speech ?? "").Contains("reached", StringComparison.Ordinal),
                "the actual Story destination transition confirms completion");
        }
    }

    private static void InteractionTargetsKeepTheirArrivalRadius()
    {
        foreach (var category in new[] { FieldNavigationCategory.Story, FieldNavigationCategory.Objects })
        {
            var target = new FieldNavigationTarget(
                500, category, "Interactable", 1000, 0, 0, "interaction:test",
                CompletesOnArrival: false, InteractionRadius: 128,
                TriggerLine: new FieldNavigationTriggerLine(1000, -50, 0, 1000, 50, 0));
            var start = new FieldPositionSnapshot(1, 500, 0, 0, 0, 0, 0, 0);
            var controller = StartFieldRoute(target, start);
            var inRange = start with { X = 900 };
            var update = controller.UpdateLiveTracking(
                inRange, new FieldNavigationInputSnapshot(0, FieldNavigationInput.None),
                new FieldNavigationControlTransform(0), isSuppressed: false, arrivalDistanceUnits: 80);

            Equal(true, (update?.Speech ?? "").Contains("Interact here", StringComparison.Ordinal),
                "an interaction target still pauses at its native radius");
            Equal(false, controller.TryResolveAutomaticInput(
                inRange, new FieldNavigationControlTransform(0), 80, out _),
                "automatic walking must not push through an interaction prompt");
        }
    }

    private static FieldNavigationController StartFieldRoute(
        FieldNavigationTarget target,
        FieldPositionSnapshot position,
        IReadOnlyList<FieldNavigationTarget>? exits = null)
    {
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([target], exitTargetProvider: exits is null ? null : _ => exits),
            new StraightRoutePlanner());
        var categorySteps = target.Category switch
        {
            FieldNavigationCategory.Story => 1,
            FieldNavigationCategory.Npcs => 2,
            FieldNavigationCategory.Objects => 3,
            _ => 0
        };
        for (var index = 0; index < categorySteps; index++)
        {
            _ = controller.HandleAction(FieldNavigationAction.NextCategory, position);
        }
        _ = controller.HandleAction(
            FieldNavigationAction.ToggleBeacon, position, new FieldNavigationControlTransform(0));
        Equal(true, controller.BeaconEnabled, $"test route to {target.StableId} is active");
        return controller;
    }

    private sealed class StraightRoutePlanner : IFieldNavigationRoutePlanner
    {
        public string LastDiagnostic => "straight test route";

        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            triangle = position.TriangleId;
            return true;
        }

        public bool TryBuildRoute(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRoutePlan plan)
        {
            plan = new FieldNavigationRoutePlan(
                position.FieldId,
                $"{target.FieldId}:{target.StableId}",
                [position.TriangleId],
                [],
                new FieldNavigationRouteWaypoint(target.X, target.Y, target.Z),
                position.TriangleId);
            return position.FieldId == target.FieldId;
        }

        public bool TryGetNextWaypoint(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRouteWaypoint waypoint)
        {
            waypoint = new FieldNavigationRouteWaypoint(target.X, target.Y, target.Z);
            return position.FieldId == target.FieldId;
        }
    }

    private sealed class RecordingSink : IHighwayKeyboardInputSink
    {
        internal List<IReadOnlyList<HighwayKeyboardTransition>> Batches { get; } = [];
        internal Queue<HighwayKeyboardSendResult> Results { get; } = new();

        public HighwayKeyboardSendResult Send(IReadOnlyList<HighwayKeyboardTransition> transitions)
        {
            Batches.Add(transitions.ToArray());
            return Results.Count > 0
                ? Results.Dequeue()
                : new HighwayKeyboardSendResult(transitions.Count, 0);
        }
    }

    private static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string label)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
