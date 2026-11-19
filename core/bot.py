# VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 20261119 @ 11h40m UTC

import logging
from pathlib import Path

import aiohttp
import discord
from discord.ext import commands

import config

log = logging.getLogger("jotanator")
COGS_DIR = Path(__file__).resolve().parent.parent / "cogs"


class JotaBot(commands.Bot):
    def __init__(self):
        intents = discord.Intents.default()
        intents.message_content = True  # required for prefix commands (enable it in the Dev Portal too)
        super().__init__(
            command_prefix=commands.when_mentioned_or(*config.PREFIXES),
            intents=intents,
            help_command=None,        # replaced by cogs/help.py
            case_insensitive=True,    # J.PING works too
            strip_after_prefix=True,  # "j. ping" works too
        )
        self._session: aiohttp.ClientSession | None = None

    @property
    def http_session(self) -> aiohttp.ClientSession:
        assert self._session is not None, "session is created in setup_hook"
        return self._session

    async def setup_hook(self):
        self._session = aiohttp.ClientSession()
        await self.load_all_cogs()
        if config.SYNC_ON_START:
            synced = await self.tree.sync()
            log.info("Synced %d slash commands", len(synced))

    async def load_all_cogs(self):
        """Load every cogs/*.py that doesn't start with an underscore."""
        for path in sorted(COGS_DIR.glob("*.py")):
            if path.stem.startswith("_"):
                continue
            ext = f"cogs.{path.stem}"
            try:
                await self.load_extension(ext)
                log.info("Loaded %s", ext)
            except Exception:
                log.exception("Failed to load %s", ext)

    async def on_ready(self):
        log.info("Logged in as %s (prefix: %s)", self.user, config.PREFIX)

    async def close(self):
        if self._session:
            await self._session.close()
        await super().close()
