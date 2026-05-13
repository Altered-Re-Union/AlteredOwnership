using AlteredOwnership.Server.Auth;
using AlteredOwnership.Server.Events;
using AlteredOwnership.Server.Services;

namespace AlteredOwnership.Server.Endpoints;

public static class CollectionEndpoints
{
    public record ImportRequest(DateTimeOffset ExportedAt, IReadOnlyList<ImportRequest.Item> Cards)
    {
        public record Item(string Reference, int Quantity);
    }

    public record CardOwnershipResponse(string Reference, int Quantity, bool IsUnique);

    public static IEndpointRouteBuilder MapCollectionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/collection").RequireAuthorization();

        group.MapGet("", async (
            CurrentUserAccessor currentUser,
            CollectionReader reader,
            CancellationToken ct) =>
        {
            var userId = await currentUser.GetOrProvisionInternalIdAsync(ct);
            var rows = await reader.GetCollectionAsync(userId, ct);
            return Results.Ok(rows.Select(r =>
                new CardOwnershipResponse(r.CardReference, r.Quantity, r.IsUnique)));
        });

        group.MapPost("import", async (
            ImportRequest request,
            CurrentUserAccessor currentUser,
            CollectionWriter writer,
            CancellationToken ct) =>
        {
            var userId = await currentUser.GetOrProvisionInternalIdAsync(ct);
            var payload = EquinoxImportEvent.Build(
                request.ExportedAt,
                request.Cards.Select(c => new EquinoxImportEvent.PayloadV1.Item(c.Reference, c.Quantity)).ToList());

            await writer.ImportAsync(userId, payload, ct);
            return Results.NoContent();
        });

        return routes;
    }
}
