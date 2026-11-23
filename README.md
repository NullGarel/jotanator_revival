# Jotanator (C# / Discord.Net port)

A silly stupid (practically fully vibe coded) discord bot that adds fun functionalities.
Port of the original Python bot; same commands, same prefixes (`j.` / `J.` / `@Bot`), same slash commands.

## Requirements

- .NET 8 SDK
- NuGet package: `Discord.Net` (restored automatically)
- In the Discord Developer Portal enable **Message Content Intent**
- For the background deploy: Linux with systemd

## .env structure

```bash
DISCORD_BOT_TOKEN=XXX
SERP_API_KEY=XXX
SYNC_ON_START=1
```

## Run

```bash
dotnet run -c Release
```

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

Run `./deploy.sh` again after every code change to redeploy.

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
