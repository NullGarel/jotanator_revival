# VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 20261119
from discord.ext import commands

from core.bot import JotaBot


class Admin(commands.Cog):
    """Owner-only tools (hidden from help)."""

    def __init__(self, bot: JotaBot):
        self.bot = bot

    @commands.command(hidden=True)
    @commands.is_owner()
    async def reload(self, ctx: commands.Context, name: str):
        """Reload a cog without restarting: j.reload images"""
        await self.bot.reload_extension(f"cogs.{name}")
        await ctx.send(f"Reloaded `{name}`.")

    @commands.command(hidden=True)
    @commands.is_owner()
    async def sync(self, ctx: commands.Context):
        """Push slash commands to Discord (run after adding/changing commands)."""
        synced = await self.bot.tree.sync()
        await ctx.send(f"Synced {len(synced)} slash commands.")


async def setup(bot: JotaBot):
    await bot.add_cog(Admin(bot))
