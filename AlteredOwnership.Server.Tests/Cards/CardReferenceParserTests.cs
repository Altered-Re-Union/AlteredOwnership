using AlteredOwnership.Server.Cards;

namespace AlteredOwnership.Server.Tests.Cards;

public class CardReferenceParserTests
{
    [Theory]
    [InlineData("ALT_ALIZE_B_AX_32_U_4624")]
    [InlineData("ALT_COREKS_B_OR_20_U_12")]
    public void IsUnique_returns_true_for_classical_form(string reference)
        => Assert.True(CardReferenceParser.IsUnique(reference));

    [Theory]
    [InlineData("ALT_ALIZE_B_AX_32_123")]
    public void IsUnique_returns_true_for_legacy_numeric_form(string reference)
        => Assert.True(CardReferenceParser.IsUnique(reference));

    [Theory]
    [InlineData("ALT_ALIZE_A_AX_35_C")]
    [InlineData("ALT_ALIZE_B_AX_32_R1")]
    [InlineData("ALT_ALIZE_B_AX_32_C")]
    [InlineData("ALT_ALIZE_A_AX_35_R2")]
    public void IsUnique_returns_false_for_non_unique(string reference)
        => Assert.False(CardReferenceParser.IsUnique(reference));

    [Theory]
    [InlineData("ALT_ALIZE_A_AX_35_C")]
    [InlineData("ALT_ALIZE_P_AX_35_C")]
    public void IsAlternateArt_returns_true_for_A_or_P_category(string reference)
        => Assert.True(CardReferenceParser.IsAlternateArt(reference));

    [Theory]
    [InlineData("ALT_DUSTERTOP_B_AX_01_C")]
    [InlineData("ALT_TCS3_B_AX_01_C")]
    [InlineData("ALT_JUDGE_B_AX_01_C")]
    public void IsAlternateArt_returns_true_for_dedicated_sets(string reference)
        => Assert.True(CardReferenceParser.IsAlternateArt(reference));

    [Theory]
    [InlineData("ALT_COREKS_B_AX_07_C")]
    [InlineData("ALT_COREKS_B_YZ_17_R1")]
    public void IsAlternateArt_returns_true_for_listed_COREKS_alt_arts(string reference)
        => Assert.True(CardReferenceParser.IsAlternateArt(reference));

    [Theory]
    [InlineData("ALT_ALIZE_B_AX_32_C")]
    [InlineData("ALT_COREKS_B_AX_99_C")]
    [InlineData("ALT_ALIZE_B_AX_32_U_4624")]
    [InlineData("")]
    public void IsAlternateArt_returns_false_for_regular_cards(string reference)
        => Assert.False(CardReferenceParser.IsAlternateArt(reference));
}
