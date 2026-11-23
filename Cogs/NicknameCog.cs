// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-20 @ 22:42 UTC
using System.Globalization;
using System.Text;
using Discord;
using Discord.Net;

namespace Jotanator;

/// <summary>Rename the bot, or (with permission) rename other members.</summary>
public sealed class NicknameCog : Cog
{
    private const int MaxNick = 32;   // Discord's nickname length limit
    private static readonly AllowedMentions NoPings = AllowedMentions.None;

    public override IEnumerable<CommandDef> BuildCommands()
    {
        // anyone: rename the bot
        yield return new CommandDef
        {
            Name = "rename",
            Aliases = ["nick"],
            Description = "Change the bot's nickname in this server",
            Help = "Change the bot's nickname here. Run with no name to reset it. Anyone can use this.",
            GuildOnly = true,
            BotPermissions = [GuildPermission.ChangeNickname],
            Cooldowns = [new CooldownSpec(1, 15, BucketType.User), new CooldownSpec(2, 30, BucketType.Guild)],
            Params = [new ParamDef("name", "New nickname (leave empty to reset)", Required: false, Rest: true)],
            Handler = RenameAsync,
        };

        // permission required: rename other members
        yield return new CommandDef
        {
            Name = "setnick",
            Aliases = ["nickuser"],
            Description = "Change another member's nickname",
            Help = "Change a member's nickname through the bot. Needs Manage Nicknames.",
            GuildOnly = true,
            UserPermissions = [GuildPermission.ManageNicknames],
            BotPermissions = [GuildPermission.ManageNicknames],
            Cooldowns = [new CooldownSpec(3, 30, BucketType.Guild)],
            Params =
            [
                new ParamDef("member", "Who to rename", ParamKind.Member),
                new ParamDef("name", "New nickname (leave empty to reset)", Required: false, Rest: true),
            ],
            Handler = SetNickAsync,
        };
    }

    private static string? Clean(string? name)
    {
        name = name?.Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    private static int Length(string s) => s.EnumerateRunes().Count();

    /// <summary>discord.py role ordering: higher position wins, ties go to the older (lower id) role.</summary>
    private static bool RoleAtLeast(IRole a, IRole b) =>
        !(a.Position < b.Position || (a.Position == b.Position && a.Id > b.Id));

    private static IRole TopRole(Discord.WebSocket.SocketGuild guild, IGuildUser user)
    {
        IRole? top = null;
        foreach (var id in user.RoleIds)
        {
            var role = guild.GetRole(id);
            if (role != null && (top == null || !RoleAtLeast(top, role))) top = role;
        }
        return top ?? guild.EveryoneRole;
    }

    /// <summary>Sets the target's nickname, replying with the error if it fails. True on success.</summary>
    private static async Task<bool> ApplyAsync(CommandContext ctx, IGuildUser target, string? name)
    {
        try
        {
            await target.ModifyAsync(x => x.Nickname = name ?? "", new RequestOptions
            {
                AuditLogReason = $"Renamed by {ctx.Author.Username} ({ctx.Author.Id})",
            });
        }
        catch (HttpException e) when (e.HttpCode == System.Net.HttpStatusCode.Forbidden)
        {
            await ctx.SendAsync("I'm not allowed to change that nickname.", ephemeral: true);
            return false;
        }
        catch (HttpException e)
        {
            await ctx.SendAsync($"Discord rejected that nickname: {e.Reason}", ephemeral: true);
            return false;
        }

        return true;
    }

    private async Task RenameAsync(CommandContext ctx)
    {
        var guild = ctx.Guild!;
        var name = Clean(ctx.Args.Str("name"));

        if (name != null && Length(name) > MaxNick)
        {
            await ctx.SendAsync($"Nicknames can be at most {MaxNick} characters (that's {Length(name)}).", ephemeral: true);
            return;
        }

        if (await ApplyAsync(ctx, guild.CurrentUser, name))
        {
            await ctx.SendAsync(name != null ? $"Nickname set to **{name}**." : "Nickname reset.",
                allowedMentions: NoPings);
        }
    }

    private async Task SetNickAsync(CommandContext ctx)
    {
        var guild = ctx.Guild!;
        var author = (IGuildUser)ctx.Author;
        var member = ctx.Args.Member("member")!;
        var name = Clean(ctx.Args.Str("name"));

        if (name != null && Length(name) > MaxNick)
        {
            await ctx.SendAsync($"Nicknames can be at most {MaxNick} characters (that's {Length(name)}).", ephemeral: true);
            return;
        }
        if (member.Id == guild.CurrentUser.Id)
        {
            await ctx.SendAsync($"To rename me, use `{ctx.CleanPrefix}rename`.", ephemeral: true);
            return;
        }
        if (member.Id == guild.OwnerId)
        {
            await ctx.SendAsync("Nobody can change the server owner's nickname through a bot.", ephemeral: true);
            return;
        }
        if (RoleAtLeast(TopRole(guild, member), TopRole(guild, guild.CurrentUser)))
        {
            await ctx.SendAsync("That member's top role is equal to or higher than mine, so I can't rename them.",
                ephemeral: true);
            return;
        }
        if (author.Id != guild.OwnerId && member.Id != author.Id &&
            RoleAtLeast(TopRole(guild, member), TopRole(guild, author)))
        {
            await ctx.SendAsync("You can't rename someone whose top role is equal to or higher than yours.",
                ephemeral: true);
            return;
        }

        if (await ApplyAsync(ctx, member, name))
        {
            await ctx.SendAsync(name != null
                    ? $"{member.Mention}'s nickname is now **{name}**."
                    : $"Reset {member.Mention}'s nickname.",
                allowedMentions: NoPings);
        }
    }
}
