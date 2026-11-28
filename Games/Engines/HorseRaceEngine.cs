// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
namespace Jotanator;

/// <summary>A five-horse race simulation with odds derived from the same model. No Discord code in here.</summary>
public sealed class HorseRace
{
    public const int Horses = 5;
    public const double FinishLine = 12.0;

    private readonly Random _rng;

    /// <summary>How fast each horse is (hidden from players; they only see the odds).</summary>
    public double[] Strength { get; } = new double[Horses];

    public double[] Position { get; } = new double[Horses];

    public HorseRace(Random? rng = null)
    {
        _rng = rng ?? Random.Shared;
        for (var i = 0; i < Horses; i++) Strength[i] = 0.92 + _rng.NextDouble() * 0.16;
    }

    /// <summary>Advances every horse one frame. True once somebody has crossed the line.</summary>
    public bool Step()
    {
        for (var i = 0; i < Horses; i++)
            Position[i] += Strength[i] * (0.5 + _rng.NextDouble());
        return Position.Any(p => p >= FinishLine);
    }

    /// <summary>Horse indexes from first to last place.</summary>
    public int[] Ranking() => Enumerable.Range(0, Horses).OrderByDescending(i => Position[i]).ToArray();

    /// <summary>Estimated win chance of each horse, by running the same race many times.</summary>
    public double[] WinChances(int trials = 4000)
    {
        var rng = new Random(12345);
        var wins = new int[Horses];
        var pos = new double[Horses];

        for (var t = 0; t < trials; t++)
        {
            Array.Clear(pos);
            while (!pos.Any(p => p >= FinishLine))
                for (var i = 0; i < Horses; i++)
                    pos[i] += Strength[i] * (0.5 + rng.NextDouble());

            wins[Array.IndexOf(pos, pos.Max())]++;
        }

        return wins.Select(w => w / (double)trials).ToArray();
    }

    /// <summary>Payout multiplier per horse (stake included) with a ~8% house edge, from 1.1x to 25x.</summary>
    public double[] Odds()
    {
        var chances = WinChances();
        return chances.Select(p => Math.Round(Math.Clamp(0.92 / Math.Max(p, 0.001), 1.1, 25.0), 1)).ToArray();
    }
}
