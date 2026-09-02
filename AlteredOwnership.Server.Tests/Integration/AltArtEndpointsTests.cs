using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Domain.Services;
using AlteredOwnership.Server.Infrastructure.EventSourcing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AlteredOwnership.Server.Tests.Integration;

// Exercises the alt-art selection feature end to end against the real CardArtCatalog
// seed (SeedCardArtCatalog runs like any other schema migration in tests — unlike the
// oversized SeedUniqueCardStock, its ~3.4k rows are cheap enough to seed for real).
//
// Fixtures used throughout, all real CardsData rows:
//  - Family 4 (LY, R) "Icebound Tundra": ALT_ALIZE_B_LY_45_R1 (default, untracked/
//    infinite) + ALT_ALIZE_A_LY_45_R1 (tracked alt-art, MainCost 2) — a normal 3-slot group.
//  - Family 2 (LY, C) "Auraq & Kibble" (HERO): ALT_ALIZE_B_LY_02_C (default, infinite)
//    + ALT_CORE_P_LY_02_C (tracked promo alt-art) — a 1-slot (hero) group.
//  - Family 1 (LY, C) "Sleight of Hand": ALT_ALIZE_B_LY_39_C, the catalog's only
//    printing — not a multi-art group at all.
//  - Family 31 (BR, C) "Booda" (TOKEN): 5 prints, 4 from base sets (ALIZE/CORE x2/
//    COREKS — infinite regardless of ownership) and one from DUSTEROP, a non-base set
//    (ALT_DUSTEROP_B_BR_31_C — a real, ownership-gated option) — a 1-slot token group.
public class AltArtEndpointsTests(OwnershipApiFactory factory) : IClassFixture<OwnershipApiFactory>
{
    private const string LandmarkDefault = "ALT_ALIZE_B_LY_45_R1";
    private const string LandmarkAlt = "ALT_ALIZE_A_LY_45_R1";
    private const string HeroDefault = "ALT_ALIZE_B_LY_02_C";
    private const string HeroAlt = "ALT_CORE_P_LY_02_C";
    private const string MonoArt = "ALT_ALIZE_B_LY_39_C";
    private const string TokenDefault = "ALT_ALIZE_B_BR_31_C";
    private const string TokenTracked = "ALT_DUSTEROP_B_BR_31_C";

    private record AltArtFamilyResponse(int FamilyId, string Faction, string Rarity, string Reference, string? Name, string CardType, int? MainCost);
    private record AltArtGroupKey(int FamilyId, string Faction, string Rarity);
    private record AltArtOption(string Reference, string Set, bool IsPromo, int? OwnedQuantity);
    private record AltArtSlotChoice(int SlotIndex, string Reference, bool IsExplicitChoice);
    private record AltArtOptionsResponse(int FamilyId, string Faction, string Rarity, List<AltArtOption> Options, List<AltArtSlotChoice> Slots);
    private record SetAltArtPreferenceRequest(int FamilyId, string Faction, string Rarity, List<string?> SlotReferences);
    private record OwnershipCheckItem(string Reference, int Quantity);
    private record OwnershipShortfall(string Reference, int Requested, int Owned);
    private record ApplyToDeckResponse(List<List<OwnershipCheckItem>> Lines, List<OwnershipCheckItem> Tokens);
    private record CsrfResponse(string Token);

    private const string Header = "card_reference;card_name;rarity;quantity\n";

    private readonly HttpClient _client = factory
        .WithWebHostBuilder(b => b.UseSetting("EquinoxImport:AllowUnencrypted", "true"))
        .CreateClient();

    private static string TimestampLine() => "\"2026-05-22 10:00:00\";;;\n";

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

        // Import events are deduplicated globally by a hash of the card reference/quantity
        // pairs (not per user) — a per-user salt line (never a tracked alt-art/unique
        // reference, so it's dropped from the projection) keeps every test's payload
        // distinct even when the "real" rows are identical across tests.
        csv += $"SALT_{user};Salt;Commun;1\n";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/collection/import")
        {
            Content = BuildImport(csv),
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        request.Headers.Add(TestAuthHandler.UserHeader, user);
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> GetOptionsAsync(string user, params AltArtGroupKey[] keys)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/alt-arts/options")
        {
            Content = JsonContent.Create(keys.ToList()),
        };
        request.Headers.Add(TestAuthHandler.UserHeader, user);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> ApplyToDeckAsync(string user, List<OwnershipCheckItem> deck)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/alt-arts/apply-to-deck")
        {
            Content = JsonContent.Create(deck),
        };
        request.Headers.Add(TestAuthHandler.UserHeader, user);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SetPreferenceAsync(string user, SetAltArtPreferenceRequest body)
    {
        // PUT mutates state via a cookie session, so the global CSRF middleware guards
        // it (see Program.cs) — needs a real antiforgery token, unlike the read-only
        // POST endpoints above which opt out with .DisableAntiforgery().
        using var csrfReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        csrfReq.Headers.Add(TestAuthHandler.UserHeader, user);
        using var csrfRes = await _client.SendAsync(csrfReq);
        csrfRes.EnsureSuccessStatusCode();
        var token = (await csrfRes.Content.ReadFromJsonAsync<CsrfResponse>())!.Token;

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/alt-arts/preferences")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        request.Headers.Add(TestAuthHandler.UserHeader, user);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Families_lists_multi_art_groups_and_applies_filters()
    {
        const string user = "alt-art-families-user";
        using var response = await Authenticated(HttpMethod.Get,
            "/api/alt-arts/families?faction[]=LY&rarity[]=R&mainCost=2", user);
        response.EnsureSuccessStatusCode();

        var families = (await response.Content.ReadFromJsonAsync<List<AltArtFamilyResponse>>())!;
        var tundra = families.SingleOrDefault(f => f.FamilyId == 4);

        Assert.NotNull(tundra);
        Assert.Equal(LandmarkDefault, tundra!.Reference); // earliest non-promo printing
        Assert.Equal("Icebound Tundra", tundra.Name);
        Assert.Equal(2, tundra.MainCost);

        // A name filter that doesn't match anything in this family excludes it.
        using var noMatch = await Authenticated(HttpMethod.Get,
            "/api/alt-arts/families?faction[]=LY&rarity[]=R&name=NoSuchCardName", user);
        var filtered = (await noMatch.Content.ReadFromJsonAsync<List<AltArtFamilyResponse>>())!;
        Assert.DoesNotContain(filtered, f => f.FamilyId == 4);
    }

    private async Task<HttpResponseMessage> Authenticated(HttpMethod method, string url, string user)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.UserHeader, user);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Options_reports_infinite_default_art_and_real_quantity_for_tracked_art()
    {
        const string user = "alt-art-options-user";
        await ImportAsync(
            TimestampLine() + Header +
            $"{LandmarkAlt};Icebound Tundra;Rare;2\n",
            user);

        using var response = await GetOptionsAsync(user, new AltArtGroupKey(4, "LY", "R"));
        response.EnsureSuccessStatusCode();
        var groups = (await response.Content.ReadFromJsonAsync<List<AltArtOptionsResponse>>())!;
        var group = Assert.Single(groups);

        var defaultOption = group.Options.Single(o => o.Reference == LandmarkDefault);
        var altOption = group.Options.Single(o => o.Reference == LandmarkAlt);
        Assert.Null(defaultOption.OwnedQuantity); // untracked art -> infinite for everyone
        Assert.Equal(2, altOption.OwnedQuantity);

        // No preference set yet: every slot defaults to the earliest non-promo printing.
        Assert.Equal(3, group.Slots.Count);
        Assert.All(group.Slots, s => Assert.Equal(LandmarkDefault, s.Reference));
        Assert.All(group.Slots, s => Assert.False(s.IsExplicitChoice));
    }

    [Fact]
    public async Task SetPreference_within_owned_quantity_is_reflected_in_options()
    {
        const string user = "alt-art-set-pref-user";
        await ImportAsync(
            TimestampLine() + Header +
            $"{LandmarkAlt};Icebound Tundra;Rare;2\n",
            user);

        using var setResponse = await SetPreferenceAsync(user,
            new SetAltArtPreferenceRequest(4, "LY", "R", [LandmarkAlt, LandmarkAlt, null]));
        Assert.Equal(System.Net.HttpStatusCode.NoContent, setResponse.StatusCode);

        using var optionsResponse = await GetOptionsAsync(user, new AltArtGroupKey(4, "LY", "R"));
        var group = Assert.Single((await optionsResponse.Content.ReadFromJsonAsync<List<AltArtOptionsResponse>>())!);
        var slots = group.Slots.OrderBy(s => s.SlotIndex).ToList();

        Assert.Equal(LandmarkAlt, slots[0].Reference);
        Assert.True(slots[0].IsExplicitChoice);
        Assert.Equal(LandmarkAlt, slots[1].Reference);
        Assert.True(slots[1].IsExplicitChoice);
        Assert.Equal(LandmarkDefault, slots[2].Reference); // null -> falls back to default
        Assert.False(slots[2].IsExplicitChoice);
    }

    [Fact]
    public async Task SetPreference_rejects_choices_exceeding_owned_quantity()
    {
        const string user = "alt-art-shortfall-user";
        await ImportAsync(
            TimestampLine() + Header +
            $"{LandmarkAlt};Icebound Tundra;Rare;2\n",
            user);

        using var response = await SetPreferenceAsync(user,
            new SetAltArtPreferenceRequest(4, "LY", "R", [LandmarkAlt, LandmarkAlt, LandmarkAlt]));

        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        var shortfalls = (await response.Content.ReadFromJsonAsync<List<OwnershipShortfall>>())!;
        var shortfall = Assert.Single(shortfalls);
        Assert.Equal(LandmarkAlt, shortfall.Reference);
        Assert.Equal(3, shortfall.Requested);
        Assert.Equal(2, shortfall.Owned);
    }

    [Fact]
    public async Task SetPreference_rejects_wrong_slot_count_and_unknown_reference()
    {
        const string user = "alt-art-invalid-user";

        using var wrongCount = await SetPreferenceAsync(user,
            new SetAltArtPreferenceRequest(4, "LY", "R", [LandmarkAlt, LandmarkAlt]));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, wrongCount.StatusCode);

        using var unknownRef = await SetPreferenceAsync(user,
            new SetAltArtPreferenceRequest(4, "LY", "R", [LandmarkAlt, "NOT_A_REAL_REFERENCE", null]));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, unknownRef.StatusCode);
    }

    [Fact]
    public async Task SetPreference_enforces_single_slot_for_hero_cards()
    {
        const string user = "alt-art-hero-user";
        await ImportAsync(
            TimestampLine() + Header +
            $"{HeroAlt};Auraq & Kibble;Commun;1\n",
            user);

        using var tooMany = await SetPreferenceAsync(user,
            new SetAltArtPreferenceRequest(2, "LY", "C", [HeroAlt, HeroAlt]));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, tooMany.StatusCode);

        using var ok = await SetPreferenceAsync(user,
            new SetAltArtPreferenceRequest(2, "LY", "C", [HeroAlt]));
        Assert.Equal(System.Net.HttpStatusCode.NoContent, ok.StatusCode);
    }

    [Fact]
    public async Task ApplyToDeck_splits_quantity_across_chosen_arts_and_passes_through_the_rest()
    {
        const string user = "alt-art-deck-user";
        await ImportAsync(
            TimestampLine() + Header +
            $"{LandmarkAlt};Icebound Tundra;Rare;2\n",
            user);

        using var setResponse = await SetPreferenceAsync(user,
            new SetAltArtPreferenceRequest(4, "LY", "R", [LandmarkAlt, LandmarkAlt, null]));
        setResponse.EnsureSuccessStatusCode();

        var deck = new List<OwnershipCheckItem>
        {
            new(LandmarkDefault, 3),   // multi-art family -> rewritten per the user's slots
            new(MonoArt, 2),           // single printing -> untouched
            new("ALT_NOT_IN_CATALOG_X", 1), // unknown reference -> untouched
        };

        using var response = await ApplyToDeckAsync(user, deck);
        response.EnsureSuccessStatusCode();

        var result = (await response.Content.ReadFromJsonAsync<ApplyToDeckResponse>())!;

        // Lines[i] corresponds positionally to deck[i] — the multi-art family (index 0)
        // splits into 2 lines, the other two inputs (indexes 1-2) each stay a single line.
        Assert.Equal(3, result.Lines.Count);
        Assert.Contains(result.Lines[0], i => i.Reference == LandmarkAlt && i.Quantity == 2);
        Assert.Contains(result.Lines[0], i => i.Reference == LandmarkDefault && i.Quantity == 1);
        var monoLine = Assert.Single(result.Lines[1]);
        Assert.Equal(new OwnershipCheckItem(MonoArt, 2), monoLine);
        var unknownLine = Assert.Single(result.Lines[2]);
        Assert.Equal(new OwnershipCheckItem("ALT_NOT_IN_CATALOG_X", 1), unknownLine);
        Assert.Empty(result.Tokens);
    }

    [Fact]
    public async Task Token_options_treat_base_set_prints_as_infinite_but_require_ownership_for_others()
    {
        const string user = "alt-art-token-options-user";

        using var beforeOwning = await GetOptionsAsync(user, new AltArtGroupKey(31, "BR", "C"));
        var groupBefore = Assert.Single((await beforeOwning.Content.ReadFromJsonAsync<List<AltArtOptionsResponse>>())!);

        var baseSetOption = groupBefore.Options.Single(o => o.Reference == TokenDefault);
        var trackedOption = groupBefore.Options.Single(o => o.Reference == TokenTracked);
        Assert.Null(baseSetOption.OwnedQuantity); // base-set token print -> infinite for everyone
        Assert.Equal(0, trackedOption.OwnedQuantity); // non-base-set print -> real ownership, none yet

        // A token group is capped at 1 slot, like a hero.
        Assert.Single(groupBefore.Slots);

        // Selecting the non-base-set print requires owning at least one copy.
        using var rejected = await SetPreferenceAsync(user,
            new SetAltArtPreferenceRequest(31, "BR", "C", [TokenTracked]));
        Assert.Equal(System.Net.HttpStatusCode.Conflict, rejected.StatusCode);

        // Requesting more than the group's single slot is rejected regardless.
        using var tooMany = await SetPreferenceAsync(user,
            new SetAltArtPreferenceRequest(31, "BR", "C", [TokenTracked, TokenTracked]));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, tooMany.StatusCode);

        await ImportAsync(
            TimestampLine() + Header +
            $"{TokenTracked};Booda;Commun;1\n",
            user);

        using var accepted = await SetPreferenceAsync(user,
            new SetAltArtPreferenceRequest(31, "BR", "C", [TokenTracked]));
        Assert.Equal(System.Net.HttpStatusCode.NoContent, accepted.StatusCode);

        // The base-set print never needed ownership at all.
        using var baseSetChoice = await SetPreferenceAsync(user,
            new SetAltArtPreferenceRequest(31, "BR", "C", [TokenDefault]));
        Assert.Equal(System.Net.HttpStatusCode.NoContent, baseSetChoice.StatusCode);
    }

    [Fact]
    public async Task ApplyToDeck_adds_a_selected_token_as_its_own_line_item()
    {
        const string user = "alt-art-token-deck-user";

        // The base-set print is infinite, so no import/ownership is needed to select it.
        using var setResponse = await SetPreferenceAsync(user,
            new SetAltArtPreferenceRequest(31, "BR", "C", [TokenDefault]));
        setResponse.EnsureSuccessStatusCode();

        // A deck never lists tokens as its own cards — the chosen token art must be
        // injected even though nothing in the input deck references it.
        var deck = new List<OwnershipCheckItem> { new(MonoArt, 2) };
        using var response = await ApplyToDeckAsync(user, deck);
        response.EnsureSuccessStatusCode();

        var result = (await response.Content.ReadFromJsonAsync<ApplyToDeckResponse>())!;

        var monoLine = Assert.Single(Assert.Single(result.Lines));
        Assert.Equal(new OwnershipCheckItem(MonoArt, 2), monoLine);
        var tokenLine = Assert.Single(result.Tokens);
        Assert.Equal(new OwnershipCheckItem(TokenDefault, 1), tokenLine);
    }

    [Fact]
    public async Task Reconciler_reverts_excess_slots_when_owned_quantity_drops()
    {
        const string user = "alt-art-reconcile-user";
        await ImportAsync(
            TimestampLine() + Header +
            $"{LandmarkAlt};Icebound Tundra;Rare;2\n",
            user);

        using var setResponse = await SetPreferenceAsync(user,
            new SetAltArtPreferenceRequest(4, "LY", "R", [LandmarkAlt, LandmarkAlt, null]));
        setResponse.EnsureSuccessStatusCode();

        // EquinoxImportEvent.ApplyV1 only ever adds — each export is a delta of newly-
        // owned cards, not a full snapshot — so ownership can't actually shrink through
        // the import endpoint today. Exercise AltArtPreferenceReconciler directly
        // against a smaller projection, as it would run the day some event does reduce
        // what the player owns (a trade/loss event, a corrected import, etc.).
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
            var reconciler = scope.ServiceProvider.GetRequiredService<AltArtPreferenceReconciler>();
            var userId = await db.Users.Where(u => u.KeycloakId == user).Select(u => u.Id).SingleAsync();

            var reduced = new ProjectionState();
            reduced.Cards[LandmarkAlt] = 1;
            await reconciler.ReconcileAsync(userId, reduced, default);
            await db.SaveChangesAsync();
        }

        using var optionsResponse = await GetOptionsAsync(user, new AltArtGroupKey(4, "LY", "R"));
        var group = Assert.Single((await optionsResponse.Content.ReadFromJsonAsync<List<AltArtOptionsResponse>>())!);
        var slots = group.Slots.OrderBy(s => s.SlotIndex).ToList();

        // Only one copy of the alt art remains, so only one slot can still point to it —
        // the previously-explicit second slot falls back to the default, alongside the
        // third slot which was already default.
        var explicitSlots = slots.Where(s => s.IsExplicitChoice).ToList();
        var defaultSlots = slots.Where(s => !s.IsExplicitChoice).ToList();
        Assert.Single(explicitSlots);
        Assert.Equal(LandmarkAlt, explicitSlots[0].Reference);
        Assert.Equal(2, defaultSlots.Count);
        Assert.All(defaultSlots, s => Assert.Equal(LandmarkDefault, s.Reference));
    }
}
