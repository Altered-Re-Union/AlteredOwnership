using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace AlteredOwnership.Server.Tests.Integration;

public class VerifyOwnershipTests(OwnershipApiFactory factory) : IClassFixture<OwnershipApiFactory>
{
    private record OwnershipCheckItem(string Reference, int Quantity);
    private record OwnershipShortfall(string Reference, int Requested, int Owned);
    private record CsrfResponse(string Token);

    private const string Header = "card_reference;card_name;rarity;quantity\n";

    // Import runs in dev-only unencrypted mode so the test needs no shared key.
    private readonly HttpClient _client = factory
        .WithWebHostBuilder(b => b.UseSetting("EquinoxImport:AllowUnencrypted", "true"))
        .CreateClient();

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

    private async Task ImportAsync(string csv, string user)
    {
        using var csrfReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        csrfReq.Headers.Add(TestAuthHandler.UserHeader, user);
        using var csrfRes = await _client.SendAsync(csrfReq);
        csrfRes.EnsureSuccessStatusCode();
        var token = (await csrfRes.Content.ReadFromJsonAsync<CsrfResponse>())!.Token;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/collection/import")
        {
            Content = BuildImport(csv),
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        request.Headers.Add(TestAuthHandler.UserHeader, user);
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Verify_reports_only_short_uniques_and_alt_arts()
    {
        const string user = "verify-ownership-user";

        await ImportAsync(
            TimestampLine() +
            Header +
            "ALT_ALIZE_B_AX_77_U_9001;Unique owned;Unique;1\n" +    // unique, owned 1
            "ALT_ALIZE_A_AX_88_C;Alt owned;Commun;3\n" +            // alt-art, owned 3
            "ALT_ALIZE_B_AX_88_C;Regular common;Commun;6\n",        // regular -> not stored
            user);

        var items = new[]
        {
            new OwnershipCheckItem("ALT_ALIZE_B_AX_77_U_9001", 1),  // owned 1 -> ok
            new OwnershipCheckItem("ALT_ALIZE_A_AX_88_C", 5),       // owned 3 -> short by 2
            new OwnershipCheckItem("ALT_ALIZE_B_AX_78_U_9002", 1),  // unique, never owned -> short
            new OwnershipCheckItem("ALT_ALIZE_B_AX_88_C", 99),      // regular -> filtered out
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/collection/verify-ownership")
        {
            Content = JsonContent.Create(items),
        };
        request.Headers.Add(TestAuthHandler.UserHeader, user);
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var shortfalls = (await response.Content.ReadFromJsonAsync<OwnershipShortfall[]>())!;
        var byRef = shortfalls.ToDictionary(s => s.Reference);

        Assert.Equal(2, byRef.Count);
        Assert.Equal(new OwnershipShortfall("ALT_ALIZE_A_AX_88_C", 5, 3), byRef["ALT_ALIZE_A_AX_88_C"]);
        Assert.Equal(new OwnershipShortfall("ALT_ALIZE_B_AX_78_U_9002", 1, 0), byRef["ALT_ALIZE_B_AX_78_U_9002"]);
        // Fully-owned unique and the regular common are absent.
        Assert.DoesNotContain("ALT_ALIZE_B_AX_77_U_9001", byRef.Keys);
        Assert.DoesNotContain("ALT_ALIZE_B_AX_88_C", byRef.Keys);
    }

    private static string TimestampLine() => "\"2026-05-22 10:00:00\";;;\n";
}
