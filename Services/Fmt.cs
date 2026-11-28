// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using System.Globalization;

namespace Jotanator;

/// <summary>Small formatting helpers shared by the economy and the games.</summary>
public static class Fmt
{
    /// <summary>1234 -> "J$1,234".</summary>
    public static string J(long amount) =>
        (amount < 0 ? "-J$" : "J$") + Math.Abs(amount).ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>+50 -> "+J$50", -8 -> "-J$8".</summary>
    public static string Signed(long amount) => amount >= 0 ? "+" + J(amount) : J(amount);

    public static string Duration(TimeSpan t)
    {
        if (t.TotalSeconds < 1) return "a moment";
        var parts = new List<string>();
        if (t.Hours > 0 || t.Days > 0) parts.Add($"{(int)t.TotalHours}h");
        if (t.Minutes > 0 || parts.Count > 0) parts.Add($"{t.Minutes}m");
        if (parts.Count == 0) parts.Add($"{t.Seconds}s");
        return string.Join(" ", parts);
    }

    /// <summary>Tiny text chart: ▁▂▃▄▅▆▇█.</summary>
    public static string Sparkline(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return "";
        const string bars = "▁▂▃▄▅▆▇█";
        var min = values.Min();
        var max = values.Max();
        return string.Concat(values.Select(v =>
            max - min < 1e-9 ? bars[3] : bars[(int)Math.Round((v - min) / (max - min) * (bars.Length - 1))]));
    }
}
