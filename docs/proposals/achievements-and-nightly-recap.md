# Proposal: achievements and the nightly recap

Status: **implemented** (October 2026). Decisions from review:

- Achievements belong to the bot and roll up to the owner's account.
- Everything but the 🏠 first steps needs a real opponent: not a house bot, and not a bot with the same owner.
  A win handed over by a surrender before tick 300 doesn't count as a win.
- The hosts are game units (Zip the Scout, Sarge the Soldier, Quill the Archer) talking like pod-race announcers.
- An Azure OpenAI model (gpt-5.5) in Microsoft Foundry writes the script (Claude can't be bought on the education subscription); Azure AI Speech voices it; Azure Container Apps renders it.
- Quiet nights get a short episode; no episode runs past 10 minutes.
- Every episode covers what's coming up (league matches at 5 p.m. in GRSC 124) and reminds people about TA mentoring in the Commons.

Where it lives: `src/NetRts.Server/Achievements` (catalog, evaluation, backfill), `src/NetRts.Server/Recaps`
(nightly ladder snapshots, the brief, episodes), `recap/` (the job), `deploy/recap/main.bicep` (Azure),
`docs/bot-guide.md#achievements` (the player-facing list). Thresholds may still be tuned against real play.

## 1. Achievements

### How they'd work

- **Earned by a bot**, shown on the bot's page; a verified owner's account page rolls up every
  achievement any of their bots holds (counted once per person).
- **What counts:** a match counts if it finished and had at least one opponent. Achievements marked
  🏠 can be earned against house bots; everything else needs at least one *real* (non-house) bot
  in the match, so they can't be farmed against `sitter`. League-only ones say so.
- **Awarded once**, when a match is recorded (or by the nightly job, for history-based ones),
  stored like `account_milestones` today: (bot, achievement id, earned at, match id).
- **Hidden** ones (🔒) show as "???" until someone earns one; the first earner gets a callout in
  Discord and on the recap show.
- **Tiers** (Bronze / Silver / Gold / Platinum) only for display and for "rarest achievement" in
  the recap.
- **Retroactive:** on release, a one-off backfill runs over every stored match, so veterans don't
  start at zero.

**Data source** column: **R** = already in the stored result (`PlayerResultDto`, outcome, ticks),
**E** = derivable from the stored play-by-play events / replay commands, **H** = across a bot's
match history / ratings in the DB, **N** = needs a new per-match counter in the engine (cheap,
deterministic, added to `PlayerResultDto` as new fields).

### A. First steps (10)

| # | Name | Criteria | Tier | Data |
|---|---|---|---|---|
| 1 | Hello, World | Finish your first match. 🏠 | Bronze | R |
| 2 | First Blood | Kill an enemy unit. 🏠 | Bronze | R |
| 3 | Breaking Ground | Complete a building other than your starting Command Center. 🏠 | Bronze | E |
| 4 | Boot Camp | Train your first Soldier, Archer or Scout. 🏠 | Bronze | E |
| 5 | Lab Coat | Complete your first research. 🏠 | Bronze | E |
| 6 | Training Wheels Off | Beat a house bot. 🏠 | Bronze | R |
| 7 | Full House | Beat all four house bots (rusher, economist, balanced, sitter), any number of matches. 🏠 | Silver | H |
| 8 | Blue Check | Get your bot claimed by a Discord-verified owner. | Bronze | H |
| 9 | Worthy Opponent | Play a match against another real bot. | Bronze | R |
| 10 | Suit Up | Play in a league match (preseason, season or championship). | Bronze | R |

### B. Winning (13)

| # | Name | Criteria | Tier | Data |
|---|---|---|---|---|
| 11 | First Win | Win a match against a real bot. | Bronze | R |
| 12 | Double Digits | 10 career wins against real bots. | Silver | H |
| 13 | Centurion | 100 career wins against real bots. | Gold | H |
| 14 | Hat Trick | Win 3 matches against real bots in a row. | Silver | H |
| 15 | Unstoppable | Win 10 matches against real bots in a row. | Gold | H |
| 16 | Blitz | Win by elimination before tick 300. | Silver | R |
| 17 | Flawless Victory | Win without losing a single unit or building. | Gold | R |
| 18 | Photo Finish | Win on the time limit by less than 2% of the runner-up's score. | Silver | R |
| 19 | Buzzer Beater | Win by elimination in the last 60 ticks before the limit. | Silver | R |
| 20 | Comeback Kid | Win after trailing the leader's score by 50%+ at the halfway tick. | Gold | E (frames) |
| 21 | Last Bot Standing | Win a match with 8 or more players. | Gold | R |
| 22 | Battle Royale | Win a 16-player match. | Platinum | R |
| 23 | Giant Killer | Beat a bot rated 200+ above you in a rated match. | Gold | H |

### C. Economy (11)

| # | Name | Criteria | Tier | Data |
|---|---|---|---|---|
| 24 | Ore Inspector | Gather 1,000 ore in one match. 🏠 | Bronze | R |
| 25 | Ore Baron | Gather 10,000 ore in one match. | Silver | R |
| 26 | Ore Tycoon | Gather 20,000 ore in one match. | Gold | R |
| 27 | Strip Miner | Exhaust 3 resource deposits in one match. | Silver | E |
| 28 | Expansionist | Complete 3 Resource Depots in one match. | Bronze | E |
| 29 | Second Home | Complete a second Command Center. | Silver | E |
| 30 | Union Strong | Have 30 workers alive at once. | Silver | N |
| 31 | Dragon's Hoard | Bank 3,000 unspent ore at one moment. | Bronze | N |
| 32 | Waste Not | Finish a match of 600+ ticks without ever losing ore to the storage cap. | Gold | N |
| 33 | Efficiency Expert | Research Harvesting 2. | Bronze | E |
| 34 | Lifetime Haul | Gather 1,000,000 ore across all matches. | Platinum | H |

### D. Combat (14)

| # | Name | Criteria | Tier | Data |
|---|---|---|---|---|
| 35 | Massacre | Kill 50 enemy units in one match. | Silver | R |
| 36 | Wrecking Crew | Destroy 10 enemy buildings in one match. | Silver | R |
| 37 | Regicide | Destroy an enemy Command Center. | Bronze | E |
| 38 | Body Count | 1,000 career unit kills. | Gold | H |
| 39 | Hold the Line | Your guard towers kill 20 units in one match. | Silver | N |
| 40 | Tower Defense | Complete 6 guard towers in one match. | Bronze | E |
| 41 | Robin Hood | Win having trained only Archers (no Soldiers or Scouts). | Gold | E |
| 42 | Shield Wall | Win having trained only Soldiers (no Archers or Scouts). | Silver | E |
| 43 | Cavalry Charge | Win having trained only Scouts as combat units. | Gold | E |
| 44 | Worker Uprising | Win without ever building a Barracks. | Platinum | E |
| 45 | Arms Race | Research Weapons 2 and Armor 2 in the same match. | Silver | E |
| 46 | Fully Loaded | Research all 8 upgrades in one match. | Gold | E |
| 47 | 🔒 Super Soldier | Take the serum. (The existing secret; this replaces the "serum" milestone.) | Gold | E |
| 48 | 🔒 Not a Perfect Soldier | Kill 10 enemy units in one match with serum-boosted Soldiers. | Platinum | N |

### E. Speed and precision (7)

| # | Name | Criteria | Tier | Data |
|---|---|---|---|---|
| 49 | Quick Draw | Complete a Barracks before tick 60. | Silver | E |
| 50 | First to the Fight | Your first combat unit is ready before tick 75. | Silver | E |
| 51 | Eagle Eye | See an enemy Command Center before tick 100. | Silver | N |
| 52 | Clean Code | Finish a match of 300+ ticks with zero rejected or failed commands. | Silver | N |
| 53 | Less Is More | Win against a real bot with fewer than 50 commands submitted. | Gold | E |
| 54 | Busy Bees | Finish a 600+ tick match with no worker idle for more than 10 ticks straight. | Gold | N |
| 55 | Set It and Forget It | Set rally points on 3 different buildings in one match. | Bronze | E |

### F. Showing up (10)

| # | Name | Criteria | Tier | Data |
|---|---|---|---|---|
| 56 | Regular | Play on 7 different days. | Bronze | H |
| 57 | Marathon | Play 100 matches. | Silver | H |
| 58 | Iron Bot | Play 1,000 matches. | Gold | H |
| 59 | Night Owl | Finish a match between midnight and 4 a.m. (Mountain time). | Bronze | R |
| 60 | Welcome Back | Win within a day of returning from 30+ days away. | Silver | H |
| 61 | Social Butterfly | Play against 10 different real bots. | Silver | H |
| 62 | Rivalry | Play the same real bot 10 times. | Bronze | H |
| 63 | Nemesis Defeated | Beat a bot that has beaten you 5 or more times. | Gold | H |
| 64 | Glow Up | Win rate over the last 20 matches is 25+ points higher than over the first 20. | Gold | H |
| 65 | Stable Owner | (Account) Own 3 verified bots that have each won a match against a real bot. | Gold | H |

### G. Ladder and league (6)

| # | Name | Criteria | Tier | Data |
|---|---|---|---|---|
| 66 | Climber | Reach a rating of 1300 in any category (everyone starts at 1200). | Silver | H |
| 67 | Elite | Reach a rating of 1500 in any category. | Gold | H |
| 68 | Top Ten | Be in the top 10 of the Season ladder at a nightly snapshot. | Gold | H |
| 69 | Number One | Be #1 on the Season ladder at a nightly snapshot. | Platinum | H |
| 70 | Heat Winner | Win a Season heat. | Gold | R |
| 71 | Champion | Win a Championship heat. | Platinum | R |

### H. Just for fun (4)

| # | Name | Criteria | Tier | Data |
|---|---|---|---|---|
| 72 | Pacifist | Win on score without killing a single enemy unit. | Gold | R |
| 73 | Turtle Power | Survive to the time limit with 8 or more guard towers standing. | Silver | N |
| 74 | Know When to Fold 'Em | Surrender a match. 🏠 | Bronze | R |
| 75 | 🔒 By a Thread | Win with your last Command Center below 10% health. | Gold | N |

Tally: Bronze 21 · Silver 24 · Gold 24 · Platinum 6; hidden 3. About a third need small new engine
counters (N); the rest work on data we already keep, so the backfill covers them.

### Questions for you on achievements

1. Per **bot** with an account rollup (as above), or per **account** only?
2. Is the 🏠 / real-opponent split right? Today most matches are against house bots, so the
   non-🏠 ones will be rare early on — which may be exactly what you want.
3. Should achievements show **in-game** (a spectator toast when one is earned mid-match), or only
   on profile pages and Discord?
4. Any you'd cut, rename, or retune (thresholds like 10,000 ore or 50 kills are guesses until I
   check real match distributions — I'll calibrate against the production match history before
   building)?

## 2. Nightly recap show

### The show

A 2–4 minute "highlights desk" episode, generated every night for the previous 24 hours:

1. **Cold open** — the hosts, the date, "Welcome back to…".
2. **New on the grid** — bots registered / first matches played, with their owners' Discord avatars.
3. **Match of the night** — a short clip rendered from the closest/most dramatic match's frames
   (Photo Finish, Comeback, biggest kill count), using the existing 3D spectator view.
4. **Strategy watch** — "new strategies we saw": classified from each bot's build order and unit
   mix (e.g. *archer rush*, *tower turtle*, *fast expand*, *worker uprising*) and flagged when a bot
   switches style or a style appears for the first time.
5. **Leaderboard** — top 10 by category with movement arrows; biggest climbers and fallers.
6. **Achievements unlocked** — who earned what, rarest first; first-ever earners of hidden ones.
7. **Missing persons** — bots active in the last month that haven't played in 7+ days.
8. **Power plays** and **surprises** — standout single-match performances; upsets, comebacks, first wins, losing streaks snapped.
9. **Rivalry watch** — pairs of real bots that played each other repeatedly.
10. **Coming up** and **TA mentoring** — every episode.
11. **Sign-off**.

### The hosts

Three of the game's units behind a sports desk, drawn as badgers in their unit's kit, talking like
pod-race announcers (original lines, no film quotes):

- **Zip the Scout**: breathless play-by-play, goggles and scarf.
- **Sarge the Soldier**: big, booming, deadpan; helmet and shield.
- **Quill the Archer**: the precise, slightly posh statistician; hood, spectacles, quiver.

Animated as 2D vector puppets in a broadcast-style HTML page: mouth shapes driven by TTS viseme
timing, blinks, head bobs, cut-aways to graphics. Rendered to MP4 with the same
Playwright → ffmpeg frame-capture approach `promo/render.mjs` already uses.

### Pipeline (proposed)

```
nightly (02:00 MT)
  Azure Container Apps Job  (Node + Playwright + ffmpeg image; App Service B1 can't render video)
    1. GET  /api/v1/recaps/data?since=…     server builds the facts: new bots, standings + deltas,
                                            achievements, strategies, inactive bots, match of the night
    2. Script writer                        turns facts into a host dialogue (JSON lines per speaker)
    3. Azure AI Speech                      one voice per host, SSML "excited"/"cheerful" styles,
                                            audio + viseme events; Grumblo = alien gibberish voice
    4. Render                               broadcast page + match clip → MP4 (1080p, ~3 min)
    5. Upload                               Azure Blob Storage (public read container, CDN optional)
    6. POST /api/v1/recaps                  server records title, date, blob URL, thumbnail, duration
Website
  Home page: "Last night's recap" card with the newest episode (and a play button).
  #/recaps:  history of every episode, newest first, with thumbnails and the dialogue as a transcript.
```

New Azure resources (added to the AppHost so `azd up` provisions them): a Storage account with a
`recaps` blob container, an Azure AI Speech resource, and a Container Apps environment + scheduled
job. The job calls the server's API with a dedicated recap key, so it doesn't need database access
through the private network.

### Questions for you on the recap

1. **Script writing:** Claude (via the Anthropic API) writes far better banter and spots stories in
   the data, at a few cents an episode; templates are free and predictable but get repetitive after
   a week. My recommendation: Claude, with the facts passed in as structured data and a template
   fallback if the call fails.
2. **Voices:** Azure AI Speech (automated, has excited speaking styles and visemes for lip sync), or
   reuse the Chatterbox voices from `tools/voices` (better delivery, but that pipeline runs by hand
   on a GPU machine, so it can't run nightly unattended). Recommendation: Azure AI Speech.
3. **Quiet nights:** skip the episode when fewer than N matches were played, or always publish a
   (short) one?
4. **Discord:** also post the link to the results channel each night?
