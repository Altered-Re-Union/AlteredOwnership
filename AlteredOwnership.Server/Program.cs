using System.Text.Json;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Domain.Services;
using AlteredOwnership.Server.Endpoints;
using AlteredOwnership.Server.Infrastructure.Auth;
using AlteredOwnership.Server.Infrastructure.Crypto;
using AlteredOwnership.Server.Infrastructure.EventSourcing;
using AlteredOwnership.Server.Infrastructure.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddRedisClientBuilder("cache").WithOutputCache();
builder.AddRedisDistributedCache("cache");
builder.AddNpgsqlDbContext<OwnershipDbContext>("ownershipdb");

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScoped<CurrentUserAccessor>();
builder.Services.AddScoped<CollectionReader>();
builder.Services.AddScoped<CollectionImporter>();
builder.Services.AddScoped<EventAppender>();

builder.Services.AddOptions<ExternalHostsOptions>()
    .Bind(builder.Configuration.GetSection(ExternalHostsOptions.SectionName));

builder.Services.AddOptions<EquinoxImportOptions>()
    .Bind(builder.Configuration.GetSection(EquinoxImportOptions.SectionName));

var externalHosts = builder.Configuration.GetSection(ExternalHostsOptions.SectionName).Get<ExternalHostsOptions>()
    ?? throw new InvalidOperationException("Missing 'ExternalHosts' configuration section.");

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
        $"img-src 'self' {externalHosts.ReunionCspSource} data:; " +
        $"font-src 'self' {externalHosts.ReunionCspSource} https://cdnjs.cloudflare.com https://cdn.jsdelivr.net; " +
        $"style-src 'self' 'unsafe-inline' {externalHosts.ReunionCspSource} https://cdnjs.cloudflare.com https://cdn.jsdelivr.net; " +
        "script-src 'self'; " +
        "connect-src 'self'; " +
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
app.UseOutputCache();

app.MapAuthEndpoints();
app.MapCollectionEndpoints();
app.MapDefaultEndpoints();

// Surfaces a subset of ExternalHosts to the SPA so wwwroot/* stays env-agnostic.
app.MapGet("/config.js", (HttpResponse response, IOptions<ExternalHostsOptions> opts) =>
{
    var cfg = opts.Value;
    var json = JsonSerializer.Serialize(new
    {
        reunionWebBase = cfg.ReunionWebBase,
        authBase = cfg.AuthBase,
    });
    response.Headers.CacheControl = "public, max-age=300";
    return Results.Text($"window.AppConfig = {json};", "application/javascript");
});

app.UseFileServer();

app.Run();

// Expose the generated Program class for WebApplicationFactory<Program> in tests.
public partial class Program { }
