using System.Runtime.InteropServices;

namespace Ff7.Accessibility.Steam2026X64.Runtime.SystemMenu;

internal enum Steam2026NativeSystemMenuScene
{
    EscapeRoot,
    GameOptions,
    Controls,
    Autosave,
    Boosts,
    Keyboard,
    Controller,
    System
}

internal sealed record Steam2026NativeSystemMenuDefinition(
    Steam2026NativeSystemMenuScene Scene,
    string SceneId,
    ulong EnterRva,
    byte[] EnterPrefix,
    ulong LeaveRva,
    byte[] LeavePrefix,
    ulong VtableRva,
    ulong FocusObjectOffset,
    ulong ModalObjectOffset);

internal readonly record struct Steam2026NativeSystemMenuLifecycleEvent(
    Steam2026NativeSystemMenuScene Scene,
    ulong Instance,
    bool Opened,
    long Generation);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
[global::Reloaded.Hooks.Definitions.X64.Function(
    global::Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
internal delegate void Steam2026NativeSystemMenuEnterOriginal(nint instance);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
[global::Reloaded.Hooks.Definitions.X64.Function(
    global::Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
internal delegate void Steam2026NativeSystemMenuLeaveOriginal(nint instance);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
[global::Reloaded.Hooks.Definitions.X64.Function(
    global::Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
internal delegate void Steam2026NativeSystemMenuManagerTickOriginal(
    nint host,
    float elapsedSeconds);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
[global::Reloaded.Hooks.Definitions.X64.Function(
    global::Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
internal delegate void Steam2026NativeSystemMenuDirectionInputOriginal(
    nint callbackContext,
    nint inputEvent);

internal static class Steam2026NativeSystemMenuDefinitions
{
    internal const ulong ManagerTickRva = 0x015D68D0;
    internal const ulong DirectionInputRva = 0x015C30D0;

    internal static readonly byte[] ManagerTickPrefix = Convert.FromHexString(
        "48895C2410555657415641574883EC500F297424400F28F1");

    internal static readonly byte[] DirectionInputPrefix = Convert.FromHexString(
        "4C8B81900000004180B8A8000000000F852B050000488B411880B8A800000000");

    private const ulong SimpleFocusObjectOffset = 0x130;
    private const ulong RichFocusObjectOffset = 0x138;
    private const ulong SimpleModalObjectOffset = 0x1B0;
    private const ulong RichModalObjectOffset = 0x240;

    private static readonly IReadOnlyList<Steam2026NativeSystemMenuDefinition> AllDefinitions =
    [
        Definition(
            Steam2026NativeSystemMenuScene.EscapeRoot,
            "escape-root",
            0x015D42A0,
            "488BC4488958104889701848897820554154415541564157",
            0x015D5B80,
            "4883EC284881C1A8000000E830DFFEFFE88B81A6FEC64029",
            0x0166D790,
            SimpleFocusObjectOffset,
            SimpleModalObjectOffset),
        Definition(
            Steam2026NativeSystemMenuScene.GameOptions,
            "game-options",
            0x015AA8F0,
            "488BC4488958104889701848897820554154415541564157",
            0x015ABCD0,
            "4881C1A8000000E9E47D0100CCCCCCCC48895C241048894C",
            0x0166B0F0,
            SimpleFocusObjectOffset,
            modalObjectOffset: 0),
        Definition(
            Steam2026NativeSystemMenuScene.Controls,
            "controls",
            0x015AD0A0,
            "488BC4488958104889701848897820554154415541564157",
            0x015ABCD0,
            "4881C1A8000000E9E47D0100CCCCCCCC48895C241048894C",
            0x0166B408,
            SimpleFocusObjectOffset,
            modalObjectOffset: 0),
        Definition(
            Steam2026NativeSystemMenuScene.Autosave,
            "autosave",
            0x0158F080,
            "48895C2410488974241848897C2420554154415541564157",
            0x01590D00,
            "4881C1A8000000E9E42D0300CCCCCCCC48895C2420488954",
            0x01669D08,
            RichFocusObjectOffset,
            RichModalObjectOffset),
        Definition(
            Steam2026NativeSystemMenuScene.Boosts,
            "boosts",
            0x01594730,
            "48895C2410488974241848897C2420554154415541564157",
            0x01590D00,
            "4881C1A8000000E9E42D0300CCCCCCCC48895C2420488954",
            0x0166A210,
            RichFocusObjectOffset,
            RichModalObjectOffset),
        Definition(
            Steam2026NativeSystemMenuScene.Keyboard,
            "keyboard",
            0x015BA8B0,
            "48895C2410488974241848897C2420554154415541564157",
            0x01590D00,
            "4881C1A8000000E9E42D0300CCCCCCCC48895C2420488954",
            0x0166BE80,
            RichFocusObjectOffset,
            RichModalObjectOffset),
        Definition(
            Steam2026NativeSystemMenuScene.Controller,
            "controller",
            0x015A6330,
            "488BC4488958104889701848897820554154415541564157",
            0x01590D00,
            "4881C1A8000000E9E42D0300CCCCCCCC48895C2420488954",
            0x0166ABD8,
            RichFocusObjectOffset,
            RichModalObjectOffset),
        Definition(
            Steam2026NativeSystemMenuScene.System,
            "system",
            0x015EE040,
            "48895C2410488974241848897C2420554154415541564157",
            0x015EFD20,
            "40534883EC20488BD94881C1A8000000E8BB3DFDFFC783A4",
            0x01672328,
            RichFocusObjectOffset,
            RichModalObjectOffset)
    ];

    internal static IReadOnlyList<Steam2026NativeSystemMenuDefinition> All =>
        AllDefinitions;

    internal static Steam2026NativeSystemMenuDefinition Get(
        Steam2026NativeSystemMenuScene scene) =>
        AllDefinitions.First(definition => definition.Scene == scene);

    private static Steam2026NativeSystemMenuDefinition Definition(
        Steam2026NativeSystemMenuScene scene,
        string sceneId,
        ulong enterRva,
        string enterPrefix,
        ulong leaveRva,
        string leavePrefix,
        ulong vtableRva,
        ulong focusObjectOffset,
        ulong modalObjectOffset) =>
        new(
            scene,
            sceneId,
            enterRva,
            Convert.FromHexString(enterPrefix),
            leaveRva,
            Convert.FromHexString(leavePrefix),
            vtableRva,
            focusObjectOffset,
            modalObjectOffset);
}
