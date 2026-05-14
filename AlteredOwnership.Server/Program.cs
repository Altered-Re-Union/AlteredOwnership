using AlteredOwnership.Server.Auth;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Endpoints;
using AlteredOwnership.Server.Services;
using Microsoft.EntityFrameworkCore;

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
builder.Services.AddScoped<CollectionWriter>();

builder.Services.AddOwnershipAuth(builder.Configuration, builder.Environment);

// CORS: open for third-party API consumers (Bearer + read-collection scope only).
// Cookie-protected endpoints stay same-origin: no Access-Control-Allow-Credentials.
builder.Services.AddCors(o => o.AddPolicy("PublicReadApi", p => p
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .WithMethods("GET")));

var app = builder.Build();

app.UseExceptionHandler();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "img-src 'self' https://*.altered-reunion.com data:; " +
        "font-src 'self' https://*.altered-reunion.com https://cdnjs.cloudflare.com https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline' https://*.altered-reunion.com https://cdnjs.cloudflare.com https://cdn.jsdelivr.net; " +
        "script-src 'self' https://cdn.jsdelivr.net; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self' https://auth.altered.re";
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
app.UseFileServer();

app.Run();

// Expose the generated Program class for WebApplicationFactory<Program> in tests.
public partial class Program { }
