using System.IO.Compression;
using System.Security.Cryptography;
using AlteredOwnership.Server.Domain;
using AlteredOwnership.Server.Domain.Events;
using AlteredOwnership.Server.Domain.Services;
using AlteredOwnership.Server.Infrastructure.Auth;
using AlteredOwnership.Server.Infrastructure.Crypto;
using AlteredOwnership.Server.Infrastructure.EventSourcing;
using Microsoft.Extensions.Options;

namespace AlteredOwnership.Server.Endpoints;

// One owned card with its catalog metadata resolved to the requested locale (all card
// fields null when the card isn't catalogued yet).
public record CardCollectionItemResponse(
    string Reference, int Quantity,
    string? Name, string? ImagePath,
    string? Set, string? Faction, string? Rarity, string? CardType,
    string? Variation, IReadOnlyList<string>? SubTypes,
    bool? IsBanned, bool? IsSuspended,
    int? MainCost, int? RecallCost, int? Forest, int? Mountain, int? Ocean);

public static class CollectionEndpoints
{
    public static IEndpointRouteBuilder MapCollectionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/collection");

        group.MapGet("", async (
            CollectionQuery query,
            string? locale,
            CurrentUserAccessor currentUser,
            CollectionReader reader,
            CancellationToken ct) =>
        {
            var userId = await currentUser.GetOrProvisionInternalIdAsync(ct);
            var loc = string.IsNullOrWhiteSpace(locale) ? "en" : locale;
            return Results.Ok(await reader.GetCollectionAsync(userId, query, loc, ct));
        }).RequireAuthorization(AuthConstants.ReadPolicy);

        group.MapPost("import", async (
            IFormFile file,
            [Microsoft.AspNetCore.Mvc.FromForm] bool termsAccepted,
            CurrentUserAccessor currentUser,
            CollectionImporter importer,
            CardMetadataBackfiller backfiller,
            IOptions<EquinoxImportOptions> importOptions,
            CancellationToken ct) =>
        {
            if (!termsAccepted)
                return Results.BadRequest("Terms must be accepted to import.");

            if (file.Length == 0)
                return Results.BadRequest("Uploaded file is empty.");

            var allowUnencrypted = importOptions.Value.AllowUnencrypted;

            EquinoxCsvParser.ParseResult parsed;
            try
            {
                await using var fileStream = file.OpenReadStream();
                using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

                var entryName = allowUnencrypted ? "clear/collection.csv" : "encrypted/collection.csv.enc";
                var entry = archive.Entries.FirstOrDefault(e =>
                    string.Equals(e.FullName, entryName, StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                    return Results.BadRequest($"Archive does not contain {entryName}.");

                byte[] entryBytes;
                await using (var entryStream = entry.Open())
                {
                    using var buffer = new MemoryStream();
                    await entryStream.CopyToAsync(buffer, ct);
                    entryBytes = buffer.ToArray();
                }

                byte[] csvBytes;
                if (allowUnencrypted)
                {
                    csvBytes = entryBytes;
                }
                else
                {
                    // Server-side config: a bad/missing key is a misconfiguration, so let it surface.
                    var key = Convert.FromHexString(importOptions.Value.DecryptionKeyHex);
                    csvBytes = SecretBoxFile.Decrypt(entryBytes, key);
                }

                using var csvStream = new MemoryStream(csvBytes);
                parsed = await EquinoxCsvParser.ParseAsync(csvStream, ct);
            }
            catch (InvalidDataException)
            {
                return Results.BadRequest("Uploaded file is not a valid .zip archive.");
            }
            catch (CryptographicException)
            {
                return Results.BadRequest("Could not decrypt the collection file.");
            }
            catch (FormatException ex)
            {
                return Results.BadRequest(ex.Message);
            }

            var userId = await currentUser.GetOrProvisionInternalIdAsync(ct);
            var payload = EquinoxImportEvent.Build(
                termsAccepted,
                parsed.Rows.Select(r => new EquinoxImportEvent.PayloadV1.Item(r.Reference, r.Quantity)).ToList());

            try
            {
                await importer.ImportAsync(userId, payload, parsed.ExportedAt, ct);
            }
            catch (DuplicateImportException)
            {
                return Results.Text("Cet export a déjà été importé.", "text/plain", null, StatusCodes.Status409Conflict);
            }
            catch (DuplicateUniquesException ex)
            {
                var refs = string.Join(", ", ex.References);
                return Results.Text(
                    $"Les uniques suivantes ont déjà été importées : {refs}",
                    "text/plain", null, StatusCodes.Status409Conflict);
            }
            catch (ConflictingUniquesException ex)
            {
                var refs = string.Join(", ", ex.References);
                return Results.Text(
                    $"Les uniques suivantes appartiennent déjà à un autre joueur : {refs}",
                    "text/plain", null, StatusCodes.Status409Conflict);
            }

            // Import committed: backfill catalog metadata for any newly-owned references
            // (best-effort, outside the event transaction).
            await backfiller.BackfillAsync(userId, ct);

            return Results.NoContent();
        })
        .RequireAuthorization(AuthConstants.ImportPolicy);

        return routes;
    }
}
