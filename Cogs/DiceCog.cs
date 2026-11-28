// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using Discord;

namespace Jotanator;

/// <summary>Just dice: a plain random roll with no stakes.</summary>
public sealed class DiceCog : Cog
{
    public override string Category => "Games";

    public override IEnumerable<CommandDef> BuildCommands()
    {
        yield return new CommandDef
        {
            Name = "dice",
            Aliases = ["roll"],
            Description = "Roll a die (d6 by default), or several: 2d6, d20...",
            Help = "Roll dice. Without arguments it's a normal six-sided die.\n" +
                   $"Notation: `NdM` with up to {DiceRoller.MaxDice} dice and {DiceRoller.MaxSides} sides, e.g. `j.dice`, `j.dice d20`, `j.dice 3d6`.",
            Params = [new ParamDef("dice", "Dice to roll, like d6, 2d6 or d20 (default d6)", Required: false)],
            Handler = RollAsync,
        };
    }

    private async Task RollAsync(CommandContext ctx)
    {
        if (!DiceRoller.TryParse(ctx.Args.Str("dice"), out var count, out var sides))
            throw new BadArgumentException(
                $"Use dice notation like `d6`, `2d6` or `d20` (up to {DiceRoller.MaxDice} dice, {DiceRoller.MaxSides} sides).");

        var rolls = DiceRoller.Roll(count, sides);
        var text = count == 1
            ? $"🎲 {ctx.Author.Mention} rolled a **{rolls[0]}** (d{sides})"
            : $"🎲 {ctx.Author.Mention} rolled {count}d{sides}: {string.Join(" + ", rolls)} = **{rolls.Sum()}**";

        await ctx.SendAsync(text, allowedMentions: AllowedMentions.None);
    }
}
