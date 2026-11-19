# VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 20261119
import config
from core.bot import JotaBot


def main():
    if not config.TOKEN:
        raise SystemExit("DISCORD_BOT_TOKEN is not set (check your .env).")
    JotaBot().run(config.TOKEN)


if __name__ == "__main__":
    main()
