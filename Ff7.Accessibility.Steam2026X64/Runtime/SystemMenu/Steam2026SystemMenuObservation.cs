namespace Ff7.Accessibility.Steam2026X64.Runtime.SystemMenu;

internal enum Steam2026SystemMenuControlKind
{
    Button,
    Toggle,
    List,
    Slider,
    Binding,
    ModalChoice
}

internal sealed record Steam2026SystemMenuObservation(
    string SceneId,
    string ControlId,
    string? Value,
    int Position,
    int Count,
    string? PrimaryBinding,
    string? SecondaryBinding,
    string? ModalText,
    bool IsFocused,
    long Generation);

internal readonly record struct Steam2026SystemMenuSpeechRequest(
    string Text,
    bool Interrupt);
