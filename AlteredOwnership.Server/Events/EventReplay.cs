using AlteredOwnership.Server.Data.Entities;

namespace AlteredOwnership.Server.Events;

public static class EventReplay
{
    public static Dictionary<string, int> ReplayAll(IEnumerable<OwnershipEvent> events)
    {
        var state = new Dictionary<string, int>();
        foreach (var evt in events)
            Apply(state, evt);
        return state;
    }

    public static void Apply(Dictionary<string, int> state, OwnershipEvent evt)
    {
        switch (evt.Kind)
        {
            case EventKind.EquinoxImport:
                EquinoxImportEvent.Apply(state, evt.Payload);
                break;

            default:
                throw new NotSupportedException($"Unknown event kind {evt.Kind}");
        }
    }
}
