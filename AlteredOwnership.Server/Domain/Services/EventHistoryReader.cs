using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Domain.Events;
using AlteredOwnership.Server.Infrastructure.EventSourcing;
using Microsoft.EntityFrameworkCore;

namespace AlteredOwnership.Server.Domain.Services;

public record EventCardPreview(string Reference, int Quantity, string? Name, string? ImagePath);

public record EventSummaryResponse(
    long Id, string Name, DateTimeOffset CreatedAt, int Received, int Given,
    IReadOnlyList<EventCardPreview> Preview);

public record EventCardLine(string Reference, int Quantity, string? Name, string? ImagePath);

public record EventDetailResponse(
    long Id, string Name, DateTimeOffset CreatedAt,
    IReadOnlyList<EventCardLine> Received, IReadOnlyList<EventCardLine> Given);

public class EventHistoryReader(OwnershipDbContext db)
{
    private const int PreviewCount = 3;

    public async Task<IReadOnlyList<EventSummaryResponse>> ListAsync(Guid userId, string locale, CancellationToken ct)
    {
        var events = await db.OwnershipEvents
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.UserEventId)
            .ToListAsync(ct);

        var descriptions = events.Select(EventDescriber.Describe).ToList();
        var catalog = await LoadCatalogAsync(
            descriptions.SelectMany(d => d.Items.Select(i => i.Reference)).Distinct().ToList(), ct);

        return events.Zip(descriptions, (evt, description) =>
        {
            var received = description.Items.Where(i => i.Quantity > 0).Sum(i => i.Quantity);
            var given = -description.Items.Where(i => i.Quantity < 0).Sum(i => i.Quantity);
            var preview = description.Items
                .Take(PreviewCount)
                .Select(i => new EventCardPreview(
                    i.Reference, i.Quantity,
                    CardLocalization.Localize(catalog.GetValueOrDefault(i.Reference)?.Name, locale),
                    CardLocalization.Localize(catalog.GetValueOrDefault(i.Reference)?.ImagePath, locale)))
                .ToList();
            return new EventSummaryResponse(evt.Id, description.Name, evt.CreatedAt, received, given, preview);
        }).ToList();
    }

    public async Task<EventDetailResponse?> GetDetailAsync(Guid userId, long eventId, string locale, CancellationToken ct)
    {
        var evt = await db.OwnershipEvents.FirstOrDefaultAsync(e => e.UserId == userId && e.Id == eventId, ct);
        if (evt is null) return null;

        var description = EventDescriber.Describe(evt);
        var catalog = await LoadCatalogAsync(description.Items.Select(i => i.Reference).Distinct().ToList(), ct);

        EventCardLine ToLine(EventItemDelta item, int quantity) => new(
            item.Reference, quantity,
            CardLocalization.Localize(catalog.GetValueOrDefault(item.Reference)?.Name, locale),
            CardLocalization.Localize(catalog.GetValueOrDefault(item.Reference)?.ImagePath, locale));

        var received = description.Items.Where(i => i.Quantity > 0).Select(i => ToLine(i, i.Quantity)).ToList();
        var given = description.Items.Where(i => i.Quantity < 0).Select(i => ToLine(i, -i.Quantity)).ToList();

        return new EventDetailResponse(evt.Id, description.Name, evt.CreatedAt, received, given);
    }

    private async Task<Dictionary<string, Card>> LoadCatalogAsync(IReadOnlyList<string> references, CancellationToken ct)
    {
        if (references.Count == 0) return new Dictionary<string, Card>();
        return await db.Cards
            .Where(c => references.Contains(c.Reference))
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Reference, ct);
    }
}
