// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
namespace Jotanator;

public enum KnifeSpeed { Slow, Medium, Fast }

/// <summary>Five finger fillet maths: faster stabs are riskier but multiply your winnings more. No Discord code in here.</summary>
public static class Fillet
{
    public const int MaxStabs = 8;

    /// <summary>Chance of not cutting yourself.</summary>
    public static double SuccessChance(KnifeSpeed s) => s switch
    {
        KnifeSpeed.Slow => 0.85,
        KnifeSpeed.Medium => 0.70,
        _ => 0.50,
    };

    /// <summary>How much the multiplier grows after a successful stab.</summary>
    public static double Gain(KnifeSpeed s) => s switch
    {
        KnifeSpeed.Slow => 0.10,
        KnifeSpeed.Medium => 0.30,
        _ => 0.80,
    };

    public static bool Stab(KnifeSpeed s, Random? rng = null) => (rng ?? Random.Shared).NextDouble() < SuccessChance(s);

    public static double Next(double multiplier, KnifeSpeed s) => multiplier * (1 + Gain(s));
}
