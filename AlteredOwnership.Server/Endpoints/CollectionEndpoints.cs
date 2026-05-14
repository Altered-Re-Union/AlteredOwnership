using AlteredOwnership.Server.Auth;
using AlteredOwnership.Server.Cards;
using AlteredOwnership.Server.Events;
using AlteredOwnership.Server.Services;

namespace AlteredOwnership.Server.Endpoints;

public static class CollectionEndpoints
{
    public record CardOwnershipResponse(string Reference, int Quantity, bool IsUnique);

    // Equinox does not yet ship an export date in the CSV. Hardcode the date the
    // user provided until Equinox adds it to the file.
    private static readonly DateTimeOffset PlaceholderExportedAt =
        new(2026, 5, 12, 0, 0, 0, TimeSpan.Zero);

    public static IEndpointRouteBuilder MapCollectionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/collection");

        group.MapGet("", async (
            CurrentUserAccessor currentUser,
            CollectionReader reader,
            CancellationToken ct) =>
        {
            var userId = await currentUser.GetOrProvisionInternalIdAsync(ct);
            var rows = await reader.GetCollectionAsync(userId, ct);
            return Results.Ok(rows.Select(r =>
                new CardOwnershipResponse(r.CardReference, r.Quantity, r.IsUnique)));
        }).RequireAuthorization(AuthConstants.ReadPolicy);

        group.MapPost("import", async (
            IFormFile file,
            CurrentUserAccessor currentUser,
            CollectionWriter writer,
            CancellationToken ct) =>
        {
            if (file.Length == 0)
                return Results.BadRequest("Uploaded file is empty.");

            await using var stream = file.OpenReadStream();
            IReadOnlyList<EquinoxCsvParser.Row> rows;
            try
            {
                rows = await EquinoxCsvParser.ParseAsync(stream, ct);
            }
            catch (FormatException ex)
            {
                return Results.BadRequest(ex.Message);
            }

            var userId = await currentUser.GetOrProvisionInternalIdAsync(ct);
            var payload = EquinoxImportEvent.Build(
                PlaceholderExportedAt,
                rows.Select(r => new EquinoxImportEvent.PayloadV1.Item(r.Reference, r.Quantity)).ToList());

            await writer.ImportAsync(userId, payload, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(AuthConstants.ImportPolicy)
        .DisableAntiforgery();

        return routes;
    }
}
