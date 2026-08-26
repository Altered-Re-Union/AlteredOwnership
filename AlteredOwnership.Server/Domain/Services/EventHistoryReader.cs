using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Domain.Boosters;
using AlteredOwnership.Server.Domain.Events;
using AlteredOwnership.Server.Infrastructure.EventSourcing;
using Microsoft.EntityFrameworkCore;
using AlteredOwnership.Server.Domain;

namespace AlteredOwnership.Server.Domain.Services;

public record EventCardPreview(string Reference, int Quantity, string? Name, string? ImagePath, bool IsUnique, bool IsBooster);

// CardCount is the number of distinct card (non-booster) line items, not a quantity sum —
// the history page uses it to decide whether a row has exactly one card to jump straight to
// (skipping the detail modal) versus needing the modal to pick among several.
// Cards/boosters are tallied separately (rather than one generic Received/Given) because a
// reward can grant boosters directly without opening them — collapsing the two kinds together
// made a +2 boosters grant render as "+2 cards" on the history page.
// Kind mirrors OwnershipEvent.Kind (e.g. "BoosterOpened") so the frontend can special-case
// how a row renders (booster-opened rows already name the booster in their title).
public record EventSummaryResponse(
    long Id, string Name, DateTimeOffset CreatedAt, string Kind,
    int CardsReceived, int CardsGiven, int BoostersReceived, int BoostersGiven, int CardCount,
    IReadOnlyList<EventCardPreview> Preview);

public record EventCardLine(string Reference, int Quantity, string? Name, string? ImagePath, bool IsUnique, bool IsBooster);

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
            descriptions.SelectMany(d => d.Items).Where(i => i.Kind == EventItemKind.Card)
                .Select(i => i.Reference).Distinct().ToList(), ct);

        return events.Zip(descriptions, (evt, description) =>
        {
            var cardsReceived = description.Items.Where(i => i.Kind == EventItemKind.Card && i.Quantity > 0).Sum(i => i.Quantity);
            var cardsGiven = -description.Items.Where(i => i.Kind == EventItemKind.Card && i.Quantity < 0).Sum(i => i.Quantity);
            var boostersReceived = description.Items.Where(i => i.Kind == EventItemKind.Booster && i.Quantity > 0).Sum(i => i.Quantity);
            var boostersGiven = -description.Items.Where(i => i.Kind == EventItemKind.Booster && i.Quantity < 0).Sum(i => i.Quantity);
            var cardCount = description.Items.Count(i => i.Kind == EventItemKind.Card);
            var preview = description.Items
                .Take(PreviewCount)
                .Select(i => ToPreview(i, catalog, locale))
                .ToList();
            return new EventSummaryResponse(
                evt.Id, description.Name, evt.CreatedAt, evt.Kind.ToString(),
                cardsReceived, cardsGiven, boostersReceived, boostersGiven, cardCount, preview);
        }).ToList();
    }

    public async Task<EventDetailResponse?> GetDetailAsync(Guid userId, long eventId, string locale, CancellationToken ct)
    {
        var evt = await db.OwnershipEvents.FirstOrDefaultAsync(e => e.UserId == userId && e.Id == eventId, ct);
        if (evt is null) return null;

        var description = EventDescriber.Describe(evt);
        var catalog = await LoadCatalogAsync(
            description.Items.Where(i => i.Kind == EventItemKind.Card).Select(i => i.Reference).Distinct().ToList(), ct);

        EventCardLine ToLine(EventItemDelta item, int quantity)
        {
            var preview = ToPreview(item with { Quantity = quantity }, catalog, locale);
            return new EventCardLine(preview.Reference, preview.Quantity, preview.Name, preview.ImagePath, preview.IsUnique, preview.IsBooster);
        }

        var received = description.Items.Where(i => i.Quantity > 0).Select(i => ToLine(i, i.Quantity)).ToList();
        var given = description.Items.Where(i => i.Quantity < 0).Select(i => ToLine(i, -i.Quantity)).ToList();

        return new EventDetailResponse(evt.Id, description.Name, evt.CreatedAt, received, given);
    }

    private static EventCardPreview ToPreview(EventItemDelta item, Dictionary<string, Card> catalog, string locale)
    {
        if (item.Kind == EventItemKind.Booster)
        {
            var boosterType = BoosterCatalog.Find(item.Reference);
            return new EventCardPreview(item.Reference, item.Quantity, boosterType?.Name, boosterType?.ImagePath, false, true);
        }

        var card = catalog.GetValueOrDefault(item.Reference);
        return new EventCardPreview(
            item.Reference, item.Quantity,
            CardLocalization.Localize(card?.Name, locale),
            CardLocalization.Localize(card?.ImagePath, locale),
            CardReferenceParser.IsUnique(item.Reference), false);
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
