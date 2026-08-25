using System.Text.Json;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Domain.Services;
using AlteredOwnership.Server.Endpoints;
using AlteredOwnership.Server.Infrastructure.Auth;
using AlteredOwnership.Server.Infrastructure.Cards;
using AlteredOwnership.Server.Infrastructure.Crypto;
using AlteredOwnership.Server.Infrastructure.EventSourcing;
using AlteredOwnership.Server.Infrastructure.Hosting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// Single source of truth for the pinned Altered-Card-Renderer build: used both for the
// CSP script-src allow-list below and for the URL handed to wwwroot pages via /config.js.
// Bump the commit here after reviewing the diff at
// https://github.com/PolluxTroy0/Altered-Card-Renderer/commits/main — nowhere else.
const string CardRendererCommit = "6e7a90f";
const string CardRendererScriptUrl =
    $"https://cdn.jsdelivr.net/gh/PolluxTroy0/Altered-Card-Renderer@{CardRendererCommit}/altered-card-renderer-minified.js";

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddRedisClientBuilder("cache").WithOutputCache();
builder.AddRedisDistributedCache("cache");
builder.AddNpgsqlDbContext<OwnershipDbContext>("ownershipdb");

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<UnhandledExceptionLogger>();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();

// UserRole (SetUserRoleRequest.Role in the admin promote/demote endpoint) is read
// from/written to JSON as its name ("Admin"/"Player"), not its numeric value.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = AuthConstants.CsrfHeaderName;
    o.Cookie.Name = "ao.csrf";
    // Always over the TLS edge in prod (Traefik forwards https); SameAsRequest elsewhere
    // since antiforgery hard-fails when Always meets a plain-HTTP dev/test request.
    o.Cookie.SecurePolicy = builder.Environment.IsProduction()
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScoped<CurrentUserAccessor>();
builder.Services.AddScoped<CollectionReader>();
builder.Services.AddScoped<OwnershipVerifier>();
builder.Services.AddScoped<CollectionImporter>();
builder.Services.AddScoped<CardMetadataBackfiller>();
builder.Services.AddScoped<RewardService>();
builder.Services.AddScoped<ProjectionReconciler>();
builder.Services.AddScoped<BoosterService>();
builder.Services.AddScoped<EventAppender>();
builder.Services.AddScoped<UserProvisioningService>();
builder.Services.AddScoped<UniqueStockService>();
builder.Services.AddScoped<EventHistoryReader>();
builder.Services.AddScoped<IAuthorizationHandler, AdminAuthorizationHandler>();

builder.Services.AddOptions<ExternalHostsOptions>()
    .Bind(builder.Configuration.GetSection(ExternalHostsOptions.SectionName));

builder.Services.AddOptions<EquinoxImportOptions>()
    .Bind(builder.Configuration.GetSection(EquinoxImportOptions.SectionName))
    .Validate(
        o => !o.AllowUnencrypted || !builder.Environment.IsProduction(),
        "EquinoxImport:AllowUnencrypted is a dev-only escape hatch and must not be enabled in Production.")
    .ValidateOnStart();

var externalHosts = builder.Configuration.GetSection(ExternalHostsOptions.SectionName).Get<ExternalHostsOptions>()
    ?? throw new InvalidOperationException("Missing 'ExternalHosts' configuration section.");

builder.Services.AddHttpClient<IAlteredCardsClient, AlteredCardsClient>(
    http => http.BaseAddress = new Uri(externalHosts.CardsApiBase));
builder.Services.AddHostedService<CardCatalogRefreshService>();

builder.Services.AddOptions<KeycloakAdminOptions>()
    .Bind(builder.Configuration.GetSection(KeycloakAdminOptions.SectionName));
builder.Services.AddHttpClient<IKeycloakAdminClient, KeycloakAdminClient>(
    http => http.BaseAddress = new Uri(externalHosts.AuthBase));

builder.Services.AddOwnershipAuth(builder.Configuration, builder.Environment);

// Behind Traefik over plain HTTP: trust X-Forwarded-Proto/-For so OIDC redirect_uri,
// Secure cookies, and request.Scheme reflect the real HTTPS edge.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// CORS: open for third-party API consumers (Bearer + read-collection scope only).
// Cookie-protected endpoints stay same-origin: no Access-Control-Allow-Credentials.
builder.Services.AddCors(o => o.AddPolicy("PublicReadApi", p => p
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .WithMethods("GET")));

var app = builder.Build();

app.UseForwardedHeaders();

app.UseExceptionHandler();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        // Card artwork (Name/ImagePath from the cards.alteredcore.org catalog, shown
        // in the history page's event previews) is served from Altered's S3 bucket.
        // cdn.alteredcore.org is the Altered-Card-Renderer's own image/data source,
        // used to draw unique cards that have no catalog entry (history + boosters).
        "img-src 'self' data: https://altered-dev.s3.eu-west-3.amazonaws.com https://cdn.alteredcore.org; " +
        // cdn.alteredcore.org also serves the renderer's own fonts (HapticPro, alteredicons, ...);
        // data: covers its bundled Font Awesome Kit icon font, embedded as a base64 woff2.
        "font-src 'self' data: https://cdnjs.cloudflare.com https://cdn.jsdelivr.net https://cdn.alteredcore.org; " +
        // The alteredicons icon font is pulled in via a <link rel=stylesheet> the renderer
        // injects at runtime, from the same cdn.alteredcore.org host as img-src/font-src.
        "style-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com https://cdn.jsdelivr.net https://cdn.alteredcore.org; " +
        // Pinned to the exact file+commit the loader script requests (see CardRendererCommit
        // above and /js/card-renderer-loader.js) so an unrelated jsdelivr-hosted script
        // couldn't slip in here. The renderer also injects a small inline script the browser
        // reported by content hash — allow-listed below rather than 'unsafe-inline' so an
        // injected/reflected script still can't run. If new distinct hashes keep showing up
        // (e.g. one per card), that means the content is per-instance and this approach needs
        // revisiting.
        // The renderer lazy-loads a QR lib from Altered's own CDN for some cards; scoped to
        // that exact file rather than the whole cdn.alteredcore.org host.
        "script-src 'self' " + CardRendererScriptUrl +
        " https://cdn.alteredcore.org/forge/assets/vendor/qrcodejs/qrcode.min.js" +
        " 'sha256-rvoYWRsJWALEi7RxCTeJ1+9+IKP6fTcNUMZNVHddmoo=' " +
        "'sha256-PSbJmyvxZNzvRDi+D/Qj0klN/RcQ2sw/ms/fz2MzO6Y=' " +
        "'sha256-STS9SqU0XU6MD9z/R5Vuu133130JZh3udAlYzLFvda4='; " +
        // cards.alteredcore.org is the Altered-Card-Renderer's card-lookup API
        // (distinct from the CardsApiBase import client, which is server-only).
        "connect-src 'self' https://cdn.alteredcore.org https://cards.alteredcore.org; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        $"form-action 'self' {externalHosts.AuthBase}";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
    await db.Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();

// CSRF, secure by default. UseAntiforgery() validates endpoints the framework already
// flags (e.g. form/IFormFile uploads like import). The middleware below closes the gap:
// it validates every *other* cookie-authenticated unsafe request (JSON or no-body POSTs,
// logout, future write endpoints) so they're protected without per-endpoint wiring.
// Token-based (Bearer) callers aren't a CSRF vector, and any endpoint can opt out with
// .DisableAntiforgery() — which also marks it here as already-handled/opted-out.
app.UseAntiforgery();
app.Use(async (ctx, next) =>
{
    var method = ctx.Request.Method;
    var isUnsafe = !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method)
        && !HttpMethods.IsOptions(method) && !HttpMethods.IsTrace(method);

    if (isUnsafe && ctx.GetEndpoint() is { } endpoint)
    {
        // Any IAntiforgeryMetadata means UseAntiforgery() owns this endpoint
        // (RequiresValidation true) or it explicitly opted out (false).
        var handledByFramework = endpoint.Metadata.GetMetadata<IAntiforgeryMetadata>() is not null;
        var isBearer = ctx.Request.Headers.Authorization
            .Any(h => h is not null && h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase));

        if (!handledByFramework && !isBearer)
        {
            try
            {
                await ctx.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(ctx);
            }
            catch (AntiforgeryValidationException)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
        }
    }

    await next();
});

app.UseOutputCache();

app.MapAuthEndpoints();
app.MapCollectionEndpoints();
app.MapAdminEndpoints();
app.MapHistoryEndpoints();
app.MapBoosterEndpoints();
app.MapDefaultEndpoints();

// Surfaces a subset of ExternalHosts to the SPA so wwwroot/* stays env-agnostic.
app.MapGet("/config.js", (HttpResponse response, IOptions<ExternalHostsOptions> opts) =>
{
    var cfg = opts.Value;
    var json = JsonSerializer.Serialize(new
    {
        reunionWebBase = cfg.ReunionWebBase,
        authBase = cfg.AuthBase,
        cardRendererScriptUrl = CardRendererScriptUrl,
    });
    response.Headers.CacheControl = "public, max-age=300";
    return Results.Text($"window.AppConfig = {json};", "application/javascript");
});

app.UseFileServer();

app.Run();

// Expose the generated Program class for WebApplicationFactory<Program> in tests.
public partial class Program { }
