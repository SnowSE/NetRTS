#!/usr/bin/env python3
"""NetRts tutorial, step 3: build a Barracks and train an army.

Step 2's economy, plus: pick a build site from the map terrain, send one worker
to build a Barracks, train Soldiers there, and report rejected commands and
CommandFailed events. The soldiers just stand around for now.

    python step3_army.py --name my-bot --max-ticks 300

Only the Python standard library is used.
"""
import argparse
import json
import os
import urllib.error
import urllib.request

SERVER = "http://localhost:5080"
API_KEY = None  # filled in by get_api_key()


# --------------------------------------------------------------------------- HTTP helper

class ApiError(Exception):
    """The server answered with an error, e.g. 409 {"code": "MATCH_NOT_STARTED", ...}."""

    def __init__(self, status, code, message):
        super().__init__(f"{status} {code}: {message}")
        self.status, self.code = status, code


def api(method, path, body=None):
    """Send one request to the server and return the decoded JSON reply (or None)."""
    data = json.dumps(body).encode() if body is not None else None
    request = urllib.request.Request(SERVER + path, data=data, method=method)
    request.add_header("Content-Type", "application/json")
    if API_KEY:
        request.add_header("Authorization", f"Bearer {API_KEY}")
    try:
        # A long poll can be held open for up to 30 s, so allow a generous timeout.
        with urllib.request.urlopen(request, timeout=60) as response:
            raw = response.read()
            return json.loads(raw) if raw else None
    except urllib.error.HTTPError as e:
        try:
            error = json.loads(e.read())
        except ValueError:
            error = {}
        raise ApiError(e.code, error.get("code", "HTTP_ERROR"), error.get("message", e.reason)) from None


# --------------------------------------------------------------------------- setup

def get_api_key(name):
    """Reuse the key saved in <name>.key, or register the name and save the new key."""
    key_file = f"{name}.key"
    if os.path.exists(key_file):
        with open(key_file) as f:
            return f.read().strip()
    reply = api("POST", "/api/v1/players", {"name": name})
    with open(key_file, "w") as f:
        f.write(reply["apiKey"])
    print(f"Registered '{name}'. API key saved to {key_file}")
    return reply["apiKey"]


def create_match(opponent, tick_ms=None, max_ticks=None, seed=None):
    settings = {}
    if tick_ms:
        settings["tickIntervalMs"] = tick_ms
    if max_ticks:
        settings["maxTicks"] = max_ticks
    if seed is not None:
        settings["seed"] = seed
    match = api("POST", "/api/v1/matches", {"houseBots": [opponent], "settings": settings})
    print(f"Match {match['matchId']} created against '{opponent}'")
    print(f"Watch it at {SERVER}/#/match/{match['matchId']}")
    return match["matchId"]


def wait_for_start(match_id):
    """waitForTick=0 blocks until the match has started (409 MATCH_NOT_STARTED means: ask again)."""
    while True:
        try:
            return api("GET", f"/api/v1/matches/{match_id}/state?waitForTick=0")
        except ApiError as e:
            if e.code != "MATCH_NOT_STARTED":
                raise


# --------------------------------------------------------------------------- strategy

TARGET_WORKERS = 12
COST = {"Worker": 50, "Soldier": 100, "Barracks": 150}  # GET /api/v1/rules has every cost

terrain = []                # the map's rows: '.' = open ground, '#' = rock (loaded once in play())
barracks_ordered_at = None  # tick when we last sent a worker to build a Barracks
barracks_builder_id = None  # the worker we sent to build it


def distance(ax, ay, bx, by):
    """Tiles between two points, counting diagonal steps as 1 (the game's own measure)."""
    return max(abs(ax - bx), abs(ay - by))


def find_build_site(command_center, state):
    """An open tile 3-6 tiles from the Command Center that doesn't get in the miners' way."""
    taken = {(b["x"], b["y"]) for b in state["buildings"]}
    ore = [(d["x"], d["y"]) for d in state["resources"]]
    cx, cy = command_center["x"], command_center["y"]
    best, best_score = None, None
    for y in range(cy - 6, cy + 7):
        for x in range(cx - 6, cx + 7):
            if not (0 <= x < state["mapWidth"] and 0 <= y < state["mapHeight"]):
                continue  # off the map
            if terrain[y][x] != "." or (x, y) in taken:
                continue  # rock, or a building is already there
            d = distance(x, y, cx, cy)
            if d < 3 or d > 6:
                continue  # too close (blocks the CC) or too far
            to_ore = min((distance(x, y, ox, oy) for ox, oy in ore), default=99)
            if to_ore <= 1:
                continue  # on or right next to a deposit: workers need that space
            score = d - min(to_ore, 4)  # close to the CC, but away from the ore
            if best is None or score < best_score:
                best, best_score = (x, y), score
    return best


def decide(state):
    """Look at this tick's state and return a list of commands to send."""
    global barracks_ordered_at, barracks_builder_id
    me = state["you"]["slot"]
    tick = state["tick"]
    budget = state["you"]["resources"]  # ore we haven't promised to anything yet this tick
    workers = [u for u in state["units"] if u["owner"] == me and u["type"] == "Worker"]
    my_buildings = [b for b in state["buildings"] if b["owner"] == me]
    command_center = next((b for b in my_buildings if b["type"] == "CommandCenter"), None)
    barracks = [b for b in my_buildings if b["type"] == "Barracks"]  # finished or under construction
    commands = []

    # 1. Build one Barracks once 6 workers are mining. Until it exists, save ore for it.
    saving_for_barracks = command_center is not None and not barracks and len(workers) >= 6
    recently_ordered = barracks_ordered_at is not None and tick - barracks_ordered_at < 30
    if saving_for_barracks and not recently_ordered and budget >= COST["Barracks"]:
        site = find_build_site(command_center, state)
        if site:
            # The worker closest to the CC and not carrying ore makes the best builder.
            builder = min(workers, key=lambda w: (w["carrying"], distance(w["x"], w["y"],
                                                                          command_center["x"], command_center["y"])))
            commands.append({"type": "Build", "unitIds": [builder["id"]], "buildingType": "Barracks",
                             "x": site[0], "y": site[1]})
            budget -= COST["Barracks"]
            barracks_ordered_at = tick
            barracks_builder_id = builder["id"]
            workers.remove(builder)  # don't send the builder off mining in step 2 below
            print(f"tick {tick}: worker {builder['id']} will build a Barracks at {site}")

    # 2. Idle workers go mining. Count how many workers already mine each deposit and
    #    prefer close deposits with few miners, so the workers spread out.
    deposits = state["resources"]  # only the deposits we can currently see
    if deposits:
        miners = {d["id"]: 0 for d in deposits}
        for w in workers:
            if w["targetId"] in miners:
                miners[w["targetId"]] += 1
        for w in workers:
            if w["activity"] != "Idle":
                continue
            best = min(deposits, key=lambda d: distance(w["x"], w["y"], d["x"], d["y"]) + 3 * miners[d["id"]])
            miners[best["id"]] += 1
            commands.append({"type": "Gather", "unitIds": [w["id"]], "targetId": best["id"]})

    # 3. Train more workers, one or two at a time, until we have TARGET_WORKERS
    #    (but not while we're saving up for the Barracks).
    if command_center and command_center["completed"] and not saving_for_barracks:
        queued = len(command_center["production"])
        if len(workers) + queued < TARGET_WORKERS and queued < 2 and budget >= COST["Worker"]:
            commands.append({"type": "Produce", "buildingId": command_center["id"], "unitType": "Worker"})
            budget -= COST["Worker"]

    # 4. Every finished Barracks keeps two Soldiers in its queue.
    for b in barracks:
        if b["completed"] and len(b["production"]) < 2 and budget >= COST["Soldier"]:
            commands.append({"type": "Produce", "buildingId": b["id"], "unitType": "Soldier"})
            budget -= COST["Soldier"]

    return commands


def send(match_id, commands):
    """POST the commands and report any the server rejected straight away."""
    reply = api("POST", f"/api/v1/matches/{match_id}/commands", {"commands": commands})
    for result in reply["results"]:
        if not result["accepted"]:
            command = commands[result["index"]]
            print(f"  rejected {command['type']}: {result['error']['code']} - {result['error']['message']}")


def handle_events(state):
    """React to what happened since the last tick."""
    global barracks_ordered_at
    for event in state["events"]:
        if event["kind"] == "CommandFailed":
            # Accepted earlier, but it couldn't be carried out when the time came.
            print(f"tick {event['tick']}: command failed: {event['message']}")
            if event["entityId"] == barracks_builder_id:
                barracks_ordered_at = None  # our builder gave up: try again
        elif event["kind"] in ("BuildingStarted", "BuildingCompleted", "UnitLost"):
            print(f"tick {event['tick']}: {event['message']}")


# --------------------------------------------------------------------------- the loop

def play(match_id):
    global terrain
    state = wait_for_start(match_id)
    # The terrain never changes, so fetch it once. It only exists once the match has started.
    terrain = api("GET", f"/api/v1/matches/{match_id}/map")["terrain"]
    print(f"Started! We are slot {state['you']['slot']} on a {state['mapWidth']}x{state['mapHeight']} map")

    while state["status"] != "Completed":
        handle_events(state)
        commands = decide(state)
        if commands:
            send(match_id, commands)

        if state["tick"] % 10 == 0:
            me = state["you"]["slot"]
            mine = [u for u in state["units"] if u["owner"] == me]
            workers = sum(1 for u in mine if u["type"] == "Worker")
            soldiers = sum(1 for u in mine if u["type"] == "Soldier")
            print(f"tick {state['tick']:4}: ore {state['you']['resources']:5}, "
                  f"{workers} workers, {soldiers} soldiers")

        # Wait for the next tick. sinceTick=<tick> means "only events newer than this tick".
        tick = state["tick"]
        state = api("GET", f"/api/v1/matches/{match_id}/state?waitForTick={tick + 1}&sinceTick={tick}")

    handle_events(state)  # the final tick's events
    print(f"Match over after {state['outcome']['ticks']} ticks: {state['outcome']['reason']}")


def main():
    global SERVER, API_KEY
    parser = argparse.ArgumentParser(description="NetRts tutorial bot")
    parser.add_argument("--name", required=True, help="your bot's name (3-32 letters, digits, _ or -)")
    parser.add_argument("--server", default=SERVER)
    parser.add_argument("--vs", default="sitter", help="house bot to play against")
    parser.add_argument("--tick-ms", type=int, help="milliseconds per tick (default 1000, minimum 100)")
    parser.add_argument("--max-ticks", type=int, help="end the match after this many ticks (default 1800)")
    parser.add_argument("--seed", type=int, help="map seed, to replay the same map")
    args = parser.parse_args()

    SERVER = args.server.rstrip("/")
    API_KEY = get_api_key(args.name)
    match_id = create_match(args.vs, args.tick_ms, args.max_ticks, args.seed)
    try:
        play(match_id)
    except KeyboardInterrupt:
        # Don't leave a half-played match running: concede it.
        api("POST", f"/api/v1/matches/{match_id}/surrender")
        print("\nSurrendered.")


if __name__ == "__main__":
    main()
