// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using Discord;
using Discord.WebSocket;

namespace Jotanator;

/// <summary>Tic tac toe against the bot (easy / medium / hard) or against another member.</summary>
public sealed class TicTacToeCog : GameCog
{
    protected override string Key => "ttt";

    public override IEnumerable<CommandDef> BuildCommands()
    {
        yield return new CommandDef
        {
            Name = "tictactoe",
            Aliases = ["ttt"],
            Description = "Play tic tac toe against the bot or another member",
            Help = "Play tic tac toe on a 3x3 button grid. Mention a member to challenge them (a small J$ wager " +
                   "goes to the winner); leave it out to play me. Difficulty: easy, medium (default) or hard (unbeatable).\n" +
                   "Examples: `j.ttt`, `j.ttt hard`, `j.ttt @friend`",
            Params = [GameParams.Opponent(), GameParams.Difficulty()],
            Cooldowns = [new CooldownSpec(1, 5, BucketType.User)],
            Handler = PlayAsync,
        };
    }

    private async Task PlayAsync(CommandContext ctx)
    {
        var opponent = ResolveOpponent(ctx, ctx.Args.User("opponent"));
        var difficulty = ParseDifficulty(ctx.Args.Str("difficulty"));
        await LaunchAsync(ctx, new TttSession(Bot, ctx.Author, opponent, difficulty, Remove));
    }
}

internal sealed class TttSession : GameSession
{
    private readonly TttBoard _board = new();
    private readonly string _difficulty;
    private int _turn = 1;
    private int[]? _winLine;

    public TttSession(JotaBot bot, IUser p1, IUser? p2, string difficulty, Action<string> onDone)
        : base(bot, "ttt", "Tic Tac Toe", p1, p2, stake: 0, friendlyStake: true, onDone)
    {
        _difficulty = difficulty;
    }

    protected override int TurnPlayer => _turn;

    protected override long BotWinReward => _difficulty switch { "easy" => 4, "hard" => 16, _ => 8 };

    protected override string Content()
    {
        var head = $"{Header()}{(VsBot ? $" ({_difficulty})" : "")}\n❌ {PlayerLabel(1)}  vs  ⭕ {PlayerLabel(2)}";
        return Over ? $"{head}\n{ResultText}" : $"{head}\n{PlayerLabel(_turn)}, it's your turn!";
    }

    protected override MessageComponent Components()
    {
        var builder = new ComponentBuilder();

        for (var i = 0; i < 9; i++)
        {
            var row = i / 3;
            var id = CId(i.ToString());
            var inWin = _winLine != null && _winLine.Contains(i);

            if (_board.Cells[i] == TttBoard.X)
                builder.WithButton("X", id, inWin ? ButtonStyle.Success : ButtonStyle.Danger, disabled: true, row: row);
            else if (_board.Cells[i] == TttBoard.O)
                builder.WithButton("O", id, inWin ? ButtonStyle.Success : ButtonStyle.Primary, disabled: true, row: row);
            else
                builder.WithButton(customId: id, emote: new Emoji("⬜"), style: ButtonStyle.Secondary, disabled: Over, row: row);
        }

        builder.WithButton("Forfeit", CId("quit"), ButtonStyle.Secondary, disabled: Over, row: 3);
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

        if (!int.TryParse(action, out var cell) || cell is < 0 or >= 9 || _board.Cells[cell] != ' ')
        {
            await c.DeferAsync();
            return;
        }

        await PlaceAsync(cell);

        if (!Over && VsBot)
            await PlaceAsync(_board.PickMove(TttBoard.O, _difficulty));

        await UpdateAsync(c);
    }

    /// <summary>Places the current player's mark, then ends the game or passes the turn.</summary>
    private async Task PlaceAsync(int cell)
    {
        _board.Cells[cell] = _turn == 1 ? TttBoard.X : TttBoard.O;

        var (winner, line) = _board.Winner();
        if (winner != ' ')
        {
            _winLine = line;
            var side = winner == TttBoard.X ? Side.Player1 : Side.Player2;
            await FinishAsync(side, $"🏆 {PlayerLabel(winner == TttBoard.X ? 1 : 2)} wins!");
        }
        else if (_board.IsFull)
        {
            await FinishAsync(Side.Draw, "🤝 It's a draw!");
        }
        else
        {
            _turn = 3 - _turn;
        }
    }
}
