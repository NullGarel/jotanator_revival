// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-20 @ 22:42 UTC
using Discord;

namespace Jotanator;

/// <summary>Auto-generated list of every command. Nothing to maintain by hand.</summary>
public sealed class HelpCog : Cog
{
    public override IEnumerable<CommandDef> BuildCommands()
    {
        yield return new CommandDef
        {
            Name = "help",
            Description = "List commands, or show details for one",
            Help = "Show every command, or details for one.",
            Params = [new ParamDef("command", "Command to look up", Required: false, Rest: true, Autocomplete: AutocompleteCommands)],
            Handler = HelpAsync,
        };
    }

    private static string Describe(CommandDef cmd) =>
        !string.IsNullOrEmpty(cmd.Description) ? cmd.Description
        : !string.IsNullOrEmpty(cmd.Help) ? cmd.Help.Split('\n')[0]
        : "No description.";

    private static async Task<bool> UsableAsync(CommandContext ctx, CommandDef cmd) =>
        !cmd.Hidden && await cmd.CanRunAsync(ctx);

    private async Task<Embed> OverviewAsync(CommandContext ctx)
    {
        var embed = new EmbedBuilder()
            .WithTitle("Commands")
            .WithDescription($"Prefix: `{Config.Prefix}`  •  commands marked **/** also work as slash commands.");

        foreach (var cog in Bot.Cogs)
        {
            var lines = new List<string>();
            foreach (var cmd in cog.Commands.OrderBy(c => c.Name, StringComparer.Ordinal))
            {
                if (!await UsableAsync(ctx, cmd)) continue;
                var mark = cmd.Slash ? " **/**" : "";
                lines.Add($"`{Config.Prefix}{cmd.Name}`{mark}: {Describe(cmd)}");
            }

            if (lines.Count == 0) continue;
            var text = string.Join("\n", lines);
            embed.AddField(cog.Name, text.Length > 1024 ? text[..1024] : text, inline: false);
        }

        embed.WithFooter($"{Config.Prefix}help <command> for details");
        return embed.Build();
    }

    private async Task<Embed?> DetailAsync(CommandContext ctx, string name)
    {
        var key = name.Trim().ToLowerInvariant();
        var prefix = Config.Prefix.ToLowerInvariant();
        if (key.StartsWith(prefix, StringComparison.Ordinal)) key = key[prefix.Length..];

        var cmd = Bot.GetCommand(key);
        if (cmd == null || !await UsableAsync(ctx, cmd)) return null;

        var embed = new EmbedBuilder()
            .WithTitle($"{Config.Prefix}{cmd.Name} {cmd.Signature}".Trim())
            .WithDescription(!string.IsNullOrEmpty(cmd.Help) ? cmd.Help : Describe(cmd));

        if (cmd.Aliases.Length > 0)
            embed.AddField("Aliases", string.Join(", ", cmd.Aliases.Select(a => $"`{a}`")), inline: true);
        if (cmd.Slash)
            embed.AddField("Slash", $"`/{cmd.Name}`", inline: true);

        return embed.Build();
    }

    private async Task HelpAsync(CommandContext ctx)
    {
        var command = ctx.Args.Str("command");
        var embed = !string.IsNullOrEmpty(command)
            ? await DetailAsync(ctx, command)
            : await OverviewAsync(ctx);

        if (embed == null)
        {
            await ctx.SendAsync($"No command called `{command}`.", ephemeral: true);
            return;
        }

        await ctx.SendAsync(embed: embed);
    }

    private static IEnumerable<string> AutocompleteCommands(JotaBot bot, string current) =>
        bot.Commands
            .Where(c => !c.Hidden)
            .Select(c => c.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .Where(n => n.Contains(current, StringComparison.OrdinalIgnoreCase))
            .Take(25);
}
