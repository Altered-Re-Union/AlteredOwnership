namespace AlteredOwnership.Server.Cards;
using System.Collections.Generic;
using System.Linq;


public static class CardReferenceParser
{
    // Altered references look like ALT_<set>_<booster>_<faction>_<id>_<rarity>[_<uniqueSerial>]
    // Rarity segment "U" with a trailing serial identifies a unique copy.
    public static bool IsUnique(string reference)
    {
        var parts = reference.Split('_');

        // classical form : ALT_<SET>_<CAT>_<FACTION>_<NUM>_U_<id>  -> 7 segments, 'U' at position 5
        if (parts.Length >= 7 && parts[5] == "U") return true;

        // legacy form : ALT_<SET>_<CAT>_<FACTION>_<NUM>_<id numérique>  -> 6 segments
        if (parts.Length == 6 && parts[5].All(char.IsDigit)) return true;

        return false;
    }

    private static readonly HashSet<string> DedicatedAltSets =
    [
        "DUSTERTOP", "DUSTERCB", "DUSTEROP",
        "EOLETOP", "EOLECB", "EOLEOP",
        "TCS3", "WCS25", "WCQ25", "MUSUBI",
        "JUDGE", "WCF25", "WCS26",

    ];

    private static readonly HashSet<string> CoreKsAltArts =
    [
        "ALT_COREKS_B_AX_07", "ALT_COREKS_B_AX_18",
        "ALT_COREKS_B_BR_07", "ALT_COREKS_B_BR_22",
        "ALT_COREKS_B_LY_09", "ALT_COREKS_B_LY_21",
        "ALT_COREKS_B_MU_08", "ALT_COREKS_B_MU_09",
        "ALT_COREKS_B_OR_20", "ALT_COREKS_B_OR_21",
        "ALT_COREKS_B_YZ_11", "ALT_COREKS_B_YZ_17",
    ];

    public static bool IsAlternateArt(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return false;

        var parts = reference.Split('_');
        if (parts.Length < 3) return false;

        var set = parts[1];
        var category = parts[2];

        if (category == "A" || category == "P") return true;
        if (DedicatedAltSets.Contains(set)) return true;

        if (set == "COREKS" && parts.Length >= 5)
        {
            var key = string.Join("_", parts.Take(5));
            return CoreKsAltArts.Contains(key);
        }

        return false;
    }
}
