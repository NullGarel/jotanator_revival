# VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 20261119
from typing import Optional

import discord
from discord import app_commands
from discord.ext import commands

import config
from core.bot import JotaBot


def describe(cmd: commands.Command) -> str:
    return cmd.description or cmd.short_doc or "No description."


def has_slash(cmd: commands.Command) -> bool:
    return getattr(cmd, "app_command", None) is not None


class Help(commands.Cog):
    """Auto-generated list of every command. Nothing to maintain by hand."""

    def __init__(self, bot: JotaBot):
        self.bot = bot

    async def _usable(self, ctx: commands.Context, cmd: commands.Command) -> bool:
        if cmd.hidden or not cmd.enabled:
            return False
        try:
            return await cmd.can_run(ctx)
        except commands.CommandError:
            return False

    async def _overview(self, ctx: commands.Context) -> discord.Embed:
        embed = discord.Embed(
            title="Commands",
            description=(f"Prefix: `{config.PREFIX}`  •  commands marked **/** "
                         "also work as slash commands."),
        )
        for name, cog in sorted(self.bot.cogs.items()):
            lines = []
            for cmd in sorted(cog.get_commands(), key=lambda c: c.name):
                if await self._usable(ctx, cmd):
                    mark = " **/**" if has_slash(cmd) else ""
                    lines.append(f"`{config.PREFIX}{cmd.name}`{mark}: {describe(cmd)}")
            if lines:
                text = "\n".join(lines)
                embed.add_field(name=name, value=text[:1024], inline=False)
        embed.set_footer(text=f"{config.PREFIX}help <command> for details")
        return embed

    async def _detail(self, ctx: commands.Context, name: str) -> Optional[discord.Embed]:
        cmd = self.bot.get_command(name.strip().lower().removeprefix(config.PREFIX.lower()))
        if cmd is None or not await self._usable(ctx, cmd):
            return None
        embed = discord.Embed(
            title=f"{config.PREFIX}{cmd.qualified_name} {cmd.signature}".strip(),
            description=cmd.help or describe(cmd),
        )
        if cmd.aliases:
            embed.add_field(name="Aliases", value=", ".join(f"`{a}`" for a in cmd.aliases))
        if has_slash(cmd):
            embed.add_field(name="Slash", value=f"`/{cmd.qualified_name}`")
        if isinstance(cmd, commands.Group):
            subs = [f"`{c.name}`" for c in cmd.commands if not c.hidden]
            if subs:
                embed.add_field(name="Subcommands", value=", ".join(subs), inline=False)
        return embed

    @commands.hybrid_command(name="help", description="List commands, or show details for one")
    @app_commands.describe(command="Command to look up")
    async def help_cmd(self, ctx: commands.Context, *, command: Optional[str] = None):
        """Show every command, or details for one."""
        embed = await self._detail(ctx, command) if command else await self._overview(ctx)
        if embed is None:
            await ctx.send(f"No command called `{command}`.", ephemeral=True)
            return
        await ctx.send(embed=embed)

    @help_cmd.autocomplete("command")
    async def help_autocomplete(self, interaction: discord.Interaction, current: str):
        names = sorted(c.qualified_name for c in self.bot.walk_commands() if not c.hidden)
        return [app_commands.Choice(name=n, value=n)
                for n in names if current.lower() in n.lower()][:25]


async def setup(bot: JotaBot):
    await bot.add_cog(Help(bot))
