<#
.SYNOPSIS
  An 8-player brawl: the C# starter bot, the Python starter bot and six house bots.

.DESCRIPTION
  Signs the Python bot up (once; its key is saved in python\apikey-<name>.txt), creates a named
  8-seat match with it in seat 0 and house bots in six more, then opens a window for each starter
  bot and the spectator page in your browser. The match starts when the C# bot takes the last seat.

  Needs Python 3.8+ (`python` on the PATH) and the .NET 10 SDK.

.EXAMPLE
  .\samples\demo-8-player.ps1
.EXAMPLE
  .\samples\demo-8-player.ps1 -Server http://localhost:5080 -TickMs 100
#>
param(
  [string]   $Server     = 'https://netrts.snowse.io',
  [string]   $MatchName  = "class-demo-$(Get-Date -Format 'HHmm')",
  [string]   $PythonName = 'python-starter',
  [string]   $CSharpName = 'csharp-starter',
  [string[]] $HouseBots  = @('rusher', 'balanced', 'economist', 'rusher', 'balanced', 'economist'),
  [int]      $TickMs     = 250,
  [int]      $MaxTicks   = 1500
)
$ErrorActionPreference = 'Stop'
$Server = $Server.TrimEnd('/')
$here = $PSScriptRoot

if ($HouseBots.Count -ne 6) { throw "Give exactly 6 house bots (seats 3-8); got $($HouseBots.Count)." }

# 1. The Python bot's API key: sign up the first time, then reuse the saved key.
$keyFile = Join-Path $here "python\apikey-$PythonName.txt"
if (Test-Path $keyFile) {
  $pyKey = (Get-Content $keyFile -Raw).Trim()
} else {
  Write-Host "Signing up $PythonName..."
  $player = Invoke-RestMethod -Method Post -Uri "$Server/api/v1/players" -ContentType 'application/json' `
    -Body (@{ name = $PythonName } | ConvertTo-Json)
  $pyKey = $player.apiKey
  Set-Content -Path $keyFile -Value $pyKey -NoNewline
  Write-Host "Saved its key in $keyFile"
}

# 2. The match: the Python bot takes seat 0 as its creator, house bots fill six more, one seat stays open.
$body = @{
  name       = $MatchName
  maxPlayers = 8
  houseBots  = $HouseBots
  settings   = @{ tickIntervalMs = $TickMs; maxTicks = $MaxTicks }
} | ConvertTo-Json
$match = Invoke-RestMethod -Method Post -Uri "$Server/api/v1/matches" -ContentType 'application/json' `
  -Headers @{ Authorization = "Bearer $pyKey" } -Body $body
$id = $match.matchId
Write-Host "Created '$MatchName' ($id): $PythonName + $($HouseBots -join ', ') + one seat for $CSharpName"

# 3. The bots, each in its own window so you can watch them log.
Start-Process python -WorkingDirectory (Join-Path $here 'python') `
  -ArgumentList @('bot.py', '--server', $Server, '--key', $pyKey, '--join', $id)
Start-Process dotnet -WorkingDirectory (Join-Path $here 'csharp') `
  -ArgumentList @('run', 'StarterBot.cs', '--player-name', $CSharpName, '--server', $Server, '--join-match', $id)

# 4. Watch it. Press 3D, then Full screen, then Follow the brawl.
$watch = "$Server/#/match/$MatchName"
Write-Host "Watch: $watch"
Start-Process $watch
