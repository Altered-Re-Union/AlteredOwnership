using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace AlteredOwnership.Server.Tests.Integration;

public class CollectionImportTests(OwnershipApiFactory factory) : IClassFixture<OwnershipApiFactory>
{
    private record CardOwnershipResponse(string Reference, int Quantity);

    private const string Header = "card_reference;card_name;rarity;quantity\n";

    // Import runs in the dev-only unencrypted mode so tests need no shared key:
    // the upload carries the plaintext clear/collection.csv entry.
    private readonly HttpClient _client = factory
        .WithWebHostBuilder(b => b.UseSetting("EquinoxImport:AllowUnencrypted", "true"))
        .CreateClient();

    private record CsrfResponse(string Token);

    private static string TimestampLine(string timestamp) => $"\"{timestamp}\";;;\n";

    private static MultipartFormDataContent BuildImport(string csv)
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("clear/collection.csv");
            using var entryStream = entry.Open();
            entryStream.Write(Encoding.UTF8.GetBytes(csv));
        }

        var content = new MultipartFormDataContent();
        var zipContent = new ByteArrayContent(zipStream.ToArray());
        zipContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(zipContent, "file", "collection.zip");
        content.Add(new StringContent("true"), "termsAccepted");
        return content;
    }

    // GET the session-bound antiforgery token; the response also sets the paired cookie,
    // which the cookie-handling test client carries to the subsequent import POST.
    private static async Task<string> FetchCsrfAsync(HttpClient client, string? user = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        if (user is not null) req.Headers.Add(TestAuthHandler.UserHeader, user);
        using var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<CsrfResponse>())!.Token;
    }

    private static async Task<HttpResponseMessage> PostImportAsync(HttpClient client, string csv, string? user = null)
    {
        var token = await FetchCsrfAsync(client, user);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/collection/import")
        {
            Content = BuildImport(csv),
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        if (user is not null) request.Headers.Add(TestAuthHandler.UserHeader, user);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Import_then_get_returns_only_alt_arts_and_uniques()
    {
        var client = _client;

        var csv =
            TimestampLine("2026-05-20 17:14:43") +
            Header +
            "ALT_ALIZE_A_AX_35_C;Vaike;Commun;3\n" +              // alt-art -> kept
            "ALT_ALIZE_B_AX_32_C;Machine commun;Commun;6\n" +     // regular -> dropped
            "ALT_ALIZE_B_AX_32_R1;Machine rare;Rare;2\n" +        // regular -> dropped
            "ALT_ALIZE_B_AX_32_U_4624;Unique;Unique;1\n" +        // unique  -> kept
            "ALT_DUSTERTOP_B_AX_01_C;Topdust;Commun;2\n";         // dedicated set -> kept

        var importResponse = await PostImportAsync(client, csv);
        Assert.Equal(HttpStatusCode.NoContent, importResponse.StatusCode);

        var collection = await client.GetFromJsonAsync<CardOwnershipResponse[]>("/api/collection");

        Assert.NotNull(collection);
        var byRef = collection!.ToDictionary(c => c.Reference);
        Assert.Equal(3, byRef.Count);
        Assert.Equal(3, byRef["ALT_ALIZE_A_AX_35_C"].Quantity);
        Assert.Equal(1, byRef["ALT_ALIZE_B_AX_32_U_4624"].Quantity);
        Assert.Equal(2, byRef["ALT_DUSTERTOP_B_AX_01_C"].Quantity);
        Assert.DoesNotContain("ALT_ALIZE_B_AX_32_C", byRef.Keys);
        Assert.DoesNotContain("ALT_ALIZE_B_AX_32_R1", byRef.Keys);
    }

    [Fact]
    public void Unencrypted_mode_refuses_to_start_in_production()
    {
        var app = factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("EquinoxImport:AllowUnencrypted", "true");
        });

        var ex = Assert.Throws<OptionsValidationException>(() => app.CreateClient());
        Assert.Contains("must not be enabled in Production", ex.Message);
    }

    [Fact]
    public async Task Import_without_timestamp_is_rejected()
    {
        var client = _client;

        // Old-format export: no timestamp line, straight to the column header.
        var csv =
            Header +
            "ALT_ALIZE_A_AX_35_C;Vaike;Commun;3\n";

        var response = await PostImportAsync(client, csv);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("this export has no timestamp", body);
    }

    [Fact]
    public async Task Reimporting_same_cards_with_different_timestamp_fails()
    {
        var client = _client;

        // Regular core card (dropped from the projection) so this test never
        // pollutes the exact-count assertion in the import-then-get test.
        const string cards = "ALT_ALIZE_B_AX_99_C;Reimport probe;Commun;2\n";

        var firstResponse = await PostImportAsync(client, TimestampLine("2026-05-20 17:14:43") + Header + cards);
        Assert.Equal(HttpStatusCode.NoContent, firstResponse.StatusCode);

        // Same collection, different export timestamp: still a duplicate.
        var secondResponse = await PostImportAsync(client, TimestampLine("2026-05-21 09:00:00") + Header + cards);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        var body = await secondResponse.Content.ReadAsStringAsync();
        Assert.Contains("déjà été importé", body);
    }

    [Fact]
    public async Task Import_with_undecryptable_file_is_rejected()
    {
        var client = factory.CreateClient();

        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("encrypted/collection.csv.enc");
            using var entryStream = entry.Open();
            // Long enough to hold a nonce + tag, but not a valid secretbox payload.
            entryStream.Write(new byte[64]);
        }

        var content = new MultipartFormDataContent();
        var zipContent = new ByteArrayContent(zipStream.ToArray());
        zipContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(zipContent, "file", "collection.zip");
        content.Add(new StringContent("true"), "termsAccepted");

        var token = await FetchCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/collection/import") { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Could not decrypt", body);
    }

    [Fact]
    public async Task Import_without_csrf_token_is_rejected()
    {
        var csv =
            TimestampLine("2026-05-23 12:00:00") +
            Header +
            "ALT_ALIZE_A_AX_70_C;No token;Commun;1\n";

        // Authenticated cookie request but no antiforgery token/cookie: the CSRF
        // middleware must reject it before the endpoint runs.
        using var content = BuildImport(csv);
        var response = await _client.PostAsync("/api/collection/import", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Two_people_importing_the_same_file_second_fails()
    {
        // Non-unique alt-art card: both users could legitimately own it, so the
        // only thing that can fail the second import is the global export dedup.
        var csv =
            TimestampLine("2026-05-20 17:14:43") +
            Header +
            "ALT_ALIZE_A_AX_50_C;Shared file;Commun;3\n";

        var first = await PostImportAsync(_client, csv, "two-people-same-file-a");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await PostImportAsync(_client, csv, "two-people-same-file-b");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("déjà été importé", body);
    }

    [Fact]
    public async Task Two_people_importing_files_differing_only_by_timestamp_second_fails()
    {
        const string cards = "ALT_ALIZE_A_AX_51_C;Shared file;Commun;3\n";

        var first = await PostImportAsync(
            _client, TimestampLine("2026-05-20 17:14:43") + Header + cards, "two-people-diff-ts-a");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        // Same cards, only the export timestamp differs: the dedup hash ignores it,
        // so the second person is still rejected as a duplicate.
        var second = await PostImportAsync(
            _client, TimestampLine("2026-05-21 09:00:00") + Header + cards, "two-people-diff-ts-b");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("déjà été importé", body);
    }
}
