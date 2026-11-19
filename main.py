import asyncio
import html
import os
import re
from urllib.parse import quote_plus

import aiohttp
import discord
from discord import app_commands
from ddgs import DDGS  # formerly "duckduckgo_search"
from dotenv import load_dotenv

load_dotenv()

TOKEN = os.environ.get("DISCORD_BOT_TOKEN", "")
SERP_API_KEY = os.environ.get("SERP_API_KEY", "")

TIMEOUT = 8          # seconds per provider
MAX_RESULTS = 30     # images kept per search
VIEW_TIMEOUT = 120   # seconds before buttons disable themselves

HEADERS = {
    "User-Agent": (
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
        "(KHTML, like Gecko) Chrome/124.0 Safari/537.36"
    ),
    "Accept-Language": "en-US,en;q=0.9",
}

IMG_RE = re.compile(r'https?://[^"\'\s\\<>]+?\.(?:jpe?g|png|webp|gif)', re.I)


# ---------- providers ----------

async def google_images(session, query):
    url = f"https://www.google.com/search?q={quote_plus(query)}&tbm=isch&safe=active"
    async with session.get(url, headers=HEADERS) as r:
        r.raise_for_status()
        text = await r.text()
    found = IMG_RE.findall(text.replace("\\u003d", "=").replace("\\u0026", "&"))
    return [u for u in found if "gstatic.com" not in u and "google." not in u]


async def serper_images(session, query):
    """Google Images results via Serper.dev (needs SERP_API_KEY)."""
    async with session.post(
        "https://google.serper.dev/images",
        headers={"X-API-KEY": SERP_API_KEY, "Content-Type": "application/json"},
        json={"q": query, "num": MAX_RESULTS},
    ) as r:
        if r.status != 200:
            body = (await r.text())[:200]
            raise RuntimeError(f"HTTP {r.status}: {body}")
        data = await r.json()
    return [item["imageUrl"] for item in data.get("images", []) if "imageUrl" in item]


async def bing_images(session, query):
    url = f"https://www.bing.com/images/search?q={quote_plus(query)}&adlt=strict"
    async with session.get(url, headers=HEADERS) as r:
        r.raise_for_status()
        text = await r.text()
    return [html.unescape(u) for u in re.findall(r"murl&quot;:&quot;(.*?)&quot;", text)]


async def ddg_images(session, query):
    def _search():
        return [x["image"] for x in DDGS().images(query, safesearch="on", max_results=MAX_RESULTS)]
    return await asyncio.to_thread(_search)


PROVIDERS = {"Google": google_images}
if SERP_API_KEY:
    PROVIDERS["Google (API)"] = serper_images
else:
    print("SERP_API_KEY not set: API fallback disabled.")
PROVIDERS["Bing"] = bing_images
PROVIDERS["DuckDuckGo"] = ddg_images
CHOICES = ["Google", "Bing", "DuckDuckGo"]


async def is_valid_image(session, url):
    try:
        async with session.get(url, headers=HEADERS,
                               timeout=aiohttp.ClientTimeout(total=5)) as r:
            return r.status == 200 and r.headers.get("Content-Type", "").startswith("image/")
    except Exception:
        return False


async def find_images(session, query, preferred="Google"):
    """Try the preferred provider first, then the rest in chain order.
    Returns (source, urls, first_valid_index)."""
    order = [preferred] + [p for p in PROVIDERS if p != preferred]
    for name in order:
        try:
            urls = await asyncio.wait_for(PROVIDERS[name](session, query), TIMEOUT)
        except Exception as e:
            print(f"[{name}] failed: {type(e).__name__}: {e}")
            continue
        urls = list(dict.fromkeys(urls))[:MAX_RESULTS]  # dedupe, keep order
        if not urls:
            print(f"[{name}] no results")
            continue
        # make sure at least one of the first few actually loads, else fall through
        for i, url in enumerate(urls[:5]):
            if await is_valid_image(session, url):
                return name, urls, i
        print(f"[{name}] no loadable images")
    return None, [], 0


# ---------- UI ----------

class ImageView(discord.ui.View):
    def __init__(self, session, user_id, query, source, urls, start):
        super().__init__(timeout=VIEW_TIMEOUT)
        self.session = session
        self.user_id = user_id
        self.query = query
        self.source = source
        self.urls = urls
        self.index = start
        self.valid = {start: True}   # cache of index -> loadable?
        self.message: discord.WebhookMessage | None = None

    def embed(self):
        e = discord.Embed(title=self.query[:200])
        e.set_image(url=self.urls[self.index])
        e.set_footer(text=f"{self.source} • {self.index + 1}/{len(self.urls)}")
        return e

    async def interaction_check(self, interaction):
        if interaction.user.id != self.user_id:
            await interaction.response.send_message(
                "Run your own `/img` to browse.", ephemeral=True)
            return False
        return True

    async def step(self, interaction, direction):
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
    async def prev(self, interaction, button):
        await self.step(interaction, -1)

    @discord.ui.button(emoji="▶", style=discord.ButtonStyle.secondary)
    async def next(self, interaction, button):
        await self.step(interaction, +1)

    @discord.ui.button(emoji="✖", style=discord.ButtonStyle.danger)
    async def close(self, interaction, button):
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


class Bot(discord.Client):
    def __init__(self):
        super().__init__(intents=discord.Intents.default())
        self.tree = app_commands.CommandTree(self)
        self.session: aiohttp.ClientSession | None = None

    async def setup_hook(self):
        self.session = aiohttp.ClientSession()
        await self.tree.sync()

    async def close(self):
        if self.session:
            await self.session.close()
        await super().close()


bot = Bot()


@bot.tree.command(name="img", description="Search for images")
@app_commands.describe(query="What to search for", source="Where to search (default: Google)")
@app_commands.choices(source=[app_commands.Choice(name=n, value=n) for n in CHOICES])
@app_commands.checks.cooldown(1, 5)
async def img(interaction: discord.Interaction, query: str,
              source: app_commands.Choice[str] | None = None):
    await interaction.response.defer()
    preferred = source.value if source else "Google"
    name, urls, start = await find_images(bot.session, query, preferred)
    if not urls:
        await interaction.followup.send("Couldn't find any images, all sources failed.")
        return
    view = ImageView(bot.session, interaction.user.id, query, name, urls, start)
    view.message = await interaction.followup.send(embed=view.embed(), view=view, wait=True)


@img.error
async def img_error(interaction: discord.Interaction, error: app_commands.AppCommandError):
    if isinstance(error, app_commands.CommandOnCooldown):
        await interaction.response.send_message(
            f"Slow down, try again in {error.retry_after:.1f}s.", ephemeral=True)
    else:
        raise error


bot.run(TOKEN)