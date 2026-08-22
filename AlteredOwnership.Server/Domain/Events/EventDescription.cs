namespace AlteredOwnership.Server.Domain.Events;

// A single card's contribution to an event, signed: positive = received,
// negative = given away. No event kind currently produces negative deltas, but the
// history display (and this shape) already supports it.
public record EventItemDelta(string Reference, int Quantity);

// What an event's own payload says happened, independent of the account's running
// projection state — used to describe a single event for the history page, as
// opposed to Apply's job of folding it into the cumulative CardOwnership totals.
public record EventDescription(string Name, IReadOnlyList<EventItemDelta> Items);
