# Running and deploying Badger Brawl

There are three ways to run the server. All of them use the same code; only where the database
lives changes.

| | Command | Database | Needs |
|---|---|---|---|
| Quickest | `dotnet run --project src/NetRts.Server` | SQLite file `netrts.db` | .NET 10 SDK |
| Local dev | `aspire run` | PostgreSQL in a Docker container | + Docker, Aspire CLI |
| Azure | `azd up` | Azure Database for PostgreSQL | + Azure subscription, `azd` |

The server picks PostgreSQL whenever Aspire hands it a `netrtsdb` connection string, and SQLite
otherwise (`src/NetRts.Server/Program.cs`). The orchestration for both the local and the Azure setup
lives in one file, `src/NetRts.AppHost/AppHost.cs`.

## Local development with Aspire

```bash
aspire run                       # or: dotnet run --project src/NetRts.AppHost
```

This starts:

- **PostgreSQL** in a container (persistent, with a `netrts_data` volume, so players and results
  survive restarts) plus **pgweb** to browse the tables;
- the **server**, wired to that database;
- the **Aspire dashboard** — its URL is printed in the console — with logs, traces and metrics for
  every request and tick, and links to the server and pgweb.

Install the Aspire CLI with `dotnet tool install -g Aspire.Cli` if `aspire` isn't on your path.

## Deploying to Azure

The AppHost describes the Azure setup too. The database lives in a private network: nothing on the
internet can reach it, and the only way in for people is a database console that answers only your
IP addresses.

```text
internet ──HTTPS──► server (App Service) ─────┐
your IPs ──HTTPS──► dbconsole (pgweb)  ───────┤ "apps" subnet (VNet integration)
                                              ▼
                       "data" subnet: private endpoint ──► PostgreSQL (public access disabled)
```

| Resource | Purpose |
|---|---|
| Virtual network `10.40.0.0/16` | Subnets `apps` (the web apps' outbound traffic) and `data` (the database's private endpoint). |
| App Service plan (Linux, **B1**, 1 instance) | Runs the server, the database console and the Aspire dashboard. |
| `server` web app | The Badger Brawl server, HTTPS, **Always On**, connected to the network. |
| `dbconsole` web app | [pgweb](https://github.com/sosedoff/pgweb), connected to the network. Every request from an address not on your list is refused, including to its deployment (SCM) site. |
| Aspire dashboard web app | Logs, traces and metrics from the deployed server. |
| Azure Container Registry | Holds the images that `azd` builds and pushes. |
| Managed identity | The server's identity: pulls images and signs in to PostgreSQL. |
| Azure Database for PostgreSQL flexible server | PostgreSQL 16, Burstable B1ms, 32 GB. **Public network access disabled**; reachable only through its private endpoint. Entra ID sign-in only; no passwords exist. |
| Private endpoint + private DNS zone | Gives the database a private address in the `data` subnet, and makes its normal host name resolve to that address inside the network. |

> Aspire marks its networking APIs (virtual network, subnets, private endpoints) as experimental, so a
> future Aspire release may rename them. The project opts in with `NoWarn ASPIREAZURE003` in
> `NetRts.AppHost.csproj`.

### Before the first deployment: your IP addresses

The console only answers addresses you list, and deploying fails until there is at least one. Find
yours (for example at <https://ifconfig.me>) and store it, plus any others, in the AppHost's user
secrets:

```bash
dotnet user-secrets set "NetRts:AdminIpAddresses:0" "203.0.113.7/32" --project src/NetRts.AppHost
dotnet user-secrets set "NetRts:AdminIpAddresses:1" "198.51.100.0/24" --project src/NetRts.AppHost   # e.g. the campus network
```

Use `/32` for a single address. To change the list later, edit the secrets and run `azd provision`.

### First deployment

```bash
az login                         # if you haven't already
azd auth login
azd init                         # run in the repo root; it detects the Aspire AppHost — pick an environment name, e.g. netrts-prod
azd up                           # choose subscription and region; provisions everything, builds and deploys
```

`azd up` prints the URLs of the server and the database console. The database tables are created the
first time the server starts.

Then make yourself a database administrator (once). Only the server's identity is one after
deployment:

```bash
az postgres flexible-server microsoft-entra-admin create \
  --resource-group <resource group> --server-name <postgres server name> \
  --object-id $(az ad signed-in-user show --query id -o tsv) \
  --display-name $(az ad signed-in-user show --query userPrincipalName -o tsv) \
  --type User
```

Both names are in the Azure portal, or in the output of `azd show`.

### Using the database console

There are no database passwords. You sign in with a short-lived Microsoft Entra token instead:

1. Get a token (valid for about an hour):
   ```bash
   az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv
   ```
2. Open the `dbconsole` URL and fill in its connection form: **Host** = the PostgreSQL server's host
   name (`<server>.postgres.database.azure.com`), **Port** = 5432, **User** = your Entra sign-in
   (e.g. `you@school.edu`), **Password** = the token, **Database** = `netrtsdb`, **SSL** = `require`.

An open session stays connected after the token expires; you only need a new token to connect again.

### Updating

```bash
azd deploy                       # rebuild and redeploy the apps only
azd up                           # also re-apply infrastructure changes from AppHost.cs
```

### Tearing down

```bash
azd down --purge                 # deletes the resource group and everything in it, including the database
```

### Why it's configured this way

- **One instance, never scaled out.** Live matches are held in the server's memory, so a second
  instance would split them. The plan is pinned to one B1 instance in `AppHost.cs`. B1 is ample: one
  match costs a few milliseconds of CPU per tick. Change the SKU there if you need more headroom
  (for example `P0v3`), but keep the capacity at 1.
- **Always On.** App Service unloads apps that receive no requests for a while. That would stop
  the background tick loops mid-match and drop finished matches that haven't been written to the
  database yet.
- **A private database.** The database has no public endpoint at all, so it can't be attacked from
  the internet even with a stolen identity. The console is the one way in for people, and it is
  locked to your addresses and still needs your Entra sign-in.
- **No database passwords.** The connection string handed to the server in Azure contains no
  credentials. The Azure PostgreSQL client (`AddAzureNpgsqlDbContext` in `Program.cs`) sees that and
  signs in with the app's managed identity. Locally the container connection string does contain a
  password, and the same client uses it.
- **Restarts end live matches.** A redeploy or platform restart stops matches in progress; finished
  matches, players and ratings are safe in PostgreSQL. Avoid deploying during a tournament round.

### Settings

Every `NetRts:*` option in `src/NetRts.Server/appsettings.json` (tick speed, map size, limits) can
be overridden in Azure as an App Service application setting, using `__` for `:`. For example,
`NetRts__MaxLiveMatches = 100`. Set them in the portal under the web app → **Settings →
Environment variables**, or with `az webapp config appsettings set`.
