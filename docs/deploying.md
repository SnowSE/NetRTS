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

`azd up` prints the URLs of the server and the database console. The server applies the database
migrations (`src/NetRts.Server/Data/Migrations`) every time it starts. `azd up` also asks for the
three Discord parameters described in [Discord sign-in](#discord-sign-in).

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
- **Restarts end live matches.** A redeploy or platform restart stops matches in progress and drops
  league matches that haven't finished (they live in memory too); finished matches, players, ratings and
  points are safe in PostgreSQL. Avoid deploying while league heats are being played.

### Settings

Every `NetRts:*` option in `src/NetRts.Server/appsettings.json` (tick speed, map size, limits) can
be overridden in Azure as an App Service application setting, using `__` for `:`. For example,
`NetRts__MaxLiveMatches = 100`. Set them in the portal under the web app → **Settings →
Environment variables**, or with `az webapp config appsettings set`.

## Discord sign-in

People sign in to the website with Discord to claim their bots (a blue check and their server
nickname) and to become game admins. Bots keep using API keys. Without these settings the site
works as before and hides the sign-in link.

1. At <https://discord.com/developers/applications>, create an application. Under **OAuth2**, copy
   the **Client ID** and reset/copy the **Client Secret**, and add a redirect for every address the
   site is served from: `https://netrts.snowse.io/signin-discord`, `http://localhost:5080/signin-discord`
   for local runs, and so on.
2. Get the server (guild) ID: in Discord, turn on **Settings → Advanced → Developer Mode**, then
   right-click the server and choose **Copy Server ID**. Members of that server are verified, their
   server nickname is shown, and anyone with the **Administrator** permission there (or the owner)
   is a game admin who can run preseason, season and championship matches. Admin rights are re-read
   whenever that person signs in again.
3. Configure the server:

   | Where | How |
   |---|---|
   | Local (`dotnet run`, `aspire run`) | `dotnet user-secrets set "Discord:ClientId" "…" --project src/NetRts.Server`, likewise `Discord:ClientSecret` and `Discord:ServerId` |
   | `azd up` | Answer the `discord-client-id`, `discord-client-secret` and `discord-server-id` prompts (stored in the azd environment) |
   | netrts.snowse.io | App settings `Discord__ClientId`, `Discord__ClientSecret`, `Discord__ServerId` and `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` on the web app |

`ASPNETCORE_FORWARDEDHEADERS_ENABLED` makes the server trust App Service's `X-Forwarded-Proto`.
Without it the callback URL it sends to Discord starts with `http://` and Discord rejects it. The
AppHost sets it for `azd` deployments. Sign-in cookies are encrypted with keys stored in the
database (`data_protection_keys`), so restarts and redeploys don't sign anyone out.

## Announcing results in Discord

The shared server posts to the coding-competition channel:

- **A new league match** (preseason, season, championship): its name and a link, as soon as an
  admin creates it, so people can point their bots at it.
- **League matches:** one message once every heat has finished, with each heat's top four and
  their points, linking to the match's scoreboard page.
- **Achievements:** the first bot ever to earn each one, straight away; and every night at
  midnight, a digest of everything earned that day, bot by bot.
- **The nightly recap:** last night's episode, with its poster and a link, at 6:30 a.m. local time
  (`Recap__AnnounceAt`), or as soon as it's published if that's later in the day. Once per episode.
- **Practice between real bots:** one short message each.
- **Practice against house bots:** one digest every hour (`Discord__ResultsDigestMinutes`).
- **House-bot exhibitions:** nothing.

To turn it on, open the channel's settings → **Integrations → Webhooks → New Webhook**, copy its
URL, and set two app settings on the web app:

| Setting | Value |
|---|---|
| `Discord__ResultsWebhookUrl` | the webhook URL (treat it as a secret: anyone with it can post) |
| `NetRts__PublicUrl` | `https://netrts.snowse.io`, so messages link to the matches |

Leave `Discord__ResultsWebhookUrl` unset anywhere else (local runs, private servers) and nothing is
posted. Messages show bot names and owners' nicknames as text with mentions turned off, so nobody
is pinged. Posting happens in the background, at most one message every two seconds; if Discord is
unreachable the message is logged and skipped.

## Starting a new season

Every result is recorded in the current season, whose label is empty. To close a season, label it
in the database; its leaderboards stay viewable with `?season=<label>`, and the next season starts
from zero:

```sql
UPDATE player_stats SET "Season" = '2026-27' WHERE "Season" = '';
UPDATE matches      SET "Season" = '2026-27' WHERE "Season" = '';
```

The same works for a beta test: play `Preseason` matches, then label them (e.g. `beta-2026`) or
just leave them on the separate preseason leaderboard.

## Replays and storage

Every finished match stores, besides its summary and replay (the seed and every command, in
`matches`), its frames: what spectators saw on every tick, Brotli-compressed in chunks of 100 ticks
(`match_frame_chunks`), with a manifest holding the map and play-by-play (`match_frames`). The
website's replay viewer plays these, so a replay always shows what happened, even after the rules
change. Sizes measured on real matches:

| Match | Frames |
|---|---|
| 1v1, ~1,000 ticks | ~170 KB |
| 16 players, 1,800 ticks | ~770 KB |

Matches recorded before frames existed get theirs rebuilt from the replay the first time someone
opens them (a few seconds; a long 16-player match ~15 s), kept only if the re-run ends exactly as
the match did. Each match also records the `GameRules.Version` it was played under; bump
`GameSimulation.Revision` when a change would make old replays play out differently.

To see how much space frames use, and to drop old ones (their matches, results and replays stay):

```sql
SELECT count(*), pg_size_pretty(sum(octet_length("Data"))) FROM match_frame_chunks;  -- PostgreSQL
DELETE FROM match_frames WHERE "CreatedAt" < now() - interval '180 days';            -- chunks go with them
```

## Nightly recap show

A Container Apps job makes a highlights video every night (see [`recap/README.md`](../recap/README.md))
and uploads it to blob storage. The server lists it on the home page and `#/recaps` by itself: the
job tells it directly, and the server also checks storage every 5 minutes, adding any episode it
doesn't have and removing any whose video was deleted. Nobody has to manage the list.

| Resource (rg-netrts, West US 3) | Purpose |
|---|---|
| `netrts-ai` (Foundry, `AIServices`) | Azure OpenAI deployment `recap-writer` (gpt-5.5, 50K tokens/minute) writes the script; Azure AI Speech voices the hosts. Keyless: managed identities only. |
| `netrtsrecaps…` storage account, private `recaps` container | The videos, posters and each episode's `episode.json`. No public access, no shared keys; the server signs short-lived read links. |
| `netrtsrecap…` container registry (Basic) | The job's image, built in Azure with `az acr build`. |
| `id-netrts-recap` | The job's identity: blob writer, Cognitive Services User and OpenAI User on `netrts-ai`, AcrPull. |
| `netrts-recap-env` + `netrts-recap` job | Consumption Container Apps. Runs at 09:00 UTC (2 a.m. MST), 2 vCPUs, up to 2 hours; logs to `netrts-logs`. |

Why not Claude: Claude on Foundry is an Azure Marketplace purchase, and the faculty subscription
(education credits) is refused ("can't be purchased using a free subscription"). Azure OpenAI models
are billed to the subscription itself. To use another model, list what's available with
`az cognitiveservices model list -l westus3` and pass `writerModel`/`writerModelVersion` to the template.

**Setting it up** (done once; safe to run again):

```bash
deploy/recap/setup.sh                 # storage, Foundry, registry, the image, the job
deploy/recap/setup.sh --wire-server   # also sets ConnectionStrings__recaps and Recap__ApiKey on the web app
```

`--wire-server` restarts the site (as any app setting change does), so run it when no matches are
on. Until the web app has both settings, and is running code with the recap endpoints, the job
starts each night, finds nothing to do, and stops. It also does nothing when its own settings are
missing.

- **Run it now:** `az containerapp job start -g rg-netrts -n netrts-recap`. Publishing a day again
  replaces that day's episode, and the job deletes the day's older files.
- **Remove an episode:** delete its folder (`<date>/`) from the `recaps` container; it leaves the
  site within 5 minutes.
- **What the show says about the schedule** comes from the server's `Recap` settings:
  `Recap__LeagueSchedule` ("League matches are played at 5 p.m. in GRSC 124."), `Recap__LeagueDays__0=Tuesday`
  and so on to name the next league day, and `Recap__Mentoring` (TA mentoring in the Commons).
- **Ladder snapshots.** At midnight (server time zone, `NetRts__TimeZone`, default `America/Denver`)
  the server records every ladder for the day just ended; the show compares tonight's with last
  night's, and the season ladder's top ten earn achievements there.
- **CI** builds the job's image in the registry on every push to `main` and points the job at it
  (`id-netrts-github` has Contributor on the registry and the job, granted by the template).

## Database migrations

The schema is managed by EF Core migrations, one set per provider, because generated migrations use
one database's column types: `Data/Migrations/Sqlite` and `Data/Migrations/Postgres`. After
changing the model in `Data/NetRtsDb.cs`, add a migration to both:

```bash
cd src/NetRts.Server
dotnet ef migrations add <Name> --context SqliteNetRtsDb   -o Data/Migrations/Sqlite   --namespace NetRts.Server.Data.Migrations.Sqlite
dotnet ef migrations add <Name> --context PostgresNetRtsDb -o Data/Migrations/Postgres --namespace NetRts.Server.Data.Migrations.Postgres
```

Databases created before migrations existed are recognised at startup (they have a `players` table
but no history) and marked as being at the first migration, so the rest applies on top and existing
bots keep their keys.

## Continuous deployment to netrts.snowse.io

Separately from the `azd` setup above, every push to `main` that passes CI publishes the server image
to Docker Hub (`snowcollege/netrts`) and then deploys that exact image (`sha-<commit>`) to
<https://netrts.snowse.io> (the `deploy` job in `.github/workflows/ci.yaml`).

| Resource (group `rg-netrts`, West US 3) | Purpose |
|---|---|
| `netrts-plan` (Linux, P0v3, 1 instance) | App Service plan. Keep it at one instance; live matches live in memory. |
| `netrts-jallen` web app | Runs the Docker Hub image. Always On, HTTPS only, system-assigned identity. Custom domain `netrts.snowse.io`: a Cloudflare CNAME to `netrts-jallen.azurewebsites.net`, DNS only (not proxied) so the free App Service managed certificate can renew. |
| `netrts-jallen-pg` | Azure Database for PostgreSQL 16, Burstable B1ms. Entra ID sign-in only; the firewall admits Azure services only. |
| `id-netrts-github` | Managed identity GitHub Actions signs in as (OIDC, `main` branch only), with Website Contributor on the web app. |
| `netrts-insights` + `netrts-logs` | Application Insights on a Log Analytics workspace (30-day retention, **1 GB/day cap**). The web app's `APPLICATIONINSIGHTS_CONNECTION_STRING` setting switches the server's OpenTelemetry export to it. |

The web app's connection string `netrtsdb` has no password; the server signs in to PostgreSQL with
the web app's identity, which is a Microsoft Entra admin on the database. GitHub needs the
`AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID` repository secrets (the identity's
client ID and the faculty subscription's tenant and ID) alongside the Docker Hub ones.

### Telemetry

With `APPLICATIONINSIGHTS_CONNECTION_STRING` set the server exports to Application Insights;
otherwise it exports to `OTEL_EXPORTER_OTLP_ENDPOINT` (the Aspire dashboard when run under Aspire).
In the portal, open `netrts-insights` for **Live metrics**, **Failures**, **Performance** and
**Logs**. The game's own metrics are under **Metrics** → namespace *azure.applicationinsights* (custom):

| Metric | What to watch |
|---|---|
| `netrts.tick.duration`, `netrts.tick.overruns` | Ticks slower than their interval mean the server is falling behind. |
| `netrts.matches.live` (by `netrts.match.status`) | Waiting and active matches, against `MaxLiveMatches`. |
| `netrts.matches.started` / `netrts.matches.completed` (by kind, end reason), `netrts.match.ticks` | Match volume and how matches end. |
| `netrts.house_bot.failures` (by bot) | House bot turns that threw. |
| `netrts.recorder.queue`, `netrts.recorder.failures`, `netrts.recorder.duration` | Finished matches not yet saved are lost on restart; alert on failures. |

Admins get links to these portal pages, plus saved Logs queries (exceptions, warnings and errors,
failed requests, slowest endpoints, game metrics, tick health), in a **Telemetry** section on their
account page once two app settings are set on the web app:

| Setting | Value |
|---|---|
| `NetRts__AppInsightsResourceId` | `/subscriptions/<subscription id>/resourceGroups/rg-netrts/providers/microsoft.insights/components/netrts-insights` |
| `NetRts__AzureTenant` | the subscription's tenant ID, so the links open in that directory |

Per-tick requests (state long-polls, command submissions, spectator polls and streams) and health
probes are left out of traces to keep volume down; the standard HTTP request metrics still count them.
