// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-20 @ 22:42 UTC
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;

namespace Jotanator;

public enum ParamKind { String, Member }

/// <summary>One command parameter. Rest = consumes the remainder of the line (discord.py's "*, name").</summary>
public sealed record ParamDef(
    string Name,
    string Description,
    ParamKind Kind = ParamKind.String,
    bool Required = true,
    bool Rest = false,
    Func<JotaBot, string, IEnumerable<string>>? Autocomplete = null);

/// <summary>Declarative definition of a command: metadata, checks, cooldowns, parameters and handler.</summary>
public sealed class CommandDef
{
    public required string Name { get; init; }
    public string[] Aliases { get; init; } = [];
    public string Description { get; init; } = "";
    public string Help { get; init; } = "";
    public bool Hidden { get; init; }
    public bool Slash { get; init; } = true;

    public bool GuildOnly { get; init; }
    public bool OwnerOnly { get; init; }
    public GuildPermission[] UserPermissions { get; init; } = [];
    public GuildPermission[] BotPermissions { get; init; } = [];
    public CooldownSpec[] Cooldowns { get; init; } = [];
    public ParamDef[] Params { get; init; } = [];

    public required Func<CommandContext, Task> Handler { get; init; }

    public Cog? Cog { get; internal set; }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CooldownBucket> _buckets = new();

    /// <summary>Usage string like discord.py's: "&lt;query&gt;..." / "[name]...".</summary>
    public string Signature => string.Join(" ", Params.Select(p =>
        (p.Required ? $"<{p.Name}>" : $"[{p.Name}]") + (p.Rest ? "..." : "")));

    // ---------------------------------------------------------------- checks

    public async Task CheckAsync(CommandContext ctx)
    {
        if (GuildOnly && ctx.Guild is null)
            throw new NoPrivateMessageException();

        if (OwnerOnly && !await ctx.Bot.IsOwnerAsync(ctx.Author))
            throw new NotOwnerException();

        if (UserPermissions.Length > 0)
        {
            var member = ctx.Author as IGuildUser;
            var missing = UserPermissions
                .Where(p => member is null || !member.GuildPermissions.Has(p)).ToList();
            if (missing.Count > 0) throw new MissingPermissionsException(missing);
        }

        if (BotPermissions.Length > 0)
        {
            var me = ctx.Guild?.CurrentUser;
            var missing = BotPermissions
                .Where(p => me is null || !me.GuildPermissions.Has(p)).ToList();
            if (missing.Count > 0) throw new BotMissingPermissionsException(missing);
        }
    }

    /// <summary>Like discord.py's can_run: true if every check passes (cooldowns are not considered).</summary>
    public async Task<bool> CanRunAsync(CommandContext ctx)
    {
        try
        {
            await CheckAsync(ctx);
            return true;
        }
        catch (CheckFailureException)
        {
            return false;
        }
    }

    public void ConsumeCooldowns(CommandContext ctx)
    {
        for (var i = 0; i < Cooldowns.Length; i++)
        {
            var spec = Cooldowns[i];
            var id = spec.Bucket == BucketType.Guild ? (ctx.Guild?.Id ?? ctx.Author.Id) : ctx.Author.Id;
            var bucket = _buckets.GetOrAdd($"{i}:{id}", _ => new CooldownBucket(spec.Rate, spec.PerSeconds));
            var retry = bucket.Update(CooldownBucket.Now());
            if (retry != null) throw new CooldownException(retry.Value);
        }
    }

    // ------------------------------------------------------- argument parsing

    public async Task<Args> ParsePrefixAsync(CommandContext ctx, string input)
    {
        var args = new Args();
        var view = new StringView(input);

        foreach (var p in Params)
        {
            var raw = p.Rest ? view.ReadRest() : view.ReadWord();
            if (string.IsNullOrEmpty(raw))
            {
                if (p.Required) throw new MissingArgumentException(p.Name);
                args.Set(p.Name, null);
                continue;
            }

            if (p.Kind == ParamKind.Member)
                args.Set(p.Name, await ConvertMemberAsync(ctx, raw));
            else
                args.Set(p.Name, raw);
        }

        return args;
    }

    public Args FromInteraction(CommandContext ctx)
    {
        var args = new Args();
        var options = ctx.Interaction!.Data.Options;

        foreach (var p in Params)
        {
            var option = options.FirstOrDefault(o => o.Name == p.Name);
            if (option == null)
            {
                if (p.Required) throw new MissingArgumentException(p.Name);
                args.Set(p.Name, null);
                continue;
            }

            if (p.Kind == ParamKind.Member)
            {
                if (option.Value is not IGuildUser member)
                    throw new BadArgumentException(
                        $"Member \"{(option.Value as IUser)?.Username ?? p.Name}\" not found.");
                args.Set(p.Name, member);
            }
            else
            {
                args.Set(p.Name, option.Value?.ToString());
            }
        }

        return args;
    }

    /// <summary>Resolves a mention, an ID, or a (nick)name to a guild member.</summary>
    private static async Task<IGuildUser> ConvertMemberAsync(CommandContext ctx, string word)
    {
        var guild = ctx.Guild;
        if (guild != null)
        {
            ulong id = 0;
            var mention = Regex.Match(word, @"^<@!?(\d+)>$");
            if (mention.Success) ulong.TryParse(mention.Groups[1].Value, out id);
            else if (Regex.IsMatch(word, @"^\d{15,21}$")) ulong.TryParse(word, out id);

            if (id != 0)
            {
                IGuildUser? user = guild.GetUser(id);
                if (user == null)
                {
                    try { user = await ctx.Bot.Client.Rest.GetGuildUserAsync(guild.Id, id); }
                    catch { /* not found */ }
                }
                if (user != null) return user;
            }
            else
            {
                var name = word.TrimStart('@');
                var cached = guild.Users.FirstOrDefault(u =>
                    string.Equals(u.Username, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(u.Nickname, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(u.GlobalName, name, StringComparison.OrdinalIgnoreCase));
                if (cached != null) return cached;

                try
                {
                    var found = await ((IGuild)guild).SearchUsersAsync(name, 1);
                    var first = found.FirstOrDefault();
                    if (first != null) return first;
                }
                catch { /* fall through */ }
            }
        }

        throw new BadArgumentException($"Member \"{word}\" not found.");
    }

    // ------------------------------------------------------- slash registration

    public SlashCommandBuilder BuildSlash()
    {
        var builder = new SlashCommandBuilder()
            .WithName(Name)
            .WithDescription(string.IsNullOrWhiteSpace(Description) ? "No description." : Description);

        foreach (var p in Params)
        {
            builder.AddOption(new SlashCommandOptionBuilder()
                .WithName(p.Name)
                .WithDescription(string.IsNullOrWhiteSpace(p.Description) ? "…" : p.Description)
                .WithType(p.Kind == ParamKind.Member ? ApplicationCommandOptionType.User : ApplicationCommandOptionType.String)
                .WithRequired(p.Required)
                .WithAutocomplete(p.Autocomplete != null));
        }

        if (GuildOnly)
            builder.WithContextTypes(InteractionContextType.Guild);
        if (UserPermissions.Length > 0)
            builder.WithDefaultMemberPermissions(UserPermissions[0]);

        return builder;
    }
}
