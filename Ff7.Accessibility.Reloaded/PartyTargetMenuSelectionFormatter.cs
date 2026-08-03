namespace Ff7.Accessibility.Reloaded;

public static class PartyTargetMenuSelectionFormatter
{
    public static NativeMenuSelection Create(
        StatusMenuSnapshot status,
        uint widgetAddress,
        int cursor)
    {
        var text =
            $"{status.Name}. HP {status.CurrentHp} of {status.MaxHp}. " +
            $"MP {status.CurrentMp} of {status.MaxMp}";
        var key =
            $"party-target:{widgetAddress:X8}:{cursor}:{status.CharacterId}:" +
            $"{status.CurrentHp}:{status.MaxHp}:{status.CurrentMp}:{status.MaxMp}";
        return new NativeMenuSelection(text, null, key);
    }
}
