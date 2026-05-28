namespace AlteredOwnership.Server.Infrastructure.Auth;

public static class AuthConstants
{
    public const string CookieScheme = "Cookie";
    public const string OidcScheme = "Oidc";
    public const string BearerScheme = "Bearer";

    public const string ImportPolicy = "ImportCollection";
    public const string ReadPolicy = "ReadCollection";

    // Any authenticated cookie session, no scope required (me, logout, csrf).
    public const string SessionPolicy = "Session";

    public const string ReadScope = "read-collection";
    public const string WriteScope = "write-collection";

    public const string SilentLoginPropertyKey = ".silent";

    // Header the SPA echoes the antiforgery request token in.
    public const string CsrfHeaderName = "X-CSRF-TOKEN";
}
