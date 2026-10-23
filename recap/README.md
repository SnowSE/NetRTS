# The nightly recap show

Every night a Container Apps job makes a short highlights show about the day's matches. It's hosted by
three of the game's units at a sports desk: **Zip** the Scout (breathless play-by-play), **Sarge** the
Soldier (a gruff drill sergeant, deadpan) and **Quill** the Archer (the stats). The site lists every episode on
`#/recaps`, and the home page shows last night's.

```text
1. GET  /api/v1/recaps/brief      the day's facts from the game server (X-Recap-Key)
2. script                         an Azure OpenAI model (gpt-5.5) in Microsoft Foundry writes the dialogue
                                  as JSON (src/script.mjs); a plain template takes over if it fails
3. voices                         Azure AI Speech, one neural voice per host, with visemes (mouth shapes)
                                  that drive each host's voice meter and movement
4. render                         show/index.html in headless Chromium, frame by frame, into ffmpeg
5. upload                         the MP4, a poster and episode.json to the private "recaps" blob container
6. POST /api/v1/recaps            the server records the episode at once (it would also find episode.json
                                  in storage within 5 minutes); it serves short-lived links to the video
```

What's in the brief: totals, new bots, the ladders (with yesterday's ranks), rating movers,
achievements earned (first-ever first), strategies (new ones, and bots switching style), the match of
the night (with a replay of its busiest stretch), power plays, surprises (upsets, comebacks, first wins,
losing streaks snapped), rivalries, bots that haven't played in a week, what's coming up (league
matches at 5 p.m. in GRSC 124, and any league match taking players) and the TA mentoring reminder.
Both reminders are in every episode. A quiet day (fewer than 5 matches between real bots) gets a short
show. The length follows the day (about 1.5 minutes when quiet, 4 for a handful of matches, up to 8 for
a big league night), and nothing runs past 10: the script is written to a word budget, and if the
voices still run long, lines from the middle are dropped (never the open, coming up, mentoring or sign-off).

**It only runs when it's set up.** Without its settings (below), or against a server without the
recap endpoints turned on, it logs what's missing and exits successfully without doing anything.

## Running it locally

Needs Node 22+, ffmpeg on the path, and Chromium (or `CHROMIUM_CHANNEL=msedge` for the installed Edge).

```bash
cd recap && npm ci
# against a local server started with Recap__ApiKey=dev-key:
NETRTS_URL=http://localhost:5080 RECAP_KEY=dev-key CHROMIUM_CHANNEL=msedge \
  node src/index.mjs --local --date 2026-10-21 --script template --voices silent
# out/2026-10-21/recap.mp4, poster.jpg, brief.json, script.json
```

`--local` skips the upload and publish (`--publish` does them anyway). `--voices silent` times each
line from its length and flaps the mouths without audio; `--script template` skips the model. Under
`aspire run`, the AppHost has a `recap` container you can start from the dashboard: a template
episode with silent hosts, uploaded to Azurite, which the server then lists.

## Settings

| Variable | |
|---|---|
| `NETRTS_URL`, `RECAP_KEY` | The game server, and its `Recap:ApiKey`. |
| `RECAP_DATE` | The day to cover, `yyyy-MM-dd`. Default: yesterday in the server's time zone. |
| `AZURE_OPENAI_ENDPOINT`, `RECAP_MODEL` | `https://<resource>.openai.azure.com/` and the deployment (default `recap-writer`). Signs in with the managed identity (`AZURE_CLIENT_ID`), or `AZURE_OPENAI_API_KEY`. |
| `SPEECH_ENDPOINT` | `https://<resource>.cognitiveservices.azure.com/` (the same Foundry resource). Managed identity, or `SPEECH_KEY`. |
| `RECAP_BLOB_URL` | `https://<account>.blob.core.windows.net/`, managed identity. Or `RECAP_BLOB_CONNECTION` (Azurite). |
| `RECAP_FPS` | Frames per second (default 24). |

The voices and their styles are in `src/hosts.mjs`. The hosts are the game's own units, rendered from
its 3D models into `show/hosts/*.png` by `tools/render-hosts.mjs` (run it again when the models in
`src/NetRts.Server/wwwroot/3d` change; it needs a game server running for their files). The
broadcast is `show/show.js`: `load(show)` once, then `seek(t)` for any second, with no clock involved,
so every frame comes out exact.
