# Plan: spring-semester rule changes (cell occupancy, mining slots, trebuchet blast)

Status: **building** (interview started 2026-11-02; round 3 decisions 2026-11-26). Target: spring semester 2027.

> Built on branch `spring-rules`; test server https://netrts-spring.azurewebsites.net runs the `spring-draft` rule set.
> Tune with `netrts-sim` (see `src/NetRts.Sim`).

## Where things stand today

- Units never block each other: any number can stand on one tile. Only rock, buildings and deposits block
  (`GameSimulation.Passable`, `GameSimulation.cs:292`).
- New units take the nearest empty tile, and stack when none is near (`FindFreeTileNear`, `GameSimulation.cs:322`).
- A grub node is a single-tile `DepositEntity`; any number of workers can mine it. A worker whose node runs dry
  moves on to the nearest other deposit (`UpdateGatherer`).
- Trebuchet shells land `ShotTicks` later and hit every enemy on the target tile only.
- `GameRules` is static constants; `GET /api/v1/rules` notes say "Units may share tiles" (`GameRules.cs:164`),
  which has been public since launch. (Left alone for now; see open question G.)

## Decisions so far

1. **Occupancy, in whole numbers out of 9 per cell.** Worker 2, Soldier 1, Archer 1, Scout 2, Trebuchet 9.
   So a cell holds at most 4 workers, 9 soldiers/archers, 4 scouts or 1 trebuchet. Types mix freely
   (e.g. 2 workers + 5 soldiers).
2. **Both players share a cell's capacity.**
3. **Units pass through each other.** Occupancy only matters for units that have stopped: idle, mining or
   building (attacking: open question A).
4. **First in keeps the cell.** A newcomer that doesn't fit goes to the nearest cell it can reach that has room.
   Units already in a cell never move, so bumps can't chain.
5. **Ties: zipper merge.** Units from different players stopping on the same cell in the same tick alternate by
   player, in an order derived from the match seed. Same rule for contested grub nodes.
6. **Mining: 2 workers per grub node, 2 more queued.** The first two to arrive mine; the next two wait in line.
   Later arrivals look for another node within their vision and apply the same rules there. A worker leaving to
   deliver frees its slot at once (the next in line takes it); on return it queues like a newcomer.
7. **Spawning:** new units are bumped outward from the building as far as occupancy requires.
8. **Trebuchet blast radius:** 100% damage on the target cell, 50% on the 8 adjacent cells.
9. **Economy changes are intended.** Rebalance with simulations (gather rate, carry, node layout near starts,
   trebuchet numbers).
10. **Spectator badges, now (independent of the rules):** one icon per unit type (per player) in a cell, with a
    count badge (threshold: open question H).

## Round 3 decisions (2026-11-26)

Driven by prod data (52 rated matches + practice matches with 2+ student bots, Oct 9 - Nov 26, 644
player-games): half of rated matches hit the tick limit and economy (all ore ever banked) decides a third of
those; guard towers made 22% of all kills (3,064 towers vs 1,602 barracks); 11% of players ever built a
trebuchet; Fortify 2.5% and Armor3 <1% of players; the median student in long games exhausts all 9 home
patches.

11. **Guard tower:** attack cooldown 1 -> 3 and a higher cost (amount from simulations). New upgrade cuts the
    cooldown to 2.
12. **Spoils:** destroying an enemy building pays the killer a fraction of its cost in grubs. Start at 50%;
    pick the number from simulations (must not snowball the leader or end games too early).
13. **Trebuchet:** cheaper siege path: drop or halve Siege Engineering (simulations decide).
14. **Fortify:** make it worth buying (bigger armor per tier and/or tower bonus; simulations decide).
15. **Extra headquarters:** each one costs more by how many you own (finished or under construction):
    cost = base x (1 + 0.25 x HQs you own now), counting finished ones and sites already started. With the
    base 400: 2nd 500, 3rd 600, 4th 700. Integer math: base x (4 + owned) / 4. Confirmed by the user.
16. **Grub regrowth:** a patch never holds more than it started with. Below that it regrows at a rate
    proportional to what it holds now (small patches crawl, nearly full ones recover fast), capped at the
    starting amount. A patch mined to 0 never comes back, so it can still be removed at 0 as today. Integer
    math: per-patch remainder carried tick to tick (parts per million). Rate tuned so regrowth stays a small
    share of income (first guess: a few percent of the map's ore over a match).
17. **Mining limit is per edge, not per patch (replaces decision 6):** at most 2 miners mine from each tile
    next to a patch. Recommended: the limit belongs to the tile, shared by every patch it touches, so it agrees
    with cell occupancy (4 workers per cell). Overflow (user, 2026-11-26): a cell still holds only 4 workers,
    so 2 mine and up to 2 wait in the same cell; anyone else is bumped to the nearest cell with room (normal
    occupancy rule). A bumped worker whose new cell isn't next to a patch with a free mining spot searches its
    vision (5) for a patch with a free spot and goes there; none, it goes idle (answers open question D). A
    worker leaving to deliver gives up its spot at once and the next one steps up; on return it is a
    newcomer. Harvest rate (gather per tick, carry) may change if the simulations say so.
18. **Farms are farmed:** a Farm (API `Farm`, Snow College name on the site) is a building that holds a set
    number of grubs; workers stand next to it and gather from it like a patch (same per-edge limit). It runs
    out, and an empty farm has to be replanted (paying again) before it yields more. Proposed API: an empty
    farm stays standing; a worker sent to Gather it replants it if the player can pay, so no new command type.
    Destroying one pays spoils like any building. Replaces the compost booster (confirm with user). Cost, size,
    gather rate and replant cost from simulations: a farm must be worth less per worker than a fresh patch,
    and worth it once home patches run dry.
19. **Scoring at the tick limit** (recommended, not yet decided): destruction + survival, economy only as a
    tiebreak or at reduced weight; 15 of 46 tick-limit games flip without economy.

## Open questions (round 2)

Recommendations in italics.

- **A. Attacking units:** does a unit attacking from a standstill occupy space like an idle one? *Yes; units
  chasing a target don't, until they stop. Otherwise a blob of firing archers ignores the limit and feeds the
  trebuchet blast.*
- **B. Builders:** around a building site, is it 2 workers per cell (as said in the interview) or the normal
  occupancy (4)? *Normal occupancy, so there is one rule.*
- **C. Zipper order:** within one player, lowest unit id first? Is the player order drawn once per match or per
  tick? *Lowest id first; player order redrawn each tick from the seed, so nobody wins every tie all match.*
- **D. Mining queue:** queue of 2, then look for another node within the worker's vision (5)? Do queued workers
  occupy cells around the node? No node in vision: idle? *Yes to all, and show "waiting" in state.*
- **E. Trebuchet blast:** friendly fire? Buildings hit? Halve before armor or after? *No friendly fire; buildings
  hit; halve before armor.*
- **F. API shape:** the interview floated `/api/v2` so v1 keeps working. A match can only run one rule set, so the
  rules have to be a **per-match setting**; commands don't change and new information is additive fields.
  *Keep `/api/v1`, add fields, make rules a match setting. A v1 bot can still play a new-rules match.*
- **G. Rollout:** admins (and hosts) pick the rules when creating a match? What date does the default switch?
  When does the "Units may share tiles" note come out of `/rules`?
- **H. Badges:** threshold 2+ (two units shouldn't look like one)? Both the 2D map and the 3D view?

## Design sketch

**Occupancy.** Each cell has 9 points. A stopped unit holds its type's points in its cell. Per tick, after
movement: collect units that stopped this tick, group by cell, order each group by the zipper rule, admit while
they fit, and send the rest to the nearest reachable cell with room (BFS over passable cells, fixed neighbour
order, so it stays deterministic). No floating point.

**Rule sets.** `GameRules` becomes a rule set chosen per match (e.g. `Classic`, `Spring2027`), carried in match
settings, the stored match record and replays; `GameRules.Version` per rule set. Old matches replay under the rules
they were played with.

**Mining.** Per node: up to 2 miners plus a queue of 2, kept in arrival order (zipper for ties). A delivering
worker leaves the node; the queue head moves up. Overflow workers search for nodes within vision whose miners and
queue aren't full.

**Trebuchet.** Shell resolution hits the target cell at 100% and the 8 neighbours at 50% (per decision E).

**Spawning.** Reuse the occupancy placement, starting from the building's edge.

**API (additive).**
- `rules`: each unit type's `occupancy`, `cellCapacity` (9), `minersPerNode` (2), `queuePerNode` (2),
  trebuchet `splash`.
- `state`: per deposit, `miners` and `queued`; per worker, a `waiting` status.
- Match summary/settings: the rule set.

## Work plan

0. **Badges (now):** count badges per unit type per cell in the 2D map and 3D view.
1. **Rule sets:** make `GameRules` per match; plumb through create match, league, hosted, exhibitions, recording,
   replays; keep `Classic` byte-for-byte (the determinism/replay test must still pass).
2. **Occupancy engine:** arrival resolution, zipper, bump placement, spawn placement; tests for each, including
   cross-player ties and walls.
3. **Mining slots and queue**, overflow search within vision.
4. **Trebuchet blast radius.**
5. **API fields and docs** (`rules`, `state`, bot guide, OpenAPI), only for the new rule set.
6. **House bots** (`PlanBot` plans) and sample bots (Python, C#), tutorial, Colab notebook: play well under the new
   rules.
7. **Balance:** run house-bot vs house-bot simulations under both rule sets; tune gather rate, carry capacity, node
   layout near starts and trebuchet numbers until early-game income and match length look right.
8. **Rollout:** new rules as an option for preseason/hosted matches first; switch the default on the agreed date;
   then remove the "Units may share tiles" note for the new rule set.

## Risks

- Economy drop for every existing bot; students' bots that send all workers to one node lose income until they
  adapt (intended, but announce it).
- Determinism: ordering bugs in the zipper or BFS would break replays; cover with the replay test under both rule
  sets.
- Performance: occupancy bookkeeping per tick on 16-player maps; keep per-cell totals in an array, not LINQ scans.
- Achievements tied to trebuchet kills or stacking may need new thresholds.

## Simulation results (2026-11-26, branch `spring-rules`, `netrts-sim`)

4-player mirror matches (one house bot x4), 48 games per cell. Mixed line-ups are useless: Formics wins every
game under every variant. Formics game lengths are bimodal (~700 or ~1,300 ticks): compare means, not medians.

- Tower cooldown 3: tower kill share 12% -> 4-5% (Economist, Balanced), tick-limit games 8-17% -> 0%. Cost 150 fine.
- Spoils 50%: 350-640 grubs per player per game; leader at tick 300 wins no more often than classic. Keep 50%.
- HQ +25%: Formics builds slightly fewer extra HQs (0.7 -> 0.5); otherwise no change.
- Regrowth: mean Formics length 1,064 (classic), 1,024 (200 ppm), 890 (500 ppm). 200 ppm is mild; 500 shortens games.
- Siege discount (rusher mirror): trebuchets 12.8 -> 14.0 per game. Rusher already beelines siege, so this can't
  show whether students would start building them.
- Whole draft (spring): mean Formics length 893, no tick-limit games, no extra snowballing.
- Untested: Fortify and RapidFire (no house bot researches them in these games), splash (not implemented), farms,
  occupancy and per-edge mining (not implemented).
- Round 3b (RapidFire/Fortify-aware Formics, splash): Formics buys RapidFire in every spring game and Fortify1 in
  42% (vs 13% classic); spring Formics mean length 984 (was 893 before it bought them; classic 1,065). Splash 50%
  (rusher mirror, the only trebuchet user): mean 996 -> 1,204 alone, 1,112 -> 1,041 inside spring; no clear
  direction, nothing breaks.
- Test server: https://netrts-spring.azurewebsites.net (rg-netrts-spring, B1, NetRts__Rules=spring-draft, SQLite
  in /home, no Discord settings so nothing posts to the real server).
