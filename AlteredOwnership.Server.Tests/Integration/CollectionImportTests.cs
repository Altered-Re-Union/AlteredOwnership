using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace AlteredOwnership.Server.Tests.Integration;

public class CollectionImportTests(OwnershipApiFactory factory) : IClassFixture<OwnershipApiFactory>
{
    private record CardOwnershipResponse(string Reference, int Quantity);

    [Fact]
    public async Task Import_then_get_returns_only_alt_arts_and_uniques()
    {
        var client = factory.CreateClient();

        const string csv =
            "card_reference;card_name;rarity;quantity\n" +
            "ALT_ALIZE_A_AX_35_C;Vaike;Commun;3\n" +              // alt-art -> kept
            "ALT_ALIZE_B_AX_32_C;Machine commun;Commun;6\n" +     // regular -> dropped
            "ALT_ALIZE_B_AX_32_R1;Machine rare;Rare;2\n" +        // regular -> dropped
            "ALT_ALIZE_B_AX_32_U_4624;Unique;Unique;1\n" +        // unique  -> kept
            "ALT_DUSTERTOP_B_AX_01_C;Topdust;Commun;2\n";         // dedicated set -> kept

        using var content = new MultipartFormDataContent();
        var csvContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        csvContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(csvContent, "file", "collection.csv");

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
}
