using System.Security.Claims;
using AlteredOwnership.Server.Infrastructure.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AlteredOwnership.Server.Endpoints;

public static class AuthEndpoints
{
    public record MeResponse(string Sub, string? Pseudo, string? Email);

    public record CsrfResponse(string Token);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth");

        // Triggers the OIDC code flow. ?remember=true bumps absolute lifetime to 15j.
        // ?silent=true sends prompt=none so an existing Keycloak SSO session can log
        // the user in without a UI step. If no SSO session exists, Keycloak returns
        // login_required and OnRemoteFailure quietly redirects back to returnUrl.
        group.MapGet("login", (
            HttpContext ctx,
            IOptions<KeycloakOptions> opts,
            [FromQuery] string? returnUrl,
            [FromQuery] bool remember = false,
            [FromQuery] bool silent = false) =>
        {
            var safeReturn = IsLocalUrl(returnUrl) ? returnUrl! : "/";
            var props = new AuthenticationProperties
            {
                RedirectUri = safeReturn,
                IsPersistent = remember,
                ExpiresUtc = remember
                    ? DateTimeOffset.UtcNow.Add(opts.Value.RememberMeAbsoluteTimeout)
                    : null,
            };
            if (silent)
                props.Items[AuthConstants.SilentLoginPropertyKey] = "true";
            return Results.Challenge(props, [AuthConstants.OidcScheme]);
        });

        // Clears the local session and asks Keycloak to end SSO.
        group.MapPost("logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(AuthConstants.CookieScheme);
            await ctx.SignOutAsync(AuthConstants.OidcScheme, new AuthenticationProperties
            {
                RedirectUri = "/",
            });
            return Results.Empty;
        }).RequireAuthorization(p => p
            .AddAuthenticationSchemes(AuthConstants.CookieScheme)
            .RequireAuthenticatedUser());

        // Issues an antiforgery request token (and sets the paired secret cookie) bound
        // to the current cookie session. The SPA echoes it back in the X-CSRF-TOKEN header.
        group.MapGet("csrf", (HttpContext ctx, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(ctx);
            return Results.Ok(new CsrfResponse(tokens.RequestToken!));
        }).RequireAuthorization(AuthConstants.SessionPolicy);

        // Returns the current user's identity, or 401 if not authenticated.
        group.MapGet("me", (HttpContext ctx) =>
        {
            var user = ctx.User;
            if (user.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();

            var sub = user.FindFirstValue("sub")
                ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? "";
            var pseudo = user.FindFirstValue("pseudo")
                ?? user.Identity.Name;
            var email = user.FindFirstValue("email");
            return Results.Ok(new MeResponse(sub, pseudo, email));
        }).RequireAuthorization(p => p
            .AddAuthenticationSchemes(AuthConstants.CookieScheme)
            .RequireAuthenticatedUser());

        return routes;
    }

    private static bool IsLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url)
        && url.StartsWith('/')
        && !url.StartsWith("//")
        && !url.StartsWith("/\\");
}
