namespace AlteredOwnership.Server.Domain.Events;

// What a reference in an EventItemDelta names — a Cards-catalog reference, or a
// BoosterCatalog key. History rendering resolves name/image against the matching
// catalog based on this tag instead of guessing from the reference's shape.
public enum EventItemKind { Card, Booster }

// A single item's contribution to an event, signed: positive = received,
// negative = given away. No event kind currently produces negative deltas for
// cards, but the history display (and this shape) already supports it.
public record EventItemDelta(string Reference, int Quantity, EventItemKind Kind = EventItemKind.Card);

// What an event's own payload says happened, independent of the account's running
// projection state — used to describe a single event for the history page, as
// opposed to Apply's job of folding it into the cumulative projection totals.
public record EventDescription(string Name, IReadOnlyList<EventItemDelta> Items);
