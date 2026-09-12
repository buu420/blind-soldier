using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Reflection;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;
using Ff7.Accessibility.Steam2026X64.Runtime.Input;
using Reloaded.Hooks;

/// <summary>
/// Automatic movement on the Steam 2026 host, from the commanded direction to the
/// direction the game itself is holding.
///
/// <para>What these exist to stop happening again: the x64 runtime pressed the keys the
/// guest control table names with <c>SendInput</c>, reported every transition inserted,
/// and the party did not move - because that table is the <em>output</em> of the host's own
/// <c>IDirectInputDeviceA::GetDeviceState</c> shim, which synthesizes a legacy DIK array
/// from its own logical actions rather than watching those keys. The presses did reach
/// something, though: numpad 2 is NVDA's read-current-character command, and the player
/// heard "blank" over and over.</para>
///
/// <para>So the assertions here are about the game's state and the game's direction, never
/// about an emission having been accepted.</para>
/// </summary>
internal static class Steam2026NativeDirectionalInputTests
{
    private const uint ControlTable = 0x009A85E8;
    private const int ControlTableLength = 3 * 0x64;
    private const uint Keyboard = Steam2026NativeDirectInputKeyboardHook.ExpectedGuestDestination;
    private const int StateLength = Steam2026NativeDirectInputKeyboardHook.KeyboardStateLength;
    private const nint Device = 0x4321;

    // The live bank-0 tokens of the running game, captured from its own memory: the
    // numeric keypad, which is also why SendInput was talking to the screen reader.
    private const byte TokenUp = 0x48;
    private const byte TokenRight = 0x4D;
    private const byte TokenDown = 0x50;
    private const byte TokenLeft = 0x4B;

    private static readonly DateTime Start = new(2026, 9, 11, 16, 0, 0, DateTimeKind.Utc);

    public static void Run()
    {
        TheRegistrationRecordIsWhatAuthorizesTheHook();
        ACommandedDirectionBecomesTheDirectionTheGameHolds();
        ADiagonalIsBothCardinalsAndSurvivesEveryPoll();
        ADirectionChangeReplacesTheOldOne();
        LettingGoNeedsNoWriteAndLeavesNothingBehind();
        ARemappedControlTableIsFollowedRatherThanAssumed();
        WhatThePlayerIsHoldingIsNeverTakenAway();
        NothingIsWrittenOnTheCallsThatAreNotOurs();
        AStaleOrUnfocusedDirectionIsNotDelivered();
        PressesNeedTheOverlayAndReleasesNeverFail();
        TheRealGuestWritePathLandsInTheGamesOwnPage();
        TheProductionDetourInstallsOverARealNativeFunction();
        AutowalkUsesTheNativeSink();
    }

    /// <summary>
    /// The hook is allowed nowhere except the function the host's own named registration
    /// table still points at, under the name it is registered with, with the argument
    /// count this detour's ABI declares, and with the entry bytes that were decompiled.
    /// The live prefix is the only usable signature: this image's code is encrypted on
    /// disk.
    /// </summary>
    private static void TheRegistrationRecordIsWhatAuthorizesTheHook()
    {
        var memory = CreateRegisteredImage();
        Equal(
            true,
            Steam2026NativeDirectInputKeyboardHook.TryValidateRegistration(
                ImageBase, ImageSize, memory, out var hostAddress, out var diagnostic),
            $"the evidenced registration validates ({diagnostic})");
        Equal(
            ImageBase + Steam2026NativeDirectInputKeyboardHook.GetDeviceStateRva,
            hostAddress,
            "the validated address is the registered one");

        foreach (var (name, corrupt) in new (string, Action<FakeNativeMemoryReader>)[]
                 {
                     ("a record pointing somewhere else", m => m.Write(
                         ImageBase + Steam2026NativeDirectInputKeyboardHook.RegistrationRecordRva
                             + sizeof(ulong),
                         BitConverter.GetBytes(ImageBase + 0x00001000))),
                     ("a record registered under another name", m => m.Write(
                         ImageBase + Steam2026NativeDirectInputKeyboardHook.RegistrationRecordRva,
                         BitConverter.GetBytes(ImageBase + 0x00002000))),
                     ("the one-argument shape Acquire uses", m => m.Write(
                         ImageBase + Steam2026NativeDirectInputKeyboardHook.RegistrationRecordRva
                             + (2 * sizeof(ulong)),
                         BitConverter.GetBytes(0x0000000100000001UL))),
                     ("a name that does not read back", m => m.Write(
                         ImageBase + Steam2026NativeDirectInputKeyboardHook.NameRva,
                         "IDirectInputDeviceA::GetDeviceData\0"u8.ToArray())),
                     ("a different live prefix", m => m.Write(
                         ImageBase + Steam2026NativeDirectInputKeyboardHook.GetDeviceStateRva,
                         [0xE9, 0x00, 0x00, 0x00, 0x00])),
                     ("code that is not executable", m =>
                     {
                         m.ClearRegions();
                         m.MapRegion(
                             ImageBase,
                             ImageSize,
                             ImageBase,
                             isCommitted: true,
                             isExecutable: false);
                     }),
                 })
        {
            var tampered = CreateRegisteredImage();
            corrupt(tampered);
            Equal(
                false,
                Steam2026NativeDirectInputKeyboardHook.TryValidateRegistration(
                    ImageBase, ImageSize, tampered, out _, out _),
                $"{name} is refused");
        }
    }

    private static void ACommandedDirectionBecomesTheDirectionTheGameHolds()
    {
        var host = new FakeHost();
        host.Command(FieldNavigationInput.Down);

        Equal(0, host.Poll(), "the host's own call still returns its own result");
        Equal((byte)0x80, host.State(TokenDown), "the game's keyboard state holds the Down key");
        Equal(
            FieldNavigationInput.Down,
            host.ObservedDirection(),
            "and the game's logical direction is Down");
        Equal(1, host.Overlays, "one byte was written for one direction");
    }

    private static void ADiagonalIsBothCardinalsAndSurvivesEveryPoll()
    {
        // Up and Left are 0x48 and 0x4B - the same four-byte word - so this also covers the
        // second of the two writes not undoing the first.
        foreach (var diagonal in new[] { FieldNavigationInput.UpLeft, FieldNavigationInput.DownRight })
        {
            AssertDiagonalHolds(diagonal);
        }
    }

    private static void AssertDiagonalHolds(FieldNavigationInput diagonal)
    {
        var host = new FakeHost();
        host.Command(diagonal);

        // Two seconds of polls with nothing else happening. The shim rebuilds the buffer
        // every time, so a direction that is only marked once would last a single frame.
        for (var frame = 0; frame < 120; frame++)
        {
            host.Advance(TimeSpan.FromMilliseconds(16));
            host.Renew();
            _ = host.Poll();
            Equal(
                diagonal,
                host.ObservedDirection(),
                $"{diagonal} is still held on frame {frame}");
        }

        var down = diagonal == FieldNavigationInput.DownRight;
        Equal((byte)(down ? 0x80 : 0x00), host.State(TokenDown), "Down");
        Equal((byte)(down ? 0x80 : 0x00), host.State(TokenRight), "and Right");
        Equal((byte)(down ? 0x00 : 0x80), host.State(TokenUp), "or Up");
        Equal((byte)(down ? 0x00 : 0x80), host.State(TokenLeft), "and Left");
    }

    private static void ADirectionChangeReplacesTheOldOne()
    {
        var host = new FakeHost();
        host.Command(FieldNavigationInput.Down);
        _ = host.Poll();
        host.Command(FieldNavigationInput.Left);
        _ = host.Poll();

        Equal((byte)0x00, host.State(TokenDown), "the old direction is gone");
        Equal((byte)0x80, host.State(TokenLeft), "and the new one is held");
        Equal(FieldNavigationInput.Left, host.ObservedDirection(), "the game turns left");
    }

    /// <summary>
    /// Releasing is the absence of a write. The shim clears and rebuilds all 256 bytes on
    /// its next poll, so nothing has to be undone - and nothing may be, because the bytes
    /// it has just written are the player's.
    /// </summary>
    private static void LettingGoNeedsNoWriteAndLeavesNothingBehind()
    {
        var host = new FakeHost();
        host.Command(FieldNavigationInput.Down);
        _ = host.Poll();
        var written = host.Overlays;

        host.Command(FieldNavigationInput.None);
        _ = host.Poll();
        Equal((byte)0x00, host.State(TokenDown), "the key is not held any more");
        Equal(
            FieldNavigationInput.None,
            host.ObservedDirection(),
            "and the game is holding no direction");
        Equal(written, host.Overlays, "letting go wrote nothing at all");
    }

    /// <summary>
    /// The bytes come from the live control table, not from a constant. A player who has
    /// remapped the legacy keys moves on the keys they chose - here an extended token,
    /// which is the case the resolver splits into a scan code and a flag and this sink has
    /// to put back together.
    /// </summary>
    private static void ARemappedControlTableIsFollowedRatherThanAssumed()
    {
        const byte remappedDown = 0xD0;
        var host = new FakeHost(downToken: remappedDown);
        host.Command(FieldNavigationInput.Down);
        _ = host.Poll();

        Equal((byte)0x80, host.State(remappedDown), "the remapped Down key is the one held");
        Equal((byte)0x00, host.State(TokenDown), "and the stock one is untouched");
        Equal(
            FieldNavigationInput.Down,
            host.ObservedDirection(),
            "the game reads its own mapping and moves down");
    }

    private static void WhatThePlayerIsHoldingIsNeverTakenAway()
    {
        // 0x51 shares the four-byte word the Down token lives in, which is the byte only a
        // read-modify-write keeps: an overlay that simply stored the word would erase it.
        const byte sameWordAsDown = 0x51;
        var host = new FakeHost();
        host.HostHeldTokens = [TokenLeft, sameWordAsDown];
        host.Command(FieldNavigationInput.Down);
        _ = host.Poll();

        Equal((byte)0x80, host.State(TokenLeft), "the player's own key survives the overlay");
        Equal(
            (byte)0x80,
            host.State(sameWordAsDown),
            "including one in the same four-byte word as the direction");
        Equal((byte)0x80, host.State(TokenDown), "beside the automatic one");

        // And a direction the host already holds costs no write at all.
        var both = new FakeHost();
        both.HostHeldTokens = [TokenDown];
        both.Command(FieldNavigationInput.Down);
        _ = both.Poll();
        Equal(0, both.Overlays, "a direction the host already holds is left alone");
        Equal((byte)0x80, both.State(TokenDown), "and is still held");
    }

    private static void NothingIsWrittenOnTheCallsThatAreNotOurs()
    {
        var mouse = new FakeHost();
        mouse.Command(FieldNavigationInput.Down);
        _ = mouse.PollRaw(0x10, Keyboard);
        Equal(0, mouse.Overlays, "a 16-byte device state is not a keyboard");

        var failed = new FakeHost { OriginalResult = unchecked((int)0x8007001E) };
        failed.Command(FieldNavigationInput.Down);
        Equal(
            unchecked((int)0x8007001E),
            failed.PollRaw(StateLength, Keyboard),
            "a failed call keeps its own HRESULT");
        Equal(0, failed.Overlays, "and is not written into");

        var elsewhere = new FakeHost();
        elsewhere.Command(FieldNavigationInput.Down);
        _ = elsewhere.PollRaw(StateLength, Keyboard + 0x1000);
        Equal(0, elsewhere.Overlays, "another destination is not the buffer we know");
        Equal(1, (int)elsewhere.Hook.RefusedDestinations, "and it is counted, not ignored");
    }

    private static void AStaleOrUnfocusedDirectionIsNotDelivered()
    {
        var stale = new FakeHost();
        stale.Command(FieldNavigationInput.Down);
        stale.Advance(Steam2026NativeDirectionalInputSink.Freshness + TimeSpan.FromMilliseconds(1));
        _ = stale.Poll();
        Equal(0, stale.Overlays, "a direction nobody renews is not delivered");

        // Renewing it again brings it straight back: the command was never taken off the
        // shared controller behind its back, which would have left it owning a key it
        // could not re-press.
        stale.Renew();
        _ = stale.Poll();
        Equal((byte)0x80, stale.State(TokenDown), "and a renewed one is delivered again");

        var background = new FakeHost();
        background.Command(FieldNavigationInput.Down);
        background.IsForeground = false;
        _ = background.Poll();
        Equal(0, background.Overlays, "nothing is delivered while the game is not in front");
    }

    private static void PressesNeedTheOverlayAndReleasesNeverFail()
    {
        var detached = new Steam2026NativeDirectionalInputSink(now: () => Start);
        var press = detached.Send([new HighwayKeyboardTransition(TokenDown, IsKeyDown: true)]);
        Equal(0, press.InsertedCount, "a press with no overlay installed is refused");
        Equal(
            true,
            detached.Diagnostic.Contains("overlay is not installed", StringComparison.Ordinal),
            $"and says why ({detached.Diagnostic})");

        var release = detached.Send([new HighwayKeyboardTransition(TokenDown, IsKeyDown: false)]);
        Equal(
            1,
            release.InsertedCount,
            "a release is always accepted, or the shared controller would own a key forever");
    }

    /// <summary>
    /// The same overlay over the production translated address space and its guarded
    /// write, so the byte is proved to land in the page the game's own mapper resolves
    /// rather than in a dictionary that agrees with itself.
    /// </summary>
    private static void TheRealGuestWritePathLandsInTheGamesOwnPage()
    {
        var fixture = FieldObservationFixture.CreatePopulated();
        var native = new WritableFakeNativeMemory(fixture.Native);
        var addressSpace = new TranslatedX86AddressSpace(
            FieldObservationFixture.ModuleBase, native, native);
        Equal(
            true,
            addressSpace.HasExpectedResolverSignature(),
            "the fixture carries the real resolver signature");

        var sink = new Steam2026NativeDirectionalInputSink(now: () => Start);
        using var hook = Steam2026NativeDirectInputKeyboardHook.CreateForOverlayTest(
            sink,
            addressSpace,
            addressSpace,
            (_, byteCount, destination) =>
            {
                // The shim's own behaviour: clear all 256 bytes, then mark what it has.
                for (var index = 0; index < byteCount; index++)
                {
                    fixture.Write(destination + (uint)index, [0]);
                }

                return 0;
            },
            () => Start);

        Equal(
            1,
            sink.Send([new HighwayKeyboardTransition(TokenDown, IsKeyDown: true)]).InsertedCount,
            "the overlay accepts the press");
        Equal(0, hook.InvokeForTest(Device, StateLength, Keyboard), "the call succeeds");
        Equal(1, (int)hook.AppliedOverlays, "one guarded guest write");

        Span<byte> state = stackalloc byte[StateLength];
        Equal(true, addressSpace.TryRead(Keyboard, state), "the guest buffer reads back");
        Equal((byte)0x80, state[TokenDown], "and the game's own page holds the Down key");
    }

    /// <summary>
    /// Autowalk must use native delivery even when no sink is passed to the field
    /// coordinator. An unattached native sink refuses movement instead of sending
    /// Windows key events to the screen reader.
    /// </summary>
    private static void AutowalkUsesTheNativeSink()
    {
        var sink = new Steam2026NativeDirectionalInputSink();
        Equal(
            true,
            ReferenceEquals(
                sink,
                ResolveSink(NavigationAutoWalkController.CreateCurrentProcess(
                    new OverlayGuestMemory(), sink))),
            "the shared auto-walk factory delivers through the sink it is given");

        var fixture = FieldObservationFixture.CreatePopulated();
        fixture.Write(
            (uint)FieldNavigationInputReader.AddressCurrentKeyInput,
            BitConverter.GetBytes(0u));
        fixture.Write(
            (uint)FieldNavigationObjectReader.AddressFieldEventDataPtr,
            BitConverter.GetBytes(0u));
        const uint processId = 42;
        var foregroundInput = new Steam2026ForegroundInputAdapter(
            () => (nint)1, _ => processId, _ => (short)0, processId);
        using var coordinator = new Steam2026FieldNavigationCoordinator(
            new AccessibilityConfig
            {
                EnableFieldNavigationAssistant = true,
                EnableFieldExitProximityCues = false,
                EnableFieldLadderProximityCues = false,
            },
            fixture.Direct,
            foregroundInput,
            new Steam2026FieldObjectObservationReader(
                fixture.Direct, _ => null, _ => null, Array.Empty<FieldNavigationObjectDefinition>()),
            Path.GetTempPath(),
            AppContext.BaseDirectory,
            (_, _) => { },
            _ => { });

        var coordinatorSink = Field<Steam2026NativeDirectionalInputSink>(
            coordinator, "directionalInput");
        Equal(
            true,
            ReferenceEquals(
                coordinatorSink,
                ResolveSink(Field<NavigationAutoWalkController>(coordinator, "autoWalk"))),
            "the field runtime's own auto walk delivers through the native overlay");
    }


    /// <summary>
    /// The production detour over a real native function, installed by the real hooking
    /// backend and called through a function pointer - not a delegate standing in for one.
    /// The stub counts its own invocations, so "the original ran exactly once" is observed
    /// rather than arranged, and the overlay has to reach the guest page afterwards.
    ///
    /// <para>A bare test host cannot initialise Reloaded's backend - the NuGet graph
    /// resolves a Reloaded.Memory that dropped the type Reloaded.Assembler was built
    /// against - so that case is reported loudly instead of passing quietly. The isolated
    /// host runner substitutes the loader's own copy and does exercise it.</para>
    /// </summary>
    private static void TheProductionDetourInstallsOverARealNativeFunction()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var page = NativeStub.Allocate();
        if (page == 0)
        {
            Console.WriteLine("Native keyboard overlay: skipped, no executable page.");
            return;
        }

        var counter = page + 0x80;
        NativeStub.WriteCountingStub(page, counter);

        var fixture = FieldObservationFixture.CreatePopulated();
        var native = new WritableFakeNativeMemory(fixture.Native);
        var addressSpace = new TranslatedX86AddressSpace(
            FieldObservationFixture.ModuleBase, native, native);
        for (var index = 0; index < StateLength; index++)
        {
            fixture.Write(Keyboard + (uint)index, [0]);
        }

        // What the host left in the buffer: the player holding Left.
        fixture.Write(Keyboard + TokenLeft, [0x80]);

        var sink = new Steam2026NativeDirectionalInputSink(now: () => Start);
        Steam2026NativeDirectInputKeyboardHook installed;
        try
        {
            installed = Steam2026NativeDirectInputKeyboardHook.CreateOverValidatedAddress(
                sink,
                addressSpace,
                ReloadedHooks.Instance,
                (ulong)page,
                () => Start,
                Console.WriteLine);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Native keyboard overlay: INSTALL NOT EXERCISED in this host - {ex.Message}");
            return;
        }

        using (installed)
        {
            Equal(
                1,
                sink.Send([new HighwayKeyboardTransition(TokenDown, IsKeyDown: true)]).InsertedCount,
                "the overlay is installed, so the press is accepted");
            var call = Marshal.GetDelegateForFunctionPointer
                <Steam2026NativeDirectInputKeyboardHook.GetDeviceStateDelegate>(page);
            Equal(0, call(Device, StateLength, Keyboard), "the detour returns the original HRESULT");
            Equal(1L, NativeStub.ReadCounter(counter), "the original ran exactly once");

            Span<byte> state = stackalloc byte[StateLength];
            Equal(true, addressSpace.TryRead(Keyboard, state), "the guest buffer reads back");
            Equal((byte)0x80, state[TokenDown], "the real detour marked the commanded direction");
            Equal((byte)0x80, state[TokenLeft], "and left the player's own key alone");
            Equal(1, (int)installed.AppliedOverlays, "one overlay, through the real backend");
        }
    }

    private static class NativeStub
    {
        private const uint Commit = 0x1000;
        private const uint Reserve = 0x2000;
        private const uint ExecuteReadWrite = 0x40;

        internal static nint Allocate() =>
            VirtualAlloc(0, 0x1000, Commit | Reserve, ExecuteReadWrite);

        /// <summary>
        /// <c>mov rax, counter; inc qword [rax]; xor eax, eax; ret</c> - a real native
        /// function with this ABI's return, and position independent so the backend may
        /// relocate its prologue into a trampoline.
        /// </summary>
        internal static void WriteCountingStub(nint page, nint counter)
        {
            var code = new List<byte> { 0x48, 0xB8 };
            code.AddRange(BitConverter.GetBytes((long)counter));
            code.AddRange([0x48, 0xFF, 0x00, 0x33, 0xC0, 0xC3]);
            Marshal.Copy(code.ToArray(), 0, page, code.Count);
            Marshal.WriteInt64(counter, 0);
        }

        internal static long ReadCounter(nint counter) => Marshal.ReadInt64(counter);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern nint VirtualAlloc(nint address, nuint size, uint type, uint protect);
    }
    private const ulong ImageBase = 0x0000000140000000;
    private const ulong ImageSize = 0x02100000;

    private static FakeNativeMemoryReader CreateRegisteredImage()
    {
        var memory = new FakeNativeMemoryReader();
        memory.MapRegion(ImageBase, ImageSize, ImageBase, isCommitted: true, isExecutable: true);
        memory.Write(
            ImageBase + Steam2026NativeDirectInputKeyboardHook.RegistrationRecordRva,
            BitConverter.GetBytes(ImageBase + Steam2026NativeDirectInputKeyboardHook.NameRva));
        memory.Write(
            ImageBase + Steam2026NativeDirectInputKeyboardHook.RegistrationRecordRva
                + sizeof(ulong),
            BitConverter.GetBytes(
                ImageBase + Steam2026NativeDirectInputKeyboardHook.GetDeviceStateRva));
        memory.Write(
            ImageBase + Steam2026NativeDirectInputKeyboardHook.RegistrationRecordRva
                + (2 * sizeof(ulong)),
            BitConverter.GetBytes(Steam2026NativeDirectInputKeyboardHook.ExpectedArgumentWord));
        memory.Write(
            ImageBase + Steam2026NativeDirectInputKeyboardHook.NameRva,
            "IDirectInputDeviceA::GetDeviceState\0"u8.ToArray());
        memory.Write(
            ImageBase + Steam2026NativeDirectInputKeyboardHook.GetDeviceStateRva,
            Convert.FromHexString(Steam2026NativeDirectInputKeyboardHook.ExpectedPrefixHex));
        return memory;
    }

    /// <summary>
    /// The host's side of one automatic direction: a guest memory with the control table
    /// the resolver reads, the real shared auto-walk controller commanding directions
    /// through the production sink, and a stand-in for the native shim that clears and
    /// rebuilds the 256-byte state on every poll exactly as the decompiled one does.
    /// </summary>
    private sealed class FakeHost
    {
        private readonly OverlayGuestMemory guest = new();
        private readonly Steam2026NativeDirectionalInputSink sink;
        private readonly NavigationAutoWalkController autoWalk;
        private readonly FieldNavigationInputReader consumer;
        private readonly byte up;
        private readonly byte right;
        private readonly byte down;
        private readonly byte left;

        public FakeHost(
            byte upToken = TokenUp,
            byte rightToken = TokenRight,
            byte downToken = TokenDown,
            byte leftToken = TokenLeft)
        {
            up = upToken;
            right = rightToken;
            down = downToken;
            left = leftToken;
            // The resolver reads all three banks in one go, so the whole table has to be
            // mapped - a partial read is a refusal, not a zero.
            guest.Write(ControlTable, new byte[ControlTableLength]);
            WriteToken(12, up);
            WriteToken(13, right);
            WriteToken(14, down);
            WriteToken(15, left);
            guest.Write(Keyboard, new byte[StateLength]);
            guest.Write(
                (uint)FieldNavigationInputReader.AddressCurrentKeyInput,
                BitConverter.GetBytes(0u));

            sink = new Steam2026NativeDirectionalInputSink(() => IsForeground, () => Now);
            Hook = Steam2026NativeDirectInputKeyboardHook.CreateForOverlayTest(
                sink, guest, guest, Original, () => Now);
            autoWalk = NavigationAutoWalkController.CreateCurrentProcess(guest, sink);
            _ = autoWalk.TryStart(NavigationAutoWalkDomain.Field, routeActive: true);
            consumer = new FieldNavigationInputReader(
                address => guest.ReadUInt32((uint)address));
        }

        public Steam2026NativeDirectInputKeyboardHook Hook { get; }

        public DateTime Now { get; private set; } = Start;

        public bool IsForeground { get; set; } = true;

        public int OriginalResult { get; set; }

        /// <summary>Keys the host itself reports held - the player's own hands.</summary>
        public byte[] HostHeldTokens { get; set; } = [];

        public int Overlays => (int)Hook.AppliedOverlays;

        public void Advance(TimeSpan span) => Now += span;

        /// <summary>What the route asks for this frame, through the real controller.</summary>
        public void Command(FieldNavigationInput direction)
        {
            var result = autoWalk.Drive(
                direction,
                canMove: direction != FieldNavigationInput.None,
                routeActive: true);
            Equal(
                true,
                result.Success,
                $"the shared controller accepted {direction} ({result.Diagnostic})");
            Renew();
        }

        public void Renew() => sink.Renew(Now);

        public int Poll() => PollRaw(StateLength, Keyboard);

        public int PollRaw(int byteCount, uint destination) =>
            Hook.InvokeForTest(Device, byteCount, destination);

        public byte State(byte token) =>
            guest.ReadByte(Keyboard + token);

        /// <summary>
        /// The direction the game ends up holding. This is <c>FUN_0041A21E</c>'s contract -
        /// each control bank slot's token is looked up in the DIK array and its logical bit
        /// set - decoded by the production reader the rest of the mod observes.
        /// </summary>
        public FieldNavigationInput ObservedDirection()
        {
            var mask = 0u;
            mask |= Bit(up, FieldNavigationInputReader.UpMask);
            mask |= Bit(right, FieldNavigationInputReader.RightMask);
            mask |= Bit(down, FieldNavigationInputReader.DownMask);
            mask |= Bit(left, FieldNavigationInputReader.LeftMask);
            guest.Write(
                (uint)FieldNavigationInputReader.AddressCurrentKeyInput,
                BitConverter.GetBytes(mask));
            return consumer.Read().Direction;

            uint Bit(byte token, uint bit) => (State(token) & 0x80) != 0 ? bit : 0u;
        }

        private int Original(nint device, int byteCount, uint destination)
        {
            if (OriginalResult != 0)
            {
                return OriginalResult;
            }

            if (byteCount == StateLength && destination == Keyboard)
            {
                guest.Write(destination, new byte[StateLength]);
                foreach (var token in HostHeldTokens)
                {
                    guest.Write(destination + token, [0x80]);
                }
            }

            return 0;
        }

        private void WriteToken(int slot, byte token) =>
            guest.Write(
                ControlTable + ((uint)slot * sizeof(uint)),
                BitConverter.GetBytes((uint)token));
    }

    /// <summary>Guest memory that can be written, which the read-only doubles cannot.</summary>
    private sealed class OverlayGuestMemory : ILegacyAddressSpace, ILegacyMemoryWriter
    {
        private readonly Dictionary<uint, byte> bytes = [];

        public void Write(uint address, IReadOnlyList<byte> values)
        {
            for (var index = 0; index < values.Count; index++)
            {
                bytes[checked(address + (uint)index)] = values[index];
            }
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            if (virtualAddress == 0)
            {
                destination.Clear();
                return false;
            }

            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(virtualAddress + (uint)index, out destination[index]))
                {
                    destination.Clear();
                    return false;
                }
            }

            return true;
        }

        public bool TryWriteInt32(uint virtualAddress, int value)
        {
            if (virtualAddress == 0 || (virtualAddress & 3) != 0)
            {
                return false;
            }

            Write(virtualAddress, BitConverter.GetBytes(value));
            return true;
        }

        public byte ReadByte(uint virtualAddress)
        {
            Span<byte> buffer = stackalloc byte[1];
            return TryRead(virtualAddress, buffer) ? buffer[0] : (byte)0;
        }

        public uint ReadUInt32(uint virtualAddress)
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            return TryRead(virtualAddress, buffer)
                ? BinaryPrimitives.ReadUInt32LittleEndian(buffer)
                : 0u;
        }
    }

    private sealed class WritableFakeNativeMemory(FakeNativeMemoryReader inner)
        : INativeMemoryReader, INativeMemoryWriter
    {
        public bool TryReadUInt64(ulong address, out ulong value) =>
            inner.TryReadUInt64(address, out value);

        public bool TryRead(ulong address, Span<byte> destination) =>
            inner.TryRead(address, destination);

        public bool TryQueryRegion(ulong address, out NativeMemoryRegion region) =>
            inner.TryQueryRegion(address, out region);

        public bool TryExchangeInt32(ulong hostAddress, int value)
        {
            inner.Write(hostAddress, BitConverter.GetBytes(value));
            return true;
        }
    }

    private static IHighwayKeyboardInputSink? ResolveSink(NavigationAutoWalkController controller) =>
        SinkOf(Field<object>(controller, "directionalInput"));

    private static IHighwayKeyboardInputSink? SinkOf(object steering) =>
        Field<IHighwayKeyboardInputSink>(steering, "sink");

    private static T Field<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(
            name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"Native directional input: {instance.GetType().Name} has no {name} field.");
        return (T)field.GetValue(instance)!;
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Native directional input: {message}. Expected {expected}, actual {actual}.");
        }
    }
}
