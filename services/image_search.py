"""Image search providers + the fallback chain. No Discord code in here."""
import asyncio
import html
import re
from urllib.parse import quote_plus

import aiohttp
from ddgs import DDGS  # formerly "duckduckgo_search"

import config

HEADERS = {
    "User-Agent": (
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
        "(KHTML, like Gecko) Chrome/124.0 Safari/537.36"
    ),
    "Accept-Language": "en-US,en;q=0.9",
}

IMG_RE = re.compile(r'https?://[^"\'\s\\<>]+?\.(?:jpe?g|png|webp|gif)', re.I)


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
        headers={"X-API-KEY": config.SERP_API_KEY, "Content-Type": "application/json"},
        json={"q": query, "num": config.MAX_RESULTS},
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
        return [x["image"] for x in
                DDGS().images(query, safesearch="on", max_results=config.MAX_RESULTS)]
    return await asyncio.to_thread(_search)


# Order matters: this is the fallback chain.
PROVIDERS = {"Google": google_images}
if config.SERP_API_KEY:
    PROVIDERS["Google (API)"] = serper_images
else:
    print("SERP_API_KEY not set: API fallback disabled.")
PROVIDERS["Bing"] = bing_images
PROVIDERS["DuckDuckGo"] = ddg_images


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
            urls = await asyncio.wait_for(PROVIDERS[name](session, query), config.TIMEOUT)
        except Exception as e:
            print(f"[{name}] failed: {type(e).__name__}: {e}")
            continue
        urls = list(dict.fromkeys(urls))[:config.MAX_RESULTS]
        if not urls:
            print(f"[{name}] no results")
            continue
        for i, url in enumerate(urls[:5]):
            if await is_valid_image(session, url):
                return name, urls, i
        print(f"[{name}] no loadable images")
    return None, [], 0
