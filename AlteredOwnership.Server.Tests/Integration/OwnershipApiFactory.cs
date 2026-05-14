using AlteredOwnership.Server.Auth;
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

        builder.ConfigureTestServices(services =>
        {
            // Replace the Redis-backed distributed cache so tests need no Redis container.
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache>(new MemoryDistributedCache(
                Options.Create(new MemoryDistributedCacheOptions())));

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
            });
        });
    }
}
