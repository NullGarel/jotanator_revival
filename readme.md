# Jotanator

A silly stupid (partially, at least for now, vibe coded) discord bot that adds fun functionalities.

- .env structure:

```bash
DISCORD_BOT_TOKEN=XXX
SERP_API_KEY=XXX
SYNC_ON_START=1
```

- Run it on background

```bash
nohup python main.py > bot.log 2>&1 &
echo $! > bot.pid
```
