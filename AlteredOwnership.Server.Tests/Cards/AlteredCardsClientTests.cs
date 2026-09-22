using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AlteredOwnership.Server.Infrastructure.Cards;

namespace AlteredOwnership.Server.Tests.Cards;

// Regression coverage for a bug where AlteredCardsClient used System.Net.Http.Json's default
// (case-sensitive, PascalCase) JSON options against an API that speaks camelCase both ways:
// the outgoing request body serialized "References" instead of "references" (the API rejects
// it with 400 "references array is required"), and — even past that — the response's lowercase
// fields ("reference", "imagePath", "set", ...) never bound to the PascalCase CardDto
// properties, so every backfilled card silently ended up with every field null. This never
// surfaced before because every booster type before the EOLECB alt-art booster draws uniques,
// which skip catalog backfill entirely — this was the first code path to ever exercise it.
public class AlteredCardsClientTests
{
    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public string? CapturedRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            CapturedRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return response;
        }
    }

    private const string SampleApiResponse = """
        [{
            "reference": "ALT_EOLECB_A_OR_112_C",
            "name": { "en": "Hippogriff", "fr": "Hippogriffe" },
            "imagePath": { "en": "https://cdn.example/en.jpg", "fr": "https://cdn.example/fr.jpg" },
            "set": null,
            "faction": { "code": "OR" },
            "rarity": { "reference": "COMMON" },
            "cardType": { "reference": "CHARACTER" },
            "cardSubTypes": [{ "reference": "ANIMAL" }],
            "variation": "alt-art",
            "isBanned": false,
            "isSuspended": false,
            "mainCost": 2,
            "recallCost": 2,
            "oceanPower": 1,
            "mountainPower": 1,
            "forestPower": 1
        }]
        """;

    [Fact]
    public async Task FetchBatchAsync_sends_camelCase_references_field()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json"),
        });
        var client = new AlteredCardsClient(new HttpClient(handler) { BaseAddress = new Uri("https://cards.example/") });

        await client.FetchBatchAsync(["ALT_EOLECB_A_OR_112_C"], CancellationToken.None);

        Assert.NotNull(handler.CapturedRequestBody);
        using var body = JsonDocument.Parse(handler.CapturedRequestBody!);
        Assert.True(body.RootElement.TryGetProperty("references", out var refs));
        Assert.Equal("ALT_EOLECB_A_OR_112_C", refs[0].GetString());
    }

    [Fact]
    public async Task FetchBatchAsync_binds_the_camelCase_response_fields()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SampleApiResponse, Encoding.UTF8, "application/json"),
        });
        var client = new AlteredCardsClient(new HttpClient(handler) { BaseAddress = new Uri("https://cards.example/") });

        var result = await client.FetchBatchAsync(["ALT_EOLECB_A_OR_112_C"], CancellationToken.None);

        var card = Assert.Single(result);
        Assert.Equal("ALT_EOLECB_A_OR_112_C", card.Reference);
        Assert.Equal("Hippogriff", card.Name?["en"]);
        Assert.Equal("https://cdn.example/en.jpg", card.ImagePath?["en"]);
        Assert.Equal("OR", card.Faction?.Code);
        Assert.Equal("COMMON", card.Rarity?.Reference);
        Assert.Equal(2, card.MainCost);
        Assert.Null(card.Set);
    }
}
