using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AlteredOwnership.Server.Infrastructure.Crypto;

namespace AlteredOwnership.Server.Tests.Integration;

public class CollectionImportTests(OwnershipApiFactory factory) : IClassFixture<OwnershipApiFactory>
{
    private record CardOwnershipResponse(string Reference, int Quantity);

    private const string Header = "card_reference;card_name;rarity;quantity\n";

    private static readonly byte[] Key = Convert.FromHexString(OwnershipApiFactory.DecryptionKeyHex);

    private static string TimestampLine(string timestamp) => $"\"{timestamp}\";;;\n";

    private static MultipartFormDataContent BuildImport(string csv)
    {
        var encrypted = SecretBoxFile.Encrypt(Encoding.UTF8.GetBytes(csv), Key);

        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("encrypted/collection.csv.enc");
            using var entryStream = entry.Open();
            entryStream.Write(encrypted);
        }

        var content = new MultipartFormDataContent();
        var zipContent = new ByteArrayContent(zipStream.ToArray());
        zipContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(zipContent, "file", "collection.zip");
        content.Add(new StringContent("true"), "termsAccepted");
        return content;
    }

    private static async Task<HttpResponseMessage> ImportAsAsync(HttpClient client, string user, string csv)
    {
        using var content = BuildImport(csv);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/collection/import") { Content = content };
        request.Headers.Add(TestAuthHandler.UserHeader, user);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Import_then_get_returns_only_alt_arts_and_uniques()
    {
        var client = factory.CreateClient();

        var csv =
            TimestampLine("2026-05-20 17:14:43") +
            Header +
            "ALT_ALIZE_A_AX_35_C;Vaike;Commun;3\n" +              // alt-art -> kept
            "ALT_ALIZE_B_AX_32_C;Machine commun;Commun;6\n" +     // regular -> dropped
            "ALT_ALIZE_B_AX_32_R1;Machine rare;Rare;2\n" +        // regular -> dropped
            "ALT_ALIZE_B_AX_32_U_4624;Unique;Unique;1\n" +        // unique  -> kept
            "ALT_DUSTERTOP_B_AX_01_C;Topdust;Commun;2\n";         // dedicated set -> kept

        using var content = BuildImport(csv);
        var importResponse = await client.PostAsync("/api/collection/import", content);
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
    public async Task Import_without_timestamp_is_rejected()
    {
        var client = factory.CreateClient();

        // Old-format export: no timestamp line, straight to the column header.
        var csv =
            Header +
            "ALT_ALIZE_A_AX_35_C;Vaike;Commun;3\n";

        using var content = BuildImport(csv);
        var response = await client.PostAsync("/api/collection/import", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("this export has no timestamp", body);
    }

    [Fact]
    public async Task Reimporting_same_cards_with_different_timestamp_fails()
    {
        var client = factory.CreateClient();

        // Regular core card (dropped from the projection) so this test never
        // pollutes the exact-count assertion in the import-then-get test.
        const string cards = "ALT_ALIZE_B_AX_99_C;Reimport probe;Commun;2\n";

        using var first = BuildImport(TimestampLine("2026-05-20 17:14:43") + Header + cards);
        var firstResponse = await client.PostAsync("/api/collection/import", first);
        Assert.Equal(HttpStatusCode.NoContent, firstResponse.StatusCode);

        // Same collection, different export timestamp: still a duplicate.
        using var second = BuildImport(TimestampLine("2026-05-21 09:00:00") + Header + cards);
        var secondResponse = await client.PostAsync("/api/collection/import", second);

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

        using var content = new MultipartFormDataContent();
        var zipContent = new ByteArrayContent(zipStream.ToArray());
        zipContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(zipContent, "file", "collection.zip");
        content.Add(new StringContent("true"), "termsAccepted");

        var response = await client.PostAsync("/api/collection/import", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Could not decrypt", body);
    }

    [Fact]
    public async Task Two_people_importing_the_same_file_second_fails()
    {
        var client = factory.CreateClient();

        // Non-unique alt-art card: both users could legitimately own it, so the
        // only thing that can fail the second import is the global export dedup.
        var csv =
            TimestampLine("2026-05-20 17:14:43") +
            Header +
            "ALT_ALIZE_A_AX_50_C;Shared file;Commun;3\n";

        var first = await ImportAsAsync(client, "two-people-same-file-a", csv);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await ImportAsAsync(client, "two-people-same-file-b", csv);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("déjà été importé", body);
    }

    [Fact]
    public async Task Two_people_importing_files_differing_only_by_timestamp_second_fails()
    {
        var client = factory.CreateClient();

        const string cards = "ALT_ALIZE_A_AX_51_C;Shared file;Commun;3\n";

        var first = await ImportAsAsync(
            client, "two-people-diff-ts-a", TimestampLine("2026-05-20 17:14:43") + Header + cards);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        // Same cards, only the export timestamp differs: the dedup hash ignores it,
        // so the second person is still rejected as a duplicate.
        var second = await ImportAsAsync(
            client, "two-people-diff-ts-b", TimestampLine("2026-05-21 09:00:00") + Header + cards);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("déjà été importé", body);
    }
}
