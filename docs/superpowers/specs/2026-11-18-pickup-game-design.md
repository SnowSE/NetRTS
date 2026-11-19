# Pick-up Game: design

Date: 2026-11-18. Status: agreed in conversation, awaiting final approval.
Clickable prototype of the phone screens (open "Deck, printed"): https://claude.ai/artifact/VXFDniiMkMkoUScKMorWuS

## Why

A recruiting booth at a high school, tomorrow. A laptop drives a big screen; students walk up, scan a QR
code and, on their own phones, build a plan for a bot. Their bot then plays on the big screen against
whoever holds "the hill". The goal is interest from passers-by: something fun in the first minute that
shows "you just programmed something", and a way back to Snow College's Software Engineering program
and to Block Bot afterwards.

Success: students stay to try again, crowds form around the big screen, and some leave with a link.

## Goals

- Walk-up play: scan, get a random name and a ready-made plan, change it, play within about a minute.
- A real multi-step strategy, with choices constrained to menus (cards), never free text.
- King of the hill ("winner stays on"), with matches that grow from 1v1 up to 4 players as the line grows.
- The big screen always shows a match.
- A match-over screen that leaves a good impression and links to Snow and to Block Bot.

## Non-goals

- Accounts, sign-in, ladders, achievements, recaps, Discord posts.
- Typing names or anything else (nothing a student types goes on the projector).
- Controlling units directly from the phone.
- Matching Block Bot's rules exactly (pick-up has its own simpler step rules).

## Anti-goals (a failure even if it works)

- **Anything is saved.** No player names, plans, stats or results in the database, the replay store or
  Discord. Everything lives in memory and is gone when the server stops.
- A walk-up student waits a long time before playing.
- One strategy wins every time, so the screen shows the same match over and over.
- The pick-up game changes balance or behavior for normal matches, Block Bot or the house bots.

## Constraints

- Ready for the event tomorrow, **all of it** (no cut line; decided). Runs on the laptop (`dotnet run`,
  SQLite) behind a travel router, with the production site as the fallback.
- **Always on in production** (decided): anyone can open the pick-up page and join the hill at any time.
- Snow College theme throughout (Snow blue `#00447c`, orange `#d4661b`, the lore names from `lore.js`).
- Codebase rules (CLAUDE.md): `GameRules` owns balance and pages read `GET /api/v1/rules`; the engine
  stays deterministic (no clocks, no `System.Random`); Protocol DTOs get new fields, never renamed ones;
  `DuplicationTests` must pass; new pages load with `import()` via `mountLazy`; async callbacks run
  inside `mountScope`; new JS modules under ~800 lines; `docs/bot-guide.md` stays in sync with rules.

## Approaches considered

**How the bot plays a plan**
1. *Chosen:* a new server-side pick-up bot (C#, on `BotView` / `BotMemory` / `BotTurn` like the house
   bots) that runs the steps directly and remembers progress through each step.
2. Rejected: compile the plan into a Block Bot program. Block Bot re-checks every rule each tick, so it
   can't express "run once, in order", runs out of switches, and a step can stall forever when units die.

**The phone screen**: five prototypes were built (Script, Settings, Coach, Deck, Blueprint). *Chosen:*
the Deck in its "printed" look (flat cards, two inks on white card stock).

## The game

**Start.** Every player starts with their HQ, the usual 5 workers, **one free Housing**, and whatever they
bought from a **1,500-grub starting pile** while waiting. Bought units and buildings **exist at tick 0**.
Unspent grubs are in the bank. Up to 3 of each building can be bought.

**Treaty.** For the first **30 seconds** no player can damage another player (units and towers). The
server enforces it.

**Length.** About 2:30: the 30-second treaty and 2 minutes of fighting, on a small map with fast ticks
(250 ms; 100 ms let the economy deck win every time). At the time limit the highest score wins; equal scores keep the champion. A player who
loses every building is out at once; the last one standing wins early.

**Score (pick-up only; decided):** a **combat score**: points for damage dealt and for enemy units and
buildings destroyed. Banked grubs and living units don't count, so a plan that never fights can't win on
its start pile. Normal matches keep today's score.

**The plan** (the same shape for every player and for the house):
- **Start pile:** units (Soldier, Archer, Scout, Worker) and buildings (Housing, Co-op Store, Makerspace,
  Bell Tower), paid from the 1,500.
- **Then:** up to **6 steps**, each run **once, in order**; a step starts when the one before it is done.
  Step kinds:
  - *Workers up to N*, *[unit] up to N*: train until you have N. "Up to", never "N more". If you
    already have N the step is done immediately (the editor warns). Optional **"Keep it at this level"**:
    after the step, whenever the count drops below N, replace losses **before** working on later steps.
  - *Build [building]*, *Take [class]* (research).
  - A step that can't be afforded waits. Menus only offer what is possible at that point in the plan.
- **Forever:** up to **3 cards** of any kind, done in order, over and over, after the last step. A unit
  or worker card makes one more each time, a building card builds another, a class card is taken once and
  then skipped. If every card would be skipped, the loop stops.
- **Attack:** when the army reaches N fighters, or at a clock time (0:30, 1:00, 1:30, 2:00); target
  their base or the nearest enemies; when the base is attacked, defend it or keep attacking.
- **Attacking is one push, not waves** (my call): once the trigger fires, the bot attacks for the rest
  of the match and new fighters join the attack as they're trained.
- **Live buttons during your match:** *Attack now!* fires the attack immediately (during the treaty it
  says how many seconds remain and attacks when the treaty ends). *Pull back* brings fighters home and
  holds the automatic trigger for 15 s, after which it can fire again (my call).

Three ready-made decks fill a plan in one tap: **Honey Rush**, **Turtle Town**, **Grub Boom** (contents in
the prototype's `PRESETS`). They must be tested against each other and adjusted (on the deck, not in
`GameRules`) until each can beat another.

## The line and the hill

- Opening the phone page puts you in line with a random two-word name (the Block Bot `randomName`
  word lists), unique among people in line right now. No Ready button.
- **Players per match:** the champion plus up to 3 from the line: 1 waiting → 1v1, 2 → 3 players,
  3 or more → 4.
- **Winner stays on.** Losers' phones go back to their plan ("tweak and get back in line"). A champion
  retires after **5 wins**.
- **The house** holds the hill when no student does (at the start, and after a retirement). It plays one
  of the three decks under the same rules, shown with a house nickname ("Honey Badger · house").
- **Quiet times:** with a student champion and an empty line, the champion defends against the next
  house deck; with no students at all, house deck plays house deck.
- **House matches end early** when a student is ready; a student never waits on a house match.
- **Planning time when alone:** if a student's turn would come right away, they get a planning timer
  first (60 s the first time, 20 s when they come back) with a **Start now** button.
- **Countdown:** when it's your turn the phone's top bar becomes **"Starting in 10…"**. You can keep
  editing; the plan as it stands at zero is what plays.
- Someone who scans and walks away still plays once: their bot plays their plan.
- **Getting back in line** (my call): opening the page joins the line once. After a match the phone
  shows the match-over screen, and **getting back in line takes a tap** ("Tweak your plan and get back in
  line"), so abandoned phones don't cycle forever. A phone not heard from for 3 minutes leaves the line. Name
  and place survive a reload (a token in `sessionStorage`).
- **Champion details** (my call): the champion gets the same 10-second countdown (and can edit) before
  each defense. Only wins against students count toward the 5. A champion-vs-house match cut short for
  a student counts for nothing. A retired champion can get back in line like anyone.
- **Always on** (decided): the hill runs on production too. House-vs-house matches run only while
  someone is watching (a big screen or a phone is connected), so an empty site doesn't burn CPU at 100 ms
  ticks (my call). Pick-up matches stay out of the public match lists and history; they're not recorded.

## Phone screens (see the prototype for every detail)

- **Top bar:** name, place in line ("3rd in line · about 2 min") and grubs left; or "You're next · Starts
  in 0:45 · Start now"; or the orange "Starting in 8…".
- **Tabs:** Start, Then, Forever, Attack. A horizontally scrolling hand of cards below the zone.
- **Cards:** a render of the game's own 3D model (classes show a course number such as `CHEM 1110`
  instead), the cost, the name, and Health / Damage / Armor / Range from `GET /api/v1/rules`, each a big
  number with a small label. A strip over the picture says why a card can't be played.
- **Then list:** drag to reorder by a grip (also Move up / Move down in each step's menu); fixed-width
  pictures so the text lines up; +/− for counts.
- **Now playing:** the current step big and bold with the previous and next step dimmed; a dark status
  panel in the Block Bot HUD style (Grubs, Workers, Fighters, Buildings with per-minute change and a
  filled trend line, colors from `--hud-*`); a table of every player (Grubs, Workers, Fighters,
  Buildings, Score; the leader of each column highlighted, crown on the champion); *Attack now!*
  (orange) and *Pull back* (blue). No clock or "Fight" label.
- **Match over:** a rendered scene from the game (orange Soldiers attacking a blue Bell Tower, the tower
  firing back) behind the result, laid out so the headline sits on the sky and the podium on the grass;
  podium with names and scores (you outlined); "What your bot did" (steps done, fighters trained, lost,
  buildings, classes); the main button **"Tweak your plan and get back in line"**; then "You just
  programmed a bot. A plan the computer follows step by step is a program. Snow College students build
  all sorts of cool software (like this game) in the Software Engineering program." with
  **Software Engineering at Snow College** (`https://engineering.snow.edu`), **Take your bot home**, and
  a link to start fresh in Block Bot (`/#/blocks`).

## Take your bot home

The match-over link carries the plan turned into a Block Bot program, base64url JSON in the link:
`/#/blocks/import?program=…`. Nothing is stored on the server. The conversion (prototype
`toBlockProgram`): defend rule first, one rule per step in order (`count < N → train`, `count < have+1 →
build`, research with `once`), the attack rule, then the Forever units as `trainMix`. It plays close to
the pick-up plan, not exactly (Block Bot re-runs steps; no starting pile at home).

**Block Bot needs a new route** `#/blocks/import?program=…`: decode, run through the existing program
check (`BlockProgramValidator`, via the API), save as a new strategy in the browser, open the editor.
Bad or oversized links show a plain error.

## Big screen

The live match in the 3D view (`mount3d`) on the laptop, with: the QR code and "Got next?", the champion
and their win count, who's playing, and the line (next few names). It moves on to each new match by
itself.

## Left to the builder

- Endpoint shapes and where pick-up state lives (an in-memory service next to `MatchManager`), and how
  pick-up matches skip `MatchRecorder`, achievements and the announcer.
- Engine changes for the start pile, the free Housing and the treaty, as new per-match settings
  (deterministic; normal matches unchanged); spawn placement for bought buildings and units.
- Map size for 2 to 4 players.
- How the phone hears about its turn (polling or the existing match stream).
- Card art: render with the existing 3D models in the page, or ship renders made by a script; keep one
  owner for the look.
- The host page for the big screen; the QR code.
- Tests: the pick-up bot's step rules, the treaty, nothing recorded, the Block Bot conversion passing
  `BlockProgramValidator`, and `docs/bot-guide.md` if anything player-facing in the API changes.
