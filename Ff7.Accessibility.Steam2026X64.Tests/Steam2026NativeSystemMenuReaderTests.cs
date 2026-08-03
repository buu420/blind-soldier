using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime.SystemMenu;

internal static class Steam2026NativeSystemMenuReaderTests
{
    private const ulong ModuleBase = 0x0000000140000000;
    private const ulong TestButtonVtable = ModuleBase + 0x01010000;
    private const ulong TestButtonCompleteObjectLocator = ModuleBase + 0x01810000;
    private const ulong TestNonButtonVtable = ModuleBase + 0x01010100;
    private const ulong TestNonButtonCompleteObjectLocator = ModuleBase + 0x01811000;

    internal static void Run()
    {
        DefinitionsMatchReverseEngineeredNativeLayout();
        ReadsFocusedNativeButtonStateFromTheContainerVector();
        RejectsAmbiguousNativeButtonSelection();
        SkipsNonButtonNodesWhenDerivingNativeFocus();
        ReadsRootBackFromItsDedicatedNativeControl();
        ReadsGameOptionsBackFromItsDedicatedNativeControl();
        ReadsControlsBackFromItsDedicatedNativeControl();
        ReadsRichSceneFocusFromTheNativeNavigationIndex();
        ReadsAutosaveActionsFromTheirNativeControls();
        ReadsAutosaveModalChoicesFromTheirNamedNativeControls();
        DiscoversTheActiveSceneThroughTheRuntimeMuiManager();
        SharedLeaveCallbacksRecoverSceneFromActiveInstance();
        ReadsNestedSceneFocusAndNativeValues();
        ReadsKeyboardAssignmentsFromTheNativeBindingObject();
        ReadsControllerAssignmentsForXboxAndPlayStationLayouts();
        ReadsModalAfterTheNativeParentButtonIsCleared();
        ReadsModalChoiceAndReturnsToParent();
        RejectsAStaleOrWrongSceneInstance();
    }

    private static void SkipsNonButtonNodesWhenDerivingNativeFocus()
    {
        var memory = new FakeNativeMemoryReader();
        var reader = new Steam2026NativeSystemMenuReader(ModuleBase, memory);
        const ulong instance = 0x0000000200A00000;
        const ulong container = 0x0000000200A10000;
        var nodes = new[]
        {
            0x0000000200A20000UL,
            0x0000000200A30000UL,
            0x0000000200A40000UL,
            0x0000000200A50000UL,
            0x0000000200A60000UL
        };
        var definition = Steam2026NativeSystemMenuDefinitions.Get(
            Steam2026NativeSystemMenuScene.EscapeRoot);
        memory.Write(
            instance,
            BitConverter.GetBytes(ModuleBase + definition.VtableRva));
        memory.Write(
            instance + definition.FocusObjectOffset,
            BitConverter.GetBytes(container));
        WriteButtonVector(memory, container, 0x0000000200A70000, nodes);
        WriteNonButtonObject(memory, nodes[0]);
        for (var index = 0; index < nodes.Length; index++)
        {
            memory.Write(
                nodes[index] + 0xD8,
                BitConverter.GetBytes(index == 2 ? 2 : 0));
        }
        reader.ObserveLifecycle(new Steam2026NativeSystemMenuLifecycleEvent(
            Steam2026NativeSystemMenuScene.EscapeRoot,
            instance,
            Opened: true,
            Generation: 7));

        True(reader.TryRead(out var observation), "focus after decorative node");
        Equal("boosts", observation.ControlId, "non-button node does not shift option index");
    }

    private static void RejectsAmbiguousNativeButtonSelection()
    {
        var memory = new FakeNativeMemoryReader();
        var reader = new Steam2026NativeSystemMenuReader(ModuleBase, memory);
        const ulong instance = 0x0000000200900000;
        const ulong container = 0x0000000200910000;
        WriteScene(
            memory,
            Steam2026NativeSystemMenuScene.EscapeRoot,
            instance,
            container,
            focus: 0);
        True(memory.TryReadUInt64(container + 0x38, out var begin), "ambiguous vector begin");
        True(
            memory.TryReadUInt64(begin + 0x10, out var secondButton),
            "ambiguous second button");
        memory.Write(secondButton + 0xD8, BitConverter.GetBytes(2));
        reader.ObserveLifecycle(new Steam2026NativeSystemMenuLifecycleEvent(
            Steam2026NativeSystemMenuScene.EscapeRoot,
            instance,
            Opened: true,
            Generation: 6));

        False(reader.TryRead(out _), "two selected native buttons fail closed");
    }

    private static void ReadsFocusedNativeButtonStateFromTheContainerVector()
    {
        var memory = new FakeNativeMemoryReader();
        var reader = new Steam2026NativeSystemMenuReader(ModuleBase, memory);
        const ulong instance = 0x0000000200800000;
        const ulong container = 0x0000000200810000;
        var controls = new[]
        {
            0x0000000200820000UL,
            0x0000000200830000UL,
            0x0000000200840000UL,
            0x0000000200850000UL
        };
        WriteScene(
            memory,
            Steam2026NativeSystemMenuScene.EscapeRoot,
            instance,
            container,
            focus: 0);
        WriteButtonVector(memory, container, 0x0000000200860000, controls);
        memory.Write(controls[0] + 0xD8, BitConverter.GetBytes(0));
        memory.Write(controls[1] + 0xD8, BitConverter.GetBytes(2));
        memory.Write(controls[2] + 0xD8, BitConverter.GetBytes(0));
        memory.Write(controls[3] + 0xD8, BitConverter.GetBytes(0));
        reader.ObserveLifecycle(new Steam2026NativeSystemMenuLifecycleEvent(
            Steam2026NativeSystemMenuScene.EscapeRoot,
            instance,
            Opened: true,
            Generation: 5));

        True(reader.TryRead(out var observation), "native selected-button observation");
        Equal("boosts", observation.ControlId, "selected child state determines focus");
    }

    private static void ReadsRichSceneFocusFromTheNativeNavigationIndex()
    {
        var memory = new FakeNativeMemoryReader();
        var reader = new Steam2026NativeSystemMenuReader(ModuleBase, memory);
        const ulong instance = 0x0000000200870000;
        const ulong navigation = 0x0000000200880000;
        var definition = Steam2026NativeSystemMenuDefinitions.Get(
            Steam2026NativeSystemMenuScene.System);
        memory.Write(
            instance,
            BitConverter.GetBytes(ModuleBase + definition.VtableRva));
        memory.Write(
            instance + definition.FocusObjectOffset,
            BitConverter.GetBytes(navigation));
        memory.Write(navigation + 0xE8, BitConverter.GetBytes(4));
        memory.Write(instance + 0x298, BitConverter.GetBytes(63));
        reader.ObserveLifecycle(new Steam2026NativeSystemMenuLifecycleEvent(
            Steam2026NativeSystemMenuScene.System,
            instance,
            Opened: true,
            Generation: 8));

        True(reader.TryRead(out var observation), "rich native navigation observation");
        Equal("brightness", observation.ControlId, "rich native navigation focus");
        Equal("63", observation.Value, "rich native navigation value");
    }

    private static void ReadsRootBackFromItsDedicatedNativeControl()
    {
        var memory = new FakeNativeMemoryReader();
        var reader = new Steam2026NativeSystemMenuReader(ModuleBase, memory);
        const ulong instance = 0x0000000200890000;
        const ulong rootOptions = 0x00000002008A0000;
        const ulong optionsBegin = 0x00000002008B0000;
        const ulong gameOptions = 0x00000002008C0000;
        const ulong boosts = 0x00000002008D0000;
        const ulong exit = 0x00000002008E0000;
        const ulong back = 0x00000002008F0000;
        var definition = Steam2026NativeSystemMenuDefinitions.Get(
            Steam2026NativeSystemMenuScene.EscapeRoot);
        memory.Write(
            instance,
            BitConverter.GetBytes(ModuleBase + definition.VtableRva));
        memory.Write(
            instance + definition.FocusObjectOffset,
            BitConverter.GetBytes(rootOptions));
        WriteButtonVector(
            memory,
            rootOptions,
            optionsBegin,
            [gameOptions, boosts, exit]);
        memory.Write(gameOptions + 0xD8, BitConverter.GetBytes(0));
        memory.Write(boosts + 0xD8, BitConverter.GetBytes(0));
        memory.Write(exit + 0xD8, BitConverter.GetBytes(0));
        WriteButtonObject(memory, back);
        memory.Write(instance + 0x1A0, BitConverter.GetBytes(back));
        memory.Write(back + 0xD8, BitConverter.GetBytes(2));
        reader.ObserveLifecycle(new Steam2026NativeSystemMenuLifecycleEvent(
            Steam2026NativeSystemMenuScene.EscapeRoot,
            instance,
            Opened: true,
            Generation: 9));

        True(reader.TryRead(out var observation), "dedicated root Back observation");
        Equal("back", observation.ControlId, "dedicated root Back focus");
    }

    private static void ReadsGameOptionsBackFromItsDedicatedNativeControl() =>
        ReadsDedicatedBackFromNativeControl(
            Steam2026NativeSystemMenuScene.GameOptions,
            instance: 0x0000000200B00000,
            focusObject: 0x0000000200B10000,
            controlOffset: 0x1A0,
            ordinaryControlCount: 3,
            generation: 40,
            label: "Game Options");

    private static void ReadsControlsBackFromItsDedicatedNativeControl() =>
        ReadsDedicatedBackFromNativeControl(
            Steam2026NativeSystemMenuScene.Controls,
            instance: 0x0000000200C00000,
            focusObject: 0x0000000200C10000,
            controlOffset: 0x190,
            ordinaryControlCount: 2,
            generation: 41,
            label: "Controls");

    private static void ReadsDedicatedBackFromNativeControl(
        Steam2026NativeSystemMenuScene scene,
        ulong instance,
        ulong focusObject,
        ulong controlOffset,
        int ordinaryControlCount,
        long generation,
        string label)
    {
        var memory = new FakeNativeMemoryReader();
        var reader = new Steam2026NativeSystemMenuReader(ModuleBase, memory);
        var definition = Steam2026NativeSystemMenuDefinitions.Get(scene);
        var controls = Enumerable.Range(0, ordinaryControlCount)
            .Select(index => focusObject + 0x2000 + ((ulong)index * 0x1000))
            .ToArray();
        var back = focusObject + 0x9000;
        memory.Write(
            instance,
            BitConverter.GetBytes(ModuleBase + definition.VtableRva));
        memory.Write(
            instance + definition.FocusObjectOffset,
            BitConverter.GetBytes(focusObject));
        WriteButtonVector(memory, focusObject, focusObject + 0x1000, controls);
        foreach (var control in controls)
        {
            memory.Write(control + 0xD8, BitConverter.GetBytes(0));
        }

        WriteButtonObject(memory, back);
        memory.Write(instance + controlOffset, BitConverter.GetBytes(back));
        memory.Write(back + 0xD8, BitConverter.GetBytes(2));
        reader.ObserveLifecycle(new Steam2026NativeSystemMenuLifecycleEvent(
            scene,
            instance,
            Opened: true,
            Generation: generation));

        True(reader.TryRead(out var observation), $"dedicated {label} Back observation");
        Equal("back", observation.ControlId, $"dedicated {label} Back focus");
    }

    private static void ReadsAutosaveActionsFromTheirNativeControls()
    {
        var memory = new FakeNativeMemoryReader();
        var reader = new Steam2026NativeSystemMenuReader(ModuleBase, memory);
        const ulong instance = 0x0000000200920000;
        const ulong navigation = 0x0000000200930000;
        const ulong autosave = 0x0000000200940000;
        const ulong apply = 0x0000000200950000;
        const ulong restoreDefaults = 0x0000000200960000;
        var definition = Steam2026NativeSystemMenuDefinitions.Get(
            Steam2026NativeSystemMenuScene.Autosave);
        memory.Write(
            instance,
            BitConverter.GetBytes(ModuleBase + definition.VtableRva));
        memory.Write(
            instance + definition.FocusObjectOffset,
            BitConverter.GetBytes(navigation));
        memory.Write(navigation + 0xE8, BitConverter.GetBytes(1));
        WriteButtonObject(memory, autosave);
        WriteButtonObject(memory, apply);
        WriteButtonObject(memory, restoreDefaults);
        memory.Write(instance + 0x210, BitConverter.GetBytes(autosave));
        memory.Write(instance + 0x220, BitConverter.GetBytes(apply));
        memory.Write(instance + 0x230, BitConverter.GetBytes(restoreDefaults));
        memory.Write(autosave + 0xD8, BitConverter.GetBytes(0));
        memory.Write(apply + 0xD8, BitConverter.GetBytes(2));
        memory.Write(restoreDefaults + 0xD8, BitConverter.GetBytes(0));
        reader.ObserveLifecycle(new Steam2026NativeSystemMenuLifecycleEvent(
            Steam2026NativeSystemMenuScene.Autosave,
            instance,
            Opened: true,
            Generation: 10));

        True(reader.TryRead(out var applyObservation), "Autosave Apply observation");
        Equal("apply", applyObservation.ControlId, "Autosave Apply focus");

        memory.Write(apply + 0xD8, BitConverter.GetBytes(0));
        memory.Write(restoreDefaults + 0xD8, BitConverter.GetBytes(2));
        True(reader.TryRead(out var defaultObservation), "Autosave Default observation");
        Equal("default", defaultObservation.ControlId, "Autosave Default focus");
    }

    private static void ReadsAutosaveModalChoicesFromTheirNamedNativeControls()
    {
        var memory = new FakeNativeMemoryReader();
        var reader = new Steam2026NativeSystemMenuReader(ModuleBase, memory);
        const ulong instance = 0x0000000200970000;
        const ulong navigation = 0x0000000200980000;
        const ulong autosave = 0x0000000200990000;
        const ulong apply = 0x00000002009A0000;
        const ulong restoreDefaults = 0x00000002009B0000;
        const ulong modal = 0x00000002009C0000;
        const ulong decide = 0x00000002009D0000;
        const ulong cancel = 0x00000002009E0000;
        var definition = Steam2026NativeSystemMenuDefinitions.Get(
            Steam2026NativeSystemMenuScene.Autosave);
        memory.Write(
            instance,
            BitConverter.GetBytes(ModuleBase + definition.VtableRva));
        memory.Write(
            instance + definition.FocusObjectOffset,
            BitConverter.GetBytes(navigation));
        memory.Write(navigation + 0xE8, BitConverter.GetBytes(1));
        WriteButtonObject(memory, autosave);
        WriteButtonObject(memory, apply);
        WriteButtonObject(memory, restoreDefaults);
        memory.Write(instance + 0x210, BitConverter.GetBytes(autosave));
        memory.Write(instance + 0x220, BitConverter.GetBytes(apply));
        memory.Write(instance + 0x230, BitConverter.GetBytes(restoreDefaults));
        memory.Write(autosave + 0xD8, BitConverter.GetBytes(2));
        memory.Write(apply + 0xD8, BitConverter.GetBytes(0));
        memory.Write(restoreDefaults + 0xD8, BitConverter.GetBytes(0));
        memory.Write(instance + 0x240, BitConverter.GetBytes(modal));
        memory.Write(modal + 0xA0, [1]);
        WriteButtonObject(memory, decide);
        WriteButtonObject(memory, cancel);
        memory.Write(instance + 0x250, BitConverter.GetBytes(decide));
        memory.Write(instance + 0x260, BitConverter.GetBytes(cancel));
        memory.Write(decide + 0xD8, BitConverter.GetBytes(0));
        memory.Write(cancel + 0xD8, BitConverter.GetBytes(2));
        reader.ObserveLifecycle(new Steam2026NativeSystemMenuLifecycleEvent(
            Steam2026NativeSystemMenuScene.Autosave,
            instance,
            Opened: true,
            Generation: 11));

        True(reader.TryRead(out var observation), "Autosave modal observation");
        Equal("autosave-modal", observation.SceneId, "Autosave modal scene");
        Equal("No", observation.Value, "Autosave modal Cancel choice");
    }

    private static void DefinitionsMatchReverseEngineeredNativeLayout()
    {
        Equal(8, Steam2026NativeSystemMenuDefinitions.All.Count, "native scene count");
        Equal(
            0x015D68D0UL,
            Steam2026NativeSystemMenuDefinitions.ManagerTickRva,
            "native MUI settings-manager tick RVA");
        Equal(
            "48895C2410555657415641574883EC500F297424400F28F1",
            Convert.ToHexString(
                Steam2026NativeSystemMenuDefinitions.ManagerTickPrefix),
            "native MUI settings-manager tick exact prefix");
        Equal(
            0x015C30D0UL,
            Steam2026NativeSystemMenuDefinitions.DirectionInputRva,
            "native MUI direction-input callback RVA");
        Equal(
            "4C8B81900000004180B8A8000000000F852B050000488B411880B8A800000000",
            Convert.ToHexString(
                Steam2026NativeSystemMenuDefinitions.DirectionInputPrefix),
            "native MUI direction-input callback exact prefix");

        var expectedLifecycle = new[]
        {
            (Steam2026NativeSystemMenuScene.EscapeRoot, 0x015D42A0UL, 0x015D5B80UL),
            (Steam2026NativeSystemMenuScene.GameOptions, 0x015AA8F0UL, 0x015ABCD0UL),
            (Steam2026NativeSystemMenuScene.Controls, 0x015AD0A0UL, 0x015ABCD0UL),
            (Steam2026NativeSystemMenuScene.Autosave, 0x0158F080UL, 0x01590D00UL),
            (Steam2026NativeSystemMenuScene.Boosts, 0x01594730UL, 0x01590D00UL),
            (Steam2026NativeSystemMenuScene.Keyboard, 0x015BA8B0UL, 0x01590D00UL),
            (Steam2026NativeSystemMenuScene.Controller, 0x015A6330UL, 0x01590D00UL),
            (Steam2026NativeSystemMenuScene.System, 0x015EE040UL, 0x015EFD20UL)
        };
        foreach (var (scene, enterRva, leaveRva) in expectedLifecycle)
        {
            var definition = Steam2026NativeSystemMenuDefinitions.Get(scene);
            Equal(enterRva, definition.EnterRva, $"{scene} scene-enter RVA");
            Equal(leaveRva, definition.LeaveRva, $"{scene} scene-leave RVA");
            Equal(24, definition.EnterPrefix.Length, $"{scene} scene-enter exact prefix length");
            Equal(24, definition.LeavePrefix.Length, $"{scene} scene-leave exact prefix length");
        }
        Equal(
            8,
            Steam2026NativeSystemMenuDefinitions.All.Select(value => value.EnterRva).Distinct().Count(),
            "each scene has a distinct native enter callback");
        Equal(
            4,
            Steam2026NativeSystemMenuDefinitions.All.Select(value => value.LeaveRva).Distinct().Count(),
            "native leave callbacks are intentionally shared by scene families");

        var root = Steam2026NativeSystemMenuDefinitions.Get(
            Steam2026NativeSystemMenuScene.EscapeRoot);
        Equal(0x0166D790UL, root.VtableRva, "root vtable RVA");
        Equal(0x130UL, root.FocusObjectOffset, "root focus object offset");
        Equal(0x1B0UL, root.ModalObjectOffset, "root modal object offset");

        var boosts = Steam2026NativeSystemMenuDefinitions.Get(
            Steam2026NativeSystemMenuScene.Boosts);
        Equal(0x138UL, boosts.FocusObjectOffset, "rich focus object offset");
        Equal(0x240UL, boosts.ModalObjectOffset, "rich modal object offset");
    }

    private static void DiscoversTheActiveSceneThroughTheRuntimeMuiManager()
    {
        const ulong host = 0x0000000201000000;
        const ulong manager = 0x0000000201010000;
        var memory = new FakeNativeMemoryReader();
        var reader = new Steam2026NativeSystemMenuReader(ModuleBase, memory);
        var root = WriteScene(
            memory,
            Steam2026NativeSystemMenuScene.EscapeRoot,
            instance: 0x0000000201020000,
            focusObject: 0x0000000201030000,
            focus: 0);
        memory.Write(root + 0xA0, [1]);
        memory.Write(host + 0x50, BitConverter.GetBytes(manager));
        memory.Write(manager + 0x38, BitConverter.GetBytes(root));
        memory.Write(manager + 0x48, BitConverter.GetBytes(0UL));

        True(
            reader.ObserveManagerHost(host),
            "runtime MUI manager discovers current Escape scene");
        True(reader.TryRead(out var rootObservation), "manager root observation");
        Equal("escape-root", rootObservation.SceneId, "manager root scene");
        Equal(1, reader.ActiveSceneCount, "one manager-discovered scene");

        var boosts = WriteScene(
            memory,
            Steam2026NativeSystemMenuScene.Boosts,
            instance: 0x0000000201040000,
            focusObject: 0x0000000201050000,
            focus: 5);
        memory.Write(boosts + 0xA0, [1]);
        memory.Write(boosts + 0x270, [1]);
        memory.Write(manager + 0x48, BitConverter.GetBytes(boosts));

        True(
            reader.ObserveManagerHost(host),
            "active pending scene takes precedence during transition");
        True(reader.TryRead(out var boostObservation), "manager child observation");
        Equal("boosts", boostObservation.SceneId, "manager child scene");
        Equal("speed-boost", boostObservation.ControlId, "manager child focus");

        memory.Write(boosts + 0xA0, [0]);
        memory.Write(root + 0xA0, [0]);
        False(
            reader.ObserveManagerHost(host),
            "inactive current and pending scenes close the menu");
        False(reader.TryRead(out _), "closed manager exposes no stale scene");

        memory.Write(root + 0xA0, [1]);
        memory.Write(root, BitConverter.GetBytes(ModuleBase + 0x1234));
        False(
            reader.ObserveManagerHost(host),
            "manager rejects a scene with an unknown native vtable");
        False(reader.TryRead(out _), "wrong-vtable scene remains unavailable");
    }

    private static void SharedLeaveCallbacksRecoverSceneFromActiveInstance()
    {
        var tracker = new Steam2026NativeSystemMenuLifecycleTracker();
        var gameOptionsInstance = unchecked((nint)0x0000000200001000UL);
        var controlsInstance = unchecked((nint)0x0000000200002000UL);

        True(
            tracker.TryOpen(
                Steam2026NativeSystemMenuScene.GameOptions,
                gameOptionsInstance,
                out var gameOptionsOpen),
            "Game Options scene enter is tracked");
        Equal(true, gameOptionsOpen.Opened, "Game Options enter event");
        Equal(
            Steam2026NativeSystemMenuScene.GameOptions,
            gameOptionsOpen.Scene,
            "Game Options enter scene");

        True(
            tracker.TryOpen(
                Steam2026NativeSystemMenuScene.Controls,
                controlsInstance,
                out var controlsOpen),
            "Controls scene enter is tracked");
        Equal(
            Steam2026NativeSystemMenuScene.Controls,
            controlsOpen.Scene,
            "Controls enter scene");

        True(
            tracker.TryClose(gameOptionsInstance, out var gameOptionsClose),
            "shared simple-menu leave resolves Game Options by instance");
        Equal(false, gameOptionsClose.Opened, "Game Options leave event");
        Equal(
            Steam2026NativeSystemMenuScene.GameOptions,
            gameOptionsClose.Scene,
            "shared simple-menu leave retains the correct scene");

        True(
            tracker.TryClose(controlsInstance, out var controlsClose),
            "shared simple-menu leave resolves Controls by instance");
        Equal(
            Steam2026NativeSystemMenuScene.Controls,
            controlsClose.Scene,
            "shared simple-menu leave keeps instances independent");
        False(
            tracker.TryClose(gameOptionsInstance, out _),
            "an already-closed instance cannot produce a duplicate leave event");
    }

    private static void ReadsNestedSceneFocusAndNativeValues()
    {
        var memory = new FakeNativeMemoryReader();
        var reader = new Steam2026NativeSystemMenuReader(ModuleBase, memory);
        var root = WriteScene(
            memory,
            Steam2026NativeSystemMenuScene.EscapeRoot,
            instance: 0x0000000200000000,
            focusObject: 0x0000000200010000,
            focus: 0);
        reader.ObserveLifecycle(new Steam2026NativeSystemMenuLifecycleEvent(
            Steam2026NativeSystemMenuScene.EscapeRoot,
            root,
            Opened: true,
            Generation: 10));

        True(reader.TryRead(out var rootObservation), "root observation");
        Equal("escape-root", rootObservation.SceneId, "root scene");
        Equal("game-options", rootObservation.ControlId, "root focus");

        var system = WriteScene(
            memory,
            Steam2026NativeSystemMenuScene.System,
            instance: 0x0000000200100000,
            focusObject: 0x0000000200110000,
            focus: 2);
        memory.Write(system + 0x290, BitConverter.GetBytes(1));
        reader.ObserveLifecycle(new Steam2026NativeSystemMenuLifecycleEvent(
            Steam2026NativeSystemMenuScene.System,
            system,
            Opened: true,
            Generation: 11));

        True(reader.TryRead(out var systemObservation), "system observation");
        Equal("system", systemObservation.SceneId, "nested system scene");
        Equal("display-mode", systemObservation.ControlId, "system control");
        Equal("Borderless Windowed", systemObservation.Value, "native display mode");
        Equal(2, systemObservation.Position, "display mode position");
        Equal(3, systemObservation.Count, "display mode count");

        SelectButton(memory, 0x0000000200110000, 4);
        memory.Write(system + 0x298, BitConverter.GetBytes(52));
        True(reader.TryRead(out var brightness), "brightness observation");
        Equal("brightness", brightness.ControlId, "brightness control");
        Equal("52", brightness.Value, "brightness value");

        reader.ObserveLifecycle(new Steam2026NativeSystemMenuLifecycleEvent(
            Steam2026NativeSystemMenuScene.System,
            system,
            Opened: false,
            Generation: 12));
        True(reader.TryRead(out var returned), "returned root observation");
        Equal("escape-root", returned.SceneId, "parent restored after child close");
    }

    private static void ReadsModalChoiceAndReturnsToParent()
    {
        var memory = new FakeNativeMemoryReader();
        var reader = new Steam2026NativeSystemMenuReader(ModuleBase, memory);
        var instance = 0x0000000300000000UL;
        var focusObject = 0x0000000300010000UL;
        var modal = 0x0000000300020000UL;
        WriteScene(
            memory,
            Steam2026NativeSystemMenuScene.EscapeRoot,
            instance,
            focusObject,
            focus: 2);
        memory.Write(instance + 0x1B0, BitConverter.GetBytes(modal));
        memory.Write(modal + 0xA0, [1]);
        WriteButtonVector(
            memory,
            modal,
            modal + 0x1000,
            [modal + 0x2000, modal + 0x3000]);
        SelectButton(memory, modal, 0);
        reader.ObserveLifecycle(new Steam2026NativeSystemMenuLifecycleEvent(
            Steam2026NativeSystemMenuScene.EscapeRoot,
            instance,
            Opened: true,
            Generation: 20));

        True(reader.TryRead(out var modalObservation), "modal observation");
        Equal("escape-root-modal", modalObservation.SceneId, "modal scene");
        Equal("confirm-choice", modalObservation.ControlId, "modal control");
        Equal("Exit game?", modalObservation.ModalText, "modal warning");
        Equal("Yes", modalObservation.Value, "modal choice");

        SelectButton(memory, modal, 1);
        True(reader.TryRead(out var moved), "moved modal observation");
        Equal("No", moved.Value, "moved modal choice");

        memory.Write(modal + 0xA0, [0]);
        True(reader.TryRead(out var parent), "parent after modal closes");
        Equal("exit", parent.ControlId, "parent focus after modal close");
    }

    private static void ReadsModalAfterTheNativeParentButtonIsCleared()
    {
        var memory = new FakeNativeMemoryReader();
        var reader = new Steam2026NativeSystemMenuReader(ModuleBase, memory);
        const ulong instance = 0x0000000300100000;
        const ulong focusObject = 0x0000000300110000;
        const ulong modal = 0x0000000300120000;
        WriteScene(
            memory,
            Steam2026NativeSystemMenuScene.EscapeRoot,
            instance,
            focusObject,
            focus: 2);
        reader.ObserveLifecycle(new Steam2026NativeSystemMenuLifecycleEvent(
            Steam2026NativeSystemMenuScene.EscapeRoot,
            instance,
            Opened: true,
            Generation: 21));
        True(reader.TryRead(out var parent), "exit parent before modal");
        Equal("exit", parent.ControlId, "cached exit parent focus");

        memory.Write(instance + 0x1B0, BitConverter.GetBytes(modal));
        memory.Write(modal + 0xA0, [1]);
        WriteButtonVector(
            memory,
            modal,
            modal + 0x1000,
            [modal + 0x2000, modal + 0x3000]);
        SelectButton(memory, modal, 1);
        ClearButtonSelection(memory, focusObject);

        True(reader.TryRead(out var observation), "modal after cleared parent focus");
        Equal("escape-root-modal", observation.SceneId, "cleared-parent modal scene");
        Equal("No", observation.Value, "cleared-parent modal choice");
        Equal("Exit game?", observation.ModalText, "cleared-parent modal warning");
    }

    private static void ReadsKeyboardAssignmentsFromTheNativeBindingObject()
    {
        var memory = new FakeNativeMemoryReader();
        var reader = new Steam2026NativeSystemMenuReader(ModuleBase, memory);
        var instance = 0x0000000350000000UL;
        var focusObject = 0x0000000350010000UL;
        WriteScene(
            memory,
            Steam2026NativeSystemMenuScene.Keyboard,
            instance,
            focusObject,
            focus: 1);
        memory.Write(instance + 0x3F0, BitConverter.GetBytes(82));
        memory.Write(instance + 0x428, BitConverter.GetBytes(26));
        reader.ObserveLifecycle(new Steam2026NativeSystemMenuLifecycleEvent(
            Steam2026NativeSystemMenuScene.Keyboard,
            instance,
            Opened: true,
            Generation: 25));

        True(reader.TryRead(out var moveUp), "keyboard move-up observation");
        Equal("move-up", moveUp.ControlId, "keyboard move-up control");
        Equal("Up Arrow", moveUp.PrimaryBinding, "keyboard primary assignment");
        Equal("W", moveUp.SecondaryBinding, "keyboard secondary assignment");

        SelectButton(memory, focusObject, 13);
        memory.Write(instance + 0x3D8, BitConverter.GetBytes(75));
        memory.Write(instance + 0x3DC, BitConverter.GetBytes(78));
        True(reader.TryRead(out var flee), "keyboard flee observation");
        Equal("flee-battle", flee.ControlId, "keyboard flee control");
        Equal(
            "Page Up plus Page Down",
            flee.PrimaryBinding,
            "keyboard flee chord");
        Equal<string?>(null, flee.SecondaryBinding, "keyboard flee secondary");

        SelectButton(memory, focusObject, 16);
        True(reader.TryRead(out var apply), "keyboard apply observation");
        Equal("apply", apply.ControlId, "keyboard apply focus");
    }

    private static void ReadsControllerAssignmentsForXboxAndPlayStationLayouts()
    {
        var memory = new FakeNativeMemoryReader();
        var reader = new Steam2026NativeSystemMenuReader(ModuleBase, memory);
        var instance = 0x0000000360000000UL;
        var focusObject = 0x0000000360010000UL;
        WriteScene(
            memory,
            Steam2026NativeSystemMenuScene.Controller,
            instance,
            focusObject,
            focus: 5);
        memory.Write(instance + 0x3A4, BitConverter.GetBytes(0));
        memory.Write(instance + 0x3AC, BitConverter.GetBytes(13));
        reader.ObserveLifecycle(new Steam2026NativeSystemMenuLifecycleEvent(
            Steam2026NativeSystemMenuScene.Controller,
            instance,
            Opened: true,
            Generation: 26));

        True(reader.TryRead(out var xboxConfirm), "Xbox confirm observation");
        Equal("confirm", xboxConfirm.ControlId, "Xbox confirm control");
        Equal("A", xboxConfirm.PrimaryBinding, "Xbox confirm assignment");

        memory.Write(instance + 0x3A4, BitConverter.GetBytes(1));
        SelectButton(memory, focusObject, 11);
        memory.Write(instance + 0x3F4, BitConverter.GetBytes(9));
        True(reader.TryRead(out var psRotate), "PlayStation rotate observation");
        Equal(
            "rotate-camera-left",
            psRotate.ControlId,
            "PlayStation rotate control");
        Equal("L1", psRotate.PrimaryBinding, "PlayStation rotate assignment");

        memory.Write(instance + 0x3A4, BitConverter.GetBytes(0));
        SelectButton(memory, focusObject, 13);
        memory.Write(instance + 0x3BC, BitConverter.GetBytes(9));
        memory.Write(instance + 0x3C0, BitConverter.GetBytes(10));
        True(reader.TryRead(out var xboxFlee), "Xbox flee observation");
        Equal("flee-battle", xboxFlee.ControlId, "Xbox flee control");
        Equal("LB plus RB", xboxFlee.PrimaryBinding, "Xbox flee chord");
    }

    private static void RejectsAStaleOrWrongSceneInstance()
    {
        var memory = new FakeNativeMemoryReader();
        var reader = new Steam2026NativeSystemMenuReader(ModuleBase, memory);
        var instance = WriteScene(
            memory,
            Steam2026NativeSystemMenuScene.Boosts,
            instance: 0x0000000400000000,
            focusObject: 0x0000000400010000,
            focus: 5);
        memory.Write(instance + 0x270, [1]);
        reader.ObserveLifecycle(new Steam2026NativeSystemMenuLifecycleEvent(
            Steam2026NativeSystemMenuScene.Boosts,
            instance,
            Opened: true,
            Generation: 30));

        True(reader.TryRead(out var valid), "valid boost instance");
        Equal("speed-boost", valid.ControlId, "boost focus");
        Equal("On", valid.Value, "boost value");

        memory.Write(instance, BitConverter.GetBytes(ModuleBase + 0x1234));
        False(reader.TryRead(out _), "wrong vtable rejected");
    }

    private static ulong WriteScene(
        FakeNativeMemoryReader memory,
        Steam2026NativeSystemMenuScene scene,
        ulong instance,
        ulong focusObject,
        int focus)
    {
        var definition = Steam2026NativeSystemMenuDefinitions.Get(scene);
        memory.Write(
            instance,
            BitConverter.GetBytes(ModuleBase + definition.VtableRva));
        memory.Write(
            instance + definition.FocusObjectOffset,
            BitConverter.GetBytes(focusObject));
        var controls = Enumerable.Range(0, GetButtonCount(scene))
            .Select(index => focusObject + 0x2000 + ((ulong)index * 0x1000))
            .ToArray();
        WriteButtonVector(memory, focusObject, focusObject + 0x1000, controls);
        SelectButton(memory, focusObject, focus);
        return instance;
    }

    private static int GetButtonCount(Steam2026NativeSystemMenuScene scene) =>
        scene switch
        {
            Steam2026NativeSystemMenuScene.EscapeRoot => 4,
            Steam2026NativeSystemMenuScene.GameOptions => 4,
            Steam2026NativeSystemMenuScene.Controls => 3,
            Steam2026NativeSystemMenuScene.Autosave => 5,
            Steam2026NativeSystemMenuScene.Boosts => 9,
            Steam2026NativeSystemMenuScene.System => 10,
            Steam2026NativeSystemMenuScene.Keyboard
                or Steam2026NativeSystemMenuScene.Controller => 19,
            _ => throw new ArgumentOutOfRangeException(nameof(scene))
        };

    private static void SelectButton(
        FakeNativeMemoryReader memory,
        ulong container,
        int selectedIndex)
    {
        memory.Write(container + 0xE8, BitConverter.GetBytes(selectedIndex));
        True(memory.TryReadUInt64(container + 0x38, out var begin), "button vector begin");
        True(memory.TryReadUInt64(container + 0x40, out var end), "button vector end");
        var count = checked((int)((end - begin) / 0x10));
        for (var index = 0; index < count; index++)
        {
            True(
                memory.TryReadUInt64(begin + ((ulong)index * 0x10), out var control),
                "button vector control");
            memory.Write(
                control + 0xD8,
                BitConverter.GetBytes(index == selectedIndex ? 2 : 0));
        }
    }

    private static void ClearButtonSelection(
        FakeNativeMemoryReader memory,
        ulong container)
    {
        True(memory.TryReadUInt64(container + 0x38, out var begin), "button vector begin");
        True(memory.TryReadUInt64(container + 0x40, out var end), "button vector end");
        var count = checked((int)((end - begin) / 0x10));
        for (var index = 0; index < count; index++)
        {
            True(
                memory.TryReadUInt64(begin + ((ulong)index * 0x10), out var control),
                "button vector control");
            memory.Write(control + 0xD8, BitConverter.GetBytes(0));
        }
    }

    private static void WriteButtonVector(
        FakeNativeMemoryReader memory,
        ulong container,
        ulong begin,
        IReadOnlyList<ulong> controls)
    {
        memory.Write(container + 0x38, BitConverter.GetBytes(begin));
        memory.Write(
            container + 0x40,
            BitConverter.GetBytes(begin + ((ulong)controls.Count * 0x10)));
        memory.Write(
            container + 0x48,
            BitConverter.GetBytes(begin + ((ulong)controls.Count * 0x10)));
        for (var index = 0; index < controls.Count; index++)
        {
            var entry = begin + ((ulong)index * 0x10);
            memory.Write(entry, BitConverter.GetBytes(controls[index]));
            memory.Write(entry + 0x08, BitConverter.GetBytes(0UL));
            WriteButtonObject(memory, controls[index]);
        }
    }

    private static void WriteButtonObject(
        FakeNativeMemoryReader memory,
        ulong address)
    {
        WriteRttiHierarchy(
            memory,
            TestButtonVtable,
            TestButtonCompleteObjectLocator,
            hierarchyRva: 0x01810200,
            baseArrayRva: 0x01810300,
            derivedDescriptorRva: 0x01810400,
            buttonDescriptorRva: 0x01810500,
            nodeDescriptorRva: 0x01810600,
            includesButton: true);
        memory.Write(address, BitConverter.GetBytes(TestButtonVtable));
    }

    private static void WriteNonButtonObject(
        FakeNativeMemoryReader memory,
        ulong address)
    {
        WriteRttiHierarchy(
            memory,
            TestNonButtonVtable,
            TestNonButtonCompleteObjectLocator,
            hierarchyRva: 0x01811200,
            baseArrayRva: 0x01811300,
            derivedDescriptorRva: 0x01811400,
            buttonDescriptorRva: 0,
            nodeDescriptorRva: 0x01811600,
            includesButton: false);
        memory.Write(address, BitConverter.GetBytes(TestNonButtonVtable));
    }

    private static void WriteRttiHierarchy(
        FakeNativeMemoryReader memory,
        ulong vtable,
        ulong completeObjectLocator,
        uint hierarchyRva,
        uint baseArrayRva,
        uint derivedDescriptorRva,
        uint buttonDescriptorRva,
        uint nodeDescriptorRva,
        bool includesButton)
    {
        memory.Write(vtable - sizeof(ulong), BitConverter.GetBytes(completeObjectLocator));
        WriteUInt32(memory, completeObjectLocator + 0x00, 1);
        WriteUInt32(memory, completeObjectLocator + 0x04, 0);
        WriteUInt32(memory, completeObjectLocator + 0x08, 0);
        WriteUInt32(memory, completeObjectLocator + 0x0C, 0x01810100);
        WriteUInt32(memory, completeObjectLocator + 0x10, hierarchyRva);
        WriteUInt32(
            memory,
            completeObjectLocator + 0x14,
            checked((uint)(completeObjectLocator - ModuleBase)));

        var hierarchy = ModuleBase + hierarchyRva;
        WriteUInt32(memory, hierarchy + 0x00, 0);
        WriteUInt32(memory, hierarchy + 0x04, 1);
        WriteUInt32(memory, hierarchy + 0x08, includesButton ? 3U : 2U);
        WriteUInt32(memory, hierarchy + 0x0C, baseArrayRva);

        var baseArray = ModuleBase + baseArrayRva;
        WriteUInt32(memory, baseArray, derivedDescriptorRva);
        if (includesButton)
        {
            WriteUInt32(memory, baseArray + 0x04, buttonDescriptorRva);
            WriteUInt32(memory, baseArray + 0x08, nodeDescriptorRva);
        }
        else
        {
            WriteUInt32(memory, baseArray + 0x04, nodeDescriptorRva);
        }

        WriteBaseClassDescriptor(
            memory,
            ModuleBase + derivedDescriptorRva,
            typeDescriptorRva: 0x01810100,
            hierarchyRva);
        if (includesButton)
        {
            WriteBaseClassDescriptor(
                memory,
                ModuleBase + buttonDescriptorRva,
                typeDescriptorRva: 0x017320E0,
                hierarchyRva);
        }
        WriteBaseClassDescriptor(
            memory,
            ModuleBase + nodeDescriptorRva,
            typeDescriptorRva: 0x01732070,
            hierarchyRva);
    }

    private static void WriteBaseClassDescriptor(
        FakeNativeMemoryReader memory,
        ulong address,
        uint typeDescriptorRva,
        uint hierarchyRva)
    {
        WriteUInt32(memory, address + 0x00, typeDescriptorRva);
        WriteUInt32(memory, address + 0x04, 0);
        memory.Write(address + 0x08, BitConverter.GetBytes(0));
        memory.Write(address + 0x0C, BitConverter.GetBytes(-1));
        memory.Write(address + 0x10, BitConverter.GetBytes(0));
        WriteUInt32(memory, address + 0x14, 0x40);
        WriteUInt32(memory, address + 0x18, hierarchyRva);
    }

    private static void WriteUInt32(
        FakeNativeMemoryReader memory,
        ulong address,
        uint value) =>
        memory.Write(address, BitConverter.GetBytes(value));

    private static void True(bool actual, string label) =>
        Equal(true, actual, label);

    private static void False(bool actual, string label) =>
        Equal(false, actual, label);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected {expected}, got {actual}.");
        }
    }
}
