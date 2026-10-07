#!/usr/bin/env python3
"""NetRts reference bot in plain Python 3 (standard library only).

The whole protocol in one file:
  1. register a player (or reuse an API key)       POST /api/v1/players
  2. create a match vs a house bot, or join one     POST /api/v1/matches, POST /api/v1/matches/{id}/join
  3. every tick: long-poll the state, decide, send  GET  .../state?waitForTick=N, POST .../commands
  4. print the result                               GET  .../result

Strategy: mine with ~10 workers, build one Barracks, train soldiers non-stop and
attack-move to the enemy's start position once 6 soldiers are ready.
"""
import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.request


# --------------------------------------------------------------------------- HTTP

class ApiError(Exception):
    """Every non-2xx response carries a JSON body {"code": ..., "message": ...}."""

    def __init__(self, status, code, message):
        super().__init__(f"{status} {code}: {message}")
        self.status, self.code, self.message = status, code, message


class Api:
    def __init__(self, server, api_key=None):
        self.server = server.rstrip("/")
        self.api_key = api_key

    def call(self, method, path, body=None):
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(self.server + path, data=data, method=method)
        req.add_header("Content-Type", "application/json")
        if self.api_key:
            req.add_header("Authorization", f"Bearer {self.api_key}")
        try:
            with urllib.request.urlopen(req, timeout=60) as resp:  # long polls last up to 30 s
                raw = resp.read()
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as e:
            try:
                err = json.loads(e.read())
            except ValueError:
                err = {}
            raise ApiError(e.code, err.get("code", "HTTP_ERROR"), err.get("message", e.reason)) from None


# --------------------------------------------------------------------------- strategy

TARGET_WORKERS = 10
ATTACK_AT = 6  # soldiers needed before the first attack


def dist(ax, ay, bx, by):
    """Chebyshev distance: the game's notion of range and adjacency."""
    return max(abs(ax - bx), abs(ay - by))


class Bot:
    def __init__(self, game_map, rules):
        self.terrain = game_map["terrain"]  # rows of '.' (open) and '#' (rock)
        self.cost = {u["type"]: u["cost"] for u in rules["units"]}
        self.cost.update({b["type"]: b["cost"] for b in rules["buildings"]})
        self.barracks_ordered_at = -1000  # tick we last told a worker to build a Barracks
        self.attacking = False

    def decide(self, s):
        me = s["you"]["slot"]
        ore = s["you"]["resources"]
        units = [u for u in s["units"] if u["owner"] == me]
        buildings = [b for b in s["buildings"] if b["owner"] == me]
        enemies = [e for e in s["units"] + s["buildings"] if e["owner"] != me]
        workers = [u for u in units if u["type"] == "Worker"]
        soldiers = [u for u in units if u["type"] == "Soldier"]
        cc = next((b for b in buildings if b["type"] == "CommandCenter"), None)
        barracks = [b for b in buildings if b["type"] == "Barracks"]
        commands = []

        def spend(kind):
            nonlocal ore
            if ore < self.cost[kind]:
                return False
            ore -= self.cost[kind]
            return True

        # 1. Build a Barracks once we have a few workers mining and can afford it.
        if cc and not barracks and len(workers) >= 6 and s["tick"] - self.barracks_ordered_at > 40:
            site = self.find_site(cc, s)
            builder = min(workers, key=lambda w: (w["carrying"], dist(w["x"], w["y"], cc["x"], cc["y"])))
            if site and spend("Barracks"):
                commands.append({"type": "Build", "unitIds": [builder["id"]], "buildingType": "Barracks",
                                 "x": site[0], "y": site[1]})
                self.barracks_ordered_at = s["tick"]
                workers = [w for w in workers if w is not builder]

        # 2. Idle workers mine, spread over the visible deposits (near ones first).
        deposits = s["resources"]
        if deposits:
            load = {d["id"]: 0 for d in deposits}
            for w in workers:
                if w.get("targetId") in load:
                    load[w["targetId"]] += 1
            for w in workers:
                if w["activity"] != "Idle":
                    continue
                best = min(deposits, key=lambda d: dist(w["x"], w["y"], d["x"], d["y"]) + 4 * load[d["id"]])
                load[best["id"]] += 1
                commands.append({"type": "Gather", "unitIds": [w["id"]], "targetId": best["id"]})

        # 3. Keep the Command Center training workers up to the target (but save up for the Barracks).
        if cc and cc["completed"]:
            planned = len(workers) + len(cc["production"])
            saving = not barracks and len(workers) >= 6
            if planned < TARGET_WORKERS and len(cc["production"]) < 2 and not saving and spend("Worker"):
                commands.append({"type": "Produce", "buildingId": cc["id"], "unitType": "Worker"})

        # 4. Every finished Barracks trains soldiers continuously.
        for b in barracks:
            if b["completed"] and len(b["production"]) < 2 and spend("Soldier"):
                commands.append({"type": "Produce", "buildingId": b["id"], "unitType": "Soldier"})

        # 5. Attack: once ATTACK_AT soldiers exist, send every idle soldier in.
        if len(soldiers) >= ATTACK_AT:
            self.attacking = True
        if self.attacking:
            idle = [u["id"] for u in soldiers if u["activity"] == "Idle"]
            if idle:
                if enemies:  # something visible: go hit the closest thing
                    sx, sy = soldiers[0]["x"], soldiers[0]["y"]
                    target = min(enemies, key=lambda e: dist(sx, sy, e["x"], e["y"]))
                    commands.append({"type": "Attack", "unitIds": idle, "targetId": target["id"]})
                else:  # attack-move toward the enemy base (engages anything seen on the way)
                    tx, ty = self.enemy_base(s)
                    commands.append({"type": "Attack", "unitIds": idle, "x": tx, "y": ty})
        return commands

    def enemy_base(self, s):
        """Start position of the first opponent still in the game."""
        me = s["you"]["slot"]
        for p in s["players"]:
            if p["slot"] != me and not p["eliminated"]:
                return p["startPosition"]["x"], p["startPosition"]["y"]
        return s["mapWidth"] // 2, s["mapHeight"] // 2

    def find_site(self, cc, s):
        """An open tile 3-6 tiles from the CC, not on/next to a deposit and not on a building."""
        taken = {(b["x"], b["y"]) for b in s["buildings"]}
        ore = [(d["x"], d["y"]) for d in s["resources"]]
        height, width = len(self.terrain), len(self.terrain[0])
        best = None
        for y in range(cc["y"] - 6, cc["y"] + 7):
            for x in range(cc["x"] - 6, cc["x"] + 7):
                d = dist(x, y, cc["x"], cc["y"])
                if not (3 <= d <= 6 and 0 <= x < width and 0 <= y < height):
                    continue
                if self.terrain[y][x] != "." or (x, y) in taken:
                    continue
                if any(dist(x, y, ox, oy) <= 1 for ox, oy in ore):
                    continue
                # Prefer close to the CC, and away from the ore so we don't block the miners.
                near_ore = min((dist(x, y, ox, oy) for ox, oy in ore), default=99)
                score = d - min(near_ore, 4)
                if best is None or score < best[0]:
                    best = (score, x, y)
        return (best[1], best[2]) if best else None


# --------------------------------------------------------------------------- main loop

def log(msg):
    print(time.strftime("[%H:%M:%S] ") + msg, flush=True)


def get_into_match(api, args):
    settings = {k: v for k, v in {"tickIntervalMs": args.tick_ms, "maxTicks": args.max_ticks,
                                  "seed": args.seed}.items() if v is not None}
    if args.join:
        match = api.call("POST", f"/api/v1/matches/{args.join}/join")
        log(f"Joined match {match['matchId']}")
    elif args.create:
        match = api.call("POST", "/api/v1/matches", {"maxPlayers": args.players, "settings": settings})
        log(f"Created match {match['matchId']} - waiting for {args.players - 1} more player(s) to --join it")
    else:
        match = api.call("POST", "/api/v1/matches", {"houseBots": [args.vs], "settings": settings})
        log(f"Created match {match['matchId']} vs house bot '{args.vs}'")
    return match["matchId"]


def wait_for_start(api, match_id):
    """waitForTick=0 blocks (up to 30 s per request) until the match starts."""
    while True:
        try:
            return api.call("GET", f"/api/v1/matches/{match_id}/state?waitForTick=0")
        except ApiError as e:
            if e.code != "MATCH_NOT_STARTED":
                raise
            log("Waiting for the match to fill up...")


def play(api, match_id):
    state = wait_for_start(api, match_id)  # the map only exists once the match has started
    rules = api.call("GET", "/api/v1/rules")
    game_map = api.call("GET", f"/api/v1/matches/{match_id}/map")
    bot = Bot(game_map, rules)
    log(f"Match started: slot {state['you']['slot']} on a {game_map['width']}x{game_map['height']} map")

    while state["status"] != "Completed":
        commands = bot.decide(state)
        if commands:
            reply = api.call("POST", f"/api/v1/matches/{match_id}/commands", {"commands": commands})
            for r in reply["results"]:
                if not r["accepted"]:
                    log(f"tick {state['tick']}: rejected {commands[r['index']]['type']}: {r['error']['message']}")
        if state["tick"] % 100 == 0:
            mine = [u for u in state["units"] if u["owner"] == state["you"]["slot"]]
            log(f"tick {state['tick']}: ore {state['you']['resources']}, {len(mine)} units")

        # Long-poll for the next tick; sinceTick limits events to the ones we haven't seen.
        tick = state["tick"]
        state = api.call("GET", f"/api/v1/matches/{match_id}/state?waitForTick={tick + 1}&sinceTick={tick}")
        for e in state["events"]:
            if e["kind"] in ("CommandFailed", "BuildingCompleted", "PlayerEliminated"):
                log(f"tick {e['tick']}: {e['kind']}: {e['message']}")
    return state


def print_result(api, match_id):
    result = api.call("GET", f"/api/v1/matches/{match_id}/result")
    outcome = result["outcome"]
    winner = next((p["name"] for p in result["players"] if p["winner"]), "draw")
    print(f"\n=== Finished after {outcome['ticks']} ticks ({outcome['reason']}). Winner: {winner} ===")
    for p in result["players"]:
        sc = p["score"]
        total = sc["destruction"] + sc["economy"] + sc["survival"]
        print(f"  {p['name']:<20} total {total:>6}  (destruction {sc['destruction']}, economy {sc['economy']},"
              f" survival {sc['survival']})  units made {p['unitsProduced']}, lost {p['unitsLost']},"
              f" killed {p['unitsKilled']}")
    return winner


def main():
    ap = argparse.ArgumentParser(description="NetRts reference bot (Python, stdlib only)")
    ap.add_argument("--server", default=os.environ.get("NETRTS_SERVER", "http://localhost:5080"))
    ap.add_argument("--name", help="bot name to register (needed unless --key is given)")
    ap.add_argument("--key", default=os.environ.get("NETRTS_API_KEY"), help="existing API key")
    mode = ap.add_mutually_exclusive_group()
    mode.add_argument("--vs", default="sitter", help="house bot to play against (default: sitter)")
    mode.add_argument("--create", action="store_true", help="create a match for others to join")
    mode.add_argument("--join", metavar="MATCH_ID", help="join an existing match")
    ap.add_argument("--players", type=int, default=2, help="seats when using --create")
    ap.add_argument("--tick-ms", type=int, help="tick interval for a match you create")
    ap.add_argument("--max-ticks", type=int, help="tick limit for a match you create")
    ap.add_argument("--seed", type=int, help="map seed for a match you create")
    args = ap.parse_args()

    api = Api(args.server, args.key)
    try:
        if not api.api_key:
            if not args.name:
                ap.error("pass --name to register, or --key / NETRTS_API_KEY")
            try:
                reg = api.call("POST", "/api/v1/players", {"name": args.name})
            except ApiError as e:
                if e.code == "NAME_TAKEN":
                    sys.exit(f"'{args.name}' is already registered - pass its --key, or choose another --name.")
                raise
            api.api_key = reg["apiKey"]
            print(f"\nRegistered '{reg['name']}'. API key (shown once - save it!):\n  {reg['apiKey']}\n")

        match_id = get_into_match(api, args)
        play(api, match_id)
        print_result(api, match_id)
    except ApiError as e:
        sys.exit(f"API error {e.status} {e.code}: {e.message}")
    except urllib.error.URLError as e:
        sys.exit(f"Cannot reach {args.server}: {e.reason}")
    except KeyboardInterrupt:
        sys.exit("Interrupted.")


if __name__ == "__main__":
    main()
