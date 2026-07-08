using System.Text.Json;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Domain.Events;
using AlteredOwnership.Server.Infrastructure.EventSourcing;
using Microsoft.EntityFrameworkCore;

namespace AlteredOwnership.Server.Domain.Services;

public class RewardService(EventAppender appender, OwnershipDbContext db)
{
    public Task RewardToUserAsync(Guid userId, string cardReference, int quantity, string acquiredFrom,
        CancellationToken ct)
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

        return appender.AppendAsync(
            newEvent,
            (expected, c) => ReconcileCardOwnershipAsync(userId, expected, c),
            ct);
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