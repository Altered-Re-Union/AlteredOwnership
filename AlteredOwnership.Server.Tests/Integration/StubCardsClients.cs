using AlteredOwnership.Server.Infrastructure.Cards;

namespace AlteredOwnership.Server.Tests.Integration;

// Default catalog client for tests: returns nothing and makes no network calls.
public sealed class NullCardsClient : IAlteredCardsClient
{
    public Task<IReadOnlyList<CardDto>> FetchBatchAsync(
        IReadOnlyCollection<string> references, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CardDto>>([]);
}

// Records the references requested per call and returns canned cards, or throws to exercise
// best-effort backfill. `Throws` is mutable so a single instance can simulate an outage on a
// first import then recover on a second.
public sealed class RecordingCardsClient(
    Func<IReadOnlyCollection<string>, IReadOnlyList<CardDto>>? respond = null)
    : IAlteredCardsClient
{
    private readonly Func<IReadOnlyCollection<string>, IReadOnlyList<CardDto>> _respond =
        respond ?? (_ => []);

    public List<string[]> Calls { get; } = [];
    public bool Throws { get; set; }

    public Task<IReadOnlyList<CardDto>> FetchBatchAsync(
        IReadOnlyCollection<string> references, CancellationToken ct)
    {
        Calls.Add(references.ToArray());
        if (Throws) throw new HttpRequestException("simulated catalog API outage");
        return Task.FromResult(_respond(references));
    }
}
