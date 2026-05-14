namespace AlteredOwnership.Server.Auth;

public static class AuthConstants
{
    public const string CookieScheme = "Cookie";
    public const string OidcScheme = "Oidc";
    public const string BearerScheme = "Bearer";

    public const string ImportPolicy = "ImportCollection";
    public const string ReadPolicy = "ReadCollection";

    public const string ReadScope = "read-collection";
    public const string WriteScope = "write-collection";
}
