namespace AlteredOwnership.Server.Infrastructure.Auth;

public static class AuthConstants
{
    public const string CookieScheme = "Cookie";
    public const string OidcScheme = "Oidc";
    public const string BearerScheme = "Bearer";

    // SPA-only via cookie, must carry write-collection scope. Used by every SPA
    // action that mutates the collection projection (import, opening a booster).
    public const string WritePolicy = "WriteCollection";
    public const string ReadPolicy = "ReadCollection";
    public const string AdminPolicy = "Admin";

    // Any authenticated cookie session, no scope required (me, logout, csrf).
    public const string SessionPolicy = "Session";

    public const string ReadScope = "read-collection";
    public const string WriteScope = "write-collection";

    public const string SilentLoginPropertyKey = ".silent";

    // Header the SPA echoes the antiforgery request token in.
    public const string CsrfHeaderName = "X-CSRF-TOKEN";
}
