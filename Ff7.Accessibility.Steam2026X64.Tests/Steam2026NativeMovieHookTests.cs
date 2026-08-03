using System.Reflection;
using System.Text;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime.Movies;
using Reloaded.Hooks.Definitions;

internal static class Steam2026NativeMovieHookTests
{
    private const int CanonicalPathCapacity = 0x104;
    private const ulong CanonicalPathRva = 0x0207CF10;
    private const ulong MovieObjectPointerRva = 0x0207CF08;
    private const ulong StartedStateOffset = 0x01FC;

    private static readonly NativeMovieCallbackKind[] HookKinds =
    [
        NativeMovieCallbackKind.Prepare,
        NativeMovieCallbackKind.Release,
        NativeMovieCallbackKind.Start,
        NativeMovieCallbackKind.Stop
    ];

    internal static void Run(Steam2026FingerprintResult supportedRuntime)
    {
        HookLeaseUsesExactCallbackTableAfterDetoursReplaceEntryBytes(supportedRuntime);
        HookLeaseRequiresTheFullEnabledCohortAndRevokesCleanly(supportedRuntime);
        StateReaderCopiesStableBoundedPathAndStartState(supportedRuntime);
        StateReaderRejectsPathPointerAndStateTears(supportedRuntime);
        OpeningVirtualIdentityMapsOnlyToTheValidatedPhysicalPath(supportedRuntime);
        HookSetOwnsExactlyTheFourProvenCallbacks();
    }

    private static void HookLeaseUsesExactCallbackTableAfterDetoursReplaceEntryBytes(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = MovieIngressFixture.Create(supportedRuntime);
        WriteExactCallbackTable(fixture.Memory);
        var identities = GetHookIdentities(fixture.Contract);

        foreach (var kind in HookKinds)
        {
            fixture.Corrupt(kind);
        }

        fixture.Contract.ActivateHookLease(identities, _ => true);
        Equal(
            true,
            fixture.Contract.TryCapturePrepare(
                identities[NativeMovieCallbackKind.Prepare],
                new DateTime(2026, 7, 20, 20, 0, 0, DateTimeKind.Utc),
                @"X:\Games\FF7\data\movies\opening.avi",
                true,
                out _),
            "active movie hook lease accepts the pre-detour exact identity");

        fixture.Memory.Write(
            MovieIngressFixture.ModuleBase + GetCallbackRecordRva(NativeMovieCallbackKind.Stop) + 8,
            BitConverter.GetBytes(MovieIngressFixture.ModuleBase + 0x1234));
        Equal(
            false,
            fixture.Contract.TryCapturePrepare(
                identities[NativeMovieCallbackKind.Prepare],
                new DateTime(2026, 7, 20, 20, 0, 1, DateTimeKind.Utc),
                @"X:\Games\FF7\data\movies\opening.avi",
                true,
                out _),
            "active movie hook lease rejects one stale callback-table record");
    }

    private static void HookLeaseRequiresTheFullEnabledCohortAndRevokesCleanly(
        Steam2026FingerprintResult supportedRuntime)
    {
        var incomplete = MovieIngressFixture.Create(supportedRuntime);
        WriteExactCallbackTable(incomplete.Memory);
        var incompleteIdentities = GetHookIdentities(incomplete.Contract);
        Equal(
            true,
            Throws<InvalidOperationException>(() => incomplete.Contract.ActivateHookLease(
                incompleteIdentities,
                kind => kind != NativeMovieCallbackKind.Stop)),
            "movie hook lease rejects an incomplete enabled cohort");

        var fixture = MovieIngressFixture.Create(supportedRuntime);
        WriteExactCallbackTable(fixture.Memory);
        var identities = GetHookIdentities(fixture.Contract);
        foreach (var kind in HookKinds)
        {
            fixture.Corrupt(kind);
        }

        fixture.Contract.ActivateHookLease(identities, _ => true);
        fixture.Contract.RevokeHookLease();
        Equal(
            false,
            fixture.Contract.TryCapturePrepare(
                identities[NativeMovieCallbackKind.Prepare],
                new DateTime(2026, 7, 20, 20, 1, 0, DateTimeKind.Utc),
                @"X:\Games\FF7\data\movies\opening.avi",
                true,
                out _),
            "revoked movie hook lease no longer accepts patched entry bytes");
        fixture.Contract.RevokeHookLease();
    }

    private static void StateReaderCopiesStableBoundedPathAndStartState(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = MovieIngressFixture.Create(supportedRuntime);
        const ulong movieObject = 0x0000000500000000;
        const string expectedPath =
            @"X:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\ff7\workingdir\data\movies\opening.avi";
        fixture.Memory.MapRegion(
            movieObject,
            0x1000,
            movieObject,
            isCommitted: true,
            isExecutable: false,
            isImage: false,
            isReadable: true);
        WriteCanonicalPath(fixture.Memory, expectedPath);
        fixture.Memory.Write(
            MovieIngressFixture.ModuleBase + MovieObjectPointerRva,
            BitConverter.GetBytes(movieObject));
        fixture.Memory.Write(movieObject + StartedStateOffset, BitConverter.GetBytes(1));

        var reader = new Steam2026NativeMovieStateReader(
            supportedRuntime,
            MovieIngressFixture.ModuleBase,
            MovieIngressFixture.ModuleImageSize,
            fixture.Memory);
        Equal(true, reader.TryReadCanonicalPath(out var path), "stable canonical movie path read");
        Equal(expectedPath, path, "canonical movie path copied exactly");
        Equal(true, reader.TryReadStartState(out var state), "stable movie start state read");
        Equal(1, state, "movie start state copied exactly");

        fixture.Memory.Write(
            MovieIngressFixture.ModuleBase + CanonicalPathRva,
            Enumerable.Repeat((byte)'A', CanonicalPathCapacity).ToArray());
        Equal(
            false,
            reader.TryReadCanonicalPath(out _),
            "unterminated maximum-length movie path rejected");
    }

    private static void StateReaderRejectsPathPointerAndStateTears(
        Steam2026FingerprintResult supportedRuntime)
    {
        const ulong firstObject = 0x0000000500000000;
        const ulong secondObject = 0x0000000500002000;

        var pathFixture = MovieIngressFixture.Create(supportedRuntime);
        WriteCanonicalPath(pathFixture.Memory, @"X:\Games\FF7\data\movies\opening.avi");
        var tearingPathMemory = new TearingNativeMemoryReader(
            pathFixture.Memory,
            MovieIngressFixture.ModuleBase + CanonicalPathRva,
            2,
            () => WriteCanonicalPath(pathFixture.Memory, @"X:\Games\FF7\data\movies\ending.avi"));
        var pathReader = new Steam2026NativeMovieStateReader(
            supportedRuntime,
            MovieIngressFixture.ModuleBase,
            MovieIngressFixture.ModuleImageSize,
            tearingPathMemory);
        Equal(false, pathReader.TryReadCanonicalPath(out _), "torn canonical movie path rejected");

        var pointerFixture = MovieIngressFixture.Create(supportedRuntime);
        MapMovieObject(pointerFixture.Memory, firstObject, 1);
        MapMovieObject(pointerFixture.Memory, secondObject, 1);
        pointerFixture.Memory.Write(
            MovieIngressFixture.ModuleBase + MovieObjectPointerRva,
            BitConverter.GetBytes(firstObject));
        var remappingPointerMemory = new RemappingNativeMemoryReader(
            pointerFixture.Memory,
            MovieIngressFixture.ModuleBase + MovieObjectPointerRva,
            2,
            () => pointerFixture.Memory.Write(
                MovieIngressFixture.ModuleBase + MovieObjectPointerRva,
                BitConverter.GetBytes(secondObject)));
        var pointerReader = new Steam2026NativeMovieStateReader(
            supportedRuntime,
            MovieIngressFixture.ModuleBase,
            MovieIngressFixture.ModuleImageSize,
            remappingPointerMemory);
        Equal(false, pointerReader.TryReadStartState(out _), "torn movie object pointer rejected");

        var stateFixture = MovieIngressFixture.Create(supportedRuntime);
        MapMovieObject(stateFixture.Memory, firstObject, 1);
        stateFixture.Memory.Write(
            MovieIngressFixture.ModuleBase + MovieObjectPointerRva,
            BitConverter.GetBytes(firstObject));
        var tearingStateMemory = new TearingNativeMemoryReader(
            stateFixture.Memory,
            firstObject + StartedStateOffset,
            2,
            () => stateFixture.Memory.Write(
                firstObject + StartedStateOffset,
                BitConverter.GetBytes(2)));
        var stateReader = new Steam2026NativeMovieStateReader(
            supportedRuntime,
            MovieIngressFixture.ModuleBase,
            MovieIngressFixture.ModuleImageSize,
            tearingStateMemory);
        Equal(false, stateReader.TryReadStartState(out _), "torn movie start state rejected");
    }

    private static void HookSetOwnsExactlyTheFourProvenCallbacks()
    {
        var type = typeof(Steam2026NativeMovieHookSet);
        Equal(true, typeof(IDisposable).IsAssignableFrom(type), "native movie hook set is disposable");
        var hookFields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(field => field.FieldType.IsGenericType)
            .Where(field => field.FieldType.GetGenericTypeDefinition() == typeof(IHook<>))
            .ToArray();
        Equal(4, hookFields.Length, "native movie hook set exact hook count");
        var delegateTypes = hookFields
            .Select(field => field.FieldType.GetGenericArguments().Single())
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();
        SequenceEqual(
            new[]
            {
                typeof(NativeMoviePrepareOriginal),
                typeof(NativeMovieReleaseOriginal),
                typeof(NativeMovieStartOriginal),
                typeof(NativeMovieStopOriginal)
            }.OrderBy(type => type.Name, StringComparer.Ordinal),
            delegateTypes,
            "native movie hook set delegate cohort");
    }

    private static void OpeningVirtualIdentityMapsOnlyToTheValidatedPhysicalPath(
        Steam2026FingerprintResult supportedRuntime)
    {
        const string expectedPhysicalPath =
            @"X:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\ff7\workingdir\data\movies\opening.avi";
        var identity = new Steam2026OpeningMoviePathIdentity(expectedPhysicalPath);

        Equal(
            true,
            identity.TryMapForObserver("0://DATA/MOVIES/OPENING.AVI", out var mappedVirtual),
            "exact case-insensitive opening virtual identity maps");
        Equal(
            Path.GetFullPath(expectedPhysicalPath),
            mappedVirtual,
            "opening virtual identity maps only to validated physical path");
        Equal(
            true,
            identity.TryMapForObserver(expectedPhysicalPath, out var mappedPhysical),
            "already absolute expected physical path remains supported");
        Equal(
            Path.GetFullPath(expectedPhysicalPath),
            mappedPhysical,
            "absolute expected physical path remains exact");

        foreach (var rejected in new[]
                 {
                     "0://data/movies/ending.avi",
                     "0://data/movies/opening.avi.bak",
                     "prefix-0://data/movies/opening.avi",
                     "0://data/movies/../movies/opening.avi",
                     "0://data/./movies/opening.avi",
                     "1://data/movies/opening.avi",
                     "0://override/movies/opening.avi",
                     "0://data/movies//opening.avi",
                     "0:\\data\\movies\\opening.avi",
                     "opening.avi",
                     @"Y:\data\movies\opening.avi",
                     @"X:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\ff7\workingdir\data\movies\..\movies\opening.avi",
                     " 0://data/movies/opening.avi",
                     "0://data/movies/opening.avi "
                 })
        {
            Equal(
                false,
                identity.TryMapForObserver(rejected, out _),
                $"non-exact opening identity rejected: {rejected}");
        }

        var fixture = MovieIngressFixture.Create(supportedRuntime);
        var observer = new OpeningMovieLifecycleObserver(expectedPhysicalPath, fixture.Contract);
        var snapshots = new List<NativeMovieIngressSnapshot>();
        var state = 0;
        using var coordinator = new NativeMovieDetourIngressCoordinator(
            fixture.Contract,
            observer,
            (_, _) => 1,
            () => { },
            () =>
            {
                state = 1;
                return 1;
            },
            () => state = 0,
            () => identity.TryMapForObserver(
                "0://data/movies/opening.avi",
                out var mappedPath)
                ? mappedPath
                : null,
            () => state,
            () => new DateTime(2026, 7, 20, 22, 0, 0, DateTimeKind.Utc),
            new DelegatingNativeIngressQueue<NativeMovieIngressSnapshot>(snapshots.Add));

        Equal(1, coordinator.OnPrepare(0, 0), "virtual opening integration prepare return");
        Equal(1, coordinator.OnStart(), "virtual opening integration start return");
        Equal(
            MovieLifecycleKind.Started,
            snapshots.Last().LifecycleEvent?.Kind,
            "mapped virtual opening identity reaches observer as opening start");
    }

    private static Dictionary<NativeMovieCallbackKind, NativeMovieCallbackIdentity> GetHookIdentities(
        NativeMovieCallbackContract contract)
    {
        var identities = new Dictionary<NativeMovieCallbackKind, NativeMovieCallbackIdentity>();
        foreach (var kind in HookKinds)
        {
            Equal(true, contract.TryValidateIdentity(kind, out var identity), $"pre-hook {kind} identity");
            identities.Add(kind, identity);
        }

        return identities;
    }

    private static void WriteExactCallbackTable(FakeNativeMemoryReader memory)
    {
        foreach (var kind in HookKinds)
        {
            memory.Write(
                MovieIngressFixture.ModuleBase + GetCallbackRecordRva(kind) + 8,
                BitConverter.GetBytes(
                    MovieIngressFixture.ModuleBase + NativeMovieCallbackContract.GetRva(kind)));
        }
    }

    private static ulong GetCallbackRecordRva(NativeMovieCallbackKind kind) => kind switch
    {
        NativeMovieCallbackKind.Prepare => 0x016D37F8,
        NativeMovieCallbackKind.Release => 0x016D3818,
        NativeMovieCallbackKind.Start => 0x016D3838,
        NativeMovieCallbackKind.Stop => 0x016D3858,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static void WriteCanonicalPath(FakeNativeMemoryReader memory, string path)
    {
        var encoded = Encoding.Latin1.GetBytes(path);
        if (encoded.Length >= CanonicalPathCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(path));
        }

        var buffer = new byte[CanonicalPathCapacity];
        encoded.CopyTo(buffer, 0);
        memory.Write(MovieIngressFixture.ModuleBase + CanonicalPathRva, buffer);
    }

    private static void MapMovieObject(FakeNativeMemoryReader memory, ulong address, int state)
    {
        memory.MapRegion(
            address,
            0x1000,
            address,
            isCommitted: true,
            isExecutable: false,
            isImage: false,
            isReadable: true);
        memory.Write(address + StartedStateOffset, BitConverter.GetBytes(state));
    }

    private static bool Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static void SequenceEqual<T>(
        IEnumerable<T> expected,
        IEnumerable<T> actual,
        string label)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException($"{label}: sequence mismatch.");
        }
    }
}
