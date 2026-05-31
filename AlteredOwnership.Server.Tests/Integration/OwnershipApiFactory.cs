using AlteredOwnership.Server.Infrastructure.Auth;
using AlteredOwnership.Server.Infrastructure.Cards;
using AlteredOwnership.Server.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace AlteredOwnership.Server.Tests.Integration;

public class OwnershipApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // libsodium secretbox key tests encrypt the collection with; the import endpoint
    // is configured to decrypt with the same key below.
    public const string DecryptionKeyHex = "";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OwnershipDbContext>()
            .Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:ownershipdb", _postgres.GetConnectionString());
        builder.UseSetting("EquinoxImport:DecryptionKeyHex", DecryptionKeyHex);

        builder.ConfigureTestServices(services =>
        {
            // Replace the Redis-backed distributed cache so tests need no Redis container.
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache>(new MemoryDistributedCache(
                Options.Create(new MemoryDistributedCacheOptions())));

            // No network by default: import-triggered card backfill gets a no-op catalog
            // client. Tests that exercise backfill override this with their own stub.
            services.RemoveAll<IAlteredCardsClient>();
            services.AddSingleton<IAlteredCardsClient>(new NullCardsClient());

            // Bypass OIDC/Bearer: always authenticate as a fixed test user with both scopes.
            services.AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            // Production policies pin to CookieScheme/BearerScheme; repoint them to the test scheme.
            services.PostConfigure<AuthorizationOptions>(opt =>
            {
                opt.AddPolicy(AuthConstants.ImportPolicy, p => p
                    .AddAuthenticationSchemes(TestAuthHandler.SchemeName)
                    .RequireAuthenticatedUser());
                opt.AddPolicy(AuthConstants.ReadPolicy, p => p
                    .AddAuthenticationSchemes(TestAuthHandler.SchemeName)
                    .RequireAuthenticatedUser());
                opt.AddPolicy(AuthConstants.SessionPolicy, p => p
                    .AddAuthenticationSchemes(TestAuthHandler.SchemeName)
                    .RequireAuthenticatedUser());
            });
        });
    }
}
