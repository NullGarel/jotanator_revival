// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-23 @ 16:57 UTC
using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;

namespace Jotanator;

/// <summary>Play tic tac toe against the bot with a 3x3 grid of buttons.</summary>
public sealed class TicTacToeCog : Cog
{
    private static readonly string[] Difficulties = ["easy", "medium", "hard"];
    private readonly ConcurrentDictionary<string, TttGame> _games = new();

    public override IEnumerable<CommandDef> BuildCommands()
    {
        yield return new CommandDef
        {
            Name = "tictactoe",
            Aliases = ["ttt"],
            Description = "Play tic tac toe against the bot",
            Help = "Play tic tac toe against the bot. Difficulty is easy, medium (default) or hard (unbeatable). " +
                   "Example: `j.ttt hard`",
            Params =
            [
                new ParamDef("difficulty", "easy, medium or hard", Required: false,
                    Autocomplete: (bot, current) =>
                        Difficulties.Where(d => d.Contains(current, StringComparison.OrdinalIgnoreCase))),
            ],
            Cooldowns = [new CooldownSpec(1, 5, BucketType.User)],
            Handler = PlayAsync,
        };
    }

    public override Task OnLoadAsync()
    {
        Bot.Client.ButtonExecuted += OnButtonAsync;
        return Task.CompletedTask;
    }

    public override Task OnUnloadAsync()
    {
        Bot.Client.ButtonExecuted -= OnButtonAsync;
        return Task.CompletedTask;
    }

    private async Task PlayAsync(CommandContext ctx)
    {
        var raw = ctx.Args.Str("difficulty")?.Trim().ToLowerInvariant();
        var difficulty = string.IsNullOrEmpty(raw) ? "medium" : raw;
        if (!Difficulties.Contains(difficulty))
            throw new BadArgumentException("Difficulty must be `easy`, `medium` or `hard`.");

        var game = new TttGame(ctx.Author.Id, difficulty, id => _games.TryRemove(id, out _));
        _games[game.Id] = game;

        game.Message = await ctx.SendAsync(game.Content(), components: game.Components(), wantMessage: true);
        game.ResetTimeout();
    }

    private Task OnButtonAsync(SocketMessageComponent component)
    {
        // custom id format: ttt:<gameId>:<cell 0-8 | quit>
        var parts = component.Data.CustomId.Split(':');
        if (parts.Length != 3 || parts[0] != "ttt") return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                if (!_games.TryGetValue(parts[1], out var game))
                {
                    await component.RespondAsync("This game has ended. Start a new one.", ephemeral: true);
                    return;
                }

                await game.HandleAsync(component, parts[2]);
            }
            catch (Exception e)
            {
                Logger.Error("Tic tac toe button failed", e);
            }
        });

        return Task.CompletedTask;
    }
}

/// <summary>State and rules of one game. The human is X and moves first; the bot is O.</summary>
internal sealed class TttGame
{
    private const char Human = 'X';
    private const char Me = 'O';

    private static readonly int[][] Lines =
    [
        [0, 1, 2], [3, 4, 5], [6, 7, 8],   // rows
        [0, 3, 6], [1, 4, 7], [2, 5, 8],   // columns
        [0, 4, 8], [2, 4, 6],              // diagonals
    ];

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public IUserMessage? Message { get; set; }

    private readonly ulong _userId;
    private readonly string _difficulty;
    private readonly Action<string> _onDone;
    private readonly char[] _board = Enumerable.Repeat(' ', 9).ToArray();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _timeout;
    private bool _over;
    private string _result = "";
    private int[]? _winLine;

    public TttGame(ulong userId, string difficulty, Action<string> onDone)
    {
        _userId = userId;
        _difficulty = difficulty;
        _onDone = onDone;
    }

    // ------------------------------------------------------------ rendering

    public string Content() =>
        _over
            ? $"**Tic Tac Toe** ({_difficulty})\n{_result}"
            : $"**Tic Tac Toe** vs me ({_difficulty}): you are ❌, I'm ⭕\nYour turn!";

    public MessageComponent Components()
    {
        var builder = new ComponentBuilder();

        for (var i = 0; i < 9; i++)
        {
            var row = i / 3;
            var id = $"ttt:{Id}:{i}";
            var inWin = _winLine != null && _winLine.Contains(i);

            if (_board[i] == Human)
                builder.WithButton("X", id, inWin ? ButtonStyle.Success : ButtonStyle.Danger, disabled: true, row: row);
            else if (_board[i] == Me)
                builder.WithButton("O", id, inWin ? ButtonStyle.Success : ButtonStyle.Primary, disabled: true, row: row);
            else
                builder.WithButton(customId: id, emote: new Emoji("⬜"), style: ButtonStyle.Secondary,
                    disabled: _over, row: row);
        }

        builder.WithButton("Forfeit", $"ttt:{Id}:quit", ButtonStyle.Secondary, disabled: _over, row: 3);
        return builder.Build();
    }

    // ------------------------------------------------------------ timeout

    /// <summary>(Re)starts the inactivity timer.</summary>
    public void ResetTimeout()
    {
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _timeout, cts)?.Cancel();

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(Config.ViewTimeoutSeconds), cts.Token); }
            catch (OperationCanceledException) { return; }
            await OnTimeoutAsync();
        });
    }

    private void Stop()
    {
        Interlocked.Exchange(ref _timeout, null)?.Cancel();
        _onDone(Id);
    }

    private async Task OnTimeoutAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_over) return;
            _over = true;
            _result = "⏰ Game timed out.";
            _onDone(Id);

            if (Message == null) return;
            try
            {
                await Message.ModifyAsync(m =>
                {
                    m.Content = Content();
                    m.Components = Components();
                });
            }
            catch
            {
                // message deleted or token expired; nothing to do
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // ------------------------------------------------------------ interaction

    public async Task HandleAsync(SocketMessageComponent component, string action)
    {
        // only the player who started the game may click
        if (component.User.Id != _userId)
        {
            await component.RespondAsync($"This isn't your game. Start your own with `{Config.Prefix}ttt`.",
                ephemeral: true);
            return;
        }

        await _gate.WaitAsync();
        try
        {
            if (_over)
            {
                await component.DeferAsync();
                return;
            }

            ResetTimeout();

            if (action == "quit")
            {
                End("You forfeited. 🏳️");
            }
            else if (int.TryParse(action, out var cell) && cell is >= 0 and < 9 && _board[cell] == ' ')
            {
                _board[cell] = Human;
                if (!CheckEnd())
                {
                    _board[PickBotMove()] = Me;
                    CheckEnd();
                }
            }
            else
            {
                await component.DeferAsync();
                return;
            }

            await component.UpdateAsync(m =>
            {
                m.Content = Content();
                m.Components = Components();
            });

            if (_over) Stop();
        }
        finally
        {
            _gate.Release();
        }
    }

    // ------------------------------------------------------------ game rules

    private void End(string result)
    {
        _over = true;
        _result = result;
    }

    /// <summary>Checks for a win or a draw and marks the game as over if so.</summary>
    private bool CheckEnd()
    {
        var (winner, line) = Winner(_board);
        if (winner == Human) { _winLine = line; End("You win! 🎉"); return true; }
        if (winner == Me) { _winLine = line; End("I win! 😎"); return true; }
        if (!_board.Contains(' ')) { End("It's a draw! 🤝"); return true; }
        return false;
    }

    private static (char Who, int[]? Line) Winner(char[] b)
    {
        foreach (var l in Lines)
            if (b[l[0]] != ' ' && b[l[0]] == b[l[1]] && b[l[1]] == b[l[2]])
                return (b[l[0]], l);
        return (' ', null);
    }

    private List<int> EmptyCells() =>
        Enumerable.Range(0, 9).Where(i => _board[i] == ' ').ToList();

    private int PickBotMove()
    {
        var empty = EmptyCells();

        switch (_difficulty)
        {
            case "hard":
                return BestMove(empty);

            case "medium":
                // win if possible, otherwise block, otherwise random
                var move = FindLineMove(Me) ?? FindLineMove(Human);
                if (move != null) return move.Value;
                break;
        }

        return empty[Random.Shared.Next(empty.Count)];
    }

    /// <summary>A cell that completes a line for the given player (two in a row + one empty), if any.</summary>
    private int? FindLineMove(char who)
    {
        foreach (var l in Lines)
        {
            var mine = l.Count(i => _board[i] == who);
            var free = l.Where(i => _board[i] == ' ').ToList();
            if (mine == 2 && free.Count == 1) return free[0];
        }
        return null;
    }

    /// <summary>Minimax: picks randomly among the moves with the best score (unbeatable).</summary>
    private int BestMove(List<int> empty)
    {
        var best = int.MinValue;
        var candidates = new List<int>();

        foreach (var i in empty)
        {
            _board[i] = Me;
            var score = Score(_board, botTurn: false, depth: 1);
            _board[i] = ' ';

            if (score > best) { best = score; candidates.Clear(); }
            if (score == best) candidates.Add(i);
        }

        return candidates[Random.Shared.Next(candidates.Count)];
    }

    private static int Score(char[] b, bool botTurn, int depth)
    {
        var (winner, _) = Winner(b);
        if (winner == Me) return 10 - depth;
        if (winner == Human) return depth - 10;
        if (!b.Contains(' ')) return 0;

        var best = botTurn ? int.MinValue : int.MaxValue;
        for (var i = 0; i < 9; i++)
        {
            if (b[i] != ' ') continue;
            b[i] = botTurn ? Me : Human;
            var score = Score(b, !botTurn, depth + 1);
            b[i] = ' ';
            best = botTurn ? Math.Max(best, score) : Math.Min(best, score);
        }
        return best;
    }
}