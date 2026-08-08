namespace Ff7.Accessibility.Reloaded;

public static class BattleStatusCatalog
{
    private const uint BeneficialMask =
        (1u << 8) |
        (1u << 15) |
        (1u << 16) |
        (1u << 17) |
        (1u << 18) |
        (1u << 20) |
        (1u << 24) |
        (1u << 28) |
        (1u << 29) |
        (1u << 30);

    public static string Name(int bit) => bit switch
    {
        0 => "Death",
        1 => "Near Death",
        2 => "Sleep",
        3 => "Poison",
        4 => "Sadness",
        5 => "Fury",
        6 => "Confusion",
        7 => "Silence",
        8 => "Haste",
        9 => "Slow",
        10 => "Stop",
        11 => "Frog",
        12 => "Small",
        13 => "Slow Numb",
        14 => "Petrify",
        15 => "Regen",
        16 => "Barrier",
        17 => "Magic Barrier",
        18 => "Reflect",
        19 => "Dual",
        20 => "Shield",
        21 => "Death Sentence",
        22 => "Manipulate",
        23 => "Berserk",
        24 => "Peerless",
        25 => "Paralysis",
        26 => "Darkness",
        27 => "Dual Drain",
        28 => "Death Force",
        29 => "Resist",
        30 => "Lucky Girl",
        31 => "Imprisoned",
        _ => "Unknown Status"
    };

    public static bool IsBeneficial(int bit) =>
        bit is >= 0 and < 32 && (BeneficialMask & (1u << bit)) != 0;

    public static IReadOnlyList<string> ActiveNames(uint mask, bool beneficial)
    {
        var names = new List<string>();
        for (var bit = 0; bit < 32; bit++)
        {
            if ((mask & (1u << bit)) != 0 && IsBeneficial(bit) == beneficial)
            {
                names.Add(Name(bit));
            }
        }

        return names;
    }
}
