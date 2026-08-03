namespace Ff7.Accessibility.Runtime.Abstractions;

[Flags]
public enum RuntimeCapability
{
    None = 0,
    Lifecycle = 1 << 0,
    ForegroundInput = 1 << 1,
    Menus = 1 << 2,
    Dialogue = 1 << 3,
    Field = 1 << 4,
    Navigation = 1 << 5,
    Battle = 1 << 6,
    Movies = 1 << 7,
    Saves = 1 << 8,
    FullParity = Lifecycle | ForegroundInput | Menus | Dialogue | Field
                 | Navigation | Battle | Movies | Saves
}
