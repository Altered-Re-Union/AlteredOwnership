using System.Text.Json;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Domain.Events;
using AlteredOwnership.Server.Infrastructure.EventSourcing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AlteredOwnership.Server.Domain.Services;

public class RewardService(EventAppender appender, OwnershipDbContext db, ProjectionReconciler reconciler)
{
    public Task RewardToUserAsync(Guid userId, string cardReference, int quantity, string acquiredFrom,
        CancellationToken ct) =>
        RewardBatchAsync([userId], [(cardReference, quantity)], [], acquiredFrom, ct);

    // Grants the same mix of fixed cards and booster types to every target as a
    // single all-or-nothing unit, one RewardEvent per user (never one event per
    // item): if any grant conflicts (e.g. a unique already owned), none of them are
    // applied — there's no risk of a crash or a mid-batch failure leaving it
    // ambiguous which targets in the batch actually received their reward.
    public async Task RewardBatchAsync(
        IReadOnlyList<Guid> userIds,
        IReadOnlyList<(string CardReference, int Quantity)> fixedCards,
        IReadOnlyList<(string BoosterTypeKey, int Quantity)> boosterGrants,
        string acquiredFrom, CancellationToken ct)
    {
        try
        {
            await appender.RunInTransactionAsync(async (batch, c) =>
            {
                foreach (var userId in userIds)
                    await AppendRewardAsync(batch, userId, fixedCards, boosterGrants, acquiredFrom, c);
            }, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: "23514", ConstraintName: "CK_CardOwnerships_UniqueQuantityOne" })
        {
            db.ChangeTracker.Clear();
            throw new DuplicateUniquesException(fixedCards.Select(g => g.CardReference).Distinct().ToList());
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: "23505", ConstraintName: "IX_CardOwnerships_CardReference" })
        {
            db.ChangeTracker.Clear();
            throw new ConflictingUniquesException(fixedCards.Select(g => g.CardReference).Distinct().ToList());
        }
    }

    private Task AppendRewardAsync(
        EventBatch batch, Guid userId,
        IReadOnlyList<(string CardReference, int Quantity)> fixedCards,
        IReadOnlyList<(string BoosterTypeKey, int Quantity)> boosterGrants,
        string acquiredFrom, CancellationToken ct)
    {
        var payload = RewardEvent.Build(
            fixedCards.Select(c => new RewardEvent.PayloadV1.Item(c.CardReference, c.Quantity)).ToList(),
            boosterGrants.Select(b => new RewardEvent.PayloadV1.BoosterItem(b.BoosterTypeKey, b.Quantity)).ToList(),
            acquiredFrom);
        var newEvent = new OwnershipEvent
        {
            UserId = userId,
            Kind = RewardEvent.Kind,
            Payload = JsonSerializer.SerializeToDocument(payload),
            PayloadHash = null,
            ExportedAt = DateTimeOffset.UtcNow,
        };
        return batch.AppendAsync(newEvent, (expected, c) => reconciler.ReconcileAsync(userId, expected, c), ct);
    }
}
