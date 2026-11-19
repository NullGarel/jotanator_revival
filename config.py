# VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 20261119
import os

from dotenv import load_dotenv

load_dotenv()

TOKEN = os.environ.get("DISCORD_BOT_TOKEN", "")
SERP_API_KEY = os.environ.get("SERP_API_KEY", "")
SYNC_ON_START = os.environ.get("SYNC_ON_START", "1") == "1"

PREFIX = "j."
# Both cases accepted; "@Bot command" also works (added in core/bot.py)
PREFIXES = ("j.", "J.")

# Image search
TIMEOUT = 8          # seconds per provider
MAX_RESULTS = 30     # images kept per search
VIEW_TIMEOUT = 120   # seconds before buttons disable themselves
