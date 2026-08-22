namespace AlteredOwnership.Server.Infrastructure.Auth;

public class KeycloakAdminOptions
{
    public const string SectionName = "KeycloakAdmin";

    // Read-only service account (players-readonly-svc), granted only the
    // realm-management "view-users" client role — never realm-admin.
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Realm { get; set; } = "players";
}
