namespace AlteredOwnership.Server.Infrastructure.EventSourcing;

// Everything a user's full event history folds into. Card references -> quantity
// owned, booster type keys -> quantity unopened. Kept together so a single event
// (e.g. opening a booster) can mutate both in one Apply call.
public class ProjectionState
{
    public Dictionary<string, int> Cards { get; } = new();

    public Dictionary<string, int> Boosters { get; } = new();
}
