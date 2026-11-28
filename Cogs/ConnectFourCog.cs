// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using Discord;
using Discord.WebSocket;

namespace Jotanator;

/// <summary>Connect Four against the bot (easy / medium / hard) or against another member.</summary>
public sealed class ConnectFourCog : GameCog
{
    protected override string Key => "c4";

    public override IEnumerable<CommandDef> BuildCommands()
    {
        yield return new CommandDef
        {
            Name = "connect4",
            Aliases = ["c4", "connectfour"],
            Description = "Play Connect Four against the bot or another member",
            Help = "Drop pieces into a 7x6 grid with the number buttons and connect four in a row. Mention a member to " +
                   "challenge them (a small J$ wager goes to the winner); leave it out to play me. " +
                   "Difficulty: easy, medium (default) or hard.\nExamples: `j.c4`, `j.c4 hard`, `j.c4 @friend`",
            Params = [GameParams.Opponent(), GameParams.Difficulty()],
            Cooldowns = [new CooldownSpec(1, 5, BucketType.User)],
            Handler = PlayAsync,
        };
    }

    private async Task PlayAsync(CommandContext ctx)
    {
        var opponent = ResolveOpponent(ctx, ctx.Args.User("opponent"));
        var difficulty = ParseDifficulty(ctx.Args.Str("difficulty"));
        await LaunchAsync(ctx, new C4Session(Bot, ctx.Author, opponent, difficulty, Remove));
    }
}

internal sealed class C4Session : GameSession
{
    private static readonly string[] Digits = ["1️⃣", "2️⃣", "3️⃣", "4️⃣", "5️⃣", "6️⃣", "7️⃣"];

    private readonly C4Board _board = new();
    private readonly string _difficulty;
    private int _turn = 1;
    private int[]? _winLine;

    public C4Session(JotaBot bot, IUser p1, IUser? p2, string difficulty, Action<string> onDone)
        : base(bot, "c4", "Connect Four", p1, p2, stake: 0, friendlyStake: true, onDone)
    {
        _difficulty = difficulty;
    }

    protected override int TurnPlayer => _turn;

    protected override long BotWinReward => _difficulty switch { "easy" => 5, "hard" => 20, _ => 10 };

    private string Board()
    {
        var lines = new List<string> { string.Concat(Digits) };
        for (var row = 0; row < C4Board.Rows; row++)
        {
            var cells = new List<string>();
            for (var col = 0; col < C4Board.Cols; col++)
            {
                var i = row * C4Board.Cols + col;
                cells.Add(_winLine != null && _winLine.Contains(i) ? "🟩"
                    : _board.Cells[i] == 1 ? "🔴"
                    : _board.Cells[i] == 2 ? "🟡"
                    : "⚪");
            }
            lines.Add(string.Concat(cells));
        }
        return string.Join("\n", lines);
    }

    protected override string Content()
    {
        var head = $"{Header()}{(VsBot ? $" ({_difficulty})" : "")}\n🔴 {PlayerLabel(1)}  vs  🟡 {PlayerLabel(2)}\n\n{Board()}\n";
        return head + (Over ? ResultText : $"{(_turn == 1 ? "🔴" : "🟡")} {PlayerLabel(_turn)}, it's your turn!");
    }

    protected override MessageComponent Components()
    {
        var builder = new ComponentBuilder();

        for (var col = 0; col < C4Board.Cols; col++)
        {
            // 4 buttons on the first row, 3 + Forfeit on the second
            builder.WithButton((col + 1).ToString(), CId(col.ToString()), ButtonStyle.Primary,
                disabled: Over || !_board.CanDrop(col), row: col < 4 ? 0 : 1);
        }

        builder.WithButton("Forfeit", CId("quit"), ButtonStyle.Secondary, disabled: Over, row: 1);
        return builder.Build();
    }

    protected override async Task OnActionAsync(SocketMessageComponent c, string action)
    {
        if (action == "quit")
        {
            await ForfeitAsync(PlayerNumber(c.User.Id), "🏳️ Forfeited.", penalize: true);
            await UpdateAsync(c);
            return;
        }

        if (!await RequireTurnAsync(c, _turn)) return;

        if (!int.TryParse(action, out var col) || !_board.CanDrop(col))
        {
            await c.DeferAsync();
            return;
        }

        await DropAsync(col);

        if (!Over && VsBot)
            await DropAsync(_board.PickMove(2, _difficulty));

        await UpdateAsync(c);
    }

    private async Task DropAsync(int col)
    {
        var row = _board.Drop(col, _turn);
        var line = _board.WinLineAt(row, col);

        if (line != null)
        {
            _winLine = line;
            await FinishAsync(_turn == 1 ? Side.Player1 : Side.Player2, $"🏆 {PlayerLabel(_turn)} connects four!");
        }
        else if (_board.IsFull)
        {
            await FinishAsync(Side.Draw, "🤝 The board is full: it's a draw!");
        }
        else
        {
            _turn = 3 - _turn;
        }
    }
}
