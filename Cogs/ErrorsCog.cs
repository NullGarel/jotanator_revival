// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-20 @ 22:42 UTC
namespace Jotanator;

/// <summary>Central error handling for prefix and slash commands.</summary>
public sealed class ErrorsCog : Cog
{
    public override IEnumerable<CommandDef> BuildCommands() => [];

    public override Task OnLoadAsync()
    {
        Bot.CommandError += OnCommandErrorAsync;
        return Task.CompletedTask;
    }

    public override Task OnUnloadAsync()
    {
        Bot.CommandError -= OnCommandErrorAsync;
        return Task.CompletedTask;
    }

    private async Task OnCommandErrorAsync(CommandContext ctx, Exception error)
    {
        string msg;
        switch (error)
        {
            case CooldownException cd:
                msg = $"Slow down, try again in {cd.RetryAfter:F1}s.";
                break;
            case MissingArgumentException missing:
                var usage = $"{Config.Prefix}{ctx.Command.Name} {ctx.Command.Signature}".Trim();
                msg = $"Missing `{missing.ParamName}`. Usage: `{usage}`";
                break;
            case NotOwnerException:
                msg = "Owner only.";
                break;
            case BadArgumentException or CheckFailureException:
                msg = string.IsNullOrEmpty(error.Message) ? "You can't use that here." : error.Message;
                break;
            default:
                Logger.Error($"Unhandled error in {ctx.Command.Name}", error);
                msg = "Something went wrong.";
                break;
        }

        try
        {
            await ctx.SendAsync(msg, ephemeral: true);
        }
        catch (Exception e)
        {
            Logger.Error("Could not send the error message", e);
        }
    }
}
