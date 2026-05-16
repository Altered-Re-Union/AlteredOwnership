using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache")
    .WithContainerName("altered-ownership-redis");

var postgres = builder.AddPostgres("postgres")
    .WithContainerName("altered-ownership-postgres")
    .WithDataVolume()
    .WithPgAdmin(pgadmin => pgadmin.WithContainerName("altered-ownership-pgadmin"));

var ownershipDb = postgres.AddDatabase("ownershipdb");

var server = builder.AddProject<Projects.AlteredOwnership_Server>("altered")
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(ownershipDb)
    .WaitFor(ownershipDb)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

if (builder.Environment.IsDevelopment())
{
    ownershipDb.WithCommand(
        name: "reset-database",
        displayName: "Reset database (drop + re-migrate)",
        executeCommand: async context =>
        {
            var logger = context.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ResetOwnershipDb");
            var commands = context.ServiceProvider.GetRequiredService<ResourceCommandService>();

            // Stop the server so it releases its pooled connections before we drop the db.
            await commands.ExecuteCommandAsync(
                server.Resource, KnownResourceCommands.StopCommand, context.CancellationToken);

            var serverConnStr = await postgres.Resource
                .GetConnectionStringAsync(context.CancellationToken);
            if (string.IsNullOrEmpty(serverConnStr))
                return CommandResults.Failure("No Postgres connection string available.");

            await using var conn = new NpgsqlConnection(serverConnStr);
            await conn.OpenAsync(context.CancellationToken);

            await using (var terminate = conn.CreateCommand())
            {
                terminate.CommandText = """
                    SELECT pg_terminate_backend(pid)
                    FROM pg_stat_activity
                    WHERE datname = 'ownershipdb' AND pid <> pg_backend_pid();
                    """;
                await terminate.ExecuteNonQueryAsync(context.CancellationToken);
            }

            await using (var drop = conn.CreateCommand())
            {
                drop.CommandText = "DROP DATABASE IF EXISTS \"ownershipdb\"";
                await drop.ExecuteNonQueryAsync(context.CancellationToken);
            }

            await using (var create = conn.CreateCommand())
            {
                create.CommandText = "CREATE DATABASE \"ownershipdb\"";
                await create.ExecuteNonQueryAsync(context.CancellationToken);
            }

            logger.LogInformation("ownershipdb dropped and recreated; starting server to replay migrations.");

            await commands.ExecuteCommandAsync(
                server.Resource, KnownResourceCommands.StartCommand, context.CancellationToken);

            return CommandResults.Success();
        },
        commandOptions: new CommandOptions
        {
            IconName = "ArrowCounterclockwise",
            IconVariant = IconVariant.Filled,
            ConfirmationMessage = "Drop the ownershipdb database, recreate it, and restart the server so EF Core re-applies all migrations?",
        });
}

builder.Build().Run();
