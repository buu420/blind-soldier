using Ff7.Accessibility.Core;
using Ff7.Accessibility.Runtime.Abstractions;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class CoreMenuDialogueDispatcherTests
{
    public static void Run()
    {
        AssertExactMenuSelectionAndAvailability();
        AssertMenuAmbiguityClosesSpeechState();
        AssertDialoguePageAndChoiceOrdering();
        AssertForegroundLossClearsDedupeState();
        AssertSpeechFailurePropagates();
    }

    private static void AssertExactMenuSelectionAndAvailability()
    {
        var output = new Output();
        var dispatcher = CreateDispatcher(output);
        var timestamp = Utc(0);

        dispatcher.Dispatch(Batch(Frame(timestamp, menu: Menu(1, new(0, "New Game", true, true)))), timestamp);
        dispatcher.Dispatch(Batch(Frame(Utc(1), menu: Menu(2, new(0, "New Game", true, true)))), Utc(1));
        dispatcher.Dispatch(Batch(Frame(Utc(2), menu: Menu(3, new(1, "Continue", false, true)))), Utc(2));

        AssertSequence(
            ["New Game", "Continue unavailable"],
            output.Spoken,
            "menu selection speech and semantic dedupe");

        dispatcher.Dispatch(Batch(Frame(Utc(3), menuUpdate: RuntimeDomainUpdate<MenuFrameObservation>.Closed)), Utc(3));
        dispatcher.Dispatch(Batch(Frame(Utc(4), menu: Menu(4, new(0, "New Game", true, true)))), Utc(4));
        AssertEqual("New Game", output.Spoken[^1], "identical menu selection after explicit close");
    }

    private static void AssertMenuAmbiguityClosesSpeechState()
    {
        var output = new Output();
        var dispatcher = CreateDispatcher(output);
        dispatcher.Dispatch(Batch(Frame(Utc(0), menu: Menu(1, new(0, "Item", true, true)))), Utc(0));
        dispatcher.Dispatch(Batch(Frame(
            Utc(1),
            menu: new MenuFrameObservation(
                "root",
                true,
                2,
                [new(0, "Item", true, true), new(1, "Magic", true, true)]))), Utc(1));
        dispatcher.Dispatch(Batch(Frame(Utc(2), menu: Menu(3, new(0, "Item", true, true)))), Utc(2));

        AssertSequence(["Item", "Item"], output.Spoken, "ambiguous menu frame is silent and clears dedupe");

        dispatcher.Dispatch(Batch(Frame(
            Utc(3),
            menu: new MenuFrameObservation(
                "root",
                true,
                4,
                [new(0, "Item", true, true), new(0, "Item", true, false)]))), Utc(3));
        AssertEqual(2, output.Spoken.Count, "duplicate native row identity is silent");
    }

    private static void AssertDialoguePageAndChoiceOrdering()
    {
        var output = new Output();
        var dispatcher = CreateDispatcher(output);
        var first = new DialoguePageObservation(
            true,
            0,
            1,
            "Cloud",
            "Not interested.",
            [new(0, "No", true, true), new(1, "Yes", true, false)]);
        dispatcher.Dispatch(Batch(Frame(Utc(0), dialogue: first)), Utc(0));
        dispatcher.Dispatch(Batch(Frame(Utc(1), dialogue: first)), Utc(1));

        var changedChoice = new DialoguePageObservation(
            true,
            0,
            1,
            "Cloud",
            "Not interested.",
            [new(0, "No", true, false), new(1, "Yes", false, true)]);
        dispatcher.Dispatch(Batch(Frame(Utc(2), dialogue: changedChoice)), Utc(2));

        AssertSequence(
            ["Cloud", "Not interested.", "No", "Yes unavailable"],
            output.Spoken,
            "dialogue speaker, visible page, and selected choice order");

        var ambiguousChoice = new DialoguePageObservation(
            true,
            0,
            2,
            string.Empty,
            "Choose.",
            [new(0, "Left", true, true), new(1, "Right", true, true)]);
        dispatcher.Dispatch(Batch(Frame(Utc(3), dialogue: ambiguousChoice)), Utc(3));
        AssertEqual("Choose.", output.Spoken[^1], "ambiguous choices do not suppress verified visible page text");
    }

    private static void AssertForegroundLossClearsDedupeState()
    {
        var output = new Output();
        var dispatcher = CreateDispatcher(output);
        dispatcher.Dispatch(Batch(Frame(Utc(0), menu: Menu(1, new(0, "Materia", true, true)))), Utc(0));
        dispatcher.Dispatch(Batch(Frame(Utc(1), isForeground: false, menu: Menu(2, new(0, "Materia", true, true)))), Utc(1));
        dispatcher.Dispatch(Batch(Frame(Utc(2), menu: Menu(3, new(0, "Materia", true, true)))), Utc(2));
        AssertSequence(["Materia", "Materia"], output.Spoken, "foreground loss resets textual speech ownership");
    }

    private static void AssertSpeechFailurePropagates()
    {
        var output = new Output { FailSpeech = true };
        var dispatcher = CreateDispatcher(output);
        try
        {
            dispatcher.Dispatch(Batch(Frame(Utc(0), menu: Menu(1, new(0, "Save", true, true)))), Utc(0));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException("Menu speech output failure must propagate to AccessibilityRuntime fail-close.");
    }

    private static RuntimeEventDispatcher CreateDispatcher(Output output) =>
        new(
            new AccessibilityConfig
            {
                EnableSpeech = true,
                EnableRuntimeMenuSpeech = true,
                EnableRuntimeDialogueSpeech = true
            },
            output,
            _ => { });

    private static MenuFrameObservation Menu(int revision, MenuRowObservation selected) =>
        new("root", true, revision, [selected]);

    private static RuntimeDispatchBatch Batch(RuntimeFrameObservation frame) =>
        new(frame, Array.Empty<RuntimeEvent>(), null);

    private static RuntimeFrameObservation Frame(
        DateTime timestamp,
        bool isForeground = true,
        MenuFrameObservation? menu = null,
        DialoguePageObservation? dialogue = null,
        RuntimeDomainUpdate<MenuFrameObservation>? menuUpdate = null,
        RuntimeDomainUpdate<DialoguePageObservation>? dialogueUpdate = null) =>
        new(
            timestamp,
            new GameLifecycleObservation(isForeground, false, 0, 1),
            menuUpdate ?? (menu is null
                ? RuntimeDomainUpdate<MenuFrameObservation>.Unchanged
                : RuntimeDomainUpdate<MenuFrameObservation>.Present(menu)),
            dialogueUpdate ?? (dialogue is null
                ? RuntimeDomainUpdate<DialoguePageObservation>.Unchanged
                : RuntimeDomainUpdate<DialoguePageObservation>.Present(dialogue)),
            RuntimeDomainUpdate<FieldFrameObservation>.Unchanged,
            RuntimeDomainUpdate<BattleFrameObservation>.Unchanged,
            RuntimeDomainUpdate<NavigationWorldObservation>.Unchanged);

    private static DateTime Utc(int seconds) =>
        new(2026, 7, 19, 20, 0, seconds, DateTimeKind.Utc);

    private static void AssertSequence(IReadOnlyList<string> expected, IReadOnlyList<string> actual, string label)
    {
        AssertEqual(expected.Count, actual.Count, $"{label} count");
        for (var index = 0; index < expected.Count; index++)
        {
            AssertEqual(expected[index], actual[index], $"{label} item {index}");
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private sealed class Output : IAccessibilityOutput
    {
        public List<string> Spoken { get; } = [];

        public bool FailSpeech { get; set; }

        public void Speak(string text, bool interrupt)
        {
            if (FailSpeech)
            {
                throw new InvalidOperationException("test speech failure");
            }

            Spoken.Add(text);
        }

        public void PlayCue(AccessibilityCue cue)
        {
        }

        public void StopCue(AccessibilityCueKind kind)
        {
        }
    }
}
