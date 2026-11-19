# Pick-up Game Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A walk-up "king of the hill" mode: phones build a card plan, the server plays it on the big screen against the hill's holder, nothing is saved.

**Architecture:** The engine gains per-seat start kits, a treaty and a combat score (new config, normal matches untouched). A new server-played `PickupBot` runs a plan's steps once, in order. A singleton `PickupHill` service owns the line, the champion and the house, and creates unrecorded matches through `MatchManager`. A lazily loaded `wwwroot/pickup/` page serves the phone (`#/pickup`) and the big screen (`#/pickup/screen`).

**Tech Stack:** C# 12 / .NET 10, ASP.NET Core minimal APIs, MSTest-style tests already in `tests/`, vanilla JS modules, three.js (vendored) through `mount3d`.

**Spec:** `docs/superpowers/specs/2026-11-18-pickup-game-design.md` (read it; the prototype source is `/tmp/claude-1000/-home-jonathan-code-NetRTS/22f4d802-3d9e-4a19-9bcd-2337dcb458ad/scratchpad/pickup-phones.html`, the "print" style).

## Global Constraints

- Nothing about pick-up players is saved: no DB rows, no `MatchRecorder`, no achievements, no announcer, no replay frames kept.
- Normal matches, Block Bot and house bots behave exactly as before: `BotCharacterizationTests` hashes unchanged, replay determinism test passes.
- Start pile 1,500 grubs; one free Housing (`Barracks`); max 3 of each building in the pile; 6 steps; 1–3 Forever cards; treaty 30 s; match 150 s; tick 250 ms (so treaty 120 ticks, `MaxTicks` 600).
- Item ids on the wire are API names (`Soldier`, `Trebuchet`, `Barracks`, `ResourceDepot`, `TechLab`, `GuardTower`, `SiegeWorkshop`, `Weapons1`…); pages turn them into Snow names with `lore.js`.
- Costs and stats come from `GameRules` (server) and `GET /api/v1/rules` (pages). No second copy.
- `dotnet build` (warnings are errors) and `dotnet test` green after every task; `DuplicationTests` passes.
- JS: new pages load via `mountLazy(() => import(...))`; async work inside `mountScope`; modules under ~800 lines.

## Review Focus

- A phone that reloads mid-countdown or mid-match keeps its name, place and match (token in `sessionStorage`; `GET me` with an unknown token re-joins).
- A plan whose step needs something it never builds (deleted Makerspace step) waits on that step instead of crashing; the bot still defends and gathers.
- Two students tap Start now at once / a house match is interrupted while a phone polls: no duplicate matches, no student in two matches.
- A champion's phone closed: the champion still defends (plays their last plan) until retirement or loss.
- A pick-up match never shows in `GET /api/v1/matches`, history, or Block Bot's lists, and is removed from memory after it ends.

---

### Task 1: Engine: start kits, treaty, combat score

**Files:**
- Modify: `src/NetRts.Engine/GameSimulation.cs` (GameConfig, PlayerSeat, constructor, ScoreOf), `src/NetRts.Engine/GameSimulation.Tick.cs` (damage loop, tower fire), `src/NetRts.Engine/Entities.cs` (PlayerState.DamageDealt)
- Test: `tests/NetRts.Engine.Tests/PickupRulesTests.cs`

**Interfaces — Produces:**
- `public sealed record StartKit(int Resources, IReadOnlyList<UnitType> Units, IReadOnlyList<BuildingType> Buildings);`
- `public sealed record PlayerSeat(Guid Id, string Name, StartKit? Kit = null);`
- `GameConfig.TreatyTicks` (int, default 0), `GameConfig.CombatScore` (bool, default false).

- [ ] Tests (each builds a 2-seat sim, seed fixed):
  - `Kit_sets_bank_units_and_completed_buildings`: kit (200, [Soldier×3], [Barracks, GuardTower]) → resources 200; 5 workers + 3 soldiers; CommandCenter + Barracks + GuardTower all `Completed`, owned by slot 0, none on rock/deposit, no two buildings adjacent (8-neighbourhood).
  - `No_kit_is_unchanged`: seat without kit → `StateHash()` equals a sim built before the change (compare against a sim built with `PlayerSeat(id, name)` and default config: same hash for two constructions; plus resources 500).
  - `Treaty_blocks_damage_between_players`: `TreatyTicks = 50`, soldiers of both sides spawned adjacent (`SpawnUnit`), step 50 ticks → every unit at full HP; step 20 more → some HP lost.
  - `Towers_hold_fire_during_treaty`: enemy soldier next to a kit GuardTower, 30 ticks of treaty → soldier full HP.
  - `Combat_score_counts_damage_and_kills_only`: `CombatScore = true`; at tick 0 both totals are 0 despite kits; after a fight the attacker's total > 0 and equals Destruction field.
- [ ] Implement: kit placement after the HQ: units via `FindFreeTileNear(start)`; buildings at the first `RingTiles(start, 7)` tile with Chebyshev distance ≥ 2 that is `Passable`, has no unit, and has no building or deposit in its 8 neighbours; `Resources = kit.Resources`. Treaty: while `Tick <= Config.TreatyTicks`, skip attacks whose target owner differs from the attacker owner (in the damage loop) and skip tower targeting. `DamageDealt += damage` for cross-owner hits. Combat score: `ScoreOf` returns `Destruction = p.Destruction + p.DamageDealt, Economy = 0, Survival = 0` when `CombatScore`.
- [ ] `dotnet test --project tests/NetRts.Engine.Tests` green (incl. replay/characterization). Commit `Engine: start kits, treaty and combat score for pick-up matches`.

### Task 2: Plan DTOs, validator, decks

**Files:**
- Create: `src/NetRts.Protocol/Pickup.cs`, `src/NetRts.Bots/Pickup/PickupPlans.cs`
- Test: `tests/NetRts.Engine.Tests/PickupPlanTests.cs`

**Interfaces — Produces:**
```csharp
public sealed record PickupBuyDto(string Item, int Count);
public sealed record PickupStepDto { string Kind /* workers|train|build|research */; string? Item; int Count; bool Keep; }
public sealed record PickupAttackDto { string Trigger = "army" /* army|time */; int Army = 12; int AtSecond = 90; string Target = "base" /* base|nearest */; bool Defend = true; }
public sealed record PickupPlanDto { IReadOnlyList<PickupBuyDto> Start = []; IReadOnlyList<PickupStepDto> Steps = []; IReadOnlyList<string> Forever = ["Soldier"]; PickupAttackDto Attack = new(); }
public sealed record PickupDeckDto(string Id, string Name, string HouseName, PickupPlanDto Plan);
public static class PickupPlans {
  public const int StartGrubs = 1500, MaxSteps = 6, MaxForever = 3, MaxEachBuilding = 3, MaxCount = 60;
  public static string? Validate(PickupPlanDto plan);          // null when OK; a sentence otherwise
  public static StartKit KitFor(PickupPlanDto plan);           // 1500 - cost, units, buildings + one free Barracks
  public static IReadOnlyList<PickupDeckDto> Decks { get; }    // rush, turtle, boom — prototype PRESETS, API names
}
```
Start items allowed: Soldier, Archer, Scout, Worker, Barracks, ResourceDepot, TechLab, GuardTower. Deck house names: "Honey Badger" (rush), "Blue Badger" (turtle), "Grub Hoarder" (boom).

- [ ] Tests: decks all validate; spending 1,550 fails ("costs more than 1,500"); 4 GuardTowers fails; 7 steps fails; 0 or 4 Forever cards fail; unknown item fails; `train` with a building item fails; `KitFor(rush)` = 0 resources, 10 Soldiers + 4 Archers, Barracks once.
- [ ] Implement with costs from `GameRules`. Commit `Pick-up plans: DTOs, validation and the three decks`.

### Task 3: PickupBot

**Files:**
- Create: `src/NetRts.Bots/Pickup/PickupBot.cs`
- Test: `tests/NetRts.Engine.Tests/PickupBotTests.cs` (use `TestGames`/full-match helpers the Block Bot tests use)

**Interfaces — Produces:**
```csharp
public sealed class PickupBot(string name, PickupPlanDto plan, int treatyTicks) : IBotStrategy {
  public void AttackNow();  public void PullBack();
  public int CurrentStep { get; }   // 0..Steps.Count (== Count: on Forever)
  public int Trained { get; }       // fighters trained this match
}
```
Behaviour (spec "The plan"): steps once, in order; `workers`/`train` done when count ≥ N; `keep` steps join a keep list that trains replacements before later steps; `build` done when a building of that type was placed after the step began (count incl. under construction > count at step start); `research` done when owned or in progress; unaffordable or impossible steps wait. Forever cycles cards (unit/worker: one each; building: place one; research: once then skip; stop when a full cycle does nothing). Attack: trigger fires after treaty when army ≥ N or tick ≥ AtSecond×10; then all fighters attack (`enemyBase` via `view.AttackTarget()`, `nearest` like Block Bot's nearestEnemy) with retarget; new fighters join. `PullBack`: fighters move to `RallyPoint`, attack off, triggers ignored for 150 ticks. `AttackNow`: attack on (held until treaty ends). Defend (when `Defend`): threat within 10 of base → fighters not attacking defend. Always `FinishAbandonedSites` and `GatherIdleWorkers(GatherStyle.RememberedNearDropOff)`.

- [ ] Tests: rush deck vs sitter-plan 1v1 with kits and treaty runs 1500 ticks without exceptions and the rush player wins; a plan `[train Soldier up to 3]` reaches step 1 (`CurrentStep == 1`) with 3 soldiers; a plan whose step 1 is `research Weapons1` with no TechLab stays on step 0 and keeps gathering; `PullBack` then 100 ticks → no fighter has an Attack last order.
- [ ] Commit `PickupBot: plays a pick-up plan`.

### Task 4: Unrecorded pick-up matches in MatchManager

**Files:**
- Modify: `src/NetRts.Server/Matches/MatchHost.cs` (MatchSettings, kits, IsPickup), `src/NetRts.Server/Matches/MatchManager.cs` (CreatePickup, Listed, Housekeep), `src/NetRts.Server/Endpoints/MatchEndpoints.cs`, `src/NetRts.Server/Endpoints/BlockEndpoints.cs` (use `Listed`)
- Test: `tests/NetRts.Server.Tests/PickupMatchTests.cs`

**Interfaces — Produces:**
- `MatchSettings(..., int TreatyTicks = 0, bool CombatScore = false)`
- `MatchHost.IsPickup { get; init; }`, `MatchHost.TryJoin(Seat seat, IBotStrategy? bot, StartKit? kit)` (kits shuffled with seats)
- `MatchManager.CreatePickup(IReadOnlyList<(Seat Seat, IBotStrategy Bot, StartKit Kit)> players) : MatchHost` — map `MinSizeFor(n)` (32), 100 ms, 1500 ticks, treaty 300, combat score, starts at once, **no** `recorder.Enqueue`.
- `MatchManager.Listed` = `All` without pick-up matches. Housekeep removes completed pick-up matches after `CompletedMatchRetentionMinutes` without needing `Recorded`.

- [ ] Tests: a pick-up match isn't in `GET /api/v1/matches`; `GET /api/v1/matches/{id}` works (the big screen spectates it); after it completes nothing is written (`match_players` count unchanged) and Housekeep removes it.
- [ ] Commit `Pick-up matches: unrecorded, unlisted, with kits`.

### Task 5: PickupHill service and endpoints

**Files:**
- Create: `src/NetRts.Server/Pickup/PickupHill.cs` (state machine), `src/NetRts.Server/Pickup/PickupNames.cs`, `src/NetRts.Server/Pickup/PickupEndpoints.cs`, DTOs in `src/NetRts.Protocol/Pickup.cs`
- Modify: `Program.cs` (register singleton + hosted loop + endpoints)
- Test: `tests/NetRts.Server.Tests/PickupHillTests.cs` (drive `PickupHill.Update(now)` with a fake `TimeProvider`; `AutoTick` off where possible)

**Interfaces — Produces (HTTP, JSON camelCase):**
- `POST /api/v1/pickup/join {token?}` → `PickupMeDto`
- `GET /api/v1/pickup/me?token=` → `PickupMeDto` (404 `UNKNOWN_TOKEN`)
- `PUT /api/v1/pickup/plan {token, plan}` → 204 or 400 `{error}`
- `POST /api/v1/pickup/start-now {token}`, `POST /api/v1/pickup/rejoin {token}` → `PickupMeDto`
- `POST /api/v1/pickup/order {token, order: "attack"|"pullback"}` → 204
- `GET /api/v1/pickup/hill` → `PickupHillDto`; `GET /api/v1/pickup/decks` → `PickupDeckDto[]`
```csharp
PickupMeDto { Token; Name; Phase /* line|next|countdown|playing|over */; int? Place; int? SecondsLeft; Guid? MatchId;
  bool Champion; int Wins; int Step; int Steps; IReadOnlyList<PickupPlayerDto> Players; PickupResultDto? Result; PickupPlanDto Plan; }
PickupPlayerDto { Name; bool You; bool Champion; bool House; int Grubs; int Workers; int Fighters; int Buildings; int Score; }
PickupResultDto { int Place; bool Won; IReadOnlyList<PickupPlayerDto> Players; int StepsDone; int Steps; int FightersTrained; int FightersLost; int Buildings; int Classes; }
PickupHillDto { Guid? MatchId; string Phase; PickupPlayerDto? Champion; int ChampionWins; IReadOnlyList<PickupPlayerDto> Playing; IReadOnlyList<string> Line; int? SecondsLeft; }
```
Rules (spec "The line and the hill"): join gives a unique two-word name (word lists in `PickupNames`, same words as `blocks/intro.js`), the default plan = rush deck, `ReadyAt = now + 60 s` (rejoin: 20 s; start-now: now). Line order = join order. Match forms when the front entry is ready: champion + up to 3 ready entries from the front → all get `countdown` 10 s (plans read at zero). House matches (champion-vs-house or house-vs-house, next deck in rotation) run only when nothing student-ready waits and a watcher was seen in the last 30 s (hill polled or any `me` call); they're stopped without result when a student becomes ready. Results: winner = `Outcome.WinnerId`; ties keep the champion; student champion's wins count only against students; 5 wins → retire, house takes the hill; losers → `over` (with result) until `rejoin`. Entries not seen for 60 s leave the line (not while counting down or playing). Players' stats come from the match's spectator view (units by type/owner, buildings by owner, resources, score total).

- [ ] Tests: join twice with same token → same name; two joins → place 1 and 2; front entry not ready → phase `next` with seconds left; start-now → countdown → after 10 s a match exists with 2 seats (champion = house) and both phones `playing`; house wins → student `over` with a result, champion still house; a student who wins 5 times → retires (champion house); unseen 61 s → removed; `order` for a non-player → 409.
- [ ] Commit `Pick-up hill: the line, the champion and the house`.

### Task 6: Pick-up pages (phone + big screen) and nav

**Files:**
- Create: `src/NetRts.Server/wwwroot/pickup/index.js` (route, polling, phases), `deck.js` (Start/Then/Forever/Attack tabs, cards, drag), `playing.js` (now playing + HUD + table), `over.js` (match over), `screen.js` (big screen), `convert.js` (`toBlockProgram`), `pickup.css`; images `src/NetRts.Server/wwwroot/images/pickup/*.webp` (card renders, `scene.webp`)
- Modify: `wwwroot/app.js` (routes `#/pickup`, `#/pickup/screen`), `wwwroot/index.html` (remove the brand toggle button "Original theme", add `<a href="#/pickup" data-section="pickup">Pick-up Game</a>`)

Port the prototype's "Deck, printed" screens, replacing local state with the API: plan edits `PUT plan` (debounced 400 ms); poll `me` every 1 s; decks from `GET decks`; costs/stats from `GET /api/v1/rules`; names via `lore.js`. Big screen: `mount3d` on the current `matchId` with `openMatchFeed`, sidebar with "Got next?", a QR code for `location.origin + '/#/pickup'` (the vendored QR approach used by colab, or `images/` generated), champion + wins, playing, line. Match-over links: `https://engineering.snow.edu`, take-home `/#/blocks/import?program=…`, `/#/blocks`.

- [ ] Verify in a browser (two tabs as two students + one big screen): join, edit, start now, countdown, match plays on the screen, Attack now/Pull back answer, match over shows podium and links. Commit `Pick-up Game pages`.

### Task 7: Block Bot import link

**Files:** Modify `wwwroot/app.js` (blocks regex accepts `import`), `wwwroot/blocks/index.js` (+ small `import.js`): decode base64url JSON → `POST`-free check via existing program validation endpoint (or `GET vocabulary` + local canon) → `newStrategy` saved in the browser store → open its editor; bad link → plain error with a link to Block Bot.
- [ ] Test: `BlockExportTests`-style server test that `toBlockProgram` output for each deck passes `BlockProgramValidator` (decks serialized with the same mapping in C#, or a node run of `convert.js` in the test). Commit `Block Bot: open a program from a link`.

### Task 8: Docs, full test, deploy

- [ ] `docs/bot-guide.md`: a short "Pick-up Game" section (rules differences); `docs/requirements-coverage.md` unaffected unless it lists endpoints.
- [ ] `dotnet build && dotnet test` green. Run locally, play a full match with two tabs.
- [ ] Before pushing to `main`: list `https://netrts.snowse.io/api/v1/matches`; if any match has `status != Completed` or a league match has `closed == false`, stop and ask. Then push; watch CI; check `/health` reports the new commit; open `https://netrts.snowse.io/#/pickup`.
