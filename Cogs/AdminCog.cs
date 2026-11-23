// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-20 @ 22:42 UTC
namespace Jotanator;

/// <summary>Owner-only tools (hidden from help).</summary>
public sealed class AdminCog : Cog
{
    public override IEnumerable<CommandDef> BuildCommands()
    {
        yield return new CommandDef
        {
            Name = "reload",
            Help = "Reload a cog without restarting: j.reload images",
            Hidden = true,
            Slash = false,
            OwnerOnly = true,
            Params = [new ParamDef("name", "Cog to reload")],
            Handler = ReloadAsync,
        };

        yield return new CommandDef
        {
            Name = "sync",
            Help = "Push slash commands to Discord (run after adding/changing commands).",
            Hidden = true,
            Slash = false,
            OwnerOnly = true,
            Handler = SyncAsync,
        };
    }

    private async Task ReloadAsync(CommandContext ctx)
    {
        var name = ctx.Args.Str("name")!;
        await Bot.ReloadCogAsync(name);
        await ctx.SendAsync($"Reloaded `{name}`.");
    }

    private async Task SyncAsync(CommandContext ctx)
    {
        var count = await Bot.SyncAsync();
        await ctx.SendAsync($"Synced {count} slash commands.");
    }
}
