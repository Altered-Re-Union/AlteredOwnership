using System.Text.Json;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Domain.Events;
using AlteredOwnership.Server.Infrastructure.EventSourcing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AlteredOwnership.Server.Domain.Services;

public class RewardService(EventAppender appender, OwnershipDbContext db, UniqueStockService stock)
{
    public Task RewardToUserAsync(Guid userId, string cardReference, int quantity, string acquiredFrom,
        CancellationToken ct) =>
        RewardManyAsync([(userId, cardReference, quantity)], acquiredFrom, ct);

    // Grants every (user, card, quantity) tuple as a single all-or-nothing unit: if
    // any grant conflicts (e.g. a unique already owned), none of them are applied —
    // there's no risk of a crash or a mid-batch failure leaving it ambiguous which
    // targets in the batch actually received their card.
    public async Task RewardManyAsync(
        IReadOnlyList<(Guid UserId, string CardReference, int Quantity)> grants,
        string acquiredFrom, CancellationToken ct)
    {
        try
        {
            await appender.RunInTransactionAsync(async (batch, c) =>
            {
                foreach (var (userId, cardReference, quantity) in grants)
                    await AppendRewardAsync(batch, userId, cardReference, quantity, acquiredFrom, c);
            }, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: "23514", ConstraintName: "CK_CardOwnerships_UniqueQuantityOne" })
        {
            db.ChangeTracker.Clear();
            throw new DuplicateUniquesException(grants.Select(g => g.CardReference).Distinct().ToList());
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: "23505", ConstraintName: "IX_CardOwnerships_CardReference" })
        {
            db.ChangeTracker.Clear();
            throw new ConflictingUniquesException(grants.Select(g => g.CardReference).Distinct().ToList());
        }
    }

    // Draws `quantityPerUser` random uniques (optionally restricted to a set) for each
    // target and grants them, all inside one transaction: if stock runs out or a draw
    // conflicts partway through, every reservation and grant made so far in this call —
    // for every target — rolls back too. Returns the references granted per user.
    public async Task<Dictionary<Guid, List<string>>> RewardRandomUniquesAsync(
        IReadOnlyList<Guid> userIds, string? set, int quantityPerUser, string acquiredFrom, CancellationToken ct)
    {
        try
        {
            return await appender.RunInTransactionAsync(async (batch, c) =>
            {
                var granted = userIds.ToDictionary(id => id, _ => new List<string>());
                foreach (var userId in userIds)
                {
                    for (var i = 0; i < quantityPerUser; i++)
                    {
                        var reference = await stock.ReserveRandomAsync(set, c);
                        await AppendRewardAsync(batch, userId, reference, 1, acquiredFrom, c);
                        granted[userId].Add(reference);
                    }
                }
                return granted;
            }, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: "23514" or "23505",
            ConstraintName: "CK_CardOwnerships_UniqueQuantityOne" or "IX_CardOwnerships_CardReference"
        })
        {
            // A freshly-reserved unique conflicting with an existing CardOwnership row
            // means UniqueCardStock is out of sync with actual ownership — rare enough
            // (and not attributable to one specific draw) that a generic message is fine.
            db.ChangeTracker.Clear();
            throw new ConflictingUniquesException([]);
        }
    }

    private Task AppendRewardAsync(
        EventBatch batch, Guid userId, string cardReference, int quantity, string acquiredFrom, CancellationToken ct)
    {
        var payload = RewardEvent.Build(cardReference, quantity, acquiredFrom);
        var newEvent = new OwnershipEvent
        {
            UserId = userId,
            Kind = RewardEvent.Kind,
            Payload = JsonSerializer.SerializeToDocument(payload),
            PayloadHash = null,
            ExportedAt = DateTimeOffset.UtcNow,
        };
        return batch.AppendAsync(newEvent, (expected, c) => ReconcileCardOwnershipAsync(userId, expected, c), ct);
    }

    public async Task ReconcileCardOwnershipAsync(Guid userId, Dictionary<string, int> expectedState,
        CancellationToken ct)
    {
        var currentRows = await db.CardOwnerships
            .Where(c => c.UserId == userId)
            .ToDictionaryAsync(c => c.CardReference, ct);

        foreach (var (reference, quantity) in expectedState)
        {
            if (currentRows.Remove(reference, out var row))
            {
                row.Quantity = quantity;
            }
            else
            {
                db.CardOwnerships.Add(new CardOwnership
                {
                    UserId = userId,
                    CardReference = reference,
                    Quantity = quantity,
                    IsUnique = CardReferenceParser.IsUnique(reference)
                });
            }
        }
    }
}
