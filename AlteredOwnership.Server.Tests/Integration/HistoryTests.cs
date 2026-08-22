using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Domain.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AlteredOwnership.Server.Tests.Integration;

public class HistoryTests(OwnershipApiFactory factory) : IClassFixture<OwnershipApiFactory>
{
    private record EventSummaryDto(long Id, string Name, int Received, int Given, List<PreviewDto> Preview);
    private record PreviewDto(string Reference, int Quantity, string? Name, string? ImagePath);
    private record DetailDto(long Id, string Name, List<LineDto> Received, List<LineDto> Given);
    private record LineDto(string Reference, int Quantity, string? Name, string? ImagePath);

    // Import runs in dev-only unencrypted mode so this test needs no shared key.
    private readonly HttpClient _client = factory
        .WithWebHostBuilder(b => b.UseSetting("EquinoxImport:AllowUnencrypted", "true"))
        .CreateClient();

    private async Task<Guid> SeedUserAsync(string keycloakId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
        var user = new User { Id = Guid.NewGuid(), KeycloakId = keycloakId, CreatedAt = DateTimeOffset.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task GiveRewardAsync(Guid userId, string reference, int quantity, string acquiredFrom)
    {
        using var scope = factory.Services.CreateScope();
        var rewards = scope.ServiceProvider.GetRequiredService<RewardService>();
        await rewards.RewardToUserAsync(userId, reference, quantity, acquiredFrom, CancellationToken.None);
    }

    private async Task<List<EventSummaryDto>> GetHistoryAsync(string keycloakId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/history");
        req.Headers.Add(TestAuthHandler.UserHeader, keycloakId);
        using var res = await _client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<List<EventSummaryDto>>())!;
    }

    [Fact]
    public async Task List_returns_a_reward_event_with_its_delta_and_preview()
    {
        const string keycloakId = "history-reward-user";
        var userId = await SeedUserAsync(keycloakId);
        await GiveRewardAsync(userId, "ALT_ALIZE_B_AX_70_C", 2, "Event Test Paris");

        var events = await GetHistoryAsync(keycloakId);

        var evt = Assert.Single(events);
        Assert.Equal("Event Test Paris", evt.Name);
        Assert.Equal(2, evt.Received);
        Assert.Equal(0, evt.Given);
        var preview = Assert.Single(evt.Preview);
        Assert.Equal("ALT_ALIZE_B_AX_70_C", preview.Reference);
        Assert.Equal(2, preview.Quantity);
    }

    [Fact]
    public async Task Detail_puts_the_reward_under_received_and_leaves_given_empty()
    {
        const string keycloakId = "history-reward-detail-user";
        var userId = await SeedUserAsync(keycloakId);
        await GiveRewardAsync(userId, "ALT_ALIZE_B_AX_71_C", 1, "Event Test Lyon");

        var eventId = (await GetHistoryAsync(keycloakId)).Single().Id;

        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/history/{eventId}");
        req.Headers.Add(TestAuthHandler.UserHeader, keycloakId);
        using var res = await _client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        var detail = await res.Content.ReadFromJsonAsync<DetailDto>();

        var received = Assert.Single(detail!.Received);
        Assert.Equal("ALT_ALIZE_B_AX_71_C", received.Reference);
        Assert.Empty(detail.Given);
    }

    [Fact]
    public async Task Users_only_see_their_own_events()
    {
        const string ownerKeycloakId = "history-owner";
        const string otherKeycloakId = "history-other";
        var ownerId = await SeedUserAsync(ownerKeycloakId);
        await SeedUserAsync(otherKeycloakId);
        await GiveRewardAsync(ownerId, "ALT_ALIZE_B_AX_72_C", 1, "Event Test Owner");

        Assert.Empty(await GetHistoryAsync(otherKeycloakId));

        var eventId = (await GetHistoryAsync(ownerKeycloakId)).Single().Id;
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/history/{eventId}");
        req.Headers.Add(TestAuthHandler.UserHeader, otherKeycloakId);
        using var res = await _client.SendAsync(req);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, res.StatusCode);
    }

    private const string ImportHeader = "card_reference;card_name;rarity;quantity\n";

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

    [Fact]
    public async Task List_caps_the_preview_at_three_cards_but_sums_the_full_delta()
    {
        const string keycloakId = "history-import-user";
        var csv =
            TimestampLine("2026-05-20 17:14:43") +
            ImportHeader +
            "ALT_ALIZE_A_AX_60_C;Card 1;Commun;1\n" +
            "ALT_ALIZE_A_AX_61_C;Card 2;Commun;2\n" +
            "ALT_ALIZE_A_AX_62_C;Card 3;Commun;3\n" +
            "ALT_ALIZE_A_AX_63_C;Card 4;Commun;4\n";

        using var csrfReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        csrfReq.Headers.Add(TestAuthHandler.UserHeader, keycloakId);
        using var csrfRes = await _client.SendAsync(csrfReq);
        var token = (await csrfRes.Content.ReadFromJsonAsync<CsrfDto>())!.Token;

        using var importReq = new HttpRequestMessage(HttpMethod.Post, "/api/collection/import")
        {
            Content = BuildImport(csv),
        };
        importReq.Headers.Add("X-CSRF-TOKEN", token);
        importReq.Headers.Add(TestAuthHandler.UserHeader, keycloakId);
        using var importRes = await _client.SendAsync(importReq);
        importRes.EnsureSuccessStatusCode();

        var evt = Assert.Single(await GetHistoryAsync(keycloakId));
        Assert.Equal(10, evt.Received); // 1 + 2 + 3 + 4
        Assert.Equal(3, evt.Preview.Count);
    }

    private record CsrfDto(string Token);
}
