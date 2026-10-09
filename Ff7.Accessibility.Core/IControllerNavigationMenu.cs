namespace Ff7.Accessibility.Core;

/// <summary>Cheap input policy shared by the capture hooks; no speech or native writes.</summary>
public interface IControllerNavigationMenu
{
    bool IsOpen { get; }
    bool IsSuppressing { get; }
    bool UsesModifierControls { get; }
    bool SettingsIsOpen { get; }
    string LastRefusal { get; }
    ControllerNavigationMenuResult Observe(GamepadSnapshot snapshot,
        ControllerNavigationContext context, DateTime nowUtc);
    void RequestClose();
    void Close();
    void SetSettingsOpen(bool open);
}
