using AlteredOwnership.Server.Data.Entities;

namespace AlteredOwnership.Server.Domain;

// Shared rules for the alt-art selection feature — used by AltArtService (the
// endpoints) and AltArtPreferenceReconciler (post-import cleanup), so both agree on
// what "infinite" and "how many copies" mean.
public static class AltArtRules
{
    private const string MusubiSet = "MUSUBI";
    private const string HeroCardType = "HERO";
    private const string TokenCardType = "TOKEN";

    // A printing is owned in unlimited quantity by every player when it's the kind of
    // art the collection importer never tracks in CardOwnership (the "default"/plain
    // illustration — see CardReferenceParser.IsAlternateArt), when it's from MUSUBI (a
    // set everyone has equal access to even though MUSUBI prints ARE normally tracked —
    // MUSUBI is in CardReferenceParser.DedicatedAltSets), or when it's a TOKEN printed
    // in a base set: base-set tokens ship in every box, so ownership isn't a real
    // constraint for them the way it is for a token from a promo/organized-play set.
    public static bool IsInfinite(CardArtCatalogEntry entry) =>
        !CardReferenceParser.IsAlternateArt(entry.Reference)
        || entry.Set == MusubiSet
        || (entry.CardType == TokenCardType && entry.IsBaseSet);

    // A deck holds at most 3 copies of any card, except a HERO (exactly 1 per deck) or
    // a TOKEN (a single chosen art represents every copy the card's effects create).
    public static int MaxSlots(string cardType) => cardType is HeroCardType or TokenCardType ? 1 : 3;
}
