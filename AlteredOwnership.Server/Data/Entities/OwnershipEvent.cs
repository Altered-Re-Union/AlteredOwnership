using System.Text.Json;
using AlteredOwnership.Server.Events;

namespace AlteredOwnership.Server.Data.Entities;

public class OwnershipEvent
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public int UserEventId { get; set; }

    public EventKind Kind { get; set; }

    public JsonDocument Payload { get; set; } = default!;

    // Globally unique fingerprint of the event content, set only when the event
    // kind needs deduplication (e.g. Equinox imports). Enforced by a partial
    // unique index where PayloadHash IS NOT NULL.
    public string? PayloadHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
