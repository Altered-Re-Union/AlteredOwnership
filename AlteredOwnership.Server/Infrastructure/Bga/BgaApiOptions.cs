namespace AlteredOwnership.Server.Infrastructure.Bga;

public class BgaApiOptions
{
    public const string SectionName = "BgaApi";

    // apiKey query param for altered-bga-api's GET /api/players/reunion-id
    // (ApiKeys:Ownership on that side) -- ours alone, never shared with anything else.
    public string ApiKey { get; set; } = "";
}
