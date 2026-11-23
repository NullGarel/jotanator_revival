// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-20 @ 22:42 UTC
namespace Jotanator;

/// <summary>Basic utility commands.</summary>
public sealed class GeneralCog : Cog
{
    public override IEnumerable<CommandDef> BuildCommands()
    {
        yield return new CommandDef
        {
            Name = "ping",
            Description = "Check the bot's latency",
            Help = "Check the bot's latency.",
            Handler = PingAsync,
        };
    }

    private async Task PingAsync(CommandContext ctx)
    {
        await ctx.SendAsync($"Pong! {Bot.Client.Latency}ms");
    }
}
