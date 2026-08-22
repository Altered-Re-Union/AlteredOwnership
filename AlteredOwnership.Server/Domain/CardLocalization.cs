namespace AlteredOwnership.Server.Domain;

public static class CardLocalization
{
    // Picks the requested locale from a per-language jsonb dictionary, falling back
    // to English.
    public static string? Localize(Dictionary<string, string>? text, string locale)
    {
        if (text is null || text.Count == 0)
            return null;
        return text.TryGetValue(locale, out var value) ? value : text.GetValueOrDefault("en");
    }
}
