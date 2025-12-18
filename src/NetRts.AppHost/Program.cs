var builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL database
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();

var netRtsDb = postgres.AddDatabase("netrtsdb");

// TODO: Fix project references when Aspire Projects namespace is generated correctly
// For now, build other projects directly without AppHost
// var api = builder.AddProject<Projects.NetRts_Api>("api")
//     .WithReference(netRtsDb)
//     .WithExternalHttpEndpoints();

// var client = builder.AddProject<Projects.NetRts_Client>("client")
//     .WithReference(api)
//     .WithExternalHttpEndpoints();

builder.Build().Run();
