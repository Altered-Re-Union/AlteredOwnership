using AlteredOwnership.Server.Domain.Services;
using AlteredOwnership.Server.Infrastructure.Auth;

namespace AlteredOwnership.Server.Endpoints;

public static class HistoryEndpoints
{
    public static IEndpointRouteBuilder MapHistoryEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/history");

        group.MapGet("", async (
            string? locale,
            CurrentUserAccessor currentUser,
            EventHistoryReader reader,
            CancellationToken ct) =>
        {
            var userId = await currentUser.GetOrProvisionInternalIdAsync(ct);
            var loc = string.IsNullOrWhiteSpace(locale) ? "en" : locale;
            return Results.Ok(await reader.ListAsync(userId, loc, ct));
        }).RequireAuthorization(AuthConstants.ReadPolicy);

        group.MapGet("{eventId:long}", async (
            long eventId,
            string? locale,
            CurrentUserAccessor currentUser,
            EventHistoryReader reader,
            CancellationToken ct) =>
        {
            var userId = await currentUser.GetOrProvisionInternalIdAsync(ct);
            var loc = string.IsNullOrWhiteSpace(locale) ? "en" : locale;
            var detail = await reader.GetDetailAsync(userId, eventId, loc, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        }).RequireAuthorization(AuthConstants.ReadPolicy);

        return routes;
    }
}
