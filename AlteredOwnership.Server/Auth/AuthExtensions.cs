using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace AlteredOwnership.Server.Auth;

public static class AuthExtensions
{
    public static IServiceCollection AddOwnershipAuth(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment env)
    {
        services.AddOptions<KeycloakOptions>()
            .Bind(configuration.GetSection(KeycloakOptions.SectionName))
            .ValidateDataAnnotations();

        var options = configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>()
            ?? throw new InvalidOperationException("Missing 'Keycloak' configuration section.");

        services.AddSingleton<ITicketStore, RedisTicketStore>();

        services
            .AddAuthentication(o =>
            {
                o.DefaultScheme = AuthConstants.CookieScheme;
                o.DefaultChallengeScheme = AuthConstants.OidcScheme;
            })
            .AddCookie(AuthConstants.CookieScheme, c =>
            {
                c.Cookie.Name = "ao.sid";
                c.Cookie.HttpOnly = true;
                c.Cookie.SameSite = SameSiteMode.Lax;
                c.Cookie.SecurePolicy = env.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                c.SlidingExpiration = true;
                c.ExpireTimeSpan = options.SessionIdleTimeout;
                c.LoginPath = "/api/auth/login";
                c.LogoutPath = "/api/auth/logout";

                // Endpoints in /api/* return 401/403 instead of redirecting to login.
                c.Events.OnRedirectToLogin = ctx =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/api"))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }
                    ctx.Response.Redirect(ctx.RedirectUri);
                    return Task.CompletedTask;
                };
                c.Events.OnRedirectToAccessDenied = ctx =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/api"))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }
                    ctx.Response.Redirect(ctx.RedirectUri);
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(AuthConstants.OidcScheme, o =>
            {
                o.Authority = options.Authority;
                o.ClientId = options.ClientId;
                o.ClientSecret = options.ClientSecret;
                o.ResponseType = OpenIdConnectResponseType.Code;
                o.UsePkce = true;
                o.SaveTokens = true;
                o.GetClaimsFromUserInfoEndpoint = true;
                o.MapInboundClaims = false;
                o.RequireHttpsMetadata = !env.IsDevelopment();
                o.CallbackPath = "/api/auth/callback";
                o.SignedOutCallbackPath = "/api/auth/signout-callback";

                o.Scope.Clear();
                o.Scope.Add("openid");
                o.Scope.Add("profile");
                o.Scope.Add("email");
                o.Scope.Add(AuthConstants.ReadScope);
                o.Scope.Add(AuthConstants.WriteScope);

                o.TokenValidationParameters.NameClaimType = "pseudo";
                o.TokenValidationParameters.RoleClaimType = "roles";
            })
            .AddJwtBearer(AuthConstants.BearerScheme, o =>
            {
                o.Authority = options.Authority;
                o.RequireHttpsMetadata = !env.IsDevelopment();
                o.MapInboundClaims = false;
                o.TokenValidationParameters.ValidateAudience = false;
                o.TokenValidationParameters.NameClaimType = "pseudo";
            });

        // Wire the Redis-backed ticket store via DI after the cookie handler is registered.
        services.AddOptions<CookieAuthenticationOptions>(AuthConstants.CookieScheme)
            .Configure<ITicketStore>((co, store) => co.SessionStore = store);

        services.AddAuthorization(opt =>
        {
            // Strict: SPA-only via cookie, must carry write-collection scope.
            opt.AddPolicy(AuthConstants.ImportPolicy, p => p
                .AddAuthenticationSchemes(AuthConstants.CookieScheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx => HasScope(ctx.User, AuthConstants.WriteScope)));

            // Lax: SPA via cookie OR third party with read-collection scope.
            opt.AddPolicy(AuthConstants.ReadPolicy, p => p
                .AddAuthenticationSchemes(AuthConstants.CookieScheme, AuthConstants.BearerScheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx => HasScope(ctx.User, AuthConstants.ReadScope)));
        });

        return services;
    }

    private static bool HasScope(System.Security.Claims.ClaimsPrincipal user, string scope) =>
        user.FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope);
}
