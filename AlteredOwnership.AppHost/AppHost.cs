var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache")
    .WithContainerName("altered-ownership-redis");

var postgres = builder.AddPostgres("postgres")
    .WithContainerName("altered-ownership-postgres")
    .WithDataVolume()
    .WithPgAdmin(pgadmin => pgadmin.WithContainerName("altered-ownership-pgadmin"));

var ownershipDb = postgres.AddDatabase("ownershipdb");

builder.AddProject<Projects.AlteredOwnership_Server>("altered")
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(ownershipDb)
    .WaitFor(ownershipDb)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

builder.Build().Run();
