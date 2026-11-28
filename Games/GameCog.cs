// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;

namespace Jotanator;

/// <summary>Parameters shared by many game commands.</summary>
public static class GameParams
{
    public static readonly string[] Levels = ["easy", "medium", "hard"];

    /// <summary>Challenge a member; leave empty to play the bot. Names aren't accepted so "hard" can't be mistaken for a user.</summary>
    public static ParamDef Opponent() =>
        new("opponent", "Challenge this member (leave empty to play against the bot)", ParamKind.User, Required: false, ByName: false);

    public static ParamDef Difficulty() =>
        new("difficulty", "easy, medium (default) or hard", Required: false,
            Autocomplete: (_, current) => Levels.Where(l => l.Contains(current, StringComparison.OrdinalIgnoreCase)));

    public static ParamDef Bet() =>
        new("bet", "How much J$ to bet (default 10)", ParamKind.Int, Required: false, Min: 1, Max: 1_000_000_000);
}

/// <summary>Base for a cog that runs button games: keeps the live sessions and routes clicks to them.</summary>
public abstract class GameCog : Cog
{
    private readonly ConcurrentDictionary<string, GameSession> _sessions = new();

    /// <summary>The custom-id prefix of this cog's games (same as the sessions' Key).</summary>
    protected abstract string Key { get; }

    public override string Category => "Games";

    protected void Remove(string sessionId) => _sessions.TryRemove(sessionId, out _);

    public override Task OnLoadAsync()
    {
        Bot.Client.ButtonExecuted += OnComponentAsync;
        Bot.Client.SelectMenuExecuted += OnComponentAsync;
        return Task.CompletedTask;
    }

    public override Task OnUnloadAsync()
    {
        Bot.Client.ButtonExecuted -= OnComponentAsync;
        Bot.Client.SelectMenuExecuted -= OnComponentAsync;
        return Task.CompletedTask;
    }

    /// <summary>Registers the session and posts its first message.</summary>
    protected async Task LaunchAsync(CommandContext ctx, GameSession session)
    {
        _sessions[session.Id] = session;
        try
        {
            await session.StartAsync(ctx);
        }
        catch
        {
            _sessions.TryRemove(session.Id, out _);
            throw;
        }
    }

    /// <summary>The opponent to play against, or null for the bot (no opponent given, or the bot itself was picked).</summary>
    protected IUser? ResolveOpponent(CommandContext ctx, IUser? requested)
    {
        if (requested == null || requested.Id == Bot.Client.CurrentUser.Id) return null;
        if (requested.Id == ctx.Author.Id) throw new BadArgumentException("You can't challenge yourself.");
        if (requested.IsBot) throw new BadArgumentException("Other bots don't play games. Challenge a person, or leave the opponent out to play me.");
        return requested;
    }

    protected static string ParseDifficulty(string? raw)
    {
        var level = string.IsNullOrWhiteSpace(raw) ? "medium" : raw.Trim().ToLowerInvariant();
        if (!GameParams.Levels.Contains(level))
            throw new BadArgumentException("Difficulty must be `easy`, `medium` or `hard`.");
        return level;
    }

    private Task OnComponentAsync(SocketMessageComponent component)
    {
        // custom id format: <key>:<sessionId>:<action>
        var parts = component.Data.CustomId.Split(':');
        if (parts.Length != 3 || parts[0] != Key) return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                if (!_sessions.TryGetValue(parts[1], out var session))
                {
                    await component.RespondAsync("This game has ended. Start a new one.", ephemeral: true);
                    return;
                }

                await session.HandleAsync(component, parts[2]);
            }
            catch (Exception e)
            {
                Logger.Error($"{Key} interaction failed", e);
            }
        });

        return Task.CompletedTask;
    }
}
