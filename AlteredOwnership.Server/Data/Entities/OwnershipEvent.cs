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

    public DateTimeOffset CreatedAt { get; set; }
}
