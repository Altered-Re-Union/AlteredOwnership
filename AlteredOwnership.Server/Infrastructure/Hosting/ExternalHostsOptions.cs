namespace AlteredOwnership.Server.Infrastructure.Hosting;

public class ExternalHostsOptions
{
    public const string SectionName = "ExternalHosts";

    // CSP source for any *.altered-reunion.com host (img/font/style-src).
    public string ReunionCspSource { get; set; } = "";

    // Absolute base URL the SPA hotlinks for stylesheets, images, and navbar links.
    public string ReunionWebBase { get; set; } = "";

    // Absolute base URL for the Keycloak auth host (CSP form-action and surfaced to the SPA).
    public string AuthBase { get; set; } = "";
}
