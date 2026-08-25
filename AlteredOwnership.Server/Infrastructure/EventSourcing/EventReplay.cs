using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Domain.Events;

namespace AlteredOwnership.Server.Infrastructure.EventSourcing;

public static class EventReplay
{
    public static ProjectionState ReplayAll(IEnumerable<OwnershipEvent> events)
    {
        var state = new ProjectionState();
        foreach (var evt in events)
            Apply(state, evt);
        return state;
    }

    public static void Apply(ProjectionState state, OwnershipEvent evt)
    {
        switch (evt.Kind)
        {
            case EventKind.EquinoxImport:
                EquinoxImportEvent.Apply(state, evt.Payload);
                break;

            case EventKind.RewardEvent:
                RewardEvent.Apply(state, evt.Payload);
                break;

            case EventKind.BoosterOpened:
                BoosterOpenedEvent.Apply(state, evt.Payload);
                break;

            default:
                throw new NotSupportedException($"Unknown event kind {evt.Kind}");
        }
    }
}
