using System.Diagnostics;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Endpoints;
using AlteredOwnership.Server.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlteredOwnership.Server.Domain.Services;

// apply-to-deck is called by BGA's gateway under a 1-2 s overall budget, so the first
// request after a start can't afford the one-off costs a warm process has already paid:
// the Keycloak discovery document + signing keys (JwtBearer fetches them lazily on the
// first token), the catalog snapshot, EF's compilation of each query shape, and the first
// pooled database connection. This runs all of them once, in the background right after
// startup, using the real code paths — a throwaway user id that matches no row, so
// nothing is read or written for real. Failures only log: a step that fails here simply
// stays lazy and is paid by the first request exactly as it was before.
public sealed class AltArtWarmupService(
    CardArtCatalogCache catalogCache,
    IServiceScopeFactory scopes,
    IOptionsMonitor<JwtBearerOptions> jwtOptions,
    IConfiguration configuration,
    ILogger<AltArtWarmupService> logger) : BackgroundService
{
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Warmup:Enabled", true))
            return;

        // Independent steps, so a slow Keycloak doesn't delay the database ones.
        await Task.WhenAll(
            Step("catalog + queries", WarmDatabaseAsync, stoppingToken),
            Step("OIDC metadata", WarmOidcAsync, stoppingToken));
    }

    private async Task Step(string name, Func<CancellationToken, Task> run, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await run(ct);
                logger.LogInformation("Warm-up '{Step}' done in {Elapsed} ms.", name, sw.ElapsedMilliseconds);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (attempt >= RetryDelays.Length)
                {
                    logger.LogWarning(ex, "Warm-up '{Step}' failed; it will be paid by the first request instead.", name);
                    return;
                }

                logger.LogInformation("Warm-up '{Step}' failed ({Reason}); retrying.", name, ex.GetType().Name);
                await Task.Delay(RetryDelays[attempt], ct);
            }
        }
    }

    private async Task WarmDatabaseAsync(CancellationToken ct)
    {
        var catalog = await catalogCache.GetAsync(ct);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
        var altArts = scope.ServiceProvider.GetRequiredService<AltArtService>();

        // The user lookup apply-to-deck's endpoint runs (CurrentUserAccessor.GetOrProvisionAsync).
        _ = await db.Users
            .Where(u => u.KeycloakId == "warmup-no-such-user")
            .Select(u => new CurrentUser(u.Id, u.AltArtPreferenceMode))
            .FirstOrDefaultAsync(ct);

        // Preferences + ownership queries, through the real service, with a tracked print
        // so the ownership query isn't skipped; Global mode runs the same two queries.
        var tracked = catalog.ByReference.Values.FirstOrDefault(c => !catalog.IsInfinite(c));
        if (tracked is not null)
        {
            _ = await altArts.ApplyToDeckAsync(
                Guid.Empty, AltArtPreferenceMode.PerDeck, [new OwnershipCheckItem(tracked.Reference, 1)], ct);
        }
    }

    private async Task WarmOidcAsync(CancellationToken ct)
    {
        var manager = jwtOptions.Get(AuthConstants.BearerScheme).ConfigurationManager;
        if (manager is null)
            return;

        // Fetches the discovery document and the signing keys, and caches them in the
        // handler's own ConfigurationManager — the one every Bearer token is validated with.
        _ = await manager.GetConfigurationAsync(ct);
    }
}
