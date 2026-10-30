# %% [markdown]
# # 🦡 Badger Brawl: code your own army
#
# **Badger Brawl** is a real-time strategy game made at Snow College in Ephraim,
# and **nobody plays it with a mouse.** You write a Python program, a *bot*, that commands a clan of badgers:
# workers dig up grubs, housing trains soldiers and archers, and your army marches across the
# Sanpete Valley to knock over the other team's HQ.
#
# Every **tick** (about a second) the game moves forward. Between ticks your code looks at what your
# badgers can see and sends orders. That's the whole game.
#
# **How to use this notebook:** click a cell, then press **▶** (or **Shift+Enter**). Run the cells in order, top to bottom.
#
# Everything here is also at [netrts.snowse.io/#/colab](https://netrts.snowse.io/#/colab), with the slides.
#
# Source code for the whole game: [github.com/SnowSE/NetRTS](https://github.com/SnowSE/NetRTS)

# %%
# @title ⚙️ Step 0: Setup. Click ▶ to run (you don't need to read this one) { display-mode: "form" }
import time
import requests
from IPython.display import display, HTML, IFrame

SERVER = "https://netrts.snowse.io"
API_KEY = ""
match_id = None
match_name = None
GAME_MAP = None
COST = {}
memory = {}      # your decide() function can remember things here between ticks
_reserved = set()  # build spots we already handed out this match


class GameError(Exception):
    def __init__(self, code, message):
        super().__init__(f"{code}: {message}")
        self.code, self.message = code, message


def call(method, path, body=None):
    """Every request to the game is plain HTTP + JSON."""
    headers = {"Authorization": f"Bearer {API_KEY}"} if API_KEY else {}
    for attempt in range(3):
        try:
            r = requests.request(method, f"{SERVER}/api/v1/{path}", json=body, headers=headers, timeout=60)
        except requests.RequestException:
            if attempt == 2:
                raise
            time.sleep(1)
            continue
        if r.status_code == 429 and "RATE_LIMITED" in r.text and attempt < 2:
            time.sleep(1)
            continue
        if r.status_code >= 400:
            try:
                err = r.json()
            except ValueError:
                err = {}
            raise GameError(err.get("code", str(r.status_code)), err.get("message", r.reason))
        return r.json() if r.content else None


# ---------------------------------------------------------------- signing up

def sign_up(name):
    global API_KEY
    try:
        reply = call("POST", "players", {"name": name})
    except GameError as e:
        tips = {
            "NAME_TAKEN": f"'{name}' is taken. If it's yours, paste your key in the API_KEY box. If not, pick another name.",
            "INVALID_NAME": "Names are 3-32 letters, digits, - or _ (no spaces). Fix it and run the cell again.",
            "RATE_LIMITED": "Lots of people are signing up at once. Wait one minute and run the cell again.",
        }
        print("😬 " + tips.get(e.code, e.message))
        return
    API_KEY = reply["apiKey"]
    display(HTML(
        f"<p>🎉 Signed up as <b>{reply['name']}</b>!</p>"
        f"<p>Your secret key is <code style='font-size:1.1em'>{API_KEY}</code></p>"
        "<p><b>Copy it into the API_KEY box above now.</b> It is shown only once, and if Colab restarts "
        "you'll need it to get your badgers back.</p>"))


def who_am_i():
    return call("GET", "players/me")


def _need_key():
    if not API_KEY:
        raise RuntimeError("Run the 'Name your clan' cell first so the game knows who you are.")


# ---------------------------------------------------------------- getting into a match

def _speed_settings(speed, max_ticks):
    speed = max(0.1, min(10, speed))
    settings = {"tickIntervalMs": int(1000 / speed)}
    if max_ticks:
        settings["maxTicks"] = max_ticks
    return settings


def _give_up_current():
    """Starting a new match concedes the one you were in, so old matches don't pile up on the server."""
    if match_id is None:
        return
    for action in ("surrender", "leave"):
        try:
            call("POST", f"matches/{match_id}/{action}")
            print("🏳️ Gave up your previous match.")
            return
        except GameError:
            pass


def _entered(match):
    global match_id, match_name, GAME_MAP
    match_id, match_name, GAME_MAP = match["matchId"], match.get("name"), None
    memory.clear()
    _reserved.clear()
    link = f"{SERVER}/#/match/{match_name or match_id}"
    display(HTML(f"<p>👀 Watch it live: <a href='{link}' target='_blank'>{link}</a> (opens in a new tab)</p>"))


def practice(vs="sitter", speed=1, max_ticks=None):
    """Start a match against one of the house badgers: sitter, rusher, balanced or economist."""
    _need_key()
    _give_up_current()
    _entered(call("POST", "matches", {"houseBots": [vs], "settings": _speed_settings(speed, max_ticks)}))
    print(f"⚔️ New match against '{vs}'.")


def host_match(name, seats=2, speed=1, house_bots=(), max_ticks=None):
    """Start a match with a name your classmates can join. It begins once every seat is taken."""
    _need_key()
    _give_up_current()
    _entered(call("POST", "matches", {"name": name, "maxPlayers": seats, "houseBots": list(house_bots),
                                      "settings": _speed_settings(speed, max_ticks)}))
    print(f"🏟️ Match '{name}' is open with {seats} seats. Tell your classmates to join_match('{name}').")


def join_match(name):
    """Join a classmate's match by its name."""
    _need_key()
    match = call("GET", f"matches/named/{name}")
    if match["matchId"] != match_id:
        _give_up_current()
    try:
        call("POST", f"matches/{match['matchId']}/join")
    except GameError as e:
        if e.code != "ALREADY_JOINED":
            raise
    _entered(match)
    print(f"🤝 You're in '{name}'.")


def _ready():
    """Wait for the match to start, then load the map and prices (once per match)."""
    global GAME_MAP, COST
    if match_id is None:
        raise RuntimeError("Start a match first: practice('sitter'), host_match(...) or join_match(...).")
    while True:
        try:
            s = call("GET", f"matches/{match_id}/state?waitForTick=0")
            break
        except GameError as e:
            if e.code != "MATCH_NOT_STARTED":
                raise
            print("⏳ Waiting for every seat to fill...")
    if GAME_MAP is None:
        GAME_MAP = call("GET", f"matches/{match_id}/map")
        rules = call("GET", "rules")
        COST = {x["type"]: x["cost"] for x in rules["units"] + rules["buildings"] + rules["upgrades"]}
    return s


def watch(height=520):
    """Show the live match right here in the notebook."""
    if match_id is None:
        raise RuntimeError("Start a match first.")
    display(IFrame(f"{SERVER}/#/match/{match_name or match_id}", width="100%", height=height))


# ---------------------------------------------------------------- looking around

def get_state():
    _ready()
    return call("GET", f"matches/{match_id}/state")


def look():
    """Print what your badgers can see right now."""
    s = get_state()
    me = s["you"]
    print(f"Tick {s['tick']} of {s['maxTicks']}   🐛 grubs: {me['resources']}   status: {s['status']}")
    print("\nYour badgers:")
    for kind in ("Worker", "Soldier", "Archer", "Scout", "Trebuchet"):
        units = my_units(s, kind)
        if units:
            print(f"  {len(units):>2} {kind:<9} ids {[u['id'] for u in units]}")
    print("\nYour buildings:")
    for b in my_buildings(s):
        status = "ready" if b["completed"] else f"{b['constructionPercent']}% built"
        print(f"  {b['type']:<14} id {b['id']:<4} at ({b['x']},{b['y']})  {status}")
    print("\nGrub piles you can see:")
    home = my_hq(s) or {"x": 0, "y": 0}
    for d in sorted(s["resources"], key=lambda d: distance(d, home))[:6]:
        print(f"  id {d['id']:<4} at ({d['x']},{d['y']})  {d['remaining']} grubs left")
    seen = enemies(s)
    print(f"\nEnemies you can see: {len(seen)}")
    for e in seen[:6]:
        print(f"  {e['type']:<14} id {e['id']:<4} at ({e['x']},{e['y']})")
    return s


def send(*orders):
    """Send one or more orders right now, and say whether the game accepted each one."""
    _ready()
    orders = [o for group in orders for o in (group if isinstance(group, list) else [group])]
    reply = call("POST", f"matches/{match_id}/commands", {"commands": orders})
    for order, result in zip(orders, reply["results"]):
        if result["accepted"]:
            print(f"✅ {order['type']} accepted")
        else:
            print(f"❌ {order['type']} rejected: {result['error']['message']}")


# ---------------------------------------------------------------- helpers for your strategy

def my_units(s, kind=None):
    return [u for u in s["units"] if u["owner"] == s["you"]["slot"] and kind in (None, u["type"])]


def my_buildings(s, kind=None):
    return [b for b in s["buildings"] if b["owner"] == s["you"]["slot"] and kind in (None, b["type"])]


def my_hq(s):
    """Your HQ (the API calls it a CommandCenter), or None if it's gone."""
    hqs = my_buildings(s, "CommandCenter")
    return hqs[0] if hqs else None


def enemies(s):
    """Every enemy unit and building your badgers can see right now."""
    return [e for e in s["units"] + s["buildings"] if e["owner"] != s["you"]["slot"]]


def distance(a, b):
    """Tiles between two things (diagonal steps count as one, like a king in chess)."""
    return max(abs(a["x"] - b["x"]), abs(a["y"] - b["y"]))


def closest(things, to):
    return min(things, key=lambda t: distance(t, to)) if things else None


def buy(s, thing):
    """True if you can afford it, and takes the grubs out of this tick's budget."""
    if s["you"]["resources"] < COST[thing]:
        return False
    s["you"]["resources"] -= COST[thing]
    return True


def enemy_base(s):
    """Where the nearest rival started the game. Their HQ is probably still there."""
    me = s["you"]["slot"]
    home = my_hq(s) or {"x": s["mapWidth"] // 2, "y": s["mapHeight"] // 2}
    rivals = [p["startPosition"] for p in s["players"] if p["slot"] != me and not p["eliminated"]]
    return closest(rivals, home) if rivals else {"x": s["mapWidth"] // 2, "y": s["mapHeight"] // 2}


def build_spot(s, near, min_dist=3, max_dist=6):
    """An open tile near a building that isn't in the miners' way. Returns {'x':..,'y':..} or None."""
    terrain = GAME_MAP["terrain"]
    taken = {(b["x"], b["y"]) for b in s["buildings"]} | _reserved
    ore = s["resources"]
    best = None
    for y in range(near["y"] - max_dist, near["y"] + max_dist + 1):
        for x in range(near["x"] - max_dist, near["x"] + max_dist + 1):
            spot = {"x": x, "y": y}
            d = distance(spot, near)
            if not (min_dist <= d <= max_dist and 0 <= y < len(terrain) and 0 <= x < len(terrain[0])):
                continue
            if terrain[y][x] != "." or (x, y) in taken:
                continue
            near_ore = min((distance(spot, o) for o in ore), default=99)
            if near_ore <= 1:
                continue
            score = d - min(near_ore, 4)
            if best is None or score < best[0]:
                best = (score, spot)
    if best:
        _reserved.add((best[1]["x"], best[1]["y"]))
        return best[1]
    return None


# ---------------------------------------------------------------- order builders (each returns one order)

def _ids(units):
    units = units if isinstance(units, list) else [units]
    return [u["id"] if isinstance(u, dict) else u for u in units]


def gather(workers, pile):
    return {"type": "Gather", "unitIds": _ids(workers), "targetId": pile["id"]}


def train(building, unit_type, count=1):
    return {"type": "Produce", "buildingId": building["id"], "unitType": unit_type, "count": count}


def build(worker, building_type, spot):
    return {"type": "Build", "unitIds": _ids(worker), "buildingType": building_type, "x": spot["x"], "y": spot["y"]}


def attack(units, target):
    """target is an enemy (attack it) or a place like {'x': 50, 'y': 50} (march there, fighting on the way)."""
    if "id" in target:
        return {"type": "Attack", "unitIds": _ids(units), "targetId": target["id"]}
    return {"type": "Attack", "unitIds": _ids(units), "x": target["x"], "y": target["y"]}


def move(units, spot):
    return {"type": "Move", "unitIds": _ids(units), "x": spot["x"], "y": spot["y"]}


def research(building, upgrade):
    return {"type": "Research", "buildingId": building["id"], "upgrade": upgrade}


# ---------------------------------------------------------------- the bot loop

def _status(s, problem):
    me = s["you"]
    counts = " · ".join(f"{n} {k}" for k in ("Worker", "Soldier", "Archer", "Scout", "Trebuchet")
                        if (n := len(my_units(s, k))))
    line = (f"⏱ tick {s['tick']}/{s['maxTicks']} &nbsp; 🐛 {me['resources']} grubs &nbsp; 🦡 {counts or 'no units'}"
            f" &nbsp; 🏠 {len(my_buildings(s))} buildings &nbsp; 👁 {len(enemies(s))} enemies in sight")
    if problem:
        line += f"<br><span style='color:#c0392b'>last problem: {problem}</span>"
    return HTML(f"<div style='font-family:monospace'>{line}</div>")


def play(decide, vs=None, speed=5, max_ticks=None):
    """Let decide(state) run your badgers until the match ends.
    play(decide, vs="sitter") starts a new practice match first; play(decide) plays the current one."""
    if vs:
        practice(vs, speed, max_ticks)
    s = _ready()
    if s["status"] == "Completed":
        print("That match is over. Start a new one with play(decide, vs='sitter').")
        return
    board = display(_status(s, None), display_id=True)
    problem = None
    try:
        while s["status"] != "Completed":
            try:
                orders = decide(s) or []
            except Exception as e:  # a bug in decide() shouldn't end the match
                problem = f"your decide() crashed: {type(e).__name__}: {e}"
                orders = []
            if orders:
                reply = call("POST", f"matches/{match_id}/commands", {"commands": orders})
                for order, result in zip(orders, reply["results"]):
                    if not result["accepted"]:
                        problem = f"{order['type']} rejected: {result['error']['message']}"
            tick = s["tick"]
            s = call("GET", f"matches/{match_id}/state?waitForTick={tick + 1}&sinceTick={tick}")
            for e in s["events"]:
                if e["kind"] == "CommandFailed" and e.get("owner") in (None, s["you"]["slot"]):
                    problem = e["message"]
            board.update(_status(s, problem))
    except KeyboardInterrupt:
        print("⏸ Stopped. The match keeps going, but your badgers have no orders. play(decide) takes over again.")
        return
    show_result()


def show_result():
    r = call("GET", f"matches/{match_id}/result")
    winner = next((p["name"] for p in r["players"] if p["winner"]), None)
    rows = "".join(
        f"<tr><td>{'🏆 ' if p['winner'] else ''}{p['name']}</td>"
        f"<td>{p['score']['destruction'] + p['score']['economy'] + p['score']['survival']}</td>"
        f"<td>{p['score']['destruction']}</td><td>{p['score']['economy']}</td><td>{p['score']['survival']}</td>"
        f"<td>{p['unitsProduced']}</td><td>{p['unitsKilled']}</td><td>{p['unitsLost']}</td></tr>"
        for p in r["players"])
    display(HTML(
        f"<h3>{'🏆 ' + winner + ' wins!' if winner else 'A draw!'}  ({r['outcome']['ticks']} ticks, {r['outcome']['reason']})</h3>"
        "<table><tr><th>Clan</th><th>Score</th><th>Destruction</th><th>Economy</th><th>Survival</th>"
        f"<th>Trained</th><th>Defeated</th><th>Lost</th></tr>{rows}</table>"))


print("✅ Setup done. On to Step 1!")

# %% [markdown]
# ## Step 1: Name your clan
#
# Type a name in the **MY_NAME** box (3 to 32 letters, digits, `-` or `_`, no spaces), then run the cell.
# Start it with your school, like `manti-sam`, so your class can find each other. Keep it school-appropriate: everyone can see it!
#
# The first time, the game gives you a **secret key**. Paste it into the **API_KEY** box so you can come back later.

# %%
# @title Step 1: Name your clan
MY_NAME = ""  # @param {type:"string"}
API_KEY = ""  # @param {type:"string"}

if API_KEY:
    print(f"🦡 Welcome back, {who_am_i()['name']}!")
else:
    sign_up(MY_NAME)

# %% [markdown]
# ## Step 2: Start a practice match
#
# Your first opponent is the **Sleepy Sitter**, a house badger that only digs grubs and never fights back.
#
# Run the cell, then **click the link** to watch your campus in another tab. Keep that tab open next to this one.
# (Or run `watch()` to see it right here in the notebook.)
#
# This match runs at normal speed (1 tick per second) for 30 minutes, so you have time to give orders by hand.

# %%
practice("sitter")

# %% [markdown]
# ## Step 3: Look around
#
# Your bot only sees what your badgers can see. `look()` asks the server what that is.
#
# Find your **Worker ids**, your **CommandCenter id** (that's your HQ) and the **id of a grub pile** near it. You'll need them next.

# %%
s = look()

# %% [markdown]
# ## Step 4: Give your first order
#
# Orders are just little bits of JSON. This one says: *workers, go dig grubs from that pile.*
#
# Put a real grub pile id where it says `PILE_ID`, run it, then look at the other tab. Your workers should start moving!

# %%
PILE_ID = 0  # <-- change me

send({"type": "Gather", "units": "all", "unitType": "Worker", "targetId": PILE_ID})

# %% [markdown]
# Now train another worker at your HQ. Workers cost **50 grubs**.

# %%
HQ_ID = 0  # <-- change me

send({"type": "Produce", "buildingId": HQ_ID, "unitType": "Worker"})

# %% [markdown]
# **Try it yourself:** run `look()` again. Did your grubs go up? Is the new worker there?
# New workers stand around until you give them an order. Can you send just that one to dig?
# (Hint: `"unitIds": [ID]` picks particular badgers instead of `"units": "all"`.)

# %%
s = look()

# %% [markdown]
# ## Step 5: Teach your bot to play by itself
#
# Typing orders by hand is slow. A real bot runs a function called `decide` **every single tick**:
# it gets the current state, and it returns a list of orders.
#
# We'll build `decide` one skill at a time. To keep things short, each skill is its own small function.
#
# **Skill 1: idle workers go dig at the closest pile.**

# %%
def put_workers_to_work(s):
    orders = []
    for worker in my_units(s, "Worker"):
        if worker["activity"] == "Idle" and s["resources"]:
            pile = closest(s["resources"], worker)
            orders.append(gather(worker, pile))
    return orders


def decide(s):
    return put_workers_to_work(s)


# a quick 300-tick match at 10x speed, just to see it work
play(decide, vs="sitter", speed=10, max_ticks=300)

# %% [markdown]
# **Skill 2: grow the clan.** More workers means more grubs. Keep the HQ training workers until there are 10.
#
# `buy(s, "Worker")` checks you can afford it and takes the cost out of this tick's budget, so two skills can't spend the same grubs.

# %%
def train_workers(s):
    hq = my_hq(s)
    if hq and hq["completed"] and not hq["production"] and len(my_units(s, "Worker")) < 10 and buy(s, "Worker"):
        return [train(hq, "Worker")]
    return []


def decide(s):
    return train_workers(s) + put_workers_to_work(s)


play(decide, vs="sitter", speed=10, max_ticks=300)

# %% [markdown]
# Compare the **Economy** score with your first run. Did it go up?
#
# **Skill 3: build housing and train soldiers.** Housing (the API calls it a `Barracks`) costs 150 grubs and trains fighters.
# Once there are 6 workers, one of them goes to build it. `memory` lets the bot remember it already asked.

# %%
def build_barracks(s):
    if my_buildings(s, "Barracks") or s["tick"] - memory.get("barracks_ordered", -999) < 60:
        return []
    workers = my_units(s, "Worker")
    hq = my_hq(s)
    if not hq or len(workers) < 6 or not buy(s, "Barracks"):
        return []
    spot = build_spot(s, near=hq)
    if not spot:
        return []
    memory["barracks_ordered"] = s["tick"]
    return [build(closest(workers, hq), "Barracks", spot)]


def train_soldiers(s):
    orders = []
    for barracks in my_buildings(s, "Barracks"):
        if barracks["completed"] and not barracks["production"] and buy(s, "Soldier"):
            orders.append(train(barracks, "Soldier"))
    return orders


def decide(s):
    return build_barracks(s) + train_workers(s) + train_soldiers(s) + put_workers_to_work(s)


play(decide, vs="sitter", speed=10, max_ticks=500)

# %% [markdown]
# **Skill 4: ATTACK!** 🦡⚔️ Once there are 6 soldiers, every idle soldier charges. If they can see an enemy,
# they go for the closest one. If not, they march toward the enemy's starting corner and fight anything on the way.
#
# This is a full match. Can you beat the Sleepy Sitter?

# %%
def attack_when_ready(s):
    soldiers = my_units(s, "Soldier")
    if len(soldiers) >= 6:
        memory["attacking"] = True
    idle = [u for u in soldiers if u["activity"] == "Idle"]
    if not memory.get("attacking") or not idle:
        return []
    seen = enemies(s)
    target = closest(seen, idle[0]) if seen else enemy_base(s)
    return [attack(idle, target)]


def decide(s):
    return (build_barracks(s) + train_workers(s) + train_soldiers(s)
            + put_workers_to_work(s) + attack_when_ready(s))


play(decide, vs="sitter", speed=10)

# %% [markdown]
# ## Step 6: Beat the house badgers
#
# | House badger | Name in code | Plays like |
# |---|---|---|
# | **Sleepy Sitter** | `sitter` | Digs grubs, never fights back. |
# | **Blue Badger** | `balanced` | Steady economy, soldiers and archers, upgrades, attacks in waves. |
# | **Grub Hoarder** | `economist` | Out-digs everyone, hides behind bell towers, shows up late with a huge army. |
# | **Honey Badger** | `rusher` | Races to two Snow Mobiles (siege trucks) and rolls them at your campus. |
#
# Your bot from Step 5 probably loses to the others. Change your skills, add new ones, and try again:

# %%
play(decide, vs="balanced", speed=10)

# %% [markdown]
# ### Ideas to make your bot smarter
#
# **Archers** shoot from 4 tiles away. Mix them in:
# ```python
# kind = "Archer" if len(my_units(s, "Archer")) < len(my_units(s, "Soldier")) else "Soldier"
# if buy(s, kind): orders.append(train(barracks, kind))
# ```
#
# **A second Barracks** trains twice as fast. Change `if my_buildings(s, "Barracks")` to `if len(my_buildings(s, "Barracks")) >= 2`.
#
# **A Bell Tower** (`GuardTower`, 125 grubs, needs a Barracks first) shoots anything that comes close to your campus:
# ```python
# build(worker, "GuardTower", build_spot(s, near=hq, min_dist=2, max_dist=4))
# ```
#
# **Upgrades** need a Makerspace (`TechLab`, 150 grubs):
# ```python
# research(techlab, "Weapons1")   # +2 damage. Also: Armor1, Mobility1, then Weapons2, Armor2 ...
# ```
#
# **Don't charge in one at a time.** Wait for a bigger army, or `move` everyone to a meeting spot first.
#
# **Scouts** (75 grubs) run twice as fast and see farther. Send one to find the enemy early.
#
# Every price and stat in the game:

# %%
import pandas as pd
rules = call("GET", "rules")
display(pd.DataFrame(rules["units"])[["type", "cost", "maxHp", "damage", "armor", "range", "speed", "producedAt"]])
display(pd.DataFrame(rules["buildings"])[["type", "cost", "maxHp", "damage", "range", "produces", "requires"]])
display(pd.DataFrame(rules["upgrades"])[["type", "cost", "requires", "effect"]])

# %% [markdown]
# ## Step 7: Battle a classmate
#
# One of you **hosts** a match and picks a name for it. The other **joins** it by that name.
# Then you both run `play(decide)`. The match starts when both seats are full.

# %%
# The host runs this (pick your own match name):
host_match("alex-vs-sam", seats=2, speed=5)

# %%
# The other player runs this, with the same match name:
join_match("alex-vs-sam")

# %%
# Both players:
play(decide)

# %% [markdown]
# ## 🏟️ The class brawl
#
# Your teacher opens one big match for everybody, called `class-brawl`, with
# `host_match("class-brawl", seats=...)`. When they say go, join it and let your bots fight it out.

# %%
join_match("class-brawl")
play(decide)

# %% [markdown]
# ## Want more?
#
# - Every rule, order and error message: [the bot guide](https://github.com/SnowSE/NetRTS/blob/main/docs/bot-guide.md)
# - Try the API by hand: [netrts.snowse.io/scalar](https://netrts.snowse.io/scalar)
# - The whole game is open source: [github.com/SnowSE/NetRTS](https://github.com/SnowSE/NetRTS). It's written in C#.
# - Want to build things like this? Look at [Snow College's software engineering program](https://snow.edu/academics/science_math/engineering/se/) in Ephraim.
