using System.Text.Json;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Domain.Boosters;
using AlteredOwnership.Server.Domain.Events;
using AlteredOwnership.Server.Infrastructure.EventSourcing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AlteredOwnership.Server.Domain.Services;

public class UnknownBoosterTypeException(string boosterTypeKey)
    : Exception($"Unknown booster type '{boosterTypeKey}'.")
{
    public string BoosterTypeKey { get; } = boosterTypeKey;
}

public class NoBoosterAvailableException(string boosterTypeKey)
    : Exception($"No unopened booster of type '{boosterTypeKey}' available.")
{
    public string BoosterTypeKey { get; } = boosterTypeKey;
}

public class BoosterService(EventAppender appender, OwnershipDbContext db, UniqueStockService stock, ProjectionReconciler reconciler)
{
    // Opens `quantity` boosters of one type for one user as a single all-or-nothing
    // event: every card drawn (and the whole booster-count decrement) lands in one
    // BoosterOpenedEvent, never one event per booster or per card. The booster
    // inventory's own non-negative check constraint is what actually rejects an
    // over-open — no separate pre-check needed, and rolling back the transaction on
    // that violation also undoes the unique-stock draws made along the way.
    public async Task<IReadOnlyList<string>> OpenAsync(Guid userId, string boosterTypeKey, int quantity, CancellationToken ct)
    {
        var type = BoosterCatalog.Find(boosterTypeKey) ?? throw new UnknownBoosterTypeException(boosterTypeKey);

        try
        {
            return await appender.RunInTransactionAsync(async (batch, c) =>
            {
                var cardReferences = new List<string>();
                for (var i = 0; i < quantity; i++)
                    cardReferences.Add(await stock.ReserveRandomAsync(type.Set, type.Faction, c));

                var payload = BoosterOpenedEvent.Build(boosterTypeKey, quantity, cardReferences);
                var newEvent = new OwnershipEvent
                {
                    UserId = userId,
                    Kind = BoosterOpenedEvent.Kind,
                    Payload = JsonSerializer.SerializeToDocument(payload),
                    PayloadHash = null,
                    ExportedAt = DateTimeOffset.UtcNow,
                };
                await batch.AppendAsync(newEvent, (expected, c2) => reconciler.ReconcileAsync(userId, expected, c2), c);
                return (IReadOnlyList<string>)cardReferences;
            }, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: "23514", ConstraintName: "CK_BoosterInventories_QuantityNonNegative" })
        {
            db.ChangeTracker.Clear();
            throw new NoBoosterAvailableException(boosterTypeKey);
        }
    }
}
