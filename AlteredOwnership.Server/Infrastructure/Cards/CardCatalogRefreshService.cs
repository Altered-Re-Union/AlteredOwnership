using AlteredOwnership.Server.Domain.Services;

namespace AlteredOwnership.Server.Infrastructure.Cards;

// Hourly safety net: re-fetches every owned-but-uncatalogued card reference, recovering any
// whose import-time backfill failed (e.g. the catalog API was briefly unavailable). The live
// path is the import-time backfill; this just sweeps the gaps once an hour.
public class CardCatalogRefreshService(
    IServiceScopeFactory scopeFactory,
    ILogger<CardCatalogRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RefreshAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // host is shutting down
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var backfiller = scope.ServiceProvider.GetRequiredService<CardMetadataBackfiller>();
            await backfiller.BackfillMissingAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Hourly card catalog refresh failed.");
        }
    }
}
