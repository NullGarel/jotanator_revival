// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
namespace Jotanator;

public enum CrapsStatus
{
    /// <summary>The come-out roll set a point; keep rolling.</summary>
    PointSet,
    /// <summary>7 or 11 on the come-out.</summary>
    Natural,
    /// <summary>2 or 3 on the come-out.</summary>
    Craps,
    /// <summary>12 on the come-out (pass loses, don't pass pushes).</summary>
    Twelve,
    /// <summary>The point came up again.</summary>
    PointHit,
    /// <summary>A 7 before the point.</summary>
    SevenOut,
}

/// <summary>One round of craps: a come-out roll, then rolling until the point or a 7. No Discord code in here.</summary>
public sealed class CrapsRound
{
    public int? Point { get; private set; }

    public bool Finished { get; private set; }

    public CrapsStatus Roll(int d1, int d2)
    {
        var sum = d1 + d2;

        if (Point is null)
        {
            switch (sum)
            {
                case 7 or 11: Finished = true; return CrapsStatus.Natural;
                case 2 or 3: Finished = true; return CrapsStatus.Craps;
                case 12: Finished = true; return CrapsStatus.Twelve;
                default: Point = sum; return CrapsStatus.PointSet;
            }
        }

        if (sum == Point) { Finished = true; return CrapsStatus.PointHit; }
        if (sum == 7) { Finished = true; return CrapsStatus.SevenOut; }
        return CrapsStatus.PointSet;
    }

    /// <summary>Result for the pass-line bettor (the shooter) after a finishing roll.</summary>
    public static Outcome PassResult(CrapsStatus s) => s switch
    {
        CrapsStatus.Natural or CrapsStatus.PointHit => Outcome.Win,
        _ => Outcome.Loss,
    };

    /// <summary>Result for the don't-pass bettor (the fader) after a finishing roll.</summary>
    public static Outcome DontPassResult(CrapsStatus s) => s switch
    {
        CrapsStatus.Craps or CrapsStatus.SevenOut => Outcome.Win,
        CrapsStatus.Twelve => Outcome.Draw,
        _ => Outcome.Loss,
    };
}
