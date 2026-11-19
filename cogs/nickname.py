# VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-19 @ 12:01 UTC

from typing import Optional

import discord
from discord import app_commands
from discord.ext import commands

from core.bot import JotaBot

MAX_NICK = 32  # Discord's nickname length limit
NO_PINGS = discord.AllowedMentions.none()


class Nickname(commands.Cog):
    """Rename the bot, or (with permission) rename other members."""

    def __init__(self, bot: JotaBot):
        self.bot = bot

    @staticmethod
    def _clean(name: Optional[str]) -> Optional[str]:
        name = name.strip() if name else None
        return name or None

    async def _apply(self, ctx: commands.Context, target: discord.Member,
                     name: Optional[str]) -> bool:
        """Set target's nickname, replying with the error if it fails. True on success."""
        try:
            await target.edit(nick=name,
                              reason=f"Renamed by {ctx.author} ({ctx.author.id})")
        except discord.Forbidden:
            await ctx.send("I'm not allowed to change that nickname.", ephemeral=True)
            return False
        except discord.HTTPException as e:
            await ctx.send(f"Discord rejected that nickname: {e.text}", ephemeral=True)
            return False
        return True

    # ---- anyone: rename the bot ----

    @commands.hybrid_command(name="rename", aliases=["nick"],
                             description="Change the bot's nickname in this server")
    @app_commands.describe(name="New nickname (leave empty to reset)")
    @commands.guild_only()
    @commands.bot_has_permissions(change_nickname=True)
    @commands.cooldown(1, 15, commands.BucketType.user)
    @commands.cooldown(2, 30, commands.BucketType.guild)
    async def rename(self, ctx: commands.Context, *, name: Optional[str] = None):
        """Change the bot's nickname here. Run with no name to reset it. Anyone can use this."""
        assert ctx.guild is not None
        name = self._clean(name)
        if name and len(name) > MAX_NICK:
            await ctx.send(f"Nicknames can be at most {MAX_NICK} characters "
                           f"(that's {len(name)}).", ephemeral=True)
            return
        if await self._apply(ctx, ctx.guild.me, name):
            await ctx.send(f"Nickname set to **{name}**." if name else "Nickname reset.",
                           allowed_mentions=NO_PINGS)

    # ---- permission required: rename other members ----

    @commands.hybrid_command(name="setnick", aliases=["nickuser"],
                             description="Change another member's nickname")
    @app_commands.describe(member="Who to rename",
                           name="New nickname (leave empty to reset)")
    @commands.guild_only()
    @commands.has_permissions(manage_nicknames=True)
    @commands.bot_has_permissions(manage_nicknames=True)
    @commands.cooldown(3, 30, commands.BucketType.guild)
    async def setnick(self, ctx: commands.Context, member: discord.Member,
                      *, name: Optional[str] = None):
        """Change a member's nickname through the bot. Needs Manage Nicknames."""
        guild = ctx.guild
        author = ctx.author
        assert guild is not None and isinstance(author, discord.Member)

        name = self._clean(name)
        if name and len(name) > MAX_NICK:
            await ctx.send(f"Nicknames can be at most {MAX_NICK} characters "
                           f"(that's {len(name)}).", ephemeral=True)
            return
        if member == guild.me:
            await ctx.send(f"To rename me, use `{ctx.clean_prefix}rename`.", ephemeral=True)
            return
        if member == guild.owner:
            await ctx.send("Nobody can change the server owner's nickname through a bot.",
                           ephemeral=True)
            return
        if member.top_role >= guild.me.top_role:
            await ctx.send("That member's top role is equal to or higher than mine, "
                           "so I can't rename them.", ephemeral=True)
            return
        if (author != guild.owner and member != author
                and member.top_role >= author.top_role):
            await ctx.send("You can't rename someone whose top role is equal to or "
                           "higher than yours.", ephemeral=True)
            return

        if await self._apply(ctx, member, name):
            who = member.display_name if not name else name
            await ctx.send(f"{member.mention}'s nickname is now **{who}**." if name
                           else f"Reset {member.mention}'s nickname.",
                           allowed_mentions=NO_PINGS)


async def setup(bot: JotaBot):
    await bot.add_cog(Nickname(bot))