using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Domain.Events;

namespace AlteredOwnership.Server.Infrastructure.EventSourcing;

public static class EventDescriber
{
    public static EventDescription Describe(OwnershipEvent evt) => evt.Kind switch
    {
        EventKind.EquinoxImport => EquinoxImportEvent.Describe(evt.Payload),
        EventKind.RewardEvent => RewardEvent.Describe(evt.Payload),
        EventKind.BoosterOpened => BoosterOpenedEvent.Describe(evt.Payload),
        _ => throw new NotSupportedException($"Unknown event kind {evt.Kind}"),
    };
}
