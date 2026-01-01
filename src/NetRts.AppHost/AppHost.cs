var builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL database
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin(pgadmin => pgadmin.WithLifetime(ContainerLifetime.Persistent));

var netRtsDb = postgres.AddDatabase("netrtsdb");

// The API project now serves both the API and the Blazor client
var api = builder.AddProject<Projects.NetRts_Api>("api")
    .WithReference(netRtsDb)
    .WithExternalHttpEndpoints();

builder.Build().Run();
