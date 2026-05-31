using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Infrastructure.Cards;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AlteredOwnership.Server.Tests.Integration;

public class CardCatalogTests(OwnershipApiFactory factory) : IClassFixture<OwnershipApiFactory>
{
    private const string Header = "card_reference;card_name;rarity;quantity\n";

    private record CollectionItem(
        string Reference, int Quantity, string? Name, string? ImagePath,
        string? Set, string? Faction, string? Rarity, string? CardType,
        string? Variation, List<string>? SubTypes,
        bool? IsBanned, bool? IsSuspended,
        int? MainCost, int? RecallCost, int? Forest, int? Mountain, int? Ocean);

    [Fact]
    public async Task Backfill_populates_card_metadata_in_all_languages()
    {
        var recording = new RecordingCardsClient(refs => refs.Select(Dto).ToList());
        var client = MakeClient(recording);
        const string user = "catalog-langs";
        const string reference = "ALT_ALIZE_A_AX_60_C";

        var import = await PostImportAsync(client,
            TimestampLine("2026-05-20 17:14:43") + Header + $"{reference};Vaike;Commun;2\n", user);
        Assert.Equal(HttpStatusCode.NoContent, import.StatusCode);

        // A single locale-less call returns every language; each is selectable at read time.
        Assert.Equal([reference], Assert.Single(recording.Calls));
        Assert.Equal($"de:{reference}", Assert.Single(await GetCollectionAsync(client, "?locale=de", user)).Name);

        var item = Assert.Single(await GetCollectionAsync(client, "?locale=en", user));
        Assert.Equal(reference, item.Reference);
        Assert.Equal(2, item.Quantity);
        Assert.Equal($"en:{reference}", item.Name);
        Assert.Equal("AX", item.Faction);
        Assert.Equal("CHARACTER", item.CardType);
        Assert.Equal(["ANIMAL"], item.SubTypes);
    }

    [Fact]
    public async Task Backfill_is_best_effort_and_recovers_on_later_import()
    {
        var recording = new RecordingCardsClient(refs => refs.Select(Dto).ToList());
        var client = MakeClient(recording);
        const string user = "catalog-besteffort";
        const string ref1 = "ALT_ALIZE_A_AX_62_C";
        const string ref2 = "ALT_ALIZE_A_BR_63_C";
        const string ref3 = "ALT_ALIZE_A_OR_64_C";

        // First import: catalog API is down -> import still succeeds, no metadata.
        recording.Throws = true;
        var first = await PostImportAsync(client,
            TimestampLine("2026-05-20 17:14:43") + Header + $"{ref1};A;Commun;1\n", user);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var afterFail = await GetCollectionAsync(client, "", user);
        Assert.Equal(ref1, Assert.Single(afterFail).Reference);
        Assert.Null(afterFail[0].Name);

        // Second import (new card), API back up: backfills BOTH the new and the
        // previously-missing reference.
        recording.Throws = false;
        recording.Calls.Clear();
        var second = await PostImportAsync(client,
            TimestampLine("2026-05-21 09:00:00") + Header + $"{ref1};A;Commun;1\n{ref2};B;Commun;1\n", user);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        var afterRecover = await GetCollectionAsync(client, "?locale=en", user);
        Assert.Equal(2, afterRecover.Length);
        Assert.All(afterRecover, c => Assert.NotNull(c.Name));
        Assert.Equal([ref1, ref2], recording.Calls.SelectMany(c => c).Distinct().OrderBy(x => x));

        // Third import adds another card; already-catalogued refs are NOT re-fetched.
        recording.Calls.Clear();
        var third = await PostImportAsync(client,
            TimestampLine("2026-05-22 09:00:00") + Header
            + $"{ref1};A;Commun;1\n{ref2};B;Commun;1\n{ref3};C;Commun;1\n", user);
        Assert.Equal(HttpStatusCode.NoContent, third.StatusCode);
        Assert.Equal([ref3], recording.Calls.SelectMany(c => c).Distinct());
    }

    [Fact]
    public async Task Collection_join_applies_filters_and_language()
    {
        // No backfill: default NullCardsClient, so we seed the catalog explicitly.
        var client = factory
            .WithWebHostBuilder(b => b.UseSetting("EquinoxImport:AllowUnencrypted", "true"))
            .CreateClient();
        const string user = "catalog-filters";

        string[] refs =
        [
            "ALT_ALIZE_A_AX_70_C", // CHARACTER, AX, ANIMAL, cost 3
            "ALT_ALIZE_A_BR_71_C", // SPELL,     BR,         cost 5
            "ALT_ALIZE_A_OR_72_C", // CHARACTER, OR, BOON,   cost 6
            "ALT_ALIZE_A_YZ_73_C", // not catalogued
        ];
        var csv = TimestampLine("2026-05-20 17:14:43") + Header
            + string.Join("", refs.Select((r, i) => $"{r};Card{i};Commun;{i + 1}\n"));
        Assert.Equal(HttpStatusCode.NoContent, (await PostImportAsync(client, csv, user)).StatusCode);

        await SeedCardsAsync(
            BuildCard(refs[0], "AX", "CHARACTER", ["ANIMAL"], mainCost: 3, nameFr: "Loup", nameEn: "Wolf"),
            BuildCard(refs[1], "BR", "SPELL", [], mainCost: 5),
            BuildCard(refs[2], "OR", "CHARACTER", ["BOON"], mainCost: 6));

        // No filter: all four owned cards visible, including the uncatalogued one.
        var all = await GetCollectionAsync(client, "", user);
        Assert.Equal(4, all.Length);
        Assert.Contains(all, c => c.Reference == refs[3] && c.Name is null);

        // type[] = CHARACTER excludes the SPELL and the uncatalogued card.
        var characters = await GetCollectionAsync(client, "?type[]=CHARACTER", user);
        Assert.Equal([refs[0], refs[2]], characters.Select(c => c.Reference).OrderBy(x => x));

        // subtype[] = ANIMAL (text[] overlap).
        var animals = await GetCollectionAsync(client, "?subtype[]=ANIMAL", user);
        Assert.Equal(refs[0], Assert.Single(animals).Reference);

        // Range vs exact numeric.
        var costly = await GetCollectionAsync(client, "?mainCost[gte]=5", user);
        Assert.Equal([refs[1], refs[2]], costly.Select(c => c.Reference).OrderBy(x => x));
        var cheap = await GetCollectionAsync(client, "?mainCost=3", user);
        Assert.Equal(refs[0], Assert.Single(cheap).Reference);

        // Locale selection + fallback to English for an unknown locale.
        var fr = await GetCollectionAsync(client, "?faction[]=AX&locale=fr", user);
        Assert.Equal("Loup", Assert.Single(fr).Name);
        var en = await GetCollectionAsync(client, "?faction[]=AX&locale=en", user);
        Assert.Equal("Wolf", Assert.Single(en).Name);
        var de = await GetCollectionAsync(client, "?faction[]=AX&locale=de", user);
        Assert.Equal("Wolf", Assert.Single(de).Name);
    }

    // --- helpers -----------------------------------------------------------------

    private HttpClient MakeClient(IAlteredCardsClient cardsClient) => factory
        .WithWebHostBuilder(b =>
        {
            b.UseSetting("EquinoxImport:AllowUnencrypted", "true");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IAlteredCardsClient>();
                s.AddSingleton(cardsClient);
            });
        })
        .CreateClient();

    private async Task SeedCardsAsync(params Card[] cards)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
        db.Cards.AddRange(cards);
        await db.SaveChangesAsync();
    }

    private static Card BuildCard(
        string reference, string faction, string type, string[] subTypes, int mainCost,
        string? nameFr = null, string? nameEn = null)
    {
        var name = new Dictionary<string, string>();
        if (nameFr is not null) name["fr"] = nameFr;
        if (nameEn is not null) name["en"] = nameEn;
        return new Card
        {
            Reference = reference,
            Set = "COREKS",
            Faction = faction,
            Rarity = "COMMON",
            CardType = type,
            Variation = "standard",
            SubTypes = [.. subTypes],
            MainCost = mainCost,
            Name = name,
        };
    }

    private static readonly string[] Locales = ["en", "fr", "de", "es", "it"];

    private static CardDto Dto(string reference) => new()
    {
        Reference = reference,
        Name = Locales.ToDictionary(l => l, l => $"{l}:{reference}"),
        ImagePath = Locales.ToDictionary(l => l, l => $"https://img/{l}/{reference}.png"),
        Set = new RefValue("COREKS"),
        Faction = new FactionValue("AX"),
        Rarity = new RefValue("COMMON"),
        CardType = new RefValue("CHARACTER"),
        CardSubTypes = [new RefValue("ANIMAL")],
        Variation = "standard",
        MainCost = 3,
        RecallCost = 2,
        ForestPower = 1,
        MountainPower = 2,
        OceanPower = 3,
    };

    private static async Task<CollectionItem[]> GetCollectionAsync(HttpClient client, string queryString, string user)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/collection" + queryString);
        req.Headers.Add(TestAuthHandler.UserHeader, user);
        using var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<CollectionItem[]>())!;
    }

    private static string TimestampLine(string timestamp) => $"\"{timestamp}\";;;\n";

    private record CsrfResponse(string Token);

    private static async Task<HttpResponseMessage> PostImportAsync(HttpClient client, string csv, string user)
    {
        using var csrfReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        csrfReq.Headers.Add(TestAuthHandler.UserHeader, user);
        using var csrfRes = await client.SendAsync(csrfReq);
        csrfRes.EnsureSuccessStatusCode();
        var token = (await csrfRes.Content.ReadFromJsonAsync<CsrfResponse>())!.Token;

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

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/collection/import") { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", token);
        request.Headers.Add(TestAuthHandler.UserHeader, user);
        return await client.SendAsync(request);
    }
}
