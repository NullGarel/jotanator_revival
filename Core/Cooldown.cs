// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-20 @ 22:42 UTC
using System.Diagnostics;

namespace Jotanator;

public enum BucketType { User, Guild }

public sealed record CooldownSpec(int Rate, double PerSeconds, BucketType Bucket);

/// <summary>Same token-window algorithm as discord.py's Cooldown.</summary>
internal sealed class CooldownBucket
{
    private readonly int _rate;
    private readonly double _per;
    private int _tokens;
    private double _window;
    private readonly object _gate = new();

    public CooldownBucket(int rate, double per)
    {
        _rate = rate;
        _per = per;
        _tokens = rate;
    }

    public static double Now() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    /// <summary>Returns null if the use is allowed, otherwise seconds until retry.</summary>
    public double? Update(double now)
    {
        lock (_gate)
        {
            if (now > _window + _per) _tokens = _rate;
            if (_tokens == 0) return _per - (now - _window);
            if (_tokens == _rate) _window = now;
            _tokens--;
            return null;
        }
    }
}
