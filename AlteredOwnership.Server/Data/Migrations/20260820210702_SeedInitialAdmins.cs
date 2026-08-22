using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;

#nullable disable

namespace AlteredOwnership.Server.Data.Migrations
{
    /// <inheritdoc />
    // Bootstraps the first admin(s) from config (InitialAdmins:KeycloakIds, a single
    // comma-separated string) so nobody has to hand-edit the Users table before the
    // promote-role endpoint has an admin to call it. EF Core migrations only support a
    // parameterless constructor (no constructor DI, verified — Activator.CreateInstance,
    // not the app's service provider), so this reads config the same way Program.cs's
    // builder would — appsettings.json/appsettings.<ASPNETCORE_ENVIRONMENT>.json if
    // present (both optional; the standalone migrations image ships neither) plus
    // environment variables (InitialAdmins__KeycloakIds=id1,id2) — rather than via
    // IOptions. A single comma-separated value (vs. indexed __0/__1/__N keys) because
    // the ops deploy repo's plain-.env + docker-compose ${VAR} substitution has no
    // convention for binding an unbounded indexed list.
    public partial class SeedInitialAdmins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var keycloakId in LoadKeycloakIds())
            {
                var id = Guid.NewGuid();
                migrationBuilder.Sql($"""
                    INSERT INTO "Users" ("Id", "KeycloakId", "Role", "CreatedAt")
                    VALUES ('{id}', '{Escape(keycloakId)}', 'Admin', now())
                    ON CONFLICT ("KeycloakId") DO UPDATE SET "Role" = 'Admin';
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Demotes rather than deletes: these Users rows may since have real
            // CardOwnership/OwnershipEvent history tied to them.
            foreach (var keycloakId in LoadKeycloakIds())
            {
                migrationBuilder.Sql($"""
                    UPDATE "Users" SET "Role" = 'Player' WHERE "KeycloakId" = '{Escape(keycloakId)}';
                    """);
            }
        }

        private static IReadOnlyList<string> LoadKeycloakIds()
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var raw = configuration["InitialAdmins:KeycloakIds"];
            if (string.IsNullOrWhiteSpace(raw)) return [];

            return raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct()
                .ToList();
        }

        private static string Escape(string value) => value.Replace("'", "''");
    }
}
