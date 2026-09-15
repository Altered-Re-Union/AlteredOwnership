namespace AlteredOwnership.Server.Data.Entities;

// Whether a player's alt-art choices for regular (non-token) cards are driven by their
// global UserCardArtPreference slots (Global — auto-applied everywhere a deck's cards
// are resolved: opening it for edit, duplicating, importing, and ApplyToDeckAsync,
// which is also what BGA sees via altered-bga-api) or picked individually per deck in
// the website's own deckbuilder (PerDeck — the deck's own card references are the
// source of truth, UserCardArtPreference is never consulted for them). Token art is
// always global regardless of this setting, since a token is never itself a deck line.
public enum AltArtPreferenceMode
{
    PerDeck = 0,
    Global = 1,
}
