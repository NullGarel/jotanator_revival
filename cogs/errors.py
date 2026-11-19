# VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 20261119
import logging

from discord.ext import commands

import config
from core.bot import JotaBot

log = logging.getLogger("jotanator")


class Errors(commands.Cog):
    """Central error handling for prefix and slash commands."""

    def __init__(self, bot: JotaBot):
        self.bot = bot

    @commands.Cog.listener()
    async def on_command_error(self, ctx: commands.Context, error: Exception):
        if isinstance(error, commands.CommandNotFound):
            return
        # unwrap CommandInvokeError / HybridCommandError to the real exception
        for _ in range(3):
            inner = getattr(error, "original", None)
            if inner is None:
                break
            error = inner

        if isinstance(error, commands.CommandOnCooldown):
            msg = f"Slow down, try again in {error.retry_after:.1f}s."
        elif isinstance(error, commands.MissingRequiredArgument):
            usage = f"{config.PREFIX}{ctx.command.qualified_name} {ctx.command.signature}" if ctx.command else ""
            msg = f"Missing `{error.param.name}`. Usage: `{usage.strip()}`"
        elif isinstance(error, commands.NotOwner):
            msg = "Owner only."
        elif isinstance(error, (commands.BadArgument, commands.CheckFailure)):
            msg = str(error) or "You can't use that here."
        else:
            log.error("Unhandled error in %s", ctx.command, exc_info=error)
            msg = "Something went wrong."
        await ctx.send(msg, ephemeral=True)


async def setup(bot: JotaBot):
    await bot.add_cog(Errors(bot))
