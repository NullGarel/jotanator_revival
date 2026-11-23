// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-20 @ 22:42 UTC
namespace Jotanator;

/// <summary>
/// Template for a new command module. Copy this file, rename the class (keep the "Cog" suffix,
/// e.g. FunCog), delete the [Disabled] attribute and it is loaded automatically on the next start.
///
/// Every CommandDef works as both "j.name" and "/name" and shows up in "j.help" automatically
/// (it uses the Description text). Run "j.sync" after adding or changing commands.
/// </summary>
[Disabled]
public sealed class ExampleCog : Cog
{
    public override IEnumerable<CommandDef> BuildCommands()
    {
        yield return new CommandDef
        {
            Name = "echo",
            Description = "Repeat what you say",
            Help = "Repeat what you say. (Longer text here shows up in `j.help echo`.)",
            Params = [new ParamDef("text", "What to repeat", Rest: true)],
            Handler = async ctx => await ctx.SendAsync(ctx.Args.Str("text")),
        };
    }
}
