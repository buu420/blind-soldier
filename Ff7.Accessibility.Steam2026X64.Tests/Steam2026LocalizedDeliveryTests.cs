using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;

/// <summary>
/// The x64 coordinator with reviewed localized identities, end to end: a Bunio Polish V1.05
/// prefix reaches description delivery while an unknown one stays silent, and the two films
/// Polish starts from moved bytes (zcoal_2 101 for English 104, zcoal_3 70 for English 90) are
/// held until the native film starts and then described by the film's own track, exactly like
/// English, instead of being read at once from raw localized offsets.
/// </summary>
internal static class Steam2026LocalizedDeliveryTests
{
    private static readonly DateTime Start = new(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);

    public static void Run()
    {
        ReviewedPolishPrefixDeliversItsMovedActionCueAndUnknownStaysSilent();
        foreach (var (field, entity, script, polishByte) in new[] { (729, 4, 0, 101), (730, 11, 3, 70) })
            PolishMovedFilmWaitsForTheNativeFilmAndStartsItsEnglishTrack(field, entity, script, polishByte);
        Console.WriteLine("Steam 2026 x64 localized delivery tests passed.");
    }

    private static LoadedFieldScriptIdentity Polish(int field) =>
        LocalizedFieldDescriptionCueManifest.ReviewedIdentities
            .Where(i => i.FieldId == field && i.Languages.Contains("pl"))
            .Select(i => new LoadedFieldScriptIdentity(field, i.PrefixSha256))
            .Single();

    private static LoadedFieldScriptIdentity NearMiss(LoadedFieldScriptIdentity identity) =>
        identity with { ScriptPrefixSha256 = identity.ScriptPrefixSha256[..^1] + (identity.ScriptPrefixSha256[^1] == '0' ? '1' : '0') };

    private static Steam2026FieldCutsceneDescriptionCoordinator Coordinator(
        FakeAddressSpace memory, LoadedFieldScriptIdentity identity, FieldMovieNarrationTracker? narration = null) =>
        new(memory, FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions(), narration,
            useLoadedScriptIdentity: true, readIdentity: () => identity);

    // nvmkin1 (322): Polish inserts an IFSW and a MUSIC, so the WAIT English runs at 446 runs at 456.
    private static void ReviewedPolishPrefixDeliversItsMovedActionCueAndUnknownStaysSilent()
    {
        var polish = Polish(322);
        var cue = LocalizedFieldDescriptionCueManifest.CreateDescriptions(polish)
            .Single(c => c.Key == new FieldCutsceneDescriptionKey(322, 0, 0, 456));
        var english = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions().Single(c => c.Key == new FieldCutsceneDescriptionKey(322, 0, 0, 446));
        Equal(english.Text, cue.Text, "the moved Polish cue is the English catalog cue");
        var snapshot = new Steam2026FieldCutsceneIngressSnapshot(1, Start,
            new FieldScriptContext(322, 0, 0, 456, FieldOpcodeAddressResolver.OpcodeWaitIndex));

        var reviewed = Coordinator(new FakeAddressSpace { FieldId = 322 }, polish);
        Equal(true, reviewed.Observe(snapshot), "the reviewed Polish prefix queues its native WAIT");
        var spoken = new List<string>();
        Equal(true, reviewed.TrySpeakPending(true, () => false, text => { spoken.Add(text); return true; }, Start, out var delivered),
            "and delivers it");
        Equal(cue, delivered, "at the coordinates the Polish script runs it");
        Equal(english.Text, spoken.Single(), "with the English catalog words, for the translation step");

        var unknown = Coordinator(new FakeAddressSpace { FieldId = 322 }, NearMiss(polish));
        Equal(false, unknown.Observe(snapshot), "an unknown prefix queues nothing");
        Equal(false, unknown.TrySpeakPending(true, () => false, _ => true, Start, out _), "and says nothing");
        Equal(false, unknown.Observe(snapshot with { Sequence = 2, Context = snapshot.Context with { ByteIndex = 446 } }),
            "and the English byte is not authorized for it either");
    }

    private static void PolishMovedFilmWaitsForTheNativeFilmAndStartsItsEnglishTrack(int field, int entity, int script, int polishByte)
    {
        var polish = Polish(field);
        var snapshot = new Steam2026FieldCutsceneIngressSnapshot(1, Start,
            new FieldScriptContext(field, entity, script, polishByte, FieldOpcodeAddressResolver.OpcodeMovieIndex));
        var memory = new FakeAddressSpace { FieldId = (ushort)field };
        memory.SetFilm(34, active: false);
        var film = new FakeOutput();
        var coordinator = Coordinator(memory, polish, new FieldMovieNarrationTracker(_ => film, _ => { }, FieldPositionReader.FieldModule));
        var spoken = new List<string>();
        bool Speak(string text) { spoken.Add(text); return true; }

        Equal(true, coordinator.Observe(snapshot), $"{field}: the Polish MOVIE queues the film's English cue");
        Equal(false, coordinator.TrySpeakPending(true, () => false, Speak, Start.AddMilliseconds(100), out _),
            $"{field}: the cue is held until the native film starts");
        Equal(0, spoken.Count + film.Starts, $"{field}: nothing is read before the film is on screen");

        memory.SetFilm(34, active: true);
        Equal(true, coordinator.TrySpeakPending(true, () => false, Speak, Start.AddMilliseconds(300), out var delivered),
            $"{field}: the film's description starts with the film");
        Equal(new FieldCutsceneDescriptionKey(field, entity, script, polishByte), delivered.Key, $"{field}: it is the Polish cue");
        Equal(1, film.Starts, $"{field}: by the film's own track, once");
        Equal(0, spoken.Count, $"{field}: and the paragraph is not read over it");

        var unknownMemory = new FakeAddressSpace { FieldId = (ushort)field };
        unknownMemory.SetFilm(34, active: false);
        var unknownFilm = new FakeOutput();
        var unknown = Coordinator(unknownMemory, NearMiss(polish), new FieldMovieNarrationTracker(_ => unknownFilm, _ => { }, FieldPositionReader.FieldModule));
        Equal(false, unknown.Observe(snapshot), $"{field}: an unknown prefix queues nothing");
        unknownMemory.SetFilm(34, active: true);
        Equal(false, unknown.TrySpeakPending(true, () => false, Speak, Start.AddMilliseconds(300), out _), $"{field}: and delivers nothing");
        Equal(0, spoken.Count + unknownFilm.Starts, $"{field}: no English offset was applied to it");
    }

    /// <summary>Enough field and film state for the coordinator's sample and dialogue reads.</summary>
    private sealed class FakeAddressSpace : ILegacyAddressSpace
    {
        private const uint ScriptContext = 0x00700000;
        private ushort movieActive;
        private ushort movieNumber;

        public byte Module { get; set; } = 1;
        public ushort FieldId { get; set; }
        public byte Disc { get; set; } = 1;

        public void SetFilm(int number, bool active)
        {
            movieNumber = (ushort)number;
            movieActive = active ? (ushort)1 : (ushort)0;
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            switch (virtualAddress)
            {
                case (uint)FieldPositionReader.AddressCurrentModule when destination.Length == 1:
                    destination[0] = Module;
                    return true;
                case (uint)FieldAudibleCueStateReader.AddressActiveFieldMessageCount when destination.Length == 1:
                    destination[0] = 0;
                    return true;
                case (uint)FieldAudibleCueStateReader.AddressFieldScriptContextPointer when destination.Length == 4:
                    return Write(destination, ScriptContext);
                case ScriptContext + FieldAudibleCueStateReader.FieldScriptContextStateOffset when destination.Length == 1:
                    destination[0] = FieldMovieNarrationPolicy.MovieHandlerStateInProgress;
                    return true;
                case ScriptContext + FieldAudibleCueStateReader.FieldScriptContextPhaseOffset when destination.Length == 2:
                    return Write(destination, (ushort)FieldMovieNarrationPolicy.MovieHandlerPhaseYielding);
                case (uint)FieldPositionReader.AddressFieldId when destination.Length == 2:
                    return Write(destination, FieldId);
                case (uint)FieldAudibleCueStateReader.AddressFieldBackgroundMovieMode when destination.Length == 1:
                    destination[0] = 0;
                    return true;
                case (uint)FieldAudibleCueStateReader.AddressFieldMovieActive when destination.Length == 2:
                    return Write(destination, movieActive);
                case (uint)FieldAudibleCueStateReader.AddressFieldMovieNumber when destination.Length == 2:
                    return Write(destination, movieNumber);
                case (uint)MovieFilmNameResolver.AddressMovieDisc when destination.Length == 1:
                    destination[0] = Disc;
                    return true;
                case (uint)FieldAudibleCueStateReader.AddressFieldMovieCommand when destination.Length == 1:
                    destination[0] = FieldMovieNarrationSample.CommandStartMovie;
                    return true;
                case (uint)FieldAudibleCueStateReader.AddressFieldMoviesSkipped when destination.Length == 1:
                    destination[0] = 0;
                    return true;
                case (uint)FieldAudibleCueStateReader.AddressFieldMovieFrame when destination.Length == 2:
                    return Write(destination, (ushort)0);
                default:
                    return false;
            }
        }

        private static bool Write(Span<byte> destination, ushort value)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
            return true;
        }

        private static bool Write(Span<byte> destination, uint value)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
            return true;
        }
    }

    private sealed class FakeOutput : IFieldMovieNarrationOutput
    {
        private bool playing;
        public int Starts { get; private set; }
        public bool IsPlaying => playing;
        public bool Start(string reason) { Starts++; playing = true; return true; }
        public bool Stop(string reason) { playing = false; return true; }
        public void Dispose() => playing = false;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
    }
}
