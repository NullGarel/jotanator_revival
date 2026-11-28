// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using System.Globalization;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;

namespace Jotanator;

public enum ParamKind
{
    String,
    /// <summary>A member of the current server.</summary>
    Member,
    /// <summary>Any Discord user (mention, ID, or name when ByName is set).</summary>
    User,
    /// <summary>A whole number, optionally limited by Min / Max.</summary>
    Int,
    /// <summary>A file attached to the message (slash: an attachment option).</summary>
    Attachment,
}

/// <summary>
/// One command parameter. Rest = consumes the remainder of the line (discord.py's "*, name").
/// ByName = a User parameter also accepts plain names (turn it off where a word like "hard" must not be mistaken for a name).
/// </summary>
public sealed record ParamDef(
    string Name,
    string Description,
    ParamKind Kind = ParamKind.String,
    bool Required = true,
    bool Rest = false,
    Func<JotaBot, string, IEnumerable<string>>? Autocomplete = null,
    long? Min = null,
    long? Max = null,
    bool ByName = true);

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
    public string Signature => string.Join(" ", Params
        .Where(p => p.Kind != ParamKind.Attachment)
        .Select(p => (p.Required ? $"<{p.Name}>" : $"[{p.Name}]") + (p.Rest ? "..." : "")));

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
        var skipped = new List<ParamDef>();

        foreach (var p in Params)
        {
            if (p.Kind == ParamKind.Attachment)
            {
                // attachments aren't typed in; they come with the message
                args.Set(p.Name, ctx.Message?.Attachments.FirstOrDefault());
                continue;
            }

            var mark = view.Position;
            var raw = p.Rest ? view.ReadRest() : view.ReadWord();
            if (string.IsNullOrEmpty(raw))
            {
                if (p.Required) throw new MissingArgumentException(p.Name);
                args.Set(p.Name, null);
                continue;
            }

            try
            {
                args.Set(p.Name, await ConvertAsync(ctx, p, raw));
            }
            catch (NoMatchException) when (!p.Required && !p.Rest && p.Kind != ParamKind.String)
            {
                // an optional typed parameter that doesn't match: leave the word for the next parameter
                view.Position = mark;
                args.Set(p.Name, null);
                skipped.Add(p);
            }
        }

        // words nobody took yet may still belong to a skipped optional parameter
        // (lets "j.blackjack @friend 50" work as well as "j.blackjack 50 @friend")
        if (skipped.Count > 0)
        {
            string? word;
            while (skipped.Count > 0 && (word = view.ReadWord()) != null)
            {
                foreach (var p in skipped.ToList())
                {
                    try
                    {
                        args.Set(p.Name, await ConvertAsync(ctx, p, word));
                        skipped.Remove(p);
                        break;
                    }
                    catch (NoMatchException)
                    {
                        // try the next skipped parameter
                    }
                }
            }
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

            switch (p.Kind)
            {
                case ParamKind.Member:
                    if (option.Value is not IGuildUser member)
                        throw new BadArgumentException(
                            $"Member \"{(option.Value as IUser)?.Username ?? p.Name}\" not found.");
                    args.Set(p.Name, member);
                    break;

                case ParamKind.User:
                    args.Set(p.Name, option.Value as IUser
                        ?? throw new BadArgumentException($"User for `{p.Name}` not found."));
                    break;

                case ParamKind.Int:
                    args.Set(p.Name, Convert.ToInt64(option.Value, CultureInfo.InvariantCulture));
                    break;

                case ParamKind.Attachment:
                    args.Set(p.Name, option.Value as IAttachment);
                    break;

                default:
                    args.Set(p.Name, option.Value?.ToString());
                    break;
            }
        }

        return args;
    }

    private static async Task<object?> ConvertAsync(CommandContext ctx, ParamDef p, string raw)
    {
        switch (p.Kind)
        {
            case ParamKind.Member:
                return await FindMemberAsync(ctx, raw, byName: true)
                       ?? throw new NoMatchException($"Member \"{raw}\" not found.");
            case ParamKind.User:
                return await FindUserAsync(ctx, raw, p.ByName)
                       ?? throw new NoMatchException($"User \"{raw}\" not found.");
            case ParamKind.Int:
                return ParseInt(p, raw);
            default:
                return raw;
        }
    }

    private static long ParseInt(ParamDef p, string raw)
    {
        var text = raw.Replace(",", "").Replace("_", "");
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            throw new NoMatchException($"`{p.Name}` must be a whole number.");

        if ((p.Min is long lo && n < lo) || (p.Max is long hi && n > hi))
        {
            var range = p.Min is long a && p.Max is long b ? $"between {a:N0} and {b:N0}"
                : p.Min is long min ? $"at least {min:N0}"
                : $"at most {p.Max:N0}";
            throw new BadArgumentException($"`{p.Name}` must be {range}.");
        }

        return n;
    }

    private static bool TryParseId(string word, out ulong id)
    {
        id = 0;
        var mention = Regex.Match(word, @"^<@!?(\d+)>$");
        if (mention.Success) return ulong.TryParse(mention.Groups[1].Value, out id);
        return Regex.IsMatch(word, @"^\d{15,21}$") && ulong.TryParse(word, out id);
    }

    /// <summary>Resolves a mention, an ID, or (with byName) a (nick)name to a guild member.</summary>
    private static async Task<IGuildUser?> FindMemberAsync(CommandContext ctx, string word, bool byName)
    {
        var guild = ctx.Guild;
        if (guild == null) return null;

        if (TryParseId(word, out var id))
        {
            IGuildUser? user = guild.GetUser(id);
            if (user == null)
            {
                try { user = await ctx.Bot.Client.Rest.GetGuildUserAsync(guild.Id, id); }
                catch { /* not found */ }
            }
            return user;
        }

        if (!byName) return null;

        var name = word.TrimStart('@');
        var cached = guild.Users.FirstOrDefault(u =>
            string.Equals(u.Username, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(u.Nickname, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(u.GlobalName, name, StringComparison.OrdinalIgnoreCase));
        if (cached != null) return cached;

        try
        {
            var found = await ((IGuild)guild).SearchUsersAsync(name, 1);
            return found.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Like FindMemberAsync, but a user who isn't in the server (or a DM) is fine too.</summary>
    private static async Task<IUser?> FindUserAsync(CommandContext ctx, string word, bool byName)
    {
        var member = await FindMemberAsync(ctx, word, byName);
        if (member != null) return member;

        if (TryParseId(word, out var id))
        {
            IUser? user = ctx.Bot.Client.GetUser(id);
            if (user != null) return user;
            try { return await ctx.Bot.Client.Rest.GetUserAsync(id); }
            catch { return null; }
        }

        return null;
    }

    // ------------------------------------------------------- slash registration

    public SlashCommandBuilder BuildSlash()
    {
        var builder = new SlashCommandBuilder()
            .WithName(Name)
            .WithDescription(string.IsNullOrWhiteSpace(Description) ? "No description." : Description);

        // slash options: required ones have to come first
        foreach (var p in Params.OrderByDescending(p => p.Required))
        {
            var option = new SlashCommandOptionBuilder()
                .WithName(p.Name)
                .WithDescription(string.IsNullOrWhiteSpace(p.Description) ? "…" : p.Description)
                .WithRequired(p.Required);

            switch (p.Kind)
            {
                case ParamKind.Member:
                case ParamKind.User:
                    option.WithType(ApplicationCommandOptionType.User);
                    break;
                case ParamKind.Int:
                    option.WithType(ApplicationCommandOptionType.Integer);
                    if (p.Min is long lo) option.WithMinValue(lo);
                    if (p.Max is long hi) option.WithMaxValue(hi);
                    break;
                case ParamKind.Attachment:
                    option.WithType(ApplicationCommandOptionType.Attachment);
                    break;
                default:
                    option.WithType(ApplicationCommandOptionType.String);
                    option.WithAutocomplete(p.Autocomplete != null);
                    break;
            }

            builder.AddOption(option);
        }

        if (GuildOnly)
            builder.WithContextTypes(InteractionContextType.Guild);
        if (UserPermissions.Length > 0)
            builder.WithDefaultMemberPermissions(UserPermissions[0]);

        return builder;
    }
}
