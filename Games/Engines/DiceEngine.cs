// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using System.Text.RegularExpressions;

namespace Jotanator;

/// <summary>Dice notation like "d6", "2d6", "3d20". No Discord code in here.</summary>
public static class DiceRoller
{
    public const int MaxDice = 20;
    public const int MaxSides = 1000;

    private static readonly Regex Notation = new(@"^(\d{1,2})?d(\d{1,4})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParse(string? text, out int count, out int sides)
    {
        count = 1;
        sides = 6;
        if (string.IsNullOrWhiteSpace(text)) return true;

        var m = Notation.Match(text.Trim());
        if (!m.Success) return false;

        count = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 1;
        sides = int.Parse(m.Groups[2].Value);
        return count is >= 1 and <= MaxDice && sides is >= 2 and <= MaxSides;
    }

    public static int[] Roll(int count, int sides, Random? rng = null)
    {
        rng ??= Random.Shared;
        return Enumerable.Range(0, count).Select(_ => rng.Next(1, sides + 1)).ToArray();
    }
}
