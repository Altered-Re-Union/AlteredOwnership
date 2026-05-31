using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Infrastructure.Cards;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AlteredOwnership.Server.Domain.Services;

// Populates the shared Card catalog from cards.alteredcore.org for references the player
// owns but that aren't catalogued yet. Runs AFTER the import transaction commits and is
// best-effort: a failing external API never fails the import — the ownership projection is
// the source of truth, and missing metadata is backfilled on a later import.
public class CardMetadataBackfiller(
    OwnershipDbContext db,
    IAlteredCardsClient client,
    ILogger<CardMetadataBackfiller> logger)
{
    private const int BatchSize = 100; // the catalog API caps a batch at 200 references

    public async Task BackfillAsync(Guid userId, CancellationToken ct)
    {
        var missing = await db.CardOwnerships
            .Where(co => co.UserId == userId && !db.Cards.Any(c => c.Reference == co.CardReference))
            .Select(co => co.CardReference)
            .ToListAsync(ct);

        if (missing.Count == 0)
            return;

        try
        {
            var cards = await FetchAsync(missing, ct);
            await UpsertAsync(cards, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Card metadata backfill failed for {Count} references; will retry on next import.", missing.Count);
        }
    }

    // One call per chunk with no locale, so the API returns every language at once.
    private async Task<List<Card>> FetchAsync(IReadOnlyList<string> references, CancellationToken ct)
    {
        var cards = new List<Card>();
        foreach (var chunk in references.Chunk(BatchSize))
        {
            var dtos = await client.FetchBatchAsync(chunk, ct);
            cards.AddRange(dtos.Select(ToCard));
        }
        return cards;
    }

    private async Task UpsertAsync(IReadOnlyList<Card> fetched, CancellationToken ct)
    {
        var pending = fetched.ToDictionary(c => c.Reference);

        // Insert the still-missing rows. A concurrent import may insert the same reference
        // between our existence check and SaveChanges, so on a conflict we re-check what now
        // exists and retry with the remainder. Each real conflict shrinks `pending`, so this
        // terminates.
        while (pending.Count > 0)
        {
            db.ChangeTracker.Clear();
            var present = await db.Cards
                .Where(c => pending.Keys.Contains(c.Reference))
                .Select(c => c.Reference)
                .ToListAsync(ct);
            foreach (var reference in present)
                pending.Remove(reference);

            if (pending.Count == 0)
                break;

            db.Cards.AddRange(pending.Values);
            try
            {
                await db.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
            {
                // Lost a race on at least one Reference PK; loop to recompute the remainder.
            }
        }
    }

    private static Card ToCard(CardDto dto) => new()
    {
        Reference = dto.Reference,
        Name = dto.Name ?? new(),
        ImagePath = dto.ImagePath ?? new(),
        Set = dto.Set?.Reference ?? "",
        Faction = dto.Faction?.Code ?? "",
        Rarity = dto.Rarity?.Reference ?? "",
        CardType = dto.CardType?.Reference ?? "",
        Variation = dto.Variation ?? "",
        SubTypes = dto.CardSubTypes?
            .Select(s => s.Reference)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList() ?? [],
        IsBanned = dto.IsBanned,
        IsSuspended = dto.IsSuspended,
        MainCost = dto.MainCost,
        RecallCost = dto.RecallCost,
        Forest = dto.ForestPower,
        Mountain = dto.MountainPower,
        Ocean = dto.OceanPower,
    };
}
