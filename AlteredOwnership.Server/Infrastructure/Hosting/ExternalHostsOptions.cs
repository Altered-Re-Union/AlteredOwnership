namespace AlteredOwnership.Server.Infrastructure.Hosting;

public class ExternalHostsOptions
{
    public const string SectionName = "ExternalHosts";

    // Absolute base URL of the community site (altered.re), used for the "back to
    // altered.re" link and the account link. Surfaced to the SPA via /config.js.
    public string ReunionWebBase { get; set; } = "";

    // Absolute base URL for the Keycloak auth host (CSP form-action and surfaced to the SPA).
    public string AuthBase { get; set; } = "";

    // Absolute base URL of the card catalog API (cards.alteredcore.org). Server-only:
    // queried during import to backfill card metadata. Not surfaced to the SPA.
    public string CardsApiBase { get; set; } = "";
}
