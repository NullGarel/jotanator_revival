// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-20 @ 22:42 UTC
using System.Text.RegularExpressions;
using Discord;

namespace Jotanator;

public class CommandException : Exception
{
    public CommandException(string message = "") : base(message) { }
}

public class CheckFailureException : CommandException
{
    public CheckFailureException(string message = "") : base(message) { }
}

public sealed class NotOwnerException : CheckFailureException
{
    public NotOwnerException() : base("Owner only.") { }
}

public sealed class NoPrivateMessageException : CheckFailureException
{
    public NoPrivateMessageException() : base("This command cannot be used in private messages.") { }
}

public sealed class MissingPermissionsException : CheckFailureException
{
    public MissingPermissionsException(IEnumerable<GuildPermission> missing)
        : base($"You are missing {PermNames.Format(missing)} permission(s) to run this command.") { }
}

public sealed class BotMissingPermissionsException : CheckFailureException
{
    public BotMissingPermissionsException(IEnumerable<GuildPermission> missing)
        : base($"Bot requires {PermNames.Format(missing)} permission(s) to run this command.") { }
}

public sealed class BadArgumentException : CommandException
{
    public BadArgumentException(string message) : base(message) { }
}

public sealed class MissingArgumentException : CommandException
{
    public string ParamName { get; }
    public MissingArgumentException(string paramName)
        : base($"{paramName} is a required argument that is missing.") => ParamName = paramName;
}

public sealed class CooldownException : CommandException
{
    public double RetryAfter { get; }
    public CooldownException(double retryAfter)
        : base($"You are on cooldown. Try again in {retryAfter:F2}s") => RetryAfter = retryAfter;
}

internal static class PermNames
{
    public static string Format(IEnumerable<GuildPermission> perms)
    {
        var names = perms.Select(p => Regex.Replace(p.ToString(), "(?<=[a-z])(?=[A-Z])", " ")).ToList();
        return names.Count > 2
            ? $"{string.Join(", ", names.Take(names.Count - 1))}, and {names[^1]}"
            : string.Join(" and ", names);
    }
}
