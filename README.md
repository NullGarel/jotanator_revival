# Jotanator (C# / Discord.Net port)

A silly stupid (partially, at least for now, vibe coded) discord bot that adds fun functionalities.
Port of the original Python bot; same commands, same prefixes (`j.` / `J.` / `@Bot`), same slash commands.

## Requirements

- .NET 8 SDK
- NuGet package: `Discord.Net` (restored automatically)
- In the Discord Developer Portal enable **Message Content Intent**

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

## Run it on background (Linux)

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
