# Badger Brawl tutorial: your first bot

Badger Brawl is a real-time strategy game where you don't click: you write a program (a *bot*) that
plays for you over a REST API. This tutorial takes you from nothing to a bot that mines ore,
builds a Barracks, trains an army and destroys the house bot `sitter`. You watch every step in
the spectator page in your browser.

Every code example comes in two languages, **C#** and **Python**, which do exactly the same thing.
Pick one and stick with it.

You need:

- the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), to run the game server;
- for your bot, either the same **.NET 10 SDK** (for C#) **or** **Python 3.10** or newer. Both
  versions use only the built-in libraries, so there's nothing to install from NuGet or `pip`;
- a terminal: bash, Git Bash or PowerShell all work;
- about an hour.

You don't need to know anything about REST APIs or game bots yet. If you can write a `for` loop
and a function in C# or Python, you're ready.

## Pages

1. [Start the server and look around](01-start-the-server.md): run the game, watch two house bots play.
2. [Talk to the API by hand](02-talk-to-the-api.md): register, start a match and give orders with `curl` or PowerShell.
3. [Your first bot: the loop](03-first-bot-loop.md): a program that follows a match tick by tick.
4. [Put your workers to work](04-put-workers-to-work.md): mine ore and train more workers.
5. [Build and train](05-build-and-train.md): pick a build site, build a Barracks, train soldiers, handle errors.
6. [Attack and win](06-attack-and-win.md): march on the enemy base and read the result.
7. [Where next](07-where-next.md): ideas for making your bot stronger.

Each page adds one idea to the bot. The finished code for every step, in both languages, is in
[`code/`](code/):

| C# | Python | Page | What it adds |
|---|---|---|---|
| [`step1_loop.cs`](code/step1_loop.cs) | [`step1_loop.py`](code/step1_loop.py) | 3 | Register, create a match, follow it tick by tick |
| [`step2_economy.cs`](code/step2_economy.cs) | [`step2_economy.py`](code/step2_economy.py) | 4 | Workers mine; the Command Center trains more |
| [`step3_army.cs`](code/step3_army.cs) | [`step3_army.py`](code/step3_army.py) | 5 | Build a Barracks, train soldiers, report errors |
| [`step4_attack.cs`](code/step4_attack.cs) | [`step4_attack.py`](code/step4_attack.py) | 6 | Attack. This bot beats `sitter` |

Each C# file is a .NET 10 *file-based app*: a single `.cs` file that you run with
`dotnet run step1_loop.cs`, with no project file. Each Python file runs with `python step1_loop.py`.

Other docs you'll want later:

- [Bot guide](../bot-guide.md): the complete API and rules reference (every command, error code and stat).
- [Getting started](../getting-started.md): other ways to set up and run NetRts.
- [How it works](../how-it-works.md): what happens inside the server.

[Start: Start the server and look around →](01-start-the-server.md)
