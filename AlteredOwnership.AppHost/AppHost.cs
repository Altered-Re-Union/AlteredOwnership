var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache");

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();

var ownershipDb = postgres.AddDatabase("ownershipdb");

var server = builder.AddProject<Projects.AlteredOwnership_Server>("server")
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(ownershipDb)
    .WaitFor(ownershipDb)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithReference(server)
    .WaitFor(server);

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
