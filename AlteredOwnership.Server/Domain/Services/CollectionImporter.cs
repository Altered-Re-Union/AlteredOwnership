using System.Text.Json;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Domain.Events;
using AlteredOwnership.Server.Infrastructure.EventSourcing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AlteredOwnership.Server.Domain.Services;

public class DuplicateUniquesException(IReadOnlyList<string> references)
    : Exception("One or more uniques in the import are already owned.")
{
    public IReadOnlyList<string> References { get; } = references;
}

public class ConflictingUniquesException(IReadOnlyList<string> references)
    : Exception("One or more uniques in the import are already owned by another player.")
{
    public IReadOnlyList<string> References { get; } = references;
}

public class CollectionImporter(EventAppender appender, OwnershipDbContext db)
{
    public async Task ImportAsync(Guid userId, EquinoxImportEvent.PayloadV1 payload, CancellationToken ct)
    {
        var newEvent = new OwnershipEvent
        {
            UserId = userId,
            Kind = EquinoxImportEvent.Kind,
            Payload = JsonSerializer.SerializeToDocument(payload),
            PayloadHash = EquinoxImportEvent.ComputeHash(payload),
        };
        try
        {
            await appender.AppendAsync(
                newEvent,
                (expected, c) => ReconcileCardOwnershipsAsync(userId, expected, c),
                ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: "23514", ConstraintName: "CK_CardOwnerships_UniqueQuantityOne" })
        {
            db.ChangeTracker.Clear();
            var newUniqueRefs = payload.Cards
                .Where(c => CardReferenceParser.IsUnique(c.Reference))
                .Select(c => c.Reference)
                .ToHashSet();
            var alreadyOwned = await db.CardOwnerships
                .Where(c => c.UserId == userId && newUniqueRefs.Contains(c.CardReference))
                .Select(c => c.CardReference)
                .ToListAsync(ct);
            throw new DuplicateUniquesException(alreadyOwned);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: "23505", ConstraintName: "IX_CardOwnerships_CardReference" })
        {
            db.ChangeTracker.Clear();
            var newUniqueRefs = payload.Cards
                .Where(c => CardReferenceParser.IsUnique(c.Reference))
                .Select(c => c.Reference)
                .ToHashSet();
            var ownedByOthers = await db.CardOwnerships
                .Where(c => c.UserId != userId && newUniqueRefs.Contains(c.CardReference))
                .Select(c => c.CardReference)
                .ToListAsync(ct);
            throw new ConflictingUniquesException(ownedByOthers);
        }
    }

    private async Task ReconcileCardOwnershipsAsync(
        Guid userId,
        Dictionary<string, int> expectedState,
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
                    IsUnique = CardReferenceParser.IsUnique(reference),
                });
            }
        }

        foreach (var orphan in currentRows.Values)
            db.CardOwnerships.Remove(orphan);
    }
}
