// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
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
            Name = "give",
            Help = "Create (or, with a negative amount, remove) J$ for someone: j.give @user 500",
            Hidden = true,
            Slash = false,
            OwnerOnly = true,
            Params =
            [
                new ParamDef("user", "Who to give J$ to", ParamKind.User),
                new ParamDef("amount", "Amount (negative removes)", ParamKind.Int, Min: -1_000_000_000, Max: 1_000_000_000),
            ],
            Handler = GiveAsync,
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

    private async Task GiveAsync(CommandContext ctx)
    {
        var target = ctx.Args.User("user")!;
        var amount = ctx.Args.Int("amount")!.Value;

        var applied = await Bot.Economy.AdjustAsync(target.Id, amount, "admin", $"by {ctx.Author.Id}");
        await ctx.SendAsync($"{(applied >= 0 ? "Gave" : "Took")} {Fmt.J(Math.Abs(applied ?? 0))} {(applied >= 0 ? "to" : "from")} {target.Username}. " +
                            $"Balance: {Fmt.J(await Bot.Economy.GetBalanceAsync(target.Id))}");
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
