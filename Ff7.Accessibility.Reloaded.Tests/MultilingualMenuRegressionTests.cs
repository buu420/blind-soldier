using Ff7.Accessibility.Reloaded;

internal static class MultilingualMenuRegressionTests
{
    private const int MenuModule = 5;
    private const int RootContext = 0x3A83126F;
    private const int MenuTextContext = 0x3DCCCCCD;
    private const int ItemContext = 0x3DCED917;
    private const int QuitContext = 0x3C23D70A;

    internal static void Run()
    {
        PreservesPrintableUnicodeInRenderedText();
        ReadsLocalizedNameEntryPrompt();
        ReadsLocalizedQuitChoices();
        ReadsLocalizedConfigRows();
        ReadsLocalizedConfigWidgetRows();
        ReadsNativeConfigValueByRowInsteadOfEnglishLabel();
        IgnoresLocalizedTitleMenuChoices();
        IgnoresLocalizedStatusDetailLabelsAsPartyMembers();
        ReadsLocalizedPartyFormation();
        ReadsLocalizedStatusScreen();
        ReadsLocalizedTitleLoadScreen();
        ReadsLocalizedMateriaTutorial();
    }

    private static void PreservesPrintableUnicodeInRenderedText()
    {
        var now = UtcNow();
        var diagnostics = new MenuTextRenderDiagnostics(TimeSpan.Zero, () => now);
        Equal(
            true,
            diagnostics.TryCreateEntry("  Élément\t日本語\0discard", 12, 34, 7, 0, out var entry),
            "localized rendered text is accepted");
        Equal("Élément 日本語", entry.Text, "accented Latin and Japanese text survive normalization");
    }

    private static void ReadsLocalizedNameEntryPrompt()
    {
        var tracker = new FieldDialogueDrawSpeechTracker(TimeSpan.Zero);
        var now = UtcNow();
        var prompt = new MenuTextRenderEntry("名前を入力してください。", 53, 30, 7, ItemContext);

        tracker.Observe(prompt, MenuModule, now);
        tracker.Observe(prompt, MenuModule, now.AddMilliseconds(1));
        Equal("名前を入力してください。", tracker.Poll(now.AddMilliseconds(1)), "localized name-entry prompt");
    }

    private static void ReadsLocalizedQuitChoices()
    {
        var tracker = new StaticMenuCursorSpeechTracker(TimeSpan.Zero);
        var now = UtcNow();
        tracker.ObserveDraw(new MenuTextRenderEntry("Voulez-vous quitter ?", 220, 158, 7, QuitContext), now);
        tracker.ObserveCursor(new MenuCursorDrawObservation("B", MenuModule, 364, 304, RootContext), now.AddMilliseconds(1));
        tracker.ObserveDraw(new MenuTextRenderEntry("Oui", 212, 296, 7, QuitContext), now.AddMilliseconds(2));
        tracker.ObserveDraw(new MenuTextRenderEntry("Non", 414, 296, 0, QuitContext), now.AddMilliseconds(2));

        Equal("Non", tracker.Poll(now.AddMilliseconds(2)), "localized Quit selection");
    }

    private static void ReadsLocalizedConfigRows()
    {
        var tracker = new StaticMenuCursorSpeechTracker(TimeSpan.Zero);
        var now = UtcNow();
        tracker.ObserveDraw(new MenuTextRenderEntry("Configuration", 508, 13, 7, RootContext), now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Modifier le mode sonore", 16, 13, 7, MenuTextContext), now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Couleur de fenêtre", 62, 79, 5, MenuTextContext), now);
        tracker.ObserveDraw(new MenuTextRenderEntry("Son", 62, 117, 5, MenuTextContext), now);
        tracker.ObserveConfigRow(1, now.AddMilliseconds(1));

        Equal(
            "Son. Modifier le mode sonore",
            tracker.Poll(now.AddMilliseconds(1)),
            "localized Config row and help text");
    }

    private static void ReadsLocalizedConfigWidgetRows()
    {
        var coordinator = new InGameMenuSpeechCoordinator(TimeSpan.Zero);
        var now = UtcNow();
        coordinator.ObserveDraw(new MenuTextRenderEntry("Couleur de fenêtre", 72, 93, 7, MenuTextContext), now);
        coordinator.ObserveDraw(new MenuTextRenderEntry("Son", 72, 109, 7, MenuTextContext), now);
        coordinator.ObserveDraw(new MenuTextRenderEntry("Manette", 72, 125, 7, MenuTextContext), now);
        coordinator.ObserveCursor(new MenuCursorDrawObservation("B", MenuModule, 41, 125, MenuTextContext), now);
        coordinator.ObserveWidget(new MenuWidgetState("Config main", 2, 1, 10), now);

        Equal("Manette", coordinator.Poll(now), "localized Config widget selection");
    }

    private static void IgnoresLocalizedTitleMenuChoices()
    {
        var coordinator = new InGameMenuSpeechCoordinator(TimeSpan.Zero);
        var now = UtcNow();
        coordinator.ObserveDraw(new MenuTextRenderEntry("NOUVELLE PARTIE", 267, 192, 7, MenuTextContext), now);
        Equal(null, coordinator.Poll(now), "localized title menu choice is not mistaken for an in-game selection");
    }

    private static void ReadsNativeConfigValueByRowInsteadOfEnglishLabel()
    {
        var reader = new ConfigMenuValueReader(
            address => address == ConfigMenuValueReader.AddressBattleSpeed ? (byte)128 : (byte)0,
            _ => 0,
            address => address == ConfigMenuValueReader.AddressCurrentRow ? 5 : 0);

        Equal(
            "50 percent from Fast to Slow",
            reader.ReadCurrentMainValue("Vitesse de combat")?.Text,
            "localized Config labels use the native row index");
    }

    private static void IgnoresLocalizedStatusDetailLabelsAsPartyMembers()
    {
        var coordinator = new InGameMenuSpeechCoordinator(TimeSpan.Zero);
        var now = UtcNow();
        coordinator.ObserveDraw(new MenuTextRenderEntry("次のレベルまで", 110, 17, 7, 0x3E4CCCCD), now);
        coordinator.ObserveWidget(new MenuWidgetState("Status character", 0, 1, 3), now);
        Equal(null, coordinator.Poll(now), "localized Status detail is not a party member name");
    }

    private static void ReadsLocalizedPartyFormation()
    {
        var tracker = new PartyFormationSpeechTracker(TimeSpan.Zero);
        var now = UtcNow();
        tracker.ObserveDraw(new MenuTextRenderEntry("編成", 508, 14, 7, 0), MenuModule, now);
        tracker.ObserveDraw(new MenuTextRenderEntry("STARTボタンで決定", 26, 13, 7, MenuTextContext), MenuModule, now);
        tracker.ObserveDraw(new MenuTextRenderEntry("クラウド", 134, 77, 7, MenuTextContext), MenuModule, now);
        tracker.ObserveCursor(
            new MenuCursorDrawObservation("B", MenuModule, 0, 120, 0x3DCF0D84),
            now.AddMilliseconds(1));

        Equal(
            "編成. Party slot 1, クラウド. STARTボタンで決定",
            tracker.Poll(now.AddMilliseconds(80)),
            "localized Reform title, member name, and instruction");
    }

    private static void ReadsLocalizedStatusScreen()
    {
        var tracker = new StatusMenuSpeechTracker(TimeSpan.Zero);
        var now = UtcNow();
        tracker.ObserveDraw(new MenuTextRenderEntry("ステータス", 508, 13, 7, RootContext), now);
        tracker.ObserveDraw(new MenuTextRenderEntry("ちから", 60, 120, 5, MenuTextContext), now.AddMilliseconds(1));

        var speech = tracker.Poll(now.AddMilliseconds(1), () => (StatusMenuSnapshot?)CreateStatus());
        Equal(true, speech?.StartsWith("クラウド. Level 7.", StringComparison.Ordinal) == true, "localized Status ownership");
    }

    private static void ReadsLocalizedTitleLoadScreen()
    {
        var now = UtcNow();
        var tracker = new TitleLoadMenuSpeechTracker(
            TimeSpan.Zero,
            _ => true,
            (_, game) => game == 1 ? CreatePreview() : Ff7SaveSlotPreview.Empty);
        tracker.ObserveWidget(
            new ActiveMenuWidgetSnapshot(
                TitleLoadMenuSpeechTracker.SaveFileWidgetAddress,
                "Title load save file",
                MenuWidgetKind.TitleSaveFile,
                0,
                0,
                5,
                2,
                0,
                0,
                0),
            20,
            now);
        tracker.Poll(now);
        tracker.ObserveDraw(new MenuTextRenderEntry("セーブするゲームを選んでください。", 10, 13, 7, 0), 20, now.AddMilliseconds(10));
        tracker.ObserveDraw(new MenuTextRenderEntry("ゲーム", 343, 13, 6, 0), 20, now.AddMilliseconds(11));
        tracker.ObserveDraw(new MenuTextRenderEntry("01", 402, 13, 7, 0), 20, now.AddMilliseconds(12));

        Equal(
            true,
            tracker.Poll(now.AddMilliseconds(12))?.Contains("Game 1. クラウド", StringComparison.Ordinal) == true,
            "localized title load game selection");
    }

    private static void ReadsLocalizedMateriaTutorial()
    {
        var tracker = new MateriaTutorialSpeechTracker();
        var now = UtcNow();
        tracker.Observe(new MenuTextRenderEntry("マテリアの使い方を説明します。", 23, 23, 7, 0), MenuModule, now);
        tracker.Observe(new MenuTextRenderEntry("チュートリアル", 66, 431, 4, 0), MenuModule, now.AddMilliseconds(1));
        Equal("マテリアの使い方を説明します。", tracker.Poll(now.AddMilliseconds(1)), "localized Materia tutorial");
    }

    private static StatusMenuSnapshot CreateStatus() =>
        new(
            PartySlot: 0,
            CharacterId: 0,
            Name: "クラウド",
            Level: 7,
            CurrentHp: 314,
            MaxHp: 314,
            CurrentMp: 54,
            MaxMp: 54,
            Strength: 17,
            Dexterity: 8,
            Vitality: 14,
            Magic: 13,
            Spirit: 12,
            Luck: 10,
            Attack: 22,
            AttackPercent: 96,
            Defense: 18,
            DefensePercent: 13,
            MagicAttack: 13,
            MagicDefense: 12,
            MagicDefensePercent: 4,
            Experience: 1250,
            ExperienceToNextLevel: 550,
            LimitLevel: 1,
            WeaponName: "バスターソード",
            ArmorName: "ブロンズバングル",
            AccessoryName: null);

    private static Ff7SaveSlotPreview CreatePreview() =>
        new(false, "クラウド", 8, 296, 334, 18, 64, 539, 1572, "壱番魔晄炉");

    private static DateTime UtcNow() =>
        new(2026, 8, 9, 14, 0, 0, DateTimeKind.Utc);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
