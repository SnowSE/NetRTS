#!/usr/bin/env python3
"""NetRts tutorial, step 1: the bot loop.

Registers (or reuses a saved API key), starts a match against a house bot, then
follows the match tick by tick and prints what it sees. It sends no orders yet.

    python step1_loop.py --name my-bot
    python step1_loop.py --name my-bot --max-ticks 30      # a short match

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


# --------------------------------------------------------------------------- the loop

def play(match_id):
    state = wait_for_start(match_id)
    print(f"Started! We are slot {state['you']['slot']} on a {state['mapWidth']}x{state['mapHeight']} map")

    while state["status"] != "Completed":
        me = state["you"]["slot"]
        my_units = [u for u in state["units"] if u["owner"] == me]
        print(f"tick {state['tick']:4}: ore {state['you']['resources']:5}, {len(my_units)} units")

        # Wait for the next tick. sinceTick=<tick> means "only events newer than this tick".
        tick = state["tick"]
        state = api("GET", f"/api/v1/matches/{match_id}/state?waitForTick={tick + 1}&sinceTick={tick}")

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
