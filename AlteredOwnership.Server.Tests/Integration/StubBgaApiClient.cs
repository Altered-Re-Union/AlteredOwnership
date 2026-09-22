using AlteredOwnership.Server.Infrastructure.Bga;

namespace AlteredOwnership.Server.Tests.Integration;

// Configurable test double for IBgaApiClient -- the real client hits the live
// altered-bga-api gateway tests have no credentials for.
public class StubBgaApiClient : IBgaApiClient
{
    private readonly Dictionary<string, string> _reunionIdsByBgaName = new();

    public StubBgaApiClient KnownPlayer(string bgaName, string reunionId)
    {
        _reunionIdsByBgaName[bgaName] = reunionId;
        return this;
    }

    public Task<string?> ResolveReunionIdAsync(string bgaName, CancellationToken ct) =>
        Task.FromResult(_reunionIdsByBgaName.GetValueOrDefault(bgaName));
}
