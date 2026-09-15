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

    // MUSUBI 1.0 was entirely equal-access "kit" cards, but MUSUBI 2.0 (CardsData commit
    // "missing musubi 2.0 cards") added real booster-pull prints alongside them — these
    // six are randomly pulled and must be tracked in CardOwnership like any other alt
    // art, not treated as everyone's for free like the rest of the set.
    private static readonly HashSet<string> MusubiTrackedPrints =
    [
        "ALT_MUSUBI_B_AX_30_R1",
        "ALT_MUSUBI_B_BR_74_R1",
        "ALT_MUSUBI_B_LY_29_R1",
        "ALT_MUSUBI_B_MU_70_R1",
        "ALT_MUSUBI_B_OR_66_R1",
        "ALT_MUSUBI_B_YZ_21_C",
    ];

    // One-off exceptions confirmed by the user: printings that, despite not being a
    // base-set/MUSUBI print, were actually given to every participant of their
    // organized-play event, the same "everyone already has it" status as most of
    // MUSUBI — not a rule that can be derived from the catalog's own columns.
    private static readonly HashSet<string> AlwaysAvailablePrints =
    [
        "ALT_DUSTEROP_B_BR_31_C", // Booda token, given to every Duster organized-play participant
    ];

    // A printing is owned in unlimited quantity by every player when it's the kind of
    // art the collection importer never tracks in CardOwnership (the "default"/plain
    // illustration — see CardReferenceParser.IsAlternateArt), when it's from MUSUBI and
    // not one of the tracked MUSUBI 2.0 prints above (MUSUBI prints ARE normally tracked
    // — MUSUBI is in CardReferenceParser.DedicatedAltSets — but most of the set is equal-
    // access), when it's a TOKEN printed in a base set (base-set tokens ship in every
    // box, so ownership isn't a real constraint for them the way it is for a token from a
    // promo/organized-play set), or when it's one of the one-off exceptions above.
    public static bool IsInfinite(CardArtCatalogEntry entry) =>
        !CardReferenceParser.IsAlternateArt(entry.Reference)
        || (entry.Set == MusubiSet && !MusubiTrackedPrints.Contains(entry.Reference))
        || (entry.CardType == TokenCardType && entry.IsBaseSet)
        || AlwaysAvailablePrints.Contains(entry.Reference);

    // A deck holds at most 3 copies of any card, except a HERO (exactly 1 per deck) or
    // a TOKEN (a single chosen art represents every copy the card's effects create).
    public static int MaxSlots(string cardType) => cardType is HeroCardType or TokenCardType ? 1 : 3;
}
