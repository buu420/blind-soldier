using System.Buffers.Binary;

namespace Ff7.Accessibility.Steam2026X64.Runtime.SystemMenu;

/// <summary>
/// Discovers the active scene through the native MUI settings manager, then
/// reads its focused control and value. Every pointer and scalar used for
/// speech is read twice and rejected if it changes during the observation.
/// </summary>
internal sealed class Steam2026NativeSystemMenuReader
{
    private const ulong ButtonVectorBeginOffset = 0x38;
    private const ulong ButtonVectorEndOffset = 0x40;
    private const ulong ButtonVectorEntrySize = 0x10;
    private const ulong ButtonStateOffset = 0xD8;
    private const int SelectedButtonState = 2;
    private const ulong NavigationIndexOffset = 0xE8;
    private const ulong RootAndGameOptionsBackControlOffset = 0x1A0;
    private const ulong ControlsBackControlOffset = 0x190;
    private const int MaximumButtonCount = 64;
    private const ulong SupportedModuleImageSize = 0x0212A000;
    private const uint NodeTypeDescriptorRva = 0x01732070;
    private const uint ButtonTypeDescriptorRva = 0x017320E0;
    private const ulong RttiCompleteObjectLocatorSize = 0x18;
    private const ulong RttiClassHierarchySize = 0x10;
    private const ulong RttiBaseClassDescriptorSize = 0x1C;
    private const ulong ModalActiveOffset = 0xA0;
    private const ulong ManagerPointerOffset = 0x50;
    private const ulong CurrentSceneOffset = 0x38;
    private const ulong PendingSceneOffset = 0x48;
    private const ulong SceneActiveOffset = 0xA0;
    private const ulong ControllerLayoutOffset = 0x3A4;
    private const ulong ControllerLayoutStride = 0x38;
    private const int MaximumResolutionCount = 128;

    private static readonly string[] BindingControlIds =
    [
        "move-up",
        "move-down",
        "move-left",
        "move-right",
        "confirm",
        "cancel-run",
        "menu",
        "switch",
        "pause",
        "toggle-map",
        "rotate-camera-left",
        "rotate-camera-right",
        "flee-battle",
        "change-pov",
        "target"
    ];

    private static readonly ulong[] AutosaveControlOffsets =
    [
        0x210,
        0x220,
        0x230
    ];

    private static readonly ulong[] AutosaveModalControlOffsets =
    [
        0x250,
        0x260
    ];

    private readonly ulong moduleBase;
    private readonly INativeMemoryReader memory;
    private readonly List<ActiveScene> activeScenes = [];
    private readonly Dictionary<ulong, ButtonCastInfo> buttonCasts = [];
    private long nextDerivedGeneration;

    internal Steam2026NativeSystemMenuReader(
        ulong moduleBase,
        INativeMemoryReader memory)
    {
        if (moduleBase == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(moduleBase));
        }

        this.moduleBase = moduleBase;
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    internal string LastDiagnostic { get; private set; } =
        "No native Escape-menu scene has been observed.";

    internal int ActiveSceneCount => activeScenes.Count;

    internal bool ObserveManagerHost(ulong host)
    {
        if (host == 0
            || !TryAdd(host, ManagerPointerOffset, out var managerAddress)
            || !TryReadUInt64Stable(managerAddress, out var manager)
            || manager == 0)
        {
            ClearManagerScene(
                "The native MUI settings-manager pointer was unavailable or unstable.");
            return false;
        }

        if ((!TryReadActiveManagerScene(
                 manager,
                 PendingSceneOffset,
                 out var definition,
                 out var instance)
             && !TryReadActiveManagerScene(
                 manager,
                 CurrentSceneOffset,
                 out definition,
                 out instance))
            || !TryReadUInt64Stable(managerAddress, out var managerAfter)
            || manager != managerAfter)
        {
            ClearManagerScene(
                "No supported active native Escape-menu scene is present.");
            return false;
        }

        if (activeScenes.Count == 1
            && activeScenes[0].Definition.Scene == definition.Scene
            && activeScenes[0].Instance == instance)
        {
            LastDiagnostic =
                $"Observed active {definition.Scene} MUI scene at 0x{instance:X}.";
            return true;
        }

        activeScenes.Clear();
        activeScenes.Add(new ActiveScene(
            definition,
            instance,
            ++nextDerivedGeneration));
        LastDiagnostic =
            $"Discovered active {definition.Scene} MUI scene at 0x{instance:X}.";
        return true;
    }

    internal void ObserveLifecycle(
        Steam2026NativeSystemMenuLifecycleEvent lifecycleEvent)
    {
        if (lifecycleEvent.Instance == 0)
        {
            return;
        }

        nextDerivedGeneration = Math.Max(
            nextDerivedGeneration,
            lifecycleEvent.Generation);
        if (lifecycleEvent.Opened)
        {
            Remove(lifecycleEvent.Scene, lifecycleEvent.Instance);
            activeScenes.Add(new ActiveScene(
                Steam2026NativeSystemMenuDefinitions.Get(lifecycleEvent.Scene),
                lifecycleEvent.Instance,
                lifecycleEvent.Generation));
            LastDiagnostic =
                $"Observed {lifecycleEvent.Scene} native scene at 0x{lifecycleEvent.Instance:X}.";
            return;
        }

        Remove(lifecycleEvent.Scene, lifecycleEvent.Instance);
        LastDiagnostic =
            $"Observed {lifecycleEvent.Scene} native scene close at 0x{lifecycleEvent.Instance:X}.";
    }

    internal bool TryRead(out Steam2026SystemMenuObservation observation)
    {
        observation = null!;
        if (activeScenes.Count == 0)
        {
            LastDiagnostic = "No native Escape-menu scene is active.";
            return false;
        }

        var active = activeScenes[^1];
        if (!TryValidateInstance(active, out var diagnostic))
        {
            LastDiagnostic = diagnostic;
            return false;
        }

        var focusAvailable = TryReadFocus(active, out var focus);
        if (focusAvailable)
        {
            active.LastFocus = focus;
        }

        if (TryReadModal(
                active,
                focusAvailable ? focus : active.LastFocus ?? -1,
                out observation))
        {
            LastDiagnostic =
                $"{active.Definition.Scene} confirmation focus {observation.Value}.";
            return true;
        }

        active.ModalWasActive = false;
        if (!focusAvailable)
        {
            LastDiagnostic =
                $"{active.Definition.Scene} focus state was unavailable or unstable.";
            return false;
        }

        if (!TryMapControl(active.Definition.Scene, focus, out var controlId))
        {
            LastDiagnostic =
                $"{active.Definition.Scene} exposed unmapped native focus {focus}.";
            return false;
        }

        if (!TryCreateObservation(active, controlId, out observation))
        {
            LastDiagnostic =
                $"{active.Definition.Scene} value for {controlId} was unavailable or unstable.";
            return false;
        }

        LastDiagnostic =
            $"{active.Definition.Scene} focus {focus}: {controlId}.";
        return true;
    }

    internal void Reset()
    {
        activeScenes.Clear();
        buttonCasts.Clear();
        nextDerivedGeneration = 0;
        LastDiagnostic = "Native Escape-menu reader reset.";
    }

    private bool TryValidateInstance(ActiveScene active, out string diagnostic)
    {
        diagnostic = string.Empty;
        if (!TryReadUInt64Stable(active.Instance, out var vtable)
            || vtable != moduleBase + active.Definition.VtableRva)
        {
            diagnostic =
                $"{active.Definition.Scene} instance no longer has its exact native vtable.";
            return false;
        }

        return true;
    }

    private bool TryReadActiveManagerScene(
        ulong manager,
        ulong sceneOffset,
        out Steam2026NativeSystemMenuDefinition definition,
        out ulong instance)
    {
        definition = null!;
        instance = 0;
        if (!TryAdd(manager, sceneOffset, out var sceneAddress)
            || !TryReadUInt64Stable(sceneAddress, out instance)
            || instance == 0
            || !TryReadUInt64Stable(instance, out var vtable)
            || !TryFindDefinition(vtable, out definition)
            || !TryAdd(instance, SceneActiveOffset, out var activeAddress)
            || !TryReadByteStable(activeAddress, out var active)
            || active == 0
            || !TryReadUInt64Stable(sceneAddress, out var instanceAfter)
            || instance != instanceAfter)
        {
            definition = null!;
            instance = 0;
            return false;
        }

        return true;
    }

    private bool TryFindDefinition(
        ulong vtable,
        out Steam2026NativeSystemMenuDefinition definition)
    {
        foreach (var candidate in Steam2026NativeSystemMenuDefinitions.All)
        {
            if (TryAdd(moduleBase, candidate.VtableRva, out var expected)
                && vtable == expected)
            {
                definition = candidate;
                return true;
            }
        }

        definition = null!;
        return false;
    }

    private void ClearManagerScene(string diagnostic)
    {
        activeScenes.Clear();
        LastDiagnostic = diagnostic;
    }

    private bool TryReadFocus(ActiveScene active, out int focus)
    {
        focus = 0;
        if (TryGetDedicatedBackControl(
                active.Definition.Scene,
                out var backControlOffset,
                out var backFocus)
            && TryReadDirectControlState(
                active.Instance,
                backControlOffset,
                out var backState)
            && backState == SelectedButtonState)
        {
            focus = backFocus;
            return true;
        }

        if (active.Definition.Scene == Steam2026NativeSystemMenuScene.Autosave
            && TryReadSelectedDirectControlIndex(
                active.Instance,
                AutosaveControlOffsets,
                out var autosaveControl))
        {
            focus = autosaveControl + 1;
            return true;
        }

        if (!TryAdd(
                active.Instance,
                active.Definition.FocusObjectOffset,
                out var pointerAddress)
            || !TryReadUInt64Stable(pointerAddress, out var focusObject)
            || focusObject == 0)
        {
            return false;
        }

        var readFocus = UsesNativeNavigationIndex(active.Definition.Scene)
            ? TryAdd(focusObject, NavigationIndexOffset, out var indexAddress)
              && TryReadInt32Stable(indexAddress, out focus)
            : TryReadSelectedButtonIndex(focusObject, out focus);
        if (!readFocus
            || !TryReadUInt64Stable(pointerAddress, out var focusObjectAfter)
            || focusObject != focusObjectAfter)
        {
            focus = 0;
            return false;
        }

        return focus is >= 0 and <= 64;
    }

    private static bool TryGetDedicatedBackControl(
        Steam2026NativeSystemMenuScene scene,
        out ulong controlOffset,
        out int focus)
    {
        (controlOffset, focus) = scene switch
        {
            Steam2026NativeSystemMenuScene.EscapeRoot =>
                (RootAndGameOptionsBackControlOffset, 3),
            Steam2026NativeSystemMenuScene.GameOptions =>
                (RootAndGameOptionsBackControlOffset, 3),
            Steam2026NativeSystemMenuScene.Controls =>
                (ControlsBackControlOffset, 2),
            _ => (0UL, -1)
        };
        return controlOffset != 0;
    }

    private bool TryReadSelectedDirectControlIndex(
        ulong instance,
        IReadOnlyList<ulong> controlOffsets,
        out int selectedIndex)
    {
        selectedIndex = -1;
        for (var index = 0; index < controlOffsets.Count; index++)
        {
            if (!TryReadDirectControlState(
                    instance,
                    controlOffsets[index],
                    out var state))
            {
                selectedIndex = -1;
                return false;
            }

            if (state != SelectedButtonState)
            {
                continue;
            }

            if (selectedIndex >= 0)
            {
                selectedIndex = -1;
                return false;
            }

            selectedIndex = index;
        }

        return selectedIndex >= 0;
    }

    private bool TryReadDirectControlState(
        ulong instance,
        ulong controlOffset,
        out int state)
    {
        state = 0;
        if (!TryAdd(instance, controlOffset, out var pointerAddress)
            || !TryReadUInt64Stable(pointerAddress, out var node)
            || node == 0
            || !TryResolveButton(node, out var isButton, out var button)
            || !isButton
            || !TryAdd(button, ButtonStateOffset, out var stateAddress)
            || !TryReadInt32Stable(stateAddress, out state)
            || !TryReadUInt64Stable(pointerAddress, out var nodeAfter)
            || node != nodeAfter)
        {
            state = 0;
            return false;
        }

        return true;
    }

    private static bool UsesNativeNavigationIndex(
        Steam2026NativeSystemMenuScene scene) =>
        scene is Steam2026NativeSystemMenuScene.Autosave
            or Steam2026NativeSystemMenuScene.Boosts
            or Steam2026NativeSystemMenuScene.Keyboard
            or Steam2026NativeSystemMenuScene.Controller
            or Steam2026NativeSystemMenuScene.System;

    private bool TryReadSelectedButtonIndex(
        ulong container,
        out int selectedIndex)
    {
        selectedIndex = -1;
        if (!TryAdd(container, ButtonVectorBeginOffset, out var beginAddress)
            || !TryAdd(container, ButtonVectorEndOffset, out var endAddress)
            || !TryReadUInt64Stable(beginAddress, out var begin)
            || !TryReadUInt64Stable(endAddress, out var end)
            || begin == 0
            || end < begin)
        {
            return false;
        }

        var byteLength = end - begin;
        if (byteLength == 0
            || byteLength % ButtonVectorEntrySize != 0
            || byteLength / ButtonVectorEntrySize > MaximumButtonCount)
        {
            return false;
        }

        var count = (int)(byteLength / ButtonVectorEntrySize);
        var buttonIndex = 0;
        for (var index = 0; index < count; index++)
        {
            if (!TryAdd(
                    begin,
                    (ulong)index * ButtonVectorEntrySize,
                    out var entryAddress)
                || !TryReadUInt64Stable(entryAddress, out var node)
                || node == 0
                || !TryResolveButton(node, out var isButton, out var button))
            {
                selectedIndex = -1;
                return false;
            }

            if (!isButton)
            {
                continue;
            }

            if (!TryAdd(button, ButtonStateOffset, out var stateAddress)
                || !TryReadInt32Stable(stateAddress, out var state))
            {
                selectedIndex = -1;
                return false;
            }

            var currentButtonIndex = buttonIndex++;
            if (state != SelectedButtonState)
            {
                continue;
            }

            if (selectedIndex >= 0)
            {
                selectedIndex = -1;
                return false;
            }

            selectedIndex = currentButtonIndex;
        }

        return selectedIndex >= 0
            && TryReadUInt64Stable(beginAddress, out var beginAfter)
            && TryReadUInt64Stable(endAddress, out var endAfter)
            && begin == beginAfter
            && end == endAfter;
    }

    private bool TryResolveButton(
        ulong node,
        out bool isButton,
        out ulong button)
    {
        isButton = false;
        button = 0;
        if (!TryReadUInt64Stable(node, out var vtable)
            || !IsSupportedModuleRange(vtable, 1))
        {
            return false;
        }

        if (!buttonCasts.TryGetValue(vtable, out var castInfo))
        {
            if (!TryReadButtonCastInfo(vtable, out castInfo))
            {
                return false;
            }
            buttonCasts.Add(vtable, castInfo);
        }

        if (node < castInfo.CompleteObjectOffset)
        {
            return false;
        }

        var completeObject = node - castInfo.CompleteObjectOffset;
        isButton = castInfo.IsButton;
        return !isButton
            || TryAdd(completeObject, castInfo.ButtonOffset, out button);
    }

    private bool TryReadButtonCastInfo(
        ulong vtable,
        out ButtonCastInfo castInfo)
    {
        castInfo = default;
        if (vtable < sizeof(ulong)
            || !TryReadUInt64Stable(
                vtable - sizeof(ulong),
                out var completeObjectLocator)
            || !IsSupportedModuleRange(
                completeObjectLocator,
                RttiCompleteObjectLocatorSize)
            || !TryReadUInt32Stable(
                completeObjectLocator,
                out var signature)
            || signature != 1
            || !TryReadInt32Stable(
                completeObjectLocator + 0x04,
                out var completeObjectOffset)
            || completeObjectOffset < 0
            || !TryReadUInt32Stable(
                completeObjectLocator + 0x0C,
                out var completeTypeRva)
            || !TryGetSupportedModuleAddress(
                completeTypeRva,
                1,
                out _)
            || !TryReadUInt32Stable(
                completeObjectLocator + 0x10,
                out var hierarchyRva)
            || !TryGetSupportedModuleAddress(
                hierarchyRva,
                RttiClassHierarchySize,
                out var hierarchy)
            || !TryReadUInt32Stable(
                completeObjectLocator + 0x14,
                out var selfRva)
            || !TryGetSupportedModuleAddress(
                selfRva,
                RttiCompleteObjectLocatorSize,
                out var self)
            || self != completeObjectLocator
            || !TryReadUInt32Stable(hierarchy, out var hierarchySignature)
            || hierarchySignature != 0
            || !TryReadUInt32Stable(
                hierarchy + 0x08,
                out var baseClassCount)
            || baseClassCount is 0 or > MaximumButtonCount
            || !TryReadUInt32Stable(
                hierarchy + 0x0C,
                out var baseClassArrayRva)
            || !TryGetSupportedModuleAddress(
                baseClassArrayRva,
                (ulong)baseClassCount * sizeof(uint),
                out var baseClassArray))
        {
            return false;
        }

        var nodeMatched = false;
        var buttonMatched = false;
        var buttonOffset = 0;
        for (var index = 0U; index < baseClassCount; index++)
        {
            if (!TryReadUInt32Stable(
                    baseClassArray + ((ulong)index * sizeof(uint)),
                    out var descriptorRva)
                || !TryGetSupportedModuleAddress(
                    descriptorRva,
                    RttiBaseClassDescriptorSize,
                    out var descriptor)
                || !TryReadUInt32Stable(
                    descriptor,
                    out var typeDescriptorRva)
                || !TryReadInt32Stable(
                    descriptor + 0x08,
                    out var memberOffset)
                || !TryReadInt32Stable(
                    descriptor + 0x0C,
                    out var vbtableOffset))
            {
                return false;
            }

            if (typeDescriptorRva == NodeTypeDescriptorRva
                && vbtableOffset == -1
                && memberOffset == completeObjectOffset)
            {
                nodeMatched = true;
            }

            if (typeDescriptorRva != ButtonTypeDescriptorRva)
            {
                continue;
            }

            if (buttonMatched || vbtableOffset != -1 || memberOffset < 0)
            {
                return false;
            }

            buttonMatched = true;
            buttonOffset = memberOffset;
        }

        if (!nodeMatched)
        {
            return false;
        }

        castInfo = new ButtonCastInfo(
            buttonMatched,
            (uint)completeObjectOffset,
            (uint)buttonOffset);
        return true;
    }

    private bool TryGetSupportedModuleAddress(
        uint rva,
        ulong size,
        out ulong address)
    {
        address = 0;
        if ((ulong)rva > SupportedModuleImageSize
            || size > SupportedModuleImageSize - (ulong)rva
            || !TryAdd(moduleBase, rva, out address))
        {
            address = 0;
            return false;
        }
        return true;
    }

    private bool IsSupportedModuleRange(ulong address, ulong size) =>
        address >= moduleBase
        && address - moduleBase <= SupportedModuleImageSize
        && size <= SupportedModuleImageSize - (address - moduleBase);

    private bool TryReadModal(
        ActiveScene active,
        int parentFocus,
        out Steam2026SystemMenuObservation observation)
    {
        observation = null!;
        if (active.Definition.ModalObjectOffset == 0)
        {
            return false;
        }

        if (!TryAdd(
                active.Instance,
                active.Definition.ModalObjectOffset,
                out var pointerAddress)
            || !TryReadUInt64Stable(pointerAddress, out var modal)
            || modal == 0
            || !TryAdd(modal, ModalActiveOffset, out var activeAddress)
            || !TryReadByteStable(activeAddress, out var modalActive)
            || modalActive == 0
            || !TryReadUInt64Stable(pointerAddress, out var modalAfter)
            || modal != modalAfter)
        {
            return false;
        }

        var choice = -1;
        var choiceAvailable =
            active.Definition.Scene == Steam2026NativeSystemMenuScene.Autosave
            && TryReadSelectedDirectControlIndex(
                active.Instance,
                AutosaveModalControlOffsets,
                out choice);
        if (!choiceAvailable
            && !TryReadSelectedButtonIndex(modal, out choice))
        {
            return false;
        }

        var choiceText = choice switch
        {
            0 => "Yes",
            1 => "No",
            _ => null
        };
        if (choiceText is null)
        {
            return false;
        }

        if (!active.ModalWasActive)
        {
            active.ModalWasActive = true;
            active.ModalGeneration = ++nextDerivedGeneration;
        }

        observation = new Steam2026SystemMenuObservation(
            active.Definition.SceneId + "-modal",
            "confirm-choice",
            choiceText,
            choice + 1,
            Count: 2,
            PrimaryBinding: null,
            SecondaryBinding: null,
            ModalText: GetModalText(active.Definition.Scene, parentFocus),
            IsFocused: true,
            active.ModalGeneration);
        return true;
    }

    private bool TryCreateObservation(
        ActiveScene active,
        string controlId,
        out Steam2026SystemMenuObservation observation)
    {
        string? value = null;
        string? primaryBinding = null;
        string? secondaryBinding = null;
        var position = 0;
        var count = 0;

        switch (active.Definition.Scene)
        {
            case Steam2026NativeSystemMenuScene.Autosave
                when controlId == "autosave":
                if (!TryReadToggle(active.Instance + 0x270, out value))
                {
                    observation = null!;
                    return false;
                }
                break;

            case Steam2026NativeSystemMenuScene.Boosts:
                var boostOffset = controlId switch
                {
                    "battle-assist" => 0x271UL,
                    "no-encounters" => 0x272UL,
                    "speed-boost" => 0x270UL,
                    _ => 0UL
                };
                if (boostOffset != 0
                    && !TryReadToggle(active.Instance + boostOffset, out value))
                {
                    observation = null!;
                    return false;
                }
                break;

            case Steam2026NativeSystemMenuScene.System:
                if (!TryReadSystemValue(
                        active.Instance,
                        controlId,
                        out value,
                        out position,
                        out count))
                {
                    observation = null!;
                    return false;
                }
                break;

            case Steam2026NativeSystemMenuScene.Keyboard:
                if (!TryReadKeyboardBinding(
                        active.Instance,
                        controlId,
                        out primaryBinding,
                        out secondaryBinding))
                {
                    observation = null!;
                    return false;
                }
                break;

            case Steam2026NativeSystemMenuScene.Controller:
                if (!TryReadControllerBinding(
                        active.Instance,
                        controlId,
                        out primaryBinding))
                {
                    observation = null!;
                    return false;
                }
                break;
        }

        observation = new Steam2026SystemMenuObservation(
            active.Definition.SceneId,
            controlId,
            value,
            position,
            count,
            primaryBinding,
            secondaryBinding,
            ModalText: null,
            IsFocused: true,
            active.Generation);
        return true;
    }

    private bool TryReadKeyboardBinding(
        ulong instance,
        string controlId,
        out string? primary,
        out string? secondary)
    {
        primary = null;
        secondary = null;
        if (controlId is "apply" or "default" or "back")
        {
            return true;
        }

        if (controlId == "flee-battle")
        {
            return TryReadKeyboardChord(
                instance + 0x3D8,
                instance + 0x3DC,
                out primary);
        }

        if (!TryGetKeyboardBindingOffsets(
                controlId,
                out var primaryOffset,
                out var secondaryOffset)
            || !TryReadInt32Stable(instance + primaryOffset, out var primaryCode)
            || !TryFormatKeyboardScancode(
                primaryCode,
                optional: false,
                out primary))
        {
            return false;
        }

        if (secondaryOffset == 0)
        {
            return true;
        }

        return TryReadInt32Stable(
                instance + secondaryOffset,
                out var secondaryCode)
            && TryFormatKeyboardScancode(
                secondaryCode,
                optional: true,
                out secondary);
    }

    private bool TryReadKeyboardChord(
        ulong leftAddress,
        ulong rightAddress,
        out string? chord)
    {
        chord = null;
        if (!TryReadInt32Stable(leftAddress, out var leftCode)
            || !TryReadInt32Stable(rightAddress, out var rightCode)
            || !TryFormatKeyboardScancode(
                leftCode,
                optional: false,
                out var left)
            || !TryFormatKeyboardScancode(
                rightCode,
                optional: false,
                out var right))
        {
            return false;
        }

        chord = left == "Unassigned" || right == "Unassigned"
            ? "Unassigned"
            : $"{left} plus {right}";
        return true;
    }

    private bool TryReadControllerBinding(
        ulong instance,
        string controlId,
        out string? primary)
    {
        primary = null;
        if (controlId is "apply" or "default" or "back")
        {
            return true;
        }

        if (!TryReadInt32Stable(
                instance + ControllerLayoutOffset,
                out var layout)
            || layout is < 0 or > 1)
        {
            return false;
        }

        var layoutOffset = checked(
            (ulong)layout * ControllerLayoutStride);
        if (controlId == "flee-battle")
        {
            return TryReadControllerChord(
                instance + 0x3BC + layoutOffset,
                instance + 0x3C0 + layoutOffset,
                playStationLayout: layout == 1,
                out primary);
        }

        if (!TryGetControllerBindingOffset(
                controlId,
                out var bindingOffset)
            || !TryReadInt32Stable(
                instance + bindingOffset + layoutOffset,
                out var buttonCode))
        {
            return false;
        }

        return TryFormatControllerButton(
            buttonCode,
            playStationLayout: layout == 1,
            out primary);
    }

    private bool TryReadControllerChord(
        ulong leftAddress,
        ulong rightAddress,
        bool playStationLayout,
        out string? chord)
    {
        chord = null;
        if (!TryReadInt32Stable(leftAddress, out var leftCode)
            || !TryReadInt32Stable(rightAddress, out var rightCode)
            || !TryFormatControllerButton(
                leftCode,
                playStationLayout,
                out var left)
            || !TryFormatControllerButton(
                rightCode,
                playStationLayout,
                out var right))
        {
            return false;
        }

        chord = left == "Unassigned" || right == "Unassigned"
            ? "Unassigned"
            : $"{left} plus {right}";
        return true;
    }

    private static bool TryGetKeyboardBindingOffsets(
        string controlId,
        out ulong primary,
        out ulong secondary)
    {
        (primary, secondary) = controlId switch
        {
            "confirm" => (0x3C8UL, 0x400UL),
            "cancel-run" => (0x3CCUL, 0x404UL),
            "switch" => (0x3D0UL, 0x408UL),
            "menu" => (0x3D4UL, 0x40CUL),
            "rotate-camera-left" => (0x3D8UL, 0x410UL),
            "rotate-camera-right" => (0x3DCUL, 0x414UL),
            "change-pov" => (0x3E0UL, 0x418UL),
            "target" => (0x3E4UL, 0x41CUL),
            "toggle-map" => (0x420UL, 0UL),
            "pause" => (0x3ECUL, 0x424UL),
            "move-up" => (0x3F0UL, 0x428UL),
            "move-down" => (0x3F4UL, 0x42CUL),
            "move-left" => (0x3F8UL, 0x430UL),
            "move-right" => (0x3FCUL, 0x434UL),
            _ => (0UL, 0UL)
        };
        return primary != 0;
    }

    private static bool TryGetControllerBindingOffset(
        string controlId,
        out ulong offset)
    {
        offset = controlId switch
        {
            "confirm" => 0x3ACUL,
            "cancel-run" => 0x3B0UL,
            "switch" => 0x3B4UL,
            "menu" => 0x3B8UL,
            "rotate-camera-left" => 0x3BCUL,
            "rotate-camera-right" => 0x3C0UL,
            "change-pov" => 0x3C4UL,
            "target" => 0x3C8UL,
            "toggle-map" => 0x3CCUL,
            "pause" => 0x3D0UL,
            "move-up" => 0x3D4UL,
            "move-down" => 0x3D8UL,
            "move-left" => 0x3DCUL,
            "move-right" => 0x3E0UL,
            _ => 0UL
        };
        return offset != 0;
    }

    private static bool TryFormatKeyboardScancode(
        int code,
        bool optional,
        out string? name)
    {
        name = null;
        if (code == -1)
        {
            name = optional ? null : "Unassigned";
            return true;
        }

        if (code == 0)
        {
            name = "Unassigned";
            return true;
        }

        if (code is < 0 or > 512)
        {
            return false;
        }

        if (code is >= 4 and <= 29)
        {
            name = ((char)('A' + code - 4)).ToString();
            return true;
        }

        if (code is >= 30 and <= 38)
        {
            name = (code - 29).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        if (code is >= 58 and <= 69)
        {
            name = $"F{code - 57}";
            return true;
        }

        if (code is >= 89 and <= 97)
        {
            name = $"Numpad {code - 88}";
            return true;
        }

        if (code is >= 104 and <= 115)
        {
            name = $"F{code - 91}";
            return true;
        }

        name = code switch
        {
            39 => "0",
            40 => "Enter",
            41 => "Escape",
            42 => "Backspace",
            43 => "Tab",
            44 => "Space",
            45 => "Minus",
            46 => "Equals",
            47 => "Left Bracket",
            48 => "Right Bracket",
            49 => "Backslash",
            50 => "Non-US Hash",
            51 => "Semicolon",
            52 => "Apostrophe",
            53 => "Grave",
            54 => "Comma",
            55 => "Period",
            56 => "Slash",
            57 => "Caps Lock",
            70 => "Print Screen",
            71 => "Scroll Lock",
            72 => "Pause",
            73 => "Insert",
            74 => "Home",
            75 => "Page Up",
            76 => "Delete",
            77 => "End",
            78 => "Page Down",
            79 => "Right Arrow",
            80 => "Left Arrow",
            81 => "Down Arrow",
            82 => "Up Arrow",
            83 => "Num Lock",
            84 => "Numpad Divide",
            85 => "Numpad Multiply",
            86 => "Numpad Minus",
            87 => "Numpad Plus",
            88 => "Numpad Enter",
            98 => "Numpad 0",
            99 => "Numpad Period",
            100 => "Non-US Backslash",
            101 => "Application",
            102 => "Power",
            103 => "Numpad Equals",
            116 => "Execute",
            117 => "Help",
            118 => "Menu",
            119 => "Select",
            120 => "Stop",
            121 => "Again",
            122 => "Undo",
            123 => "Cut",
            124 => "Copy",
            125 => "Paste",
            126 => "Find",
            127 => "Mute",
            128 => "Volume Up",
            129 => "Volume Down",
            130 => "Locking Caps Lock",
            131 => "Locking Num Lock",
            132 => "Locking Scroll Lock",
            133 => "Numpad Comma",
            134 => "Numpad Equals",
            153 => "Alt Erase",
            154 => "Sys Req",
            155 => "Cancel",
            156 => "Clear",
            157 => "Prior",
            158 => "Return",
            159 => "Separator",
            160 => "Out",
            161 => "Oper",
            162 => "Clear Again",
            163 => "Cr Sel",
            164 => "Ex Sel",
            176 => "Numpad 00",
            177 => "Numpad 000",
            178 => "Thousands Separator",
            179 => "Decimal Separator",
            180 => "Currency Unit",
            181 => "Currency Subunit",
            182 => "Numpad Left Parenthesis",
            183 => "Numpad Right Parenthesis",
            184 => "Numpad Left Brace",
            185 => "Numpad Right Brace",
            186 => "Numpad Tab",
            187 => "Numpad Backspace",
            188 => "Numpad A",
            189 => "Numpad B",
            190 => "Numpad C",
            191 => "Numpad D",
            192 => "Numpad E",
            193 => "Numpad F",
            194 => "Numpad XOR",
            195 => "Numpad Power",
            196 => "Numpad Percent",
            197 => "Numpad Less",
            198 => "Numpad Greater",
            199 => "Numpad Ampersand",
            200 => "Numpad Double Ampersand",
            201 => "Numpad Vertical Bar",
            202 => "Numpad Double Vertical Bar",
            203 => "Numpad Colon",
            204 => "Numpad Hash",
            205 => "Numpad Space",
            206 => "Numpad At",
            207 => "Numpad Exclamation",
            208 => "Numpad Memory Store",
            209 => "Numpad Memory Recall",
            210 => "Numpad Memory Clear",
            211 => "Numpad Memory Add",
            212 => "Numpad Memory Subtract",
            213 => "Numpad Memory Multiply",
            214 => "Numpad Memory Divide",
            215 => "Numpad Plus Minus",
            216 => "Numpad Clear",
            217 => "Numpad Clear Entry",
            218 => "Numpad Binary",
            219 => "Numpad Octal",
            220 => "Numpad Decimal",
            221 => "Numpad Hexadecimal",
            224 => "Left Control",
            225 => "Left Shift",
            226 => "Left Alt",
            227 => "Left Windows",
            228 => "Right Control",
            229 => "Right Shift",
            230 => "Right Alt",
            231 => "Right Windows",
            257 => "Mode",
            _ => $"Key code {code}"
        };
        return true;
    }

    private static bool TryFormatControllerButton(
        int code,
        bool playStationLayout,
        out string? name)
    {
        name = null;
        if (code is -1 or 0)
        {
            name = "Unassigned";
            return true;
        }

        if (code is < 0 or > 255)
        {
            return false;
        }

        name = code switch
        {
            2 => playStationLayout ? "Options" : "Start",
            3 => "D-pad Up",
            4 => "D-pad Right",
            5 => "D-pad Down",
            6 => "D-pad Left",
            7 => playStationLayout ? "L2" : "LT",
            8 => playStationLayout ? "R2" : "RT",
            9 => playStationLayout ? "L1" : "LB",
            10 => playStationLayout ? "R1" : "RB",
            11 => playStationLayout ? "Triangle" : "Y",
            12 => playStationLayout ? "Circle" : "B",
            13 => playStationLayout ? "Cross" : "A",
            14 => playStationLayout ? "Square" : "X",
            15 => playStationLayout ? "Share" : "Back",
            _ => $"Button code {code}"
        };
        return true;
    }

    private bool TryReadSystemValue(
        ulong instance,
        string controlId,
        out string? value,
        out int position,
        out int count)
    {
        value = null;
        position = 0;
        count = 0;
        switch (controlId)
        {
            case "resolution":
                return TryReadResolution(
                    instance,
                    out value,
                    out position,
                    out count);

            case "display-mode":
                if (!TryReadInt32Stable(instance + 0x290, out var displayMode)
                    || displayMode is < 0 or > 2)
                {
                    return false;
                }

                value = displayMode switch
                {
                    0 => "Fullscreen",
                    1 => "Borderless Windowed",
                    2 => "Windowed",
                    _ => null
                };
                position = displayMode + 1;
                count = 3;
                return true;

            case "primary-display":
                if (!TryReadInt32Stable(instance + 0x294, out var display)
                    || display is < 0 or > 63)
                {
                    return false;
                }

                value = $"Display {display + 1}";
                return true;

            case "brightness":
                return TryReadPercentage(instance + 0x298, out value);

            case "master-volume":
                return TryReadPercentage(instance + 0x2A0, out value);

            default:
                return true;
        }
    }

    private bool TryReadResolution(
        ulong instance,
        out string? value,
        out int position,
        out int count)
    {
        value = null;
        position = 0;
        count = 0;
        if (!TryReadUInt64Stable(instance + 0x270, out var begin)
            || !TryReadUInt64Stable(instance + 0x278, out var end)
            || !TryReadInt32Stable(instance + 0x288, out var index)
            || begin == 0
            || end < begin
            || (end - begin) % 8 != 0)
        {
            return false;
        }

        var rawCount = (end - begin) / 8;
        if (rawCount is 0 or > MaximumResolutionCount
            || index < 0
            || (ulong)index >= rawCount
            || (ulong)index > (ulong.MaxValue - begin) / 8)
        {
            return false;
        }

        var selectedAddress = begin + ((ulong)index * 8);
        Span<byte> first = stackalloc byte[8];
        Span<byte> second = stackalloc byte[8];
        if (!memory.TryRead(selectedAddress, first)
            || !memory.TryRead(selectedAddress, second)
            || !first.SequenceEqual(second))
        {
            return false;
        }

        var width = BinaryPrimitives.ReadInt32LittleEndian(first);
        var height = BinaryPrimitives.ReadInt32LittleEndian(first[4..]);
        if (width is < 320 or > 32768 || height is < 200 or > 32768)
        {
            return false;
        }

        value = $"{width} by {height}";
        position = index + 1;
        count = checked((int)rawCount);
        return true;
    }

    private bool TryReadToggle(ulong address, out string? value)
    {
        value = null;
        if (!TryReadByteStable(address, out var enabled)
            || enabled > 1)
        {
            return false;
        }

        value = enabled == 0 ? "Off" : "On";
        return true;
    }

    private bool TryReadPercentage(ulong address, out string? value)
    {
        value = null;
        if (!TryReadInt32Stable(address, out var percentage)
            || percentage is < 0 or > 100)
        {
            return false;
        }

        value = percentage.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    private bool TryReadUInt64Stable(ulong address, out ulong value)
    {
        value = 0;
        return memory.TryReadUInt64(address, out var first)
            && memory.TryReadUInt64(address, out var second)
            && first == second
            && ((value = first) == first);
    }

    private bool TryReadInt32Stable(ulong address, out int value)
    {
        Span<byte> first = stackalloc byte[sizeof(int)];
        Span<byte> second = stackalloc byte[sizeof(int)];
        if (!memory.TryRead(address, first)
            || !memory.TryRead(address, second)
            || !first.SequenceEqual(second))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadInt32LittleEndian(first);
        return true;
    }

    private bool TryReadUInt32Stable(ulong address, out uint value)
    {
        Span<byte> first = stackalloc byte[sizeof(uint)];
        Span<byte> second = stackalloc byte[sizeof(uint)];
        if (!memory.TryRead(address, first)
            || !memory.TryRead(address, second)
            || !first.SequenceEqual(second))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(first);
        return true;
    }

    private bool TryReadByteStable(ulong address, out byte value)
    {
        Span<byte> first = stackalloc byte[1];
        Span<byte> second = stackalloc byte[1];
        if (!memory.TryRead(address, first)
            || !memory.TryRead(address, second)
            || first[0] != second[0])
        {
            value = 0;
            return false;
        }

        value = first[0];
        return true;
    }

    private static bool TryAdd(ulong left, ulong right, out ulong result)
    {
        result = left + right;
        return result >= left;
    }

    private static bool TryMapControl(
        Steam2026NativeSystemMenuScene scene,
        int focus,
        out string controlId)
    {
        controlId = scene switch
        {
            Steam2026NativeSystemMenuScene.EscapeRoot => focus switch
            {
                0 => "game-options",
                1 => "boosts",
                2 => "exit",
                3 => "back",
                _ => string.Empty
            },
            Steam2026NativeSystemMenuScene.GameOptions => focus switch
            {
                0 => "system",
                1 => "edit-controls",
                2 => "autosave",
                3 => "back",
                _ => string.Empty
            },
            Steam2026NativeSystemMenuScene.Controls => focus switch
            {
                0 => "keyboard",
                1 => "controller",
                2 => "back",
                _ => string.Empty
            },
            Steam2026NativeSystemMenuScene.Autosave => focus switch
            {
                1 => "autosave",
                2 => "apply",
                3 => "default",
                4 => "back",
                _ => string.Empty
            },
            Steam2026NativeSystemMenuScene.Boosts => focus switch
            {
                1 => "battle-assist",
                3 => "no-encounters",
                5 => "speed-boost",
                6 => "apply",
                7 => "default",
                8 => "back",
                _ => string.Empty
            },
            Steam2026NativeSystemMenuScene.System => focus switch
            {
                1 => "resolution",
                2 => "display-mode",
                3 => "primary-display",
                4 => "brightness",
                6 => "master-volume",
                7 => "apply",
                8 => "default",
                9 => "back",
                _ => string.Empty
            },
            Steam2026NativeSystemMenuScene.Keyboard
                or Steam2026NativeSystemMenuScene.Controller =>
                MapBindingControl(focus),
            _ => string.Empty
        };
        return controlId.Length > 0;
    }

    private static string MapBindingControl(int focus)
    {
        if (focus is >= 1 and <= 15)
        {
            return BindingControlIds[focus - 1];
        }

        return focus switch
        {
            16 => "apply",
            17 => "default",
            18 => "back",
            _ => string.Empty
        };
    }

    private static string GetModalText(
        Steam2026NativeSystemMenuScene scene,
        int parentFocus) =>
        scene switch
        {
            Steam2026NativeSystemMenuScene.EscapeRoot =>
                "Exit game?",
            Steam2026NativeSystemMenuScene.Boosts =>
                "You cannot undo these changes once applied. " +
                "You also cannot unlock achievements with this save. Proceed?",
            Steam2026NativeSystemMenuScene.System when parentFocus == 8 =>
                "Restore default settings?",
            Steam2026NativeSystemMenuScene.Autosave when parentFocus == 3 =>
                "Restore default settings?",
            Steam2026NativeSystemMenuScene.Keyboard when parentFocus == 17 =>
                "Restore default settings?",
            Steam2026NativeSystemMenuScene.Controller when parentFocus == 17 =>
                "Restore default settings?",
            Steam2026NativeSystemMenuScene.System =>
                "Apply this setting? It will revert to its previous setting in 10 seconds.",
            Steam2026NativeSystemMenuScene.Keyboard
                or Steam2026NativeSystemMenuScene.Controller =>
                "One or more required controls are unassigned.",
            _ => "Apply this setting?"
        };

    private void Remove(
        Steam2026NativeSystemMenuScene scene,
        ulong instance)
    {
        for (var index = activeScenes.Count - 1; index >= 0; index--)
        {
            var active = activeScenes[index];
            if (active.Definition.Scene == scene && active.Instance == instance)
            {
                activeScenes.RemoveAt(index);
            }
        }
    }

    private readonly record struct ButtonCastInfo(
        bool IsButton,
        uint CompleteObjectOffset,
        uint ButtonOffset);

    private sealed class ActiveScene(
        Steam2026NativeSystemMenuDefinition definition,
        ulong instance,
        long generation)
    {
        internal Steam2026NativeSystemMenuDefinition Definition { get; } = definition;

        internal ulong Instance { get; } = instance;

        internal long Generation { get; } = generation;

        internal bool ModalWasActive { get; set; }

        internal long ModalGeneration { get; set; }

        internal int? LastFocus { get; set; }
    }
}
