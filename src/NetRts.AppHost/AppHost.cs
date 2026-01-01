var builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL database
var password = builder.AddParameter("postgres-password", secret: true);
var postgres = builder.AddPostgres("postgres", password: password)
    .WithDataVolume("netrts_data")
    .WithPgWeb()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithPgAdmin(pgadmin => pgadmin.WithLifetime(ContainerLifetime.Persistent));

var netRtsDb = postgres.AddDatabase("netrtsdb");

// The API project now serves both the API and the Blazor client
var api = builder.AddProject<Projects.NetRts_Api>("api")
    .WithReference(netRtsDb)
    .WaitFor(netRtsDb)
    .WithExternalHttpEndpoints();

builder.Build().Run();
