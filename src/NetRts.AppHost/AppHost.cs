using Azure.Provisioning.AppService;

var builder = DistributedApplication.CreateBuilder(args);

// Locally (`aspire run`): the server, a PostgreSQL container and pgweb.
// Published (`azd up`): Azure App Service + Azure Database for PostgreSQL inside a private network.
//
//   internet ──HTTPS──► server (App Service) ──┐
//   your IPs ──HTTPS──► dbconsole (pgweb)  ────┤ apps subnet (VNet integration)
//                                              ▼
//                           data subnet: private endpoint ──► PostgreSQL (no public access)
//
// The database accepts Microsoft Entra ID sign-ins only. The server uses its managed identity;
// people use their own Entra account through the database console. See docs/deploying.md.

var network = builder.AddAzureVirtualNetwork("netrts-vnet", "10.40.0.0/16");
var appsSubnet = network.AddSubnet("apps", "10.40.0.0/24");
var dataSubnet = network.AddSubnet("data", "10.40.1.0/27");

builder.AddAzureAppServiceEnvironment("netrts-env")
    .WithDelegatedSubnet(appsSubnet)
    .ConfigureInfrastructure(infra =>
    {
        // One small instance: live matches are held in the server's memory, so never scale out.
        var plan = infra.GetProvisionableResources().OfType<AppServicePlan>().Single();
        plan.Sku = new AppServiceSkuDescription { Name = "B1", Capacity = 1 };
    });

var postgres = builder.AddAzurePostgresFlexibleServer("postgres")
    .RunAsContainer(container => container
        .WithImageTag("16") // match the PostgreSQL version Azure deploys
        .WithDataVolume("netrts_data")
        .WithLifetime(ContainerLifetime.Persistent)
        .WithPgWeb());

// Reachable only from inside the network: this also turns off the server's public endpoint.
dataSubnet.AddPrivateEndpoint(postgres);

var db = postgres.AddDatabase("netrtsdb");

// The nightly recap show's videos (Azurite locally). The server hands out short-lived links to them.
var recapVideos = builder.AddAzureStorage("storage")
    .RunAsEmulator(azurite => azurite.WithLifetime(ContainerLifetime.Persistent))
    .AddBlobs("recaps");

// The key the recap job uses to read the night's brief and publish episodes.
var recapKey = builder.AddParameter("recap-key", new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);

var server = builder.AddProject<Projects.NetRts_Server>("server")
    .WithReference(db)
    .WaitFor(db)
    .WithReference(recapVideos)
    .WithEnvironment("Recap__ApiKey", recapKey)
    .WithExternalHttpEndpoints()
    .PublishAsAzureAppServiceWebsite((_, site) =>
    {
        // Matches tick on background timers; without Always On, App Service unloads an idle app mid-match.
        site.SiteConfig.IsAlwaysOn = true;
    });

if (builder.ExecutionContext.IsRunMode)
{
    // The recap job, started by hand from the dashboard: a template script and silent hosts (no
    // Azure OpenAI or Speech locally), uploaded to Azurite, where the server's sync finds it.
    builder.AddDockerfile("recap", "../../recap")
        .WithExplicitStart()
        .WithArgs("node", "src/index.mjs", "--local", "--publish", "--script", "template", "--voices", "silent", "--out", "/tmp/recap")
        .WithReference(recapVideos)
        .WithEnvironment("NETRTS_URL", server.GetEndpoint("http"))
        .WithEnvironment("RECAP_KEY", recapKey)
        .WithEnvironment("RECAP_DATE", () => DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
}

if (builder.ExecutionContext.IsPublishMode)
{
    // Discord sign-in (see docs/deploying.md). Locally the server reads these from its own user secrets.
    server
        .WithEnvironment("Discord__ClientId", builder.AddParameter("discord-client-id"))
        .WithEnvironment("Discord__ClientSecret", builder.AddParameter("discord-client-secret", secret: true))
        .WithEnvironment("Discord__ServerId", builder.AddParameter("discord-server-id"))
        // App Service terminates HTTPS in front of the app; trust its X-Forwarded-Proto so the Discord
        // callback URL and the sign-in cookie use https.
        .WithEnvironment("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true");

    // A pgweb console inside the network, so the private database can still be inspected.
    // Only the addresses in NetRts:AdminIpAddresses (CIDR, e.g. "203.0.113.7/32") can open it.
    var adminIps = builder.Configuration.GetSection("NetRts:AdminIpAddresses").GetChildren()
        .Select(ip => ip.Value).OfType<string>().ToArray();
    if (adminIps.Length == 0)
    {
        throw new InvalidOperationException(
            "Set NetRts:AdminIpAddresses before deploying, e.g. " +
            "dotnet user-secrets set \"NetRts:AdminIpAddresses:0\" \"203.0.113.7/32\" --project src/NetRts.AppHost");
    }

    // Built from deploy/dbconsole (a one-line wrapper around the pgweb image): App Service deploys
    // images it builds and pushes to its own registry.
    builder.AddDockerfile("dbconsole", "../../deploy/dbconsole")
        .WithHttpEndpoint(targetPort: 8081)
        .WithExternalHttpEndpoints()
        .PublishAsAzureAppServiceWebsite((_, site) =>
        {
            site.SiteConfig.IPSecurityRestrictionsDefaultAction = SiteDefaultAction.Deny;
            site.SiteConfig.AllowIPSecurityRestrictionsForScmToUseMain = true;
            for (var i = 0; i < adminIps.Length; i++)
            {
                site.SiteConfig.IPSecurityRestrictions.Add(new AppServiceIPSecurityRestriction
                {
                    Name = $"admin-{i}",
                    IPAddressOrCidr = adminIps[i],
                    Action = "Allow",
                    Priority = 100 + i,
                });
            }
        });
}

builder.Build().Run();
