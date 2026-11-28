# Jotanator (C# / Discord.Net port)

A silly stupid (practically fully vibe coded) discord bot that adds fun functionalities.
Port of the original Python bot; same prefixes (`j.` / `J.` / `@Bot`), and every command also works as a slash command.

## Requirements

- .NET 10 SDK
- NuGet packages (restored automatically): `Discord.Net`, `Microsoft.Data.Sqlite`, `SixLabors.ImageSharp`
- In the Discord Developer Portal enable **Message Content Intent**
- For the background deploy: Linux with systemd

## .env structure

```bash
DISCORD_BOT_TOKEN=XXX
SERP_API_KEY=XXX
SYNC_ON_START=1

# Jotacoin economy (all optional)
DB_PATH=jotanator.db      # where balances/stats are stored (SQLite file)
START_BALANCE=100         # what a brand-new account starts with
PAY_FEE_PERCENT=5         # fee burned on every j.pay
```

## Run

```bash
dotnet run -c Release
```

## Commands

`j.help` lists everything; `j.help <command>` shows details. Commands marked with `/` also work as slash commands.

### Jotacoin economy (J$)

J$ is both the currency and the score: every game adds or removes a small amount depending on whether you win or lose.
Balances live in one SQLite file (`DB_PATH`), global across servers.

| Command | What it does |
| --- | --- |
| `j.balance [user]` | Balance, rank, badges and title |
| `j.daily` | Claim a random 32-512 J$ once per UTC day |
| `j.pay <user> <amount>` | Send J$ (a small fee is burned, see `PAY_FEE_PERCENT`) |
| `j.top` | Leaderboard |
| `j.stats [user]` | Win/loss/draw record per game |
| `j.inflation` | Price index, 14-day trend and money supply |
| `j.shop`, `j.buy <item>`, `j.inventory`, `j.title <text>` | Spend J$: Lucky Charm (doubles your next daily), badges, custom title |

**Inflation:** shop prices and game amounts are multiplied by a price index. The index follows the average J$ held per
active player (smoothed day by day, clamped to 0.5x-4x) and there is a small daily chance of a random market event
(boom, crash, shortage, sale). Money spent in the shop and `pay` fees leave the economy.

### Games

Games marked **(H&B)** can be played against the bot (default) or against a member: mention them to challenge them
(`j.ttt @friend`). Games marked **J$** use a bet.

| Command | Notes |
| --- | --- |
| `j.tictactoe` / `j.ttt` (H&B) | easy / medium / hard (unbeatable) |
| `j.connect4` / `j.c4` (H&B) | easy / medium / hard |
| `j.minesweeper` / `j.mines` | menus + buttons, first reveal is always safe |
| `j.hangman` | `en` or `pt` words, letter menus |
| `j.blackjack [bet]` (J$) (H&B) | vs the dealer (3:2 on naturals), or a 21 duel with hidden hands |
| `j.craps [bet] [pass/dontpass]` (J$) (H&B) | pass line vs don't pass, 1:1 |
| `j.horse [bet]` (J$) (H&B) | five horses with odds, animated race |
| `j.fillet [bet]` (J$) (H&B) | press-your-luck knife game |
| `j.dice [2d6]` | plain dice, no stakes |

Rules of the money:

- Against the bot, free games (tic tac toe, Connect Four, minesweeper, hangman) pay a small reward for a win and take half of it for a loss.
- Against another member, both players put the same small wager (or the bet) in escrow and the winner takes the pot, so duels never create or destroy J$.
- Stakes are held in the database while a game runs. If the bot restarts mid-game, they are refunded on startup.
- Quitting a game that has J$ at stake counts as a loss.

### Utilities and images

| Command | What it does |
| --- | --- |
| `j.av [user]` | Avatar as a file (PNG/GIF), plus the server avatar if the member has one |
| `j.e <emoji>` | Custom (or standard) emoji as a file |
| `j.sav` | Server icon as a file |
| `j.togif`, `j.fliph`, `j.flipv`, `j.neg`, `j.grayscale` | Image edits |
| `j.rchannel`, `j.gchannel`, `j.bchannel` | Keep only the red / green / blue channel |
| `j.hue <0-360>` | Rotate the hue |

Image commands use an attached image, the image of the message you reply to, or the latest image in the channel.
Animated GIFs keep their animation. Only images hosted on Discord are fetched, and size limits apply.

## Run it in the background (Linux, systemd)

`deploy.sh` publishes the bot and installs it as a systemd **user** service: it starts at boot,
restarts if it crashes and keeps running after you log out. Keep it next to `Jotanator.csproj` and `.env`.

```bash
chmod +x deploy.sh
./deploy.sh
```

What it does:

1. Publishes a Release build.
2. Installs it to `~/jotanator` (override with `INSTALL_DIR=/path ./deploy.sh`) and copies your `.env` there.
3. Creates and starts the `jotanator` service and enables lingering (so it survives logout and starts at boot).

Run `./deploy.sh` again after every code change to redeploy. Your economy database (`jotanator.db` in the install folder)
is not touched by redeploys.

| Command | What it does |
| --- | --- |
| `./deploy.sh` | Publish, install and (re)start the bot |
| `./deploy.sh status` | Show the service status |
| `./deploy.sh logs` | Follow the live logs |
| `./deploy.sh stop` | Stop the bot |
| `./deploy.sh uninstall` | Remove the service (keeps `~/jotanator`) |

Notes:

- If lingering can't be enabled automatically, run `sudo loginctl enable-linger $USER` once.
- The token lives in `~/jotanator/.env`; the script sets it to `chmod 600`.
- **Back up `~/jotanator/jotanator.db`** (stop the bot first, or the `-wal` file may hold recent changes).
- Without systemd you can still run it manually:

```bash
dotnet publish -c Release -o publish
cd publish
nohup ./Jotanator > bot.log 2>&1 &
echo $! > bot.pid
```

(Put the `.env` next to the binary, or in a parent folder.)

## Adding commands

Copy `Cogs/_ExampleCog.cs`, rename the class (keep the `Cog` suffix), remove `[Disabled]`.
Every `CommandDef` is both a `j.` command and a slash command and shows up in `j.help` automatically.
Parameters can be text, whole numbers (`ParamKind.Int`), users, members or attachments.
For a new button game, derive from `GameCog` and `GameSession` (see `Cogs/TicTacToeCog.cs`): the base classes handle
challenges, stakes, payouts, stats and timeouts.
