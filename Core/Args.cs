// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-20 @ 22:42 UTC
using Discord;

namespace Jotanator;

/// <summary>Parsed command arguments, filled from a prefix message or a slash interaction.</summary>
public sealed class Args
{
    private readonly Dictionary<string, object?> _values = new();

    public void Set(string name, object? value) => _values[name] = value;

    public string? Str(string name) => _values.TryGetValue(name, out var v) ? v as string : null;

    public IGuildUser? Member(string name) => _values.TryGetValue(name, out var v) ? v as IGuildUser : null;
}
