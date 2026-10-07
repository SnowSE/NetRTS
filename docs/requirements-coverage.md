# Requirements coverage

How the implementation meets [`specs/001-rts-game-engine/spec.md`](../specs/001-rts-game-engine/spec.md).
"Engine" tests live in `tests/NetRts.Engine.Tests`, "Server" tests in `tests/NetRts.Server.Tests`.
Deliberate deviations are marked **Δ**.

## User stories

| Story | Status | Evidence |
|---|---|---|
| US1 View initial state with fog of war | Done | Engine `MapAndVisionTests` (starting units, vision radius 5 vs 6, enemy hidden, enemy orders hidden); Server `A_match_against_a_house_bot_starts_immediately_with_fog_applied`, `Game_endpoints_require_a_valid_api_key` |
| US2 Queue unit commands | Done | Engine `CommandTests` (move, range selectors, ownership, bounds, latest-command-wins); Server `Commands_are_validated_on_submit_and_executed_on_the_next_tick` |
| US3 Buildings and production | Done | Engine `EconomyAndBuildingTests` (workers construct, cost on start, occupied tile, production spawns adjacent, insufficient resources) |
| US4 Upgrades | Done | Engine `Research_enforces_prerequisites_and_reports_progress`, `Damage_is_reduced_by_armor_and_increased_by_weapon_upgrades`, mobility test |
| US5 Score and winner | Done | Engine `CombatAndVictoryTests` (elimination, time limit, draw, surrender, commands rejected after end); Server surrender/archive/rating test |

## Functional requirements

| FR | Implementation |
|---|---|
| 001 Create matches with unique ids | `POST /api/v1/matches`, `POST /api/v1/exhibitions`. **Δ** 2–4 players, not only 2. |
| 002 Grid map | `MapGenerator`. **Δ** default 64×64 (configurable 32–128) — 100×100 made games slow to develop for. |
| 003 Balanced starts | Four-way mirror-symmetric maps; tested in `Map_is_mirror_symmetric_so_every_start_is_fair`. |
| 004 Initial resources | 500 ore, 5 workers, a Command Center. |
| 005 Ticks at an interval | `MatchHost` tick loop; interval per match (100 ms–10 s, default 1 s). |
| 006 Status and duration | `MatchStatus`, `tick`, `outcome.ticks`. |
| 007–015 Game state | `GET …/state` → `GameStateDto`: own units/buildings (hp, status, construction %, production queues), resources, visibility grid, visible enemies and deposits, tick. |
| 016–022 Unit commands | `Move`, `Attack` (target or attack-move), `Gather`, `Stop`; selectors `"5-10"`, `"all"`, `"idle"`, `unitIds`, `unitType`; batches of up to 100. |
| 023–028 Persistent FIFO queue | 500-command queue per player, 100 executed per tick, remainder carried over; `QUEUE_FULL`. Tested in `Queue_is_capped_and_drained_a_fixed_number_of_commands_per_tick`. |
| 029 Latest command wins | Orders overwrite; tested. |
| 030 Queue size in state | `you.queuedCommands`, `you.queueCapacity`. |
| 031–036 Construction | `Build`: workers walk to the site, cost paid when ground breaks, progress = worker-ticks, `constructionPercent`. Site validation (bounds, rock, buildings, deposits, enemy units, prerequisites, ore). |
| 037–042 Production | `Produce` with `count`; queue of 5 per building; spawns adjacent. **Δ** cost is paid when queued rather than when the item reaches the front, so a rejection is immediate. |
| 043 Unit types | Worker, Soldier, Scout, plus Archer (ranged). |
| 044 Building types | Command Center, Barracks, Resource Depot (drop-off + storage), plus TechLab (research) and Guard Tower (defence). |
| 045–046 Distinct stats | `GameRules`; published at `GET /api/v1/rules`. |
| 047–051 Combat | Range checks, armor, simultaneous resolution, removal at 0 hp, freed tiles. |
| 052–055 Resources | Finite deposits, continuous mine-and-haul, storage capacity, no negative balances. |
| 056–060 Upgrades | Weapons, Armor, Mobility, Harvesting — two tiers each, tier 2 requires tier 1; apply to current and future units. |
| 061 Score | Destruction, economy, survival; live in every state response. |
| 062 Elimination | Losing your last Command Center. **Δ** extra Command Centers can be built, so it's the *last* one. |
| 063–064 Time limit | Highest total wins; ties are draws. |
| 065 Results endpoint | `GET …/result` (live or archived), plus `GET …/replay`. |
| 066–067 Auth and participation | Per-player API keys (hashed at rest); every game endpoint checks the caller is seated (`NOT_A_PARTICIPANT`). |
| 068 Error codes | Uniform `{code, message}` bodies; per-command codes listed in the bot guide. |
| 069 Concurrent matches | One `MatchHost` per match; `Ten_concurrent_matches_all_progress`. |

## Edge cases from the spec

| Case | Behaviour |
|---|---|
| Two units move onto the same tile | Units may share tiles; only buildings, deposits and rock block. |
| A bot stops sending commands | Its units keep their last orders (workers keep mining) or idle. |
| A unit dies with orders queued | Commands referring to it skip it when they run. |
| Units kill each other in one tick | Both die (simultaneous combat; tested). |
| Commanding an unfinished building | `BUILDING_NOT_OPERATIONAL`. |
| Fog updates as units move | Visibility is recomputed every tick. |
| Too many commands | Queue cap → `QUEUE_FULL`; per-tick cap carries the rest over. |
| Several workers on one deposit | Each takes ore in id order until it runs dry; then they move to a nearby deposit. |

## Success criteria

| SC | Evidence |
|---|---|
| 001 State within 2 s of creation | State is available at tick 0 immediately; a 3 s warm-up precedes tick 1. |
| 002 Command → execution → state in one tick | Server `Commands_are_validated_on_submit_and_executed_on_the_next_tick`. |
| 003 Fog hides enemies | Engine fog tests; attacking unseen ids is rejected without revealing they exist. |
| 004 Matches finish with a winner | Engine `FullMatchTests`; Server `A_bot_plays_a_whole_match_over_http_and_beats_the_sitter`. |
| 005 10 concurrent matches | Server `Ten_concurrent_matches_all_progress`; a tick costs single-digit to ~25 ms with large armies. |
| 006 Fast acknowledgements | Submission is in-memory validation under a per-match lock. |
| 007 Three working sample bots | House bots `rusher`, `economist`, `balanced` (+ `sitter`), the C# runner, and `samples/python/bot.py`; they beat a passive opponent and finish matches against each other. |
| 008 Score breakdown | `MatchResultDto`: destruction / economy / survival plus kill and loss stats. |
| 009 Invalid commands rejected informatively | Per-command `{code, message}`; tests cover ownership, bounds, resources, prerequisites. |
| 010 Units act within one tick | Commands run at the start of the next tick. |
