# VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 20261119
"""Template for a new command module. Copy it, rename it WITHOUT the leading
underscore (e.g. cogs/fun.py) and it is loaded automatically on the next start.
Files starting with "_" are ignored by the loader.

Every hybrid_command works as both `j.name` and `/name`, and shows up in
`j.help` automatically (it uses the `description=` text).
"""
from discord import app_commands
from discord.ext import commands

from core.bot import JotaBot


class Example(commands.Cog):
    """Shown as a section title in the help list."""

    def __init__(self, bot: JotaBot):
        self.bot = bot

    @commands.hybrid_command(name="echo", description="Repeat what you say")
    @app_commands.describe(text="What to repeat")
    async def echo(self, ctx: commands.Context, *, text: str):
        """Repeat what you say. (Longer text here shows up in `j.help echo`.)"""
        await ctx.send(text)


async def setup(bot: JotaBot):
    await bot.add_cog(Example(bot))
