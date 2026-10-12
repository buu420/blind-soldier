using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

internal static class LegacyNativeMoviePlaybackReaderTests
{
    internal static void Run()
    {
        ReadsTheNativeEndingRequestAndDecoderClock();
        RejectsUnownedPreparedCompletedAndCreditsWithoutCallingTheGetter();
        RejectsTornNativeStateAndWrongCanonicalPath();
        RejectsAnUnverifiedOrChangedCallableBeforeInvocation();
        KeepsGenerationAcrossUnreadableFramesAndAdvancesOnARealRestart();
        UsesTheExactFfnxDecoderFrameRate();
        RejectsPreCallTearsAndUnavailableStockSeeking();
        RejectsUnreviewedFingerprintsAndCallableTears();
        ValidatesCachedPlaybackWithoutCallingTheNativeGetter();
    }

    private static void ReadsTheNativeEndingRequestAndDecoderClock()
    {
        var memory = new MovieMemory();
        var calls = 0;
        var reader = Reader(memory, () => { calls++; return 150; });
        True(reader.TryRead(out var sample), reader.LastDiagnostic);
        Equal("ending2.avi", sample.FilmFileName, "native module-25 request identity");
        Equal(10d, sample.PositionSeconds, "stock IMediaSeeking frame clock");
        Equal(1, calls, "only checked playing state invokes getter");
        True(reader.TryResolveObservationTarget(out var target, out _), "stock update target identity");
        Equal((long)LegacyNativeMoviePlaybackReader.UpdateSampleAddress, target, "stock game-thread update target");
        memory.Ending3();
        True(reader.TryRead(out sample), reader.LastDiagnostic);
        Equal("ending3.avi", sample.FilmFileName, "exact canonical ending3 path identity");
        Equal(28, sample.Module, "endgame credits module differs from menu credits");
    }

    private static void RejectsUnownedPreparedCompletedAndCreditsWithoutCallingTheGetter()
    {
        foreach (var state in new[] { "field", "prepared", "completed", "credits", "menucredits" })
        {
            var memory = new MovieMemory();
            if (state == "field") memory.Word(LegacyNativeMoviePlaybackReader.ModuleAddress, 1);
            if (state == "prepared") memory.Int(LegacyNativeMoviePlaybackReader.StartedAddress, 0);
            if (state == "completed") memory.Int(LegacyNativeMoviePlaybackReader.CompletedAddress, 1);
            if (state == "credits") { memory.Ending3(); memory.Int(LegacyNativeMoviePlaybackReader.CreditsStageAddress, 1); }
            if (state == "menucredits") memory.Word(LegacyNativeMoviePlaybackReader.ModuleAddress, 27);
            var calls = 0;
            var reader = Reader(memory, () => { calls++; return 150; });
            Equal(false, reader.TryRead(out _), $"{state} is not an active ending film");
            Equal(0, calls, $"{state} must not invoke native getter");
        }
    }

    private static void RejectsTornNativeStateAndWrongCanonicalPath()
    {
        foreach (var address in new[] { LegacyNativeMoviePlaybackReader.ModuleAddress,
            LegacyNativeMoviePlaybackReader.StartedAddress, LegacyNativeMoviePlaybackReader.CreditsPathAddress })
        {
            var memory = new MovieMemory(); memory.Ending3();
            var calls = 0;
            var reader = Reader(memory, () => { calls++; memory.Bytes[address] ^= 1; return 150; });
            Equal(false, reader.TryRead(out _), "state changed across the native frame read");
            Equal(1, calls, "post-call tear rejects sample after the single checked getter read");
        }
        var wrong = new MovieMemory(); wrong.Ending3();
        wrong.Text(LegacyNativeMoviePlaybackReader.CreditsPathAddress, @"C:\arbitrary\ending3.avi");
        var wrongCalls = 0;
        Equal(false, Reader(wrong, () => { wrongCalls++; return 1; }).TryRead(out _), "basename alone never proves film identity");
        Equal(0, wrongCalls, "wrong native requested path fails before callable invocation");
    }

    private static void RejectsAnUnverifiedOrChangedCallableBeforeInvocation()
    {
        var memory = new MovieMemory();
        var calls = 0;
        var reader = Reader(memory, () => { calls++; return 1; });
        memory.Bytes[LegacyNativeMoviePlaybackReader.GetterAddress] = 0xCC;
        Equal(false, reader.TryRead(out _), "changed getter signature is rejected");
        Equal(0, calls, "changed getter is never invoked");
        memory = new MovieMemory();
        reader = Reader(memory, () => { calls++; return 1; }, executable: false);
        Equal(false, reader.TryRead(out _), "nonexecutable getter memory is rejected");
        Equal(0, calls, "uncommitted/nonexecutable memory is never called");
    }

    private static void KeepsGenerationAcrossUnreadableFramesAndAdvancesOnARealRestart()
    {
        var memory = new MovieMemory(); uint frame = 100;
        var reader = Reader(memory, () => frame);
        True(reader.TryRead(out var first), reader.LastDiagnostic);
        memory.Available = false; Equal(false, reader.TryRead(out _), "unreadable sample");
        memory.Available = true; frame = 110;
        True(reader.TryRead(out var resumed), reader.LastDiagnostic);
        Equal(first.Generation, resumed.Generation, "failed read is not evidence of a new playback");
        frame = 0; True(reader.TryRead(out var restarted), reader.LastDiagnostic);
        Equal(first.Generation + 1, restarted.Generation, "native frame rewind creates new playback generation");
    }

    private static void UsesTheExactFfnxDecoderFrameRate()
    {
        var memory = new MovieMemory(); memory.Ffnx();
        var reader = Reader(memory, () => 300, ffnx: true);
        True(reader.TryRead(out var sample), reader.LastDiagnostic);
        Equal(30d, sample.FramesPerSecond, "replacement decoder fps is copied rather than guessed as stock15");
        Equal(10d, sample.PositionSeconds, "raw FFNx frame counter follows actual decoder fps");
        True(reader.TryResolveObservationTarget(out var target, out _), "exact current FFNx update callable");
        Equal((long)(MovieMemory.FfnxBase + LegacyNativeMoviePlaybackReader.FfnxUpdateSampleRva), target,
            "existing FFNx update replacement retains its verified observation seam");
        memory.Double(MovieMemory.FfnxBase + LegacyNativeMoviePlaybackReader.FfnxFpsRva, double.NaN);
        Equal(false, reader.TryRead(out _), "invalid decoder fps cannot fabricate playback time");
    }

    private static void RejectsPreCallTearsAndUnavailableStockSeeking()
    {
        var torn = new MovieMemory();
        var moduleReads = 0;
        torn.OnRead = address =>
        {
            if (address == LegacyNativeMoviePlaybackReader.ModuleAddress && ++moduleReads == 2)
                torn.Int(LegacyNativeMoviePlaybackReader.StartedAddress, 0);
        };
        var calls = 0;
        Equal(false, Reader(torn, () => { calls++; return 1; }).TryRead(out _), "pre-call native flags tear is refused");
        Equal(0, calls, "pre-call native tear never enters getter");
        foreach (var address in new[] { LegacyNativeMoviePlaybackReader.StockSampleAddress,
            LegacyNativeMoviePlaybackReader.StockSeekingAddress, 0x40000u, 0x50030u })
        {
            var memory = new MovieMemory(); memory.Int(address, 0);
            Equal(false, Reader(memory, () => { calls++; return 1; }).TryRead(out _), "unavailable stock COM prerequisite is refused");
            Equal(0, calls, "missing stock COM prerequisite never enters native getter");
        }
    }

    private static void RejectsUnreviewedFingerprintsAndCallableTears()
    {
        var memory = new MovieMemory(); var calls = 0;
        foreach (var identity in new[] {
            new LegacyNativeMoviePlaybackIdentity("UNKNOWN", 0, string.Empty),
            new LegacyNativeMoviePlaybackIdentity(LegacyNativeMoviePlaybackReader.StockSha256, MovieMemory.FfnxBase, "UNKNOWN") })
        {
            var refused = false;
            try { _ = new LegacyNativeMoviePlaybackReader(memory, () => { calls++; return 0; }, identity, (_, _) => true); }
            catch (ArgumentException) { refused = true; }
            True(refused, "unreviewed executable/FFNx fingerprint must not enable the reader");
            Equal(0, calls, "identity validation never invokes getter");
        }
        var codeReads = 0;
        memory.OnRead = address =>
        {
            if (address == LegacyNativeMoviePlaybackReader.GetterAddress && ++codeReads == 2)
                memory.Bytes[address] = 0xCC;
        };
        Equal(false, Reader(memory, () => { calls++; return 0; }).TryRead(out _), "callable torn between pre-call validations is refused");
        Equal(0, calls, "changed callable is refused before invoking injected native delegate");
    }

    private static void ValidatesCachedPlaybackWithoutCallingTheNativeGetter()
    {
        var memory = new MovieMemory(); var calls = 0;
        var reader = Reader(memory, () => { calls++; return 150; });
        True(reader.TryRead(out var cached), reader.LastDiagnostic);
        True(reader.TryIsCurrentPlayback(cached, out var playing), "cached validity is readable");
        Equal(true, playing, "a paused/stationary decoder keeps its checked cached frame");
        Equal(1, calls, "worker validation never calls getter a second time");
        memory.Int(LegacyNativeMoviePlaybackReader.CompletedAddress, 1);
        True(reader.TryIsCurrentPlayback(cached, out playing), "completed playback is coherently observed");
        Equal(false, playing, "completion stops cached movie without waiting for another update callback");
        Equal(1, calls, "completion validity check never enters native getter");
        memory.Available = false;
        Equal(false, reader.TryIsCurrentPlayback(cached, out _), "unreadable worker validity does not retain stale playback");
        Equal(1, calls, "unreadable worker validation never invokes getter");
    }

    private static LegacyNativeMoviePlaybackReader Reader(MovieMemory memory, Func<uint> getter,
        bool executable = true, bool ffnx = false) => new(memory, getter,
            new LegacyNativeMoviePlaybackIdentity(LegacyNativeMoviePlaybackReader.StockSha256,
                ffnx ? MovieMemory.FfnxBase : 0,
                ffnx ? LegacyNativeMoviePlaybackReader.FfnxSha256 : string.Empty), (_, _) => executable);

    private sealed class MovieMemory : ILegacyAddressSpace
    {
        internal const uint FfnxBase = 0x10000000;
        internal readonly Dictionary<uint, byte> Bytes = new();
        internal bool Available = true;
        internal Action<uint>? OnRead;
        internal MovieMemory()
        {
            Word(LegacyNativeMoviePlaybackReader.ModuleAddress, 25);
            Int(LegacyNativeMoviePlaybackReader.StartedAddress, 1);
            Int(LegacyNativeMoviePlaybackReader.CompletedAddress, 0);
            Int(LegacyNativeMoviePlaybackReader.PreparedAddress, 1);
            Int(LegacyNativeMoviePlaybackReader.Ending2StageAddress, 1);
            Int(LegacyNativeMoviePlaybackReader.MovieDirectoryPointerAddress, 0x20000);
            Text(0x20000, @"C:\Movies\");
            Text(LegacyNativeMoviePlaybackReader.Ending2NameAddress, "ending2.avi");
            Write(LegacyNativeMoviePlaybackReader.GetterAddress, Convert.FromHexString("558BEC83EC0CC745F800000000C745FC00000000C745F400000000833D10109A"));
            Write(LegacyNativeMoviePlaybackReader.UpdateSampleAddress,
                Convert.FromHexString("558BEC81EC040100005657C745FC01000000C745F000000000C70514109A0000"));
            Int(LegacyNativeMoviePlaybackReader.StockSampleAddress, 0x30000);
            Int(LegacyNativeMoviePlaybackReader.StockSeekingAddress, 0x40000);
            Int(0x40000, 0x50000); Int(0x50030, 0x60000);
        }
        internal void Ending3()
        {
            Word(LegacyNativeMoviePlaybackReader.ModuleAddress, 28);
            Int(LegacyNativeMoviePlaybackReader.CreditsModeAddress, 1);
            Int(LegacyNativeMoviePlaybackReader.CreditsStageAddress, 3);
            Int(LegacyNativeMoviePlaybackReader.CreditsStartedAddress, 1);
            Int(LegacyNativeMoviePlaybackReader.CreditsCompletedAddress, 0);
            Text(LegacyNativeMoviePlaybackReader.CreditsPathAddress, @"C:\Movies\ending3.avi");
        }
        internal void Ffnx()
        {
            var target = FfnxBase + LegacyNativeMoviePlaybackReader.FfnxGetterRva;
            Write(LegacyNativeMoviePlaybackReader.GetterAddress, [0xE9]);
            Int(LegacyNativeMoviePlaybackReader.GetterAddress + 1, unchecked((int)(target - LegacyNativeMoviePlaybackReader.GetterAddress - 5)));
            Write(target, Convert.FromHexString("A1E0D9101283B8FC01000000750333C0C3E97AD20000"));
            Write(FfnxBase + LegacyNativeMoviePlaybackReader.FfnxFrameGetterRva,
                Convert.FromHexString("A160AE3112C3"));
            Int(FfnxBase + LegacyNativeMoviePlaybackReader.FfnxMovieObjectPointerRva, (int)LegacyNativeMoviePlaybackReader.MovieObjectAddress);
            Double(FfnxBase + LegacyNativeMoviePlaybackReader.FfnxFpsRva, 30);
            var update = FfnxBase + LegacyNativeMoviePlaybackReader.FfnxUpdateSampleRva;
            Write(LegacyNativeMoviePlaybackReader.UpdateSampleAddress, [0xE9]);
            Int(LegacyNativeMoviePlaybackReader.UpdateSampleAddress + 1,
                unchecked((int)(update - LegacyNativeMoviePlaybackReader.UpdateSampleAddress - 5)));
            Write(update, Convert.FromHexString("A1E0D91012C7800002000000000000A1E0D9101283B8FC01000000750333C0C3"));
        }
        internal void Text(uint address, string value) => Write(address, System.Text.Encoding.Latin1.GetBytes(value + "\0"));
        internal void Int(uint address, int value) => Write(address, BitConverter.GetBytes(value));
        internal void Word(uint address, ushort value) => Write(address, BitConverter.GetBytes(value));
        internal void Double(uint address, double value) => Write(address, BitConverter.GetBytes(value));
        private void Write(uint address, byte[] value)
        { for (var i = 0; i < value.Length; i++) Bytes[address + (uint)i] = value[i]; }
        public bool TryRead(uint address, Span<byte> destination)
        {
            OnRead?.Invoke(address);
            if (!Available) return false;
            for (var i = 0; i < destination.Length; i++)
            { if (!Bytes.TryGetValue(address + (uint)i, out var value)) return false; destination[i] = value; }
            return true;
        }
    }
    private static void True(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
    private static void Equal<T>(T expected, T actual, string label)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{label}: expected {expected}, got {actual}"); }
}
