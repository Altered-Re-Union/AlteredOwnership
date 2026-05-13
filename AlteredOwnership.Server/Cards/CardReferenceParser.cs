namespace AlteredOwnership.Server.Cards;

public static class CardReferenceParser
{
    // Altered references look like ALT_<set>_<booster>_<faction>_<id>_<rarity>[_<uniqueSerial>]
    // Rarity segment "U" with a trailing serial identifies a unique copy.
    public static bool IsUnique(string reference)
    {
        var parts = reference.Split('_');
        return parts.Length >= 7 && parts[^2] == "U";
    }
}
