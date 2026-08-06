using System;
using System.Globalization;

namespace FF7_Launcher;

internal static class LauncherAccessibilityText
{
    internal static string GetButtonName(int index, CultureInfo culture = null)
    {
        if (index < 0 || index > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var language = (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName.ToLowerInvariant();
        switch (language)
        {
            case "fr":
                return new[] { "Jouer", "Paramètres", "Sortir" }[index];
            case "de":
                return new[] { "Spiel", "Einstellungen", "Beenden" }[index];
            case "es":
                return new[] { "Jugar", "Configuración", "Salir" }[index];
            case "ja":
                // These are the exact words rendered in the Japanese assets.
                return new[] { "Play", "Setting", "Exit" }[index];
            default:
                return new[] { "Play", "Options", "Exit" }[index];
        }
    }
}
