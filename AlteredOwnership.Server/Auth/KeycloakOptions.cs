namespace AlteredOwnership.Server.Auth;

public class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    // Used by both bearer JWT validation (resource server side) and OIDC code flow.
    public string Authority { get; set; } = "";

    // Confidential OIDC client used by the SPA's server-side login (e.g. ownership-frontend).
    public string ClientId { get; set; } = "";

    public string ClientSecret { get; set; } = "";

    // 8h sliding session by default. Bumped to 15j when ?remember=true on /api/auth/login.
    public TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromHours(8);
    public TimeSpan RememberMeAbsoluteTimeout { get; set; } = TimeSpan.FromDays(15);
}
