using System.Text.Json;

namespace AlteredOwnership.Server.Data.Entities;

public enum EventKind
{
    EquinoxImport = 1,
}

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

    // Timestamp the source export was generated at (read from the CSV), kept as
    // event metadata rather than inside the payload.
    public DateTimeOffset ExportedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
