// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-28 @ 19:27 UTC
using Discord;
using Discord.WebSocket;

namespace Jotanator;

/// <summary>Minesweeper: pick a column and a row from the menus, then reveal or flag.</summary>
public sealed class MinesweeperCog : GameCog
{
    protected override string Key => "mines";

    public override IEnumerable<CommandDef> BuildCommands()
    {
        yield return new CommandDef
        {
            Name = "minesweeper",
            Aliases = ["mines", "ms"],
            Description = "Play minesweeper with menus and buttons",
            Help = "Pick a column and a row from the menus, then press Reveal (or Flag to mark a mine). " +
                   "Your first reveal is always safe. Clear the board for J$; hitting a mine costs you some.\n" +
                   "Difficulty: easy (6x6, 5 mines), medium (8x8, 10, default) or hard (9x9, 15).",
            Params = [GameParams.Difficulty()],
            Cooldowns = [new CooldownSpec(1, 5, BucketType.User)],
            Handler = PlayAsync,
        };
    }

    private async Task PlayAsync(CommandContext ctx)
    {
        var difficulty = ParseDifficulty(ctx.Args.Str("difficulty"));
        await LaunchAsync(ctx, new MineSession(Bot, ctx.Author, difficulty, Remove));
    }
}

internal sealed class MineSession : GameSession
{
    private static readonly string[] Keycaps = ["0️⃣", "1️⃣", "2️⃣", "3️⃣", "4️⃣", "5️⃣", "6️⃣", "7️⃣", "8️⃣", "9️⃣"];

    private readonly MineBoard _board;
    private readonly string _difficulty;
    private int? _col;
    private int? _row;

    public MineSession(JotaBot bot, IUser player, string difficulty, Action<string> onDone)
        : base(bot, "mines", "Minesweeper", player, null, stake: 0, friendlyStake: false, onDone)
    {
        _difficulty = difficulty;
        _board = difficulty switch
        {
            "easy" => new MineBoard(6, 6, 5),
            "hard" => new MineBoard(9, 9, 15),
            _ => new MineBoard(8, 8, 10),
        };
    }

    protected override long BotWinReward => _difficulty switch { "easy" => 6, "hard" => 20, _ => 12 };

    private string Board()
    {
        var lines = new List<string> { "⬛" + string.Concat(Enumerable.Range(1, _board.Width).Select(i => Keycaps[i])) };

        for (var y = 0; y < _board.Height; y++)
        {
            var cells = new List<string> { Keycaps[y + 1] };
            for (var x = 0; x < _board.Width; x++)
                cells.Add(Cell(x, y));
            lines.Add(string.Concat(cells));
        }

        return string.Join("\n", lines);
    }

    private string Cell(int x, int y)
    {
        if (Over)
        {
            if (_board.IsMine(x, y))
                return _board.ExplodedIndex == _board.Index(x, y) ? "💥" : _board.IsFlagged(x, y) ? "🚩" : "💣";
            if (_board.IsFlagged(x, y)) return "❌";
        }

        if (_board.IsRevealed(x, y))
            return _board.Adjacent(x, y) == 0 ? "⬛" : Keycaps[_board.Adjacent(x, y)];

        return _board.IsFlagged(x, y) ? "🚩" : "⬜";
    }

    protected override string Content()
    {
        var picked = _col != null && _row != null ? $"Selected: column **{_col + 1}**, row **{_row + 1}**" : "Pick a column and a row below.";
        return $"{Header()} ({_difficulty}) • 💣 {_board.Mines} mines • 🚩 {_board.FlagsLeft} left\n{Board()}\n" +
               (Over ? ResultText : picked);
    }

    private SelectMenuBuilder Menu(string action, string placeholder, int count, int? selected)
    {
        var menu = new SelectMenuBuilder()
            .WithCustomId(CId(action))
            .WithPlaceholder(placeholder)
            .WithMinValues(1)
            .WithMaxValues(1);

        for (var i = 0; i < count; i++)
            menu.AddOption(label: $"{placeholder} {i + 1}", value: i.ToString(), isDefault: selected == i);

        return menu;
    }

    protected override MessageComponent Components()
    {
        var builder = new ComponentBuilder();

        if (!Over)
        {
            builder.WithSelectMenu(Menu("col", "Column", _board.Width, _col), row: 0);
            builder.WithSelectMenu(Menu("row", "Row", _board.Height, _row), row: 1);
        }

        builder.WithButton("Reveal", CId("reveal"), ButtonStyle.Success, new Emoji("⛏️"), disabled: Over, row: 2);
        builder.WithButton("Flag", CId("flag"), ButtonStyle.Primary, new Emoji("🚩"), disabled: Over, row: 2);
        builder.WithButton("Quit", CId("quit"), ButtonStyle.Secondary, disabled: Over, row: 2);
        return builder.Build();
    }

    protected override async Task OnActionAsync(SocketMessageComponent c, string action)
    {
        switch (action)
        {
            case "col" or "row":
                var value = int.Parse(c.Data.Values.First());
                if (action == "col") _col = value;
                else _row = value;
                await UpdateAsync(c);
                return;

            case "quit":
                await ForfeitAsync(1, "🏳️ You gave up.", penalize: true);
                await UpdateAsync(c);
                return;

            case "reveal" or "flag":
                if (_col is not int x || _row is not int y)
                {
                    await c.RespondAsync("Pick a column and a row first.", ephemeral: true);
                    return;
                }

                if (action == "flag")
                {
                    _board.ToggleFlag(x, y);
                }
                else
                {
                    switch (_board.Reveal(x, y))
                    {
                        case MineResult.Boom:
                            await FinishAsync(Side.Player2, "💥 **Boom!** You hit a mine.");
                            break;
                        case MineResult.Win:
                            await FinishAsync(Side.Player1, "🏆 **Cleared!** Every safe square is open.");
                            break;
                    }
                }

                _col = null;
                _row = null;
                await UpdateAsync(c);
                return;

            default:
                await c.DeferAsync();
                return;
        }
    }
}
