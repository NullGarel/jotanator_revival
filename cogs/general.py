# VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 20261119
from discord.ext import commands

from core.bot import JotaBot


class General(commands.Cog):
    """Basic utility commands."""

    def __init__(self, bot: JotaBot):
        self.bot = bot

    @commands.hybrid_command(name="ping", description="Check the bot's latency")
    async def ping(self, ctx: commands.Context):
        """Check the bot's latency."""
        await ctx.send(f"Pong! {round(self.bot.latency * 1000)}ms")


async def setup(bot: JotaBot):
    await bot.add_cog(General(bot))
