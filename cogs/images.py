# VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 20261119
import aiohttp
import discord
from discord import app_commands
from discord.ext import commands

import config
from core.bot import JotaBot
from services.image_search import find_images, is_valid_image


class ImageView(discord.ui.View):
    def __init__(self, session: aiohttp.ClientSession, user_id: int, query: str,
                 source: str, urls: list[str], start: int):
        super().__init__(timeout=config.VIEW_TIMEOUT)
        self.session = session
        self.user_id = user_id
        self.query = query
        self.source = source
        self.urls = urls
        self.index = start
        self.valid = {start: True}   # cache of index -> loadable?
        self.message: discord.Message | None = None

    def embed(self):
        e = discord.Embed(title=self.query[:200])
        e.set_image(url=self.urls[self.index])
        e.set_footer(text=f"{self.source} • {self.index + 1}/{len(self.urls)}")
        return e

    async def interaction_check(self, interaction: discord.Interaction):
        if interaction.user.id != self.user_id:
            await interaction.response.send_message(
                f"Run your own `{config.PREFIX}img` to browse.", ephemeral=True)
            return False
        return True

    async def step(self, interaction: discord.Interaction, direction: int):
        await interaction.response.defer()
        n = len(self.urls)
        idx = self.index
        for _ in range(n - 1):
            idx = (idx + direction) % n
            if idx not in self.valid:
                self.valid[idx] = await is_valid_image(self.session, self.urls[idx])
            if self.valid[idx]:
                self.index = idx
                break
        await interaction.edit_original_response(embed=self.embed(), view=self)

    @discord.ui.button(emoji="◀", style=discord.ButtonStyle.secondary)
    async def prev(self, interaction: discord.Interaction, button: discord.ui.Button):
        await self.step(interaction, -1)

    @discord.ui.button(emoji="▶", style=discord.ButtonStyle.secondary)
    async def next(self, interaction: discord.Interaction, button: discord.ui.Button):
        await self.step(interaction, +1)

    @discord.ui.button(emoji="✖", style=discord.ButtonStyle.danger)
    async def close(self, interaction: discord.Interaction, button: discord.ui.Button):
        await interaction.response.defer()
        await interaction.delete_original_response()
        self.stop()

    async def on_timeout(self):
        for child in self.children:
            if isinstance(child, discord.ui.Button):
                child.disabled = True
        if self.message:
            try:
                await self.message.edit(view=self)
            except discord.HTTPException:
                pass


class Images(commands.Cog):
    """Image search (Google, with Bing/DuckDuckGo fallback)."""

    def __init__(self, bot: JotaBot):
        self.bot = bot

    @commands.hybrid_command(name="img", aliases=["image"], description="Search for images. It will fallback to bing if google images fails.")
    @app_commands.describe(query="What to search for")
    @commands.cooldown(1, 5, commands.BucketType.user)
    async def img(self, ctx: commands.Context, *, query: str):
        """Search for images and browse them with the arrow buttons."""
        await ctx.defer()
        name, urls, start = await find_images(self.bot.http_session, query)
        if not urls:
            await ctx.send("Couldn't find any images, all sources failed.")
            return
        view = ImageView(self.bot.http_session, ctx.author.id, query, name, urls, start)
        view.message = await ctx.send(embed=view.embed(), view=view)


async def setup(bot: JotaBot):
    await bot.add_cog(Images(bot))
