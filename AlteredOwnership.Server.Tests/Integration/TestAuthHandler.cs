using System.Security.Claims;
using System.Text.Encodings.Web;
using AlteredOwnership.Server.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlteredOwnership.Server.Tests.Integration;

public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string KeycloakId = "test-keycloak-id";

    // Tests can impersonate distinct users by setting this header; without it,
    // requests authenticate as the default KeycloakId.
    public const string UserHeader = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var keycloakId = Request.Headers.TryGetValue(UserHeader, out var values)
            && !string.IsNullOrEmpty(values.ToString())
                ? values.ToString()
                : KeycloakId;

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, keycloakId),
                new Claim("sub", keycloakId),
                new Claim("scope", $"{AuthConstants.ReadScope} {AuthConstants.WriteScope}"),
            ],
            SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
