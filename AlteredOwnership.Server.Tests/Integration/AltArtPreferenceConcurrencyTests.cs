using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace AlteredOwnership.Server.Tests.Integration;

// The website's alt-art widget used to fire one PUT per click without waiting for the
// previous one: two quick clicks could race, the family's first save failed with a
// duplicate-key 500, and a later race left the stored slots on the older click (the
// player saw both top markers on the alt art, yet a 2-copy deck got one alt + one base).
// Family 4 (LY, R) "Icebound Tundra" — see AltArtEndpointsTests for the fixture notes.
public class AltArtPreferenceConcurrencyTests(OwnershipApiFactory factory) : IClassFixture<OwnershipApiFactory>
{
    private const string Default = "ALT_ALIZE_B_LY_45_R1";
    private const string Alt = "ALT_ALIZE_A_LY_45_R1";

    private record SetAltArtPreferenceRequest(int FamilyId, string Faction, string Rarity, List<string?> SlotReferences);
    private record OwnershipCheckItem(string Reference, int Quantity);
    private record ApplyToDeckResponse(List<List<OwnershipCheckItem>> Lines, List<OwnershipCheckItem> Tokens);
    private record AltArtSlotChoice(int SlotIndex, string Reference, bool IsExplicitChoice);
    private record AltArtOptionsResponse(int FamilyId, string Faction, string Rarity, List<AltArtSlotChoice> Slots);
    private record CsrfResponse(string Token);

    private readonly HttpClient _client = factory
        .WithWebHostBuilder(b => b.UseSetting("EquinoxImport:AllowUnencrypted", "true"))
        .CreateClient();

    private async Task<string> CsrfAsync(string user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        request.Headers.Add(TestAuthHandler.UserHeader, user);
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CsrfResponse>())!.Token;
    }

    private async Task<HttpResponseMessage> PutAsync(string user, string path, object body, string csrfToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        request.Headers.Add(TestAuthHandler.UserHeader, user);
        return await _client.SendAsync(request);
    }

    private static SetAltArtPreferenceRequest Pref(params string?[] slots) => new(4, "LY", "R", slots.ToList());

    // Owns 2 copies of the alt art, Global mode.
    private async Task SetupUserAsync(string user)
    {
        var csv = "\"2026-05-22 10:00:00\";;;\ncard_reference;card_name;rarity;quantity\n"
            + $"{Alt};Icebound Tundra;Rare;2\nSALT_{user};Salt;Commun;1\n";
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entryStream = archive.CreateEntry("clear/collection.csv").Open();
            entryStream.Write(Encoding.UTF8.GetBytes(csv));
        }
        var content = new MultipartFormDataContent();
        var zipContent = new ByteArrayContent(zipStream.ToArray());
        zipContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(zipContent, "file", "collection.zip");
        content.Add(new StringContent("true"), "termsAccepted");

        using var import = new HttpRequestMessage(HttpMethod.Post, "/api/collection/import") { Content = content };
        import.Headers.Add("X-CSRF-TOKEN", await CsrfAsync(user));
        import.Headers.Add(TestAuthHandler.UserHeader, user);
        using var importResponse = await _client.SendAsync(import);
        importResponse.EnsureSuccessStatusCode();

        using var setMode = await PutAsync(user, "/api/alt-arts/preference-mode", new { Mode = "Global" }, await CsrfAsync(user));
        setMode.EnsureSuccessStatusCode();
    }

    private async Task<List<string>> GetSlotReferencesAsync(string user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/alt-arts/options")
        {
            Content = JsonContent.Create(new[] { new { FamilyId = 4, Faction = "LY", Rarity = "R" } }),
        };
        request.Headers.Add(TestAuthHandler.UserHeader, user);
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var options = (await response.Content.ReadFromJsonAsync<List<AltArtOptionsResponse>>())!;
        return options.Single().Slots.OrderBy(s => s.SlotIndex).Select(s => s.Reference).ToList();
    }

    private async Task<Dictionary<string, int>> ApplyToDeckAsync(string user, params OwnershipCheckItem[] deck)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/alt-arts/apply-to-deck")
        {
            Content = JsonContent.Create(deck.ToList()),
        };
        request.Headers.Add(TestAuthHandler.UserHeader, user);
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<ApplyToDeckResponse>())!;
        return result.Lines.SelectMany(l => l)
            .GroupBy(i => i.Reference)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
    }

    [Theory]
    [InlineData(false)] // the family's very first save: no preference rows exist yet
    [InlineData(true)]  // every slot row already exists
    public async Task Concurrent_saves_all_succeed_and_store_exactly_one_of_the_requests(bool existingRows)
    {
        var first = Pref(Alt, Default, Default);
        var second = Pref(Alt, Alt, Default);

        for (var run = 0; run < 10; run++)
        {
            var user = $"alt-art-concurrent-{existingRows}-{run}";
            await SetupUserAsync(user);
            if (existingRows)
            {
                using var initial = await PutAsync(user, "/api/alt-arts/preferences", Pref(Default, Default, Default), await CsrfAsync(user));
                initial.EnsureSuccessStatusCode();
            }

            var tokens = await Task.WhenAll(CsrfAsync(user), CsrfAsync(user));
            var responses = await Task.WhenAll(
                PutAsync(user, "/api/alt-arts/preferences", first, tokens[0]),
                PutAsync(user, "/api/alt-arts/preferences", second, tokens[1]));

            Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
            var stored = await GetSlotReferencesAsync(user);
            Assert.True(stored.SequenceEqual(first.SlotReferences) || stored.SequenceEqual(second.SlotReferences),
                $"Stored slots [{string.Join(", ", stored)}] match neither request.");
        }
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    [InlineData(0, 3)]
    [InlineData(2, 1)]
    public async Task Top_two_slots_on_an_art_owned_twice_give_two_copies_of_it(int altLine, int defaultLine)
    {
        var user = $"alt-art-top-two-{altLine}-{defaultLine}";
        await SetupUserAsync(user);
        using var setPref = await PutAsync(user, "/api/alt-arts/preferences", Pref(Alt, Alt, Default), await CsrfAsync(user));
        setPref.EnsureSuccessStatusCode();

        var deck = new List<OwnershipCheckItem>();
        if (altLine > 0) deck.Add(new(Alt, altLine));
        if (defaultLine > 0) deck.Add(new(Default, defaultLine));
        var result = await ApplyToDeckAsync(user, deck.ToArray());

        var total = altLine + defaultLine;
        Assert.Equal(Math.Min(total, 2), result.GetValueOrDefault(Alt));
        Assert.Equal(Math.Max(total - 2, 0), result.GetValueOrDefault(Default));
    }
}
