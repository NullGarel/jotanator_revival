// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using Discord;

namespace Jotanator;

/// <summary>Parsed command arguments, filled from a prefix message or a slash interaction.</summary>
public sealed class Args
{
    private readonly Dictionary<string, object?> _values = new();

    public void Set(string name, object? value) => _values[name] = value;

    public string? Str(string name) => _values.TryGetValue(name, out var v) ? v as string : null;

    public IGuildUser? Member(string name) => _values.TryGetValue(name, out var v) ? v as IGuildUser : null;

    public IUser? User(string name) => _values.TryGetValue(name, out var v) ? v as IUser : null;

    public long? Int(string name) => _values.TryGetValue(name, out var v) && v is long n ? n : null;

    public IAttachment? Attachment(string name) => _values.TryGetValue(name, out var v) ? v as IAttachment : null;
}
