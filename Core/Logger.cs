// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-20 @ 22:42 UTC
using Discord;

namespace Jotanator;

/// <summary>Console logger using the same line format as the Python bot's bot.log.</summary>
public static class Logger
{
    private static readonly object Gate = new();

    public static void Info(string message, string name = "jotanator") => Write("INFO", name, message);
    public static void Warn(string message, string name = "jotanator") => Write("WARNING", name, message);

    public static void Error(string message, Exception? ex = null, string name = "jotanator") =>
        Write("ERROR", name, ex == null ? message : $"{message}\n{ex}");

    /// <summary>Hook for DiscordSocketClient.Log.</summary>
    public static Task FromDiscord(LogMessage m)
    {
        var level = m.Severity switch
        {
            LogSeverity.Critical => "CRITICAL",
            LogSeverity.Error => "ERROR",
            LogSeverity.Warning => "WARNING",
            LogSeverity.Info => "INFO",
            _ => "DEBUG",
        };
        var text = m.Exception != null ? $"{m.Message}\n{m.Exception}" : m.Message ?? "";
        Write(level, $"discord.{m.Source.ToLowerInvariant()}", text);
        return Task.CompletedTask;
    }

    private static void Write(string level, string name, string message)
    {
        lock (Gate)
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level,-8}] {name}: {message}");
    }
}
