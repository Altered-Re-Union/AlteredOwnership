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

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, KeycloakId),
                new Claim("sub", KeycloakId),
                new Claim("scope", $"{AuthConstants.ReadScope} {AuthConstants.WriteScope}"),
            ],
            SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
