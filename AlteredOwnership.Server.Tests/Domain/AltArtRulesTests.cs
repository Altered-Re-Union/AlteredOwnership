using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Domain;

namespace AlteredOwnership.Server.Tests.Domain;

public class AltArtRulesTests
{
    private static CardArtCatalogEntry AlternateArtEntry(string cardType, bool isBaseSet) => new()
    {
        Reference = "ALT_ALIZE_A_BR_31_C", // category "A" -> IsAlternateArt, not MUSUBI
        FamilyId = 31,
        CardType = cardType,
        Faction = "BR",
        Rarity = "C",
        Set = "ALIZE",
        IsBaseSet = isBaseSet,
    };

    // Same reasoning as MaxSlots: a base-set token-family print ships in every box
    // regardless of which TOKEN_* variant it is (plain tokens, mana orbs, aeroliths).
    [Theory]
    [InlineData("TOKEN")]
    [InlineData("TOKEN_MANA")]
    [InlineData("TOKEN_LANDMARK_PERMANENT")]
    public void IsInfinite_returns_true_for_every_token_family_variant_in_a_base_set(string cardType)
        => Assert.True(AltArtRules.IsInfinite(AlternateArtEntry(cardType, isBaseSet: true)));

    [Theory]
    [InlineData("TOKEN")]
    [InlineData("TOKEN_MANA")]
    [InlineData("TOKEN_LANDMARK_PERMANENT")]
    public void IsInfinite_returns_false_for_a_token_family_variant_outside_a_base_set(string cardType)
        => Assert.False(AltArtRules.IsInfinite(AlternateArtEntry(cardType, isBaseSet: false)));

    [Fact]
    public void IsInfinite_returns_false_for_a_regular_card_in_a_base_set()
        => Assert.False(AltArtRules.IsInfinite(AlternateArtEntry("CHARACTER", isBaseSet: true)));

    [Fact]
    public void MaxSlots_returns_1_for_hero()
        => Assert.Equal(1, AltArtRules.MaxSlots("HERO"));

    // A single chosen art represents every copy a token-family card's effects create,
    // regardless of which TOKEN_* variant it is (plain "Booda"-style tokens, mana orbs,
    // landmark permanents, ...) — none of them are ever a deck line with its own owned
    // quantity either way (see AltArtService.ResolveSelectedTokenItemsAsync).
    [Theory]
    [InlineData("TOKEN")]
    [InlineData("TOKEN_MANA")]
    [InlineData("TOKEN_LANDMARK_PERMANENT")]
    public void MaxSlots_returns_1_for_every_token_family_variant(string cardType)
        => Assert.Equal(1, AltArtRules.MaxSlots(cardType));

    [Theory]
    [InlineData("CHARACTER")]
    [InlineData("SPELL")]
    [InlineData("PERMANENT")]
    [InlineData("LANDMARK_PERMANENT")]
    public void MaxSlots_returns_3_for_regular_cards(string cardType)
        => Assert.Equal(3, AltArtRules.MaxSlots(cardType));
}
