# C# starter bot

A whole Badger Brawl bot in one file, [`StarterBot.cs`](StarterBot.cs), small enough for a first
programming class. There's no project to set up: .NET 10 runs a single `.cs` file directly, and
downloads the one package it uses ([CommandLineParser](https://github.com/commandlineparser/commandline),
which reads the options below) the first time you run it.

## Run it

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Save [`StarterBot.cs`](https://raw.githubusercontent.com/SnowSE/NetRTS/main/samples/csharp/StarterBot.cs)
   into an empty folder, or download it from a terminal:

   ```bash
   curl -O https://raw.githubusercontent.com/SnowSE/NetRTS/main/samples/csharp/StarterBot.cs
   ```

   (In Windows PowerShell type `curl.exe` instead of `curl`.)

3. In that folder, run it with a name of your own (3-32 letters, digits, `-` or `_`):

   ```bash
   dotnet run StarterBot.cs --player-name Frank
   ```

4. Open the link it prints to watch your match at <https://netrts.snowse.io>.

The first run signs you up and saves your secret key in a file named after your bot, like
`apikey-Frank.txt`. Keep that file; it's how the server knows the bot is you.

## Options

| Option | What it does |
|---|---|
| `--player-name Frank` | Your bot's name. Without it the bot asks. |
| `--opponent rusher` | The house bot to play: `sitter` (default, easiest), `economist`, `balanced` or `rusher` (hardest). |
| `--tick-ms 250` | Milliseconds per tick for a match you start (100-10000, default 1000). |
| `--create-match our-game` | Start a match for a classmate to join, instead of playing a house bot. |
| `--join-match our-game` | Join a waiting match by its name (or its id). |
| `--server http://localhost:5080` | Play on another server, like one running on your own computer. |

Every option, and its default, is a property of the `Options` class at the bottom of `StarterBot.cs`.
Change a `Default` there to change the setting, like your name, so you don't have to type it each
time.

To play a classmate, one of you runs `--create-match our-game` and the other `--join-match our-game`.
The first bot waits until the second one joins, then the match starts.

To see the options from the bot itself, put `--` before `--help` (otherwise `dotnet run` shows its
own help):

```bash
dotnet run StarterBot.cs -- --help
```

To add an option of your own, add a property with an `[Option("my-option")]` attribute to the
`Options` class.

## What the bot does

The program runs six steps: read the options (one line, thanks to CommandLineParser), sign up, get
into a match, wait for it to start, play, and print the result. Each step after the first is its own
method. The strategy is in `Decide`, which calls one small method per idea:

1. `GatherOre`: idle workers dig the ore closest to the headquarters.
2. `TrainWorker`: the headquarters trains workers until there are 8.
3. `BuildBarracks`: one worker builds a Barracks.
4. `TrainSoldier`: the Barracks trains soldiers.
5. `Attack`: with 5 soldiers, they attack the enemy's starting corner.

It beats `sitter`, and sometimes `economist`; `balanced` and `rusher` are too strong for it. Use
`--opponent` to pick which house bot you play.

## Ideas to make it better

- Train more workers, or send some to a second ore deposit.
- Build a second Barracks.
- Mix in Archers (they shoot from 4 tiles away).
- Defend: when enemies come near your headquarters, attack them with everything.
- Research upgrades at a TechLab.

The [bot guide](../../docs/bot-guide.md) lists every command, unit, building and rule.
